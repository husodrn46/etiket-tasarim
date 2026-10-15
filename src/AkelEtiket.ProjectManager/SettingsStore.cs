using System.Text.Json;
using AkelEtiket.Core.Serialization;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.ProjectManager;

/// <summary>
/// JSON ayar dosyası (ör. %APPDATA%\AKEL Etiket\ayarlar.json). Atomik yazılır; dosya bozuksa
/// yedeğe ayrılır ve varsayılan ayarlarla devam edilir (uygulama asla açılışta çökmez).
/// </summary>
public sealed class JsonSettingsStore<T> where T : class, new()
{
    public JsonSettingsStore(string filePath)
    {
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    /// <summary>Son yüklemede sorun olduysa açıklaması.</summary>
    public string? LastLoadProblem { get; private set; }

    public static string DefaultFolder =>
        AkelEtiket.Core.AppIdentity.RoamingFolder;

    public T Load()
    {
        LastLoadProblem = null;
        if (!File.Exists(FilePath)) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(FilePath), ModelJson.FileOptions) ?? new T();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LastLoadProblem = L10n.T($"Ayar dosyası okunamadı, varsayılanlar kullanılıyor ({ex.Message}).");
            try
            {
                File.Copy(FilePath, FilePath + ".bozuk-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
            }
            catch (IOException)
            {
            }
            return new T();
        }
    }

    public void Save(T settings)
    {
        string? directory = Path.GetDirectoryName(FilePath);
        if (directory is not null) Directory.CreateDirectory(directory);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, ModelJson.FileOptions));
        File.Move(temp, FilePath, overwrite: true);
    }
}

/// <summary>Son kullanılan dosyalar listesi (en yeni başta, en fazla <see cref="MaxCount"/>).</summary>
public sealed class RecentFilesList
{
    public const int MaxCount = 10;

    public List<string> Items { get; set; } = [];

    public void Add(string path)
    {
        string full = Path.GetFullPath(path);
        Items.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        Items.Insert(0, full);
        if (Items.Count > MaxCount) Items.RemoveRange(MaxCount, Items.Count - MaxCount);
    }

    public void Remove(string path) =>
        Items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Artık var olmayan dosyaları listeden çıkarır.</summary>
    public int PruneMissing() => Items.RemoveAll(p => !File.Exists(p));
}
