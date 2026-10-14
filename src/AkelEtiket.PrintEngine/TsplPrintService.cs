namespace AkelEtiket.PrintEngine;

/// <summary>Yazıcı/kuyruk erişimi (testlerde sahte uygulamayla değiştirilir).</summary>
internal interface IPrintBackend
{
    PrinterStatusSnapshot GetStatus(string printerName);
    IRawJobWriter StartJob(string printerName, string documentName);
    Task<SpoolWaitResult> WaitForJobAsync(string printerName, int jobId, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>Gerçek Windows yazdırma biriktiricisi.</summary>
internal sealed class WindowsPrintBackend : IPrintBackend
{
    public static readonly WindowsPrintBackend Instance = new();

    public PrinterStatusSnapshot GetStatus(string printerName) => PrinterDiscovery.GetStatus(printerName);

    public IRawJobWriter StartJob(string printerName, string documentName) => RawPrintJob.Start(printerName, documentName);

    public Task<SpoolWaitResult> WaitForJobAsync(string printerName, int jobId, TimeSpan timeout, CancellationToken cancellationToken) =>
        SpoolerMonitor.WaitForJobAsync(printerName, jobId, timeout, null, cancellationToken);
}

/// <summary>
/// TSPL RAW baskı servisi. Etiket görüntüleri (1-bit, yazıcı çözünürlüğünde) TSPL BITMAP komutlarına çevrilip
/// Windows kuyruğuna "RAW" olarak gönderilir; sürücü görüntüyü yeniden ölçeklemez.
/// Büyük toplu işler en fazla <see cref="MaxLabelsPerJob"/> etiketlik parçalara bölünür; iptal etiket sınırında durur
/// (yarım etiket gönderilmez). Arayüz iş parçacığını kilitlemez.
/// </summary>
public sealed class TsplPrintService
{
    private readonly IPrintBackend _backend;

    public TsplPrintService()
        : this(WindowsPrintBackend.Instance)
    {
    }

    internal TsplPrintService(IPrintBackend backend) => _backend = backend;

    /// <summary>Tek bir RAW işteki en fazla etiket görüntüsü.</summary>
    public int MaxLabelsPerJob { get; init; } = 25;

    /// <summary>
    /// Bütün etiketleri TEK bir Windows işi olarak gönder: aynı yazıcıya başka bir programın (ör. sipariş sisteminin
    /// etiket dinleyicisi) işi araya giremez. İptal yine etiket sınırında durur, ancak o ana kadar yazılan etiketler
    /// tek iş olarak kuyruğa verilir; kuyruğa verilen iş Windows kuyruğundan silinmedikçe basılır.
    /// </summary>
    public bool SingleJob { get; init; }

    /// <summary>Son işin kuyruk sonucunu bekleme süresi.</summary>
    public TimeSpan QueueWaitTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public string DocumentName { get; init; } = AkelEtiket.Core.AppIdentity.ProductName;

    /// <summary>
    /// Etiketleri yazdırır. <paramref name="pages"/> tembel (lazy) olabilir: görüntüler arka plan iş parçacığında,
    /// gönderildikçe üretilir. <paramref name="totalLabelCount"/> yalnızca ilerleme göstergesi içindir.
    /// </summary>
    public Task<PrintReport> PrintAsync(LabelMedia media, LabelPrinterSettings settings, IEnumerable<LabelPage> pages,
        int totalLabelCount, IProgress<PrintProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(pages);
        var snapshot = settings.Clone();
        return Task.Run(() => PrintCoreAsync(media, snapshot, pages, totalLabelCount, progress, cancellationToken), CancellationToken.None);
    }

    /// <summary>Hazır ham veriyi (kalibrasyon, besleme, kendi kendine test) tek iş olarak gönderir.</summary>
    public Task<PrintReport> SendRawAsync(string printerName, byte[] data, string documentName,
        CancellationToken cancellationToken = default, bool allowWhenNotReady = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Task.Run(async () =>
        {
            var settings = new LabelPrinterSettings { PrinterName = printerName ?? "", AllowPrintWhenOffline = allowWhenNotReady };
            var report = new PrintReport { Method = PrintMethod.TsplRaw, PrinterName = settings.PrinterName };
            if (string.IsNullOrWhiteSpace(settings.PrinterName))
            {
                report.Errors.Add("Yazıcı seçilmedi. Ayarlar'dan etiket yazıcısını seçin.");
                return report;
            }
            if (data.Length == 0)
            {
                report.Errors.Add("Gönderilecek veri boş.");
                return report;
            }
            if (!CheckPrinter(report, settings, duringPrint: false)) return report;
            report.DataGenerated = true;
            report.BytesGenerated = data.Length;
            if (cancellationToken.IsCancellationRequested)
            {
                report.Canceled = true;
                return report;
            }

            IRawJobWriter? job = null;
            try
            {
                job = _backend.StartJob(settings.PrinterName, string.IsNullOrWhiteSpace(documentName) ? DocumentName : documentName);
                report.JobIds.Add(job.JobId);
                job.Write(data);
                job.Complete();
                report.Spooled = true;
            }
            catch (PrintEngineException ex)
            {
                report.Errors.Add(ex.Message);
                job?.Abort();
                return report;
            }
            finally
            {
                job?.Dispose();
            }
            await WaitForQueueAsync(report, settings.PrinterName, report.JobIds, cancellationToken).ConfigureAwait(false);
            return report;
        }, CancellationToken.None);
    }

    private async Task<PrintReport> PrintCoreAsync(LabelMedia media, LabelPrinterSettings settings, IEnumerable<LabelPage> pages,
        int totalLabelCount, IProgress<PrintProgress>? progress, CancellationToken cancellationToken)
    {
        var report = new PrintReport { Method = PrintMethod.TsplRaw, PrinterName = settings.PrinterName };
        report.Errors.AddRange(settings.Validate());
        report.Errors.AddRange(media.Validate());
        if (report.Errors.Count > 0) return report;

        progress?.Report(new PrintProgress { Done = 0, Total = Math.Max(0, totalLabelCount), Stage = "Yazıcı denetleniyor" });
        if (!CheckPrinter(report, settings, duringPrint: false)) return report;
        if (cancellationToken.IsCancellationRequested)
        {
            report.Canceled = true;
            return report;
        }

        byte[] setup = TsplCommandBuilder.BuildSetup(media, settings);
        int maxPerJob = SingleJob ? int.MaxValue : Math.Max(1, MaxLabelsPerJob);
        int total = Math.Max(0, totalLabelCount);
        IRawJobWriter? job = null;
        int labelsInJob = 0, jobNumber = 0, done = 0;
        bool firstLabel = true;
        var completedJobs = new List<int>();

        try
        {
            using var enumerator = pages.GetEnumerator();
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    report.Canceled = true;
                    break;
                }

                LabelPage? page;
                try
                {
                    if (!enumerator.MoveNext()) break;
                    page = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    report.Canceled = true;
                    break;
                }
                catch (Exception ex) when (ex is not PrintEngineException)
                {
                    report.Errors.Add($"{done + 1}. etiket oluşturulamadı: {ex.Message}");
                    break;
                }
                if (page?.Bitmap is null)
                {
                    report.Errors.Add($"{done + 1}. etiketin görüntüsü boş.");
                    break;
                }

                if (firstLabel)
                {
                    firstLabel = false;
                    AddWarnings(report, TsplCommandBuilder.CheckLabel(media, settings, page.Bitmap.Width, page.Bitmap.Height));
                }
                var labelWarnings = new List<string>();
                byte[] data = TsplCommandBuilder.BuildLabel(page.Bitmap, settings, page.Copies, labelWarnings);
                AddWarnings(report, labelWarnings);
                report.DataGenerated = true;
                report.LabelsGenerated++;
                report.CopiesTotal += TsplCommandBuilder.ClampCopies(page.Copies);
                report.BytesGenerated += data.Length;

                if (job is null)
                {
                    // Parçalar arasında yazıcı yeniden denetlenir (baskı sırasında çevrimdışı olabilir).
                    if (jobNumber > 0 && !CheckPrinter(report, settings, duringPrint: true)) break;
                    jobNumber++;
                    string name = total > maxPerJob ? $"{DocumentName} — bölüm {jobNumber}" : DocumentName;
                    job = _backend.StartJob(settings.PrinterName, name);
                    report.JobIds.Add(job.JobId);
                    job.Write(setup);
                    report.BytesGenerated += setup.Length;
                }

                job.Write(data);
                labelsInJob++;
                done++;
                progress?.Report(new PrintProgress { Done = done, Total = Math.Max(total, done), Stage = "Yazıcı kuyruğuna gönderiliyor" });

                if (labelsInJob >= maxPerJob)
                {
                    job.Complete();
                    completedJobs.Add(job.JobId);
                    report.LabelsSpooled += labelsInJob;
                    report.Spooled = true;
                    job.Dispose();
                    job = null;
                    labelsInJob = 0;
                }
            }

            if (job is not null)
            {
                if (labelsInJob > 0)
                {
                    // İptalde bile parçadaki etiketler eksiksizdir; yarım etiket yoktur.
                    job.Complete();
                    completedJobs.Add(job.JobId);
                    report.LabelsSpooled += labelsInJob;
                    report.Spooled = true;
                }
                else
                {
                    job.Abort();
                    report.JobIds.Remove(job.JobId);
                }
                job.Dispose();
                job = null;
            }
        }
        catch (PrintEngineException ex)
        {
            report.Errors.Add(ex.Message);
            if (job is not null)
            {
                job.Abort();
                report.JobIds.Remove(job.JobId);
            }
        }
        finally
        {
            job?.Dispose();
        }

        if (report.Canceled && total > 0)
            report.Warnings.Add($"İptal edildi: {report.LabelsSpooled} etiket görüntüsü gönderildi, kalanlar gönderilmedi.");
        if (!report.DataGenerated && report.Errors.Count == 0 && !report.Canceled)
            report.Errors.Add("Yazdırılacak etiket yok.");

        if (completedJobs.Count > 0)
        {
            progress?.Report(new PrintProgress { Done = done, Total = Math.Max(total, done), Stage = "Kuyruk durumu bekleniyor" });
            await WaitForQueueAsync(report, settings.PrinterName, completedJobs, CancellationToken.None).ConfigureAwait(false);
        }
        progress?.Report(new PrintProgress { Done = done, Total = Math.Max(total, done), Stage = report.Success ? "Tamamlandı" : "Bitti" });
        return report;
    }

