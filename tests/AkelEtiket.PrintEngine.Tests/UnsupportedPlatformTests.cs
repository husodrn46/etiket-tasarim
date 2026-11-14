using AkelEtiket.Core;
using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;

namespace AkelEtiket.PrintEngine.Tests;

/// <summary>
/// Windows dışındaki sistemlerde yazdırma kuyruğu henüz yok. Yerel Windows çağrısına gidilmemeli
/// (DllNotFoundException olmamalı); her giriş noktası anlaşılır bir hata ya da boş sonuç vermeli.
/// </summary>
public class UnsupportedPlatformTests
{
    private const string Printer = "Xprinter XP-470B";
    private const string Unsupported = "Bu işletim sisteminde yazdırma henüz desteklenmiyor.";
    private static LabelMedia Media => new(100, 100, 3, 0, MediaType.Gap);

    [NonWindowsFact]
    public void Discovery_ReportsUnsupported_WithoutNativeCalls()
    {
        Assert.False(PrinterDiscovery.TryGetPrinters(out var printers, out string? error));
        Assert.Empty(printers);
        Assert.Equal(Unsupported, error);
        Assert.Equal(Unsupported, Assert.Throws<PrintEngineException>(() => PrinterDiscovery.GetPrinters()).Message);

        var status = PrinterDiscovery.GetStatus(Printer);
        Assert.False(status.Exists);
        Assert.Equal(Unsupported, status.ErrorMessage);

        Assert.Null(PrinterDiscovery.GetDefaultPrinterName());
        Assert.Null(PrinterDiscovery.FindPreferredLabelPrinter());
    }

    [NonWindowsFact]
    public void Queue_ReturnsEmptyResults()
    {
        Assert.Null(SpoolerMonitor.GetJob(Printer, 1));
        Assert.Empty(SpoolerMonitor.GetJobs(Printer));
        Assert.False(SpoolerMonitor.CancelJob(Printer, 1));
    }

    [NonWindowsFact]
    public async Task TsplPrint_ReturnsErrorReport_AndSendsNothing()
    {
        var settings = new LabelPrinterSettings { PrinterName = Printer };
        var report = await new TsplPrintService().PrintAsync(Media, settings, [new LabelPage(new MonoBitmap(800, 800), 1)], 1);
        Assert.False(report.Success);
        Assert.False(report.Spooled);
        Assert.Contains(Unsupported, report.Errors);

        var raw = await new TsplPrintService().SendRawAsync(Printer, TsplCommandBuilder.BuildFormFeed(), "test");
        Assert.False(raw.Success);
        Assert.False(raw.Spooled);
        Assert.Contains(Unsupported, raw.Errors);
    }

    [NonWindowsFact]
    public async Task DriverPrint_ReturnsErrorReport_AndProbeExplainsWhy()
    {
        var report = await new DriverPrintService().PrintAsync(Printer, Media, 1, (_, _, _) => new MonoBitmap(10, 10), _ => 1);
        Assert.False(report.Success);
        Assert.Contains(Unsupported, report.Errors);

        Assert.Null(DriverPrintService.Probe(Printer, Media, out string? error));
        Assert.Equal(Unsupported, error);
        Assert.Null(DriverPrintService.FindMatchingPaper(Printer, Media));
    }

    /// <summary>Kuyruk olmasa da TSPL verisi her sistemde üretilir (Mac baskısının üstüne kurulacağı parça).</summary>
    [Fact]
    public void TsplData_IsGeneratedOnEveryPlatform()
    {
        byte[] setup = TsplCommandBuilder.BuildSetup(Media, new LabelPrinterSettings { PrinterName = Printer });
        Assert.Contains("SIZE 100", System.Text.Encoding.ASCII.GetString(setup));
    }
}
