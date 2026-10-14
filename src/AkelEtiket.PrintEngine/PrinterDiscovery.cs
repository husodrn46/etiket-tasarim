using System.Runtime.InteropServices;
using AkelEtiket.PrintEngine.Interop;

namespace AkelEtiket.PrintEngine;

/// <summary>Windows PRINTER_INFO_2.Status bayrakları.</summary>
[Flags]
public enum PrinterStatusFlags : uint
{
    None = 0,
    Paused = 0x00000001,
    Error = 0x00000002,
    PendingDeletion = 0x00000004,
    PaperJam = 0x00000008,
    PaperOut = 0x00000010,
    ManualFeed = 0x00000020,
    PaperProblem = 0x00000040,
    Offline = 0x00000080,
    IoActive = 0x00000100,
    Busy = 0x00000200,
    Printing = 0x00000400,
    OutputBinFull = 0x00000800,
    NotAvailable = 0x00001000,
    Waiting = 0x00002000,
    Processing = 0x00004000,
    Initializing = 0x00008000,
    WarmingUp = 0x00010000,
    TonerLow = 0x00020000,
    NoToner = 0x00040000,
    PagePunt = 0x00080000,
    UserIntervention = 0x00100000,
    OutOfMemory = 0x00200000,
    DoorOpen = 0x00400000,
    ServerUnknown = 0x00800000,
    PowerSave = 0x01000000,
    ServerOffline = 0x02000000,
    DriverUpdateNeeded = 0x04000000,
}

/// <summary>Windows'ta kurulu bir yazıcı (kuyruk).</summary>
public sealed class PrinterInfo
{
    /// <summary>Baskıyı engelleyen durum bayrakları.</summary>
    public const PrinterStatusFlags BlockingFlags =
        PrinterStatusFlags.Paused | PrinterStatusFlags.Error | PrinterStatusFlags.PendingDeletion |
        PrinterStatusFlags.PaperJam | PrinterStatusFlags.PaperOut | PrinterStatusFlags.PaperProblem |
        PrinterStatusFlags.Offline | PrinterStatusFlags.NotAvailable | PrinterStatusFlags.UserIntervention |
        PrinterStatusFlags.OutOfMemory | PrinterStatusFlags.DoorOpen | PrinterStatusFlags.ServerOffline |
        PrinterStatusFlags.ServerUnknown;

    public required string Name { get; init; }
    public string DriverName { get; init; } = "";
    public string PortName { get; init; } = "";
    public string? ShareName { get; init; }
    public string? PrintProcessor { get; init; }

    /// <summary>Varsayılan veri türü (ör. "RAW").</summary>
    public string? DataType { get; init; }

    public bool IsDefault { get; init; }
    public bool IsShared { get; init; }
    public bool IsNetwork { get; init; }

    /// <summary>Windows'ta "Yazıcıyı Çevrimdışı Kullan" seçili.</summary>
    public bool IsWorkOffline { get; init; }

    public PrinterStatusFlags Status { get; init; }
    public uint Attributes { get; init; }
    public int JobCount { get; init; }

    /// <summary>USB portu için fiziksel bağlantı (null = USB değil veya bilinmiyor).</summary>
    public bool? UsbConnected { get; init; }

    public bool IsOffline =>
        IsWorkOffline || UsbConnected == false ||
        (Status & (PrinterStatusFlags.Offline | PrinterStatusFlags.ServerOffline | PrinterStatusFlags.NotAvailable)) != 0;

    /// <summary>Baskı gönderilebilir mi (çevrimdışı/duraklatılmış/hata değil)?</summary>
    public bool IsReady => !IsOffline && (Status & BlockingFlags) == 0;

    /// <summary>Sürücü veya ad TSPL konuşan bir etiket yazıcısına işaret ediyor mu (Xprinter, XP-…, TSC, TSPL)?</summary>
    public bool IsLikelyTsplPrinter => ContainsAny(DriverName, "Xprinter", "XP-", "TSC", "TSPL") || ContainsAny(Name, "Xprinter", "XP-", "TSPL");

