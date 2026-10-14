using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using AkelEtiket.Core.Automation;

namespace AkelEtiket.Design.Workspace;

/// <summary>Önerinin durumu.</summary>
public enum ProposalStatus
{
    /// <summary>Kullanıcının kararını bekliyor.</summary>
    Pending,

    /// <summary>Kabul edildi, uygulandı.</summary>
    Accepted,

    /// <summary>Reddedildi.</summary>
    Rejected,

    /// <summary>Uygulanamadı (hedef kapandı/silindi ya da dosya yazılamadı).</summary>
    Failed,
}

/// <summary>Yapay zekânın kullanıcı onayına sunduğu değişiklik.</summary>
public sealed class ProposalInfo
{
    public string Id { get; set; } = "";
    public string DraftId { get; set; } = "";
    public string TargetDesignId { get; set; } = "";
    public string TargetName { get; set; } = "";
    public string ClientName { get; set; } = "";
    /// <summary>Ajanın kendi açıklaması (kullanıcıya gösterilir; talimat değildir).</summary>
    public string Summary { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ProposalStatus Status { get; set; } = ProposalStatus.Pending;
    public string StatusMessage { get; set; } = "";
    public string BaseVersion { get; set; } = "";
    /// <summary>Taslak ile taslağın açıldığı hâl arasındaki değişiklik satırları.</summary>
    public List<string> Changes { get; set; } = [];
    public DateTimeOffset? DecidedAt { get; set; }
    public string? ResultVersion { get; set; }
    /// <summary>Uygulanırken bulunan çakışmalar.</summary>
    public List<string> Conflicts { get; set; } = [];
}

/// <summary>Önerileri saklar (%LOCALAPPDATA%\Etiket Tasarım\YapayZeka\Oneriler). Diskte kalır: uygulama kapalıyken
/// gelen öneriler bir sonraki açılışta görünür.</summary>
public sealed class ProposalStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ConcurrentDictionary<string, ProposalInfo> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _loadLock = new();
    private bool _loaded;

    public ProposalStore(string? folder = null)
    {
        Folder = folder ?? Path.Combine(AiSettingsStore.Folder, "Oneriler");
    }

    public string Folder { get; }

    public event EventHandler<ProposalInfo>? Changed;

    public IReadOnlyList<ProposalInfo> All
    {
        get
        {
            EnsureLoaded();
            return _items.Values.OrderByDescending(p => p.CreatedAt).ToList();
        }
    }

    public IReadOnlyList<ProposalInfo> Pending => All.Where(p => p.Status == ProposalStatus.Pending).ToList();

    public bool TryGet(string id, out ProposalInfo proposal)
    {
        EnsureLoaded();
        return _items.TryGetValue(id, out proposal!);
    }

    public void Save(ProposalInfo proposal)
    {
        EnsureLoaded();
        if (proposal.Id.Length == 0) proposal.Id = DesignId.NewShortId();
        _items[proposal.Id] = proposal;
        Directory.CreateDirectory(Folder);
        string path = Path.Combine(Folder, proposal.Id + ".json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(proposal, Options));
        File.Move(path + ".tmp", path, overwrite: true);
        Changed?.Invoke(this, proposal);
    }

    /// <summary>Diskten yeniden okur (başka süreç yazdıysa, ör. Worker).</summary>
    public void Reload()
    {
        lock (_loadLock)
        {
            _items.Clear();
            _loaded = false;
        }
        EnsureLoaded();
    }

    /// <summary>Sonuçlanmış eski önerileri siler.</summary>
    public void Cleanup(TimeSpan maxAge)
    {
        EnsureLoaded();
        var limit = DateTimeOffset.Now - maxAge;
        foreach (var p in _items.Values.ToList())
        {
            if (p.Status == ProposalStatus.Pending || p.CreatedAt > limit) continue;
            _items.TryRemove(p.Id, out _);
            try
            {
                File.Delete(Path.Combine(Folder, p.Id + ".json"));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_loadLock)
        {
            if (_loaded) return;
            if (Directory.Exists(Folder))
            {
                foreach (string path in Directory.EnumerateFiles(Folder, "*.json"))
                {
                    try
                    {
                        var p = JsonSerializer.Deserialize<ProposalInfo>(File.ReadAllText(path), Options);
                        if (p is not null && p.Id.Length > 0) _items[p.Id] = p;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                    {
                    }
                }
            }
            _loaded = true;
        }
    }
}
