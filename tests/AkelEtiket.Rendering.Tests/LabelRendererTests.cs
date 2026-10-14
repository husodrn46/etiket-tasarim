using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core;
using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;
using AkelEtiket.Rendering;
using SkiaSharp;

namespace AkelEtiket.Rendering.Tests;

public sealed class LabelRendererTests : IDisposable
{
    private const double Dpmm = 8.0;
    private readonly AssetStore _assets = new();
    private readonly LabelRenderer _renderer;

    public LabelRendererTests() => _renderer = new LabelRenderer(_assets);

    public void Dispose() => _renderer.Dispose();

    private static RenderOptions Design => new() { DotsPerMm = Dpmm };

    [Fact]
    public void Label100x100_At203Dpi_Is800x800Dots()
    {
        var doc = LabelDocument.CreateBlank();
        var result = _renderer.RenderLabel(doc, Design);
        Assert.Equal(800, result.Bitmap.Width);
        Assert.Equal(800, result.Bitmap.Height);
        Assert.Equal(0, result.Bitmap.CountBlack());

        var at300 = _renderer.RenderLabel(doc, Design with { DotsPerMm = 12 });
        Assert.Equal(1200, at300.Bitmap.Width);
    }

    [Fact]
    public void TurkishText_RendersInsideBox_WithoutGlyphWarnings()
    {
        var text = new TextElement { X = 10, Y = 10, Width = 80, Height = 15, Text = "Şekerlik ğüşıöç İĞÜŞÖÇ", FontFamily = "Arial", FontSize = 14 };
        var result = _renderer.RenderElement(text, Design);
        Assert.NotNull(result.Layer);
        Assert.DoesNotContain(result.Issues, i => i.Severity >= RenderIssueSeverity.Warning);
        var bounds = result.Layer!.Ink.GetInkBounds();
        Assert.NotNull(bounds);
        Assert.Equal(80, result.Layer.OffsetX);   // 10 mm × 8
        Assert.Equal(80, result.Layer.OffsetY);
        Assert.True(bounds!.Value.Width <= 640 && bounds.Value.Height <= 120);
        Assert.True(result.Layer.Ink.CountBlack() > 500);
    }

    [Fact]
    public void TextOverflow_Warns_AndAutoShrinkFits()
    {
        var text = new TextElement { Width = 20, Height = 5, Text = "Çok uzun bir ürün adı burada yazıyor", FontSize = 14 };
        var overflow = _renderer.RenderElement(text, Design);
        Assert.True(overflow.Text!.Overflow);
        Assert.Contains(overflow.Issues, i => i.Message.Contains("sığmıyor"));

        text.AutoShrink = true;
        var shrunk = _renderer.RenderElement(text, Design);
        Assert.False(shrunk.Text!.Overflow);
        Assert.True(shrunk.Text.EffectiveFontSizePt < 14);
    }

    [Fact]
    public void MissingFont_FallsBackWithWarning()
    {
        var text = new TextElement { Width = 40, Height = 10, Text = "AKEL", FontFamily = "Böyle Bir Yazı Tipi Yok" };
        var result = _renderer.RenderElement(text, Design);
        Assert.True(result.Text!.FontSubstituted);
        Assert.Contains(result.Issues, i => i.Message.Contains("yazı tipi"));
        Assert.NotNull(result.Layer);
    }

    [Fact]
    public void MovingElement_OnlyChangesOffset_NotContent()
    {
        var text = new TextElement { X = 5, Y = 5, Width = 50, Height = 10, Text = "Melamin Tepsi" };
        var a = _renderer.RenderElement(text, Design);
        text.X = 30.125;
        text.Y = 47.5;
        var b = _renderer.RenderElement(text, Design);
        Assert.True(a.Layer!.Ink.ContentEquals(b.Layer!.Ink));
        Assert.Equal(a.LocalOffsetX, b.LocalOffsetX);
        Assert.Equal(241, b.Layer.OffsetX); // 30,125 × 8 = 241
        Assert.Equal(380, b.Layer.OffsetY);
    }

