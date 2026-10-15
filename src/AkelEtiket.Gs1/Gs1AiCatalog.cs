using System.Globalization;
using AkelEtiket.BarcodeEngine;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Gs1;

/// <summary>Arayüzde gösterilecek GS1 Uygulama Tanımlayıcısı (AI) bilgisi.</summary>
/// <param name="Ai">Tanımlayıcı (ör. "00", "3103").</param>
/// <param name="Title">Kısa ad (barkod motorundaki tablo).</param>
/// <param name="Description">Türkçe açıklama ve kullanım ipucu.</param>
/// <param name="Format">GS1 biçim gösterimi (ör. N2+N18, N2+X..20).</param>
/// <param name="NeedsSeparator">Değişken uzunluklu: arkasından başka AI gelirse FNC1 ayırıcı gerekir.</param>
/// <param name="HasCheckDigit">Değerin son hanesi kontrol basamağıdır.</param>
/// <param name="IsDate">Değer YYAAGG tarihidir.</param>
/// <param name="IsCommon">Koli/palet etiketlerinde sık kullanılan AI.</param>
public sealed record Gs1AiInfo(
    string Ai,
    string Title,
    string Description,
    string Format,
    bool NeedsSeparator,
    bool HasCheckDigit,
    bool IsDate,
    bool IsCommon)
{
    /// <summary>Etikette görünen biçim: "(00)".</summary>
    public string Display => $"({Ai})";
}

