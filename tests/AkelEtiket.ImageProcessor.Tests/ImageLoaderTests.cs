using System.Text;
using SkiaSharp;

namespace AkelEtiket.ImageProcessor.Tests;

public class ImageLoaderTests
{
    private static SKBitmap TwoColorBitmap() => TestImages.CreateBitmap(64, 32, SKColors.White, c =>
    {
        using var red = new SKPaint { Color = SKColors.Red };
        c.DrawRect(new SKRect(0, 0, 32, 32), red);
    });

    private static SKColor PixelAfterDraw(LoadedImage image, int width, int height, int x, int y)
    {
        using var bitmap = TestImages.CreateBitmap(width, height, SKColors.Transparent,
            c => image.Draw(c, new SKRect(0, 0, image.Width, image.Height), new SKRect(0, 0, width, height)));
        return bitmap.GetPixel(x, y);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, "PNG")]
    [InlineData(SKEncodedImageFormat.Jpeg, "JPEG")]
    [InlineData(SKEncodedImageFormat.Webp, "WEBP")]
    public void Load_EncodedRaster_DecodesSizeFormatAndColors(SKEncodedImageFormat format, string expectedName)
    {
        using var source = TwoColorBitmap();
        using var image = ImageLoader.Load(TestImages.Encode(source, format), "test." + expectedName.ToLowerInvariant());
        Assert.Equal(64, image.Width);
        Assert.Equal(32, image.Height);
        Assert.Equal(expectedName, image.FormatName);
        Assert.False(image.IsVector);
        Assert.False(image.WasDownsampled);

        SKColor left = PixelAfterDraw(image, 64, 32, 10, 16);
        SKColor right = PixelAfterDraw(image, 64, 32, 50, 16);
        Assert.True(left.Red > 200 && left.Green < 60 && left.Blue < 60, $"Sol kırmızı olmalı: {left}");
        Assert.True(right.Red > 200 && right.Green > 200 && right.Blue > 200, $"Sağ beyaz olmalı: {right}");
    }

    [Fact]
    public void Load_Bmp24_Decodes()
    {
        using var source = TwoColorBitmap();
        using var image = ImageLoader.Load(TestImages.EncodeBmp24(source), "logo.bmp");
        Assert.Equal("BMP", image.FormatName);
        Assert.Equal((64, 32), (image.Width, image.Height));
        Assert.False(image.HasAlpha);
        Assert.Equal(SKColors.Red, PixelAfterDraw(image, 64, 32, 5, 5));
        Assert.Equal(SKColors.White, PixelAfterDraw(image, 64, 32, 60, 30));
    }

    [Fact]
    public void Load_Gif_FirstFrameDecodes()
    {
        using var image = ImageLoader.Load(TestImages.BlackPixelGif, "nokta.gif");
        Assert.Equal("GIF", image.FormatName);
        Assert.Equal((1, 1), (image.Width, image.Height));
        Assert.Equal(SKColors.Black, PixelAfterDraw(image, 1, 1, 0, 0));
    }

    [Fact]
    public void HasAlpha_ReflectsRealTransparency()
    {
        using var transparent = TestImages.CreateBitmap(20, 20, SKColors.Transparent, c =>
        {
            using var black = new SKPaint { Color = SKColors.Black };
            c.DrawRect(new SKRect(0, 0, 10, 20), black);
        });
        using var opaque = TestImages.CreateBitmap(20, 20, SKColors.White);
        using (var image = ImageLoader.Load(TestImages.Encode(transparent, SKEncodedImageFormat.Png)))
            Assert.True(image.HasAlpha);
        using (var image = ImageLoader.Load(TestImages.Encode(opaque, SKEncodedImageFormat.Png)))
            Assert.False(image.HasAlpha);
        using (var image = ImageLoader.Load(TestImages.Encode(opaque, SKEncodedImageFormat.Jpeg)))
            Assert.False(image.HasAlpha);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0xFF, 0xFF })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00 })]
    public void Load_CorruptOrEmpty_ThrowsTurkishImageLoadException(byte[] data)
    {
        var ex = Assert.Throws<ImageLoadException>(() => ImageLoader.Load(data, "bozuk.png"));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        Assert.Contains("Görsel", ex.Message);
    }

    [Fact]
    public void Load_RandomNoise_Throws()
    {
        var bytes = new byte[2048];
        new Random(12).NextBytes(bytes);
        Assert.Throws<ImageLoadException>(() => ImageLoader.Load(bytes));
        Assert.Throws<ImageLoadException>(() => ImageLoader.Load(Encoding.UTF8.GetBytes("merhaba dünya"), "not.txt"));
    }

    [Fact]
    public void Load_HugeImage_IsDownsampledToMaxDimension()
    {
        using var wide = TestImages.CreateBitmap(5000, 100, SKColors.White, c =>
        {
            using var black = new SKPaint { Color = SKColors.Black };
            c.DrawRect(new SKRect(0, 0, 2500, 100), black);
        });
        using var image = ImageLoader.Load(TestImages.Encode(wide, SKEncodedImageFormat.Png));
        Assert.Equal(ImageLoader.MaxDimension, image.Width);
        Assert.Equal(82, image.Height); // 100 × 4096/5000 = 81,92
        Assert.Equal((5000, 100), (image.OriginalWidth, image.OriginalHeight));
        Assert.True(image.WasDownsampled);
        Assert.NotNull(image.Warning);
        Assert.Equal(SKColors.Black, PixelAfterDraw(image, 4096, 82, 100, 40));
        Assert.Equal(SKColors.White, PixelAfterDraw(image, 4096, 82, 4000, 40));
    }

    [Fact]
    public void Load_Jpeg_AppliesExifOrientation()
    {
        // 40×20, kırmızı blok sol üstte. EXIF 6 = görüntülemek için 90° saat yönünde döndür.
        using var source = TestImages.CreateBitmap(40, 20, SKColors.White, c =>
        {
            using var red = new SKPaint { Color = SKColors.Red };
            c.DrawRect(new SKRect(0, 0, 10, 10), red);
        });
        byte[] jpeg = TestImages.WithExifOrientation(TestImages.Encode(source, SKEncodedImageFormat.Jpeg, 98), 6);
        using var image = ImageLoader.Load(jpeg, "foto.jpg");
        Assert.Equal((20, 40), (image.Width, image.Height));
        SKColor topRight = PixelAfterDraw(image, 20, 40, 15, 4);
        SKColor topLeft = PixelAfterDraw(image, 20, 40, 4, 4);
        Assert.True(topRight.Red > 200 && topRight.Green < 80, $"Kırmızı blok sağ üstte olmalı: {topRight}");
        Assert.True(topLeft.Green > 200, $"Sol üst beyaz olmalı: {topLeft}");

        // EXIF 8 = 90° saat yönünün tersine: blok sol altta olmalı.
        using var ccw = ImageLoader.Load(TestImages.WithExifOrientation(TestImages.Encode(source, SKEncodedImageFormat.Jpeg, 98), 8));
        Assert.Equal((20, 40), (ccw.Width, ccw.Height));
        SKColor bottomLeft = PixelAfterDraw(ccw, 20, 40, 4, 35);
        Assert.True(bottomLeft.Red > 200 && bottomLeft.Green < 80, $"Kırmızı blok sol altta olmalı: {bottomLeft}");
    }

    [Theory]
    [InlineData("logo.PNG", true)]
    [InlineData("foto.jpeg", true)]
    [InlineData("a.JPG", true)]
    [InlineData("a.bmp", true)]
    [InlineData("a.svg", true)]
    [InlineData("a.webp", true)]
    [InlineData("a.gif", true)]
    [InlineData("a.tiff", false)]
    [InlineData("a.pdf", false)]
    [InlineData("", false)]
    public void IsSupportedFile(string path, bool expected) => Assert.Equal(expected, ImageLoader.IsSupportedFile(path));

    [Fact]
    public void OpenFileFilter_IsWellFormedForWpf()
    {
        string[] parts = ImageLoader.OpenFileFilter.Split('|');
        Assert.True(parts.Length % 2 == 0);
        Assert.Contains("*.png", ImageLoader.OpenFileFilter);
        Assert.Contains("*.svg", ImageLoader.OpenFileFilter);
    }

    [Fact]
    public void Draw_SourceRect_CropsRegion()
    {
        using var source = TwoColorBitmap();
        using var image = ImageLoader.Load(TestImages.Encode(source, SKEncodedImageFormat.Png));
        using var bitmap = TestImages.CreateBitmap(32, 32, SKColors.Transparent,
            c => image.Draw(c, new SKRect(32, 0, 64, 32), new SKRect(0, 0, 32, 32)));
        Assert.Equal(SKColors.White, bitmap.GetPixel(16, 16)); // sağ yarı beyazdı
    }

    [Fact]
    public void Draw_Downscale_UsesMipmapsWithoutAliasing()
    {
        // 1 piksellik dama tahtası 1/8 ölçeğe indirildiğinde düzgün orta gri olmalı.
        using var checker = new SKBitmap(new SKImageInfo(512, 512, SKColorType.Bgra8888, SKAlphaType.Premul));
        for (int y = 0; y < 512; y++)
            for (int x = 0; x < 512; x++)
                checker.SetPixel(x, y, ((x + y) & 1) == 0 ? SKColors.Black : SKColors.White);
        using var image = ImageLoader.Load(TestImages.Encode(checker, SKEncodedImageFormat.Png));
        GrayImage gray = TestImages.RenderLikeRendering(image, 64, 64);
        int min = gray.Pixels.Min(), max = gray.Pixels.Max();
        Assert.InRange(min, 90, 170);
        Assert.InRange(max, 90, 170);
    }

    [Fact]
    public void Draw_IsThreadSafe_AndDeterministic()
    {
        using var source = TestImages.PhotoLikeBitmap(300, 200);
        using var image = ImageLoader.Load(TestImages.Encode(source, SKEncodedImageFormat.Png));
        var results = new string[16];
        Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            using var canvasCanvasBitmap = new SKBitmap(new SKImageInfo(120, 80, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(canvasCanvasBitmap))
            {
                canvas.Clear(SKColors.White);
                canvas.RotateDegrees(3, 60, 40);
                image.Draw(canvas, new SKRect(0, 0, image.Width, image.Height), new SKRect(0, 0, 120, 80));
            }
            results[i] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(canvasCanvasBitmap.GetPixelSpan()));
        });
        Assert.All(results, r => Assert.Equal(results[0], r));
    }

    [Fact]
    public void Dispose_ThenDraw_Throws()
    {
        using var source = TwoColorBitmap();
        var image = ImageLoader.Load(TestImages.Encode(source, SKEncodedImageFormat.Png));
        image.Dispose();
        image.Dispose(); // ikinci kez sorun çıkarmamalı
        using var target = TestImages.CreateBitmap(4, 4, SKColors.White);
        using var canvas = new SKCanvas(target);
        Assert.Throws<ObjectDisposedException>(() => image.Draw(canvas, new SKRect(0, 0, 4, 4)));
    }
}
