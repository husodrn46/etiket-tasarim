using SkiaSharp;
using Xunit.Abstractions;

namespace AkelEtiket.ImageProcessor.Tests;

public class ImageAnalysisTests(ITestOutputHelper output)
{
    [Fact]
    public void Classify_AntiAliasedBlackLogo_IsGraphic()
    {
        using var bitmap = TestImages.LogoBitmap();
        Assert.Equal(ImageKind.Graphic, ImageAnalysis.Classify(GrayImage.FromBitmap(bitmap)));
    }

    [Fact]
    public void Classify_FlatColorLogo_IsGraphic()
    {
        using var bitmap = TestImages.ColorLogoBitmap();
        Assert.Equal(ImageKind.Graphic, ImageAnalysis.Classify(GrayImage.FromBitmap(bitmap)));
    }

    [Fact]
    public void Classify_JpegCompressedLogo_IsGraphic()
    {
        using var bitmap = TestImages.ColorLogoBitmap();
        using var image = ImageLoader.Load(TestImages.Encode(bitmap, SKEncodedImageFormat.Jpeg, 70));
        Assert.Equal(ImageKind.Graphic, ImageAnalysis.ClassifySource(image));
    }

    [Fact]
    public void Classify_SmoothNoisyGradient_IsPhoto()
    {
        Assert.Equal(ImageKind.Photo, ImageAnalysis.Classify(TestImages.PhotoLike(320, 240)));
        Assert.Equal(ImageKind.Photo, ImageAnalysis.Classify(TestImages.HorizontalGradient(300, 50)));
    }

    [Fact]
    public void Classify_PhotoWithTransparentSurroundings_IsPhoto()
    {
        GrayImage photo = TestImages.PhotoLike(200, 200);
        var coverage = new byte[200 * 200];
        for (int y = 0; y < 200; y++)
            for (int x = 0; x < 200; x++)
                coverage[y * 200 + x] = (byte)((x - 100) * (x - 100) + (y - 100) * (y - 100) < 80 * 80 ? 255 : 0);
        var pixels = (byte[])photo.Pixels.Clone();
        for (int i = 0; i < pixels.Length; i++) if (coverage[i] == 0) pixels[i] = 255;
        Assert.Equal(ImageKind.Photo, ImageAnalysis.Classify(new GrayImage(200, 200, pixels, coverage)));
    }

    [Fact]
    public void ClassifySource_TransparentLogoPng_IsGraphic_AndPhotoJpeg_IsPhoto()
    {
        using var logo = TestImages.LogoBitmap(transparentBackground: true);
        using (var image = ImageLoader.Load(TestImages.Encode(logo, SKEncodedImageFormat.Png)))
        {
            Assert.True(image.HasAlpha);
            Assert.Equal(ImageKind.Graphic, ImageAnalysis.ClassifySource(image));
        }

        using var photo = TestImages.PhotoLikeBitmap(640, 480);
        using (var image = ImageLoader.Load(TestImages.Encode(photo, SKEncodedImageFormat.Jpeg, 85)))
            Assert.Equal(ImageKind.Photo, ImageAnalysis.ClassifySource(image));
    }

    [Fact]
    public void Classify_EmptyOrTiny_IsGraphic()
    {
        var coverage = new byte[100];
        Assert.Equal(ImageKind.Graphic, ImageAnalysis.Classify(new GrayImage(10, 10, new byte[100], coverage)));
        Assert.Equal(ImageKind.Graphic, ImageAnalysis.Classify(TestImages.Uniform(3, 3, 128)));
    }

    [Fact]
    public void Classify_LargeImage_IsFastViaSubsampling()
    {
        GrayImage big = TestImages.PhotoLike(2400, 2400, noise: 4);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ImageKind kind = ImageAnalysis.Classify(big);
        sw.Stop();
        output.WriteLine($"2400×2400 sınıflandırma: {sw.ElapsedMilliseconds} ms");
        Assert.Equal(ImageKind.Photo, kind);
        Assert.True(sw.ElapsedMilliseconds < 1500);
    }
}
