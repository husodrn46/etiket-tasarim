using System.Globalization;
using System.Text;

namespace AkelEtiket.Core.Data;

/// <summary>Değişken veri alanı değerlerini sağlayan kaynak (veri satırı, yerleşik alanlar...).</summary>
public interface IFieldValueSource
{
    /// <summary>Alan değerini döndürür. <paramref name="format"/> {Alan:biçim} ile verilen biçimdir.</summary>
    bool TryGetValue(string name, string? format, out string value);
}

/// <summary>
/// Metindeki bir {Alan}, {Alan:biçim}, {TARIH+30} veya {Alan:BUYUK} parçası. <see cref="Name"/> yazıldığı gibidir
/// (ör. "TARIH+30"); tarih hesabı ve yazı dönüşümleri için <see cref="Expression"/> kullanılır.
/// </summary>
public readonly record struct FieldToken(string Name, string? Format, int Start, int Length)
{
    /// <summary>Ayrıştırılmış ifade: temel ad, tarih kaydırma, değer biçimi ve yazı dönüşümleri.</summary>
    public FieldExpression Expression => FieldExpression.Parse(Name, Format);

    /// <summary>Tarih kaydırması atılmış ad ("TARIH+30" → "TARIH").</summary>
    public string BaseName => Expression.BaseName;
}

/// <summary>Tarih hesabının birimi.</summary>
public enum DateOffsetUnit
{
    Day,
    Week,
    Month,
    Year,
}

/// <summary>{TARIH+30} gibi ifadelerdeki tarih kaydırması.</summary>
public readonly record struct FieldDateOffset(int Amount, DateOffsetUnit Unit)
{
    public DateTime Apply(DateTime date) => Unit switch
    {
        DateOffsetUnit.Week => date.AddDays(7.0 * Amount),
        DateOffsetUnit.Month => date.AddMonths(Amount),
        DateOffsetUnit.Year => date.AddYears(Amount),
        _ => date.AddDays(Amount),
    };
}

/// <summary>Alan değerine uygulanan yazı dönüşümü (Türkçe kurallarla).</summary>
public enum TextTransform
{
    /// <summary>BUYUK: büyük harf (i → İ, ı → I).</summary>
    Upper,

    /// <summary>KUCUK: küçük harf (I → ı, İ → i).</summary>
    Lower,

    /// <summary>BASHARF: her kelimenin ilk harfi büyük ("MELAMİN KASE" → "Melamin Kase").</summary>
    TitleCase,
}

/// <summary>
/// Alan ifadesi: <c>{Ad}</c>, <c>{Ad:biçim}</c>, <c>{Ad+N}</c> / <c>{Ad-N}</c> (tarih hesabı; birim: gün varsayılan,
/// "hafta", "ay", "yıl" ya da kısaca g/h/a/y), biçimden sonra "|" ile yazı dönüşümleri: <c>{TARIH+30:dd MMMM yyyy|BUYUK}</c>.
/// Dönüşüm tek başına da yazılabilir: <c>{UrunAdi:BUYUK}</c>, <c>{Renk:KUCUK}</c>, <c>{UrunAdi:BASHARF}</c>.
/// </summary>
public sealed record FieldExpression(string RawName, string BaseName, FieldDateOffset? Offset, string? ValueFormat, IReadOnlyList<TextTransform> Transforms)
{
    private const int MaxOffset = 36500;