    /// <summary>İşlerin kuyruk sonucunu bekler; en kötü sonucu rapora yazar.</summary>
    private async Task WaitForQueueAsync(PrintReport report, string printerName, IReadOnlyList<int> jobIds, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + QueueWaitTimeout;
        SpoolWaitResult? worst = null;
        bool anyPrinted = false;
        try
        {
            foreach (int jobId in jobIds)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining < TimeSpan.FromMilliseconds(500)) remaining = TimeSpan.FromMilliseconds(500);
                var result = await _backend.WaitForJobAsync(printerName, jobId, remaining, cancellationToken).ConfigureAwait(false);
                if (result.Outcome == SpoolOutcome.Printed) anyPrinted = true;
                if (!result.Delivered)
                {
                    worst = result;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            report.QueueOutcome = SpoolOutcome.TimedOut;
            report.QueueMessage = "Kuyruk takibi iptal edildi; işlerin durumunu Windows yazıcı kuyruğundan kontrol edin.";
            return;
        }

        if (worst is null)
        {
            var outcome = anyPrinted ? SpoolOutcome.Printed : SpoolOutcome.LeftQueue;
            report.QueueOutcome = outcome;
            report.QueueMessage = SpoolerMonitor.DescribeOutcome(outcome);
            return;
        }

        report.QueueOutcome = worst.Outcome;
        report.QueueMessage = worst.Message;
        if (worst.Outcome == SpoolOutcome.TimedOut)
            report.Warnings.Add(worst.Message);
        else
            report.Errors.Add(worst.Message);
    }