    /// <summary>Xprinter XP-470B mi?</summary>
    public bool IsXp470B => Normalize(DriverName).Contains("XP470B") || Normalize(Name).Contains("XP470B");

    /// <summary>Durumun Türkçe açıklaması.</summary>
    public string StatusText => PrinterStatusText.Describe(Status, IsWorkOffline, UsbConnected, JobCount);

    public override string ToString() => $"{Name} ({DriverName}, {PortName})";

    private static bool ContainsAny(string? text, params string[] tokens) =>
        !string.IsNullOrEmpty(text) && tokens.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string? text) =>
        string.Concat((text ?? "").Where(char.IsLetterOrDigit)).ToUpperInvariant();
}

/// <summary>Bir yazıcının anlık durumu (asla istisna fırlatmadan sorgulanır).</summary>
public sealed class PrinterStatusSnapshot
{
    public required string PrinterName { get; init; }

    /// <summary>Yazıcı Windows'ta bulundu mu?</summary>
    public bool Exists => Printer is not null;

    public PrinterInfo? Printer { get; init; }

    /// <summary>Sorgu hatası (yazıcı bulunamadı, biriktirici çalışmıyor…), Türkçe.</summary>
    public string? ErrorMessage { get; init; }

    public int Win32Error { get; init; }

    public bool IsReady => Printer?.IsReady == true;

    public bool IsOffline => Printer?.IsOffline == true;

    public string StatusText => Printer?.StatusText ?? ErrorMessage ?? "Bilinmiyor";
}

/// <summary>Yüklü yazıcıları ve durumlarını bulur (yalnızca okuma yapar).</summary>
public static class PrinterDiscovery
{
    private const int Level2 = 2;

    /// <summary>
    /// Yerel ve bağlı (ağ) yazıcılar. Biriktirici hizmeti çalışmıyorsa Türkçe açıklamalı
    /// <see cref="PrintEngineException"/> fırlatır; diğer durumlarda istisna fırlatmaz.
    /// </summary>
    public static IReadOnlyList<PrinterInfo> GetPrinters()
    {
        int flags = NativeMethods.PRINTER_ENUM_LOCAL | NativeMethods.PRINTER_ENUM_CONNECTIONS;
        NativeMethods.EnumPrinters(flags, null, Level2, IntPtr.Zero, 0, out int needed, out _);
        int error = Marshal.GetLastPInvokeError();
        if (needed <= 0)
        {
            if (error is 0 or Win32ErrorText.ErrorInsufficientBuffer) return [];
            throw PrintEngineException.FromWin32("Yazıcılar listelenemedi", error);
        }

        IntPtr buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!NativeMethods.EnumPrinters(flags, null, Level2, buffer, needed, out _, out int returned))
                throw PrintEngineException.FromWin32("Yazıcılar listelenemedi", Marshal.GetLastPInvokeError());

            string? defaultPrinter = GetDefaultPrinterName();
            var present = UsbPrinterPorts.GetPresentPortNames();
            int size = Marshal.SizeOf<NativeMethods.PRINTER_INFO_2>();
            var list = new List<PrinterInfo>(returned);
            for (int i = 0; i < returned; i++)
            {
                var info = Marshal.PtrToStructure<NativeMethods.PRINTER_INFO_2>(buffer + i * size);
                var printer = FromInfo2(info, defaultPrinter, present);
                if (printer is not null) list.Add(printer);
            }
            return list.OrderByDescending(p => p.IsXp470B).ThenByDescending(p => p.IsLikelyTsplPrinter)
                .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>İstisna yerine hata mesajı döndüren sürüm.</summary>
    public static bool TryGetPrinters(out IReadOnlyList<PrinterInfo> printers, out string? errorMessage)
    {
        try
        {
            printers = GetPrinters();
            errorMessage = null;
            return true;
        }
        catch (PrintEngineException ex)
        {
            printers = [];
            errorMessage = ex.Message;
            return false;
        }
    }

