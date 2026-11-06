using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;

namespace AkelEtiket.PrintEngine.Tests;

/// <summary>Yan yana rulo: etiketler satırlara sırayla dizilir, sayım etiket başınadır.</summary>
public class MultiAcrossTests
{
    // 5 × 20 mm, 2 mm kenar payı, bitişik: rulo 104 mm.
    private static readonly LabelMedia Roll = new(20, 40, 3, 0, MediaType.Gap) { Columns = 5, SideMarginMm = 2, ColumnGapMm = 0 };

    private static MonoBitmap Label(int id)
    {
        var bitmap = new MonoBitmap(160, 320);
        bitmap.FillRect(0, 0, 4 + id, 4, true);
        return bitmap;
    }

    [Fact]
    public void PaperIsTheWholeRow()
    {
        Assert.Equal(104, Roll.PaperWidthMm, 6);
        Assert.Equal(new LabelMedia(104, 40, 3, 0, MediaType.Gap), Roll.Paper);
        Assert.Equal(20, Roll.WidthMm);
        var single = new LabelMedia(20, 40, 3, 0, MediaType.Gap);
        Assert.Same(single, single.Paper);
        Assert.Equal(97.0, new LabelMedia(30, 10, 3, 0, MediaType.Gap) { Columns = 3, ColumnGapMm = 2.5, SideMarginMm = 1 }.PaperWidthMm, 6);
    }

    [Fact]
    public void FromSetupCarriesColumns()
    {
        var setup = new LabelSetup { WidthMm = 12.5, HeightMm = 35, Columns = 5, ColumnGapMm = 0.5, SideMarginMm = 1.27 };
        var media = LabelMedia.FromSetup(setup);
        Assert.Equal((5, 0.5, 1.27), (media.Columns, media.ColumnGapMm, media.SideMarginMm));
        Assert.Equal(setup.RowWidthMm, media.PaperWidthMm, 6);
    }

    [Fact]
    public void RowsKeepOrderCountEachLabelOnceAndCompressFullRows()
    {
        var a = Label(1);
        var b = Label(2);
        var c = Label(3);
        var builder = new LabelRowBuilder(Roll, 8);

        var afterA = builder.Add(a, 12);      // 2 tam satır A + 2 A beklemede
        var afterB = builder.Add(b, 2);       // A A B B beklemede
        var afterC = builder.Add(c, 4);       // A A B B C dolar, C C C beklemede
        var last = builder.Flush();

        var full = Assert.Single(afterA);
        Assert.Equal((2, 0), (full.Copies, full.CompletedLabels));
        Assert.Empty(afterB);
        var mixed = Assert.Single(afterC);
        Assert.Equal((1, 2), (mixed.Copies, mixed.CompletedLabels));
        Assert.NotNull(last);
        Assert.Equal((1, 1), (last.Copies, last.CompletedLabels));
        Assert.False(builder.HasPending);
        Assert.Null(builder.Flush());

        // Sütun yerleri: 2 mm + i × 20 mm, 8 nokta/mm.
        Assert.Equal(832, mixed.Bitmap.Width);
        foreach (var (column, width) in new[] { (0, 5), (1, 5), (2, 6), (3, 6), (4, 7) })
        {
            int x = 16 + column * 160;
            Assert.True(mixed.Bitmap.GetPixel(x + width - 1, 0));
            Assert.False(mixed.Bitmap.GetPixel(x + width, 0));
        }
        // Eksik son satırda boş sütunlar beyaz.
        Assert.False(last.Bitmap.GetPixel(16 + 3 * 160, 0));
    }

    [Fact]
    public void ExactMultipleCompletesTheLabelInItsFullRows()
    {
        var builder = new LabelRowBuilder(Roll, 8);
        var rows = builder.Add(Label(1), 10);
        var row = Assert.Single(rows);
        Assert.Equal((2, 1), (row.Copies, row.CompletedLabels));
        Assert.Null(builder.Flush());
    }

    [Fact]
    public void JobUsesRowWidthAndOneBitmapPerRow()
    {
        var settings = new LabelPrinterSettings { PrinterName = "Etiket", DotsPerMm = 8 };
        byte[] data = TsplCommandBuilder.BuildJob(Roll, settings, [new LabelPage(Label(1), 7), new LabelPage(Label(2), 1)]);
        var commands = TsplInspector.Parse(data);
        Assert.Contains(commands, c => c.Name == "SIZE" && c.Arguments.StartsWith("104 mm,40 mm"));
        var bitmaps = commands.Where(c => c.Name == "BITMAP").ToList();
        Assert.Equal(2, bitmaps.Count); // AAAAA × 1, AAB__ × 1
        Assert.All(bitmaps, b => Assert.StartsWith("0,0,104,", b.Arguments));
        Assert.Empty(TsplCommandBuilder.CheckLabel(Roll, settings, 832, 320));
        Assert.NotEmpty(TsplCommandBuilder.CheckLabel(Roll, settings, 160, 320));
    }

    [Fact]
    public void WiderRowThanThePrinterIsReported()
    {
        var settings = new LabelPrinterSettings { PrinterName = "Etiket", MaxPrintWidthMm = 100 };
        Assert.Contains(TsplCommandBuilder.CheckLabel(Roll, settings, 832, 320), w => w.Contains("104"));
        Assert.Contains(new LabelMedia(20, 40, 3, 0, MediaType.Gap) { Columns = 13 }.Validate(), p => p.Contains("12"));
    }
}