    /// <summary>Yazıcının varlığını ve hazır olup olmadığını denetler.</summary>
    private bool CheckPrinter(PrintReport report, LabelPrinterSettings settings, bool duringPrint)
    {
        var status = _backend.GetStatus(settings.PrinterName);
        if (!status.Exists)
        {
            report.Errors.Add(status.ErrorMessage ?? Win32ErrorText.Explain(Win32ErrorText.ErrorInvalidPrinterName, settings.PrinterName));
            return false;
        }
        var printer = status.Printer!;
        if (!duringPrint && !printer.IsLikelyTsplPrinter)
        {
            report.Warnings.Add($"'{printer.Name}' yazıcısının sürücüsü ({printer.DriverName}) TSPL destekli görünmüyor. " +
                                "TSPL komutları bu yazıcıda anlamsız metin olarak basılabilir; doğru yazıcıyı seçtiğinizden emin olun.");
        }
        if (printer.IsReady) return true;
        if (settings.AllowPrintWhenOffline)
        {
            AddWarnings(report, [$"Yazıcı hazır görünmüyor ({printer.StatusText}); ayar gereği yine de gönderildi."]);
            return true;
        }
        report.Errors.Add(duringPrint
            ? $"Yazıcı baskı sırasında hazır durumdan çıktı: {printer.StatusText}. {Advice(printer)}"
            : $"Yazıcı hazır değil: {printer.StatusText}. {Advice(printer)}");
        return false;
    }

    internal static string Advice(PrinterInfo printer)
    {
        if (printer.UsbConnected == false)
            return "Yazıcının açık ve USB kablosunun takılı olduğunu kontrol edin; kabloyu farklı bir USB yuvasına taktıysanız Windows yeni bir yazıcı kopyası oluşturmuş olabilir.";
        if (printer.IsWorkOffline)
            return "Windows'ta yazıcı kuyruğunu açıp Yazıcı menüsünden 'Yazıcıyı Çevrimdışı Kullan' seçeneğini kaldırın.";
        if (printer.IsOffline)
            return "Yazıcının açık ve bağlı olduğunu kontrol edin.";
        if (printer.Status.HasFlag(PrinterStatusFlags.Paused))
            return "Windows yazıcı kuyruğunu açıp 'Yazdırmayı Sürdür' seçeneğini kullanın.";
        if (printer.Status.HasFlag(PrinterStatusFlags.PaperOut))
            return "Etiket rulosunu kontrol edin.";
        return "Yazıcının ışıklarını ve Windows yazıcı kuyruğunu kontrol edin.";
    }

    private static void AddWarnings(PrintReport report, IEnumerable<string> warnings)
    {
        foreach (var warning in warnings)
            if (!report.Warnings.Contains(warning)) report.Warnings.Add(warning);
    }
}
