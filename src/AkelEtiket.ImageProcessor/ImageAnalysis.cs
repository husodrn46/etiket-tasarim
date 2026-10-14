using SkiaSharp;

namespace AkelEtiket.ImageProcessor;

/// <summary>Görsel analizi: tür tespiti (logo/fotoğraf) ve Otsu eşiği.</summary>
public static class ImageAnalysis
{
    /// <summary><see cref="ClassifySource"/> için kaynak görselin indirildiği en büyük kenar (piksel).</summary>
    public const int ClassificationSize = 256;

    // Sınıflandırma parametreleri (bkz. Classify açıklaması).
    private const int MidLow = 32;
    private const int MidHigh = 223;
    private const int SmoothRange = 40;
    private const double MinSmoothMidFraction = 0.04;
    private const int FlatFillPeaks = 4;
    private const int FlatFillWindow = 6;
    private const double FlatFillConcentration = 0.75;
    private const int MaxClassifyPixels = 400_000;

    /// <summary>
    /// Görselin türünü tahmin eder (yalnızca kapsanan pikseller).
    /// <para>
    /// Yöntem: Ara tonlu (32–223) ve yerel 3×3 aralığı küçük (≤ 40) "düz ara ton" pikselleri sayılır.
    /// Logo/çizimlerde ara tonlar neredeyse yalnızca kenar yumuşatmasında (yüksek yerel aralık) bulunur;
    /// fotoğraflarda ise geniş ve yumuşak geçişli bölgeler oluşturur. Düz ara tonların oranı %4'ün
    /// altındaysa görsel <see cref="ImageKind.Graphic"/> sayılır. Üstündeyse bu piksellerin değerleri
    /// incelenir: en baskın 4 tonun ±6 seviyelik pencereleri düz ara tonların %75'inden fazlasını
    /// kapsıyorsa (JPEG gürültüsüne dayanıklı) renkli düz dolgulu logo, geniş bir aralığa yayılmışsa
    /// <see cref="ImageKind.Photo"/> kabul edilir.
    /// </para>
    /// </summary>
    public static ImageKind Classify(GrayImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        GrayImage sample = Subsample(image, MaxClassifyPixels);
        int w = sample.Width, h = sample.Height;
        byte[] v = sample.Pixels;
        byte[]? cov = sample.Coverage;

        int covered = 0, smoothMid = 0;
        var histogram = new int[256];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (cov is not null && cov[i] < 128) continue;
                covered++;
                int value = v[i];
                if (value < MidLow || value > MidHigh) continue;

                int min = value, max = value;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = y + dy;
                    if ((uint)ny >= (uint)h) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx;
                        if ((uint)nx >= (uint)w) continue;
                        int j = ny * w + nx;
                        if (cov is not null && cov[j] < 128) continue;
                        int n = v[j];
                        if (n < min) min = n;
                        if (n > max) max = n;
                    }
                }
                if (max - min > SmoothRange) continue;
                smoothMid++;
                histogram[value]++;
            }
        }

        if (covered < 16) return ImageKind.Graphic;
        if (smoothMid < covered * MinSmoothMidFraction) return ImageKind.Graphic;

        // Baskın tonların çevresindeki pencereleri sırayla topla (her pencere bir kez sayılır).
        int captured = 0;
        for (int peak = 0; peak < FlatFillPeaks; peak++)
        {
            int best = -1, bestCount = 0;
            for (int t = 0; t < 256; t++)
            {
                if (histogram[t] > bestCount)
                {
                    bestCount = histogram[t];
                    best = t;
                }
            }
            if (best < 0) break;
            for (int t = Math.Max(0, best - FlatFillWindow); t <= Math.Min(255, best + FlatFillWindow); t++)
            {
                captured += histogram[t];
                histogram[t] = 0;
            }
        }
        return captured >= smoothMid * FlatFillConcentration ? ImageKind.Graphic : ImageKind.Photo;
    }

    /// <summary>
    /// Kaynak görseli (uzun kenarı en fazla <see cref="ClassificationSize"/> piksel) beyaz zemine
    /// çizip türünü tahmin eder. Sonuç varlık başına bir kez hesaplanıp önbelleğe alınabilir.
    /// </summary>
    public static ImageKind ClassifySource(LoadedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        double scale = Math.Min(1.0, ClassificationSize / (double)Math.Max(image.Width, image.Height));
        int w = Math.Max(1, (int)Math.Round(image.Width * scale));
        int h = Math.Max(1, (int)Math.Round(image.Height * scale));
        using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            image.Draw(canvas, new SKRect(0, 0, image.Width, image.Height), new SKRect(0, 0, w, h));
            canvas.Flush();
        }
        return Classify(GrayImage.FromBitmap(bitmap));
    }

    /// <summary>
    /// Otsu eşiği (sınıflar arası varyansı en büyük yapan eşik). Dönüş değeri
    /// "parlaklık &lt; eşik → siyah" kuralındaki eşiktir (1…255). Eşit varyanslı bir aralık varsa
    /// aralığın ortası seçilir (temiz iki tonlu görsellerde iki tonun tam ortası).
    /// <paramref name="coverage"/> verilirse yalnızca kapsama ≥ 128 olan pikseller sayılır.
    /// Tek tonlu (ayırt edilemeyen) görsellerde 128 döner.
    /// </summary>
    public static int OtsuThreshold(ReadOnlySpan<byte> gray, ReadOnlySpan<byte> coverage = default)
    {
        bool useCoverage = !coverage.IsEmpty;
        if (useCoverage && coverage.Length < gray.Length)
            throw new ArgumentException("Kapsama dizisi gri diziden kısa olamaz.", nameof(coverage));

        Span<long> histogram = stackalloc long[256];
        histogram.Clear();
        for (int i = 0; i < gray.Length; i++)
        {
            if (useCoverage && coverage[i] < 128) continue;
            histogram[gray[i]]++;
        }
        return OtsuFromHistogram(histogram);
    }

    internal static int OtsuFromHistogram(ReadOnlySpan<long> histogram)
    {
        long total = 0;
        double sum = 0;
        for (int t = 0; t < 256; t++)
        {
            total += histogram[t];
            sum += (double)t * histogram[t];
        }
        if (total == 0) return 128;

        double sumBackground = 0, maxVariance = -1;
        long weightBackground = 0;
        int bestStart = -1, bestEnd = -1;
        for (int t = 0; t < 255; t++)
        {
            weightBackground += histogram[t];
            if (weightBackground == 0) continue;
            long weightForeground = total - weightBackground;
            if (weightForeground == 0) break;
            sumBackground += (double)t * histogram[t];
            double meanBackground = sumBackground / weightBackground;
            double meanForeground = (sum - sumBackground) / weightForeground;
            double diff = meanBackground - meanForeground;
            double variance = (double)weightBackground * weightForeground * diff * diff;
            if (variance > maxVariance * (1 + 1e-12) + 1e-9)
            {
                maxVariance = variance;
                bestStart = bestEnd = t;
            }
            else if (variance >= maxVariance * (1 - 1e-12))
            {
                bestEnd = t;
            }
        }
        if (bestStart < 0) return 128;
        int threshold = (bestStart + bestEnd) / 2;
        return Math.Clamp(threshold + 1, 1, 255);
    }

    /// <summary>Çok büyük görüntüleri sınıflandırma için en yakın komşu ile seyreltir.</summary>
    private static GrayImage Subsample(GrayImage image, int maxPixels)
    {
        long n = (long)image.Width * image.Height;
        if (n <= maxPixels) return image;
        int step = (int)Math.Ceiling(Math.Sqrt(n / (double)maxPixels));
        int w = (image.Width + step - 1) / step, h = (image.Height + step - 1) / step;
        var pixels = new byte[w * h];
        byte[]? coverage = image.Coverage is null ? null : new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            int sourceRow = y * step * image.Width;
            for (int x = 0; x < w; x++)
            {
                int s = sourceRow + x * step;
                pixels[y * w + x] = image.Pixels[s];
                if (coverage is not null) coverage[y * w + x] = image.Coverage![s];
            }
        }
        return new GrayImage(w, h, pixels, coverage);
    }
}