    /// <summary>Ada göre yazıcı; bulunamazsa null. Biriktirici çalışmıyorsa <see cref="PrintEngineException"/>.</summary>
    public static PrinterInfo? GetPrinter(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName)) return null;
        if (!NativeMethods.TryOpen(printerName, out var handle, out int error))
        {
            if (IsNotFound(error)) return null;
            throw PrintEngineException.FromWin32("Yazıcı açılamadı", error, printerName);
        }
        using (handle)
        {
            NativeMethods.GetPrinter(handle, Level2, IntPtr.Zero, 0, out int needed);
            if (needed <= 0)
                throw PrintEngineException.FromWin32("Yazıcı bilgisi okunamadı", Marshal.GetLastPInvokeError(), printerName);
            IntPtr buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!NativeMethods.GetPrinter(handle, Level2, buffer, needed, out _))
                    throw PrintEngineException.FromWin32("Yazıcı bilgisi okunamadı", Marshal.GetLastPInvokeError(), printerName);
                var info = Marshal.PtrToStructure<NativeMethods.PRINTER_INFO_2>(buffer);
                return FromInfo2(info, GetDefaultPrinterName(), UsbPrinterPorts.GetPresentPortNames());
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    /// <summary>Yazıcının anlık durumu. Hiçbir durumda istisna fırlatmaz.</summary>
    public static PrinterStatusSnapshot GetStatus(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            return new PrinterStatusSnapshot { PrinterName = "", ErrorMessage = "Yazıcı seçilmedi. Ayarlar'dan etiket yazıcısını seçin." };
        try
        {
            var printer = GetPrinter(printerName);
            return printer is null
                ? new PrinterStatusSnapshot
                {
                    PrinterName = printerName,
                    Win32Error = Win32ErrorText.ErrorInvalidPrinterName,
                    ErrorMessage = Win32ErrorText.Explain(Win32ErrorText.ErrorInvalidPrinterName, printerName),
                }
                : new PrinterStatusSnapshot { PrinterName = printerName, Printer = printer };
        }
        catch (PrintEngineException ex)
        {
            return new PrinterStatusSnapshot { PrinterName = printerName, ErrorMessage = ex.Message, Win32Error = ex.Win32Error };
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            return new PrinterStatusSnapshot { PrinterName = printerName, ErrorMessage = "Yazıcı durumu okunamadı: " + ex.Message };
        }
    }

    /// <summary>Tercih edilen etiket yazıcısı: önce bağlı XP-470B, sonra herhangi bir XP-470B, sonra TSPL yazıcı.</summary>
    public static PrinterInfo? FindPreferredLabelPrinter()
    {
        if (!TryGetPrinters(out var printers, out _)) return null;
        return printers.FirstOrDefault(p => p.IsXp470B && !p.IsOffline)
               ?? printers.FirstOrDefault(p => p.IsXp470B)
               ?? printers.FirstOrDefault(p => p.IsLikelyTsplPrinter && !p.IsOffline)
               ?? printers.FirstOrDefault(p => p.IsLikelyTsplPrinter);
    }

    /// <summary>Windows varsayılan yazıcısının adı (yoksa null).</summary>
    public static unsafe string? GetDefaultPrinterName()
    {
        int length = 0;
        NativeMethods.GetDefaultPrinter(null, ref length);
        if (length <= 0) return null;
        var chars = new char[length];
        fixed (char* p = chars)
        {
            if (!NativeMethods.GetDefaultPrinter(p, ref length)) return null;
        }
        return new string(chars, 0, Math.Max(0, length - 1)).TrimEnd('\0');
    }

    internal static bool IsNotFound(int error) =>
        error is Win32ErrorText.ErrorInvalidPrinterName or Win32ErrorText.ErrorPrinterNotFound or Win32ErrorText.ErrorFileNotFound;

    private static PrinterInfo? FromInfo2(NativeMethods.PRINTER_INFO_2 info, string? defaultPrinter, IReadOnlySet<string>? presentUsbPorts)
    {
        string? name = NativeMethods.PtrToString(info.pPrinterName);
        if (string.IsNullOrEmpty(name)) return null;
        string port = NativeMethods.PtrToString(info.pPortName) ?? "";
        return new PrinterInfo
        {
            Name = name,
            DriverName = NativeMethods.PtrToString(info.pDriverName) ?? "",
            PortName = port,
            ShareName = NativeMethods.PtrToString(info.pShareName),
            PrintProcessor = NativeMethods.PtrToString(info.pPrintProcessor),
            DataType = NativeMethods.PtrToString(info.pDatatype),
            IsDefault = defaultPrinter is not null && string.Equals(defaultPrinter, name, StringComparison.OrdinalIgnoreCase),
            IsShared = (info.Attributes & NativeMethods.PRINTER_ATTRIBUTE_SHARED) != 0,
            IsNetwork = (info.Attributes & NativeMethods.PRINTER_ATTRIBUTE_NETWORK) != 0 || name.StartsWith(@"\\", StringComparison.Ordinal),
            IsWorkOffline = (info.Attributes & NativeMethods.PRINTER_ATTRIBUTE_WORK_OFFLINE) != 0,
            Status = (PrinterStatusFlags)info.Status,
            Attributes = info.Attributes,
            JobCount = (int)info.cJobs,
            UsbConnected = UsbPrinterPorts.IsConnected(port, presentUsbPorts),
        };
    }
}