/// <summary>
/// GS1 AI listesi: tanımlar ve doğrulama kuralları barkod motorundaki tablodan (<see cref="BarcodeEngine.Gs1"/>) gelir;
/// burada yalnızca Türkçe açıklamalar eklenir. Doğrulama mantığı tekrarlanmaz.
/// </summary>
public static class Gs1AiCatalog
{
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        ["00"] = L10n.T("Seri Nakliye Konteyner Kodu. Her koli/palete bir kez verilir; aynı numara iki kez kullanılmaz. {SSCC} alanı baskı anında ayrılır."),
        ["01"] = L10n.T("Ticari ürünün GTIN'i (14 hane; EAN-13'ün başına 0 eklenir). Koli kendisi bir ticari ürünse kullanılır."),
        ["02"] = L10n.T("Lojistik birim içindeki ürünlerin GTIN'i (14 hane). (00) ile birlikte ve (37) adetle kullanılır; (01) ile birlikte kullanılmaz."),
        ["10"] = L10n.T("Parti/lot numarası (en fazla 20 karakter, Türkçe harf ve boşluk olmaz)."),
        ["11"] = L10n.T("Üretim tarihi, YYAAGG (ör. 261013)."),
        ["12"] = L10n.T("Vade tarihi, YYAAGG."),
        ["13"] = L10n.T("Ambalajlama tarihi, YYAAGG."),
        ["15"] = L10n.T("Tavsiye edilen tüketim tarihi, YYAAGG (gün 00 = ayın sonu)."),
        ["16"] = L10n.T("Satış son tarihi, YYAAGG."),
        ["17"] = L10n.T("Son kullanma tarihi, YYAAGG (gün 00 = ayın sonu)."),
        ["20"] = L10n.T("Ürün varyantı (2 hane)."),
        ["21"] = L10n.T("Seri numarası (en fazla 20 karakter)."),
        ["30"] = L10n.T("Değişken adet (tek ürünlerde)."),
        ["37"] = L10n.T("Lojistik birimdeki ticari ürün adedi; (02) ile birlikte kullanılır."),
        ["400"] = L10n.T("Müşterinin sipariş numarası."),
        ["401"] = L10n.T("Küresel Sevkiyat Kimlik Numarası (GINC)."),
        ["402"] = L10n.T("Küresel Sevkiyat Tanımlama Numarası (GSIN, 17 hane, kontrol basamaklı)."),
        ["410"] = L10n.T("Teslim edilecek yerin GLN'si (alıcı)."),
        ["411"] = L10n.T("Fatura edilecek yerin GLN'si."),
        ["412"] = L10n.T("Satın alınan yerin (tedarikçinin) GLN'si. Gönderen firma olarak kendi GLN'nizi yazabilirsiniz."),
        ["413"] = L10n.T("Nihai teslim yerinin GLN'si."),
        ["414"] = L10n.T("Fiziksel konumun GLN'si."),
        ["415"] = L10n.T("Fatura eden tarafın GLN'si."),
        ["420"] = L10n.T("Teslim adresinin posta kodu (aynı ülke içi)."),
        ["422"] = L10n.T("Menşe ülke (ISO 3166 sayısal kod; Türkiye = 792)."),
        ["7003"] = L10n.T("Son kullanma tarihi ve saati, YYAAGGSSDD."),
        ["8004"] = L10n.T("Küresel Bireysel Varlık Tanımlayıcısı (GIAI)."),
        ["90"] = L10n.T("Taraflar arasında anlaşılmış serbest bilgi."),
    };

    private static readonly HashSet<string> CommonAis = ["00", "01", "02", "10", "11", "13", "15", "17", "21", "37", "400", "410", "412", "420"];

    private static readonly Lazy<IReadOnlyList<Gs1AiInfo>> AllLazy = new(Build);

    /// <summary>Tüm AI'lar (barkod motorunun desteklediği), sayısal sırada.</summary>
    public static IReadOnlyList<Gs1AiInfo> All => AllLazy.Value;

    /// <summary>Koli/palet etiketlerinde sık kullanılanlar.</summary>
    public static IReadOnlyList<Gs1AiInfo> Common => All.Where(a => a.IsCommon).ToList();

    public static Gs1AiInfo? Find(string ai) => All.FirstOrDefault(a => a.Ai == ai);

    private static IReadOnlyList<Gs1AiInfo> Build()
    {
        var list = new List<Gs1AiInfo>();
        foreach (var def in BarcodeEngine.Gs1.Definitions)
        {
            list.Add(new Gs1AiInfo(
                def.Ai,
                def.Title,
                Describe(def),
                FormatOf(def),
                BarcodeEngine.Gs1.RequiresSeparator(def.Ai),
                def.HasCheckDigit,
                def.IsDate,
                CommonAis.Contains(def.Ai)));
        }
        return list
            .OrderBy(a => a.Ai.Length > 2 && a.Ai.StartsWith('9') ? 1 : 0)
            .ThenBy(a => a.Ai.PadRight(4, '0'), StringComparer.Ordinal)
            .ThenBy(a => a.Ai, StringComparer.Ordinal)
            .ToList();
    }

    private static string Describe(Gs1AiDefinition def)
    {
        if (Descriptions.TryGetValue(def.Ai, out var text)) return text;
        if (def.Ai.Length == 4 && def.Ai[..3] is var stem && int.TryParse(stem, NumberStyles.None, CultureInfo.InvariantCulture, out int s) && s is >= 310 and <= 369)
            return L10n.T($"{def.Title}. Son hane ({def.Ai[3]}) ondalık basamak sayısıdır; değer 6 hanedir (ör. {def.Ai[..3]}2 + 012345 = 123,45).");
        if (def.Ai.StartsWith("39", StringComparison.Ordinal))
            return L10n.T($"{def.Title}. Son hane ondalık basamak sayısıdır.");
        if (def.Ai is "91" or "92" or "93" or "94" or "95" or "96" or "97" or "98" or "99")
            return L10n.T("Firma içi bilgi; anlamını firmanız belirler.");
        return def.Title + ".";
    }

    private static string FormatOf(Gs1AiDefinition def)
    {
        string kind = def.Numeric ? "N" : "X";
        string length = def.MinLength == def.MaxLength
            ? def.MaxLength.ToString(CultureInfo.InvariantCulture)
            : ".." + def.MaxLength.ToString(CultureInfo.InvariantCulture);
        return $"N{def.Ai.Length}+{kind}{length}";
    }
}
