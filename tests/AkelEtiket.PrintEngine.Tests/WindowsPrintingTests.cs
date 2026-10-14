using System.Text;
using System.Text.RegularExpressions;
using AkelEtiket.Core;
using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;
using AkelEtiket.PrintEngine.Interop;
using Xunit.Abstractions;

namespace AkelEtiket.PrintEngine.Tests;

/// <summary>
/// Gerçek Windows yazdırma sistemine karşı testler. GÜVENLİK: fiziksel yazıcılara hiçbir iş gönderilmez;
/// yalnızca var olmayan yazıcı adları ve dosyaya yazan "Microsoft Print to PDF" kullanılır.
/// </summary>
public class WindowsPrintingTests(ITestOutputHelper output)
{
    private const string PdfPrinter = "Microsoft Print to PDF";
    private const string MissingPrinter = "AKEL-Etiket-Olmayan-Yazici-7f3c";

    [Fact]
    public void GetPrinters_DoesNotThrow_AndDescribesEachPrinter()
    {
        var printers = PrinterDiscovery.GetPrinters();
        foreach (var p in printers)
        {
            output.WriteLine($"{p.Name} | {p.DriverName} | {p.PortName} | TSPL={p.IsLikelyTsplPrinter} XP470B={p.IsXp470B} " +
                             $"USB={p.UsbConnected?.ToString() ?? "-"} | {p.StatusText}");
            Assert.False(string.IsNullOrWhiteSpace(p.StatusText));
        }
        Assert.True(PrinterDiscovery.TryGetPrinters(out var again, out var error));
        Assert.Null(error);
        Assert.Equal(printers.Count, again.Count);
    }

    [Fact]
    public void UsbPortQuery_Works()
    {
        var ports = UsbPrinterPorts.GetPresentPortNames();
        Assert.NotNull(ports);
        output.WriteLine("Bağlı USB yazıcı portları: " + string.Join(", ", ports!));
        Assert.True(UsbPrinterPorts.IsUsbPortName("USB001"));
        Assert.False(UsbPrinterPorts.IsUsbPortName("PORTPROMPT:"));
        Assert.Null(UsbPrinterPorts.IsConnected("WSD-123", ports));
        Assert.False(UsbPrinterPorts.IsConnected("USB999", new HashSet<string>()));
    }

    [Fact]
    public void MissingPrinter_IsReportedWithoutExceptions()
    {
        Assert.Null(PrinterDiscovery.GetPrinter(MissingPrinter));
        var status = PrinterDiscovery.GetStatus(MissingPrinter);
        Assert.False(status.Exists);
        Assert.Contains("bulunamadı", status.ErrorMessage);
        Assert.Null(SpoolerMonitor.GetJob(MissingPrinter, 1));
        Assert.Empty(SpoolerMonitor.GetJobs(MissingPrinter));
        Assert.False(SpoolerMonitor.CancelJob(MissingPrinter, 1));

        var ex = Assert.Throws<PrintEngineException>(() => RawPrintJob.Start(MissingPrinter, "test"));
        Assert.Equal(Win32ErrorText.ErrorInvalidPrinterName, ex.Win32Error);
        Assert.Contains("bulunamadı", ex.Message);
    }

    [Fact]
    public async Task TsplPrint_ToMissingPrinter_ReturnsErrorReport_AndDoesNotCrash()
    {
        var settings = new LabelPrinterSettings { PrinterName = MissingPrinter };
        var media = new LabelMedia(100, 100, 3, 0, MediaType.Gap);
        var report = await new TsplPrintService().PrintAsync(media, settings, [new LabelPage(new MonoBitmap(800, 800), 1)], 1);
        output.WriteLine(report.Summary);
        Assert.False(report.Success);
        Assert.False(report.Spooled);
        Assert.Contains(report.Errors, e => e.Contains("bulunamadı"));
        Assert.Contains("2) Windows yazdırma kuyruğuna gönderilmedi.", report.Summary);

        var raw = await new TsplPrintService().SendRawAsync(MissingPrinter, TsplCommandBuilder.BuildFormFeed(), "test");
        Assert.False(raw.Success);
        Assert.Contains(raw.Errors, e => e.Contains("bulunamadı"));
    }

    [Fact]
    public async Task DriverPrint_ToMissingPrinter_ReturnsErrorReport()
    {
        var report = await new DriverPrintService().PrintAsync(MissingPrinter, new LabelMedia(100, 100, 3, 0, MediaType.Gap), 1,
            (_, dx, dy) => new MonoBitmap(10, 10), _ => 1);
        Assert.False(report.Success);
        Assert.Contains(report.Errors, e => e.Contains("bulunamadı"));
    }

