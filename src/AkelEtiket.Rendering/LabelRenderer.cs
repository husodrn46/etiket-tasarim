using AkelEtiket.Core;
using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Geometry;
using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;
using SkiaSharp;

namespace AkelEtiket.Rendering;

/// <summary>
/// ORTAK RENDER MOTORU. Tasarım modelini (mm) yazıcı çözünürlüğünde 1-bit katmanlara çevirir.
/// Ekrandaki tuval ve baskı aynı katmanları kullanır; bu yüzden tuvalde görülen siyah-beyaz sonuç
/// baskıyla birebir tutarlıdır. İş parçacığı güvenlidir (arka planda toplu baskı render'ı yapılabilir).
/// </summary>
public sealed class LabelRenderer : IDisposable
{
    public LabelRenderer(AssetStore assets, FontProvider? fonts = null)
    {
        Assets = assets ?? throw new ArgumentNullException(nameof(assets));
        Fonts = fonts ?? FontProvider.Default;
        Images = new ImageCache(assets);
    }

    public AssetStore Assets { get; }
    public FontProvider Fonts { get; }
    public ImageCache Images { get; }

    /// <summary>Etiketin nokta cinsinden boyutu.</summary>
    public static (int Width, int Height) GetLabelSizeDots(LabelSetup setup, double dotsPerMm) =>
        (Math.Max(1, Units.MmToDotsRounded(setup.WidthMm, dotsPerMm)),
         Math.Max(1, Units.MmToDotsRounded(setup.HeightMm, dotsPerMm)));

    /// <summary>Tüm etiketi z-sırasıyla birleştirir.</summary>
    public LabelRenderResult RenderLabel(LabelDocument document, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        var (width, height) = GetLabelSizeDots(document.Label, options.DotsPerMm);
        var bitmap = new MonoBitmap(width, height);
        var issues = new List<RenderIssue>();
        var results = new List<ElementRenderResult>();
        var labelBounds = new RectMm(0, 0, document.Label.WidthMm, document.Label.HeightMm);

        foreach (var element in document.Elements.ToList())
        {
            if (!element.Visible) continue;
            var result = RenderElement(element, options);
            results.Add(result);
            issues.AddRange(result.Issues);
            if (result.HiddenByCondition) continue;
            result.Layer?.CompositeOnto(bitmap);

            var bounds = ElementGeometry.GetRotatedBoundsMm(element);
            if (!labelBounds.IntersectsWith(bounds))
                issues.Add(Issue(element, RenderIssueSeverity.Warning, "Öğe tamamen etiket dışında; basılmaz."));
            else if (bounds.X < -0.05 || bounds.Y < -0.05 || bounds.Right > labelBounds.Right + 0.05 || bounds.Bottom > labelBounds.Bottom + 0.05)
                issues.Add(Issue(element, element is BarcodeElement ? RenderIssueSeverity.Error : RenderIssueSeverity.Warning,
                    element is BarcodeElement
                        ? "Barkod etiket kenarından taşıyor; kesilen barkod okunamaz."
                        : "Öğe etiket kenarından taşıyor; taşan kısım basılmaz."));
        }
        return new LabelRenderResult { Bitmap = bitmap, DotsPerMm = options.DotsPerMm, Issues = issues, Elements = results };
    }

