using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AkelEtiket.PrintEngine.Interop;

/// <summary>
/// macOS yazdırma sistemi (CUPS) çağrıları. Yalnızca sistemin kendi kitaplığı kullanılır (ek paket yok).
/// Bağlantı parametresi her yerde <c>CUPS_HTTP_DEFAULT</c> (sıfır) verilir: yerel yazdırma hizmeti.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe class CupsNative
{
    private const string Library = "/usr/lib/libcups.2.dylib";

    /// <summary>Ham veri: süzgeçlerden geçmeden yazıcıya iletilir.</summary>
    public const string FormatRaw = "application/vnd.cups-raw";

    public const int HttpContinue = 100;
    public const int WhichJobsAll = -1;

    /// <summary>IPP iş durumları (ipp_jstate_t).</summary>
    public const int JobPending = 3, JobHeld = 4, JobProcessing = 5, JobStopped = 6, JobCanceled = 7, JobAborted = 8, JobCompleted = 9;

    /// <summary>IPP durum kodu başarı mı? (0x0000–0x00FF başarı sınıfıdır.)</summary>
    public static bool IsOk(int ippStatus) => ippStatus is >= 0 and < 0x100;

    [DllImport(Library)]
    public static extern int cupsGetDests2(IntPtr http, out IntPtr dests);

    [DllImport(Library)]
    public static extern void cupsFreeDests(int count, IntPtr dests);

    [DllImport(Library)]
    public static extern int cupsCreateJob(IntPtr http, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title, int optionCount, IntPtr options);

    [DllImport(Library)]
    public static extern int cupsStartDocument(IntPtr http, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int jobId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string documentName, [MarshalAs(UnmanagedType.LPUTF8Str)] string format, int lastDocument);

    [DllImport(Library)]
    public static extern int cupsWriteRequestData(IntPtr http, byte* buffer, nuint length);

    [DllImport(Library)]
    public static extern int cupsFinishDocument(IntPtr http, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Library)]
    public static extern int cupsCancelJob2(IntPtr http, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int jobId, int purge);

    [DllImport(Library)]
    public static extern int cupsGetJobs2(IntPtr http, out IntPtr jobs, [MarshalAs(UnmanagedType.LPUTF8Str)] string? name, int myJobs, int whichJobs);

    [DllImport(Library)]
    public static extern void cupsFreeJobs(int count, IntPtr jobs);

    [DllImport(Library)]
    private static extern IntPtr cupsLastErrorString();

    /// <summary>Son çağrının IPP durum kodu.</summary>
    [DllImport(Library)]
    public static extern int cupsLastError();

    /// <summary>Yazdırma hizmeti yanıt vermiyor (IPP_STATUS_ERROR_SERVICE_UNAVAILABLE).</summary>
    public const int ServiceUnavailable = 0x0502;

    public static string LastError() => Marshal.PtrToStringUTF8(cupsLastErrorString()) ?? "";

    /// <summary>cups_dest_t: name, instance, is_default, num_options, options (64 bit: 32 bayt).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Destination
    {
        public IntPtr Name;
        public IntPtr Instance;
        public int IsDefault;
        public int OptionCount;
        public IntPtr Options;
    }

    /// <summary>cups_option_t.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Option
    {
        public IntPtr Name;
        public IntPtr Value;
    }

    /// <summary>cups_job_t.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Job
    {
        public int Id;
        public IntPtr Destination;
        public IntPtr Title;
        public IntPtr User;
        public IntPtr Format;
        public int State;
        public int Size;
        public int Priority;
        public nint CompletedTime;
        public nint CreationTime;
        public nint ProcessingTime;
    }
}
