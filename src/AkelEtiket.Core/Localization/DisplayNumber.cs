using System.Globalization;
namespace AkelEtiket.Core.Localization;

/// <summary>Unambiguous UI measurements; never used for barcode or imported cell values.</summary>
public static class DisplayNumber
{
    public static bool TryParse(string? text, CultureInfo culture, out double value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string input = text.Trim();
        if (double.TryParse(input, NumberStyles.Float, culture, out value) && double.IsFinite(value)) return true;
        if (input.Contains(',') && input.Contains('.')) return false;
        return double.TryParse(input.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }
}
