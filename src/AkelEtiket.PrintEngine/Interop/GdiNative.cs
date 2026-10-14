using System.Runtime.InteropServices;

namespace AkelEtiket.PrintEngine.Interop;

/// <summary>Sürücü (GDI) baskısı için gdi32/winspool çağrıları ve DEVMODE alan konumları.</summary>
internal static unsafe class GdiNative
{
    public const int DM_OUT_BUFFER = 2;
    public const int DM_IN_BUFFER = 8;

    public const uint DM_ORIENTATION = 0x00000001;
    public const uint DM_PAPERSIZE = 0x00000002;
    public const uint DM_PAPERLENGTH = 0x00000004;
    public const uint DM_PAPERWIDTH = 0x00000008;
    public const uint DM_SCALE = 0x00000010;
    public const uint DM_COPIES = 0x00000100;
    public const uint DM_FORMNAME = 0x00010000;

    public const short DMORIENT_PORTRAIT = 1;
    public const short DMPAPER_USER = 256;

    // DEVMODEW sabit konumları (işaretçi içermez; x86/x64 aynı).
    public const int OffsetDmSize = 68;
    public const int OffsetDmDriverExtra = 70;
    public const int OffsetDmFields = 72;
    public const int OffsetDmOrientation = 76;
    public const int OffsetDmPaperSize = 78;
    public const int OffsetDmPaperLength = 80;
    public const int OffsetDmPaperWidth = 82;
    public const int OffsetDmScale = 84;
    public const int OffsetDmCopies = 86;
    public const int MinimumDevModeSize = 88;

    public const int LOGPIXELSX = 88;
    public const int LOGPIXELSY = 90;
    public const int RASTERCAPS = 38;
    public const int PHYSICALWIDTH = 110;
    public const int PHYSICALHEIGHT = 111;
    public const int PHYSICALOFFSETX = 112;
    public const int PHYSICALOFFSETY = 113;
    public const int RC_STRETCHDIB = 0x2000;

    public const int DC_PAPERS = 2;
    public const int DC_PAPERSIZE = 3;

    public const int COLORONCOLOR = 3;
    public const uint DIB_RGB_COLORS = 0;
    public const uint SRCCOPY = 0x00CC0020;
    public const int BI_RGB = 0;

    [DllImport("winspool.drv", EntryPoint = "DocumentPropertiesW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int DocumentProperties(IntPtr hWnd, SafePrinterHandle hPrinter, string pDeviceName,
        IntPtr pDevModeOutput, IntPtr pDevModeInput, int fMode);

    [DllImport("winspool.drv", EntryPoint = "DeviceCapabilitiesW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int DeviceCapabilities(string pDevice, string? pPort, int fwCapability, IntPtr pOutput, IntPtr pDevMode);

    [DllImport("gdi32.dll", EntryPoint = "CreateDCW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateDC(string? lpszDriver, string lpszDevice, string? lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", EntryPoint = "StartDocW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int StartDoc(IntPtr hdc, ref DOCINFOW lpdi);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int StartPage(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int EndPage(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int EndDoc(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int AbortDoc(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern int GetDeviceCaps(IntPtr hdc, int index);

    [DllImport("gdi32.dll")]
    public static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int StretchDIBits(IntPtr hdc, int xDest, int yDest, int destWidth, int destHeight,
        int xSrc, int ySrc, int srcWidth, int srcHeight, byte* bits, BITMAPINFO1* bitmapInfo, uint usage, uint rop);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DOCINFOW
    {
        public int cbSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszDocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszOutput;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszDatatype;
        public int fwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    /// <summary>1 bit/piksel DIB başlığı + 2 renkli palet (RGBQUAD: mavi, yeşil, kırmızı, ayrılmış).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO1
    {
        public BITMAPINFOHEADER Header;
        public uint Color0;
        public uint Color1;
    }
}
