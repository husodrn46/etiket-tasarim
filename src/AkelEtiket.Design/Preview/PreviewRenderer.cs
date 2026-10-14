using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Geometry;
using AkelEtiket.Core.Imaging;
using AkelEtiket.Rendering;
using SkiaSharp;

namespace AkelEtiket.Design.Preview;

/// <summary>Etiket önizlemesi: ortak render motorunun 1-bit çıktısı (baskıyla aynı) ve isteğe bağlı açıklamalar.</summary>
public static class PreviewRenderer
{
    public const double PrintDotsPerMm = 8.0;

    public static LabelRenderResult Render(LabelDocument document, AssetStore assets, IFieldValueSource? fields, IImageSourceResolver? images,
        RenderMode mode = RenderMode.Design, double dotsPerMm = PrintDotsPerMm)
    {
        using var renderer = new LabelRenderer(assets);
        return renderer.RenderLabel(document, new RenderOptions { DotsPerMm = dotsPerMm, Fields = fields, Mode = mode, ImageResolver = images });
    }

    /// <summary>1-bit PNG (baskı çözünürlüğünde, isteğe bağlı tam sayı büyütmeyle).</summary>
    public static byte[] EncodeMono(MonoBitmap bitmap, double dotsPerMm, int scale = 1)
    {
        scale = Math.Clamp(scale, 1, 4);
        if (scale == 1) return MonoImageExport.EncodePng(bitmap, dotsPerMm * 25.4);
        using var skBitmap = ToSkBitmap(bitmap, scale);
        return Encode(skBitmap);
    }

    /// <summary>Öğe sınırlarını, kimliklerini ve sıralarını renkli çizen açıklamalı önizleme (yerleşimi anlamak için; baskıda yoktur).</summary>
    public static byte[] EncodeAnnotated(LabelRenderResult render, LabelDocument document, int scale = 2)
    {
        scale = Math.Clamp(scale, 1, 4);
        double dpm = render.DotsPerMm * scale;
        using var bitmap = ToSkBitmap(render.Bitmap, scale);
        using var canvas = new SKCanvas(bitmap);
        using var font = new SKFont(SKTypeface.Default, (float)Math.Max(10, 2.2 * dpm / 2));
        SKColor[] palette =
        [
            new(220, 38, 38), new(37, 99, 235), new(22, 163, 74), new(217, 119, 6),
            new(147, 51, 234), new(8, 145, 178), new(219, 39, 119),
        ];
        for (int i = 0; i < document.Elements.Count; i++)
        {
            var element = document.Elements[i];
            var color = element.Visible ? palette[i % palette.Length] : new SKColor(150, 150, 150);
            using var stroke = new SKPaint { Color = color.WithAlpha(220), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
            var corners = ElementGeometry.GetCornersMm(element);
            using (var path = new SKPath())
            {
                path.MoveTo((float)(corners[0].X * dpm), (float)(corners[0].Y * dpm));
                for (int c = 1; c < corners.Length; c++) path.LineTo((float)(corners[c].X * dpm), (float)(corners[c].Y * dpm));
                path.Close();
                canvas.DrawPath(path, stroke);
            }
            string label = $"{i}:{element.Id}" + (string.IsNullOrWhiteSpace(element.Name) ? "" : $" {Truncate(element.Name, 18)}");
            float tx = (float)(Math.Max(0, corners.Min(c => c.X)) * dpm) + 2;
            float ty = (float)(Math.Max(0, corners.Min(c => c.Y)) * dpm) + font.Size;
            float width = font.MeasureText(label);
            using var bg = new SKPaint { Color = color.WithAlpha(200), Style = SKPaintStyle.Fill };
            canvas.DrawRect(tx - 2, ty - font.Size + 2, width + 4, font.Size + 2, bg);
            using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawText(label, tx, ty, SKTextAlign.Left, font, textPaint);
        }
        // Etiket kenarı
        using var border = new SKPaint { Color = new SKColor(100, 100, 100), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        canvas.DrawRect(0, 0, bitmap.Width - 1, bitmap.Height - 1, border);
        return Encode(bitmap);
    }

    /// <summary>Toplu önizleme: etiketler ızgara hâlinde, altlarında satır numarası; hatalı etiketler kırmızı çerçeveli.</summary>
    public static byte[] EncodeContactSheet(IReadOnlyList<(string Caption, MonoBitmap? Bitmap, bool HasError)> items, int labelWidthDots, int labelHeightDots,
        int columns = 4, double thumbScale = 0.5)
    {
        columns = Math.Clamp(columns, 1, 6);
        int thumbW = Math.Max(40, (int)(labelWidthDots * thumbScale));
        int thumbH = Math.Max(40, (int)(labelHeightDots * thumbScale));
        const int pad = 10, caption = 22;
        int rows = (items.Count + columns - 1) / columns;
        int width = pad + columns * (thumbW + pad);
        int height = pad + Math.Max(1, rows) * (thumbH + caption + pad);
        using var sheet = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(sheet);
        canvas.Clear(new SKColor(235, 235, 235));
        using var font = new SKFont(SKTypeface.Default, 14);
        using var text = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var errorPaint = new SKPaint { Color = new SKColor(220, 38, 38), Style = SKPaintStyle.Stroke, StrokeWidth = 4 };
        using var framePaint = new SKPaint { Color = new SKColor(160, 160, 160), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var sampling = new SKPaint { IsAntialias = true };
        for (int i = 0; i < items.Count; i++)
        {
            int col = i % columns, row = i / columns;
            int x = pad + col * (thumbW + pad), y = pad + row * (thumbH + caption + pad);
            var dest = new SKRect(x, y, x + thumbW, y + thumbH);
            if (items[i].Bitmap is { } mono)
            {
                using var label = ToSkBitmap(mono, 1);
                using var image = SKImage.FromBitmap(label);
                canvas.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), sampling);
            }
            else
            {
                using var empty = new SKPaint { Color = SKColors.White };
                canvas.DrawRect(dest, empty);
            }
            canvas.DrawRect(dest, items[i].HasError ? errorPaint : framePaint);
            canvas.DrawText(Truncate(items[i].Caption, 40), x, y + thumbH + 16, SKTextAlign.Left, font, text);
        }
        return Encode(sheet);
    }

    private static SKBitmap ToSkBitmap(MonoBitmap mono, int scale)
    {
        var bitmap = new SKBitmap(mono.Width * scale, mono.Height * scale, SKColorType.Rgba8888, SKAlphaType.Premul);
        var pixels = new int[bitmap.Width * bitmap.Height];
        const int white = unchecked((int)0xFFFFFFFF), black = unchecked((int)0xFF000000);
        for (int y = 0; y < mono.Height; y++)
        {
            for (int x = 0; x < mono.Width; x++)
            {
                int color = mono.GetPixel(x, y) ? black : white;
                for (int sy = 0; sy < scale; sy++)
                {
                    int row = (y * scale + sy) * bitmap.Width;
                    for (int sx = 0; sx < scale; sx++) pixels[row + x * scale + sx] = color;
                }
            }
        }
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        return bitmap;
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
