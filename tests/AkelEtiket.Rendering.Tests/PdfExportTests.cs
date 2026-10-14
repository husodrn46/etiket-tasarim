using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AkelEtiket.Core.Imaging;

namespace AkelEtiket.Rendering.Tests;

/// <summary>PDF olarak kaydet: her etiket bir sayfa, sayfa ölçüsü etiketin gerçek ölçüsü (1 mm = 72/25,4 nokta).</summary>
public sealed partial class PdfExportTests
{
    [GeneratedRegex(@"/Type\s*/Page(?![a-zA-Z])")]
    private static partial Regex PageRegex();

    [GeneratedRegex(@"/MediaBox\s*\[\s*([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s*\]")]
    private static partial Regex MediaBoxRegex();

    private static MonoBitmap Label(int width, int height, int marker)
    {
        var bitmap = new MonoBitmap(width, height);
        for (int x = 0; x < width; x++) bitmap[x, marker % height] = true;
        return bitmap;
    }

    private static string Latin1(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    [Fact]
    public void MmToPoints_UsesExactInchConversion()
    {
        Assert.Equal(72f, PdfExport.MmToPoints(25.4), 3);
        Assert.Equal(283.4646f, PdfExport.MmToPoints(100), 3);
        Assert.Equal(141.7323f, PdfExport.MmToPoints(50), 3);
    }

    [Fact]
    public void Write_OnePagePerLabel_AtExactLabelSize()
    {
        // 100 × 50 mm etiket, 203 DPI (8 nokta/mm) → 800 × 400 nokta.
        var pages = Enumerable.Range(0, 3).Select(i => new PdfLabelPage(Label(800, 400, i * 10), 100, 50)).ToList();
        using var stream = new MemoryStream();
        int count = PdfExport.Write(stream, pages, "Deneme etiketi");
        Assert.Equal(3, count);

        string pdf = Latin1(stream.ToArray());
        Assert.StartsWith("%PDF-", pdf);
        Assert.Equal(3, PageRegex().Matches(pdf).Count);
        var boxes = MediaBoxRegex().Matches(pdf);
        Assert.Equal(3, boxes.Count);
        foreach (Match box in boxes)
        {
            double width = double.Parse(box.Groups[3].Value, CultureInfo.InvariantCulture) - double.Parse(box.Groups[1].Value, CultureInfo.InvariantCulture);
            double height = double.Parse(box.Groups[4].Value, CultureInfo.InvariantCulture) - double.Parse(box.Groups[2].Value, CultureInfo.InvariantCulture);
            Assert.Equal(283.46, width, 1);
            Assert.Equal(141.73, height, 1);
        }
    }

    [Fact]
    public void Write_MixedSizes_EachPageKeepsItsOwnSize()
    {
        var pages = new[]
        {
            new PdfLabelPage(Label(800, 400, 1), 100, 50),
            new PdfLabelPage(Label(320, 240, 2), 40, 30),
        };
        using var stream = new MemoryStream();
        Assert.Equal(2, PdfExport.Write(stream, pages, "Karışık"));
        var sizes = MediaBoxRegex().Matches(Latin1(stream.ToArray()))
            .Select(m => Math.Round(double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), 1))
            .ToList();
        Assert.Contains(283.5, sizes);
        Assert.Contains(113.4, sizes);
    }

    [Fact]
    public void Write_NoPages_Throws()
    {
        using var stream = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() => PdfExport.Write(stream, [], "Boş"));
    }

    [Fact]
    public void WriteFile_IsAtomic_AndLeavesNoTempFile()
    {
        string folder = Path.Combine(Path.GetTempPath(), "akel-pdf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "etiketler.pdf");
            Assert.Equal(2, PdfExport.WriteFile(path, [new PdfLabelPage(Label(80, 40, 0), 10, 5), new PdfLabelPage(Label(80, 40, 1), 10, 5)], "Dosya"));
            Assert.True(File.Exists(path));
            Assert.StartsWith("%PDF-", Latin1(File.ReadAllBytes(path)));

            // Başarısız yazım: var olan PDF'e dokunulmaz, geçici dosya kalmaz.
            long before = new FileInfo(path).Length;
            Assert.Throws<InvalidOperationException>(() => PdfExport.WriteFile(path, [], "Boş"));
            Assert.Equal(before, new FileInfo(path).Length);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
