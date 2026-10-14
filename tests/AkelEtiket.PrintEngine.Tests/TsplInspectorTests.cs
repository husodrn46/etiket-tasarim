using System.Text;
using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;

namespace AkelEtiket.PrintEngine.Tests;

public class TsplInspectorTests
{
    [Fact]
    public void BinaryPayloadContainingCrLf_DoesNotBreakParsing()
    {
        // Ters çevrildiğinde 0x0D 0x0A üretecek desen: ~0x0D = 0xF2, ~0x0A = 0xF5
        var bitmap = new MonoBitmap(16, 1, [0xF2, 0xF5]);
        var data = TsplCommandBuilder.BuildJob(new LabelMedia(2, 1, 1, 0, MediaType.Gap), new LabelPrinterSettings { PrinterName = "x" },
            [new LabelPage(bitmap, 2)]);
        var commands = TsplInspector.Parse(data, includePayload: true);
        var bmp = commands.Single(c => c.Name == "BITMAP");
        Assert.Equal([0x0D, 0x0A], bmp.Payload);
        Assert.Equal("PRINT", commands[^1].Name);
        Assert.Equal("1,2", commands[^1].Arguments);
    }

    [Fact]
    public void Describe_SummarizesImageData()
    {
        var data = TsplCommandBuilder.BuildLabel(new MonoBitmap(800, 800), new LabelPrinterSettings { PrinterName = "x" }, 1);
        string text = TsplInspector.Describe(data);
        Assert.Contains("CLS", text);
        Assert.Contains("BITMAP 0,0,100,800,0,<80.000 bayt görüntü verisi>", text);
        Assert.Contains("PRINT 1,1", text);
    }

    [Fact]
    public void TruncatedBitmap_IsReported()
    {
        byte[] data = Encoding.ASCII.GetBytes("CLS\r\nBITMAP 0,0,10,10,0,abc");
        var ex = Assert.Throws<FormatException>(() => TsplInspector.Parse(data));
        Assert.Contains("eksik", ex.Message);
        Assert.Throws<FormatException>(() => TsplInspector.Parse(Encoding.ASCII.GetBytes("BITMAP 0,0\r\n")));
    }

    [Fact]
    public void LfOnlyAndLowercase_AreTolerated()
    {
        var commands = TsplInspector.Parse(Encoding.ASCII.GetBytes("size 50 mm,30 mm\ncls\nprint 1,1\n"));
        Assert.Equal(["SIZE", "CLS", "PRINT"], commands.Select(c => c.Name));
        Assert.Equal("50 mm,30 mm", commands[0].Arguments);
    }
}
