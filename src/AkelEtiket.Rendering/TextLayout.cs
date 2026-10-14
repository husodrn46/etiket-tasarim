using System.Text;
using SkiaSharp;

namespace AkelEtiket.Rendering;

/// <summary>Yerleştirilmiş metin satırı.</summary>
internal readonly record struct TextLine(string Text, float Width);

/// <summary>Metin bloğu yerleşimi (nokta cinsinden).</summary>
internal sealed class TextBlockLayout
{
    public required List<TextLine> Lines { get; init; }
    public required float FontSize { get; init; }
    public required float LineHeight { get; init; }
    public required float Ascent { get; init; }
    public required float Descent { get; init; }

    public float TotalHeight => Lines.Count == 0 ? 0 : (Lines.Count - 1) * LineHeight + Ascent + Descent;
    public float MaxWidth => Lines.Count == 0 ? 0 : Lines.Max(l => l.Width);

    public bool FitsIn(float width, float height) => TotalHeight <= height + 0.5f && MaxWidth <= width + 0.5f;
}

/// <summary>Satır kaydırma ve otomatik küçültme (Türkçe metin için tam Unicode, karakter bazında ölçüm).</summary>
internal static class TextLayoutEngine
{
    public static TextBlockLayout Layout(string text, FontMatch font, float sizeDots, float maxWidth, bool wrap, double lineSpacing)
    {
        using var skFont = font.CreateFont(sizeDots);
        var metrics = skFont.Metrics;
        float ascent = -metrics.Ascent;
        float descent = metrics.Descent;
        float lineHeight = (float)(skFont.Spacing * lineSpacing);
        var lines = new List<TextLine>();
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (string paragraph in normalized.Split('\n'))
        {
            if (!wrap || maxWidth <= 1)
            {
                lines.Add(new TextLine(paragraph, skFont.MeasureText(paragraph)));
                continue;
            }
            WrapParagraph(paragraph, skFont, maxWidth, lines);
        }
        return new TextBlockLayout
        {
            Lines = lines,
            FontSize = sizeDots,
            LineHeight = lineHeight,
            Ascent = ascent,
            Descent = descent,
        };
    }

    /// <summary>Metin kutuya sığana kadar yazı boyutunu küçültür (en az %15'ine kadar).</summary>
    public static TextBlockLayout LayoutShrinkToFit(string text, FontMatch font, float sizeDots, float boxWidth, float boxHeight, bool wrap, double lineSpacing)
    {
        var layout = Layout(text, font, sizeDots, boxWidth, wrap, lineSpacing);
        if (layout.FitsIn(boxWidth, boxHeight)) return layout;
        float low = Math.Max(4f, sizeDots * 0.15f), high = sizeDots;
        TextBlockLayout best = Layout(text, font, low, boxWidth, wrap, lineSpacing);
        if (!best.FitsIn(boxWidth, boxHeight)) return best;
        for (int i = 0; i < 12 && high - low > 0.25f; i++)
        {
            float mid = (low + high) / 2;
            var candidate = Layout(text, font, mid, boxWidth, wrap, lineSpacing);
            if (candidate.FitsIn(boxWidth, boxHeight))
            {
                best = candidate;
                low = mid;
            }
            else
            {
                high = mid;
            }
        }
        return best;
    }

    private static void WrapParagraph(string paragraph, SKFont font, float maxWidth, List<TextLine> lines)
    {
        if (paragraph.Length == 0)
        {
            lines.Add(new TextLine("", 0));
            return;
        }
        int firstLine = lines.Count;
        var current = new StringBuilder();
        foreach (string word in SplitKeepingSpaces(paragraph))
        {
            bool isSpace = word.Length > 0 && char.IsWhiteSpace(word[0]);
            string candidate = current.ToString() + word;
            if (font.MeasureText(candidate) <= maxWidth)
            {
                current.Append(word);
                continue;
            }
            if (isSpace)
            {
                // Satır sonuna gelen boşluk yeni satıra taşınmaz.
                Flush(current, font, lines);
                continue;
            }
            if (current.Length > 0) Flush(current, font, lines);
            // Tek kelime satırdan uzunsa karakter bazında böl.
            string remaining = word;
            while (font.MeasureText(remaining) > maxWidth && remaining.Length > 1)
            {
                int count = Math.Clamp(font.BreakText(remaining, maxWidth), 1, remaining.Length);
                if (count < remaining.Length && char.IsHighSurrogate(remaining[count - 1]) && count > 1) count--;
                if (count >= remaining.Length) break;
                lines.Add(new TextLine(remaining[..count], font.MeasureText(remaining[..count])));
                remaining = remaining[count..];
            }
            current.Append(remaining);
        }
        if (current.Length > 0) Flush(current, font, lines);
        if (lines.Count == firstLine) lines.Add(new TextLine("", 0));
    }

    private static void Flush(StringBuilder current, SKFont font, List<TextLine> lines)
    {
        string line = current.ToString().TrimEnd();
        if (line.Length > 0) lines.Add(new TextLine(line, font.MeasureText(line)));
        current.Clear();
    }

    private static IEnumerable<string> SplitKeepingSpaces(string text)
    {
        int start = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (i == text.Length || char.IsWhiteSpace(text[i]) != char.IsWhiteSpace(text[start]))
            {
                yield return text[start..i];
                start = i;
            }
        }
    }
}