    /// <summary>Tek öğeyi çizer. Katman, öğenin etiket üzerindeki konumuna yerleştirilmiş olarak döner.</summary>
    public ElementRenderResult RenderElement(LabelElement element, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(element);
        RenderIssue? conditionNote = null;
        if (element.VisibleWhen is { } condition)
        {
            string field = FieldTemplate.MakePlaceholder(condition.Field);
            switch (VisibilityEvaluator.Evaluate(condition, options.Fields))
            {
                case ConditionResult.Hide:
                    return new ElementRenderResult
                    {
                        ElementId = element.Id,
                        HiddenByCondition = true,
                        Issues = [Issue(element, RenderIssueSeverity.Info, $"Koşul sağlanmadı (yalnızca {condition.Describe()} basılır); bu etikette görünmez.")],
                    };
                case ConditionResult.FieldMissing when options.Mode == RenderMode.Print:
                    return new ElementRenderResult
                    {
                        ElementId = element.Id,
                        HiddenByCondition = true,
                        Issues = [Issue(element, RenderIssueSeverity.Error, $"Koşul alanı {field} veride yok; koşul değerlendirilemedi.")],
                    };
                case ConditionResult.FieldMissing:
                    conditionNote = Issue(element, options.Fields is null ? RenderIssueSeverity.Info : RenderIssueSeverity.Warning,
                        options.Fields is null
                            ? $"Koşul: yalnızca {condition.Describe()} basılır (veri yüklenince değerlendirilir)."
                            : $"Koşul alanı {field} veride yok; tasarımda gösteriliyor.");
                    break;
            }
        }
        var box = ElementGeometry.GetDotBox(element, options.DotsPerMm);
        try
        {
            var result = element switch
            {
                TextElement text => RenderText(text, box, options),
                BarcodeElement barcode => RenderBarcode(barcode, box, options),
                ImageElement image => RenderImage(image, box, options),
                ShapeElement shape => RenderVector(element, box, ShapeElementRenderer.Prepare(shape, box.Width, box.Height, options.DotsPerMm), []),
                _ => new ElementRenderResult { ElementId = element.Id },
            };
            if (conditionNote is not null) result.AppendIssue(conditionNote);
            return result;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new ElementRenderResult
            {
                ElementId = element.Id,
                Issues = [Issue(element, RenderIssueSeverity.Error, $"Çizim hatası: {ex.Message}")],
            };
        }
    }

    private ElementRenderResult RenderText(TextElement element, DotRect box, RenderOptions options)
    {
        var resolution = FieldTemplate.Resolve(element.Text, options.Fields);
        var issues = new List<RenderIssue>();
        AddFieldIssues(element, resolution, options, issues);
        var (draw, info, problem, fontWarning) = TextElementRenderer.Prepare(element, resolution.Text, box.Width, box.Height, options.DotsPerMm, Fonts);
        if (problem is not null) issues.Add(Issue(element, RenderIssueSeverity.Warning, problem));
        if (fontWarning is not null) issues.Add(Issue(element, RenderIssueSeverity.Warning, fontWarning));
        var result = RenderVector(element, box, draw, issues);
        return new ElementRenderResult
        {
            ElementId = element.Id,
            Layer = result.Layer,
            LocalOffsetX = result.LocalOffsetX,
            LocalOffsetY = result.LocalOffsetY,
            Issues = issues,
            ResolvedContent = resolution.Text,
            IsPlaceholder = !resolution.IsComplete,
            Text = info,
        };
    }

