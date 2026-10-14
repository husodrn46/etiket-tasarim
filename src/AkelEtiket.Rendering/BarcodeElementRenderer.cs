using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core;
using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;
using SkiaSharp;

namespace AkelEtiket.Rendering;

/// <summary>
/// Barkod öğesi: çubuklar doğrudan 1-bit bitmap'e tam sayı nokta olarak yazılır (yumuşatma/dithering yok).
/// Kutunun tamamı opak beyazdır; böylece sessiz bölgeler altta kalan öğelerden etkilenmez.
/// </summary>
internal static class BarcodeElementRenderer
{
    public sealed record Output(MonoBitmap Ink, BarcodeRenderInfo Info, string? Error, IReadOnlyList<string> Warnings);

    public static Output Render(BarcodeElement element, string content, int boxWidth, int boxHeight, double dotsPerMm, FontProvider fonts)
    {
        var settings = BarcodeSettings.From(element);
        var encode = BarcodeEncoder.Encode(element.Symbology, content, settings);
        if (!encode.Success)
        {
            var info = new BarcodeRenderInfo { Validation = encode.Validation };
            return new Output(new MonoBitmap(boxWidth, boxHeight), info, encode.Error ?? "Barkod oluşturulamadı.", []);
        }

        int? textHeight = element.TextSize > 0
            ? Math.Max(6, Units.MmToDotsRounded(Units.PointsToMm(element.TextSize), dotsPerMm))
            : null;
        var options = new BarcodeLayoutOptions
        {
            ShowText = element.ShowText,
            IncludeQuietZone = element.QuietZone,
            TextHeightDots = textHeight,
            DotsPerMm = dotsPerMm,
        };
        var layout = BarcodeLayoutEngine.Layout(encode.Symbol!, boxWidth, boxHeight, options);
        var renderInfo = new BarcodeRenderInfo
        {
            Validation = encode.Validation,
            Layout = layout,
            ModuleMm = layout.ModuleDots / dotsPerMm,
            Symbol = encode.Symbol,
        };
        if (!layout.Fits)
            return new Output(new MonoBitmap(boxWidth, boxHeight), renderInfo, layout.Problem, []);

        var ink = new MonoBitmap(boxWidth, boxHeight);
        foreach (var bar in layout.Bars) ink.FillRect(bar.X, bar.Y, bar.Width, bar.Height, true);

        if (layout.Texts.Count > 0)
            DrawHumanReadable(ink, layout, element.TextFontFamily, fonts);

        var warnings = new List<string>(encode.Validation.Warnings);
        warnings.AddRange(layout.Warnings);
        return new Output(ink, renderInfo, null, warnings);
    }

    private static void DrawHumanReadable(MonoBitmap ink, BarcodeLayout layout, string family, FontProvider fonts)
    {
        var font = fonts.Get(family, bold: false, italic: false);
        using var bitmap = RasterHelpers.CreateSurfaceBitmap(ink.Width, ink.Height);
        using (var canvas = new SKCanvas(bitmap))
        {
            // Tüm parçalar için ortak punto: kutu yüksekliğine göre, en dar kutuya sığacak şekilde.
            float size = float.MaxValue;
            using (var probe = font.CreateFont(100))
            {
                var metrics = probe.Metrics;
                float digitHeight = metrics.CapHeight > 0 ? metrics.CapHeight : 72f;
                foreach (var t in layout.Texts)
                {
                    float byHeight = t.Box.Height * 0.92f / (digitHeight / 100f) * (t.Small ? 0.85f : 1f);
                    float width = probe.MeasureText(t.Text);
                    float byWidth = width > 0 ? t.Box.Width / (width / 100f) : byHeight;
                    size = Math.Min(size, Math.Min(byHeight, byWidth));
                }
            }
            size = Math.Max(4f, size);
            using var skFont = font.CreateFont(size);
            using var smallFont = font.CreateFont(size * 0.8f);
            using var paint = RasterHelpers.FillPaint(RasterHelpers.Black, antialias: false);
            foreach (var t in layout.Texts)
            {
                var f = t.Small ? smallFont : skFont;
                float width = f.MeasureText(t.Text);
                float capHeight = f.Metrics.CapHeight > 0 ? f.Metrics.CapHeight : f.Size * 0.72f;
                float x = MathF.Round(t.Box.X + (t.Box.Width - width) / 2f);
                float baseline = MathF.Round(t.Box.Y + (t.Box.Height + capHeight) / 2f);
                canvas.Save();
                canvas.ClipRect(new SKRect(t.Box.X, t.Box.Y, t.Box.Right, t.Box.Bottom));
                canvas.DrawText(t.Text, x, baseline, SKTextAlign.Left, f, paint);
                canvas.Restore();
            }
        }
        var (textInk, _) = RasterHelpers.ThresholdRgba(bitmap);
        // Metin pikselleri siyaha eklenir (barkod zemini zaten opak beyaz).
        for (int i = 0; i < ink.Data.Length; i++) ink.Data[i] |= textInk.Data[i];
    }
}
