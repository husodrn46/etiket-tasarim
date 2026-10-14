using SkiaSharp;

namespace AkelEtiket.ImageProcessor.Tests;

public class GrayImageTests
{
    private static SKBitmap Raw(SKColorType colorType, SKAlphaType alphaType, params byte[][] pixels)
    {
        var bitmap = new SKBitmap(new SKImageInfo(pixels.Length, 1, colorType, alphaType));
        Span<byte> span = bitmap.GetPixelSpan();
        for (int i = 0; i < pixels.Length; i++) pixels[i].CopyTo(span.Slice(i * 4, 4));
        return bitmap;
    }

    [Fact]
    public void FromBitmap_PremulBgra_CompositesOnWhite()
    {
        // BGRA bayt sırası, önçarpımlı.
        using var bitmap = Raw(SKColorType.Bgra8888, SKAlphaType.Premul,
            [0, 0, 0, 0],         // saydam SİYAH → beyaz, mürekkep yok
            [0, 0, 0, 255],       // opak siyah
            [0, 0, 255, 255],     // opak kırmızı
            [255, 0, 0, 255],     // opak mavi
            [0, 0, 0, 128],       // yarı saydam siyah
            [128, 128, 128, 128]); // yarı saydam beyaz (önçarpımlı)
        GrayImage gray = GrayImage.FromBitmap(bitmap);
        Assert.Equal(new byte[] { 255, 0, 77, 29, 127, 255 }, gray.Pixels);
        Assert.NotNull(gray.Coverage);
        Assert.Equal(new byte[] { 0, 255, 255, 255, 128, 128 }, gray.Coverage);
        Assert.False(gray.IsCovered(0));
        Assert.True(gray.IsCovered(4));
    }

    [Fact]
    public void FromBitmap_Rgba_UsesCorrectChannelOrder()
    {
        using var bitmap = Raw(SKColorType.Rgba8888, SKAlphaType.Premul, [255, 0, 0, 255], [0, 0, 255, 255]);
        GrayImage gray = GrayImage.FromBitmap(bitmap);
        Assert.Equal(new byte[] { 77, 29 }, gray.Pixels);
        Assert.Null(gray.Coverage); // tamamı opak
    }

    [Fact]
    public void FromBitmap_Unpremul_CompositesOnWhite()
    {
        using var bitmap = Raw(SKColorType.Bgra8888, SKAlphaType.Unpremul, [0, 0, 0, 128], [0, 0, 0, 0], [255, 255, 255, 255]);
        GrayImage gray = GrayImage.FromBitmap(bitmap);
        Assert.Equal(new byte[] { 127, 255, 255 }, gray.Pixels);
        Assert.Equal(new byte[] { 128, 0, 255 }, gray.Coverage);
    }

    [Fact]
    public void FromBitmap_Gray8_CopiesValues()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(3, 2, SKColorType.Gray8, SKAlphaType.Opaque));
        Span<byte> span = bitmap.GetPixelSpan();
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
                span[y * bitmap.RowBytes + x] = (byte)(y * 3 + x);
        GrayImage gray = GrayImage.FromBitmap(bitmap);
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5 }, gray.Pixels);
        Assert.Null(gray.Coverage);
    }

    [Fact]
    public void FromBitmap_OtherColorTypes_AreConverted()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Rgb565, SKAlphaType.Opaque));
        bitmap.SetPixel(0, 0, SKColors.Black);
        bitmap.SetPixel(1, 0, SKColors.White);
        GrayImage gray = GrayImage.FromBitmap(bitmap);
        Assert.Equal(new byte[] { 0, 255 }, gray.Pixels);
    }

    [Fact]
    public void Constructor_ValidatesSizes()
    {
        Assert.Throws<ArgumentException>(() => new GrayImage(2, 2, new byte[3]));
        Assert.Throws<ArgumentException>(() => new GrayImage(2, 2, new byte[4], new byte[2]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GrayImage(0, 2, []));
    }
}