    [Fact]
    public void GetJob_ForBogusJobId_ReturnsNull()
    {
        if (PrinterDiscovery.GetPrinter(PdfPrinter) is null)
        {
            output.WriteLine($"'{PdfPrinter}' yok; test atlandı.");
            return;
        }
        Assert.Null(SpoolerMonitor.GetJob(PdfPrinter, int.MaxValue - 7));
    }

    /// <summary>
    /// Sürücü yolunun ölçek doğruluğu (dosyaya yazan "Microsoft Print to PDF" ile; fiziksel yazıcı kullanılmaz).
    /// Bu sürücü yalnızca kendi form listesindeki boyutları kabul eder (özel boyut desteklemez); bu yüzden
    /// listede birebir bulunan 100 × 148 mm (veya 105 × 148 mm A6) kullanılır. PDF MediaBox genişliği
    /// 283,46 pt (= 100 mm) olmalı, etiket aygıt çözünürlüğünde (600 DPI) ölçeklenmeden üretilmelidir.
    /// </summary>
    [Fact]
    public async Task DriverPrint_ToPdf_HasExactPageSize_AndDeviceResolutionBitmap()
    {
        if (PrinterDiscovery.GetPrinter(PdfPrinter) is null)
        {
            output.WriteLine($"'{PdfPrinter}' yok; test atlandı.");
            return;
        }
        var media = new LabelMedia(100, 148, 3, 0, MediaType.Gap);
        if (DriverPrintService.FindMatchingPaper(PdfPrinter, media) is null) media = media with { WidthMm = 105 };
        Assert.NotNull(DriverPrintService.FindMatchingPaper(PdfPrinter, media));

        string path = Path.Combine(Path.GetTempPath(), $"akel-etiket-surucu-testi-{Guid.NewGuid():N}.pdf");
        try
        {
            double seenX = 0, seenY = 0;
            var service = new DriverPrintService { OutputFilePath = path, QueueWaitTimeout = TimeSpan.FromSeconds(60) };
            var report = await service.PrintAsync(PdfPrinter, media, 1, (_, dotsPerMmX, dotsPerMmY) =>
            {
                seenX = dotsPerMmX;
                seenY = dotsPerMmY;
                return FramedLabel(media, dotsPerMmX, dotsPerMmY);
            }, _ => 1);

            output.WriteLine(report.Summary);
            output.WriteLine($"Aygıt: {service.LastPageInfo}");
            Assert.True(report.Success, report.Summary);
            Assert.Empty(report.Warnings);
            Assert.True(File.Exists(path), "PDF dosyası oluşmadı.");

            var info = Assert.IsType<DriverPageInfo>(service.LastPageInfo);
            Assert.True(info.PageMatches(media));
            Assert.Equal(info.RenderDotsPerMmX, seenX, 9);
            Assert.Equal(info.RenderDotsPerMmY, seenY, 9);
            Assert.Equal(info.PhysicalWidth, info.BitmapWidth);   // 1 görüntü pikseli = 1 aygıt noktası
            Assert.Equal(info.PhysicalHeight, info.BitmapHeight);

            var (widthPt, heightPt) = SingleMediaBox(path);
            Assert.InRange(widthPt, media.WidthMm / 25.4 * 72 - 1.5, media.WidthMm / 25.4 * 72 + 1.5);
            Assert.InRange(heightPt, media.HeightMm / 25.4 * 72 - 1.5, media.HeightMm / 25.4 * 72 + 1.5);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    /// <summary>Sürücü özel boyutu reddederse (PDF yazıcısı 100 × 100 mm'yi A4'e çevirir) bu açıkça uyarılır ve görüntü ölçeklenmez.</summary>
    [Fact]
    public async Task DriverPrint_UnsupportedCustomSize_IsReportedHonestly()
    {
        if (PrinterDiscovery.GetPrinter(PdfPrinter) is null)
        {
            output.WriteLine($"'{PdfPrinter}' yok; test atlandı.");
            return;
        }
        var media = new LabelMedia(100, 100, 3, 0, MediaType.Gap);
        string path = Path.Combine(Path.GetTempPath(), $"akel-etiket-surucu-testi-{Guid.NewGuid():N}.pdf");
        try
        {
            var service = new DriverPrintService { OutputFilePath = path, QueueWaitTimeout = TimeSpan.FromSeconds(60) };
            var report = await service.PrintAsync(PdfPrinter, media, 1, (_, dx, dy) => FramedLabel(media, dx, dy), _ => 1);
            output.WriteLine(report.Summary);
            var info = Assert.IsType<DriverPageInfo>(service.LastPageInfo);
            if (info.PageMatches(media))
            {
                output.WriteLine("Bu sistemde PDF yazıcısı 100 × 100 mm'yi kabul etti; uyarı beklenmez.");
                return;
            }
            Assert.Contains(report.Warnings, w => w.Contains("desteklemiyor"));
            Assert.Equal((int)Math.Round(100 * info.DpiX / 25.4), info.BitmapWidth); // etiket kendi ölçüsünde kaldı, sayfaya büyütülmedi
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Kullanıcının XP-470B sürücüsü varsa: yazdırma işi OLUŞTURMADAN (yalnızca aygıt bağlamı) 100 × 100 mm sayfanın
    /// 800 × 800 nokta (8 nokta/mm) kurulduğunu doğrular. Yazıcıya hiçbir veri gönderilmez.
    /// </summary>
    [Fact]
    public void Probe_Xp470BDriver_SetsUpExactLabelPage_WithoutPrinting()
    {
        var xp = PrinterDiscovery.GetPrinters().FirstOrDefault(p => p.IsXp470B);
        if (xp is null)
        {
            output.WriteLine("XP-470B sürücüsü yok; test atlandı.");
            return;
        }
        var media = new LabelMedia(100, 100, 3, 0, MediaType.Gap);
        int jobsBefore = SpoolerMonitor.GetJobs(xp.Name).Count;
        var info = DriverPrintService.Probe(xp.Name, media, out string? error);
        output.WriteLine($"{xp.Name}: {info} hata={error}");
        Assert.Null(error);
        Assert.NotNull(info);
        Assert.True(info!.PageMatches(media));
        Assert.Equal(800, info.PhysicalWidth);
        Assert.Equal(800, info.PhysicalHeight);
        Assert.Equal(8.0, info.RenderDotsPerMmX, 9);
        Assert.Equal(jobsBefore, SpoolerMonitor.GetJobs(xp.Name).Count); // iş oluşmadı
    }

    [Fact]
    public void FindMatchingPaper_FindsStandardForms()
    {
        Assert.Equal(1000, DriverPrintService.TenthsOfMm(100));
        Assert.Equal(1016, DriverPrintService.TenthsOfMm(101.6));
        if (PrinterDiscovery.GetPrinter(PdfPrinter) is null) return;
        Assert.Equal(9, DriverPrintService.FindMatchingPaper(PdfPrinter, new LabelMedia(210, 297, 0, 0, MediaType.Continuous))); // A4
        Assert.Null(DriverPrintService.FindMatchingPaper(PdfPrinter, new LabelMedia(77, 33, 3, 0, MediaType.Gap)));
    }

    private static MonoBitmap FramedLabel(LabelMedia media, double dotsPerMmX, double dotsPerMmY)
    {
        var bitmap = new MonoBitmap(Units.MmToDotsRounded(media.WidthMm, dotsPerMmX), Units.MmToDotsRounded(media.HeightMm, dotsPerMmY));
        int t = Math.Max(1, (int)Math.Round(dotsPerMmX)); // 1 mm çerçeve
        bitmap.FillRect(0, 0, bitmap.Width, t, true);
        bitmap.FillRect(0, bitmap.Height - t, bitmap.Width, t, true);
        bitmap.FillRect(0, 0, t, bitmap.Height, true);
        bitmap.FillRect(bitmap.Width - t, 0, t, bitmap.Height, true);
        return bitmap;
    }

    private (double Width, double Height) SingleMediaBox(string path)
    {
        string pdf = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        var boxes = Regex.Matches(pdf, @"/MediaBox\s*\[\s*([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s*\]");
        Assert.Single(boxes);
        var box = boxes[0];
        double Num(int i) => double.Parse(box.Groups[i].Value, System.Globalization.CultureInfo.InvariantCulture);
        double widthPt = Num(3) - Num(1), heightPt = Num(4) - Num(2);
        output.WriteLine($"PDF MediaBox: {widthPt} × {heightPt} pt = {widthPt / 72 * 25.4:0.00} × {heightPt / 72 * 25.4:0.00} mm");
        return (widthPt, heightPt);
    }
}
