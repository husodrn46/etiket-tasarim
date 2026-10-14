using AkelEtiket.Core;
using AkelEtiket.Core.Model;
using SkiaSharp;

namespace AkelEtiket.Rendering;

/// <summary>Metin öğesi çizimi (yerel koordinat, nokta).</summary>
internal static class TextElementRenderer
{
    public static (Action<SKCanvas> Draw, TextRenderInfo Info, string? Problem, string? FontWarning) Prepare(
        TextElement element, string text, int boxWidth, int boxHeight, double dotsPerMm, FontProvider fonts)
    {
        var font = fonts.Get(element.FontFamily, element.Bold, element.Italic);
        float requestedSize = (float)(Units.PointsToMm(element.FontSize) * dotsPerMm);
        var layout = element.AutoShrink
            ? TextLayoutEngine.LayoutShrinkToFit(text, font, requestedSize, boxWidth, boxHeight, element.WordWrap, element.LineSpacing)
            : TextLayoutEngine.Layout(text, font, requestedSize, boxWidth, element.WordWrap, element.LineSpacing);

        bool overflow = text.Length > 0 && !layout.FitsIn(boxWidth, boxHeight);
        string? fontWarning = null;
        if (!font.IsExactFamily)
            fontWarning = $"\"{element.FontFamily}\" yazı tipi bu bilgisayarda yok; yerine {font.Typeface.FamilyName} kullanıldı.";
        else if (text.Length > 0)
        {
            using var probe = font.CreateFont(10);
            string missing = new(text.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c) && !probe.ContainsGlyph(c)).Distinct().Take(8).ToArray());
            if (missing.Length > 0)
                fontWarning = $"\"{font.Typeface.FamilyName}\" yazı tipinde şu karakterler yok: {missing}";
        }

        var info = new TextRenderInfo
        {
            EffectiveFontSizePt = Units.MmToPoints(layout.FontSize / dotsPerMm),
            Overflow = overflow,
            FontSubstituted = !font.IsExactFamily,
            UsedFamily = font.Typeface.FamilyName,
            LineCount = layout.Lines.Count,
        };

        void Draw(SKCanvas canvas)
        {
            var box = new SKRect(0, 0, boxWidth, boxHeight);
            if (element.Background != FillStyle.None)
            {
                using var bg = RasterHelpers.FillPaint(element.Background == FillStyle.Black ? RasterHelpers.Black : RasterHelpers.White);
                canvas.DrawRect(box, bg);
            }
            if (text.Length == 0) return;

            canvas.Save();
            canvas.ClipRect(box, SKClipOperation.Intersect, antialias: false);
            using var skFont = font.CreateFont(layout.FontSize);
            using var paint = RasterHelpers.FillPaint(element.TextColor == InkColor.Black ? RasterHelpers.Black : RasterHelpers.White, antialias: false);
            float top = element.VerticalAlignment switch
            {
                TextVerticalAlignment.Middle => (boxHeight - layout.TotalHeight) / 2f,
                TextVerticalAlignment.Bottom => boxHeight - layout.TotalHeight,
                _ => 0f,
            };
            float baseline = top + layout.Ascent;
            var metrics = skFont.Metrics;
            float underlinePos = metrics.UnderlinePosition is { } up && up > 0 ? up : layout.FontSize * 0.1f;
            float underlineThickness = Math.Max(1f, metrics.UnderlineThickness is { } ut && ut > 0 ? ut : layout.FontSize / 15f);
            foreach (var line in layout.Lines)
            {
                float x = element.HorizontalAlignment switch
                {
                    TextHorizontalAlignment.Center => (boxWidth - line.Width) / 2f,
                    TextHorizontalAlignment.Right => boxWidth - line.Width,
                    _ => 0f,
                };
                x = MathF.Round(x);
                float y = MathF.Round(baseline);
                if (line.Text.Length > 0) canvas.DrawText(line.Text, x, y, SKTextAlign.Left, skFont, paint);
                if (element.Underline && line.Width > 0)
                    canvas.DrawRect(new SKRect(x, MathF.Round(y + underlinePos), x + line.Width, MathF.Round(y + underlinePos + underlineThickness)), paint);
                baseline += layout.LineHeight;
            }
            canvas.Restore();
        }

        string? problem = overflow
            ? "Metin kutuya sığmıyor; taşan kısım basılmaz. Kutuyu büyütün veya \"Kutuya sığdır\" seçeneğini açın."
            : null;
        return (Draw, info, problem, fontWarning);
    }
}

