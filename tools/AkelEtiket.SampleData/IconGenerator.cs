using System.Buffers.Binary;
using SkiaSharp;

namespace AkelEtiket.SampleData;

/// <summary>Uygulama ikonunu (çok boyutlu .ico + 256 px PNG) vektörel olarak çizer.</summary>
internal static class IconGenerator
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    public static void Write(string assetsDirectory)
    {
        Directory.CreateDirectory(assetsDirectory);
        var images = Sizes.Select(size => (Size: size, Png: Draw(size))).ToList();
        File.WriteAllBytes(Path.Combine(assetsDirectory, "AkelEtiket-256.png"), images[^1].Png);
        File.WriteAllBytes(Path.Combine(assetsDirectory, "AkelEtiket.ico"), BuildIco(images));
        Console.WriteLine($"İkon yazıldı: {assetsDirectory}");
    }

    private static byte[] Draw(int size)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(bitmap);
        float s = size;
        float pad = s * 0.04f;
        var background = new SKRect(pad, pad, s - pad, s - pad);
        using (var paint = new SKPaint { IsAntialias = true })
        {
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, s),
                [new SKColor(0xDE, 0x2A, 0x33), new SKColor(0xA8, 0x12, 0x1B)], SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(background, s * 0.22f, s * 0.22f, paint);
        }

        // Beyaz etiket kartı
        var label = new SKRect(s * 0.16f, s * 0.24f, s * 0.84f, s * 0.78f);
        using (var white = new SKPaint { Color = SKColors.White, IsAntialias = true })
            canvas.DrawRoundRect(label, s * 0.07f, s * 0.07f, white);

        // Barkod çubukları (küçük boyutlarda sadeleşir)
        int[] pattern = size <= 24 ? [2, 1, 1, 2, 1, 1, 2] : [2, 1, 1, 3, 1, 2, 1, 1, 3, 1, 2];
        float barsLeft = label.Left + label.Width * 0.14f, barsRight = label.Right - label.Width * 0.14f;
        float unit = (barsRight - barsLeft) / (pattern.Sum() + pattern.Length - 1);
        float top = label.Top + label.Height * 0.18f;
        float bottom = label.Bottom - label.Height * (size <= 32 ? 0.18f : 0.34f);
        float x = barsLeft;
        using (var black = new SKPaint { Color = new SKColor(0x1C, 0x1C, 0x1E), IsAntialias = size > 32 })
        {
            foreach (int w in pattern)
            {
                float barWidth = Math.Max(1, MathF.Round(w * unit));
                canvas.DrawRect(new SKRect(MathF.Round(x), MathF.Round(top), MathF.Round(x) + barWidth, MathF.Round(bottom)), black);
                x += (w + 1) * unit;
            }
            if (size > 32)
            {
                float lineTop = label.Bottom - label.Height * 0.24f;
                canvas.DrawRoundRect(new SKRect(barsLeft, lineTop, barsRight, lineTop + label.Height * 0.09f), 2, 2, black);
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>PNG sıkıştırmalı ICO (Windows Vista ve sonrası).</summary>
    private static byte[] BuildIco(List<(int Size, byte[] Png)> images)
    {
        using var stream = new MemoryStream();
        var header = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), (ushort)images.Count);
        stream.Write(header);
        int offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            var entry = new byte[16];
            entry[0] = (byte)(size >= 256 ? 0 : size);
            entry[1] = (byte)(size >= 256 ? 0 : size);
            BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(6), 32);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(8), (uint)png.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(12), (uint)offset);
            stream.Write(entry);
            offset += png.Length;
        }
        foreach (var (_, png) in images) stream.Write(png);
        return stream.ToArray();
    }
}
