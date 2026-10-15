using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.BarcodeEngine;

/// <summary>GS1 Uygulama Tanımlayıcısı (AI) tanımı.</summary>
public sealed record Gs1AiDefinition(
    string Ai,
    string Title,
    bool Numeric,
    int MinLength,
    int MaxLength,
    bool HasCheckDigit = false,
    bool IsDate = false);

/// <summary>Tek bir AI + değer çifti.</summary>
public sealed record Gs1Element(string Ai, string Value, Gs1AiDefinition Definition);

/// <summary>GS1 element dizisi ayrıştırma sonucu.</summary>
public sealed class Gs1ParseResult
{
    internal Gs1ParseResult(IReadOnlyList<Gs1Element> elements, IReadOnlyList<string> errors)
    {
        Elements = elements;
        Errors = errors;
    }

    public IReadOnlyList<Gs1Element> Elements { get; }
    public IReadOnlyList<string> Errors { get; }
    public bool IsValid => Errors.Count == 0 && Elements.Count > 0;

    /// <summary>İnsan okunur biçim: (01)08691234567890(10)ABC</summary>
    public string HumanReadable => string.Concat(Elements.Select(e => $"({e.Ai}){e.Value}"));

    /// <summary>
    /// Kodlayıcıya gidecek veri: başta FNC1, değişken uzunluklu AI'lardan sonra (son eleman hariç) FNC1 ayırıcı.
    /// </summary>
    public string BuildEncoderData(char fnc1)
    {
        var sb = new StringBuilder();
        sb.Append(fnc1);
        for (int i = 0; i < Elements.Count; i++)
        {
            var e = Elements[i];
            sb.Append(e.Ai).Append(e.Value);
            if (i < Elements.Count - 1 && Gs1.RequiresSeparator(e.Ai)) sb.Append(fnc1);
        }
        return sb.ToString();
    }

    /// <summary>Çözücünün (ZXing, ASSUME_GS1) döndürmesi beklenen metin: "]C1" + veri, ayırıcılar GS (0x1D).</summary>
    public string BuildDecodedForm(string symbologyIdentifier)
    {
        var sb = new StringBuilder(symbologyIdentifier);
        for (int i = 0; i < Elements.Count; i++)
        {
            var e = Elements[i];
            sb.Append(e.Ai).Append(e.Value);
            if (i < Elements.Count - 1 && Gs1.RequiresSeparator(e.Ai)) sb.Append('\u001d');
        }
        return sb.ToString();
    }
}

/// <summary>
/// GS1-128 kuralları: AI tablosu, uzunluk/karakter kontrolleri, kontrol basamakları, FNC1 ayırıcıları.
/// Girdi biçimi: "(01)08691234567890(10)ABC123(17)261231".
/// </summary>
public static class Gs1
{
    // GS1 Genel Spesifikasyonları: önceden tanımlı uzunluklu AI ön ekleri (bunlardan sonra FNC1 gerekmez).
    private static readonly HashSet<string> PredefinedLengthPrefixes =
    [
        "00", "01", "02", "03", "04", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20",
        "31", "32", "33", "34", "35", "36", "41",
    ];

    // GS1 AI kodlanabilir karakter seti 82.
    private const string Cset82 = "!\"%&'()*+,-./0123456789:;<=>?ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz";

    private static readonly Dictionary<string, Gs1AiDefinition> Table = BuildTable();

    private static readonly Regex ParenthesizedPattern = new(@"\(([^()]*)\)([^()]*)", RegexOptions.CultureInvariant);

    public static IReadOnlyCollection<Gs1AiDefinition> Definitions => Table.Values;

    public static bool RequiresSeparator(string ai) => !PredefinedLengthPrefixes.Contains(ai[..2]);

    public static bool TryGetDefinition(string ai, out Gs1AiDefinition definition) =>
        Table.TryGetValue(ai, out definition!);

    /// <summary>GS1-128'in taşıyabileceği en fazla veri karakteri.</summary>
    public const int Gs1_128MaxDataLength = 48;

    /// <summary>GS1 DataMatrix / GS1 QR için makul üst sınır (sembol kapasitesi ayrıca denetlenir).</summary>
    public const int TwoDimensionalMaxDataLength = 600;

    /// <summary>"(AI)değer(AI)değer…" biçimindeki GS1-128 girdisini ayrıştırır ve doğrular.</summary>
    public static Gs1ParseResult Parse(string? input) => Parse(input, "GS1-128", Gs1_128MaxDataLength);

