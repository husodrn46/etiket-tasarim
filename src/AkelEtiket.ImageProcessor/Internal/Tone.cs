namespace AkelEtiket.ImageProcessor.Internal;

/// <summary>Ton eğrileri (parlaklık, kontrast, kaydırma, gama) ve keskinleştirme.</summary>
internal static class Tone
{
    /// <summary>
    /// Parlaklık (−100…+100 → −128…+128 kaydırma) ve kontrast (−100…+100) eğrisi.
    /// Kontrast standart formülle 128 etrafında uygulanır: f = 259(c+255) / (255(259−c)).
    /// </summary>
    public static byte[] BrightnessContrast(int brightness, int contrast)
    {
        double offset = Math.Clamp(brightness, -100, 100) * 1.28;
        double c = Math.Clamp(contrast, -100, 100) * 2.55;
        double factor = 259.0 * (c + 255.0) / (255.0 * (259.0 - c));
        var lut = new byte[256];
        for (int v = 0; v < 256; v++)
            lut[v] = ClampToByte(factor * (v - 128) + 128 + offset);
        return lut;
    }

    /// <summary>Gama eğrisi: çıkış = 255·(v/255)^üs. Üs &lt; 1 ara tonları açar.</summary>
    public static byte[] Gamma(double exponent)
    {
        var lut = new byte[256];
        for (int v = 0; v < 256; v++)
            lut[v] = ClampToByte(255.0 * Math.Pow(v / 255.0, exponent));
        return lut;
    }

    /// <summary>Tonu koyulaştırır (bias &gt; 0) veya açar (bias &lt; 0): v − bias.</summary>
    public static byte[] Shift(int bias)
    {
        var lut = new byte[256];
        for (int v = 0; v < 256; v++)
            lut[v] = (byte)Math.Clamp(v - bias, 0, 255);
        return lut;
    }

    public static bool IsIdentity(byte[] lut)
    {
        for (int v = 0; v < 256; v++)
            if (lut[v] != v) return false;
        return true;
    }

    public static void Apply(byte[] values, byte[] lut)
    {
        for (int i = 0; i < values.Length; i++) values[i] = lut[values[i]];
    }

    /// <summary>
    /// Kapsama duyarlı keskinleştirme (unsharp mask): v + miktar·(v − bulanık).
    /// Bulanıklık ayrılabilir [1 2 1] çekirdeğidir; maske dışındaki pikseller hesaba katılmaz,
    /// böylece öğe kenarında hale (halo) oluşmaz. Miktar = pay / 16.
    /// </summary>
    public static byte[] Sharpen(byte[] values, bool[]? covered, int width, int height, int amountSixteenths)
    {
        int n = width * height;
        var horizontal = new int[n]; // ×16 ölçekli
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int i = row + x;
                if (covered is not null && !covered[i]) continue;
                int sum = values[i] * 2, weight = 2;
                if (x > 0 && (covered is null || covered[i - 1])) { sum += values[i - 1]; weight++; }
                if (x < width - 1 && (covered is null || covered[i + 1])) { sum += values[i + 1]; weight++; }
                horizontal[i] = sum * 16 / weight;
            }
        }

        var result = new byte[n];
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int i = row + x;
                if (covered is not null && !covered[i])
                {
                    result[i] = values[i];
                    continue;
                }
                int sum = horizontal[i] * 2, weight = 2;
                if (y > 0 && (covered is null || covered[i - width])) { sum += horizontal[i - width]; weight++; }
                if (y < height - 1 && (covered is null || covered[i + width])) { sum += horizontal[i + width]; weight++; }
                int blurred16 = sum / weight;
                int detail16 = values[i] * 16 - blurred16;
                int sharpened = values[i] + detail16 * amountSixteenths / 256;
                result[i] = (byte)Math.Clamp(sharpened, 0, 255);
            }
        }
        return result;
    }

    private static byte ClampToByte(double value) => (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
}
