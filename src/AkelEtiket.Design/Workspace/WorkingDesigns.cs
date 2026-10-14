using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using AkelEtiket.Core.Automation;
using AkelEtiket.Core.Serialization;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Design.Workspace;

/// <summary>Taslağın durumu.</summary>
public enum DraftState
{
    /// <summary>Düzenleniyor.</summary>
    Open,

    /// <summary>Öneri olarak gönderildi, kullanıcı kararını bekliyor (artık düzenlenemez).</summary>
    Submitted,

    /// <summary>Kabul edildi ve hedefe uygulandı.</summary>
    Applied,

    /// <summary>Kullanıcı reddetti.</summary>
    Rejected,

    /// <summary>Ajan vazgeçti.</summary>
    Discarded,
}

/// <summary>Taslak/yeni tasarımın kalıcı bilgileri (yanında .akelabel içerik dosyası durur).</summary>
public sealed class WorkingDesignMeta
{
    public string Id { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DesignKind Kind { get; set; }
    /// <summary>Oluşturan istemci (yalnızca o değiştirebilir).</summary>
    public string Owner { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.Now;
    public long Revision { get; set; }

    // Taslak
    public string? TargetDesignId { get; set; }
    public string? TargetName { get; set; }
    public string? BaseVersion { get; set; }
    /// <summary>Taslak açıldığı andaki hedef belge (üç yönlü birleştirme için).</summary>
    public string? BaseDocumentJson { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DraftState State { get; set; } = DraftState.Open;
    public string? ProposalId { get; set; }

    // Yeni tasarım
    public string? SavedPath { get; set; }
    public string? SavedFileVersion { get; set; }

    // Önizleme verisi
    public string? PreviewProductKey { get; set; }
    public int? PreviewRow { get; set; }
}

/// <summary>Bellekte tutulan taslak ya da yeni tasarım.</summary>
public sealed class WorkingDesign
{
    internal WorkingDesign(WorkingDesignMeta meta, LabelProject project)
    {
        Meta = meta;
        Project = project;
    }

    public WorkingDesignMeta Meta { get; }
    public LabelProject Project { get; internal set; }
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    public string DesignId => new DesignId(Meta.Kind, Meta.Id).ToString();
    public string Version => $"r{Meta.Revision}";
    public bool IsDraft => Meta.Kind == DesignKind.Draft;

    public LabelDocument? GetBaseDocument() =>
        Meta.BaseDocumentJson is { Length: > 0 } json ? ModelJson.DeserializeDocument(json) : null;
}

/// <summary>
/// Taslakları ve yapay zekânın yeni tasarımlarını saklar (%LOCALAPPDATA%\Etiket Tasarım\YapayZeka\Calisma).
/// Diske de yazıldıkları için Worker ↔ masaüstü devrinde ve yeniden başlatmada kaybolmazlar.
/// </summary>
public sealed class WorkingDesignStore
{
    private static readonly JsonSerializerOptions MetaOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ConcurrentDictionary<string, WorkingDesign> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _loadLock = new();
    private bool _loaded;

    public WorkingDesignStore(string? folder = null)
    {
        Folder = folder ?? Path.Combine(AiSettingsStore.Folder, "Calisma");
    }

    public string Folder { get; }

    public IReadOnlyList<WorkingDesign> All
    {
        get
        {
            EnsureLoaded();
            return _items.Values.OrderBy(w => w.Meta.CreatedAt).ToList();
        }
    }

    public WorkingDesign Create(DesignKind kind, LabelProject project, string owner, Action<WorkingDesignMeta>? init = null)
    {
        if (kind is not (DesignKind.Draft or DesignKind.New)) throw new ArgumentOutOfRangeException(nameof(kind));
        EnsureLoaded();
        var meta = new WorkingDesignMeta { Id = Workspace.DesignId.NewShortId(), Kind = kind, Owner = owner };
        init?.Invoke(meta);
        project.FilePath = null;
        var item = new WorkingDesign(meta, project);
        _items[meta.Id] = item;
        Persist(item);
        return item;
    }

    public bool TryGet(string id, out WorkingDesign item)
    {
        EnsureLoaded();
        return _items.TryGetValue(id, out item!);
    }

    /// <summary>İçeriği ve bilgileri diske yazar (atomik).</summary>
    public void Persist(WorkingDesign item)
    {
        Directory.CreateDirectory(Folder);
        item.Meta.ModifiedAt = DateTimeOffset.Now;
        string contentPath = ContentPath(item.Meta.Id);
        string temp = contentPath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            ProjectFile.Write(item.Project, stream);
        }
        File.Move(temp, contentPath, overwrite: true);
        string metaPath = MetaPath(item.Meta.Id);
        File.WriteAllText(metaPath + ".tmp", JsonSerializer.Serialize(item.Meta, MetaOptions));
        File.Move(metaPath + ".tmp", metaPath, overwrite: true);
    }

    public void Delete(string id)
    {
        _items.TryRemove(id, out _);
        TryDelete(ContentPath(id));
        TryDelete(MetaPath(id));
    }

    /// <summary>Eski, sonuçlanmış çalışma kopyalarını siler (onay bekleyen taslaklar korunur).</summary>
    public int Cleanup(TimeSpan maxAge)
    {
        EnsureLoaded();
        int removed = 0;
        var limit = DateTimeOffset.Now - maxAge;
        foreach (var item in _items.Values.ToList())
        {
            if (item.Meta.ModifiedAt > limit) continue;
            if (item.IsDraft && item.Meta.State == DraftState.Submitted) continue;
            Delete(item.Meta.Id);
            removed++;
        }
        return removed;
    }

    /// <summary>Diskteki kopyaları yeniden okur (başka süreç yazdıysa).</summary>
    public void Reload()
    {
        lock (_loadLock)
        {
            _items.Clear();
            _loaded = false;
        }
        EnsureLoaded();
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_loadLock)
        {
            if (_loaded) return;
            if (Directory.Exists(Folder))
            {
                foreach (string metaPath in Directory.EnumerateFiles(Folder, "*.json"))
                {
                    try
                    {
                        var meta = JsonSerializer.Deserialize<WorkingDesignMeta>(File.ReadAllText(metaPath), MetaOptions);
                        if (meta is null || meta.Id.Length == 0) continue;
                        string contentPath = ContentPath(meta.Id);
                        if (!File.Exists(contentPath)) continue;
                        var loaded = ProjectFile.Load(contentPath);
                        loaded.Project.FilePath = null;
                        _items[meta.Id] = new WorkingDesign(meta, loaded.Project);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ProjectFileException)
                    {
                        // Bozuk çalışma kopyası atlanır.
                    }
                }
            }
            _loaded = true;
        }
    }

    private string ContentPath(string id) => Path.Combine(Folder, id + ProjectFile.Extension);

    private string MetaPath(string id) => Path.Combine(Folder, id + ".json");

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
}
