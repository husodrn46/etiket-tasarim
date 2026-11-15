using AkelEtiket.Core;
using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;
using AkelEtiket.PrintEngine.Interop;

namespace AkelEtiket.PrintEngine.Tests;

/// <summary>
/// Windows dışındaki sistemler. macOS'ta yazıcılar ve ham (TSPL) gönderim CUPS üzerinden çalışır; sürücü üzerinden
/// baskı yoktur. Hiçbir giriş noktası yerel Windows çağrısına gitmemeli (DllNotFoundException olmamalı); her biri
/// anlaşılır bir hata ya da boş sonuç vermeli. GÜVENLİK: fiziksel yazıcıya hiçbir iş gönderilmez; yalnızca var
/// olmayan bir yazıcı adı kullanılır.
/// </summary>
public class UnsupportedPlatformTests
{
    private const string MissingPrinter = "LabelHarbor-Olmayan-Yazici-7f3c";
    private static LabelMedia Media => new(100, 100, 3, 0, MediaType.Gap);

    [MacFact]
    public void Mac_ListsPrinters_ThroughCups_WithoutThrowing()
    {
        // Yazıcı eklenmemiş bir Mac'te liste boştur; yazdırma hizmeti kapalıysa hata iletisiyle false döner.
        bool ok = PrinterDiscovery.TryGetPrinters(out var printers, out string? error);
        if (ok) Assert.All(printers, p => Assert.False(string.IsNullOrWhiteSpace(p.StatusText)));
        else Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Null(SpoolerMonitor.GetJob(MissingPrinter, int.MaxValue - 7));
        Assert.Empty(SpoolerMonitor.GetJobs(MissingPrinter));
    }

    [MacFact]
    public async Task Mac_MissingPrinter_IsExplainedInMacTerms_AndNothingIsSent()
    {
        var status = PrinterDiscovery.GetStatus(MissingPrinter);
        Assert.False(status.Exists);
        Assert.Contains("Sistem Ayarları", status.ErrorMessage);

        var report = await new TsplPrintService().PrintAsync(Media, new LabelPrinterSettings { PrinterName = MissingPrinter },
            [new LabelPage(new MonoBitmap(800, 800), 1)], 1);
        Assert.False(report.Success);
        Assert.False(report.Spooled);
        Assert.Empty(report.JobIds);
        Assert.Contains(report.Errors, e => e.Contains("bulunamadı"));
        // Rapor macOS'ta "Windows kuyruğu" demez.
        Assert.Contains("2) macOS yazdırma kuyruğuna gönderilmedi.", report.Summary);
        Assert.DoesNotContain("Windows yazdırma kuyruğu", report.Summary);
    }

    [MacFact]
    public void Cups_StatesMapToTheSharedFlags()
    {
        Assert.Equal(JobStatusFlags.None, CupsPrintSystem.ToStatus(CupsNative.JobPending));
        Assert.Equal(JobStatusFlags.Printing, CupsPrintSystem.ToStatus(CupsNative.JobProcessing));
        Assert.Equal(JobStatusFlags.Paused, CupsPrintSystem.ToStatus(CupsNative.JobHeld));
        Assert.Equal(JobStatusFlags.Deleted, CupsPrintSystem.ToStatus(CupsNative.JobCanceled));
        Assert.Equal(JobStatusFlags.Error, CupsPrintSystem.ToStatus(CupsNative.JobAborted));
        Assert.True(CupsPrintSystem.ToStatus(CupsNative.JobCompleted).HasFlag(JobStatusFlags.Printed));

        // Durum nedenleri son ekle gelir ve birden çok olabilir.
        Assert.True(CupsPrintSystem.HasReason("offline-report", "offline"));
        Assert.True(CupsPrintSystem.HasReason("toner-low-warning,media-empty-error", "media-empty"));
        Assert.False(CupsPrintSystem.HasReason("none", "offline"));
        Assert.True(CupsNative.IsOk(0x0001));
        Assert.False(CupsNative.IsOk(0x0406));
    }

    [NonWindowsFact]
    public async Task DriverPrint_IsWindowsOnly_AndSaysSo()
    {
        var report = await new DriverPrintService().PrintAsync(MissingPrinter, Media, 1, (_, _, _) => new MonoBitmap(10, 10), _ => 1);
        Assert.False(report.Success);
        Assert.NotEmpty(report.Errors);

        Assert.Null(DriverPrintService.Probe(MissingPrinter, Media, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        if (OperatingSystem.IsMacOS()) Assert.Contains("TSPL", error);
        Assert.Null(DriverPrintService.FindMatchingPaper(MissingPrinter, Media));
    }

    /// <summary>TSPL verisi her sistemde üretilir.</summary>
    [Fact]
    public void TsplData_IsGeneratedOnEveryPlatform()
    {
        byte[] setup = TsplCommandBuilder.BuildSetup(Media, new LabelPrinterSettings { PrinterName = MissingPrinter });
        Assert.Contains("SIZE 100", System.Text.Encoding.ASCII.GetString(setup));
    }
}
