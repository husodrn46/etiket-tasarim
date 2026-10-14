using System.Text.Json;

namespace AkelEtiket.PrintEngine.Tests;

public class StatusTextTests
{
    [Theory]
    [InlineData(JobStatusFlags.Offline, SpoolOutcome.Offline)]
    [InlineData(JobStatusFlags.Printing | JobStatusFlags.PaperOut, SpoolOutcome.PaperOut)]
    [InlineData(JobStatusFlags.Error | JobStatusFlags.Printing, SpoolOutcome.Error)]
    [InlineData(JobStatusFlags.Paused, SpoolOutcome.Paused)]
    [InlineData(JobStatusFlags.UserIntervention, SpoolOutcome.UserIntervention)]
    public void JobProblems_AreClassified(JobStatusFlags flags, SpoolOutcome expected) =>
        Assert.Equal(expected, SpoolerMonitor.ClassifyProblem(flags));

    [Theory]
    [InlineData(JobStatusFlags.None)]
    [InlineData(JobStatusFlags.Spooling)]
    [InlineData(JobStatusFlags.Printing)]
    public void NormalJobStates_AreNotProblems(JobStatusFlags flags) => Assert.Null(SpoolerMonitor.ClassifyProblem(flags));

    [Fact]
    public void OutcomeMessages_NeverClaimPhysicalPrinting()
    {
        foreach (var outcome in Enum.GetValues<SpoolOutcome>())
        {
            string text = SpoolerMonitor.DescribeOutcome(outcome);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("basıldı.", text);
        }
        Assert.Contains("kontrol edin", SpoolerMonitor.DescribeOutcome(SpoolOutcome.LeftQueue));
        var printing = new JobSnapshot { JobId = 3, Status = JobStatusFlags.Printing };
        Assert.Contains("aktarılmaya devam", SpoolerMonitor.DescribeOutcome(SpoolOutcome.TimedOut, printing));
        Assert.Contains("Yazıcıya gönderiliyor", printing.StatusText);
    }

    [Fact]
    public void PrinterState_ReadyAndOfflineRules()
    {
        var ready = new PrinterInfo { Name = "a", DriverName = "Xprinter XP-470B", PortName = "USB001", UsbConnected = true, Status = PrinterStatusFlags.Printing };
        Assert.True(ready.IsReady);
        Assert.True(ready.IsXp470B);
        Assert.True(ready.IsLikelyTsplPrinter);
        Assert.Contains("Yazdırıyor", ready.StatusText);

        var unplugged = new PrinterInfo { Name = "a", PortName = "USB001", UsbConnected = false };
        Assert.True(unplugged.IsOffline);
        Assert.False(unplugged.IsReady);
        Assert.Contains("USB bağlantısı yok", unplugged.StatusText);

        var workOffline = new PrinterInfo { Name = "a", IsWorkOffline = true };
        Assert.True(workOffline.IsOffline);
        Assert.Contains("Çevrimdışı Kullan", TsplPrintService.Advice(workOffline));

        var paused = new PrinterInfo { Name = "a", Status = PrinterStatusFlags.Paused, JobCount = 2 };
        Assert.False(paused.IsOffline);
        Assert.False(paused.IsReady);
        Assert.Equal("Duraklatıldı (2 iş kuyrukta)", paused.StatusText);
        Assert.Contains("Sürdür", TsplPrintService.Advice(paused));

        Assert.False(new PrinterInfo { Name = "Ofis", DriverName = "Canon G3070 series" }.IsLikelyTsplPrinter);
        Assert.True(new PrinterInfo { Name = "Etiket", DriverName = "TSC TE200" }.IsLikelyTsplPrinter);
    }

    [Fact]
    public void Win32Errors_AreExplainedInTurkish()
    {
        Assert.Contains("bulunamadı", Win32ErrorText.Explain(1801, "barkod"));
        Assert.Contains("'barkod'", Win32ErrorText.Explain(1801, "barkod"));
        Assert.Contains("Yazdırma Biriktiricisi", Win32ErrorText.Explain(1722));
        Assert.Contains("RAW", Win32ErrorText.Explain(1804));
        Assert.Contains("USB", Win32ErrorText.Explain(31));
        var ex = PrintEngineException.FromWin32("Yazıcı açılamadı", 5, "barkod");
        Assert.Equal(5, ex.Win32Error);
        Assert.StartsWith("Yazıcı açılamadı: Erişim reddedildi", ex.Message);
        Assert.Contains("(Windows hata kodu 5)", ex.Message);
    }

    [Fact]
    public void Settings_RoundTripThroughJson_WithoutDerivedValues()
    {
        var settings = new LabelPrinterSettings
        {
            PrinterName = "barkod", Density = 10, Speed = 3, Rotate180 = true, OffsetXMm = 1.5, TearMode = TearMode.On,
        };
        string json = JsonSerializer.Serialize(settings);
        Assert.DoesNotContain("EffectiveDensity", json);
        Assert.DoesNotContain("OffsetXDots", json);
        var back = JsonSerializer.Deserialize<LabelPrinterSettings>(json)!;
        Assert.Equal(("barkod", 10, 3, true, 1.5, TearMode.On), (back.PrinterName, back.Density, back.Speed, back.Rotate180, back.OffsetXMm, back.TearMode));
        Assert.Equal(12, back.OffsetXDots);
    }

    [Fact]
    public void Report_Summary_SeparatesStages()
    {
        var report = new PrintReport { Method = PrintMethod.TsplRaw, PrinterName = "barkod" };
        Assert.False(report.Success);
        string summary = report.Summary;
        Assert.Contains("1) Baskı verisi üretilmedi.", summary);
        Assert.Contains("2) Windows yazdırma kuyruğuna gönderilmedi.", summary);
        Assert.Contains("3) Kuyruk sonucu: bilinmiyor.", summary);
        Assert.Contains("4) Fiziksel baskı uygulama tarafından doğrulanamaz", summary);
    }
}