    public static FieldExpression Parse(string rawName, string? format)
    {
        string name = (rawName ?? "").Trim();
        string baseName = name;
        FieldDateOffset? offset = null;
        int sign = Math.Max(name.LastIndexOf('+'), name.LastIndexOf('-'));
        if (sign > 0 && TryParseOffset(name[(sign + 1)..], name[sign] == '-', out var parsed) && name[..sign].Trim().Length > 0)
        {
            baseName = name[..sign].Trim();
            offset = parsed;
        }

        string? valueFormat = null;
        var transforms = new List<TextTransform>();
        if (!string.IsNullOrEmpty(format))
        {
            foreach (string part in format.Split('|'))
            {
                if (TryParseTransform(part, out var transform))
                {
                    transforms.Add(transform);
                    continue;
                }
                // Biçim olduğu gibi korunur (eski şablonlarda baştaki/sondaki boşluk bilerek yazılmış olabilir).
                if (valueFormat is null && part.Trim().Length > 0) valueFormat = part;
            }
        }
        return new FieldExpression(name, baseName, offset, valueFormat, transforms);
    }

    /// <summary>"30", "30g", "2 hafta", "6ay", "1 yıl" → kaydırma.</summary>
    private static bool TryParseOffset(string text, bool negative, out FieldDateOffset offset)
    {
        offset = default;
        string t = text.Trim();
        int digits = 0;
        while (digits < t.Length && char.IsAsciiDigit(t[digits])) digits++;
        if (digits == 0 || digits > 5) return false;
        int amount = int.Parse(t[..digits], NumberStyles.None, CultureInfo.InvariantCulture);
        if (amount > MaxOffset) return false;
        DateOffsetUnit? unit = FieldNames.Normalize(t[digits..]) switch
        {
            "" or "g" or "gun" => DateOffsetUnit.Day,
            "h" or "hafta" => DateOffsetUnit.Week,
            "a" or "ay" => DateOffsetUnit.Month,
            "y" or "yil" => DateOffsetUnit.Year,
            _ => null,
        };
        if (unit is null) return false;
        offset = new FieldDateOffset(negative ? -amount : amount, unit.Value);
        return true;
    }

    private static bool TryParseTransform(string text, out TextTransform transform)
    {
        switch (FieldNames.Normalize(text))
        {
            case "buyuk":
            case "buyukharf":
            case "upper":
                transform = TextTransform.Upper;
                return true;
            case "kucuk":
            case "kucukharf":
            case "lower":
                transform = TextTransform.Lower;
                return true;
            case "basharf":
            case "baslik":
            case "title":
                transform = TextTransform.TitleCase;
                return true;
            default:
                transform = default;
                return false;
        }
    }
}

/// <summary>Alan çözümleme sonucu.</summary>
public sealed class FieldResolution
{
    public FieldResolution(string text, IReadOnlyList<string> missingFields, bool hasFields)
        : this(text, missingFields, hasFields, [])
    {
    }

    public FieldResolution(string text, IReadOnlyList<string> missingFields, bool hasFields, IReadOnlyList<string> problems)
    {
        Text = text;
        MissingFields = missingFields;
        HasFields = hasFields;
        Problems = problems;
    }

    /// <summary>Alanları yerine konmuş metin (bulunamayan alanlar {Alan} olarak kalır).</summary>
    public string Text { get; }

    public IReadOnlyList<string> MissingFields { get; }

    public bool HasFields { get; }

    /// <summary>Alan bulundu ama ifade uygulanamadı (ör. tarih olmayan değere tarih hesabı). Türkçe açıklamalar.</summary>
    public IReadOnlyList<string> Problems { get; }

    public bool IsComplete => MissingFields.Count == 0 && Problems.Count == 0;
}