    private ElementRenderResult RenderBarcode(BarcodeElement element, DotRect box, RenderOptions options)
    {
        var resolution = FieldTemplate.Resolve(element.Content, options.Fields);
        var issues = new List<RenderIssue>();
        int quarterTurns = Math.Max(0, ElementGeometry.GetQuarterTurns(element.Rotation));

        if (!resolution.IsComplete)
        {
            AddFieldIssues(element, resolution, options, issues);
            string label = resolution.MissingFields.Count > 0
                ? string.Join(" ", resolution.MissingFields.Select(FieldTemplate.MakePlaceholder))
                : "Hatalı alan";
            var placeholder = RenderVector(element, box, PlaceholderRenderer.Prepare(label, box.Width, box.Height, Fonts, cross: false), issues);
            return new ElementRenderResult
            {
                ElementId = element.Id,
                Layer = placeholder.Layer,
                LocalOffsetX = placeholder.LocalOffsetX,
                LocalOffsetY = placeholder.LocalOffsetY,
                Issues = issues,
                IsPlaceholder = true,
                ResolvedContent = resolution.Text,
            };
        }

        var output = BarcodeElementRenderer.Render(element, resolution.Text, box.Width, box.Height, options.DotsPerMm, Fonts);
        if (output.Error is not null)
        {
            issues.Add(Issue(element, RenderIssueSeverity.Error, output.Error));
            var errorBox = RenderVector(element, box, PlaceholderRenderer.Prepare("Barkod hatası", box.Width, box.Height, Fonts, cross: true), issues);
            return new ElementRenderResult
            {
                ElementId = element.Id,
                Layer = errorBox.Layer,
                LocalOffsetX = errorBox.LocalOffsetX,
                LocalOffsetY = errorBox.LocalOffsetY,
                Issues = issues,
                ResolvedContent = resolution.Text,
                Barcode = output.Info,
            };
        }
        foreach (var warning in output.Warnings) issues.Add(Issue(element, RenderIssueSeverity.Warning, warning));
        foreach (var note in output.Info.Validation.Notes) issues.Add(Issue(element, RenderIssueSeverity.Info, note));

        var local = RasterHelpers.RotateQuarter(output.Ink, null, box.Width, box.Height, quarterTurns);
        return new ElementRenderResult
        {
            ElementId = element.Id,
            Layer = new MonoLayer(local.Ink, local.Mask, box.X + local.OffsetX, box.Y + local.OffsetY),
            LocalOffsetX = local.OffsetX,
            LocalOffsetY = local.OffsetY,
            Issues = issues,
            ResolvedContent = resolution.Text,
            Barcode = output.Info,
        };
    }

    private ElementRenderResult RenderImage(ImageElement element, DotRect box, RenderOptions options)
    {
        var issues = new List<RenderIssue>();
        int quarterTurns = ElementGeometry.GetQuarterTurns(element.Rotation);
        ImageAsset? rowAsset = null;
        string? resolvedValue = null;
        if (element.SourceField is { } field)
        {
            var resolution = FieldTemplate.Resolve(FieldTemplate.MakePlaceholder(field), options.Fields);
            if (resolution.IsComplete && !string.IsNullOrWhiteSpace(resolution.Text))
            {
                resolvedValue = resolution.Text.Trim();
                if (options.ImageResolver is null)
                {
                    issues.Add(Issue(element, RenderIssueSeverity.Error, $"\"{resolvedValue}\" görseli için görsel klasörü tanımlı değil."));
                }
                else
                {
                    rowAsset = options.ImageResolver.Resolve(resolvedValue, out string? resolveError);
                    if (rowAsset is null)
                        issues.Add(Issue(element, RenderIssueSeverity.Error, resolveError ?? $"\"{resolvedValue}\" görseli bulunamadı."));
                }
            }
            else if (options.Mode == RenderMode.Print)
            {
                issues.Add(Issue(element, RenderIssueSeverity.Error, $"{FieldTemplate.MakePlaceholder(field)} görsel alanı boş."));
            }
            if (issues.Count > 0)
            {
                string label = resolvedValue ?? FieldTemplate.MakePlaceholder(field);
                var missing = RenderVector(element, box, PlaceholderRenderer.Prepare(label, box.Width, box.Height, Fonts, cross: true), issues);
                return new ElementRenderResult
                {
                    ElementId = element.Id,
                    Layer = missing.Layer,
                    LocalOffsetX = missing.LocalOffsetX,
                    LocalOffsetY = missing.LocalOffsetY,
                    Issues = issues,
                    IsPlaceholder = true,
                    ResolvedContent = resolvedValue,
                };
            }
        }
        var output = ImageElementRenderer.Render(element, box.Width, box.Height, element.Rotation, quarterTurns, Images, rowAsset);
        if (output.Error is not null || output.Layer is null)
        {
            issues.Add(Issue(element, RenderIssueSeverity.Error, output.Error ?? "Görsel çizilemedi."));
            var placeholder = RenderVector(element, box, PlaceholderRenderer.Prepare("Görsel yok", box.Width, box.Height, Fonts, cross: true), issues);
            return new ElementRenderResult
            {
                ElementId = element.Id,
                Layer = placeholder.Layer,
                LocalOffsetX = placeholder.LocalOffsetX,
                LocalOffsetY = placeholder.LocalOffsetY,
                Issues = issues,
                IsPlaceholder = true,
            };
        }
        var layer = output.Layer.Value;
        return new ElementRenderResult
        {
            ElementId = element.Id,
            Layer = layer.Mask is { } m && m.CountBlack() == 0 ? null : new MonoLayer(layer.Ink, layer.Mask, box.X + layer.OffsetX, box.Y + layer.OffsetY),
            LocalOffsetX = layer.OffsetX,
            LocalOffsetY = layer.OffsetY,
            Issues = issues,
            Image = output.Info,
            ResolvedContent = resolvedValue,
        };
    }

