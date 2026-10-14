using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;

namespace AkelEtiket.Gs1;

/// <summary>
/// GS1 yerleşik alanları: <c>{SSCC}</c>, <c>{GLN}</c>, <c>{GCP}</c>.
/// <list type="bullet">
/// <item><b>Önizleme</b> (tasarım, önizleme, PNG, toplu önizleme): SSCC yerine numara AYIRMADAN
/// <see cref="PreviewSscc"/> (18 sıfır, seri 0 — hiçbir zaman gerçek numara olarak verilmez) gösterilir.</item>
/// <item><b>Baskı</b>: her etiket için baskı anında <see cref="SsccRegistry"/>'den ayrılan numara kullanılır
/// (<see cref="ForAllocated"/>).</item>
/// </list>
/// GLN ve firma öneki profilden okunur (profil yalnızca bu alanlar istenince yüklenir).
/// </summary>
public sealed class Gs1FieldSource : IFieldValueSource
{
    /// <summary>Önizleme SSCC'si: 18 sıfır (kontrol basamağı 0, geçerli biçim). Seri 0 asla ayrılmaz.</summary>
    public const string PreviewSscc = "000000000000000000";

    public static readonly IReadOnlyList<string> FieldNamesList = ["SSCC", "GLN", "GCP"];

    private readonly Func<Gs1CompanyProfile?>? _profile;

    private Gs1FieldSource(string sscc, bool isPreview, Func<Gs1CompanyProfile?>? profile)
    {
        Sscc = sscc;
        IsPreview = isPreview;
        _profile = profile;
    }

    /// <summary>Önizleme kaynağı: numara ayırmaz.</summary>
    public static Gs1FieldSource Preview(Func<Gs1CompanyProfile?>? profile = null) => new(PreviewSscc, true, profile);

    /// <summary>Baskı kaynağı: verilen (ayrılmış) SSCC'yi kullanır.</summary>
    public static Gs1FieldSource ForAllocated(string sscc, Func<Gs1CompanyProfile?>? profile = null)
    {
        var check = Gs1Identifiers.ValidateSscc(sscc);
        if (!check.IsValid) throw new ArgumentException($"Geçersiz SSCC: {check.Error}", nameof(sscc));
        return new Gs1FieldSource(check.Normalized, false, profile);
    }

    /// <summary>Bu kaynağın verdiği SSCC (önizlemede <see cref="PreviewSscc"/>).</summary>
    public string Sscc { get; }

    /// <summary>Önizleme kaynağı mı (numara ayrılmamış)?</summary>
    public bool IsPreview { get; }

    public bool TryGetValue(string name, string? format, out string value)
    {
        switch (FieldNames.Normalize(name))
        {
            case "sscc":
                value = Sscc;
                return true;
            case "gln":
                value = Gs1Identifiers.Normalize(_profile?.Invoke()?.Gln);
                return true;
            case "gcp":
            case "firmaoneki":
                value = _profile?.Invoke()?.NormalizedGcp ?? "";
                return true;
            default:
                value = "";
                return false;
        }
    }

    /// <summary>Alan adı bir GS1 yerleşik alanı mı? (SSCC, GLN, GCP/FirmaOneki)</summary>
    public static bool IsGs1Field(string name) => FieldNames.Normalize(name) is "sscc" or "gln" or "gcp" or "firmaoneki";

    /// <summary>Alan adı SSCC mi?</summary>
    public static bool IsSsccField(string name) => FieldNames.Normalize(name) == "sscc";

    /// <summary>Belgede (görünür olsun olmasın) {SSCC} alanı kullanılıyor mu?</summary>
    public static bool DocumentUsesSscc(LabelDocument document) => GetDocumentFieldNames(document).Any(IsSsccField);

    /// <summary>Belgede herhangi bir GS1 yerleşik alanı kullanılıyor mu?</summary>
    public static bool DocumentUsesGs1Fields(LabelDocument document) => GetDocumentFieldNames(document).Any(IsGs1Field);

    private static IEnumerable<string> GetDocumentFieldNames(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Elements.SelectMany(e => e switch
        {
            TextElement t => FieldTemplate.GetFieldNames(t.Text),
            BarcodeElement b => FieldTemplate.GetFieldNames(b.Content),
            _ => [],
        });
    }
}
