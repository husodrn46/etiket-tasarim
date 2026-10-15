using System.Text.Json;
using AkelEtiket.Core.Serialization;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Editor.Services;

/// <summary>
/// v1'den geçiş: v2'nin ayar dosyası henüz yoksa (ilk açılış) v1'in ayarlarını (%APPDATA%\AKEL Etiket\ayarlar.json)
/// devralır: yazıcı ayarları, son kullanılan dosyalar ve klasörler, tema, ızgara/yakalama tercihleri, EAN öneki.
/// v1'in dosyasına yazılmaz (bozuk olsa bile). Şablon klasörü v1'in varsayılanıysa taşınmaz (v2 kendi klasörünü
/// kullanır; v1 şablonları ilk açılışta kopyalanır) ve ilk açılış akışı yine çalışır.
/// </summary>
public static class LegacySettingsImport
{
    /// <summary>Ayarlar devralındıysa true.</summary>
    /// <param name="target">v2'nin ayar deposu (dosyası varsa hiçbir şey yapılmaz).</param>
    /// <param name="legacyFile">v1'in ayar dosyası; null ya da yoksa hiçbir şey yapılmaz.</param>
    /// <param name="legacyTemplatesFolder">v1'in varsayılan şablon klasörü (bu klasör v2'ye taşınmaz).</param>
    public static bool TryImport(JsonSettingsStore<AppSettings> target, string? legacyFile, string? legacyTemplatesFolder)
    {
        if (string.IsNullOrWhiteSpace(legacyFile) || File.Exists(target.FilePath) || !File.Exists(legacyFile)) return false;
        AppSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(legacyFile), ModelJson.FileOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return false;
        }
        if (settings is null) return false;

        settings.Normalize();
        if (string.IsNullOrWhiteSpace(settings.TemplatesFolder) || SameFolder(settings.TemplatesFolder, legacyTemplatesFolder))
            settings.TemplatesFolder = null;
        settings.FirstRunCompleted = false;
        settings.UiLanguage = "tr"; // Preserve the language of the v1 installation.
        try
        {
            target.Save(settings);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool SameFolder(string a, string? b)
    {
        if (string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