    /// <summary>
    /// "(AI)değer(AI)değer…" girdisini ayrıştırır ve doğrular. AI kuralları bütün GS1 sembolojilerinde aynıdır;
    /// yalnızca ad (iletilerde) ve veri uzunluğu sınırı sembolojiye göre değişir.
    /// </summary>
    public static Gs1ParseResult Parse(string? input, string symbologyName, int maxDataLength)
    {
        var errors = new List<string>();
        var elements = new List<Gs1Element>();
        string text = (input ?? "").Trim();
        if (text.Length == 0)
        {
            errors.Add(L10n.T($"{symbologyName} içeriği boş."));
            return new Gs1ParseResult(elements, errors);
        }
        if (!text.StartsWith('('))
        {
            errors.Add(L10n.T($"{symbologyName} içeriği parantezli AI biçiminde olmalı, ör. (01)08691234567890(10)ABC123"));
            return new Gs1ParseResult(elements, errors);
        }

        int consumed = 0;
        foreach (Match match in ParenthesizedPattern.Matches(text))
        {
            if (match.Index != consumed)
            {
                errors.Add($"Beklenmeyen karakterler: \"{text[consumed..match.Index]}\"");
            }
            consumed = match.Index + match.Length;
            string ai = match.Groups[1].Value;
            string value = match.Groups[2].Value;
            if (!Table.TryGetValue(ai, out var def))
            {
                errors.Add(L10n.T($"({ai}) bilinen bir GS1 Uygulama Tanımlayıcısı değil."));
                continue;
            }
            ValidateValue(def, value, errors);
            elements.Add(new Gs1Element(ai, value, def));
        }
        if (consumed != text.Length)
            errors.Add(L10n.T($"Ayrıştırılamayan kısım: \"{text[consumed..]}\""));
        if (elements.Count == 0 && errors.Count == 0)
            errors.Add(L10n.T("Hiç AI bulunamadı."));

        var duplicates = elements.GroupBy(e => e.Ai).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        foreach (var dup in duplicates) errors.Add(L10n.T($"({dup}) birden fazla kez kullanılmış."));

        int dataLength = elements.Sum(e => e.Ai.Length + e.Value.Length) +
                         elements.Take(elements.Count - 1).Count(e => RequiresSeparator(e.Ai));
        if (dataLength > maxDataLength)
            errors.Add(L10n.T($"{symbologyName} içeriği en fazla {maxDataLength} veri karakteri olabilir (şu an {dataLength})."));

        return new Gs1ParseResult(elements, errors);
    }

    private static void ValidateValue(Gs1AiDefinition def, string value, List<string> errors)
    {
        string label = $"({def.Ai}) {def.Title}";
        if (value.Length < def.MinLength || value.Length > def.MaxLength)
        {
            errors.Add(def.MinLength == def.MaxLength
                ? L10n.T($"{label}: {def.MinLength} karakter olmalı (girilen {value.Length}).")
                : L10n.T($"{label}: {def.MinLength}–{def.MaxLength} karakter olmalı (girilen {value.Length})."));
            return;
        }
        if (def.Numeric && !CheckDigits.IsAllDigits(value))
        {
            errors.Add(L10n.T($"{label}: yalnızca rakam içermeli."));
            return;
        }
        if (!def.Numeric)
        {
            foreach (char c in value)
            {
                if (Cset82.IndexOf(c) < 0)
                {
                    errors.Add(L10n.T($"{label}: '{c}' karakteri GS1'de kullanılamaz (Türkçe harf ve boşluk desteklenmez)."));
                    return;
                }
            }
        }
        if (def.HasCheckDigit && !CheckDigits.IsValidGs1(value))
        {
            int expected = CheckDigits.Gs1Mod10(value.AsSpan(0, value.Length - 1));
            errors.Add(L10n.T($"{label}: kontrol basamağı hatalı, son hane {expected} olmalı ({value[..^1]}{expected})."));
        }
        if (def.IsDate && !IsValidGs1Date(value))
            errors.Add(L10n.T($"{label}: tarih YYAAGG biçiminde ve geçerli olmalı (gün 00 olabilir)."));
    }

    private static bool IsValidGs1Date(string yymmdd)
    {
        if (yymmdd.Length != 6 || !CheckDigits.IsAllDigits(yymmdd)) return false;
        int month = int.Parse(yymmdd.AsSpan(2, 2), CultureInfo.InvariantCulture);
        int day = int.Parse(yymmdd.AsSpan(4, 2), CultureInfo.InvariantCulture);
        if (month is < 1 or > 12) return false;
        if (day == 0) return true;
        int year = 2000 + int.Parse(yymmdd.AsSpan(0, 2), CultureInfo.InvariantCulture);
        return day <= DateTime.DaysInMonth(year, month);
    }