    /// <summary>Vektör içerik (metin, şekil, yer tutucu): yerel çiz → eşikle → döndür.</summary>
    private static ElementRenderResult RenderVector(LabelElement element, DotRect box, Action<SKCanvas> draw, List<RenderIssue> issues)
    {
        int quarterTurns = ElementGeometry.GetQuarterTurns(element.Rotation);
        LocalLayer local;
        if (quarterTurns >= 0)
        {
            using var bitmap = RasterHelpers.CreateSurfaceBitmap(box.Width, box.Height);
            using (var canvas = new SKCanvas(bitmap)) draw(canvas);
            var (ink, mask) = RasterHelpers.ThresholdRgba(bitmap);
            local = RasterHelpers.RotateQuarter(ink, mask, box.Width, box.Height, quarterTurns);
        }
        else
        {
            var frame = RasterHelpers.GetRotatedFrame(box.Width, box.Height, element.Rotation);
            using var bitmap = RasterHelpers.CreateSurfaceBitmap(frame.Width, frame.Height);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.SetMatrix(frame.Transform);
                draw(canvas);
            }
            var (ink, mask) = RasterHelpers.ThresholdRgba(bitmap);
            local = new LocalLayer(ink, mask, frame.OffsetX, frame.OffsetY);
        }

        bool empty = local.Mask is not null && local.Mask.CountBlack() == 0;
        return new ElementRenderResult
        {
            ElementId = element.Id,
            Layer = empty ? null : new MonoLayer(local.Ink, local.Mask, box.X + local.OffsetX, box.Y + local.OffsetY),
            LocalOffsetX = local.OffsetX,
            LocalOffsetY = local.OffsetY,
            Issues = issues,
        };
    }

    private static void AddFieldIssues(LabelElement element, FieldResolution resolution, RenderOptions options, List<RenderIssue> issues)
    {
        if (resolution.IsComplete) return;
        if (resolution.MissingFields.Count > 0)
        {
            string fields = string.Join(", ", resolution.MissingFields.Select(FieldTemplate.MakePlaceholder));
            if (options.Mode == RenderMode.Print)
                issues.Add(Issue(element, RenderIssueSeverity.Error, $"Değişken alan doldurulamadı: {fields}. Veri kaynağında bu sütun yok."));
            else if (options.Fields is null)
                issues.Add(Issue(element, RenderIssueSeverity.Info, $"Değişken alan: {fields} (veri kaynağı yüklenince doldurulur)."));
            else
                issues.Add(Issue(element, RenderIssueSeverity.Warning, $"Veri kaynağında olmayan alan: {fields}."));
        }
        if (resolution.Problems.Count > 0)
        {
            issues.Add(Issue(element, options.Mode == RenderMode.Print ? RenderIssueSeverity.Error : RenderIssueSeverity.Warning,
                string.Join(" ", resolution.Problems)));
        }
    }

    private static RenderIssue Issue(LabelElement element, RenderIssueSeverity severity, string message) =>
        new(element.Id, element.DisplayName, severity, message);

    public void Dispose() => Images.Dispose();
}
