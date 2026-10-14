using AkelEtiket.Core;

namespace AkelEtiket.Rendering;

/// <summary>Metin ölçüsü (mm). Render motoruyla aynı yerleşim kurallarını kullanır.</summary>
public readonly record struct TextExtent(double WidthMm, double HeightMm, int LineCount, bool FontSubstituted, string UsedFamily);

/// <summary>
/// Bir metnin etikette kaplayacağı alanı ölçer (ör. yapay zekâ araçlarında kutu boyutunu metne göre ayarlamak için).
/// Ölçüm, baskıdaki yerleşimle aynı motorla (Skia, yazıcı çözünürlüğü) yapılır.
/// </summary>
public static class TextMetrics
{
    public static TextExtent Measure(string text, string fontFamily, double fontSizePt, bool bold = false, bool italic = false,
        double lineSpacing = 1.0, double? maxWidthMm = null, double dotsPerMm = 8.0, FontProvider? fonts = null)
    {
        var font = (fonts ?? FontProvider.Default).Get(fontFamily, bold, italic);
        float sizeDots = (float)(Units.PointsToMm(Math.Clamp(fontSizePt, 2, 500)) * dotsPerMm);
        bool wrap = maxWidthMm is > 0;
        float maxWidth = wrap ? (float)(maxWidthMm!.Value * dotsPerMm) : 0;
        var layout = TextLayoutEngine.Layout(text ?? "", font, sizeDots, maxWidth, wrap, Math.Clamp(lineSpacing, 0.5, 3.0));
        return new TextExtent(
            layout.MaxWidth / dotsPerMm,
            layout.TotalHeight / dotsPerMm,
            layout.Lines.Count,
            !font.IsExactFamily,
            font.Typeface.FamilyName);
    }
}
