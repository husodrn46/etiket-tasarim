using System.Globalization;
using AkelEtiket.Core.Model;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Core.Data;

public interface IFieldFormattingSource
{
    CultureInfo FormattingCulture { get; }
    string DefaultDateFormat { get; }
}

/// <summary>Used only for text rendering. Unformatted cells and all barcode/identifier sources stay raw.</summary>
public sealed class RegionalFieldSource(IFieldValueSource source, LabelFormatting settings) : IFieldValueSource, IDateFieldSource, IFieldFormattingSource
{
    public CultureInfo FormattingCulture { get; } = settings.OutputCulture();
    public string DefaultDateFormat => settings.DateFormat;
    private readonly CultureInfo _input = CultureInfo.GetCultureInfo(settings.InputCultureName);
    public bool TryGetValue(string name, string? format, out string value)
    {
        if (!settings.IsValid()) throw new FormatException(L10n.T("Etiketin bölgesel biçim ayarları geçersiz."));
        if (!source.TryGetValue(name,null,out string raw)) { value = ""; return false; }
        string normalized = FieldNames.Normalize(name);
        bool date = format == "DATE" || (format is null && normalized is "tarih" or "saat");
        if (date)
        {
            if (!TryGetDate(name,out var parsed,out _)) throw new FormatException(L10n.T($"{name}: tarih değeri okunamadı; veri bölgesini kontrol edin."));
            value = parsed.ToString(format is null && normalized == "saat" ? "HH:mm" : settings.DateFormat, FormattingCulture); return true;
        }
        if (string.IsNullOrEmpty(format)) { value = raw; return true; }
        if (format is not ("NUMBER" or "CURRENCY") && format.Any(c => c is 'y' or 'M' or 'd' or 'H' or 'h') && TryGetDate(name,out var explicitDate,out _))
        { value=explicitDate.ToString(format,FormattingCulture);return true; }
        if (format == "CURRENCY" || format == "NUMBER" || IsNumericFormat(format))
        {
            if (!TryNumber(raw,_input,out var number)) throw new FormatException(L10n.T($"{name}: sayı değeri okunamadı; veri bölgesini kontrol edin."));
            string output = format switch { "NUMBER" => settings.NumberFormat, "CURRENCY" => settings.CurrencyCode.Length == 0 ? settings.NumberFormat : "C", _ => format };
            if (output.StartsWith('C') && settings.CurrencyCode.Length == 0) output = settings.NumberFormat;
            value = number.ToString(output,FormattingCulture); return true;
        }
        if (TryGetDate(name,out var d,out _)) { value = d.ToString(format,FormattingCulture); return true; }
        return source.TryGetValue(name,format,out value);
    }
    private static bool IsNumericFormat(string format) => format.Length > 0 && (format[0] is 'N' or 'n' or 'F' or 'f' or 'C' or 'c' or 'P' or 'p'  or 'G' or 'g' || format.Contains('0') || format.Contains('#'));
    public bool TryGetDate(string name,out DateTime date,out bool exists)
    {
        if (source is CompositeFieldSource composite)
        {
            foreach(var leaf in composite.Sources)
            {
                bool found=new RegionalFieldSource(leaf,settings).TryGetDate(name,out date,out exists);
                if(exists)return found;
            }
            date=default;exists=false;return false;
        }
        if (source is BuiltInFieldSource builtIn) return builtIn.TryGetDate(name,out date,out exists);
        exists=source.TryGetValue(name,null,out var raw); date=default;
        return exists && (DateTime.TryParse(raw,_input,DateTimeStyles.AllowWhiteSpaces,out date) || DateTime.TryParseExact(raw,["yyyy-MM-dd","yyyy-MM-dd HH:mm:ss","yyyy-MM-ddTHH:mm:ss","yyyy-MM-ddTHH:mm:ss.FFFFFFFK"],CultureInfo.InvariantCulture,DateTimeStyles.AllowWhiteSpaces,out date));
    }
    public static bool TryNumber(string text,CultureInfo input,out decimal value)
    {
        string s = text.Trim(); string group = input.NumberFormat.NumberGroupSeparator, dec = input.NumberFormat.NumberDecimalSeparator;
        // Reject malformed grouping, rather than allowing .NET's permissive "12,34" => 1234.
        if (group.Length > 0 && group != dec && s.Contains(group,StringComparison.Ordinal))
        {
            string integral = s.Split(dec,StringSplitOptions.None)[0].TrimStart('+','-');
            var pieces = integral.Split(group,StringSplitOptions.None);
            if (pieces.Length < 2 || pieces[0].Length is < 1 or > 3 || pieces.Skip(1).Any(p => p.Length != 3) ||
                s.Split(dec,StringSplitOptions.None).Skip(1).Any(p => p.Contains(group,StringComparison.Ordinal))) { value=0; return false; }
        }
        return decimal.TryParse(s,NumberStyles.Number,input,out value);
    }
}
