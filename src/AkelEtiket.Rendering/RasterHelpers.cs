using AkelEtiket.Core.Imaging;
using SkiaSharp;

namespace AkelEtiket.Rendering;

/// <summary>Yerel (döndürülmemiş) veya döndürülmüş 1-bit katman.</summary>
internal readonly record struct LocalLayer(MonoBitmap Ink, MonoBitmap? Mask, int OffsetX, int OffsetY);

/// <summary>Skia yüzeyi ↔ 1-bit dönüşümleri ve döndürme yardımcıları.</summary>
internal static class RasterHelpers
{
    public static readonly SKColor Black = new(0, 0, 0, 255);
    public static readonly SKColor White = new(255, 255, 255, 255);

    /// <summary>Saydam RGBA (premultiplied) çizim yüzeyi oluşturur.</summary>
    public static SKBitmap CreateSurfaceBitmap(int width, int height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(Math.Max(1, width), Math.Max(1, height), SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        return bitmap;
    }

    /// <summary>
    /// RGBA yüzeyini eşikler: alfa ≥ %50 olan pikseller "boyanmış" (maske), bunlardan gri değeri %50'nin altında
    /// olanlar siyah (mürekkep). Vektör içerik (metin, şekil) için kullanılır; görseller dithering ile işlenir.
    /// </summary>
    public static (MonoBitmap Ink, MonoBitmap Mask) ThresholdRgba(SKBitmap bitmap)
    {
        int w = bitmap.Width, h = bitmap.Height;
        var ink = new MonoBitmap(w, h);
        var mask = new MonoBitmap(w, h);
        ReadOnlySpan<byte> px = bitmap.GetPixelSpan();
        int rowBytes = bitmap.RowBytes;
        for (int y = 0; y < h; y++)
        {
            int row = y * rowBytes;
            for (int x = 0; x < w; x++)
            {
                int i = row + x * 4;
                int a = px[i + 3];
                if (a < 128) continue;
                mask.SetPixel(x, y, true);
                // Premultiplied gri: (r*299 + g*587 + b*114)/1000 ; ön çarpımsız gri < 128 ⇔ 2·griₚ < a
                int grayPremul = (px[i] * 299 + px[i + 1] * 587 + px[i + 2] * 114) / 1000;
                if (grayPremul * 2 < a) ink.SetPixel(x, y, true);
            }
        }
        return (ink, mask);
    }

    /// <summary>Katmanı saat yönünde çeyrek turlarla kayıpsız döndürür ve kutu merkezine göre konumlar.</summary>
    public static LocalLayer RotateQuarter(MonoBitmap ink, MonoBitmap? mask, int boxWidth, int boxHeight, int quarterTurns)
    {
        if (quarterTurns == 0) return new LocalLayer(ink, mask, 0, 0);
        var rInk = ink.RotateQuarterTurns(quarterTurns);
        var rMask = mask?.RotateQuarterTurns(quarterTurns);
        // Merkez korunur: yeni sol üst = merkez − yeni boyut/2
        int offsetX = (int)Math.Floor(boxWidth / 2.0 - rInk.Width / 2.0 + 0.5);
        int offsetY = (int)Math.Floor(boxHeight / 2.0 - rInk.Height / 2.0 + 0.5);
        return new LocalLayer(rInk, rMask, offsetX, offsetY);
    }

    /// <summary>Döndürülmüş kutunun sınır kutusu (yerel kutu orijinine göre, nokta) ve çizim dönüşümü.</summary>
    public static (int OffsetX, int OffsetY, int Width, int Height, SKMatrix Transform) GetRotatedFrame(int boxWidth, int boxHeight, double rotationDegrees)
    {
        double rad = rotationDegrees * Math.PI / 180.0;
        double cos = Math.Abs(Math.Cos(rad)), sin = Math.Abs(Math.Sin(rad));
        double halfW = (boxWidth * cos + boxHeight * sin) / 2.0;
        double halfH = (boxWidth * sin + boxHeight * cos) / 2.0;
        double cx = boxWidth / 2.0, cy = boxHeight / 2.0;
        int offsetX = (int)Math.Floor(cx - halfW) - 1;
        int offsetY = (int)Math.Floor(cy - halfH) - 1;
        int width = (int)Math.Ceiling(cx + halfW) + 1 - offsetX;
        int height = (int)Math.Ceiling(cy + halfH) + 1 - offsetY;
        // Katman koordinatı = yerel koordinat → merkez etrafında döndür → katman orijinine kaydır
        var transform = SKMatrix.CreateTranslation((float)(cx - offsetX), (float)(cy - offsetY));
        transform = transform.PreConcat(SKMatrix.CreateRotationDegrees((float)rotationDegrees));
        transform = transform.PreConcat(SKMatrix.CreateTranslation((float)-cx, (float)-cy));
        return (offsetX, offsetY, Math.Max(1, width), Math.Max(1, height), transform);
    }

    /// <summary>Boş maske/mürekkep kontrolü: hiçbir piksel boyanmıyorsa katman gereksizdir.</summary>
    public static bool IsEmpty(MonoBitmap ink, MonoBitmap? mask) =>
        mask is not null ? mask.CountBlack() == 0 : false;

    public static SKPaint FillPaint(SKColor color, bool antialias = true) => new()
    {
        Color = color,
        Style = SKPaintStyle.Fill,
        IsAntialias = antialias,
    };
}
