using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AkelEtiket.PrintEngine.Interop;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.PrintEngine;

/// <summary>
/// macOS'ta yazıcılar ve yazdırma kuyruğu (CUPS). Windows'taki biriktirici çağrılarının karşılığıdır; aynı
/// <see cref="PrinterInfo"/>, <see cref="JobSnapshot"/> ve <see cref="IRawJobWriter"/> türlerini doldurur, böylece
/// baskı akışının geri kalanı iki sistemde de aynıdır. Yalnızca ham (TSPL) gönderim vardır; sürücü üzerinden baskı yoktur.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class CupsPrintSystem
{
    /// <summary>Sistem Ayarları'nda ekli yazıcılar. Yazdırma hizmetine ulaşılamazsa <see cref="PrintEngineException"/>.</summary>
    public static IReadOnlyList<PrinterInfo> GetPrinters()
    {
        int count = CupsNative.cupsGetDests2(IntPtr.Zero, out IntPtr dests);
        try
        {
            if (count <= 0)
            {
                // Hiç yazıcı eklenmemiş bir Mac'te de sayı sıfırdır ve sistem "hedef eklenmedi" der (ileti sistemin
                // dilindedir, metne bakılmaz); bu bir hata değildir. Yalnız hizmete ulaşılamaması hatadır.
                if (CupsNative.cupsLastError() == CupsNative.ServiceUnavailable)
                    throw new PrintEngineException(L10n.T("Yazıcılar listelenemedi: macOS yazdırma hizmetine ulaşılamadı.") + $" ({CupsNative.LastError()})");
                return [];
            }
            var jobCounts = ActiveJobCounts();
            int size = Marshal.SizeOf<CupsNative.Destination>();
            var list = new List<PrinterInfo>(count);
            for (int i = 0; i < count; i++)
            {
                var dest = Marshal.PtrToStructure<CupsNative.Destination>(dests + i * size);
                // Aynı kuyruğun kayıtlı ayar kümeleri ("örnek") ayrı yazıcı sayılmaz.
                if (dest.Instance != IntPtr.Zero) continue;
                string? name = Marshal.PtrToStringUTF8(dest.Name);
                if (string.IsNullOrEmpty(name)) continue;
                list.Add(ToPrinter(name, dest, jobCounts.GetValueOrDefault(name)));
            }
            return list.OrderByDescending(p => p.IsXp470B).ThenByDescending(p => p.IsLikelyTsplPrinter)
                .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        finally
        {
            if (dests != IntPtr.Zero) CupsNative.cupsFreeDests(count, dests);
        }
    }

    public static PrinterInfo? GetPrinter(string printerName) =>
        GetPrinters().FirstOrDefault(p => string.Equals(p.Name, printerName, StringComparison.Ordinal));

    public static string? GetDefaultPrinterName()
    {
        try
        {
            return GetPrinters().FirstOrDefault(p => p.IsDefault)?.Name;
        }
        catch (PrintEngineException)
        {
            return null;
        }
    }

    /// <summary>Yazıcı bulunamadığında gösterilen, ne yapılacağını söyleyen ileti.</summary>
    public static string NotFoundMessage(string printerName) =>
        L10n.T($"'{printerName}' adlı yazıcı bulunamadı. Yazıcının Sistem Ayarları → Yazıcılar ve Tarayıcılar'da ekli olduğunu ve Ayarlar'da doğru yazıcının seçildiğini kontrol edin.");

    private static PrinterInfo ToPrinter(string name, CupsNative.Destination dest, int jobCount)
    {
        var options = ReadOptions(dest);
        string uri = options.GetValueOrDefault("device-uri", "");
        string reasons = options.GetValueOrDefault("printer-state-reasons", "");
        var status = PrinterStatusFlags.None;
        // printer-state: 3 boşta, 4 yazdırıyor, 5 durduruldu (kuyruk duraklatılmış).
        switch (options.GetValueOrDefault("printer-state", "3"))
        {
            case "4": status |= PrinterStatusFlags.Printing; break;
            case "5": status |= PrinterStatusFlags.Paused; break;
        }
        if (options.GetValueOrDefault("printer-is-accepting-jobs", "true") == "false") status |= PrinterStatusFlags.NotAvailable;
        if (HasReason(reasons, "offline") || HasReason(reasons, "connecting-to-device")) status |= PrinterStatusFlags.Offline;
        if (HasReason(reasons, "media-empty") || HasReason(reasons, "media-needed")) status |= PrinterStatusFlags.PaperOut;
        if (HasReason(reasons, "media-jam")) status |= PrinterStatusFlags.PaperJam;
        if (HasReason(reasons, "cover-open") || HasReason(reasons, "door-open")) status |= PrinterStatusFlags.DoorOpen;
        return new PrinterInfo
        {
            Name = name,
            DriverName = options.GetValueOrDefault("printer-make-and-model", ""),
            PortName = uri,
            DataType = "RAW",
            IsDefault = dest.IsDefault != 0,
            IsShared = options.GetValueOrDefault("printer-is-shared", "false") == "true",
            IsNetwork = uri.Length > 0 && !uri.StartsWith("usb:", StringComparison.OrdinalIgnoreCase),
            Status = status,
            JobCount = jobCount,
            // USB kablosunun takılı olup olmadığı CUPS'tan doğrudan okunmaz; çevrimdışı bayrağı yeterlidir.
            UsbConnected = null,
        };
    }

    /// <summary>Durum nedenleri "-report", "-warning", "-error" son ekleriyle gelir (ör. "offline-report").</summary>
    internal static bool HasReason(string reasons, string reason) =>
        reasons.Split(',', StringSplitOptions.TrimEntries).Any(r => r.StartsWith(reason, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, string> ReadOptions(CupsNative.Destination dest)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        int size = Marshal.SizeOf<CupsNative.Option>();
        for (int i = 0; i < dest.OptionCount && dest.Options != IntPtr.Zero; i++)
        {
            var option = Marshal.PtrToStructure<CupsNative.Option>(dest.Options + i * size);
            string? key = Marshal.PtrToStringUTF8(option.Name);
            if (key is not null) options[key] = Marshal.PtrToStringUTF8(option.Value) ?? "";
        }
        return options;
    }

    // ───────────────────────── İşler ─────────────────────────

    private static List<(string Printer, JobSnapshot Job)> ReadJobs(string? printerName)
    {
        int count = CupsNative.cupsGetJobs2(IntPtr.Zero, out IntPtr jobs, printerName, 0, CupsNative.WhichJobsAll);
        var list = new List<(string, JobSnapshot)>();
        if (count <= 0 || jobs == IntPtr.Zero) return list;
        try
        {
            int size = Marshal.SizeOf<CupsNative.Job>();
            for (int i = 0; i < count; i++)
            {
                var job = Marshal.PtrToStructure<CupsNative.Job>(jobs + i * size);
                list.Add((Marshal.PtrToStringUTF8(job.Destination) ?? "", new JobSnapshot
                {
                    JobId = job.Id,
                    Document = Marshal.PtrToStringUTF8(job.Title) ?? "",
                    Status = ToStatus(job.State),
                }));
            }
        }
        finally
        {
            CupsNative.cupsFreeJobs(count, jobs);
        }
        return list;
    }

    internal static JobStatusFlags ToStatus(int state) => state switch
    {
        CupsNative.JobHeld or CupsNative.JobStopped => JobStatusFlags.Paused,
        CupsNative.JobProcessing => JobStatusFlags.Printing,
        CupsNative.JobCanceled => JobStatusFlags.Deleted,
        CupsNative.JobAborted => JobStatusFlags.Error,
        CupsNative.JobCompleted => JobStatusFlags.Printed | JobStatusFlags.Complete,
        _ => JobStatusFlags.None,
    };

    private static bool IsActive(JobStatusFlags status) =>
        (status & (JobStatusFlags.Printed | JobStatusFlags.Complete | JobStatusFlags.Deleted | JobStatusFlags.Error)) == 0;

    private static Dictionary<string, int> ActiveJobCounts() =>
        ReadJobs(null).Where(j => IsActive(j.Job.Status)).GroupBy(j => j.Printer, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    /// <summary>İşin anlık durumu (bitmiş işler de geçmişten okunur); iş hiç yoksa null.</summary>
    public static JobSnapshot? GetJob(string printerName, int jobId) =>
        ReadJobs(printerName).Select(j => j.Job).FirstOrDefault(j => j.JobId == jobId);

    /// <summary>Kuyrukta bekleyen ya da yazdırılan işler.</summary>
    public static IReadOnlyList<JobSnapshot> GetJobs(string printerName) =>
        ReadJobs(printerName).Select(j => j.Job).Where(j => IsActive(j.Status)).ToList();

    public static bool CancelJob(string printerName, int jobId) =>
        CupsNative.IsOk(CupsNative.cupsCancelJob2(IntPtr.Zero, printerName, jobId, 0));

    public static IRawJobWriter StartRawJob(string printerName, string documentName)
    {
        if (GetPrinter(printerName) is null) throw new PrintEngineException(NotFoundMessage(printerName));
        int jobId = CupsNative.cupsCreateJob(IntPtr.Zero, printerName, documentName, 0, IntPtr.Zero);
        if (jobId <= 0)
            throw new PrintEngineException(L10n.T("Yazdırma işi başlatılamadı") + $": {CupsNative.LastError()}");
        return new CupsRawJob(printerName, documentName, jobId);
    }
}

/// <summary>
/// CUPS kuyruğuna ham (RAW) iş. Veri bellekte toplanır ve <see cref="Complete"/> çağrısında tek belge olarak
/// gönderilir: belge gönderimi tek bir iş parçacığında, kesintisiz yapılmalıdır. İptalde iş kuyruktan silinir.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class CupsRawJob(string printerName, string documentName, int jobId) : IRawJobWriter
{
    private const int ChunkSize = 64 * 1024;
    private readonly MemoryStream _data = new();
    private bool _finished;

    public int JobId { get; } = jobId;

    public void Write(ReadOnlySpan<byte> data)
    {
        if (_finished) throw new InvalidOperationException(L10n.T("İş tamamlandıktan sonra veri yazılamaz."));
        _data.Write(data);
    }

    public unsafe void Complete()
    {
        if (_finished) return;
        _finished = true;
        int status = CupsNative.cupsStartDocument(IntPtr.Zero, printerName, JobId, documentName, CupsNative.FormatRaw, 1);
        if (status != CupsNative.HttpContinue) throw Fail(L10n.T("Yazdırma işi başlatılamadı"));
        byte[] buffer = _data.GetBuffer();
        int length = (int)_data.Length;
        fixed (byte* p = buffer)
        {
            for (int offset = 0; offset < length; offset += ChunkSize)
            {
                int count = Math.Min(ChunkSize, length - offset);
                if (CupsNative.cupsWriteRequestData(IntPtr.Zero, p + offset, (nuint)count) != CupsNative.HttpContinue)
                    throw Fail(L10n.T("Yazıcıya veri gönderilemedi"));
            }
        }
        if (!CupsNative.IsOk(CupsNative.cupsFinishDocument(IntPtr.Zero, printerName)))
            throw Fail(L10n.T("Yazdırma işi tamamlanamadı"));
    }

    private PrintEngineException Fail(string action)
    {
        string error = CupsNative.LastError();
        // Yarım kalan iş kuyrukta bekleyip sonradan basılmasın.
        CupsNative.cupsCancelJob2(IntPtr.Zero, printerName, JobId, 1);
        return new PrintEngineException($"{action}: {error}");
    }

    public void Abort()
    {
        if (_finished) return;
        _finished = true;
        CupsNative.cupsCancelJob2(IntPtr.Zero, printerName, JobId, 1);
    }

    public void Dispose()
    {
        if (!_finished) Abort();
        _data.Dispose();
    }
}
