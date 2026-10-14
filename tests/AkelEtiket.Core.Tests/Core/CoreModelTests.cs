using AkelEtiket.Core.Data;
using AkelEtiket.Core.Geometry;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;

namespace AkelEtiket.Core.Tests.Core;

public class UnitsTests
{
    [Theory]
    [InlineData(100, 8.0, 800)]
    [InlineData(108, 8.0, 864)]
    [InlineData(50.8, 8.0, 406)] // 406,4 → 406
    [InlineData(0.0625, 8.0, 1)] // 0,5 → yukarı
    [InlineData(100, 12.0, 1200)]
    public void MmToDotsRounded(double mm, double dpmm, int expected) =>
        Assert.Equal(expected, Units.MmToDotsRounded(mm, dpmm));

    [Fact]
    public void Dpi203_Is8DotsPerMm()
    {
        Assert.Equal(8.0, PrintResolution.Dpi203.DotsPerMm);
        Assert.Equal(203.2, PrintResolution.Dpi203.Dpi, 6);
        Assert.Equal(800, Units.MmToDotsRounded(100, PrintResolution.Dpi203.DotsPerMm));
        Assert.Equal(100, Units.BytesPerRow(800));
    }

    [Fact]
    public void PointAndDipConversions()
    {
        Assert.Equal(25.4, Units.PointsToMm(72), 9);
        Assert.Equal(96, Units.MmToDip(25.4), 9);
        Assert.Equal(10, Units.DipToMm(Units.MmToDip(10)), 9);
    }
}

public class FieldTemplateTests
{
    private sealed class Dict(Dictionary<string, string> values) : IFieldValueSource
    {
        public bool TryGetValue(string name, string? format, out string value) => values.TryGetValue(name, out value!);
    }

    [Fact]
    public void Resolve_ReplacesFields_AndReportsMissing()
    {
        var source = new Dict(new() { ["UrunAdi"] = "Melamin Tepsi", ["Fiyat"] = "120" });
        var result = FieldTemplate.Resolve("{UrunAdi} - {Fiyat} TL {Yok}", source);
        Assert.Equal("Melamin Tepsi - 120 TL {Yok}", result.Text);
        Assert.Equal(["Yok"], result.MissingFields);
        Assert.True(result.HasFields);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void Escapes_AndUnbalancedBraces_AreLiteral()
    {
        Assert.Equal("{UrunAdi}", FieldTemplate.Resolve("{{UrunAdi}}", null).Text);
        Assert.False(FieldTemplate.ContainsFields("{{UrunAdi}}"));
        Assert.Equal("a { b", FieldTemplate.Resolve("a { b", null).Text);
        Assert.False(FieldTemplate.ContainsFields("a { b"));
        Assert.False(FieldTemplate.ContainsFields("{}"));
        Assert.True(FieldTemplate.ContainsFields("x {Barkod} y"));
    }

    [Fact]
    public void Tokens_IncludeFormat()
    {
        var tokens = FieldTemplate.GetTokens("No: {SIRA:0000} {Fiyat:0.00}");
        Assert.Equal(2, tokens.Count);
        Assert.Equal("SIRA", tokens[0].Name);
        Assert.Equal("0000", tokens[0].Format);
        Assert.Equal("Fiyat", tokens[1].Name);
    }

    [Fact]
    public void RecordSource_MatchesNamesInsensitiveToTurkishCharsAndSpaces()
    {
        var source = new RecordFieldSource(["Ürün Adı", "BARKOD", "Fiyat"], ["Çorba Kasesi", "0012345678905", "1234,5"]);
        Assert.Equal("Çorba Kasesi", FieldTemplate.Resolve("{UrunAdi}", source).Text);
        Assert.Equal("Çorba Kasesi", FieldTemplate.Resolve("{ürün adı}", source).Text);
        Assert.Equal("0012345678905", FieldTemplate.Resolve("{Barkod}", source).Text); // baştaki sıfırlar korunur
        Assert.Equal("1.234,50", FieldTemplate.Resolve("{Fiyat:#,##0.00}", source).Text);
    }

    [Fact]
    public void BuiltIns_SequenceAndDate()
    {
        var builtIns = new BuiltInFieldSource(7, new DateTime(2026, 10, 13, 9, 5, 0));
        Assert.Equal("0007", FieldTemplate.Resolve("{SIRA:0000}", builtIns).Text);
        Assert.Equal("13.10.2026", FieldTemplate.Resolve("{TARIH}", builtIns).Text);
        Assert.Equal("13.10.2026", FieldTemplate.Resolve("{Tarih}", builtIns).Text);
        Assert.Equal("09:05", FieldTemplate.Resolve("{SAAT}", builtIns).Text);
        var composite = new CompositeFieldSource(new RecordFieldSource(["Ad"], ["X"]), builtIns);
        Assert.Equal("X-7", FieldTemplate.Resolve("{Ad}-{SIRA}", composite).Text);
    }
}

public class ModelJsonTests
{
    [Fact]
    public void Document_RoundTrips_AllElementTypesAndProperties()
    {
        var doc = LabelDocument.CreateBlank();
        doc.Name = "Test Şablonu";
        doc.Label.GapMm = 2.5;
        doc.Label.Media = MediaType.BlackMark;
        doc.Elements.Add(new TextElement
        {
            X = 5.25, Y = 7.5, Width = 60, Height = 12, Rotation = 15, Text = "Şekerlik {UrunAdi}",
            FontFamily = "Segoe UI", FontSize = 14.5, Bold = true, HorizontalAlignment = TextHorizontalAlignment.Center,
            AutoShrink = true, TextColor = InkColor.White, Background = FillStyle.Black, Locked = true,
        });
        doc.Elements.Add(new BarcodeElement
        {
            X = 10, Y = 30, Width = 50, Height = 25, Symbology = BarcodeType.Ean13, Content = "{Barkod}", ShowText = false,
            Rotation = 90, QrErrorCorrection = QrErrorCorrectionLevel.H,
        });
        doc.Elements.Add(new ImageElement
        {
            AssetId = "abc.png", Method = ImageConversionMethod.FloydSteinberg, Threshold = 100, Brightness = -20,
            Contrast = 35, Invert = true, FitMode = ImageFitMode.Cover, CropLeft = 0.1,
        });
        doc.Elements.Add(new ShapeElement { Kind = ShapeKind.Line, LineOrientation = LineOrientation.Vertical, StrokeWidth = 0.8 });
        doc.Guides.Add(new GuideLine { Orientation = GuideOrientation.Vertical, Position = 50 });

        string json = ModelJson.Serialize(doc);
        Assert.Contains("Şekerlik", json); // Türkçe karakter kaçışsız
        var back = ModelJson.DeserializeDocument(json);

        Assert.Equal("Test Şablonu", back.Name);
        Assert.Equal(2.5, back.Label.GapMm);
        Assert.Equal(MediaType.BlackMark, back.Label.Media);
        Assert.Equal(4, back.Elements.Count);
        var text = Assert.IsType<TextElement>(back.Elements[0]);
        Assert.Equal((5.25, 7.5, 60.0, 12.0, 15.0), (text.X, text.Y, text.Width, text.Height, text.Rotation));
        Assert.Equal("Segoe UI", text.FontFamily);
        Assert.True(text.Bold && text.AutoShrink && text.Locked);
        Assert.Equal(InkColor.White, text.TextColor);
        var barcode = Assert.IsType<BarcodeElement>(back.Elements[1]);
        Assert.Equal(BarcodeType.Ean13, barcode.Symbology);
        Assert.Equal(90, barcode.Rotation);
        Assert.Equal(QrErrorCorrectionLevel.H, barcode.QrErrorCorrection);
        Assert.False(barcode.ShowText);
        var image = Assert.IsType<ImageElement>(back.Elements[2]);
        Assert.Equal((ImageConversionMethod.FloydSteinberg, 100, -20, 35, true), (image.Method, image.Threshold, image.Brightness, image.Contrast, image.Invert));
        var shape = Assert.IsType<ShapeElement>(back.Elements[3]);
        Assert.Equal(LineOrientation.Vertical, shape.LineOrientation);
        Assert.Single(back.Guides);
        Assert.Equal(doc.Elements[0].Id, back.Elements[0].Id);
    }