    private static Dictionary<string, Gs1AiDefinition> BuildTable()
    {
        var list = new List<Gs1AiDefinition>
        {
            new("00", "SSCC", true, 18, 18, HasCheckDigit: true),
            new("01", "GTIN", true, 14, 14, HasCheckDigit: true),
            new("02", L10n.T("İçerik GTIN"), true, 14, 14, HasCheckDigit: true),
            new("10", "Parti/Lot No", false, 1, 20),
            new("11", L10n.T("Üretim Tarihi"), true, 6, 6, IsDate: true),
            new("12", "Vade Tarihi", true, 6, 6, IsDate: true),
            new("13", "Ambalaj Tarihi", true, 6, 6, IsDate: true),
            new("15", L10n.T("Tavsiye Edilen Tüketim Tarihi"), true, 6, 6, IsDate: true),
            new("16", L10n.T("Satış Son Tarihi"), true, 6, 6, IsDate: true),
            new("17", "Son Kullanma Tarihi", true, 6, 6, IsDate: true),
            new("20", "Varyant", true, 2, 2),
            new("21", "Seri No", false, 1, 20),
            new("22", L10n.T("Tüketici Ürün Varyantı"), false, 1, 20),
            new("240", L10n.T("Ek Ürün Kimliği"), false, 1, 30),
            new("241", L10n.T("Müşteri Parça No"), false, 1, 30),
            new("250", L10n.T("İkincil Seri No"), false, 1, 30),
            new("30", L10n.T("Değişken Adet"), true, 1, 8),
            new("37", "Adet", true, 1, 8),
            new("400", L10n.T("Sipariş No"), false, 1, 30),
            new("401", "GINC", false, 1, 30),
            new("402", "GSIN", true, 17, 17, HasCheckDigit: true),
            new("403", "Rota Kodu", false, 1, 30),
            new("410", "Teslim Yeri GLN", true, 13, 13, HasCheckDigit: true),
            new("411", "Fatura Yeri GLN", true, 13, 13, HasCheckDigit: true),
            new("412", L10n.T("Tedarikçi GLN"), true, 13, 13, HasCheckDigit: true),
            new("413", "Nihai Teslim GLN", true, 13, 13, HasCheckDigit: true),
            new("414", "Konum GLN", true, 13, 13, HasCheckDigit: true),
            new("415", L10n.T("Ödeme Yeri GLN"), true, 13, 13, HasCheckDigit: true),
            new("420", "Teslim Posta Kodu", false, 1, 20),
            new("422", L10n.T("Menşe Ülke"), true, 3, 3),
            new("7003", "Son Kullanma Tarih/Saat", true, 10, 10),
            new("8004", "GIAI", false, 1, 30),
            new("8005", L10n.T("Birim Fiyat"), true, 6, 6),
            new("90", "Dahili Bilgi", false, 1, 30),
        };
        // Ölçü AI'ları 310n…369n: n = ondalık basamak sayısı (0–5), değer 6 hane.
        string[] measureTitles =
        [
            L10n.T("310:Net Ağırlık (kg)"), "311:Uzunluk (m)", L10n.T("312:Genişlik (m)"), L10n.T("313:Yükseklik (m)"), "314:Alan (m²)",
            "315:Net Hacim (l)", "316:Net Hacim (m³)", L10n.T("320:Net Ağırlık (lb)"), L10n.T("330:Brüt Ağırlık (kg)"),
            "331:Lojistik Uzunluk (m)", L10n.T("332:Lojistik Genişlik (m)"), L10n.T("333:Lojistik Yükseklik (m)"),
            "335:Lojistik Hacim (l)", "336:Lojistik Hacim (m³)",
        ];
        foreach (var entry in measureTitles)
        {
            var parts = entry.Split(':');
            for (int n = 0; n <= 5; n++)
                list.Add(new Gs1AiDefinition(parts[0] + n, L10n.T($"{parts[1]}, {n} ondalık"), true, 6, 6));
        }
        for (int n = 0; n <= 9; n++)
        {
            list.Add(new Gs1AiDefinition("390" + n, L10n.T($"Tutar, {n} ondalık"), true, 1, 15));
            list.Add(new Gs1AiDefinition("392" + n, L10n.T($"Fiyat, {n} ondalık"), true, 1, 15));
        }
        for (int n = 91; n <= 99; n++)
            list.Add(new Gs1AiDefinition(n.ToString(CultureInfo.InvariantCulture), L10n.T("Firma İçi Bilgi"), false, 1, 90));
        return list.ToDictionary(d => d.Ai);
    }
}