    [Theory]
    [InlineData(BarcodeType.Ean13, "8691234567890", 0)]
    [InlineData(BarcodeType.Ean13, "8691234567890", 90)]
    [InlineData(BarcodeType.Code128, "AKEL001", 180)]
    [InlineData(BarcodeType.QrCode, "Örnek Firma Şekerlik", 270)]
    [InlineData(BarcodeType.DataMatrix, "TST001", 0)]
    public void BarcodeElement_RenderedLayerDecodes(BarcodeType type, string content, double rotation)
    {
        var barcode = new BarcodeElement { X = 10, Y = 10, Width = 50, Height = 30, Symbology = type, Content = content, Rotation = rotation };
        var result = _renderer.RenderElement(barcode, Design);
        Assert.False(result.HasErrors, string.Join("; ", result.Issues));
        Assert.Null(result.Layer!.Mask); // barkod kutusu tamamen opak beyaz
        var v = BarcodeVerifier.Verify(result.Layer.Ink, type, result.Barcode!.VerificationText);
        Assert.True(v.Success, v.Message);
    }

    [Fact]
    public void BarcodeField_DesignPlaceholder_ThenRealDataDecodes()
    {
        var barcode = new BarcodeElement { Width = 50, Height = 25, Symbology = BarcodeType.Ean13, Content = "{Barkod}" };
        var design = _renderer.RenderElement(barcode, Design);
        Assert.True(design.IsPlaceholder);
        Assert.Contains(design.Issues, i => i.Severity == RenderIssueSeverity.Info);
        Assert.False(design.HasErrors);

        var fields = new RecordFieldSource(["Barkod"], ["8691234567890"]);
        var withData = _renderer.RenderElement(barcode, Design with { Fields = fields, Mode = RenderMode.Print });
        Assert.False(withData.IsPlaceholder);
        Assert.True(BarcodeVerifier.Verify(withData.Layer!.Ink, BarcodeType.Ean13, "8691234567890").Success);

        var printWithoutField = _renderer.RenderElement(barcode, Design with { Mode = RenderMode.Print, Fields = new RecordFieldSource(["Diger"], ["x"]) });
        Assert.True(printWithoutField.HasErrors);
    }

