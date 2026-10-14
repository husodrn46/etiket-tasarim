using System.Text;
using SkiaSharp;

namespace AkelEtiket.ImageProcessor.Tests;

public class SvgTests
{
    private const string SimpleSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" width="200" height="100" viewBox="0 0 200 100">
          <rect x="0" y="0" width="100" height="100" fill="#000000"/>
          <circle cx="150" cy="50" r="40" fill="#ff0000"/>
        </svg>
        """;

    private static SKBitmap DrawToBitmap(LoadedImage image, int width, int height)
    {
        var bitmap = TestImages.CreateBitmap(width, height, SKColors.Transparent,
            c => image.Draw(c, new SKRect(0, 0, image.Width, image.Height), new SKRect(0, 0, width, height)));
        return bitmap;
    }

    [Fact]
    public void Load_Svg_IsVectorWithIntrinsicSize_AndRendersShapes()
    {
        using var image = ImageLoader.Load(Encoding.UTF8.GetBytes(SimpleSvg), "logo.svg");
        Assert.True(image.IsVector);
        Assert.True(image.HasAlpha);
        Assert.Equal("SVG", image.FormatName);
        Assert.Equal((200, 100), (image.Width, image.Height));

        // İki kat büyütülmüş çizim: vektör olduğu için kenarlar keskin kalmalı.
        using var bitmap = DrawToBitmap(image, 400, 200);
        Assert.Equal(SKColors.Black, bitmap.GetPixel(100, 100));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(300, 100));
        Assert.Equal(0, bitmap.GetPixel(395, 5).Alpha); // boş alan saydam
        Assert.Equal(SKColors.Black, bitmap.GetPixel(198, 100)); // dikdörtgen kenarına çok yakın: hâlâ tam siyah

        GrayImage gray = GrayImage.FromBitmap(bitmap);
        Assert.NotNull(gray.Coverage);
        Assert.Equal(255, gray.Pixels[5 * 400 + 395]);
        Assert.Equal(0, gray.Coverage![5 * 400 + 395]);
    }

    [Fact]
    public void Load_Svg_ContentSniffing_WorksWithoutExtensionAndWithBom()
    {
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>\n" + SimpleSvg)];
        using var image = ImageLoader.Load(withBom, null);
        Assert.True(image.IsVector);
    }

    [Fact]
    public void Load_IllustratorStyleSvg_WithDoctypeAndEntities_Loads()
    {
        const string svg = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd" [
              <!ENTITY ns_svg "http://www.w3.org/2000/svg">
            ]>
            <svg version="1.1" xmlns="&ns_svg;" width="50" height="50" viewBox="0 0 50 50">
              <rect width="50" height="50" fill="#000000"/>
            </svg>
            """;
        using var image = ImageLoader.Load(Encoding.UTF8.GetBytes(svg), "ai-export.svg");
        using var bitmap = DrawToBitmap(image, 50, 50);
        Assert.Equal(SKColors.Black, bitmap.GetPixel(25, 25));
    }

    [Fact]
    public void Load_SvgWithExternalImageReference_DoesNotResolveIt()
    {
        string path = Path.Combine(Path.GetTempPath(), $"akel-svg-ext-{Guid.NewGuid():N}.png");
        using (var black = TestImages.CreateBitmap(10, 10, SKColors.Black))
            File.WriteAllBytes(path, TestImages.Encode(black, SKEncodedImageFormat.Png));
        try
        {
            string uri = new Uri(path).AbsoluteUri;
            // Yalnızca dış görsel içeren SVG: içerik yüklenmez, anlaşılır hata verilir.
            string onlyExternal = $"""
                <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="20" height="20">
                  <image x="0" y="0" width="20" height="20" xlink:href="{uri}"/>
                </svg>
                """;
            var ex = Assert.Throws<ImageLoadException>(() => ImageLoader.Load(Encoding.UTF8.GetBytes(onlyExternal), "ext.svg"));
            Assert.Contains("dış", ex.Message);

            // Görünür içerik + dış görsel: yüklenir, dış görsel alanı boş kalır, uyarı verilir.
            string mixed = $"""
                <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="20" height="20">
                  <rect x="0" y="0" width="5" height="5" fill="#000"/>
                  <image x="10" y="10" width="10" height="10" href="{uri}"/>
                </svg>
                """;
            using var image = ImageLoader.Load(Encoding.UTF8.GetBytes(mixed), "mixed.svg");
            Assert.NotNull(image.Warning);
            using var bitmap = DrawToBitmap(image, 20, 20);
            Assert.Equal(SKColors.Black, bitmap.GetPixel(2, 2));
            Assert.Equal(0, bitmap.GetPixel(15, 15).Alpha);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_SvgEmbeddedDataUriImage_Renders()
    {
        using var black = TestImages.CreateBitmap(4, 4, SKColors.Black);
        string base64 = Convert.ToBase64String(TestImages.Encode(black, SKEncodedImageFormat.Png));
        string svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="20" height="20">
              <image x="0" y="0" width="20" height="20" xlink:href="data:image/png;base64,{base64}"/>
            </svg>
            """;
        using var image = ImageLoader.Load(Encoding.UTF8.GetBytes(svg), "embedded.svg");
        using var bitmap = DrawToBitmap(image, 20, 20);
        Assert.Equal(SKColors.Black, bitmap.GetPixel(10, 10));
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><rect")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"20\"><rect x=\"50\" y=\"50\" width=\"5\" height=\"5\"/></svg>")]
    public void Load_BrokenEmptyOrInvisibleSvg_ThrowsImageLoadException(string svg)
    {
        var ex = Assert.Throws<ImageLoadException>(() => ImageLoader.Load(Encoding.UTF8.GetBytes(svg), "bozuk.svg"));
        Assert.Contains("SVG", ex.Message);
    }

    [Fact]
    public void Load_SizelessSvgWithContent_UsesContentBounds_AndTinyViewBoxStillWorks()
    {
        using (var image = ImageLoader.Load(Encoding.UTF8.GetBytes(
                   "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect x=\"10\" y=\"10\" width=\"50\" height=\"20\"/></svg>")))
            Assert.Equal((60, 30), (image.Width, image.Height));

        using var tiny = ImageLoader.Load(Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><rect width=\"1\" height=\"1\"/></svg>"));
        Assert.Equal((1, 1), (tiny.Width, tiny.Height));
        using var bitmap = DrawToBitmap(tiny, 50, 50); // vektör: 50 kat büyütmede de dolu ve keskin
        Assert.Equal(SKColors.Black, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Black, bitmap.GetPixel(49, 49));
    }

    [Fact]
    public void Svg_ConvertsToMonoThroughStandardPipeline()
    {
        using var image = ImageLoader.Load(Encoding.UTF8.GetBytes(SimpleSvg), "logo.svg");
        Assert.Equal(ImageKind.Graphic, ImageAnalysis.ClassifySource(image));
        GrayImage gray = TestImages.RenderLikeRendering(image, 200, 100);
        MonoConversionResult result = MonoConverter.Convert(gray, ImageConversionSettings.Default);
        Assert.Equal(AkelEtiket.Core.Model.ImageConversionMethod.Logo, result.AppliedMethod);
        Assert.True(result.Ink[50, 50]);   // siyah dikdörtgen
        Assert.True(result.Ink[150, 50]);  // kırmızı daire de mürekkep olur (logo)
        Assert.False(result.Ink[190, 5]);  // boş alan beyaz
    }
}
