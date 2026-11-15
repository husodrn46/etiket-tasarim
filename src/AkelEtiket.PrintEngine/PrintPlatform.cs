using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.PrintEngine;

/// <summary>
/// Yazdırma kuyruğu Windows'ta biriktirici (winspool), macOS'ta CUPS üzerinden kullanılır. Sürücü üzerinden baskı
/// (GDI) yalnızca Windows'ta vardır. Desteklenmeyen bir sistemde yerel çağrıya hiç gidilmez; çağıran taraf
/// anlaşılır bir hata alır. TSPL üretimi ve etiket satırları her yerde çalışır.
/// </summary>
internal static class PrintPlatform
{
    /// <summary>Yazıcı listesi, ham (TSPL) gönderim ve kuyruk takibi var mı?</summary>
    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>Sürücü üzerinden baskı var mı?</summary>
    public static bool HasDriverPrinting => OperatingSystem.IsWindows();

    public static string UnsupportedMessage => L10n.T("Bu işletim sisteminde yazdırma henüz desteklenmiyor.");

    public static string NoDriverPrintingMessage =>
        L10n.T("macOS'ta sürücü üzerinden baskı yoktur. Baskı yöntemi olarak 'TSPL doğrudan (RAW)' seçin.");

    /// <summary>İletilerdeki yazdırma sistemi adı: macOS'ta kuyruk Windows'a ait değildir.</summary>
    public static string Adapt(string message) => OperatingSystem.IsMacOS() ? message.Replace("Windows", "macOS") : message;

    public static void ThrowIfUnsupported()
    {
        if (!IsSupported) throw new PrintEngineException(UnsupportedMessage);
    }
}