    [Fact]
    public void InvalidBarcode_IsErrorWithVisiblePlaceholder()
    {
        var barcode = new BarcodeElement { Width = 50, Height = 25, Symbology = BarcodeType.Ean13, Content = "8691234567891" };
        var result = _renderer.RenderElement(barcode, Design);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Issues, i => i.Message.Contains("kontrol basamağı"));
        Assert.NotNull(result.Layer);
    }

    [Fact]
    public void RectangleStroke_IsIntegerDotsAndCrisp()
    {
        var rect = new ShapeElement { X = 0, Y = 0, Width = 10, Height = 10, StrokeWidth = 0.5 };
        var result = _renderer.RenderElement(rect, Design);
        var ink = result.Layer!.Ink;
        // 80×80 kutu, 4 noktalık kenar: 80² − 72² = 1216 siyah piksel
        Assert.Equal(1216, ink.CountBlack());
        Assert.True(ink[0, 0] && ink[3, 40] && !ink[4, 40] && ink[79, 79]);
        Assert.False(result.Layer.Mask![40, 40]); // içi boş → alttaki öğeler görünür
    }

    [Fact]
    public void WhiteFilledShape_CoversElementsBelow()
    {
        var doc = LabelDocument.CreateBlank(20, 20);
        doc.Elements.Add(new ShapeElement { X = 0, Y = 0, Width = 20, Height = 20, Fill = FillStyle.Black, StrokeWidth = 0 });
        doc.Elements.Add(new ShapeElement { X = 5, Y = 5, Width = 10, Height = 10, Fill = FillStyle.White, StrokeWidth = 0 });
        var bitmap = _renderer.RenderLabel(doc, Design).Bitmap;
        Assert.True(bitmap[10, 10]);     // siyah zemin
        Assert.False(bitmap[80, 80]);    // beyaz kutu üstte
        Assert.Equal(160 * 160 - 80 * 80, bitmap.CountBlack());
    }

    [Fact]
    public void ArbitraryRotation_ProducesLargerFrame()
    {
        var text = new TextElement { X = 20, Y = 20, Width = 40, Height = 10, Text = "AKEL", FontSize = 20, Rotation = 30 };
        var result = _renderer.RenderElement(text, Design);
        Assert.NotNull(result.Layer);
        Assert.True(result.Layer!.Width > 320 * Math.Cos(Math.PI / 6) && result.Layer.Height > 80);
        Assert.True(result.LocalOffsetY < 0);
    }

    [Fact]
    public void ImageElement_ColorPngWithTransparency_ConvertsToMonoInsideBox()
    {
        var asset = _assets.Add(CreateColorPng(), "renkli.png");
        var image = new ImageElement { X = 10, Y = 10, Width = 40, Height = 40, AssetId = asset.Id, Method = ImageConversionMethod.Threshold };
        var result = _renderer.RenderElement(image, Design);
        Assert.False(result.HasErrors, string.Join("; ", result.Issues));
        Assert.NotNull(result.Layer);
        var gray = result.Layer!.Ink.ToGray8();
        Assert.All(gray, v => Assert.True(v is 0 or 255));
        // Kırmızı daire (koyu) siyah, saydam köşeler beyaz/örtülü.
        int cx = result.Layer.Width / 2, cy = result.Layer.Height / 2;
        Assert.True(result.Layer.Ink[cx, cy]);
        Assert.False(result.Layer.Ink[2, 2]);

        image.Method = ImageConversionMethod.FloydSteinberg;
        var dithered = _renderer.RenderElement(image, Design);
        Assert.False(result.Layer.Ink.ContentEquals(dithered.Layer!.Ink));
        Assert.Equal(ImageConversionMethod.FloydSteinberg, dithered.Image!.AppliedMethod);
    }

    [Fact]
    public void MissingImageAsset_IsError()
    {
        var image = new ImageElement { Width = 20, Height = 20, AssetId = "olmayan.png" };
        var result = _renderer.RenderElement(image, Design);
        Assert.True(result.HasErrors);
        Assert.True(result.IsPlaceholder);
    }

    [Fact]
    public void ScreenLayers_ComposeExactlyToPrintBitmap()
    {
        var doc = LabelDocument.CreateBlank();
        doc.Elements.Add(new ShapeElement { X = 1, Y = 1, Width = 98, Height = 98, StrokeWidth = 0.5, CornerRadius = 3 });
        doc.Elements.Add(new TextElement { X = 5, Y = 5, Width = 90, Height = 12, Text = "Örnek Firma", FontSize = 20, Bold = true, HorizontalAlignment = TextHorizontalAlignment.Center });
        doc.Elements.Add(new BarcodeElement { X = 10, Y = 60, Width = 80, Height = 30, Symbology = BarcodeType.Ean13, Content = "8691234567890" });
        doc.Elements.Add(new TextElement { X = 5, Y = 20, Width = 90, Height = 10, Text = "Döndürülmüş", Rotation = 12 });
        var label = _renderer.RenderLabel(doc, Design);

        var manual = new MonoBitmap(800, 800);
        foreach (var element in doc.Elements) _renderer.RenderElement(element, Design).Layer?.CompositeOnto(manual);
        Assert.True(label.Bitmap.ContentEquals(manual));
    }

    [Fact]
    public void ElementsOutsideLabel_AreReported()
    {
        var doc = LabelDocument.CreateBlank(50, 30);
        doc.Elements.Add(new TextElement { X = 40, Y = 5, Width = 20, Height = 5, Text = "taşan" });
        doc.Elements.Add(new BarcodeElement { X = 30, Y = 10, Width = 40, Height = 15, Symbology = BarcodeType.Code128, Content = "AKEL" });
        var result = _renderer.RenderLabel(doc, Design);
        Assert.Contains(result.Issues, i => i.Severity == RenderIssueSeverity.Warning && i.Message.Contains("taşıyor"));
        Assert.Contains(result.Issues, i => i.Severity == RenderIssueSeverity.Error && i.Message.Contains("Barkod etiket kenarından"));
    }

    [Fact]
    public void PngExport_ContainsPhysChunkForPhysicalSize()
    {
        var bitmap = new MonoBitmap(800, 800);
        bitmap.FillRect(0, 0, 100, 100, true);
        byte[] png = MonoImageExport.EncodePng(bitmap, 203.2);
        Assert.Equal("pHYs", System.Text.Encoding.ASCII.GetString(png, 37, 4));
        using var decoded = SKBitmap.Decode(png);
        Assert.Equal(800, decoded.Width);
        Assert.Equal(SKColors.Black, decoded.GetPixel(5, 5));
        Assert.Equal(SKColors.White, decoded.GetPixel(500, 500));
    }

    [Fact]
    public void Bgra32Preview_HasOnlyBlackWhiteOrTransparent()
    {
        var ink = new MonoBitmap(4, 1);
        var mask = new MonoBitmap(4, 1);
        ink[0, 0] = true; mask[0, 0] = true; mask[1, 0] = true;
        var px = MonoImageExport.ToBgra32(ink, mask);
        Assert.Equal(new byte[] { 0, 0, 0, 255, 255, 255, 255, 255, 0, 0, 0, 0, 0, 0, 0, 0 }, px);
    }

    private static byte[] CreateColorPng()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(200, 200, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = new SKColor(204, 24, 30), IsAntialias = true })
            canvas.DrawCircle(100, 100, 70, paint);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
