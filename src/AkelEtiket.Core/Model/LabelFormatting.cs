using System.Globalization;

namespace AkelEtiket.Core.Model;

/// <summary>Opt-in document formatting. Null on older labels; UI language is never consulted.</summary>
public sealed record LabelFormatting
{
    public string CultureName { get; init; } = "tr-TR";
    public string InputCultureName { get; init; } = "tr-TR";
    public string DateFormat { get; init; } = "yyyy-MM-dd";
    public string NumberFormat { get; init; } = "N2";
    public string CurrencyCode { get; init; } = "";
    public CultureInfo OutputCulture()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo(CultureName).Clone();
        if (CurrencyCode.Length > 0) culture.NumberFormat.CurrencySymbol = CurrencyCode;
        return culture;
    }
    public bool IsValid()
    {
        if (CultureName is null || InputCultureName is null || DateFormat is null || NumberFormat is null || CurrencyCode is null) return false;
        try
        {
            _ = CultureInfo.GetCultureInfo(InputCultureName);
            var culture = OutputCulture();
            if (CurrencyCode.Length != 0 && (CurrencyCode.Length != 3 || !CurrencyCode.All(c => c is >= 'A' and <= 'Z'))) return false;
            if (DateFormat.Length is 0 or > 80 || NumberFormat.Length is 0 or > 80) return false;
            _ = DateTime.Now.ToString(DateFormat,culture); _ = 1234.56m.ToString(NumberFormat,culture); return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException) { return false; }
    }
}
