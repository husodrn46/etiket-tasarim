using System.Diagnostics;
using System.Runtime.InteropServices;
using AkelEtiket.PrintEngine.Interop;

namespace AkelEtiket.PrintEngine;

/// <summary>Windows JOB_INFO_1.Status bayrakları.</summary>
[Flags]
public enum JobStatusFlags : uint
{
    None = 0,
    Paused = 0x00000001,
    Error = 0x00000002,
    Deleting = 0x00000004,
    Spooling = 0x00000008,
    Printing = 0x00000010,
    Offline = 0x00000020,
    PaperOut = 0x00000040,
    Printed = 0x00000080,
    Deleted = 0x00000100,
    BlockedDevQueue = 0x00000200,
    UserIntervention = 0x00000400,
    Restart = 0x00000800,
    Complete = 0x00001000,
    Retained = 0x00002000,
    RenderingLocally = 0x00004000,
}

/// <summary>Kuyruktaki bir işin anlık durumu.</summary>
public sealed class JobSnapshot
{
    public int JobId { get; init; }
    public string Document { get; init; } = "";
    public JobStatusFlags Status { get; init; }

    /// <summary>Sürücünün/port izleyicisinin verdiği durum metni (varsa).</summary>
    public string? DriverStatus { get; init; }

    public int Position { get; init; }
    public int TotalPages { get; init; }
    public int PagesPrinted { get; init; }

    public string StatusText => SpoolerMonitor.DescribeJobStatus(Status, DriverStatus);

    public override string ToString() => $"İş {JobId}: {StatusText}";
}

/// <summary>Kuyruk takibinin sonucu.</summary>
public enum SpoolOutcome
{
    /// <summary>İş kuyruktan çıktı: veri yazıcı portuna iletildi (fiziksel baskının kanıtı DEĞİLDİR).</summary>
    LeftQueue,
    /// <summary>Kuyruk işi "yazıcıya gönderildi" olarak işaretledi (fiziksel baskının kanıtı DEĞİLDİR).</summary>
    Printed,
    Error,
    Offline,
    PaperOut,
    Paused,
    UserIntervention,
    Deleted,
    /// <summary>Süre doldu, iş hâlâ kuyrukta.</summary>
    TimedOut,
}

/// <summary>Kuyruk takibi sonucu ve Türkçe açıklaması.</summary>
public sealed record SpoolWaitResult(SpoolOutcome Outcome, string Message, JobSnapshot? LastSnapshot)
{
    /// <summary>Veri yazıcıya iletildi mi (fiziksel baskı ayrıca kontrol edilmelidir)?</summary>
    public bool Delivered => Outcome is SpoolOutcome.LeftQueue or SpoolOutcome.Printed;
}

