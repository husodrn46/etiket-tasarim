using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Geometry;
using AkelEtiket.DataImport;
using AkelEtiket.Rendering;

namespace AkelEtiket.Design.Inspection;

/// <summary>Doğrulama sonucu.</summary>
public sealed record ValidationReport(bool Ok, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, IReadOnlyList<string> Checked)
{
    public JsonObject ToJson() => new()
    {
        ["ok"] = Ok,
        ["errors"] = new JsonArray(Errors.Select(e => (JsonNode)e).ToArray()),
        ["warnings"] = new JsonArray(Warnings.Select(e => (JsonNode)e).ToArray()),
        ["checked"] = new JsonArray(Checked.Select(e => (JsonNode)e).ToArray()),
    };
}

/// <summary>
/// Barkod ve baskı kurallarını doğrular. Etiket, baskıdaki gibi (baskı kipinde, yazıcı çözünürlüğünde) üretilir;
/// barkodlar birleşik görüntüden kesilip bağımsız çözücüyle okunur (üstüne binen öğeler de yakalanır). Veri kaynağı
/// bağlıysa satırlar tek tek denenir; hatalı satırlar numarasıyla bildirilir.
/// </summary>
public static class DesignValidator
{
    /// <summary>XP-470B en geniş baskı alanı (mm).</summary>
    public const double MaxPrintWidthMm = 108;

    public static ValidationReport Validate(LabelDocument doc, AssetStore assets, IFieldValueSource? previewFields, DataSourceTable? data,
        IImageSourceResolver? images, bool verifyBarcodes, int maxRows, double dotsPerMm = 8.0)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var checkedItems = new List<string>();

        if (doc.Label.WidthMm > MaxPrintWidthMm)
            errors.Add($"Etiket genişliği {doc.Label.WidthMm:0.#} mm; XP-470B en fazla {MaxPrintWidthMm} mm basar.");
        checkedItems.Add($"Etiket ölçüsü {doc.Label.WidthMm:0.#} × {doc.Label.HeightMm:0.#} mm");

        var label = new RectMm(0, 0, doc.Label.WidthMm, doc.Label.HeightMm);
        foreach (var element in doc.Elements.Where(e => e.Visible))
        {
            var b = ElementGeometry.GetRotatedBoundsMm(element);
            if (b.X < -0.05 || b.Y < -0.05 || b.Right > label.Width + 0.05 || b.Bottom > label.Height + 0.05)
                errors.Add($"\"{element.DisplayName}\" ({element.Id}) etiket sınırları dışına taşıyor.");
            if (element is ImageElement image && image.SourceField is null && !assets.Contains(image.AssetId))
                errors.Add($"\"{element.DisplayName}\" görseli tasarımda bulunamadı.");
        }

        bool usesData = doc.HasFieldPlaceholders;
        var items = new List<(string Label, IFieldValueSource? Fields)>();
        var builtIn = new BuiltInFieldSource(1, DateTime.Now);
        if (usesData && data is not null && data.Rows.Count > 0)
        {
            foreach (var row in data.Rows.Take(Math.Clamp(maxRows, 1, 500)))
                items.Add(($"Satır {row.RowNumber}", new CompositeFieldSource(row.ToFieldSource(), builtIn)));
            checkedItems.Add($"Veri kaynağından {items.Count} satır ({data.FileName})");
            if (data.Rows.Count > items.Count) warnings.Add($"Yalnızca ilk {items.Count} satır denetlendi (toplam {data.Rows.Count}).");
            foreach (var issue in data.Issues.Where(i => i.Severity == ImportIssueSeverity.Error).Take(20))
                errors.Add($"Veri: {issue.FullText}");
        }
        else
        {
            items.Add((usesData ? "Önizleme verisi" : "Etiket", previewFields is null ? builtIn : new CompositeFieldSource(previewFields, builtIn)));
            if (usesData && previewFields is null)
                warnings.Add("Tasarım veri alanı içeriyor ama veri kaynağı/ürün bağlı değil; alanlar baskıda veriyle dolmalı.");
        }

        using var producer = new LabelProducer(doc, assets, verifyBarcodes, images);
        int failedRows = 0;
        foreach (var (name, fields) in items)
        {
            var item = new PrintItem(1, null, 1, fields ?? builtIn);
            var (_, problems, rowWarnings) = producer.Produce(item, dotsPerMm);
            if (problems.Count > 0)
            {
                failedRows++;
                if (failedRows <= 25) errors.Add($"{name}: {string.Join(" | ", problems)}");
            }
            foreach (string w in rowWarnings.Take(3))
            {
                string text = $"{name}: {w}";
                if (!warnings.Contains(text) && warnings.Count < 40) warnings.Add(text);
            }
        }
        if (failedRows > 25) errors.Add($"… toplam {failedRows} satırda hata var.");
        checkedItems.Add(verifyBarcodes ? "Barkodlar baskı görüntüsünden okunarak doğrulandı" : "Barkod içerikleri doğrulandı (okuma testi yapılmadı)");
        if (doc.Elements.OfType<TextElement>().Any(t => !FontProvider.Default.IsInstalled(t.FontFamily)))
            warnings.Add("Bazı yazı tipleri bu bilgisayarda yüklü değil; Arial kullanılacak.");
        return new ValidationReport(errors.Count == 0, errors, warnings, checkedItems);
    }
}
