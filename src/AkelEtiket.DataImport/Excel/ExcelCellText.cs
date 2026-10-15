using System.Globalization;
using AkelEtiket.DataImport.Internal;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.DataImport.Excel;

/// <summary>
/// Excel hücre değerini metne çevirme kuralları; .xlsx (ClosedXML) ve .xls (ExcelDataReader) okuyucuları aynı
/// sonucu verir: tam sayılar basamak basamak (bilimsel gösterim/binlik ayracı olmadan), "0000000000000" gibi
/// sıfır biçimlerinde dolgu korunur, ondalıklı sayılar hücre biçimine göre Türkçe kültürle yazılır.
/// </summary>
internal static class ExcelCellText
{
    /// <summary>Excel sayıları en fazla 15 anlamlı basamakla saklar.</summary>
    public const double PrecisionLimit = 1e15;

    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Boolean(bool value) => value ? "DOĞRU" : "YANLIŞ";

    public static string Date(DateTime date) => date.TimeOfDay == TimeSpan.Zero
        ? date.ToString("dd.MM.yyyy", Turkish)
        : date.ToString("dd.MM.yyyy HH:mm", Turkish);

    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) return span.ToString("c", CultureInfo.InvariantCulture);
        string seconds = span.Seconds != 0 ? $":{span.Seconds:00}" : "";
        return span.TotalDays >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}{seconds}"
            : $"{span.Hours:00}:{span.Minutes:00}{seconds}";
    }

    public static ImportIssue ErrorValueIssue(int row, string? column, string errorText) =>
        new(row, column, ImportIssueSeverity.Warning, L10n.T($"Hücrede Excel hata değeri var ({errorText}); boş kabul edildi."));

    /// <summary>Sayı hücresini metne çevirir.</summary>
    /// <param name="format">Hücrenin sayı biçimi ("" = Genel).</param>
    /// <param name="formatId">Excel'in yerleşik biçim numarası (0 = Genel, 9/10 = yüzde, 11/48 = bilimsel).</param>
    /// <param name="formatted">Hücrenin Excel'deki görünümü (biçim uygulanmış); hesaplanamazsa null.</param>
    public static string Number(double number, string format, int formatId, Func<string?> formatted, int row, string? column, List<ImportIssue>? issues)
    {
        if (double.IsNaN(number) || double.IsInfinity(number)) return "";

        // Yüzde biçimi görüntülendiği gibi alınır (1 → %100).
        if (format.Contains('%') || formatId is 9 or 10)
            return formatted() ?? DecimalText(number);

        bool integral = Math.Floor(number) == number;
        if (integral && Math.Abs(number) < PrecisionLimit)
        {
            string digits = ((long)number).ToString(CultureInfo.InvariantCulture);
            int padLength = ZeroPadLength(format);
            if (padLength > 0 && number >= 0 && digits.Length < padLength)
                digits = digits.PadLeft(padLength, '0');
            return digits;
        }

        if (integral)
        {
            string digits = LargeIntegerText(number);
            issues?.Add(new ImportIssue(row, column, ImportIssueSeverity.Error,
                L10n.T($"15 haneden uzun sayı ({digits}). Excel sayıları en fazla 15 anlamlı basamakla saklar; son haneler bozulmuş olabilir. ") +
                L10n.T("Bu sütunu Excel'de 'Metin' olarak biçimlendirip değerleri yeniden girin.")));
            return digits;
        }

        bool isGeneral = (formatId == 0 && format.Length == 0) || format.Equals("General", StringComparison.OrdinalIgnoreCase);
        if (isGeneral) return DecimalText(number);

        string? text = formatted();
        if (text is null) return DecimalText(number);
        bool explicitScientific = format.Contains("E+", StringComparison.OrdinalIgnoreCase) ||
                                  format.Contains("E-", StringComparison.OrdinalIgnoreCase) || formatId is 11 or 48;
        if (!explicitScientific && IntegrityChecks.IsScientificNotation(text)) return DecimalText(number);
        return text;
    }

    /// <summary>Biçim yalnızca sıfırlardan oluşuyorsa ("0000000000000") dolgu uzunluğu, değilse 0.</summary>
    public static int ZeroPadLength(string format)
    {
        if (format.Length < 2) return 0;
        foreach (char c in format)
            if (c != '0') return 0;
        return format.Length;
    }

    /// <summary>Ondalıklı sayıyı bilimsel gösterim olmadan Türkçe biçimde yazar (en fazla 15 anlamlı basamak).</summary>
    public static string DecimalText(double number)
    {
        try
        {
            return ((decimal)number).ToString(Turkish);
        }
        catch (OverflowException)
        {
            return number.ToString("R", Turkish);
        }
    }

    /// <summary>15 haneden uzun tam sayıyı Excel'in sakladığı 15 anlamlı basamakla, bilimsel gösterim olmadan yazar.</summary>
    public static string LargeIntegerText(double number)
    {
        try
        {
            return ((decimal)number).ToString("0", CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return number.ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