/// <summary>Şekil (dikdörtgen, elips, çizgi) çizimi. Kenar kalınlıkları tam sayı noktaya yuvarlanır.</summary>
internal static class ShapeElementRenderer
{
    public static Action<SKCanvas> Prepare(ShapeElement element, int boxWidth, int boxHeight, double dotsPerMm)
    {
        int stroke = element.StrokeWidth <= 0 ? 0 : Math.Max(1, Units.MmToDotsRounded(element.StrokeWidth, dotsPerMm));
        float radius = (float)(element.CornerRadius * dotsPerMm);
        var strokeColor = element.StrokeColor == InkColor.Black ? RasterHelpers.Black : RasterHelpers.White;

        return canvas =>
        {
            var box = new SKRect(0, 0, boxWidth, boxHeight);
            switch (element.Kind)
            {
                case ShapeKind.Line:
                    DrawLine(canvas, element.LineOrientation, boxWidth, boxHeight, Math.Max(1, stroke), strokeColor);
                    return;

                case ShapeKind.Ellipse:
                    if (element.Fill != FillStyle.None)
                    {
                        using var fill = RasterHelpers.FillPaint(element.Fill == FillStyle.Black ? RasterHelpers.Black : RasterHelpers.White);
                        canvas.DrawOval(box, fill);
                    }
                    if (stroke > 0)
                    {
                        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
                        path.AddOval(box);
                        var inner = box;
                        inner.Inflate(-stroke, -stroke);
                        if (inner.Width > 0 && inner.Height > 0) path.AddOval(inner);
                        using var paint = RasterHelpers.FillPaint(strokeColor);
                        canvas.DrawPath(path, paint);
                    }
                    return;

                default:
                    if (element.Fill != FillStyle.None)
                    {
                        using var fill = RasterHelpers.FillPaint(element.Fill == FillStyle.Black ? RasterHelpers.Black : RasterHelpers.White, antialias: radius > 0);
                        if (radius > 0) canvas.DrawRoundRect(box, radius, radius, fill);
                        else canvas.DrawRect(box, fill);
                    }
                    if (stroke > 0)
                    {
                        using var paint = RasterHelpers.FillPaint(strokeColor, antialias: radius > 0);
                        if (radius <= 0)
                        {
                            // Keskin kenarlar: dört dolu dikdörtgen (yumuşatma yok).
                            canvas.DrawRect(new SKRect(0, 0, boxWidth, Math.Min(stroke, boxHeight)), paint);
                            canvas.DrawRect(new SKRect(0, Math.Max(0, boxHeight - stroke), boxWidth, boxHeight), paint);
                            canvas.DrawRect(new SKRect(0, 0, Math.Min(stroke, boxWidth), boxHeight), paint);
                            canvas.DrawRect(new SKRect(Math.Max(0, boxWidth - stroke), 0, boxWidth, boxHeight), paint);
                        }
                        else
                        {
                            using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
                            path.AddRoundRect(box, radius, radius);
                            var inner = box;
                            inner.Inflate(-stroke, -stroke);
                            float innerRadius = Math.Max(0, radius - stroke);
                            if (inner.Width > 0 && inner.Height > 0) path.AddRoundRect(inner, innerRadius, innerRadius);
                            canvas.DrawPath(path, paint);
                        }
                    }
                    return;
            }
        };
    }

    private static void DrawLine(SKCanvas canvas, LineOrientation orientation, int w, int h, int stroke, SKColor color)
    {
        switch (orientation)
        {
            case LineOrientation.Horizontal:
            {
                using var paint = RasterHelpers.FillPaint(color, antialias: false);
                float top = MathF.Round((h - stroke) / 2f);
                canvas.DrawRect(new SKRect(0, top, w, top + stroke), paint);
                break;
            }
            case LineOrientation.Vertical:
            {
                using var paint = RasterHelpers.FillPaint(color, antialias: false);
                float left = MathF.Round((w - stroke) / 2f);
                canvas.DrawRect(new SKRect(left, 0, left + stroke, h), paint);
                break;
            }
            default:
            {
                using var paint = new SKPaint
                {
                    Color = color,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = stroke,
                    StrokeCap = SKStrokeCap.Butt,
                    IsAntialias = true,
                };
                canvas.Save();
                canvas.ClipRect(new SKRect(0, 0, w, h));
                if (orientation == LineOrientation.DiagonalDown) canvas.DrawLine(0, 0, w, h, paint);
                else canvas.DrawLine(0, h, w, 0, paint);
                canvas.Restore();
                break;
            }
        }
    }
}

/// <summary>Tasarım sırasında gösterilen yer tutucu (doldurulmamış alan, eksik görsel, hatalı barkod).</summary>
internal static class PlaceholderRenderer
{
    public static Action<SKCanvas> Prepare(string label, int w, int h, FontProvider fonts, bool cross)
    {
        return canvas =>
        {
            using var white = RasterHelpers.FillPaint(RasterHelpers.White, antialias: false);
            canvas.DrawRect(new SKRect(0, 0, w, h), white);
            using var black = RasterHelpers.FillPaint(RasterHelpers.Black, antialias: false);
            int t = Math.Max(1, Math.Min(w, h) / 60);
            canvas.DrawRect(new SKRect(0, 0, w, t), black);
            canvas.DrawRect(new SKRect(0, h - t, w, h), black);
            canvas.DrawRect(new SKRect(0, 0, t, h), black);
            canvas.DrawRect(new SKRect(w - t, 0, w, h), black);
            if (cross)
            {
                using var line = new SKPaint { Color = RasterHelpers.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false };
                canvas.DrawLine(0, 0, w, h, line);
                canvas.DrawLine(0, h, w, 0, line);
            }
            else
            {
                // Seyrek tarama deseni: değişken alan olduğunu belli eder.
                using var hatch = new SKPaint { Color = RasterHelpers.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false };
                int step = Math.Max(8, Math.Min(w, h) / 6);
                for (int d = -h; d < w; d += step) canvas.DrawLine(d, h, d + h, 0, hatch);
            }
            if (label.Length == 0) return;
            var font = fonts.Get(FontProvider.FallbackFamily, bold: true, italic: false);
            float size = Math.Clamp(Math.Min(h * 0.28f, w * 1.6f / Math.Max(4, label.Length)), 6f, 64f);
            using var skFont = font.CreateFont(size);
            float textWidth = skFont.MeasureText(label);
            float pad = size * 0.25f;
            var labelRect = new SKRect((w - textWidth) / 2f - pad, h / 2f - size * 0.65f, (w + textWidth) / 2f + pad, h / 2f + size * 0.55f);
            canvas.DrawRect(labelRect, white);
            canvas.DrawText(label, MathF.Round((w - textWidth) / 2f), MathF.Round(h / 2f + size * 0.35f), SKTextAlign.Left, skFont, black);
        };
    }
}
