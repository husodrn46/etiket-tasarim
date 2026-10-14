using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;
using AkelEtiket.ImageProcessor.Internal;

namespace AkelEtiket.ImageProcessor;

/// <summary>
/// Gri tonlu görüntüyü termal yazıcı için 1-bit siyah-beyaza çevirir.
/// Akış: parlaklık/kontrast eğrisi → yöntem → (isteğe bağlı) ters çevirme (yalnızca maske içinde).
/// Sonuç belirlenimcidir: aynı giriş ve ayarlar her zaman aynı bitleri üretir; bu sayede ekran
/// önizlemesi ile baskı birebir aynıdır.
/// </summary>
public static class MonoConverter
{
    /// <summary>Fotoğraf yönteminde keskinleştirme miktarı (10/16 ≈ 0,6).</summary>
    internal const int PhotoSharpenSixteenths = 10;

    /// <summary>
    /// Fotoğraf yönteminde termal nokta büyümesi (dot gain) telafisi: çıkış = (v/255)^0,8.
    /// Termal kafa noktaları yayıldığı için ara tonlar kağıtta olduğundan koyu çıkar; bu eğri
    /// ara tonları biraz açarak dengeler (ör. %50 gri → yaklaşık %43 siyah).
    /// </summary>
    internal const double PhotoDotGainExponent = 0.8;

    private static readonly byte[] PhotoGammaLut = Tone.Gamma(PhotoDotGainExponent);

    public static MonoConversionResult Convert(GrayImage source, ImageConversionSettings settings, ImageKind knownKind = ImageKind.Unknown)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);

        int w = source.Width, h = source.Height, n = w * h;
        int threshold = Math.Clamp(settings.Threshold, 1, 254);
        int bias = threshold - 128;

        bool[]? covered = null;
        if (source.Coverage is not null)
        {
            covered = new bool[n];
            byte[] coverage = source.Coverage;
            for (int i = 0; i < n; i++) covered[i] = coverage[i] >= 128;
        }

        // 1) Ton eğrisi (her zaman orijinal gri değerlerin kopyası üzerinde çalışılır).
        var values = (byte[])source.Pixels.Clone();
        byte[] toneLut = Tone.BrightnessContrast(settings.Brightness, settings.Contrast);
        if (!Tone.IsIdentity(toneLut)) Tone.Apply(values, toneLut);

        // 1b) İsteğe bağlı kenar keskinleştirme (0–100 → 0–2 kat yüksek geçiren karışım).
        int sharpen = Math.Clamp(settings.Sharpen, 0, 100);
        if (sharpen > 0) values = Tone.Sharpen(values, covered, w, h, (int)Math.Round(sharpen * 32 / 100.0));

        // 2) Yöntem çözümü.
        ImageConversionMethod method = settings.Method;
        ImageKind kind = ImageKind.Unknown;
        if (method == ImageConversionMethod.Auto || !Enum.IsDefined(method))
        {
            kind = knownKind != ImageKind.Unknown ? knownKind : ImageAnalysis.Classify(source);
            method = kind == ImageKind.Photo ? ImageConversionMethod.Photo : ImageConversionMethod.Logo;
        }

        // 3) Uygula.
        var ink = new MonoBitmap(w, h);
        int? effectiveThreshold = null;
        switch (method)
        {
            case ImageConversionMethod.Threshold:
                effectiveThreshold = threshold;
                Dithering.Threshold(values, covered, w, h, threshold, ink);
                break;

            case ImageConversionMethod.Otsu:
            case ImageConversionMethod.Logo:
            {
                int otsu = source.Coverage is null
                    ? ImageAnalysis.OtsuThreshold(values)
                    : ImageAnalysis.OtsuThreshold(values, source.Coverage);
                int t = Math.Clamp(otsu + bias, 1, 255);
                effectiveThreshold = t;
                Dithering.Threshold(values, covered, w, h, t, ink);
                if (method == ImageConversionMethod.Logo) Dithering.Despeckle(ink, covered, w, h);
                break;
            }

            case ImageConversionMethod.FloydSteinberg:
                ApplyBias(values, bias);
                Dithering.ErrorDiffuse(values, covered, w, h, DiffusionKernel.FloydSteinberg, ink);
                break;

            case ImageConversionMethod.Ordered:
                ApplyBias(values, bias);
                Dithering.Ordered(values, covered, w, h, ink);
                break;

            case ImageConversionMethod.Photo:
            default:
                method = ImageConversionMethod.Photo;
                values = Tone.Sharpen(values, covered, w, h, PhotoSharpenSixteenths);
                Tone.Apply(values, PhotoGammaLut);
                ApplyBias(values, bias);
                Dithering.ErrorDiffuse(values, covered, w, h, DiffusionKernel.Stucki, ink);
                break;
        }

        // 4) Maske ve ters çevirme (yalnızca örtülen piksellerde).
        MonoBitmap? mask = covered is null ? null : BuildMask(covered, w, h);
        if (settings.Invert)
        {
            if (mask is null)
            {
                ink.Invert();
            }
            else
            {
                byte[] inkData = ink.Data, maskData = mask.Data;
                for (int i = 0; i < inkData.Length; i++)
                    inkData[i] = (byte)(~inkData[i] & maskData[i]);
            }
        }

        return new MonoConversionResult(ink, mask, method, kind, effectiveThreshold);
    }

    private static void ApplyBias(byte[] values, int bias)
    {
        if (bias != 0) Tone.Apply(values, Tone.Shift(bias));
    }

    private static MonoBitmap BuildMask(bool[] covered, int width, int height)
    {
        var mask = new MonoBitmap(width, height);
        byte[] data = mask.Data;
        int stride = mask.Stride;
        for (int y = 0; y < height; y++)
        {
            int row = y * width, outRow = y * stride;
            for (int x = 0; x < width; x++)
                if (covered[row + x])
                    data[outRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
        }
        return mask;
    }
}