/// <summary>
/// "{UrunAdi}" gibi değişken veri alanlarının ayrıştırılması ve doldurulması.
/// Süslü parantezin kendisini yazmak için "{{" ve "}}" kullanılır.
/// Desteklenenler: {Alan}, {Alan:biçim} (sayı/tarih biçimi), {TARIH+30} / {TARIH-7:dd.MM.yyyy} (tarih hesabı; tarih
/// içeren veri sütunlarında da), {Alan:BUYUK} / {Alan:KUCUK} / {Alan:BASHARF} (Türkçe yazı dönüşümleri; biçimle
/// birlikte "|" ile: {TARIH:dd MMMM yyyy|BUYUK}).
/// </summary>
public static class FieldTemplate
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string MakePlaceholder(string fieldName) => "{" + fieldName.Trim() + "}";

    /// <summary>Düz metindeki süslü parantezleri kaçış karakterine çevirir.</summary>
    public static string Escape(string literal) => (literal ?? "").Replace("{", "{{").Replace("}", "}}");

    public static bool ContainsFields(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return false;
        foreach (var _ in EnumerateTokens(text)) return true;
        return false;
    }

    public static IReadOnlyList<FieldToken> GetTokens(string? text) =>
        string.IsNullOrEmpty(text) ? [] : EnumerateTokens(text).ToList();

    public static IReadOnlyList<string> GetFieldNames(string? text) =>
        GetTokens(text).Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Alanları kaynaktaki değerlerle doldurur. Kaynak null ise alanlar olduğu gibi kalır.</summary>
    public static FieldResolution Resolve(string? text, IFieldValueSource? source)
    {
        if (string.IsNullOrEmpty(text)) return new FieldResolution("", [], false);
        if (text.IndexOf('{') < 0 && text.IndexOf('}') < 0) return new FieldResolution(text, [], false);

        var sb = new StringBuilder(text.Length + 16);
        List<string>? missing = null;
        List<string>? problems = null;
        bool hasFields = false;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '{')
            {
                if (i + 1 < text.Length && text[i + 1] == '{')
                {
                    sb.Append('{');
                    i += 2;
                    continue;
                }
                if (TryReadToken(text, i, out var token))
                {
                    hasFields = true;
                    switch (ResolveToken(token, source, out var value, out var problem))
                    {
                        case TokenOutcome.Resolved:
                            sb.Append(value);
                            break;
                        case TokenOutcome.Problem:
                            sb.Append(text, token.Start, token.Length);
                            (problems ??= []).Add(problem!);
                            break;
                        default:
                            sb.Append(text, token.Start, token.Length);
                            (missing ??= []).Add(token.Name);
                            break;
                    }
                    i = token.Start + token.Length;
                    continue;
                }
                sb.Append('{');
                i++;
                continue;
            }
            if (c == '}' && i + 1 < text.Length && text[i + 1] == '}')
            {
                sb.Append('}');
                i += 2;
                continue;
            }
            sb.Append(c);
            i++;
        }
        IReadOnlyList<string> missingList = missing is null
            ? []
            : missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        IReadOnlyList<string> problemList = problems is null ? [] : problems.Distinct().ToList();
        return new FieldResolution(sb.ToString(), missingList, hasFields, problemList);
    }

    private enum TokenOutcome
    {
        Resolved,
        Missing,
        Problem,
    }

    /// <summary>
    /// Tek bir alanı çözer. Önce yazıldığı gibi ad denenir (geriye uyum: "Model-2" gibi sütun adları), sonra tarih
    /// hesabı. Yazı dönüşümleri değer biçimlendikten sonra uygulanır.
    /// </summary>
    private static TokenOutcome ResolveToken(FieldToken token, IFieldValueSource? source, out string value, out string? problem)
    {
        value = "";
        problem = null;
        if (source is null) return TokenOutcome.Missing;
        var expression = token.Expression;
        if (source.TryGetValue(expression.RawName, expression.ValueFormat, out var raw))
        {
            value = ApplyTransforms(raw, expression.Transforms);
            return TokenOutcome.Resolved;
        }
        if (expression.Offset is not { } offset) return TokenOutcome.Missing;

        if (FieldDates.TryGetDate(source, expression.BaseName, out var date, out bool exists))
        {
            var shifted = offset.Apply(date);
            string format = expression.ValueFormat ?? "dd.MM.yyyy";
            string formatted;
            try
            {
                formatted = shifted.ToString(format, Turkish);
            }
            catch (FormatException)
            {
                formatted = shifted.ToString("dd.MM.yyyy", Turkish);
            }
            value = ApplyTransforms(formatted, expression.Transforms);
            return TokenOutcome.Resolved;
        }
        if (!exists) return TokenOutcome.Missing;
        problem = "{" + token.Name + "}: değer tarih değil; tarih hesabı yapılamadı.";
        return TokenOutcome.Problem;
    }

    /// <summary>Türkçe yazı dönüşümlerini sırayla uygular.</summary>
    public static string ApplyTransforms(string value, IReadOnlyList<TextTransform> transforms)
    {
        foreach (var transform in transforms)
        {
            value = transform switch
            {
                TextTransform.Upper => value.ToUpper(Turkish),
                TextTransform.Lower => value.ToLower(Turkish),
                _ => Turkish.TextInfo.ToTitleCase(value.ToLower(Turkish)),
            };
        }
        return value;
    }

    private static IEnumerable<FieldToken> EnumerateTokens(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '{')
            {
                if (i + 1 < text.Length && text[i + 1] == '{')
                {
                    i += 2;
                    continue;
                }
                if (TryReadToken(text, i, out var token))
                {
                    yield return token;
                    i = token.Start + token.Length;
                    continue;
                }
            }
            else if (c == '}' && i + 1 < text.Length && text[i + 1] == '}')
            {
                i += 2;
                continue;
            }
            i++;
        }
    }

    private static bool TryReadToken(string text, int start, out FieldToken token)
    {
        token = default;
        int close = -1;
        for (int j = start + 1; j < text.Length; j++)
        {
            char c = text[j];
            if (c == '}')
            {
                close = j;
                break;
            }
            if (c is '{' or '\r' or '\n') return false;
        }
        if (close < 0) return false;

        string inner = text.Substring(start + 1, close - start - 1);
        int colon = inner.IndexOf(':');
        string name = (colon >= 0 ? inner[..colon] : inner).Trim();
        string? format = colon >= 0 ? inner[(colon + 1)..] : null;
        if (name.Length == 0 || name.Length > 100) return false;
        token = new FieldToken(name, string.IsNullOrEmpty(format) ? null : format, start, close - start + 1);
        return true;
    }
}

