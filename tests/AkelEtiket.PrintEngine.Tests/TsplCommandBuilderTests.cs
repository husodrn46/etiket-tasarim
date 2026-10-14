using System.Globalization;
using System.Text;
using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;

namespace AkelEtiket.PrintEngine.Tests;

public class TsplCommandBuilderTests
{
    private static LabelMedia Gap100 => new(100, 100, 3, 0, MediaType.Gap);

    private static LabelPrinterSettings Settings(Action<LabelPrinterSettings>? configure = null)
    {
        var s = new LabelPrinterSettings { PrinterName = "test" };
        configure?.Invoke(s);
        return s;
    }

    private static string Ascii(byte[] data) => Encoding.ASCII.GetString(data);

    [Fact]
    public void Label100x100At8DotsPerMm_IsExactBitmapCommand()
    {
        var label = new MonoBitmap(800, 800);
        byte[] data = TsplCommandBuilder.BuildLabel(label, Settings(), 1);

        const string header = "CLS\r\nBITMAP 0,0,100,800,0,";
        const string tail = "\r\nPRINT 1,1\r\n";
        Assert.Equal(header, Encoding.ASCII.GetString(data, 0, header.Length));
        Assert.Equal(header.Length + 80_000 + tail.Length, data.Length);
        Assert.Equal(tail, Encoding.ASCII.GetString(data, header.Length + 80_000, tail.Length));
        // Tamamen beyaz etiket: TSPL'de beyaz = 1 bit → tüm baytlar 0xFF.
        Assert.True(data.AsSpan(header.Length, 80_000).IndexOfAnyExcept((byte)0xFF) < 0);
    }

    [Fact]
    public void Polarity_BlackPixelIsZeroBit_AndMsbIsLeftmost()
    {
        var label = new MonoBitmap(16, 2);
        label[0, 0] = true;   // satır 0, en soldaki nokta
        label[9, 0] = true;   // satır 0, ikinci baytın 2. noktası
        label[15, 1] = true;  // satır 1, son nokta
        byte[] packed = TsplCommandBuilder.PackBitmap(label);
        Assert.Equal([0x7F, 0xBF, 0xFF, 0xFE], packed);

        var parsed = TsplInspector.Parse(TsplCommandBuilder.BuildLabel(label, Settings(), 1), includePayload: true);
        var bitmap = Assert.Single(parsed, c => c.Name == "BITMAP");
        Assert.Equal("0,0,2,2,0", bitmap.Arguments);
        Assert.Equal(packed, bitmap.Payload);
    }

    [Fact]
    public void OddWidth_UsesRoundedUpBytes_AndPaddingBitsAreWhite()
    {
        var label = new MonoBitmap(813, 3);
        label.Fill(true); // her nokta siyah
        byte[] packed = TsplCommandBuilder.PackBitmap(label);
        Assert.Equal(102 * 3, packed.Length);
        for (int row = 0; row < 3; row++)
        {
            for (int b = 0; b < 101; b++) Assert.Equal(0x00, packed[row * 102 + b]);
            // 813 = 101*8 + 5 → son baytta 5 siyah (0) + 3 dolgu (1, beyaz) biti
            Assert.Equal(0b0000_0111, packed[row * 102 + 101]);
        }
        var command = TsplInspector.Parse(TsplCommandBuilder.BuildLabel(label, Settings(), 1)).Single(c => c.Name == "BITMAP");
        Assert.Equal("0,0,102,3,0", command.Arguments);
        Assert.Equal(306, command.PayloadLength);
    }

    [Fact]
    public void Setup_GapLabel_ExactHeaderLines()
    {
        string setup = Ascii(TsplCommandBuilder.BuildSetup(Gap100, Settings()));
        Assert.Equal(
            "SIZE 100 mm,100 mm\r\n" +
            "GAP 3 mm,0 mm\r\n" +
            "DIRECTION 1,0\r\n" +
            "REFERENCE 0,0\r\n" +
            "DENSITY 8\r\n" +
            "SPEED 4\r\n",
            setup);
    }