    [Fact]
    public void CloneElement_WithNewId()
    {
        var text = new TextElement { Text = "A" };
        var copy = ModelJson.CloneElement(text, newId: true);
        Assert.NotEqual(text.Id, copy.Id);
        Assert.Equal("A", ((TextElement)copy).Text);
    }

    [Fact]
    public void BarcodeRotation_SnapsToQuarterTurns()
    {
        var b = new BarcodeElement { Rotation = 100 };
        Assert.Equal(90, b.Rotation);
        b.Rotation = 314;
        Assert.Equal(270, b.Rotation);
        b.Rotation = -90;
        Assert.Equal(270, b.Rotation);
        var t = new TextElement { Rotation = -30 };
        Assert.Equal(330, t.Rotation);
    }
}

public class ElementGeometryTests
{
    [Fact]
    public void DotBox_DependsOnlyOnSizeForDimensions()
    {
        var e = new TextElement { X = 10.06, Y = 0, Width = 25, Height = 10 };
        var a = ElementGeometry.GetDotBox(e, 8);
        e.X = 10.01;
        var b = ElementGeometry.GetDotBox(e, 8);
        Assert.Equal(a.Width, b.Width);
        Assert.Equal(200, a.Width);
        Assert.Equal(80, a.Height);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 1)]
    [InlineData(180, 2)]
    [InlineData(270, 3)]
    [InlineData(360, 0)]
    [InlineData(45, -1)]
    [InlineData(89.999, 1)]
    public void QuarterTurns(double deg, int expected) => Assert.Equal(expected, ElementGeometry.GetQuarterTurns(deg));

    [Fact]
    public void HitTest_RespectsRotation()
    {
        var e = new ShapeElement { X = 0, Y = 0, Width = 40, Height = 10, Rotation = 90 };
        // 90° döndürülmüş kutu merkez (20,5) etrafında dikey: x 15..25, y -15..25
        Assert.True(ElementGeometry.HitTest(e, 20, -10));
        Assert.False(ElementGeometry.HitTest(e, 35, 5));
        var bounds = ElementGeometry.GetRotatedBoundsMm(e);
        Assert.Equal(15, bounds.X, 6);
        Assert.Equal(-15, bounds.Y, 6);
        Assert.Equal(10, bounds.Width, 6);
        Assert.Equal(40, bounds.Height, 6);
    }
}