/// <summary>Windows yazdırma kuyruğundaki işleri izler (yalnızca okuma; iptal hariç).</summary>
public static class SpoolerMonitor
{
    /// <summary>Hata durumunun kalıcı sayılması için gereken süre (anlık dalgalanmaları elemek için).</summary>
    private static readonly TimeSpan ProblemPersistence = TimeSpan.FromSeconds(2.5);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>İşin anlık durumu; iş kuyrukta yoksa veya yazıcı bulunamazsa null.</summary>
    public static JobSnapshot? GetJob(string printerName, int jobId)
    {
        if (string.IsNullOrWhiteSpace(printerName) || jobId <= 0) return null;
        if (!NativeMethods.TryOpen(printerName, out var handle, out _)) return null;
        using (handle)
        {
            NativeMethods.GetJob(handle, jobId, 1, IntPtr.Zero, 0, out int needed);
            if (needed <= 0) return null; // ERROR_INVALID_PARAMETER: iş artık kuyrukta değil
            IntPtr buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!NativeMethods.GetJob(handle, jobId, 1, buffer, needed, out _)) return null;
                return ToSnapshot(Marshal.PtrToStructure<NativeMethods.JOB_INFO_1>(buffer));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    /// <summary>Yazıcının kuyruğundaki işler (bulunamazsa boş liste).</summary>
    public static IReadOnlyList<JobSnapshot> GetJobs(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName)) return [];
        if (!NativeMethods.TryOpen(printerName, out var handle, out _)) return [];
        using (handle)
        {
            NativeMethods.EnumJobs(handle, 0, 1000, 1, IntPtr.Zero, 0, out int needed, out _);
            if (needed <= 0) return [];
            IntPtr buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!NativeMethods.EnumJobs(handle, 0, 1000, 1, buffer, needed, out _, out int returned)) return [];
                int size = Marshal.SizeOf<NativeMethods.JOB_INFO_1>();
                var jobs = new List<JobSnapshot>(returned);
                for (int i = 0; i < returned; i++)
                    jobs.Add(ToSnapshot(Marshal.PtrToStructure<NativeMethods.JOB_INFO_1>(buffer + i * size)));
                return jobs;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    /// <summary>
    /// İş kuyruktan çıkana veya kalıcı bir sorun görülene kadar bekler (süre sınırlı).
    /// "Kuyruktan çıktı" yalnızca verinin yazıcı portuna iletildiğini gösterir; etiketin basıldığını kanıtlamaz.
    /// İptal edilirse <see cref="OperationCanceledException"/> fırlatır.
    /// </summary>
    public static Task<SpoolWaitResult> WaitForJobAsync(string printerName, int jobId, TimeSpan timeout,
        IProgress<JobSnapshot>? progress = null, CancellationToken cancellationToken = default) =>
        WaitForJobAsync(() => GetJob(printerName, jobId), () => PrinterDiscovery.GetStatus(printerName), timeout, progress, cancellationToken);