    [Fact]
    public void Setup_UsesInvariantDecimals_EvenUnderTurkishCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var media = new LabelMedia(101.6, 50.8, 2.54, 0.5, MediaType.Gap);
            string setup = Ascii(TsplCommandBuilder.BuildSetup(media, Settings()));
            Assert.StartsWith("SIZE 101.6 mm,50.8 mm\r\nGAP 2.54 mm,0.5 mm\r\n", setup);
            Assert.DoesNotContain(",6 mm", setup);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(MediaType.BlackMark, "BLINE 5 mm,1.5 mm")]
    [InlineData(MediaType.Continuous, "GAP 0,0")]
    [InlineData(MediaType.Gap, "GAP 5 mm,1.5 mm")]
    public void Setup_MediaTypeCommand(MediaType type, string expected)
    {
        var media = new LabelMedia(100, 150, 5, 1.5, type);
        var commands = TsplInspector.Parse(TsplCommandBuilder.BuildSetup(media, Settings()));
        Assert.Equal("SIZE 100 mm,150 mm", commands[0].ToString());
        Assert.Equal(expected, commands[1].ToString());
    }

    [Fact]
    public void Rotate180_SendsDirectionZero()
    {
        string setup = Ascii(TsplCommandBuilder.BuildSetup(Gap100, Settings(s => s.Rotate180 = true)));
        Assert.Contains("DIRECTION 0,0\r\n", setup);
        Assert.DoesNotContain("DIRECTION 1", setup);
    }

    [Fact]
    public void DensityAndSpeed_AreClampedWhenSent_AndReportedByValidate()
    {
        var settings = Settings(s =>
        {
            s.Density = 22;
            s.Speed = 9;
        });
        string setup = Ascii(TsplCommandBuilder.BuildSetup(Gap100, settings));
        Assert.Contains("DENSITY 15\r\n", setup);
        Assert.Contains("SPEED 6\r\n", setup);
        var problems = settings.Validate();
        Assert.Contains(problems, p => p.Contains("yoğunluğu"));
        Assert.Contains(problems, p => p.Contains("hızı"));

        settings.Density = -3;
        settings.Speed = 1;
        setup = Ascii(TsplCommandBuilder.BuildSetup(Gap100, settings));
        Assert.Contains("DENSITY 0\r\n", setup);
        Assert.Contains("SPEED 2\r\n", setup);
        Assert.Empty(Settings().Validate());
    }

    [Theory]
    [InlineData(TearMode.Unchanged, null)]
    [InlineData(TearMode.On, "SET TEAR ON")]
    [InlineData(TearMode.Off, "SET TEAR OFF")]
    public void TearMode_OnlySentWhenRequested(TearMode mode, string? expected)
    {
        string setup = Ascii(TsplCommandBuilder.BuildSetup(Gap100, Settings(s => s.TearMode = mode)));
        if (expected is null) Assert.DoesNotContain("SET TEAR", setup);
        else Assert.Contains(expected + "\r\n", setup);
    }

    [Fact]
    public void PositiveOffset_MovesBitmapAndKeepsItInsideTheLabel()
    {
        var label = new MonoBitmap(800, 800);
        label[0, 0] = true;
        label[799, 799] = true; // kaydırma sonrası etiket dışına çıkar → kırpılır
        var settings = Settings(s =>
        {
            s.OffsetXMm = 2;   // 16 nokta sağa
            s.OffsetYMm = 1;   // 8 nokta aşağı
        });
        var bitmap = TsplInspector.Parse(TsplCommandBuilder.BuildLabel(label, settings, 1), includePayload: true)
            .Single(c => c.Name == "BITMAP");
        Assert.Equal("16,8,98,792,0", bitmap.Arguments); // 784 nokta = 98 bayt, 792 satır
        Assert.Equal(0x7F, bitmap.Payload![0]);          // sol üst nokta siyah
        Assert.True(bitmap.Payload.AsSpan(1).IndexOfAnyExcept((byte)0xFF) < 0); // sağ alt kırpıldı
    }

    [Fact]
    public void NegativeOffset_CropsInsteadOfSendingNegativeCoordinates()
    {
        var label = new MonoBitmap(800, 800);
        label[8, 4] = true;
        var settings = Settings(s =>
        {
            s.OffsetXMm = -1;    // 8 nokta sola
            s.OffsetYMm = -0.5;  // 4 nokta yukarı
        });
        var bitmap = TsplInspector.Parse(TsplCommandBuilder.BuildLabel(label, settings, 1), includePayload: true)
            .Single(c => c.Name == "BITMAP");
        Assert.Equal("0,0,99,796,0", bitmap.Arguments);
        Assert.Equal(0x7F, bitmap.Payload![0]); // (8,4) noktası artık (0,0)
    }

    [Fact]
    public void LabelWiderThanPrintHead_IsClippedWithWarning()
    {
        var label = new MonoBitmap(880, 10); // 110 mm @ 8 nokta/mm, XP-470B en fazla 108 mm (864 nokta)
        var warnings = new List<string>();
        var bitmap = TsplInspector.Parse(TsplCommandBuilder.BuildLabel(label, Settings(), 1, warnings))
            .Single(c => c.Name == "BITMAP");
        Assert.Equal("0,0,108,10,0", bitmap.Arguments);
        Assert.Contains(warnings, w => w.Contains("azami baskı genişliğini"));

        var mediaWarnings = TsplCommandBuilder.CheckLabel(new LabelMedia(110, 10, 3, 0, MediaType.Gap), Settings(), 880, 80);
        Assert.Contains(mediaWarnings, w => w.Contains("110 mm"));
    }

    [Fact]
    public void CheckLabel_WarnsWhenBitmapDoesNotMatchMedia()
    {
        Assert.Empty(TsplCommandBuilder.CheckLabel(Gap100, Settings(), 800, 800));
        Assert.NotEmpty(TsplCommandBuilder.CheckLabel(Gap100, Settings(), 1200, 1200));
    }

    [Fact]
    public void Job_ContainsSetupOnceAndEveryLabelWithItsCopies()
    {
        var pages = new[]
        {
            new LabelPage(new MonoBitmap(800, 800), 1),
            new LabelPage(new MonoBitmap(800, 800), 2),
            new LabelPage(new MonoBitmap(800, 800), 3),
        };
        var commands = TsplInspector.Parse(TsplCommandBuilder.BuildJob(Gap100, Settings(), pages));
        Assert.Equal(
            ["SIZE", "GAP", "DIRECTION", "REFERENCE", "DENSITY", "SPEED",
             "CLS", "BITMAP", "PRINT", "CLS", "BITMAP", "PRINT", "CLS", "BITMAP", "PRINT"],
            commands.Select(c => c.Name));
        Assert.Equal(["1,1", "1,2", "1,3"], commands.Where(c => c.Name == "PRINT").Select(c => c.Arguments));
        Assert.All(commands.Where(c => c.Name == "BITMAP"), c => Assert.Equal(80_000, c.PayloadLength));
    }

    [Fact]
    public void Copies_AreClamped()
    {
        string text = Ascii(TsplCommandBuilder.BuildLabel(new MonoBitmap(8, 1), Settings(), 0));
        Assert.EndsWith("PRINT 1,1\r\n", text);
        text = Ascii(TsplCommandBuilder.BuildLabel(new MonoBitmap(8, 1), Settings(), int.MaxValue));
        Assert.EndsWith($"PRINT 1,{TsplCommandBuilder.MaxCopies}\r\n", text);
    }

    [Fact]
    public void Calibration_FeedAndSelfTest()
    {
        Assert.Equal("SIZE 100 mm,100 mm\r\nGAP 3 mm,0 mm\r\nGAPDETECT\r\n", Ascii(TsplCommandBuilder.BuildGapDetect(Gap100)));
        Assert.Equal("SIZE 100 mm,100 mm\r\nBLINE 3 mm,0 mm\r\nBLINEDETECT\r\n",
            Ascii(TsplCommandBuilder.BuildGapDetect(Gap100 with { Type = MediaType.BlackMark })));
        Assert.Equal("SIZE 100 mm,100 mm\r\nGAP 0,0\r\n",
            Ascii(TsplCommandBuilder.BuildGapDetect(Gap100 with { Type = MediaType.Continuous })));
        Assert.Equal("FORMFEED\r\n", Ascii(TsplCommandBuilder.BuildFormFeed()));
        Assert.Equal("SELFTEST\r\n", Ascii(TsplCommandBuilder.BuildSelfTest()));
    }

    [Fact]
    public void Media_ValidatesGapAndSize()
    {
        Assert.Empty(Gap100.Validate());
        Assert.NotEmpty((Gap100 with { GapMm = 0 }).Validate());
        Assert.Empty((Gap100 with { GapMm = 0, Type = MediaType.Continuous }).Validate());
        Assert.NotEmpty((Gap100 with { WidthMm = 0 }).Validate());
        Assert.Equal(800, Gap100.WidthDots(8));
        var fromSetup = LabelMedia.FromSetup(new LabelSetup { WidthMm = 58, HeightMm = 40, GapMm = 2, Media = MediaType.BlackMark });
        Assert.Equal(new LabelMedia(58, 40, 2, 0, MediaType.BlackMark), fromSetup);
    }

    [Fact]
    public void Settings_ValidateOffsetsAndPrinter()
    {
        var problems = new LabelPrinterSettings { OffsetXMm = 25, OffsetYMm = double.NaN }.Validate();
        Assert.Contains(problems, p => p.Contains("Yazıcı seçilmedi"));
        Assert.Contains(problems, p => p.Contains("Yatay ofset"));
        Assert.Contains(problems, p => p.Contains("Dikey ofset"));
        var clone = Settings(s => s.Density = 11).Clone();
        Assert.Equal(11, clone.Density);
    }
}
