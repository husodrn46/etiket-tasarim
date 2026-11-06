using AkelEtiket.Core.Model;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;

namespace AkelEtiket.BarTender.Tests;

public sealed class BtwReaderTests
{
    [Fact]
    public void RejectsFilesThatAreNotBarTender()
    {
        Assert.False(BtwReader.LooksLikeBtw("PK\u0003\u0004"u8));
        var ex = Assert.Throws<BtwFormatException>(() => BtwReader.Read("merhaba dünya, bu bir etiket değil"u8.ToArray()));
        Assert.Contains(".btw", ex.Message);
    }

    [Fact]
    public void ReportsMissingDesignStreamInsteadOfCrashing()
    {
        byte[] file = System.Text.Encoding.UTF8.GetBytes("\r\nBar Tender Format File\r\n" + new string('-', 46) + "\r\nApplication: Version=2016 R7\r\n" + new string('-', 46) + "\r\nxx");
        Assert.Throws<BtwFormatException>(() => BtwReader.Read(file));
    }

    [Fact]
    public void ReadsLabelSizePrinterAndObjects()
    {
        byte[] file = new SyntheticBtw { LabelWidth = 787, LabelHeight = 1575, Columns = 5, Dpi = 300 }
            .Line("Çizgi 1", 340, 1181)
            .Ean13("Barkod 1", 298, 431, 270, "8692018000183", 666, 173, 4, "Arial")
            .Text("Yazı 1", 327, 105, 6, 270, "BENEKLİ ÇUKUR TABAK", points: 6, bold: true, widthScale: 948)
            .Text("Yazı 3", 175, 120, 3, 270, "KOD:\rT18 BENEKLİ", points: 4, bold: true, fixedWidth: 213, alignment: 1, border: (8, 1, 39))
            .Build();

        var label = BtwReader.Read(file);

        Assert.Equal((787, 1575, 5, 300), (label.Width, label.Height, label.Columns, label.PrinterDpi));
        Assert.Equal((4093, 79, 0), (label.PageWidth, label.SideMargin, label.ColumnGap));
        Assert.Equal("2016 R7", label.Application);
        Assert.Collection(label.Objects,
            o => Assert.IsType<BtwLine>(o),
            o =>
            {
                var b = Assert.IsType<BtwBarcode>(o);
                Assert.Equal(("BcEAN13Data", "8692018000183", 666, 173), (b.ClassName, b.Data, b.BarsWidth, b.BarHeight));
                Assert.Equal(new BtwPlacement(298, 431, BtwAnchor.TopLeft, 270, true), b.Placement);
            },
            o =>
            {
                var t = Assert.IsType<BtwText>(o);
                Assert.Equal("BENEKLİ ÇUKUR TABAK", t.Content);
                Assert.Equal(BtwAnchor.BottomLeft, t.Placement.Anchor);
                Assert.Equal((6.0, true, "Arial", 948), (t.Font!.SizePoints, t.Font.Bold, t.Font.Face, t.WidthScale));
                Assert.Null(t.FixedWidth);
                Assert.Null(t.Border);
            },
            o =>
            {
                // İkinci yazı aynı sınıfın tekrarıdır (sınıf adı yazılmaz); yapısından tanınmalı.
                var t = Assert.IsType<BtwText>(o);
                Assert.Equal("KOD:\rT18 BENEKLİ", t.Content);
                Assert.Equal((213, 1), (t.FixedWidth, t.Alignment));
                Assert.Equal(new BtwBorder(8, 1, 39), t.Border);
                Assert.Equal(BtwAnchor.MiddleLeft, t.Placement.Anchor);
            });
    }

    [Fact]
    public void ReadsEmbeddedPictureAndBox()
    {
        byte[] png = SyntheticBtw.TinyPng();
        byte[] file = new SyntheticBtw { LabelWidth = 3937, LabelHeight = 5906 }
            .Picture("Resim 1", 0, 5838, 0, 90, 2690, 1952, png)
            .Box("Kutu 1", 0, 64, 1575, 2564, 14, corner: 2, radius: 250)
            .Build();

        var label = BtwReader.Read(file);

        var picture = Assert.IsType<BtwPicture>(label.Objects[0]);
        Assert.Equal(png, picture.Data);
        Assert.Equal((".png", 2690, 1952), (picture.Extension, picture.Width, picture.Height));
        var box = Assert.IsType<BtwBox>(label.Objects[1]);
        Assert.Equal((1575, 2564, 14, 2, 250), (box.Width, box.Height, box.Thickness, box.Corner, box.CornerRadius));
    }
}

