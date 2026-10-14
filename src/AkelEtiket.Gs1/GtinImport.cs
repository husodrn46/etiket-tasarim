using AkelEtiket.Core.Data;

namespace AkelEtiket.Gs1;

/// <summary>Excel/CSV'den GTIN aktarma sonucu.</summary>
public sealed class GtinImportResult
{
    public List<RegisteredGtin> Added { get; } = [];

    /// <summary>Atlanan satırlar: "Satır 5: kontrol basamağı hatalı …".</summary>
    public List<string> Skipped { get; } = [];

    /// <summary>Kullanılan sütunlar (bilgi için).</summary>
    public string ColumnSummary { get; set; } = "";
}

/// <summary>
/// Tablo satırlarından kayıtlı GTIN listesi oluşturur. Sütunlar adına göre bulunur (Türkçe karakter ve
/// büyük/küçük harf duyarsız): GTIN/Barkod/EAN, ürün kodu, açıklama/ürün adı. Hatalı satırlar satır numarasıyla
/// bildirilir, diğerlerini engellemez. Listede zaten olan GTIN'ler tekrar eklenmez.
/// </summary>
public static class GtinImport
{
    private static readonly string[] GtinColumns = ["GTIN", "Barkod", "EAN", "EAN13", "Barcode", "BarkodNo"];
    private static readonly string[] CodeColumns = ["UrunKodu", "StokKodu", "Kod", "ProductCode", "MalzemeKodu"];
    private static readonly string[] DescriptionColumns = ["UrunAdi", "Aciklama", "Ad", "Urun", "Description", "StokAdi"];

    /// <param name="columns">Sütun adları.</param>
    /// <param name="rows">(dosyadaki satır numarası, değerler) çiftleri.</param>
    /// <param name="existing">Profilde zaten kayıtlı GTIN'ler (tekrar eklenmez).</param>
    public static GtinImportResult FromRows(IReadOnlyList<string> columns, IEnumerable<(int RowNumber, IReadOnlyList<string> Values)> rows,
        IEnumerable<RegisteredGtin> existing)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        var result = new GtinImportResult();
        int gtinIndex = FindColumn(columns, GtinColumns);
        if (gtinIndex < 0)
        {
            result.Skipped.Add("GTIN sütunu bulunamadı. Sütun adı GTIN, Barkod veya EAN olmalı.");
            return result;
        }
        int codeIndex = FindColumn(columns, CodeColumns);
        int descIndex = FindColumn(columns, DescriptionColumns);
        result.ColumnSummary = "GTIN: " + columns[gtinIndex] +
                               (codeIndex >= 0 ? ", ürün kodu: " + columns[codeIndex] : "") +
                               (descIndex >= 0 ? ", açıklama: " + columns[descIndex] : "");

        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in existing ?? [])
        {
            if (Gs1Identifiers.ToGtin14(item.Gtin) is { } key) known.Add(key);
        }

        foreach (var (rowNumber, values) in rows)
        {
            string raw = Get(values, gtinIndex);
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var check = Gs1Identifiers.ValidateGtin(raw);
            if (!check.IsValid)
            {
                result.Skipped.Add($"Satır {rowNumber}: {check.Error}");
                continue;
            }
            string key = check.Normalized.PadLeft(14, '0');
            if (!known.Add(key))
            {
                result.Skipped.Add($"Satır {rowNumber}: {check.Normalized} zaten listede.");
                continue;
            }
            result.Added.Add(new RegisteredGtin
            {
                Gtin = check.Normalized,
                ProductCode = Get(values, codeIndex).Trim(),
                Description = Get(values, descIndex).Trim(),
                PackagingLevel = check.Type == Gs1KeyType.Gtin14 && check.Normalized[0] != '0' ? PackagingLevel.Carton : PackagingLevel.ConsumerUnit,
            });
        }
        return result;
    }

    private static int FindColumn(IReadOnlyList<string> columns, IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            int index = FieldNames.FindIndex(columns, candidate);
            if (index >= 0) return index;
        }
        return -1;
    }

    private static string Get(IReadOnlyList<string> values, int index) =>
        index >= 0 && index < values.Count ? values[index] ?? "" : "";
}
