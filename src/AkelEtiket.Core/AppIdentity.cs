namespace AkelEtiket.Core;

/// <summary>
/// Genel ürün kimliği ve veri klasörleri. Mevcut kurulumlardan bağımsızdır; otomatik veri devralmaz.
/// </summary>
public static class AppIdentity
{
    public const string ProductName = "LabelHarbor";

    /// <summary>Veri klasörlerinin adı (Belgeler, %APPDATA%, %LOCALAPPDATA% altında).</summary>
    public const string DataFolderName = "Etiket Tasarım";

    /// <summary>Eski ayar uyumluluğunda kullanılan ad; genel sürüm otomatik geçiş yapmaz.</summary>
    public const string LegacyDataFolderName = "AKEL Etiket";

    /// <summary>
    /// Tüm veri klasörlerini tek bir köke yönlendirir (testler ve taşınabilir kullanım için).
    /// Örn. ETIKET_VERI=C:\Gecici\deneme → ayarlar, kurtarma, şablonlar bu klasörün altında olur.
    /// </summary>
    public const string DataRootVariable = "ETIKET_VERI";

    private static string? Override
    {
        get
        {
            string? value = Environment.GetEnvironmentVariable(DataRootVariable);
            return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
        }
    }

    /// <summary>Gezici ayarlar (%APPDATA%\Etiket Tasarım).</summary>
    public static string RoamingFolder =>
        Override is { } root ? Path.Combine(root, "Ayarlar")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), DataFolderName);

    /// <summary>Yerel veriler (%LOCALAPPDATA%\Etiket Tasarım): kurtarma, günlük, yapay zekâ çalışma alanı.</summary>
    public static string LocalFolder =>
        Override is { } root ? Path.Combine(root, "Yerel")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolderName);

    /// <summary>Kullanıcı belgeleri (Belgeler\Etiket Tasarım): şablonlar.</summary>
    public static string DocumentsFolder =>
        Override is { } root ? Path.Combine(root, "Belgeler")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), DataFolderName);

    /// <summary>Şablon klasörü (Belgeler\Etiket Tasarım\Şablonlar).</summary>
    public static string TemplatesFolder => Path.Combine(DocumentsFolder, "Şablonlar");

    /// <summary>Hata günlükleri.</summary>
    public static string LogFolder => Path.Combine(LocalFolder, "Günlük");

    /// <summary>Genel sürüm başka kurulumların şablonlarını otomatik almaz.</summary>
    public static string? LegacyTemplatesFolder => null;

    /// <summary>Genel sürüm başka kurulumların ayarlarını otomatik almaz.</summary>
    public static string? LegacySettingsFile => null;
}
