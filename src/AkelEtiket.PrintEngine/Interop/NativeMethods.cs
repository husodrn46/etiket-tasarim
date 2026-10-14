using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AkelEtiket.PrintEngine.Interop;

/// <summary>OpenPrinter tanıtıcısı; serbest bırakılırken ClosePrinter çağrılır.</summary>
internal sealed class SafePrinterHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafePrinterHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle() => NativeMethods.ClosePrinter(handle);
}

/// <summary>winspool.drv ve setupapi.dll çağrıları.</summary>
internal static unsafe class NativeMethods
{
    public const int PRINTER_ENUM_LOCAL = 0x00000002;
    public const int PRINTER_ENUM_CONNECTIONS = 0x00000004;

    public const int JOB_CONTROL_CANCEL = 3;
    public const int JOB_CONTROL_DELETE = 5;

    public const uint PRINTER_ATTRIBUTE_DEFAULT = 0x00000004;
    public const uint PRINTER_ATTRIBUTE_SHARED = 0x00000008;
    public const uint PRINTER_ATTRIBUTE_NETWORK = 0x00000010;
    public const uint PRINTER_ATTRIBUTE_LOCAL = 0x00000040;
    public const uint PRINTER_ATTRIBUTE_KEEPPRINTEDJOBS = 0x00000100;
    public const uint PRINTER_ATTRIBUTE_WORK_OFFLINE = 0x00000400;
    public const uint PRINTER_ATTRIBUTE_ENABLE_BIDI = 0x00000800;
    public const uint PRINTER_ATTRIBUTE_DIRECT = 0x00000002;

    public const int DIGCF_PRESENT = 0x00000002;
    public const int DIGCF_DEVICEINTERFACE = 0x00000010;
    public const int KEY_READ = 0x00020019;

    /// <summary>GUID_DEVINTERFACE_USBPRINT (usbprint.sys aygıt arabirimi).</summary>
    public static readonly Guid GuidDevInterfaceUsbPrint = new("28d78fad-5a12-11d1-ae5b-0000f803a8c2");

    public static readonly IntPtr InvalidHandleValue = new(-1);

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool OpenPrinter(string pPrinterName, out SafePrinterHandle phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int StartDocPrinter(SafePrinterHandle hPrinter, int level, ref DOC_INFO_1 pDocInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool EndDocPrinter(SafePrinterHandle hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool StartPagePrinter(SafePrinterHandle hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool EndPagePrinter(SafePrinterHandle hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool WritePrinter(SafePrinterHandle hPrinter, byte* pBuf, int cbBuf, out int pcWritten);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool AbortPrinter(SafePrinterHandle hPrinter);

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool EnumPrinters(int flags, string? name, int level, IntPtr pPrinterEnum, int cbBuf, out int pcbNeeded, out int pcReturned);

    [DllImport("winspool.drv", EntryPoint = "GetPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool GetPrinter(SafePrinterHandle hPrinter, int level, IntPtr pPrinter, int cbBuf, out int pcbNeeded);

    [DllImport("winspool.drv", EntryPoint = "GetJobW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool GetJob(SafePrinterHandle hPrinter, int jobId, int level, IntPtr pJob, int cbBuf, out int pcbNeeded);

    [DllImport("winspool.drv", EntryPoint = "EnumJobsW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool EnumJobs(SafePrinterHandle hPrinter, int firstJob, int noJobs, int level, IntPtr pJob, int cbBuf, out int pcbNeeded, out int pcReturned);

    [DllImport("winspool.drv", EntryPoint = "SetJobW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetJob(SafePrinterHandle hPrinter, int jobId, int level, IntPtr pJob, int command);

    [DllImport("winspool.drv", EntryPoint = "GetDefaultPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool GetDefaultPrinter(char* pszBuffer, ref int pcchBuffer);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
        int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern IntPtr SetupDiOpenDeviceInterfaceRegKey(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        int reserved, int samDesired);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DOC_INFO_1
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pDocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string pDatatype;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PRINTER_INFO_2
    {
        public IntPtr pServerName;
        public IntPtr pPrinterName;
        public IntPtr pShareName;
        public IntPtr pPortName;
        public IntPtr pDriverName;
        public IntPtr pComment;
        public IntPtr pLocation;
        public IntPtr pDevMode;
        public IntPtr pSepFile;
        public IntPtr pPrintProcessor;
        public IntPtr pDatatype;
        public IntPtr pParameters;
        public IntPtr pSecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint cJobs;
        public uint AveragePPM;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEMTIME
    {
        public ushort wYear;
        public ushort wMonth;
        public ushort wDayOfWeek;
        public ushort wDay;
        public ushort wHour;
        public ushort wMinute;
        public ushort wSecond;
        public ushort wMilliseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOB_INFO_1
    {
        public uint JobId;
        public IntPtr pPrinterName;
        public IntPtr pMachineName;
        public IntPtr pUserName;
        public IntPtr pDocument;
        public IntPtr pDatatype;
        public IntPtr pStatus;
        public uint Status;
        public uint Priority;
        public uint Position;
        public uint TotalPages;
        public uint PagesPrinted;
        public SYSTEMTIME Submitted;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    public static string? PtrToString(IntPtr ptr) => ptr == IntPtr.Zero ? null : Marshal.PtrToStringUni(ptr);

    /// <summary>Yazıcıyı açar; bulunamazsa false ve Windows hata kodu döner.</summary>
    public static bool TryOpen(string printerName, out SafePrinterHandle handle, out int error)
    {
        if (OpenPrinter(printerName, out handle, IntPtr.Zero))
        {
            error = 0;
            return true;
        }
        error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        return false;
    }
}
