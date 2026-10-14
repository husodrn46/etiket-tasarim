using System.ComponentModel;

namespace AkelEtiket.PrintEngine;

/// <summary>Baskı motoru hatası. <see cref="Exception.Message"/> kullanıcıya gösterilebilir Türkçe metindir.</summary>
public sealed class PrintEngineException : Exception
{
    public PrintEngineException(string message, int win32Error = 0, Exception? innerException = null)
        : base(message, innerException)
    {
        Win32Error = win32Error;
    }

    /// <summary>Windows hata kodu (0 = yok).</summary>
    public int Win32Error { get; }

    /// <summary>"İşlem: açıklama (Windows hata kodu N)" biçiminde hata üretir.</summary>
    public static PrintEngineException FromWin32(string action, int error, string? printerName = null) =>
        new($"{action}: {Win32ErrorText.Explain(error, printerName)} (Windows hata kodu {error})", error);
}

/// <summary>Yazdırmayla ilgili Windows hata kodlarının Türkçe açıklamaları.</summary>
public static class Win32ErrorText
{
    public const int ErrorFileNotFound = 2;
    public const int ErrorAccessDenied = 5;
    public const int ErrorInvalidHandle = 6;
    public const int ErrorNotEnoughMemory = 8;
    public const int ErrorOutOfMemory = 14;
    public const int ErrorNotReady = 21;
    public const int ErrorGenFailure = 31;
    public const int ErrorInvalidParameter = 87;
    public const int ErrorDiskFull = 112;
    public const int ErrorInsufficientBuffer = 122;
    public const int ErrorDeviceNotConnected = 1167;
    public const int ErrorTimeout = 1460;
    public const int RpcServerUnavailable = 1722;
    public const int RpcCallFailed = 1726;
    public const int ErrorUnknownPrinterDriver = 1797;
    public const int ErrorUnknownPrintProcessor = 1798;
    public const int ErrorInvalidPrinterName = 1801;
    public const int ErrorInvalidPrinterCommand = 1803;
    public const int ErrorInvalidDatatype = 1804;
    public const int ErrorPrinterDeleted = 1905;
    public const int ErrorInvalidPrinterState = 1906;
    public const int ErrorSpoolFileNotFound = 3002;
    public const int ErrorPrinterNotFound = 3012;

    /// <summary>Kodun Türkçe, ne yapılacağını söyleyen açıklaması.</summary>
    public static string Explain(int error, string? printerName = null)
    {
        string printer = string.IsNullOrWhiteSpace(printerName) ? "Seçilen yazıcı" : $"'{printerName}' adlı yazıcı";
        return error switch
        {
            ErrorInvalidPrinterName or ErrorPrinterNotFound =>
                $"{printer} bulunamadı. Yazıcının Windows'ta kurulu olduğunu ve Ayarlar'da doğru yazıcının seçildiğini kontrol edin.",
            ErrorAccessDenied => "Erişim reddedildi; bu yazıcıya yazdırma izniniz yok.",
            ErrorInvalidHandle => "Yazıcı bağlantısı geçersiz (yazıcı kaldırılmış veya yeniden adlandırılmış olabilir).",
            ErrorFileNotFound => "Dosya veya yazıcı bulunamadı.",
            ErrorNotEnoughMemory or ErrorOutOfMemory => "Bellek yetersiz.",
            ErrorNotReady => "Yazıcı hazır değil (kapalı veya bağlantısı kesik olabilir).",
            ErrorGenFailure => "Yazıcıya bağlı aygıt yanıt vermiyor; USB kablosunu ve yazıcının açık olduğunu kontrol edin.",
            ErrorDeviceNotConnected => "Yazıcı bağlı değil; USB kablosunu ve yazıcının açık olduğunu kontrol edin.",
            ErrorInvalidParameter => "Geçersiz parametre.",
            ErrorDiskFull => "Diskte yer kalmadı; yazdırma kuyruğu dosyası yazılamadı.",
            ErrorTimeout => "İşlem zaman aşımına uğradı.",
            RpcServerUnavailable or RpcCallFailed =>
                "Windows Yazdırma Biriktiricisi (Print Spooler) hizmeti çalışmıyor. Hizmetler penceresinden 'Yazdırma Biriktiricisi' hizmetini başlatın.",
            ErrorUnknownPrinterDriver => "Yazıcı sürücüsü bulunamadı; üreticinin sürücüsünü yeniden kurun.",
            ErrorUnknownPrintProcessor => "Yazdırma işlemcisi bulunamadı; yazıcı sürücüsünü yeniden kurun.",
            ErrorInvalidPrinterCommand => "Yazıcı komutu geçersiz.",
            ErrorInvalidDatatype =>
                "Yazıcı sürücüsü RAW veri türünü kabul etmiyor (v4 tipi sürücü olabilir). Üreticinin v3 sürücüsünü kurun veya Ayarlar'dan 'Windows sürücüsü' baskı yöntemini seçin.",
            ErrorPrinterDeleted => "Yazıcı silinmiş.",
            ErrorInvalidPrinterState => "Yazıcı geçersiz durumda (duraklatılmış veya hata durumunda olabilir).",
            ErrorSpoolFileNotFound => "Yazdırma kuyruğu dosyası bulunamadı.",
            _ => SystemMessage(error),
        };
    }

    private static string SystemMessage(int error)
    {
        try
        {
            string text = new Win32Exception(error).Message;
            return string.IsNullOrWhiteSpace(text) ? "Bilinmeyen Windows hatası." : text.TrimEnd('.', ' ') + ".";
        }
        catch
        {
            return "Bilinmeyen Windows hatası.";
        }
    }
}