    /// <summary>Bekleme döngüsü; iş ve yazıcı durumu verilen işlevlerle okunur (testlerde sahte kuyruk kullanılır).</summary>
    internal static async Task<SpoolWaitResult> WaitForJobAsync(Func<JobSnapshot?> getJob, Func<PrinterStatusSnapshot> getPrinter,
        TimeSpan timeout, IProgress<JobSnapshot>? progress, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        JobSnapshot? last = null;
        bool seenPrinted = false;
        SpoolOutcome? problem = null;
        TimeSpan problemSince = TimeSpan.Zero;
        // null: yazıcıya ilk turda hemen bakılır. (TimeSpan.MinValue ile başlamak "Elapsed - MinValue" çıkarmasında
        // taşma hatası veriyordu: iş ilk turda kuyrukta görünen her gerçek baskıda.)
        TimeSpan? lastPrinterCheck = null;
        SpoolOutcome? printerProblem = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = getJob();
            if (snapshot is null && getPrinter() is { Exists: false } missing)
            {
                // İş değil yazıcı kuyruğunun kendisi kayboldu (silindi/biriktirici durdu): "iletildi" sayılamaz.
                return new SpoolWaitResult(SpoolOutcome.Error, missing.ErrorMessage ?? "Yazıcı kuyruğu bulunamadı.", last);
            }
            if (snapshot is null)
            {
                return seenPrinted
                    ? new SpoolWaitResult(SpoolOutcome.Printed, DescribeOutcome(SpoolOutcome.Printed, last), last)
                    : new SpoolWaitResult(SpoolOutcome.LeftQueue, DescribeOutcome(SpoolOutcome.LeftQueue, last), last);
            }
            last = snapshot;
            progress?.Report(snapshot);

            var status = snapshot.Status;
            if ((status & (JobStatusFlags.Deleted | JobStatusFlags.Deleting)) != 0)
                return new SpoolWaitResult(SpoolOutcome.Deleted, DescribeOutcome(SpoolOutcome.Deleted, snapshot), snapshot);
            if ((status & (JobStatusFlags.Printed | JobStatusFlags.Complete)) != 0)
            {
                seenPrinted = true;
                // "Yazdırılan işleri tut" açıksa iş kuyrukta kalır; tamamlandı bayrağı yeterlidir.
                if ((status & JobStatusFlags.Retained) != 0 || (status & JobStatusFlags.Printing) == 0)
                    return new SpoolWaitResult(SpoolOutcome.Printed, DescribeOutcome(SpoolOutcome.Printed, snapshot), snapshot);
            }

            // İş bayrakları temizken yazıcı kuyruğunun kendisi duraklatılmış/çevrimdışı olabilir.
            if (lastPrinterCheck is not { } checkedAt || clock.Elapsed - checkedAt >= TimeSpan.FromSeconds(1))
            {
                lastPrinterCheck = clock.Elapsed;
                printerProblem = ClassifyPrinter(getPrinter().Printer);
            }
            var current = ClassifyProblem(status) ?? printerProblem;
            if (current is null)
            {
                problem = null;
            }
            else if (problem != current)
            {
                problem = current;
                problemSince = clock.Elapsed;
            }
            else if (clock.Elapsed - problemSince >= ProblemPersistence)
            {
                return new SpoolWaitResult(current.Value, DescribeOutcome(current.Value, snapshot), snapshot);
            }

            if (clock.Elapsed >= timeout)
            {
                var outcome = problem ?? SpoolOutcome.TimedOut;
                return new SpoolWaitResult(outcome, DescribeOutcome(outcome, snapshot), snapshot);
            }
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Kuyruktaki işi siler (kullanıcı iptali). Başarılıysa true.</summary>
    public static bool CancelJob(string printerName, int jobId)
    {
        if (string.IsNullOrWhiteSpace(printerName) || jobId <= 0) return false;
        if (!NativeMethods.TryOpen(printerName, out var handle, out _)) return false;
        using (handle)
        {
            return NativeMethods.SetJob(handle, jobId, 0, IntPtr.Zero, NativeMethods.JOB_CONTROL_DELETE)
                   || NativeMethods.SetJob(handle, jobId, 0, IntPtr.Zero, NativeMethods.JOB_CONTROL_CANCEL);
        }
    }

    internal static SpoolOutcome? ClassifyProblem(JobStatusFlags status)
    {
        if ((status & JobStatusFlags.Offline) != 0) return SpoolOutcome.Offline;
        if ((status & JobStatusFlags.PaperOut) != 0) return SpoolOutcome.PaperOut;
        if ((status & JobStatusFlags.UserIntervention) != 0) return SpoolOutcome.UserIntervention;
        if ((status & JobStatusFlags.Error) != 0) return SpoolOutcome.Error;
        if ((status & (JobStatusFlags.Paused | JobStatusFlags.BlockedDevQueue)) != 0) return SpoolOutcome.Paused;
        return null;
    }

    internal static SpoolOutcome? ClassifyPrinter(PrinterInfo? printer)
    {
        if (printer is null) return null;
        if (printer.IsOffline) return SpoolOutcome.Offline;
        var s = printer.Status;
        if ((s & PrinterStatusFlags.PaperOut) != 0) return SpoolOutcome.PaperOut;
        if ((s & (PrinterStatusFlags.UserIntervention | PrinterStatusFlags.DoorOpen | PrinterStatusFlags.PaperJam)) != 0)
            return SpoolOutcome.UserIntervention;
        if ((s & PrinterStatusFlags.Paused) != 0) return SpoolOutcome.Paused;
        if ((s & PrinterStatusFlags.Error) != 0) return SpoolOutcome.Error;
        return null;
    }

    /// <summary>Sonucun Türkçe açıklaması.</summary>
    public static string DescribeOutcome(SpoolOutcome outcome, JobSnapshot? snapshot = null)
    {
        string driver = string.IsNullOrWhiteSpace(snapshot?.DriverStatus) ? "" : $" (Windows: {snapshot!.DriverStatus})";
        return outcome switch
        {
            SpoolOutcome.LeftQueue =>
                "İş Windows kuyruğundan çıktı; veri yazıcı portuna iletildi. Etiketin basıldığını yazıcıdan kontrol edin.",
            SpoolOutcome.Printed =>
                "Windows işi 'yazıcıya gönderildi' olarak işaretledi. Etiketin basıldığını yazıcıdan kontrol edin.",
            SpoolOutcome.Offline => "Yazıcı çevrimdışı; iş kuyrukta bekliyor. USB kablosunu ve yazıcının açık olduğunu kontrol edin." + driver,
            SpoolOutcome.PaperOut => "Etiket/kağıt bitti; iş kuyrukta bekliyor. Etiket rulosunu kontrol edin." + driver,
            SpoolOutcome.UserIntervention => "Yazıcı müdahale bekliyor (kapak, etiket veya hata ışığını kontrol edin)." + driver,
            SpoolOutcome.Error => "Yazdırma işinde hata oluştu; Windows yazıcı kuyruğunu kontrol edin." + driver,
            SpoolOutcome.Paused => "Yazdırma işi veya yazıcı kuyruğu duraklatılmış; Windows yazıcı kuyruğundan devam ettirin." + driver,
            SpoolOutcome.Deleted => "Yazdırma işi kuyruktan silindi (iptal edildi).",
            SpoolOutcome.TimedOut when snapshot is not null && snapshot.Status.HasFlag(JobStatusFlags.Printing) =>
                "Veri yazıcıya aktarılmaya devam ediyor (yazıcı belleği doldukça bastıkça alır; büyük işlerde normaldir). " +
                "İlerlemeyi Windows yazıcı kuyruğundan izleyebilirsiniz." + driver,
            SpoolOutcome.TimedOut =>
                "İş belirtilen sürede kuyruktan çıkmadı; yazıcı bağlı, açık ve hazır mı? Windows yazıcı kuyruğunu kontrol edin." + driver,
            _ => outcome.ToString(),
        };
    }

    internal static string DescribeJobStatus(JobStatusFlags status, string? driverStatus)
    {
        var parts = new List<string>();
        if (status.HasFlag(JobStatusFlags.Deleted)) parts.Add("Silindi");
        else if (status.HasFlag(JobStatusFlags.Deleting)) parts.Add("Siliniyor");
        if (status.HasFlag(JobStatusFlags.Offline)) parts.Add("Yazıcı çevrimdışı");
        if (status.HasFlag(JobStatusFlags.PaperOut)) parts.Add("Kağıt bitti");
        if (status.HasFlag(JobStatusFlags.UserIntervention)) parts.Add("Müdahale gerekiyor");
        if (status.HasFlag(JobStatusFlags.Error)) parts.Add("Hata");
        if (status.HasFlag(JobStatusFlags.Paused)) parts.Add("Duraklatıldı");
        if (status.HasFlag(JobStatusFlags.BlockedDevQueue)) parts.Add("Engellendi");
        if (status.HasFlag(JobStatusFlags.Restart)) parts.Add("Yeniden başlatılıyor");
        if (status.HasFlag(JobStatusFlags.Spooling)) parts.Add("Biriktiriliyor");
        if (status.HasFlag(JobStatusFlags.Printing)) parts.Add("Yazıcıya gönderiliyor");
        if (status.HasFlag(JobStatusFlags.Printed) || status.HasFlag(JobStatusFlags.Complete)) parts.Add("Yazıcıya gönderildi");
        if (status.HasFlag(JobStatusFlags.Retained)) parts.Add("Saklandı");
        string text = parts.Count == 0 ? "Kuyrukta bekliyor" : string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(driverStatus) ? text : $"{text} ({driverStatus})";
    }

    private static JobSnapshot ToSnapshot(NativeMethods.JOB_INFO_1 info) => new()
    {
        JobId = (int)info.JobId,
        Document = NativeMethods.PtrToString(info.pDocument) ?? "",
        Status = (JobStatusFlags)info.Status,
        DriverStatus = NativeMethods.PtrToString(info.pStatus),
        Position = (int)info.Position,
        TotalPages = (int)info.TotalPages,
        PagesPrinted = (int)info.PagesPrinted,
    };
}
