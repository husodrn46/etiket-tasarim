using AkelEtiket.Core.Imaging;

namespace AkelEtiket.ImageProcessor.Internal;

/// <summary>Hata yayma çekirdeğinin bir dokunuşu (dx tarama yönüne göredir).</summary>
internal readonly record struct KernelTap(int Dx, int Dy, int Weight);

/// <summary>Hata yayma (error diffusion) çekirdeği.</summary>
internal sealed class DiffusionKernel
{
    public DiffusionKernel(string name, int divisor, params KernelTap[] taps)
    {
        Name = name;
        Divisor = divisor;
        Taps = taps;
        MaxDy = taps.Max(t => t.Dy);
    }

    public string Name { get; }
    public int Divisor { get; }
    public KernelTap[] Taps { get; }
    public int MaxDy { get; }

    /// <summary>Floyd–Steinberg (1976): 7/16, 3/16, 5/16, 1/16.</summary>
    public static readonly DiffusionKernel FloydSteinberg = new("Floyd–Steinberg", 16,
        new(1, 0, 7),
        new(-1, 1, 3), new(0, 1, 5), new(1, 1, 1));

    /// <summary>
    /// Stucki (1981): hatayı iki alt satıra 12 komşuya yayar. Floyd–Steinberg'e göre daha temiz,
    /// "solucan" deseni daha az ve kenarları daha keskin sonuç verir.
    /// </summary>
    public static readonly DiffusionKernel Stucki = new("Stucki", 42,
        new(1, 0, 8), new(2, 0, 4),
        new(-2, 1, 2), new(-1, 1, 4), new(0, 1, 8), new(1, 1, 4), new(2, 1, 2),
        new(-2, 2, 1), new(-1, 2, 2), new(0, 2, 4), new(1, 2, 2), new(2, 2, 1));
}

/// <summary>
/// 1-bit dönüşüm algoritmaları. Tüm algoritmalar tam sayı aritmetiği kullanır; aynı giriş
/// her zaman aynı bitleri üretir. Kapsanmayan (maske dışı) piksellere asla mürekkep konmaz
/// ve hata yaymada bu piksellere hata aktarılmaz.
/// </summary>
internal static class Dithering
{
    /// <summary>Klasik 8×8 Bayer eşik matrisi (0…63).</summary>
    private static readonly byte[] Bayer8 =
    [
        0, 32, 8, 40, 2, 34, 10, 42,
        48, 16, 56, 24, 50, 18, 58, 26,
        12, 44, 4, 36, 14, 46, 6, 38,
        60, 28, 52, 20, 62, 30, 54, 22,
        3, 35, 11, 43, 1, 33, 9, 41,
        51, 19, 59, 27, 49, 17, 57, 25,
        15, 47, 7, 39, 13, 45, 5, 37,
        63, 31, 55, 23, 61, 29, 53, 21,
    ];

    /// <summary>Parlaklık &lt; eşik → siyah.</summary>
    public static void Threshold(byte[] values, bool[]? covered, int width, int height, int threshold, MonoBitmap ink)
    {
        byte[] data = ink.Data;
        int stride = ink.Stride;
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            int outRow = y * stride;
            for (int x = 0; x < width; x++)
            {
                int i = row + x;
                if (values[i] < threshold && (covered is null || covered[i]))
                    data[outRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        }
    }

    /// <summary>Düzenli (Bayer 8×8) dithering. Desen, görüntünün kendi koordinatına bağlıdır.</summary>
    public static void Ordered(byte[] values, bool[]? covered, int width, int height, MonoBitmap ink)
    {
        byte[] data = ink.Data;
        int stride = ink.Stride;
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            int outRow = y * stride;
            int matrixRow = (y & 7) << 3;
            for (int x = 0; x < width; x++)
            {
                int i = row + x;
                if (covered is not null && !covered[i]) continue;
                int t = Bayer8[matrixRow + (x & 7)] * 4 + 2; // 2 … 254
                if (values[i] < t)
                    data[outRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        }
    }

    /// <summary>
    /// Yılan (serpantin) taramalı hata yayma. Eşik orta noktadır (128); ton kaydırması önceden
    /// değerlere uygulanmış olmalıdır. Kayan nokta yerine 8 bit kesirli tam sayı kullanılır.
    /// </summary>
    public static void ErrorDiffuse(byte[] values, bool[]? covered, int width, int height, DiffusionKernel kernel, MonoBitmap ink)
    {
        const int Pad = 2;
        const int Mid = 128 << 8;
        const int White = 255 << 8;
        int rowLength = width + 2 * Pad;
        int bufferRows = kernel.MaxDy + 1;
        var rows = new int[bufferRows][];
        for (int r = 0; r < bufferRows; r++) rows[r] = new int[rowLength];

        byte[] data = ink.Data;
        int stride = ink.Stride;
        KernelTap[] taps = kernel.Taps;
        int divisor = kernel.Divisor;

        for (int y = 0; y < height; y++)
        {
            bool forward = (y & 1) == 0;
            int dir = forward ? 1 : -1;
            int x = forward ? 0 : width - 1;
            int[] current = rows[0];
            int rowBase = y * width;
            int outRow = y * stride;

            for (int k = 0; k < width; k++, x += dir)
            {
                int i = rowBase + x;
                if (covered is not null && !covered[i]) continue;

                int value = (values[i] << 8) + current[x + Pad];
                int error;
                if (value < Mid)
                {
                    data[outRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                    error = value;
                }
                else
                {
                    error = value - White;
                }
                if (error == 0) continue;

                for (int t = 0; t < taps.Length; t++)
                {
                    KernelTap tap = taps[t];
                    int nx = x + tap.Dx * dir;
                    if ((uint)nx >= (uint)width) continue;
                    int ny = y + tap.Dy;
                    if (ny >= height) continue;
                    if (covered is not null && !covered[ny * width + nx]) continue;
                    rows[tap.Dy][nx + Pad] += error * tap.Weight / divisor;
                }
            }

            // Satır tamponlarını kaydır: en üstteki boşaltılıp en alta taşınır.
            int[] finished = rows[0];
            Array.Clear(finished);
            for (int r = 0; r < bufferRows - 1; r++) rows[r] = rows[r + 1];
            rows[bufferRows - 1] = finished;
        }
    }

    /// <summary>
    /// Tek başına kalmış (8 komşusunun tamamı zıt renkte olan) pikselleri temizler.
    /// İnce çizgiler ve köşeler korunur; yalnızca nokta gürültüsü (JPEG/tarama lekesi) silinir.
    /// </summary>
    public static void Despeckle(MonoBitmap ink, bool[]? covered, int width, int height)
    {
        // Bitleri bayt dizisine aç (1 = siyah); kararlar orijinal duruma göre verilir.
        var black = new byte[width * height];
        byte[] data = ink.Data;
        int stride = ink.Stride;
        for (int y = 0; y < height; y++)
        {
            int row = y * width, inRow = y * stride;
            for (int x = 0; x < width; x++)
                black[row + x] = (byte)((data[inRow + (x >> 3)] >> (7 - (x & 7))) & 1);
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (covered is not null && !covered[i]) continue;
                byte value = black[i];
                int valid = 0, same = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = y + dy;
                    if ((uint)ny >= (uint)height) continue;
                    int nRow = ny * width;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx;
                        if ((uint)nx >= (uint)width) continue;
                        int j = nRow + nx;
                        if (covered is not null && !covered[j]) continue;
                        valid++;
                        if (black[j] == value) same++;
                    }
                }
                if (valid >= 5 && same == 0) ink.SetPixel(x, y, value == 0);
            }
        }
    }
}
