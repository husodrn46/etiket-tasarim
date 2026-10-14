using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;

namespace AkelEtiket.PrintEngine.Tests;

/// <summary>Sahte kuyruk ile parçalama, iptal ve hata akışı (gerçek yazıcıya hiçbir şey gönderilmez).</summary>
public class TsplPrintServiceTests
{
    private static readonly LabelMedia Media = new(100, 100, 3, 0, MediaType.Gap);

    private sealed class FakeJob : IRawJobWriter
    {
        public FakeJob(int id, Func<int, bool>? failWrite) => (JobId, _failWrite) = (id, failWrite);
        private readonly Func<int, bool>? _failWrite;
        private int _writes;
        public int JobId { get; }
        public MemoryStream Data { get; } = new();
        public bool Completed { get; private set; }
        public bool Aborted { get; private set; }
        public bool Disposed { get; private set; }

        public void Write(ReadOnlySpan<byte> data)
        {
            _writes++;
            if (_failWrite?.Invoke(_writes) == true)
                throw new PrintEngineException("Yazıcıya veri gönderilemedi: deneme hatası", 31);
            Data.Write(data);
        }

        public void Complete() => Completed = true;
        public void Abort() => Aborted = true;
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeBackend : IPrintBackend
    {
        public List<FakeJob> Jobs { get; } = [];
        public PrinterInfo? Printer { get; set; } = new() { Name = "Etiket", DriverName = "Xprinter XP-470B", PortName = "USB001", UsbConnected = true };
        public SpoolWaitResult Outcome { get; set; } = new(SpoolOutcome.LeftQueue, "kuyruktan çıktı", null);
        public Func<int, bool>? FailWrite { get; set; }
        public int StatusChecks { get; private set; }

        public PrinterStatusSnapshot GetStatus(string printerName)
        {
            StatusChecks++;
            return Printer is null
                ? new PrinterStatusSnapshot { PrinterName = printerName, ErrorMessage = "'x' adlı yazıcı bulunamadı." }
                : new PrinterStatusSnapshot { PrinterName = printerName, Printer = Printer };
        }

        public IRawJobWriter StartJob(string printerName, string documentName)
        {
            var job = new FakeJob(100 + Jobs.Count, FailWrite);
            Jobs.Add(job);
            return job;
        }

        public Task<SpoolWaitResult> WaitForJobAsync(string printerName, int jobId, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(Outcome);
    }

    private static IEnumerable<LabelPage> Pages(int count, Action<int>? onRender = null)
    {
        for (int i = 0; i < count; i++)
        {
            onRender?.Invoke(i);
            var bitmap = new MonoBitmap(800, 800);
            bitmap[i % 800, 0] = true;
            yield return new LabelPage(bitmap, 1 + i % 2);
        }
    }

    private static LabelPrinterSettings Settings() => new() { PrinterName = "Etiket" };

    private sealed class SyncProgress(Action<PrintProgress> report) : IProgress<PrintProgress>
    {
        public void Report(PrintProgress value) => report(value);
    }

    [Fact]
    public async Task SingleJob_SendsWholeBatchAsOneJob_SoNothingCanInterleave()
    {
        var backend = new FakeBackend();
        var service = new TsplPrintService(backend) { MaxLabelsPerJob = 25, SingleJob = true };
        var report = await service.PrintAsync(Media, Settings(), Pages(60), 60);

        Assert.True(report.Success, report.Summary);
        var job = Assert.Single(backend.Jobs);
        Assert.True(job.Completed && !job.Aborted);
        Assert.Equal(60, report.LabelsSpooled);
        var commands = TsplInspector.Parse(job.Data.ToArray());
        Assert.Equal(60, commands.Count(c => c.Name == "BITMAP"));
        Assert.Equal(1, commands.Count(c => c.Name == "SIZE"));
    }

    [Fact]
    public async Task SingleJob_CancelStillSpoolsOnlyCompleteLabels()
    {
        var backend = new FakeBackend();
        using var cts = new CancellationTokenSource();
        var service = new TsplPrintService(backend) { SingleJob = true };
        var report = await service.PrintAsync(Media, Settings(), Pages(40, i => { if (i == 10) cts.Cancel(); }), 40, null, cts.Token);

        Assert.True(report.Canceled);
        var job = Assert.Single(backend.Jobs);
        Assert.True(job.Completed);
        Assert.Equal(report.LabelsSpooled, TsplInspector.Parse(job.Data.ToArray()).Count(c => c.Name == "BITMAP"));
        Assert.InRange(report.LabelsSpooled, 10, 11);
    }

    [Fact]
    public async Task LargeBatch_IsSplitIntoJobs_EachWithSetupHeader()
    {
        var backend = new FakeBackend();
        var service = new TsplPrintService(backend) { MaxLabelsPerJob = 25 };
        var progress = new List<int>();
        var report = await service.PrintAsync(Media, Settings(), Pages(60), 60,
            new SyncProgress(p => { if (p.Stage == "Yazıcı kuyruğuna gönderiliyor") progress.Add(p.Done); }));

        Assert.True(report.Success, report.Summary);
        Assert.Equal(3, backend.Jobs.Count);
        Assert.All(backend.Jobs, j => Assert.True(j.Completed && !j.Aborted && j.Disposed));
        int[] labelsPerJob = backend.Jobs
            .Select(j => TsplInspector.Parse(j.Data.ToArray()))
            .Select(cmds =>
            {
                Assert.Equal("SIZE", cmds[0].Name);
                return cmds.Count(c => c.Name == "BITMAP");
            })
            .ToArray();
        Assert.Equal([25, 25, 10], labelsPerJob);
        Assert.Equal(60, report.LabelsGenerated);
        Assert.Equal(60, report.LabelsSpooled);
        Assert.Equal(90, report.CopiesTotal); // 1,2,1,2… → 30*1 + 30*2
        Assert.Equal([100, 101, 102], report.JobIds);
        Assert.Equal(Enumerable.Range(1, 60), progress);
        Assert.Equal(SpoolOutcome.LeftQueue, report.QueueOutcome);
        Assert.Contains("4) Fiziksel baskı uygulama tarafından doğrulanamaz", report.Summary);
        Assert.Equal(3, backend.StatusChecks); // başta + 2. ve 3. parça öncesi
    }

    [Fact]
    public async Task Cancel_StopsAtLabelBoundary_AndNeverSendsHalfALabel()
    {
        var backend = new FakeBackend();
        var service = new TsplPrintService(backend) { MaxLabelsPerJob = 25 };
        using var cts = new CancellationTokenSource();
        var report = await service.PrintAsync(Media, Settings(), Pages(100, i => { if (i == 30) cts.Cancel(); }), 100, null, cts.Token);

        Assert.True(report.Canceled);
        Assert.False(report.Success);
        // 30. etiket (0 tabanlı) üretimi iptali tetikledi; o etiket ve sonrası gönderilmez ya da en fazla tamamlanmış etiketler gider.
        Assert.InRange(report.LabelsSpooled, 30, 31);
        Assert.Equal(2, backend.Jobs.Count);
        Assert.All(backend.Jobs, j => Assert.True(j.Completed));
        foreach (var job in backend.Jobs)
        {
            var commands = TsplInspector.Parse(job.Data.ToArray()); // yarım BITMAP olsaydı FormatException atardı
            Assert.Equal(commands.Count(c => c.Name == "CLS"), commands.Count(c => c.Name == "PRINT"));
        }
        Assert.Contains(report.Warnings, w => w.Contains("İptal edildi"));
    }

    [Fact]
    public async Task MissingPrinter_ReportsTurkishError_AndSendsNothing()
    {
        var backend = new FakeBackend { Printer = null };
        var report = await new TsplPrintService(backend).PrintAsync(Media, Settings(), Pages(3), 3);
        Assert.False(report.Success);
        Assert.False(report.DataGenerated);
        Assert.False(report.Spooled);
        Assert.Empty(backend.Jobs);
        Assert.Contains(report.Errors, e => e.Contains("bulunamadı"));
    }

    [Fact]
    public async Task OfflinePrinter_IsRefused_UnlessExplicitlyAllowed()
    {
        var backend = new FakeBackend
        {
            Printer = new PrinterInfo { Name = "Etiket", DriverName = "Xprinter XP-470B", PortName = "USB001", UsbConnected = false },
        };
        var report = await new TsplPrintService(backend).PrintAsync(Media, Settings(), Pages(2), 2);
        Assert.Empty(backend.Jobs);
        Assert.Contains(report.Errors, e => e.Contains("Yazıcı hazır değil") && e.Contains("USB"));

        var allow = Settings();
        allow.AllowPrintWhenOffline = true;
        report = await new TsplPrintService(backend).PrintAsync(Media, allow, Pages(2), 2);
        Assert.Single(backend.Jobs);
        Assert.Contains(report.Warnings, w => w.Contains("hazır görünmüyor"));
    }

    [Fact]
    public async Task WriteFailure_AbortsCurrentJob_AndCountsOnlyCompletedJobs()
    {
        // 1. iş: başlık + 25 etiket = 26 yazma; 2. işte 3. yazmada hata.
        var backend = new FakeBackend();
        int jobsStarted = 0;
        backend.FailWrite = writes => backend.Jobs.Count == 2 && writes == 3;
        var service = new TsplPrintService(backend) { MaxLabelsPerJob = 25 };
        var report = await service.PrintAsync(Media, Settings(), Pages(40), 40);
        jobsStarted = backend.Jobs.Count;

        Assert.Equal(2, jobsStarted);
        Assert.True(backend.Jobs[0].Completed);
        Assert.True(backend.Jobs[1].Aborted);
        Assert.Equal(25, report.LabelsSpooled);
        Assert.Equal([100], report.JobIds);
        Assert.Contains(report.Errors, e => e.Contains("Yazıcıya veri gönderilemedi"));
        Assert.False(report.Success);
    }

    [Fact]
    public async Task RenderFailure_IsReportedWithLabelNumber()
    {
        var backend = new FakeBackend();
        var report = await new TsplPrintService(backend).PrintAsync(Media, Settings(),
            Pages(5, i => { if (i == 2) throw new InvalidOperationException("veri hatalı"); }), 5);
        Assert.Contains(report.Errors, e => e.Contains("3. etiket") && e.Contains("veri hatalı"));
        Assert.Equal(2, report.LabelsSpooled);
        Assert.False(report.Success);
    }

    [Theory]
    [InlineData(SpoolOutcome.Offline, true)]
    [InlineData(SpoolOutcome.PaperOut, true)]
    [InlineData(SpoolOutcome.TimedOut, false)]
    [InlineData(SpoolOutcome.Printed, false)]
    public async Task QueueOutcome_IsReportedHonestly(SpoolOutcome outcome, bool isError)
    {
        var backend = new FakeBackend { Outcome = new SpoolWaitResult(outcome, "durum: " + outcome, null) };
        var report = await new TsplPrintService(backend).PrintAsync(Media, Settings(), Pages(1), 1);
        Assert.Equal(outcome, report.QueueOutcome);
        Assert.Equal(isError, report.Errors.Any(e => e.Contains("durum:")));
        Assert.Equal(outcome is SpoolOutcome.Printed or SpoolOutcome.LeftQueue, report.Success);
        Assert.True(report.Spooled);
    }

    [Fact]
    public async Task InvalidSettings_AreReportedBeforeAnything()
    {
        var backend = new FakeBackend();
        var settings = Settings();
        settings.Speed = 12;
        var report = await new TsplPrintService(backend).PrintAsync(Media with { GapMm = 0 }, settings, Pages(1), 1);
        Assert.Equal(0, backend.StatusChecks);
        Assert.Contains(report.Errors, e => e.Contains("hızı"));
        Assert.Contains(report.Errors, e => e.Contains("boşluk"));
    }

    [Fact]
    public async Task NonTsplDriver_ProducesWarning()
    {
        var backend = new FakeBackend { Printer = new PrinterInfo { Name = "Ofis", DriverName = "Canon G3070 series", PortName = "WSD-1" } };
        var report = await new TsplPrintService(backend).PrintAsync(Media, Settings(), Pages(1), 1);
        Assert.Contains(report.Warnings, w => w.Contains("TSPL destekli görünmüyor"));
    }

    [Fact]
    public async Task SendRaw_UsesSingleJob()
    {
        var backend = new FakeBackend();
        var report = await new TsplPrintService(backend).SendRawAsync("Etiket", TsplCommandBuilder.BuildSelfTest(), "Test");
        Assert.True(report.Success, report.Summary);
        Assert.Equal("SELFTEST\r\n", System.Text.Encoding.ASCII.GetString(Assert.Single(backend.Jobs).Data.ToArray()));
    }
}
