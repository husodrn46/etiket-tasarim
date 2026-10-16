using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Geometry;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;
using AkelEtiket.Editor.Printing;
using AkelEtiket.Gs1;
using AkelEtiket.Rendering;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.Production.Quality;

/// <summary>
/// Bounded background inspection of every supplied print item at the actual target resolution.
/// This never prints, changes a document, allocates a number or samples a job silently.
/// Software barcode decoding does not certify a physical printer or scanner.
/// </summary>
public sealed partial class PreflightService
{
    private readonly PreflightLimits _limits;

    public PreflightService(PreflightLimits? limits = null)
    {
        _limits = limits ?? new PreflightLimits();
        _limits.Validate();
    }

    // Do not pass the token to Task.Run: a pre-cancelled job still returns an honest partial report.
    public Task<PreflightReport> AnalyzeAsync(PreflightRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Project);
        ArgumentNullException.ThrowIfNull(request.Items);
        return Task.Run(() => Analyze(request, cancellationToken));
    }

    private PreflightReport Analyze(PreflightRequest request, CancellationToken cancellationToken)
    {
        var findings = new Findings(_limits.MaxIssues);
        int checkedRows = 0;
        long checkedLabels = 0, totalLabels = 0;
        bool complete = true, cancelled = false;
        int? currentRow = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var item in request.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item is not null && item.Copies > 0) totalLabels = checked(totalLabels + item.Copies);
            }

            if (!double.IsFinite(request.DotsPerMm) || request.DotsPerMm is < 1 or > 48)
                throw new LimitException("invalid_resolution", L10n.T("Baskı çözünürlüğü 1–48 nokta/mm arasında olmalıdır."));
            if (request.Items.Count == 0)
            {
                findings.Add(new(PreflightSeverity.Error, "no_items", L10n.T("Denetlenecek etiket yok. Önce veri satırlarını ve adetleri seçin.")));
                return Report();
            }
            if (request.SkippedRowCount < 0)
            {
                findings.Add(new(PreflightSeverity.Error, "invalid_skipped_count", L10n.T("Atlanan satır sayısı negatif olamaz.")));
                return Report();
            }
            if (request.SkippedRowCount > 0)
            {
                complete = false;
                findings.Add(new(PreflightSeverity.Error, "rows_skipped",
                    L10n.T($"Seçili {request.SkippedRowCount} satır baskı planına alınamadı; satır hatalarını düzeltin veya bu satırların seçimini kaldırın.")));
            }

            var original = request.Project.Document;
            if (original.Elements.Count > _limits.MaxElements)
                throw new LimitException("element_limit", L10n.T($"Tasarımda {_limits.MaxElements} öğe sınırı aşıldı; analiz tamamlanmadı."));
            if (CounterFields.DocumentUsesCounters(original) || Gs1FieldSource.DocumentUsesSscc(original))
            {
                findings.Add(new(PreflightSeverity.Error, "reserve_numbering_requires_print",
                    L10n.T("Bu tasarım ayrılan sayaç veya SSCC numarası kullanıyor. Üretim çıktısı için mevcut Yazdır akışını kullanın."),
                    Recommendation: L10n.T("Önizleme numaralarını üretim etiketi olarak kullanmayın; numaralar baskı sırasında ayrılır.")));
                complete = false;
                return Report();
            }

            // Even callers who already supply a snapshot retain ownership of their document and bytes.
            var document = ModelJson.Clone(original);
            ValidateContentSizes(document);
            var assets = SnapshotAssets(request.Project.Assets, document);
            long rowPixelWork = ValidateRenderSize(document, request.DotsPerMm);
            var items = request.Items.Take(_limits.MaxRows).ToArray();
            long pixelWork = 0;
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                currentRow = item?.RowNumber;
                if (item is null || item.Fields is null || item.Copies < 1)
                {
                    findings.Add(new(PreflightSeverity.Error, "invalid_print_item",
                        L10n.T("Etiket satırının veri kaynağı veya kopya adedi geçersiz."), currentRow));
                    checkedRows++;
                    continue;
                }
                if (checkedLabels + item.Copies > _limits.MaxLabels)
                    throw new LimitException("label_limit", L10n.T($"{_limits.MaxLabels} etiket sınırına ulaşıldı; kalan etiketler denetlenmedi."));
                if (rowPixelWork > _limits.MaxTotalPixelWork - pixelWork)
                    throw new LimitException("pixel_work_limit", L10n.T($"{_limits.MaxTotalPixelWork} piksel işlem sınırına ulaşıldı; kalan satırlar denetlenmedi."));
                pixelWork += rowPixelWork;
                var fieldCaches = new List<CachedFields>();
                var fields = CacheFields(item.Fields, _limits.MaxTextLength, fieldCaches);
                var rowItem = item with { Fields = fields };
                var images = new CachedImages(request.Images, _limits.MaxAssetBytes);
                try
                {
                    CheckRow(document, assets, rowItem, request.DotsPerMm, images, findings, cancellationToken);
                    if (fieldCaches.Any(c => c.LimitReached) || images.LimitReached)
                        throw new LimitException("row_content_limit", L10n.T("Satırın metin veya görsel bellek sınırı aşıldı; analiz tamamlanmadı."));
                    checkedRows++;
                    checkedLabels += item.Copies;
                }
                catch (Exception ex) when (ex is not (OperationCanceledException or LimitException or OutOfMemoryException))
                {
                    complete = false;
                    findings.Add(new(PreflightSeverity.Error, "row_analysis_failed",
                        L10n.T($"Bu satırın denetimi tamamlanamadı: {ex.Message}"), currentRow,
                        Recommendation: L10n.T("Veri ve görsel kaynaklarını kontrol edip denetimi yeniden çalıştırın.")));
                }
            }
            if (request.Items.Count > items.Length)
                throw new LimitException("row_limit", L10n.T($"{_limits.MaxRows} satır sınırına ulaşıldı; kalan satırlar denetlenmedi."));
            if (complete && checkedRows == request.Items.Count)
                findings.Add(new(PreflightSeverity.Info, "software_check_only",
                    L10n.T("Denetim yazılımdaki baskı görüntüsünü kapsar; fiziksel yazıcı ve tarayıcı testinin yerini tutmaz.")));
        }
        catch (OperationCanceledException)
        {
            complete = false;
            cancelled = true;
            findings.AddFinal(new(PreflightSeverity.Info, "analysis_cancelled",
                L10n.T("Denetim iptal edildi; yalnızca tamamlanan satırlar sayıldı."), currentRow));
        }
        catch (LimitException ex)
        {
            complete = false;
            findings.AddFinal(new(PreflightSeverity.Error, ex.Code, ex.Message, currentRow,
                Recommendation: L10n.T("İşi daha küçük parçalara bölün veya tasarımı sadeleştirip yeniden denetleyin.")));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            complete = false;
            findings.AddFinal(new(PreflightSeverity.Error, "analysis_failed",
                L10n.T($"Denetim tamamlanamadı: {ex.Message}"), currentRow));
        }
        return Report();

        PreflightReport Report() => new()
        {
            Issues = Array.AsReadOnly(findings.Items.ToArray()),
            RowsChecked = checkedRows,
            LabelsChecked = checkedLabels,
            TotalRows = request.Items.Count,
            TotalLabels = totalLabels,
            IsComplete = complete,
            IsCancelled = cancelled,
        };
    }

    private void ValidateContentSizes(LabelDocument document)
    {
        foreach (var element in document.Elements)
        {
            string? content = element switch { TextElement t => t.Text, BarcodeElement b => b.Content, _ => null };
            if (content?.Length > _limits.MaxTextLength)
                throw new LimitException("text_limit", L10n.T($"Bir öğe {_limits.MaxTextLength} karakter sınırını aşıyor; analiz tamamlanmadı."));
        }
    }

    private AssetStore SnapshotAssets(AssetStore source, LabelDocument document)
    {
        var assets = new AssetStore();
        long size = 0;
        foreach (string id in document.GetReferencedAssetIds())
        {
            if (!source.TryGet(id, out var asset)) continue;
            size = checked(size + asset.Size);
            if (size > _limits.MaxAssetBytes)
                throw new LimitException("asset_limit", L10n.T($"Görseller {_limits.MaxAssetBytes} bayt bellek sınırını aşıyor; analiz tamamlanmadı."));
            assets.AddExisting(new ImageAsset(asset.Id, asset.FileName, asset.Data.ToArray()));
        }
        return assets;
    }

    private long ValidateRenderSize(LabelDocument document, double dotsPerMm)
    {
        static long Pixels(double width, double height, double resolution) =>
            checked((long)Math.Ceiling(width * resolution + 2) * (long)Math.Ceiling(height * resolution + 2));
        long pixels = Pixels(document.Label.WidthMm, document.Label.HeightMm, dotsPerMm);
        if (pixels > _limits.MaxPixelsPerLabel)
            throw new LimitException("label_pixel_limit", L10n.T($"Etiket görüntüsü {_limits.MaxPixelsPerLabel} piksel sınırını aşıyor; analiz tamamlanmadı."));
        foreach (var element in document.Elements.Where(e => e.Visible))
        {
            var rotated = ElementGeometry.GetRotatedBoundsMm(element);
            long local = Math.Max(Pixels(element.Width, element.Height, dotsPerMm), Pixels(rotated.Width, rotated.Height, dotsPerMm));
            if (local > _limits.MaxPixelsPerElement)
                throw new LimitException("element_pixel_limit", L10n.T($"Bir öğe {_limits.MaxPixelsPerElement} piksel sınırını aşıyor; analiz tamamlanmadı."));
            pixels = checked(pixels + local);
        }
        if (pixels > _limits.MaxPixelsPerRow)
            throw new LimitException("row_pixel_limit", L10n.T($"Satır görüntüleri {_limits.MaxPixelsPerRow} piksel bellek sınırını aşıyor; analiz tamamlanmadı."));
        return document.Elements.OfType<BarcodeElement>().Any(b => b.Visible) ? checked(pixels * 2) : pixels;
    }

    private void CheckRow(LabelDocument document, AssetStore assets, PrintItem item, double dotsPerMm,
        CachedImages images, Findings findings, CancellationToken cancellationToken)
    {
        LabelRenderResult rendered;
        using (var renderer = new LabelRenderer(assets))
            rendered = renderer.RenderLabel(document, new RenderOptions
            {
                DotsPerMm = dotsPerMm, Fields = item.Fields, Mode = RenderMode.Print, ImageResolver = images,
            });
        cancellationToken.ThrowIfCancellationRequested();
        var elements = document.Elements.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var results = rendered.Elements.ToDictionary(e => e.ElementId, StringComparer.Ordinal);
        foreach (var issue in rendered.Issues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            elements.TryGetValue(issue.ElementId, out var element);
            results.TryGetValue(issue.ElementId, out var result);
            string code = Classify(issue, element, result);
            findings.Add(new(ToSeverity(issue.Severity), code, issue.Message, item.RowNumber,
                issue.ElementId, issue.ElementName, Recommendation(code)));
        }
        foreach (var result in rendered.Elements.Where(r => !r.HiddenByCondition))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!elements.TryGetValue(result.ElementId, out var element)) continue;
            CheckEdge(document, element, item.RowNumber, findings);
            if (element is TextElement text)
            {
                var fields = document.Formatting is { } formatting ? new RegionalFieldSource(item.Fields, formatting) : item.Fields;
                foreach (var token in FieldTemplate.GetTokens(text.Text))
                {
                    var resolved = FieldTemplate.Resolve(text.Text.Substring(token.Start, token.Length), fields);
                    if (resolved.IsComplete && string.IsNullOrWhiteSpace(resolved.Text))
                        findings.Add(new(PreflightSeverity.Warning, "empty_field",
                            L10n.T($"{FieldTemplate.MakePlaceholder(token.Name)} alanı boş; bu etikette boş basılır."),
                            item.RowNumber, element.Id, element.DisplayName,
                            L10n.T("Alan zorunluysa veriyi tamamlayın; isteğe bağlıysa uyarıyı değerlendirin.")));
                }
            }
            if (element is BarcodeElement { ShowText: true } barcode && result.Barcode is { IsValid: true } &&
                !FontProvider.Default.Get(barcode.TextFontFamily, false, false).IsExactFamily)
                findings.Add(new(PreflightSeverity.Warning, "font_substituted",
                    L10n.T($"Barkodun insan tarafından okunan metninde \"{barcode.TextFontFamily}\" yazı tipi yerine yedek yazı tipi kullanıldı."),
                    item.RowNumber, element.Id, element.DisplayName, Recommendation("font_substituted")));
        }
        if (rendered.Bitmap.CountBlack() == 0)
            findings.Add(new(PreflightSeverity.Warning, "blank_label", L10n.T("Bu etiketin baskı görüntüsü tamamen boş."), item.RowNumber,
                Recommendation: L10n.T("Görünür öğeleri, veri koşullarını ve beyaz renkli nesneleri kontrol edin.")));

        // The producer is the authoritative existing production path. Its decoder sees the composited
        // image, so an opaque element covering a barcode is caught, unlike isolated symbol validation.
        if (!rendered.Elements.Any(e => !e.HiddenByCondition && e.Barcode is { IsValid: true })) return;
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> problems = [];
        if (!rendered.HasErrors)
        {
            using (var producer = new LabelProducer(document, assets, verifyBarcodes: true, images))
                (_, problems, _) = producer.Produce(item, dotsPerMm);
            cancellationToken.ThrowIfCancellationRequested();
            if (problems.Count == 0) return;
        }

        // Producer's legacy API returns only strings. On failure inspect the already rendered regions
        // to attach precise element IDs without parsing localized text or customer element names.
        // With unrelated rendering errors the producer intentionally skips decoding, so inspect
        // independently valid symbols here too instead of claiming that they were checked.
        bool located = false;
        foreach (var result in rendered.Elements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (result.HiddenByCondition || result.Barcode is not { IsValid: true } info || result.Layer is not { } layer ||
                elements[result.ElementId] is not BarcodeElement barcode) continue;
            var region = rendered.Bitmap.Crop(layer.OffsetX, layer.OffsetY, layer.Width, layer.Height);
            var verification = BarcodeVerifier.Verify(region, barcode.Symbology, info.VerificationText);
            if (verification.Success) continue;
            located = true;
            findings.Add(new(PreflightSeverity.Error, "barcode_unreadable",
                L10n.T($"Barkod baskı görüntüsünden doğrulanamadı: {verification.Message}"), item.RowNumber,
                barcode.Id, barcode.DisplayName, Recommendation("barcode_unreadable")));
        }
        if (!located)
            foreach (string problem in problems)
                findings.Add(new(PreflightSeverity.Error, "production_error", problem, item.RowNumber));
    }

    private void CheckEdge(LabelDocument document, LabelElement element, int? row, Findings findings)
    {
        var bounds = ElementGeometry.GetRotatedBoundsMm(element);
        double nearest = Math.Min(Math.Min(bounds.X, bounds.Y), Math.Min(document.Label.WidthMm - bounds.Right, document.Label.HeightMm - bounds.Bottom));
        if (nearest >= 0 && nearest < _limits.EdgeWarningMm)
            findings.Add(new(PreflightSeverity.Warning, "near_label_edge",
                L10n.T($"Öğe etiket kenarına {_limits.EdgeWarningMm:0.##} mm'den yakın; yazıcı hizalama payını kontrol edin."),
                row, element.Id, element.DisplayName, L10n.T("Gerekirse öğeyi içeri alın ve hedef yazıcıda kalibrasyon etiketi deneyin.")));
    }

    private static string Classify(RenderIssue issue, LabelElement? element, ElementRenderResult? result)
    {
        // Exact messages from the same resource-backed renderer, never language-dependent substring
        // parsing. The stable codes remain identical after switching UI language.
        if (issue.Message == L10n.T("Öğe tamamen etiket dışında; basılmaz.")) return "element_outside_label";
        if (issue.Message == L10n.T("Barkod etiket kenarından taşıyor; kesilen barkod okunamaz.")) return "barcode_clipped";
        if (issue.Message == L10n.T("Öğe etiket kenarından taşıyor; taşan kısım basılmaz.")) return "element_clipped";
        if (issue.Message == L10n.T("Metin kutuya sığmıyor; taşan kısım basılmaz. Kutuyu büyütün veya \"Kutuya sığdır\" seçeneğini açın.")) return "text_overflow";
        if (result?.HiddenByCondition == true) return issue.Severity == RenderIssueSeverity.Error ? "condition_field_missing" : "condition_hidden";
        if (issue.Severity == RenderIssueSeverity.Error)
        {
            if (element is ImageElement) return "image_missing_or_invalid";
            if (result?.IsPlaceholder == true) return "missing_field";
            if (result?.Barcode is { IsValid: false } barcode) return barcode.Validation.IsValid ? "barcode_too_small" : "barcode_invalid";
            return "render_error";
        }
        if (result?.Text is { FontSubstituted: true }) return "font_substituted";
        return issue.Severity == RenderIssueSeverity.Warning ? "render_warning" : "render_info";
    }

    private static PreflightSeverity ToSeverity(RenderIssueSeverity severity) => severity switch
    {
        RenderIssueSeverity.Error => PreflightSeverity.Error,
        RenderIssueSeverity.Warning => PreflightSeverity.Warning,
        _ => PreflightSeverity.Info,
    };

    private static string? Recommendation(string code) => code switch
    {
        "text_overflow" => L10n.T("Metin kutusunu büyütün, yazıyı küçültün veya kutuya sığdırmayı açın."),
        "font_substituted" => L10n.T("Hedef bilgisayarda bulunan bir yazı tipi seçin veya gerekli yazı tipini kurun."),
        "barcode_unreadable" or "barcode_too_small" => L10n.T("Barkodu büyütün, sessiz bölge bırakın ve üstüne binen öğeleri kaldırın."),
        "barcode_clipped" or "element_clipped" or "element_outside_label" => L10n.T("Öğeyi döndürülmüş sınırlarıyla birlikte etiketin içine taşıyın."),
        "missing_field" or "condition_field_missing" => L10n.T("Veri sütun adını ve alan eşleştirmesini kontrol edin."),
        "image_missing_or_invalid" => L10n.T("Gömülü görseli veya veri satırının görsel klasörü bağlantısını kontrol edin."),
        _ => null,
    };

    private sealed class Findings(int limit)
    {
        public List<PreflightIssue> Items { get; } = [];
        public void Add(PreflightIssue issue)
        {
            if (Items.Count >= limit)
                throw new LimitException("issue_limit", L10n.T($"{limit} bulgu sınırına ulaşıldı; rapor ve analiz tamamlanmadı."));
            Items.Add(issue);
        }
        public void AddFinal(PreflightIssue issue) => Items.Add(issue);
    }

    private sealed class LimitException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