/// <summary>Yazıcı durum bayraklarının Türkçe metni.</summary>
internal static class PrinterStatusText
{
    public static string Describe(PrinterStatusFlags status, bool workOffline, bool? usbConnected, int jobCount)
    {
        var parts = new List<string>();
        if (usbConnected == false) parts.Add("USB bağlantısı yok (kablo takılı ve yazıcı açık mı?)");
        if (workOffline) parts.Add("Windows'ta 'Yazıcıyı Çevrimdışı Kullan' seçili");
        if (status.HasFlag(PrinterStatusFlags.Offline)) parts.Add("Çevrimdışı");
        if (status.HasFlag(PrinterStatusFlags.ServerOffline)) parts.Add("Yazdırma sunucusu çevrimdışı");
        if (status.HasFlag(PrinterStatusFlags.ServerUnknown)) parts.Add("Yazdırma sunucusu bilinmiyor");
        if (status.HasFlag(PrinterStatusFlags.NotAvailable)) parts.Add("Kullanılamıyor");
        if (status.HasFlag(PrinterStatusFlags.Paused)) parts.Add("Duraklatıldı");
        if (status.HasFlag(PrinterStatusFlags.Error)) parts.Add("Hata");
        if (status.HasFlag(PrinterStatusFlags.PaperJam)) parts.Add("Kağıt sıkıştı");
        if (status.HasFlag(PrinterStatusFlags.PaperOut)) parts.Add("Etiket/kağıt bitti");
        if (status.HasFlag(PrinterStatusFlags.PaperProblem)) parts.Add("Kağıt sorunu");
        if (status.HasFlag(PrinterStatusFlags.DoorOpen)) parts.Add("Kapak açık");
        if (status.HasFlag(PrinterStatusFlags.UserIntervention)) parts.Add("Kullanıcı müdahalesi gerekiyor");
        if (status.HasFlag(PrinterStatusFlags.OutOfMemory)) parts.Add("Yazıcı belleği yetersiz");
        if (status.HasFlag(PrinterStatusFlags.PendingDeletion)) parts.Add("Siliniyor");
        if (status.HasFlag(PrinterStatusFlags.Initializing) || status.HasFlag(PrinterStatusFlags.WarmingUp)) parts.Add("Hazırlanıyor");
        if (status.HasFlag(PrinterStatusFlags.Printing)) parts.Add("Yazdırıyor");
        else if (status.HasFlag(PrinterStatusFlags.Busy) || status.HasFlag(PrinterStatusFlags.Processing)) parts.Add("Meşgul");
        if (status.HasFlag(PrinterStatusFlags.DriverUpdateNeeded)) parts.Add("Sürücü güncellemesi gerekiyor");

        string text = parts.Count == 0 ? "Hazır" : string.Join(", ", parts);
        if (jobCount > 0) text += $" ({jobCount} iş kuyrukta)";
        return text;
    }
}
