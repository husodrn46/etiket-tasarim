using System.Text.Json;
using AkelEtiket.Core.Serialization;

namespace AkelEtiket.ProjectManager;

/// <summary>Kurtarılabilir bir oturum kaydı.</summary>
public sealed class RecoveryEntry
{
    public required string RecoveryFile { get; init; }
    public string? OriginalPath { get; init; }
    public string DocumentName { get; init; } = "";
    public DateTime SavedAt { get; init; }
}

/// <summary>
/// Otomatik kurtarma. Her uygulama oturumu kendi kurtarma dosyasını yazar ve bir kilit dosyasını açık tutar.
/// Uygulama beklenmedik şekilde kapanırsa kilit serbest kalır; sonraki açılışta bu dosyalar kurtarma için önerilir.
/// Birden fazla açık pencere birbirinin kurtarma dosyasını ezmez.
/// </summary>
public sealed class AutoRecoveryService : IDisposable
{
    private const string LockExtension = ".lock";
    private const string InfoExtension = ".json";
    private readonly string _sessionId = Guid.NewGuid().ToString("N")[..10];
    private FileStream? _lock;

    public AutoRecoveryService(string folder)
    {
        Folder = Path.GetFullPath(folder);
    }

    public string Folder { get; }

    public static string DefaultFolder =>
        Path.Combine(AkelEtiket.Core.AppIdentity.LocalFolder, "Kurtarma");

    private string SessionBase => Path.Combine(Folder, "oturum-" + _sessionId);
    private string SessionProjectPath => SessionBase + ProjectFile.Extension;

    /// <summary>Oturumu başlatır (kilit dosyasını açar).</summary>
    public void Start()
    {
        Directory.CreateDirectory(Folder);
        _lock ??= new FileStream(SessionBase + LockExtension, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
    }

    /// <summary>Geçerli projenin kurtarma kopyasını yazar.</summary>
    public void Save(LabelProject project, byte[]? previewPng = null)
    {
        Start();
        string temp = SessionBase + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            ProjectFile.Write(project, stream, previewPng);
        File.Move(temp, SessionProjectPath, overwrite: true);
        var info = new RecoveryInfoFile
        {
            OriginalPath = project.FilePath,
            DocumentName = project.Document.Name,
            SavedAt = DateTime.Now,
        };
        File.WriteAllText(SessionBase + InfoExtension, JsonSerializer.Serialize(info, ModelJson.CompactOptions));
    }

    /// <summary>Bu oturumun kurtarma dosyasını siler (proje kaydedildiğinde/kapatıldığında).</summary>
    public void ClearSession()
    {
        TryDelete(SessionProjectPath);
        TryDelete(SessionBase + InfoExtension);
    }

    /// <summary>Kapanmamış (çökmüş) eski oturumlardan kalan kurtarma dosyaları.</summary>
    public IReadOnlyList<RecoveryEntry> FindOrphanedSessions()
    {
        if (!Directory.Exists(Folder)) return [];
        var result = new List<RecoveryEntry>();
        foreach (var project in Directory.EnumerateFiles(Folder, "oturum-*" + ProjectFile.Extension))
        {
            string baseName = project[..^ProjectFile.Extension.Length];
            if (string.Equals(baseName, SessionBase, StringComparison.OrdinalIgnoreCase)) continue;
            if (IsLocked(baseName + LockExtension)) continue; // oturum hâlâ açık (başka pencere)
            RecoveryInfoFile? info = null;
            try
            {
                if (File.Exists(baseName + InfoExtension))
                    info = JsonSerializer.Deserialize<RecoveryInfoFile>(File.ReadAllText(baseName + InfoExtension), ModelJson.CompactOptions);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
            }
            result.Add(new RecoveryEntry
            {
                RecoveryFile = project,
                OriginalPath = info?.OriginalPath,
                DocumentName = info?.DocumentName ?? "Adsız",
                SavedAt = info?.SavedAt ?? File.GetLastWriteTime(project),
            });
        }
        return result.OrderByDescending(r => r.SavedAt).ToList();
    }

    /// <summary>Kurtarma dosyasını açar. Proje kaydedilmemiş sayılır (FilePath = özgün yol).</summary>
    public ProjectLoadResult Recover(RecoveryEntry entry)
    {
        var result = ProjectFile.Load(entry.RecoveryFile);
        result.Project.FilePath = entry.OriginalPath;
        return result;
    }

    public void Discard(RecoveryEntry entry)
    {
        string baseName = entry.RecoveryFile[..^ProjectFile.Extension.Length];
        TryDelete(entry.RecoveryFile);
        TryDelete(baseName + InfoExtension);
        TryDelete(baseName + LockExtension);
    }

    public void Dispose()
    {
        ClearSession();
        _lock?.Dispose();
        _lock = null;
    }

    private static bool IsLocked(string lockFile)
    {
        if (!File.Exists(lockFile)) return false;
        try
        {
            using var stream = new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class RecoveryInfoFile
    {
        public string? OriginalPath { get; set; }
        public string DocumentName { get; set; } = "";
        public DateTime SavedAt { get; set; }
    }
}
