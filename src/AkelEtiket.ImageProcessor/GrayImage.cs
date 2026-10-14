using SkiaSharp;

namespace AkelEtiket.ImageProcessor;

/// <summary>
/// 8-bit gri tonlu görüntü (0 = siyah … 255 = beyaz) ve isteğe bağlı kapsama (alfa) maskesi.
/// Kapsama 0…255'tir; null ise tüm pikseller kapsanmış (opak) kabul edilir.
/// </summary>
public sealed class GrayImage
{
    public GrayImage(int width, int height, byte[] pixels, byte[]? coverage = null)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Genişlik sıfırdan büyük olmalı.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Yükseklik sıfırdan büyük olmalı.");
        ArgumentNullException.ThrowIfNull(pixels);
        long count = (long)width * height;
        if (pixels.LongLength != count)
            throw new ArgumentException($"Piksel dizisi {count} eleman olmalı, {pixels.LongLength} verildi.", nameof(pixels));
        if (coverage is not null && coverage.LongLength != count)
            throw new ArgumentException($"Kapsama dizisi {count} eleman olmalı, {coverage.LongLength} verildi.", nameof(coverage));
        Width = width;
        Height = height;
        Pixels = pixels;
        Coverage = coverage;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Satır sıralı gri değerler (0 = siyah, 255 = beyaz).</summary>
    public byte[] Pixels { get; }

    /// <summary>Satır sıralı kapsama/alfa (0…255) ya da null (tamamen opak).</summary>
    public byte[]? Coverage { get; }

    /// <summary>Piksel öğe tarafından örtülüyor mu? (kapsama ≥ 128)</summary>
    public bool IsCovered(int index) => Coverage is null || Coverage[index] >= 128;

    /// <summary>
    /// Skia bitmap'ini gri tona çevirir. Saydam pikseller BEYAZ zemin üzerine bindirilir
    /// (tamamen saydam siyah piksel bile beyaz olur, asla mürekkep olmaz); kapsama = alfa.
    /// Tüm pikseller opaksa <see cref="Coverage"/> null olur.
    /// Parlaklık: Rec.601 luma (0,299 R + 0,587 G + 0,114 B).
    /// </summary>
    public static GrayImage FromBitmap(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            throw new ArgumentException("Boş bitmap gri tona çevrilemez.", nameof(bitmap));

        switch (bitmap.ColorType)
        {
            case SKColorType.Bgra8888:
                return FromRgba32(bitmap, redOffset: 2, blueOffset: 0);
            case SKColorType.Rgba8888:
                return FromRgba32(bitmap, redOffset: 0, blueOffset: 2);
            case SKColorType.Gray8:
                return FromGray8(bitmap);
            default:
                using (SKBitmap converted = bitmap.Copy(SKColorType.Bgra8888)
                    ?? throw new ArgumentException($"Desteklenmeyen renk biçimi: {bitmap.ColorType}.", nameof(bitmap)))
                {
                    return FromRgba32(converted, redOffset: 2, blueOffset: 0);
                }
        }
    }

    private static GrayImage FromRgba32(SKBitmap bitmap, int redOffset, int blueOffset)
    {
        int w = bitmap.Width, h = bitmap.Height, rowBytes = bitmap.RowBytes;
        ReadOnlySpan<byte> src = bitmap.GetPixelSpan();
        var gray = new byte[w * h];
        var coverage = new byte[w * h];
        bool opaqueType = bitmap.AlphaType == SKAlphaType.Opaque;
        bool premul = bitmap.AlphaType == SKAlphaType.Premul;
        bool anyTransparent = false;

        for (int y = 0; y < h; y++)
        {
            ReadOnlySpan<byte> row = src.Slice(y * rowBytes, w * 4);
            int o = y * w;
            for (int x = 0, p = 0; x < w; x++, p += 4)
            {
                int r = row[p + redOffset], g = row[p + 1], b = row[p + blueOffset];
                int a = opaqueType ? 255 : row[p + 3];
                int luma = (77 * r + 150 * g + 29 * b + 128) >> 8;
                int value;
                if (a == 255)
                {
                    value = luma;
                }
                else if (premul)
                {
                    // Önçarpımlı renk beyaz zemine: C·α + 255·(1−α) = Cp + (255 − α)
                    value = luma + (255 - a);
                }
                else
                {
                    value = (luma * a + 255 * (255 - a) + 127) / 255;
                }
                gray[o + x] = (byte)Math.Clamp(value, 0, 255);
                coverage[o + x] = (byte)a;
                if (a != 255) anyTransparent = true;
            }
        }
        return new GrayImage(w, h, gray, anyTransparent ? coverage : null);
    }

    private static GrayImage FromGray8(SKBitmap bitmap)
    {
        int w = bitmap.Width, h = bitmap.Height, rowBytes = bitmap.RowBytes;
        ReadOnlySpan<byte> src = bitmap.GetPixelSpan();
        var gray = new byte[w * h];
        for (int y = 0; y < h; y++)
            src.Slice(y * rowBytes, w).CopyTo(gray.AsSpan(y * w, w));
        return new GrayImage(w, h, gray);
    }
}
