using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.PrintEngine;

/// <summary>
/// Yazdırma kuyruğu ve sürücü erişimi şimdilik yalnızca Windows'ta vardır (winspool/GDI). Diğer sistemlerde yerel
/// çağrıya hiç gidilmez; çağıran taraf anlaşılır bir hata alır. TSPL üretimi ve etiket satırları her yerde çalışır.
/// </summary>
internal static class PrintPlatform
{
    public static bool IsSupported => OperatingSystem.IsWindows();

    public static string UnsupportedMessage => L10n.T("Bu işletim sisteminde yazdırma henüz desteklenmiyor.");

    public static void ThrowIfUnsupported()
    {
        if (!IsSupported) throw new PrintEngineException(UnsupportedMessage);
    }
}
