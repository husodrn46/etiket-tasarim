using SkiaSharp;

namespace AkelEtiket.ImageProcessor.Tests;

/// <summary>Testler için sentetik görsel üreticileri ve kodlayıcılar.</summary>
internal static class TestImages
{
    public static SKBitmap CreateBitmap(int width, int height, SKColor background, Action<SKCanvas>? draw = null)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(background);
        draw?.Invoke(canvas);
        canvas.Flush();
        return bitmap;
    }

    public static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality = 95)
    {
        using var data = bitmap.Encode(format, quality);
        return data.ToArray();
    }

    /// <summary>24 bit, alttan üste BMP (Skia BMP kodlayamadığı için elle yazılır).</summary>
    public static byte[] EncodeBmp24(SKBitmap bitmap)
    {
        int w = bitmap.Width, h = bitmap.Height;
        int rowSize = (w * 3 + 3) & ~3;
        int dataSize = rowSize * h;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((byte)'B');
        bw.Write((byte)'M');
        bw.Write(54 + dataSize);
        bw.Write(0);
        bw.Write(54);
        bw.Write(40);
        bw.Write(w);
        bw.Write(h);
        bw.Write((short)1);
        bw.Write((short)24);
        bw.Write(0);
        bw.Write(dataSize);
        bw.Write(2835);
        bw.Write(2835);
        bw.Write(0);
        bw.Write(0);
        var padding = new byte[rowSize - w * 3];
        for (int y = h - 1; y >= 0; y--)
        {
            for (int x = 0; x < w; x++)
            {
                SKColor c = bitmap.GetPixel(x, y);
                bw.Write(c.Blue);
                bw.Write(c.Green);
                bw.Write(c.Red);
            }
            bw.Write(padding);
        }
        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>JPEG'e EXIF yön etiketi (0x0112) içeren APP1 segmenti ekler.</summary>
    public static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
    {
        byte[] app1 =
        [
            0xFF, 0xE1, 0x00, 0x22,
            0x45, 0x78, 0x69, 0x66, 0x00, 0x00,             // "Exif\0\0"
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, // TIFF LE, IFD0 @ 8
            0x01, 0x00,                                     // 1 kayıt
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, // Orientation, SHORT, 1
            (byte)(orientation & 0xFF), (byte)(orientation >> 8), 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,                         // sonraki IFD yok
        ];
        var result = new byte[jpeg.Length + app1.Length];
        result[0] = jpeg[0];
        result[1] = jpeg[1];
        Buffer.BlockCopy(app1, 0, result, 2, app1.Length);
        Buffer.BlockCopy(jpeg, 2, result, 2 + app1.Length, jpeg.Length - 2);
        return result;
    }

    /// <summary>1×1 siyah GIF89a (elle hazırlanmış).</summary>
    public static readonly byte[] BlackPixelGif =
    [
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
        0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        0x02, 0x02, 0x44, 0x01, 0x00, 0x3B,
    ];

    /// <summary>
    /// Görseli, Rendering modülünün yaptığı gibi saydam bir tuvale beyaz alt zeminle çizip
    /// gri görüntüye çevirir.
    /// </summary>
    public static GrayImage RenderLikeRendering(LoadedImage image, int width, int height, SKRect? dest = null)
    {
        SKRect target = dest ?? new SKRect(0, 0, width, height);
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var white = new SKPaint { Color = SKColors.White, IsAntialias = false };
            canvas.DrawRect(target, white);
            image.Draw(canvas, new SKRect(0, 0, image.Width, image.Height), target);
            canvas.Flush();
        }
        return GrayImage.FromBitmap(bitmap);
    }

    /// <summary>Yatay gri geçiş (soldan sağa 0 → 255).</summary>
    public static GrayImage HorizontalGradient(int width, int height)
    {
        var pixels = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = (byte)Math.Round(x * 255.0 / (width - 1));
        return new GrayImage(width, height, pixels);
    }

    public static GrayImage Uniform(int width, int height, byte value)
    {
        var pixels = new byte[width * height];
        Array.Fill(pixels, value);
        return new GrayImage(width, height, pixels);
    }

    /// <summary>Fotoğraf benzeri: yumuşak dalgalı ton + hafif gürültü (belirlenimci).</summary>
    public static GrayImage PhotoLike(int width, int height, int seed = 1, int noise = 8)
    {
        var random = new Random(seed);
        var pixels = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                double v = 128 + 80 * Math.Sin(x / 40.0) * Math.Cos(y / 30.0) + 20 * Math.Sin((x + y) / 90.0);
                v += random.Next(-noise, noise + 1);
                pixels[y * width + x] = (byte)Math.Clamp((int)Math.Round(v), 0, 255);
            }
        return new GrayImage(width, height, pixels);
    }

    /// <summary>Logo benzeri: beyaz zeminde kenar yumuşatmalı siyah yazı ve şekiller.</summary>
    public static SKBitmap LogoBitmap(int width = 400, int height = 200, bool transparentBackground = false)
    {
        return CreateBitmap(width, height, transparentBackground ? SKColors.Transparent : SKColors.White, canvas =>
        {
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var font = new SKFont(SKTypeface.Default, height * 0.35f);
            canvas.DrawText("AKEL", width * 0.05f, height * 0.45f, SKTextAlign.Left, font, paint);
            canvas.DrawCircle(width * 0.8f, height * 0.3f, height * 0.18f, paint);
            canvas.DrawRect(new SKRect(width * 0.05f, height * 0.6f, width * 0.95f, height * 0.68f), paint);
            using var stroke = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3 };
            canvas.DrawRoundRect(new SKRect(width * 0.1f, height * 0.75f, width * 0.6f, height * 0.95f), 10, 10, stroke);
        });
    }

    /// <summary>Düz renkli (kırmızı, mavi, sarı) kenar yumuşatmalı logo.</summary>
    public static SKBitmap ColorLogoBitmap(int width = 400, int height = 200)
    {
        return CreateBitmap(width, height, SKColors.White, canvas =>
        {
            using var red = new SKPaint { Color = new SKColor(220, 20, 30), IsAntialias = true };
            using var blue = new SKPaint { Color = new SKColor(20, 60, 200), IsAntialias = true };
            using var yellow = new SKPaint { Color = new SKColor(250, 210, 0), IsAntialias = true };
            canvas.DrawCircle(width * 0.25f, height * 0.5f, height * 0.35f, red);
            canvas.DrawRect(new SKRect(width * 0.45f, height * 0.15f, width * 0.7f, height * 0.85f), blue);
            using var path = new SKPath();
            path.MoveTo(width * 0.75f, height * 0.85f);
            path.LineTo(width * 0.95f, height * 0.85f);
            path.LineTo(width * 0.85f, height * 0.15f);
            path.Close();
            canvas.DrawPath(path, yellow);
        });
    }

    public static SKBitmap PhotoLikeBitmap(int width, int height, int seed = 3)
    {
        GrayImage gray = PhotoLike(width, height, seed);
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var random = new Random(seed + 100);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                byte v = gray.Pixels[y * width + x];
                // Hafif renk farkı: fotoğrafa benzesin
                byte r = (byte)Math.Clamp(v + 20, 0, 255), g = v, b = (byte)Math.Clamp(v - 15 + random.Next(-3, 4), 0, 255);
                bitmap.SetPixel(x, y, new SKColor(r, g, b));
            }
        return bitmap;
    }

    public static int CountBlack(AkelEtiket.Core.Imaging.MonoBitmap bitmap, int x0, int y0, int x1, int y1)
    {
        int count = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (bitmap[x, y]) count++;
        return count;
    }
}
