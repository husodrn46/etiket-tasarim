using System.Text;
using AkelEtiket.Core.Data;
using AkelEtiket.DataImport;

namespace AkelEtiket.Editor.Services;

/// <summary>Reusable schema and parsing options; no rows, credentials or file paths.</summary>
public sealed class ImportMappingProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public Dictionary<string,string> Mappings { get; set; } = new(StringComparer.Ordinal);
    public char? Delimiter { get; set; }
    public string? EncodingName { get; set; }
    public string? SheetName { get; set; }
    public int? HeaderRow { get; set; }
    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 100) Id = Guid.NewGuid().ToString("N");
        Name = (Name ?? "").Trim(); if (Name.Length > 100) Name = Name[..100];
        Mappings = (Mappings ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Key) && !string.IsNullOrWhiteSpace(p.Value))
            .Take(500).ToDictionary(p => p.Key,p => p.Value,StringComparer.Ordinal);
        if (Delimiter is '\r' or '\n' or '\0' or '"') Delimiter = null;
        if (HeaderRow is <= 0 or > 1048576) HeaderRow = null;
        if (EncodingName?.Length > 100) EncodingName = null;
        if (SheetName?.Length > 100) SheetName = null;
    }
    public ImportOptions ToImportOptions()
    {
        Encoding? encoding = ImportChoices.ResolveEncoding(EncodingName);
        return new() { Delimiter = Delimiter, Encoding = encoding, SheetName = SheetName, HeaderRow = HeaderRow is > 0 ? HeaderRow : null };
    }
    public override string ToString() => Name;
}

public static class ColumnSuggestions
{
    private static readonly string[][] Groups =
    [
        ["urunadi", "productname", "itemname", "name"], ["urunkodu", "productcode", "itemcode", "sku", "kod"],
        ["barkod", "barcode", "ean", "ean13", "gtin", "upc"], ["adet", "quantity", "qty", "copies", "miktar", "count", "koliiciadet"],
        ["fiyat", "price", "unitprice"], ["adres", "address", "deliveryaddress"], ["alici", "recipient", "customername"],
        ["parti", "batch", "lot", "lotnumber"], ["takipno", "trackingnumber", "trackingcode"],
    ];
    public static string? Suggest(string field, IReadOnlyList<string> columns)
    {
        var exact = columns.Where(c => c == field).ToList(); if (exact.Count == 1) return exact[0];
        string normalized = FieldNames.Normalize(field);
        var folded = columns.Where(c => FieldNames.Normalize(c) == normalized).ToList();
        if (folded.Count > 0) return folded.Count == 1 ? folded[0] : null;
        var group = Groups.FirstOrDefault(g => g.Contains(normalized)); if (group is null) return null;
        var matches = columns.Where(c => group.Contains(FieldNames.Normalize(c))).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}