public sealed class BtwConverterTests
{
    private static BtwImportResult Import(SyntheticBtw btw, string name = "Deneme") =>
        BtwImporter.Convert(BtwReader.Read(btw.Build()), name);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 270)]
    [InlineData(180, 180)]
    [InlineData(270, 90)]
    public void RotationBecomesClockwise(double barTender, double expected) =>
        Assert.Equal(expected, BtwImporter.ToClockwise(barTender));

    [Fact]
    public void LabelSizeNameAndMultiAcrossNoteAreKept()
    {
        var result = Import(new SyntheticBtw { LabelWidth = 491, LabelHeight = 1378, Columns = 5 }
            .Text("Yazı 1", 104, 1358, 3, 90, "LEVA 12CM JOKER KASE 012", points: 5, bold: true, fixedWidth: 1386), "LV012");

        var doc = result.Project.Document;
        Assert.Equal("LV012", doc.Name);
        Assert.Equal(12.47, doc.Label.WidthMm, 2);
        Assert.Equal(35.0, doc.Label.HeightMm, 2);
        // Yan yana rulo: sayfa kaydındaki kenar payı (79 mil) ve bitişik etiketler.
        Assert.Equal((5, 2.0, 0.0), (doc.Label.Columns, doc.Label.SideMarginMm, doc.Label.ColumnGapMm));
        Assert.Equal(66.35, doc.Label.RowWidthMm, 1);
        Assert.Contains(result.Warnings, w => w.Contains("5 yan yana"));
        Assert.Null(result.Project.FilePath);
    }

    [Fact]
    public void BarcodeBarsStartAtTheBarTenderAnchorAndModuleSnapsToPrinterDots()
    {
        // 666 mil / 95 modül = 7,01 mil → 300 dpi'da 2 nokta (0,1693 mm).
        var result = Import(new SyntheticBtw { Dpi = 300 }.Ean13("Barkod 1", 298, 431, 270, "8692018000183", 666, 173, 4));

        var barcode = Assert.IsType<BarcodeElement>(Assert.Single(result.Project.Document.Elements));
        Assert.Equal(BarcodeType.Ean13, barcode.Symbology);
        Assert.Equal("8692018000183", barcode.Content);
        Assert.Equal(90, barcode.Rotation);
        double module = 2 * 25.4 / 300;
        Assert.Equal((95 + 22) * module, barcode.Width, 1);
        // Saat yönünde 90°: kendi sol üst köşesi (çubukların başı) etikette sağ üstte, BarTender'ın X/Y'sinde olmalı.
        double cx = barcode.X + barcode.Width / 2, cy = barcode.Y + barcode.Height / 2;
        double barsTopRightX = cx + barcode.Height / 2;
        double barsStartY = cy - barcode.Width / 2 + 11 * module;
        Assert.Equal(298 * 0.0254, barsTopRightX, 1);
        Assert.Equal(431 * 0.0254, barsStartY, 1);

        using var renderer = new LabelRenderer(result.Project.Assets);
        var render = renderer.RenderLabel(result.Project.Document, new RenderOptions { DotsPerMm = 300 / 25.4, Mode = RenderMode.Print });
        Assert.False(render.HasErrors, string.Join("; ", render.Errors.Select(e => e.Message)));
        Assert.Equal(2, Assert.Single(render.Elements).Barcode!.Layout!.ModuleDots);
    }

    [Fact]
    public void TextKeepsFontCharacterWidthAndAnchoredPosition()
    {
        var result = Import(new SyntheticBtw()
            .Text("Yazı 1", 327, 105, 6, 270, "BENEKLİ ÇUKUR TABAK", points: 6, bold: true, face: "Arial", widthScale: 948));

        var text = Assert.IsType<TextElement>(Assert.Single(result.Project.Document.Elements));
        Assert.Equal(("BENEKLİ ÇUKUR TABAK", "Arial", 6.0, true), (text.Text, text.FontFamily, text.FontSize, text.Bold));
        Assert.Equal(94.8, text.CharacterWidth);
        Assert.Equal(90, text.Rotation);
        Assert.False(text.WordWrap);
        // Sol alt bağlantı + saat yönünde 90°: yazının başlangıcı etikette üstte, X/Y noktasından başlar.
        double top = text.Y + text.Height / 2 - text.Width / 2;
        Assert.Equal(105 * 0.0254, top, 1);
        Assert.Equal(LabelDocument.CurrentFormatVersion, result.Project.Document.GetRequiredFormatVersion());
    }

    [Fact]
    public void FixedWidthTextIsWrappedLikeBarTenderAndBorderBecomesRoundedFrame()
    {
        var result = Import(new SyntheticBtw()
            .Text("Yazı 3", 175, 120, 3, 270, "KOD:\rT18 BENEKLİ", points: 4, bold: true, face: "Arial", fixedWidth: 300, alignment: 1, border: (8, 1, 39)));

        var elements = result.Project.Document.Elements;
        var frame = Assert.IsType<ShapeElement>(elements[0]);
        var text = Assert.IsType<TextElement>(elements[1]);
        Assert.Equal("KOD:\nT18\nBENEKLİ", text.Text);
        Assert.Equal(TextHorizontalAlignment.Center, text.HorizontalAlignment);
        Assert.Equal(ShapeKind.Rectangle, frame.Kind);
        Assert.Equal(0.99, frame.CornerRadius, 2);
        Assert.Equal(0.203, frame.StrokeWidth, 3);
        Assert.Equal("", frame.Name);
        // Çerçeve ve yazı aynı merkezde (orta-sol bağlantı, aynı döndürme).
        Assert.Equal(frame.Rotation, text.Rotation);
        Assert.Equal(frame.Y + frame.Height / 2, text.Y + text.Height / 2, 1);

        using var renderer = new LabelRenderer(result.Project.Assets);
        foreach (double dpmm in new[] { 8.0, 12.0 })
        {
            var render = renderer.RenderLabel(result.Project.Document, new RenderOptions { DotsPerMm = dpmm });
            Assert.DoesNotContain(render.Issues, i => i.Message.Contains("sığmıyor"));
        }
    }

    [Fact]
    public void TrailingLineBreaksAndLeadingSpacesFollowBarTender()
    {
        var result = Import(new SyntheticBtw()
            .Text("Yazı 1", 64, 394, 0, 0, "   LİSANSLI\rÜRÜN!!!\r", points: 10, fixedWidth: 3000));

        var text = Assert.IsType<TextElement>(Assert.Single(result.Project.Document.Elements));
        Assert.Equal("   LİSANSLI\nÜRÜN!!!", text.Text);
    }

    [Fact]
    public void WordLongerThanTheBoxIsSplitButSmallOverflowIsNot()
    {
        static double Width(string s) => s.Length;
        Assert.Equal(["KOD:", "119"], BtwImporter.Wrap(["KOD:119"], 4.5, Width));
        Assert.Equal(["ABCDE"], BtwImporter.Wrap(["ABCDE"], 4.95, Width));
        Assert.Equal(["009", "S.B"], BtwImporter.Wrap(["009 S.B"], 6.9, Width));
        Assert.Equal(["  AB CD"], BtwImporter.Wrap(["  AB CD"], 10, Width));
    }

    [Fact]
    public void PictureIsEmbeddedAsAssetAndStretchedToBarTenderSize()
    {
        byte[] png = SyntheticBtw.TinyPng();
        var result = Import(new SyntheticBtw { LabelWidth = 3937, LabelHeight = 5906 }
            .Picture("Resim 1", 0, 5838, 0, 90, 2690, 1952, png));

        var image = Assert.IsType<ImageElement>(Assert.Single(result.Project.Document.Elements));
        Assert.True(result.Project.Assets.TryGet(image.AssetId, out var asset));
        Assert.Equal(png, asset.Data);
        Assert.Equal(ImageFitMode.Stretch, image.FitMode);
        Assert.Equal((68.33, 49.58, 270.0), (Math.Round(image.Width, 2), Math.Round(image.Height, 2), image.Rotation));
        // Saat yönünün tersine 90°: kendi sol üst köşesi etikette sol altta (0; 148,3 mm).
        Assert.Equal(0, image.X + image.Width / 2 - image.Height / 2, 2);
        Assert.Equal(5838 * 0.0254, image.Y + image.Height / 2 + image.Width / 2, 2);
    }

    [Fact]
    public void LinesAndObjectsOutsideTheLabelAreSkippedWithNotes()
    {
        var result = Import(new SyntheticBtw()
            .Line("Çizgi 1", 340, 1181)
            .Box("Kutu 1", 1015, 1149, 113, 378, 6)
            .Text("Yazı 1", 100, 100, 0, 0, "A"));

        Assert.Single(result.Project.Document.Elements);
        Assert.Equal((1, 2), (result.ImportedCount, result.SkippedCount));
        Assert.Contains(result.Warnings, w => w.Contains("çizgi"));
        Assert.Contains(result.Warnings, w => w.Contains("dışında"));
    }

    [Fact]
    public void ChamferedBoxBecomesRoundedRectangleWithNote()
    {
        var result = Import(new SyntheticBtw { LabelWidth = 3937, LabelHeight = 5906 }
            .Box("Kutu 1", 0, 64, 1575, 2564, 14, corner: 2, radius: 250));

        var shape = Assert.IsType<ShapeElement>(Assert.Single(result.Project.Document.Elements));
        Assert.Equal(("Kutu 1", 40.0, 65.1, 6.35), (shape.Name, Math.Round(shape.Width, 1), Math.Round(shape.Height, 1), Math.Round(shape.CornerRadius, 2)));
        Assert.Contains(result.Warnings, w => w.Contains("pah"));
    }

    [Fact]
    public void ImportedProjectSavesAndReopensWithoutWarnings()
    {
        var result = Import(new SyntheticBtw()
            .Ean13("Barkod 1", 298, 431, 270, "8692018000183", 666, 173, 4)
            .Text("Yazı 1", 327, 105, 6, 270, "BENEKLİ ÇUKUR TABAK", points: 6, bold: true, widthScale: 948));
        string folder = Directory.CreateTempSubdirectory("btw-kayit-").FullName;
        try
        {
            string path = Path.Combine(folder, "deneme.akelabel");
            ProjectFile.Save(result.Project, path);
            var loaded = ProjectFile.Load(path);
            Assert.Empty(loaded.Warnings);
            var text = loaded.Project.Document.Elements.OfType<TextElement>().Single();
            Assert.Equal(94.8, text.CharacterWidth);
            Assert.Equal(LabelDocument.CurrentFormatVersion, loaded.Project.Document.FormatVersion);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

public sealed class BtwFolderConverterTests
{
    [Fact]
    public void ConvertsTreeKeepsOriginalsSkipsExistingAndWritesReport()
    {
        string root = Directory.CreateTempSubdirectory("btw-klasor-").FullName;
        try
        {
            string source = Path.Combine(root, "BARKODLAR");
            Directory.CreateDirectory(Path.Combine(source, "EV SERİSİ"));
            byte[] good = new SyntheticBtw().Text("Yazı 1", 100, 100, 0, 0, "Ürün").Build();
            File.WriteAllBytes(Path.Combine(source, "A.btw"), good);
            File.WriteAllBytes(Path.Combine(source, "EV SERİSİ", "B.btw"), good);
            File.WriteAllText(Path.Combine(source, "bozuk.btw"), "bu bir BarTender dosyası değil");
            string target = Path.Combine(root, "Aktarılan");

            var first = BtwFolderConverter.Convert(source, target);

            Assert.Equal((2, 0, 1), (first.Converted, first.Skipped, first.Failed));
            Assert.True(File.Exists(Path.Combine(target, "A.akelabel")));
            Assert.True(File.Exists(Path.Combine(target, "EV SERİSİ", "B.akelabel")));
            Assert.Equal(good, File.ReadAllBytes(Path.Combine(source, "A.btw")));
            Assert.NotNull(ProjectFile.Load(Path.Combine(target, "A.akelabel")).Project);
            Assert.NotNull(TemplateLibrary.ReadInfo(Path.Combine(target, "A.akelabel")).PreviewPng);
            string report = File.ReadAllText(first.ReportPath!);
            Assert.Contains("bozuk.btw", report);

            var second = BtwFolderConverter.Convert(source, target);
            Assert.Equal((0, 2, 1), (second.Converted, second.Skipped, second.Failed));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RefusesTargetInsideSource()
    {
        string root = Directory.CreateTempSubdirectory("btw-ic-").FullName;
        try
        {
            Assert.True(BtwFolderConverter.IsInside(Path.Combine(root, "a", "b"), root));
            Assert.False(BtwFolderConverter.IsInside(root + "-baska", root));
            Assert.Throws<ArgumentException>(() => BtwFolderConverter.Convert(root, Path.Combine(root, "çıktı")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
