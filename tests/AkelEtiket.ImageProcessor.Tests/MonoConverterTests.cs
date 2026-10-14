using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;
using SkiaSharp;

namespace AkelEtiket.ImageProcessor.Tests;

public class MonoConverterTests
{
    public static TheoryData<ImageConversionMethod> AllMethods()
    {
        var data = new TheoryData<ImageConversionMethod>();
        foreach (ImageConversionMethod m in Enum.GetValues<ImageConversionMethod>()) data.Add(m);
        return data;
    }

    private static ImageConversionMethod[] ManualMethods =>
        Enum.GetValues<ImageConversionMethod>().Where(m => m != ImageConversionMethod.Auto).ToArray();

    /// <summary>Renkli, saydam köşeli, geçişli bir PNG: ImageLoader → Rendering benzeri çizim → gri.</summary>
    private static GrayImage ColorfulPngGray()
    {
        using var bitmap = TestImages.CreateBitmap(160, 120, SKColors.Transparent, c =>
        {
            using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(160, 0),
                [SKColors.Navy, SKColors.Orange, SKColors.LightYellow], SKShaderTileMode.Clamp);
            using var gradient = new SKPaint { Shader = shader };
            c.DrawRect(new SKRect(0, 0, 160, 80), gradient);
            using var green = new SKPaint { Color = SKColors.ForestGreen, IsAntialias = true };
            c.DrawCircle(40, 95, 20, green);
            // Sağ alt köşe saydam kalır.
        });
        using var image = ImageLoader.Load(TestImages.Encode(bitmap, SKEncodedImageFormat.Png), "renkli.png");
        // Saydam bitmap + yalnızca görselin kendisi (beyaz alt zemin yok) → kapsama = görsel alfası.
        using var target = TestImages.CreateBitmap(160, 120, SKColors.Transparent, c => image.Draw(c, new SKRect(0, 0, 160, 120)));
        return GrayImage.FromBitmap(target);
    }

    [Theory]
    [MemberData(nameof(AllMethods))]
    public void EveryMethod_ProducesConsistentOneBitOutput(ImageConversionMethod method)
    {
        GrayImage gray = ColorfulPngGray();
        Assert.NotNull(gray.Coverage);
        MonoConversionResult result = MonoConverter.Convert(gray, new ImageConversionSettings(method));

        Assert.NotEqual(ImageConversionMethod.Auto, result.AppliedMethod);
        Assert.Equal((160, 120), (result.Ink.Width, result.Ink.Height));
        Assert.All(result.Ink.ToGray8(), v => Assert.True(v is 0 or 255));
        Assert.NotNull(result.Mask);
        for (int y = 0; y < 120; y++)
            for (int x = 0; x < 160; x++)
            {
                bool covered = gray.Coverage![y * 160 + x] >= 128;
                Assert.Equal(covered, result.Mask![x, y]);
                if (!covered) Assert.False(result.Ink[x, y], $"Maske dışında mürekkep: ({x},{y}) {method}");
            }
        Assert.True(result.Ink.CountBlack() > 0, "Koyu bölgeler siyah olmalı");
        if (method == ImageConversionMethod.Auto)
            Assert.NotEqual(ImageKind.Unknown, result.DetectedKind);
        else
            Assert.Equal(method, result.AppliedMethod);
    }

    [Theory]
    [MemberData(nameof(AllMethods))]
    public void TransparentBlackPixels_NeverBecomeInk_EvenWhenInverted(ImageConversionMethod method)
    {
        // Sol yarı opak siyah, sağ yarı TAMAMEN SAYDAM SİYAH (0,0,0,0).
        using var bitmap = TestImages.CreateBitmap(40, 20, SKColors.Transparent, c =>
        {
            using var black = new SKPaint { Color = SKColors.Black };
            c.DrawRect(new SKRect(0, 0, 20, 20), black);
        });
        GrayImage gray = GrayImage.FromBitmap(bitmap);
        Assert.All(gray.Pixels.Skip(25).Take(10), v => Assert.Equal(255, v));

        foreach (bool invert in new[] { false, true })
        {
            MonoConversionResult result = MonoConverter.Convert(gray, new ImageConversionSettings(method, Invert: invert));
            Assert.Equal(0, TestImages.CountBlack(result.Ink, 20, 0, 40, 20));
            int leftBlack = TestImages.CountBlack(result.Ink, 0, 0, 20, 20);
            Assert.Equal(invert ? 0 : 400, leftBlack);
        }
    }

    [Fact]
    public void FullyTransparentDarkSource_ProducesNoInk()
    {
        // Gri değerler siyah olsa bile kapsama 0 ise hiçbir algoritma mürekkep koymamalı.
        var pixels = new byte[30 * 30];
        var coverage = new byte[30 * 30];
        for (int i = 0; i < coverage.Length; i++) coverage[i] = (byte)(i % 30 < 15 ? 255 : 0);
        var gray = new GrayImage(30, 30, pixels, coverage);
        foreach (var method in ManualMethods)
        {
            MonoConversionResult result = MonoConverter.Convert(gray, new ImageConversionSettings(method));
            Assert.Equal(0, TestImages.CountBlack(result.Ink, 15, 0, 30, 30));
        }
    }

    [Fact]
    public void ChangingMethod_ChangesOutput()
    {
        GrayImage gray = TestImages.PhotoLike(200, 120);
        var outputs = ManualMethods.ToDictionary(m => m, m => MonoConverter.Convert(gray, new ImageConversionSettings(m)).Ink);
        var dithered = new[] { ImageConversionMethod.FloydSteinberg, ImageConversionMethod.Ordered, ImageConversionMethod.Photo, ImageConversionMethod.Threshold };
        for (int a = 0; a < dithered.Length; a++)
            for (int b = a + 1; b < dithered.Length; b++)
                Assert.False(outputs[dithered[a]].ContentEquals(outputs[dithered[b]]), $"{dithered[a]} ile {dithered[b]} aynı çıktı verdi");
    }

    [Theory]
    [InlineData(ImageConversionMethod.Threshold)]
    [InlineData(ImageConversionMethod.Otsu)]
    [InlineData(ImageConversionMethod.Logo)]
    [InlineData(ImageConversionMethod.FloydSteinberg)]
    [InlineData(ImageConversionMethod.Ordered)]
    [InlineData(ImageConversionMethod.Photo)]
    public void HigherThreshold_IsNeverLighter(ImageConversionMethod method)
    {
        GrayImage gray = TestImages.HorizontalGradient(256, 32);
        int previous = -1;
        for (int t = 20; t <= 240; t += 20)
        {
            int black = MonoConverter.Convert(gray, new ImageConversionSettings(method, Threshold: t)).Ink.CountBlack();
            Assert.True(black >= previous - 4, $"{method}: eşik {t} için siyah {black}, önceki {previous}");
            previous = black;
        }
    }

    [Fact]
    public void Threshold_IsExact()
    {
        GrayImage gray = TestImages.HorizontalGradient(256, 1);
        MonoConversionResult result = MonoConverter.Convert(gray, new ImageConversionSettings(ImageConversionMethod.Threshold, Threshold: 100));
        Assert.Equal(100, result.EffectiveThreshold);
        for (int x = 0; x < 256; x++)
            Assert.Equal(gray.Pixels[x] < 100, result.Ink[x, 0]);
    }

    [Fact]
    public void Otsu_FindsThresholdBetweenModes_UsingOnlyCoveredPixels()
    {
        var random = new Random(5);
        int w = 200, h = 100;
        var pixels = new byte[w * h];
        var coverage = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                bool covered = y < 80;
                coverage[i] = covered ? (byte)255 : (byte)0;
                // Kapsanmayan şerit simsiyah: Otsu'yu yanıltmamalı.
                pixels[i] = !covered ? (byte)0 : (byte)Math.Clamp((x < 100 ? 50 : 200) + random.Next(-10, 11), 0, 255);
            }
        var gray = new GrayImage(w, h, pixels, coverage);
        int otsu = ImageAnalysis.OtsuThreshold(pixels, coverage);
        Assert.InRange(otsu, 75, 175);

        MonoConversionResult result = MonoConverter.Convert(gray, new ImageConversionSettings(ImageConversionMethod.Otsu));
        Assert.Equal(otsu, result.EffectiveThreshold);
        Assert.Equal(100 * 80, result.Ink.CountBlack()); // sol yarının tamamı, başka hiçbir şey
    }

    [Fact]
    public void OtsuThreshold_EdgeCases()
    {
        Assert.Equal(128, ImageAnalysis.OtsuThreshold(ReadOnlySpan<byte>.Empty));
        Assert.Equal(128, ImageAnalysis.OtsuThreshold(new byte[] { 90, 90, 90 }));
        // İki temiz ton: eşik tam ortada olmalı.
        int t = ImageAnalysis.OtsuThreshold(new byte[] { 0, 0, 0, 255, 255, 255 });
        Assert.InRange(t, 120, 136);
    }

    [Fact]
    public void Brightness_And_Contrast_MoveToneInExpectedDirection()
    {
        GrayImage gradient = TestImages.HorizontalGradient(256, 64);
        int Dark(int brightness, int contrast, GrayImage img) =>
            MonoConverter.Convert(img, new ImageConversionSettings(ImageConversionMethod.FloydSteinberg, Brightness: brightness, Contrast: contrast)).Ink.CountBlack();

        Assert.True(Dark(-50, 0, gradient) > Dark(0, 0, gradient));
        Assert.True(Dark(0, 0, gradient) > Dark(50, 0, gradient));

        GrayImage uniform = TestImages.Uniform(100, 100, 100); // 128'den koyu
        Assert.True(Dark(0, 60, uniform) > Dark(0, 0, uniform));
        Assert.True(Dark(0, 0, uniform) > Dark(0, -60, uniform));
    }

    [Theory]
    [MemberData(nameof(AllMethods))]
    public void Invert_FlipsEveryCoveredPixel(ImageConversionMethod method)
    {
        GrayImage gray = ColorfulPngGray();
        MonoConversionResult normal = MonoConverter.Convert(gray, new ImageConversionSettings(method));
        MonoConversionResult inverted = MonoConverter.Convert(gray, new ImageConversionSettings(method, Invert: true));
        for (int y = 0; y < gray.Height; y++)
            for (int x = 0; x < gray.Width; x++)
            {
                bool covered = normal.Mask is null || normal.Mask[x, y];
                Assert.Equal(covered && !normal.Ink[x, y], inverted.Ink[x, y]);
            }
    }

    [Theory]
    [MemberData(nameof(AllMethods))]
    public void Output_IsDeterministic(ImageConversionMethod method)
    {
        GrayImage gray = TestImages.PhotoLike(180, 140, seed: 9);
        var settings = new ImageConversionSettings(method, Threshold: 140, Brightness: 10, Contrast: 15);
        MonoBitmap a = MonoConverter.Convert(gray, settings).Ink;
        MonoBitmap b = MonoConverter.Convert(gray, settings).Ink;
        Assert.True(a.ContentEquals(b));
    }

    [Theory]
    [InlineData(ImageConversionMethod.FloydSteinberg)]
    [InlineData(ImageConversionMethod.Ordered)]
    [InlineData(ImageConversionMethod.Photo)]
    public void MidGray_GivesBalancedDither(ImageConversionMethod method)
    {
        GrayImage gray = TestImages.Uniform(200, 200, 128);
        double ratio = MonoConverter.Convert(gray, new ImageConversionSettings(method)).Ink.CountBlack() / 40000.0;
        Assert.InRange(ratio, 0.38, 0.6);
    }

    [Fact]
    public void Ordered_MidGray_IsExactlyHalf()
    {
        GrayImage gray = TestImages.Uniform(64, 64, 128);
        Assert.Equal(64 * 64 / 2, MonoConverter.Convert(gray, new ImageConversionSettings(ImageConversionMethod.Ordered)).Ink.CountBlack());
    }

    [Fact]
    public void Photo_OnGradient_ProducesMonotonicTonalRamp()
    {
        GrayImage gray = TestImages.HorizontalGradient(512, 64);
        MonoBitmap ink = MonoConverter.Convert(gray, new ImageConversionSettings(ImageConversionMethod.Photo)).Ink;
        double previous = 1.0;
        var fractions = new List<double>();
        for (int band = 0; band < 8; band++)
        {
            double fraction = TestImages.CountBlack(ink, band * 64, 0, band * 64 + 64, 64) / (64.0 * 64);
            fractions.Add(fraction);
            Assert.True(fraction <= previous + 0.03, $"Ton rampası bozuk: {string.Join(", ", fractions.Select(f => f.ToString("0.00")))}");
            previous = fraction;
        }
        Assert.True(fractions[0] > 0.85, $"Koyu uç yeterince koyu değil: {fractions[0]:0.00}");
        Assert.True(fractions[^1] < 0.1, $"Açık uç yeterince açık değil: {fractions[^1]:0.00}");
    }

    [Fact]
    public void Logo_RemovesIsolatedSpecks_ButKeepsThinLines()
    {
        var pixels = new byte[50 * 50];
        Array.Fill(pixels, (byte)255);
        pixels[25 * 50 + 25] = 0;                          // tek nokta (leke)
        for (int x = 5; x < 45; x++) pixels[10 * 50 + x] = 0; // 1 piksel kalınlığında çizgi
        var gray = new GrayImage(50, 50, pixels);

        MonoBitmap logo = MonoConverter.Convert(gray, new ImageConversionSettings(ImageConversionMethod.Logo)).Ink;
        Assert.False(logo[25, 25]);
        Assert.Equal(40, TestImages.CountBlack(logo, 0, 10, 50, 11));

        MonoBitmap threshold = MonoConverter.Convert(gray, new ImageConversionSettings(ImageConversionMethod.Threshold)).Ink;
        Assert.True(threshold[25, 25]);
    }

    [Fact]
    public void Auto_PicksLogoForGraphics_PhotoForPhotos_AndHonorsKnownKind()
    {
        using var logoBitmap = TestImages.LogoBitmap();
        GrayImage logo = GrayImage.FromBitmap(logoBitmap);
        MonoConversionResult logoResult = MonoConverter.Convert(logo, ImageConversionSettings.Default);
        Assert.Equal(ImageKind.Graphic, logoResult.DetectedKind);
        Assert.Equal(ImageConversionMethod.Logo, logoResult.AppliedMethod);
        Assert.NotNull(logoResult.EffectiveThreshold);

        GrayImage photo = TestImages.PhotoLike(300, 200);
        MonoConversionResult photoResult = MonoConverter.Convert(photo, ImageConversionSettings.Default);
        Assert.Equal(ImageKind.Photo, photoResult.DetectedKind);
        Assert.Equal(ImageConversionMethod.Photo, photoResult.AppliedMethod);
        Assert.Null(photoResult.EffectiveThreshold);

        MonoConversionResult forced = MonoConverter.Convert(logo, ImageConversionSettings.Default, ImageKind.Photo);
        Assert.Equal(ImageConversionMethod.Photo, forced.AppliedMethod);
        Assert.Equal(ImageKind.Photo, forced.DetectedKind);

        MonoConversionResult manual = MonoConverter.Convert(photo, new ImageConversionSettings(ImageConversionMethod.Ordered));
        Assert.Equal(ImageKind.Unknown, manual.DetectedKind);
    }

    [Fact]
    public void Mask_IsNull_WhenSourceFullyCovered()
    {
        MonoConversionResult result = MonoConverter.Convert(TestImages.Uniform(10, 10, 0), new ImageConversionSettings(ImageConversionMethod.Threshold));
        Assert.Null(result.Mask);
        Assert.Equal(100, result.Ink.CountBlack());
    }

    [Fact]
    public void Settings_FromImageElement_CopiesAllFields()
    {
        var element = new ImageElement
        {
            Method = ImageConversionMethod.Ordered,
            Threshold = 90,
            Brightness = -15,
            Contrast = 40,
            Invert = true,
        };
        Assert.Equal(new ImageConversionSettings(ImageConversionMethod.Ordered, 90, -15, 40, true), ImageConversionSettings.From(element));
        Assert.Equal(ImageConversionSettings.From(element), ImageConversionSettings.From(element)); // önbellek anahtarı
    }

    [Fact]
    public void OddWidths_KeepPaddingBitsClear()
    {
        GrayImage gray = TestImages.Uniform(13, 5, 0);
        foreach (var method in ManualMethods)
        {
            MonoBitmap ink = MonoConverter.Convert(gray, new ImageConversionSettings(method, Invert: false)).Ink;
            Assert.Equal(65, ink.CountBlack());
            for (int y = 0; y < 5; y++) Assert.Equal(0, ink.Data[y * ink.Stride + 1] & 0x07);
        }
    }
}
