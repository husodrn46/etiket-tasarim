using System.Globalization;

namespace AkelEtiket.Core.Data;

/// <summary>
/// Tarih değeri verebilen alan kaynağı (tarih hesabı için: {TARIH+30}). <paramref name="exists"/> alanın kaynakta
/// bulunup bulunmadığını söyler (bulundu ama tarih değilse false + exists=true).
/// </summary>
public interface IDateFieldSource
{
    bool TryGetDate(string name, out DateTime date, out bool exists);
}

/// <summary>Tarih hesabı yardımcıları.</summary>
public static class FieldDates
{
    private static readonly string[] Formats =
    [
        "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yyyy HH:mm", "d.M.yyyy H:mm", "dd.MM.yyyy HH:mm:ss", "dd.MM.yy",
        "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yyyy HH:mm", "dd-MM-yyyy", "d-M-yyyy",
        "yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy.MM.dd",
    ];

    /// <summary>Hücre metnini tarihe çevirir (Excel içe aktarımının "gg.aa.yyyy" biçimi ve yaygın biçimler).</summary>
    public static bool TryParse(string? text, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return DateTime.TryParseExact(text.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date);
    }

    /// <summary>Kaynaktan alanın tarih değerini alır.</summary>
    public static bool TryGetDate(IFieldValueSource source, string name, out DateTime date, out bool exists)
    {
        if (source is IDateFieldSource dates) return dates.TryGetDate(name, out date, out exists);
        date = default;
        exists = source.TryGetValue(name, null, out string raw);
        return exists && TryParse(raw, out date);
    }
}

/// <summary>Sütun adları + tek satır değerlerinden oluşan alan kaynağı (Excel/CSV satırı).</summary>
public sealed class RecordFieldSource : IFieldValueSource, IDateFieldSource
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly IReadOnlyList<string> _columns;
    private readonly IReadOnlyList<string?> _values;

    public RecordFieldSource(IReadOnlyList<string> columns, IReadOnlyList<string?> values)
    {
        _columns = columns ?? throw new ArgumentNullException(nameof(columns));
        _values = values ?? throw new ArgumentNullException(nameof(values));
    }

    public bool TryGetValue(string name, string? format, out string value)
    {
        int index = FieldNames.FindIndex(_columns, name);
        if (index < 0)
        {
            value = "";
            return false;
        }
        string raw = index < _values.Count ? _values[index] ?? "" : "";
        value = string.IsNullOrEmpty(format) ? raw : ApplyFormat(raw, format);
        return true;
    }

    public bool TryGetDate(string name, out DateTime date, out bool exists)
    {
        date = default;
        int index = FieldNames.FindIndex(_columns, name);
        exists = index >= 0;
        return exists && FieldDates.TryParse(index < _values.Count ? _values[index] : null, out date);
    }

    /// <summary>
    /// {Fiyat:0.00} gibi biçimler: değer sayıysa Türkçe kültürle biçimlenir, tarihse tarih biçimi uygulanır,
    /// aksi hâlde değer olduğu gibi döner. Barkod değerleri biçim verilmedikçe asla dönüştürülmez.
    /// </summary>
    public static string ApplyFormat(string raw, string format)
    {
        string trimmed = raw.Trim();
        if (decimal.TryParse(trimmed, NumberStyles.Number, Turkish, out var trNumber) && LooksTurkishNumber(trimmed))
            return SafeFormat(trNumber, format) ?? raw;
        if (decimal.TryParse(trimmed, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var number))
            return SafeFormat(number, format) ?? raw;
        if (DateTime.TryParse(trimmed, Turkish, DateTimeStyles.AllowWhiteSpaces, out var date))
        {
            try { return date.ToString(format, Turkish); }
            catch (FormatException) { return raw; }
        }
        return raw;
    }

    private static bool LooksTurkishNumber(string s) =>
        s.Contains(',') && (!s.Contains('.') || s.LastIndexOf(',') > s.LastIndexOf('.'));

    private static string? SafeFormat(decimal number, string format)
    {
        try { return number.ToString(format, Turkish); }
        catch (FormatException) { return null; }
    }
}

/// <summary>
/// Yerleşik alanlar: {SIRA} (toplu baskıdaki sıra no; {SIRA:0000} ile sıfır dolgulu),
/// {TARIH} (gg.aa.yyyy), {SAAT} (ss:dd). Biçim verilebilir: {TARIH:dd MMMM yyyy}.
/// </summary>
public sealed class BuiltInFieldSource : IFieldValueSource, IDateFieldSource
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static readonly IReadOnlyList<string> FieldNamesList = ["SIRA", "TARIH", "SAAT"];

    public BuiltInFieldSource(long sequence, DateTime now)
    {
        Sequence = sequence;
        Now = now;
    }

    public long Sequence { get; }
    public DateTime Now { get; }

    public bool TryGetValue(string name, string? format, out string value)
    {
        switch (FieldNames.Normalize(name))
        {
            case "sira":
            case "sirano":
                value = Format(() => Sequence.ToString(format ?? "0", Turkish), Sequence.ToString(Turkish));
                return true;
            case "tarih":
                value = Format(() => Now.ToString(format ?? "dd.MM.yyyy", Turkish), Now.ToString("dd.MM.yyyy", Turkish));
                return true;
            case "saat":
                value = Format(() => Now.ToString(format ?? "HH:mm", Turkish), Now.ToString("HH:mm", Turkish));
                return true;
            default:
                value = "";
                return false;
        }
    }

    private static string Format(Func<string> formatter, string fallback)
    {
        try { return formatter(); }
        catch (FormatException) { return fallback; }
    }

    public bool TryGetDate(string name, out DateTime date, out bool exists)
    {
        string normalized = FieldNames.Normalize(name);
        exists = normalized is "sira" or "sirano" or "tarih" or "saat";
        date = Now;
        return normalized is "tarih" or "saat";
    }
}

/// <summary>Birden fazla kaynağı sırayla dener (önce veri satırı, sonra yerleşik alanlar).</summary>
public sealed class CompositeFieldSource : IFieldValueSource, IDateFieldSource
{
    private readonly IFieldValueSource[] _sources;
    public IReadOnlyList<IFieldValueSource> Sources => _sources;

    public CompositeFieldSource(params IFieldValueSource?[] sources) =>
        _sources = sources.Where(s => s is not null).Cast<IFieldValueSource>().ToArray();

    public bool TryGetValue(string name, string? format, out string value)
    {
        foreach (var source in _sources)
            if (source.TryGetValue(name, format, out value)) return true;
        value = "";
        return false;
    }

    /// <summary>Alanı ilk bulunduran kaynaktan tarih alır (değer tarih değilse orada durur).</summary>
    public bool TryGetDate(string name, out DateTime date, out bool exists)
    {
        foreach (var source in _sources)
        {
            bool found = FieldDates.TryGetDate(source, name, out date, out exists);
            if (exists) return found;
        }
        date = default;
        exists = false;
        return false;
    }
}