/// <summary>Alan adı eşleştirme yardımcıları (Türkçe karakter ve büyük/küçük harf duyarsız).</summary>
public static class FieldNames
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    /// Karşılaştırma için sadeleştirir: küçük harf, Türkçe karakterler Latin karşılığına,
    /// boşluk/alt çizgi/tire/nokta kaldırılır. "Ürün Adı" → "urunadi".
    /// </summary>
    public static string Normalize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char ch in name)
        {
            char c = ch switch
            {
                'ı' or 'I' or 'İ' or 'i' => 'i',
                'ğ' or 'Ğ' => 'g',
                'ü' or 'Ü' => 'u',
                'ş' or 'Ş' => 's',
                'ö' or 'Ö' => 'o',
                'ç' or 'Ç' => 'c',
                _ => char.ToLowerInvariant(ch),
            };
            if (char.IsWhiteSpace(c) || c is '_' or '-' or '.') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Verilen adı sütun listesinde arar: önce birebir, sonra harf duyarsız, sonra sadeleştirilmiş.</summary>
    public static int FindIndex(IReadOnlyList<string> columns, string name)
    {
        for (int i = 0; i < columns.Count; i++)
            if (string.Equals(columns[i], name, StringComparison.Ordinal)) return i;
        for (int i = 0; i < columns.Count; i++)
            if (string.Equals(columns[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        for (int i = 0; i < columns.Count; i++)
            if (string.Compare(columns[i], name, Turkish, CompareOptions.IgnoreCase) == 0) return i;
        string normalized = Normalize(name);
        if (normalized.Length == 0) return -1;
        for (int i = 0; i < columns.Count; i++)
            if (Normalize(columns[i]) == normalized) return i;
        return -1;
    }
}
