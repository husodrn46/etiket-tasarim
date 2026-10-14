using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace AkelEtiket.Core.Assets;

/// <summary>Projeye gömülü orijinal görsel dosyası. İçerik değişmez (immutable).</summary>
public sealed class ImageAsset
{
    public ImageAsset(string id, string fileName, byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(data);
        Id = id;
        FileName = string.IsNullOrWhiteSpace(fileName) ? id : fileName;
        Data = data;
    }

    /// <summary>İçerik özetinden türetilen kimlik + uzantı (ör. "3fa2…c1.png").</summary>
    public string Id { get; }

    /// <summary>Kullanıcının eklediği dosyanın özgün adı.</summary>
    public string FileName { get; }

    public byte[] Data { get; }

    public string Extension => Path.GetExtension(Id).ToLowerInvariant();

    public long Size => Data.LongLength;
}

/// <summary>
/// Bir projenin görsel varlıkları. Aynı içerik iki kez eklenirse tek kopya tutulur.
/// Kaydederken pakete gömülür; böylece şablon başka bilgisayarda eksik görsel olmadan açılır.
/// </summary>
public sealed class AssetStore
{
    private readonly Dictionary<string, ImageAsset> _assets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _sync = new();

    public event EventHandler? Changed;

    public IReadOnlyList<ImageAsset> Assets
    {
        get
        {
            lock (_sync) return _assets.Values.ToList();
        }
    }

    public int Count
    {
        get
        {
            lock (_sync) return _assets.Count;
        }
    }

    /// <summary>Dosya içeriğini ekler ve varlığı döndürür (aynı içerik varsa mevcut olanı).</summary>
    public ImageAsset Add(byte[] data, string fileName)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0) throw new ArgumentException("Boş dosya eklenemez.", nameof(data));
        string id = ComputeId(data, fileName);
        ImageAsset asset;
        lock (_sync)
        {
            if (_assets.TryGetValue(id, out var existing)) return existing;
            asset = new ImageAsset(id, Path.GetFileName(fileName ?? id), data);
            _assets[id] = asset;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return asset;
    }

    /// <summary>Kimliği belli bir varlığı ekler (paket açılırken, yapıştırırken).</summary>
    public void AddExisting(ImageAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        lock (_sync)
        {
            if (_assets.ContainsKey(asset.Id)) return;
            _assets[asset.Id] = asset;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool TryGet(string id, [NotNullWhen(true)] out ImageAsset? asset)
    {
        lock (_sync) return _assets.TryGetValue(id ?? "", out asset);
    }

    public bool Contains(string id)
    {
        lock (_sync) return _assets.ContainsKey(id ?? "");
    }

    /// <summary>Kullanılmayan varlıkları siler; silinen sayısını döndürür.</summary>
    public int RemoveUnused(IEnumerable<string> usedIds)
    {
        var used = new HashSet<string>(usedIds, StringComparer.OrdinalIgnoreCase);
        int removed;
        lock (_sync)
        {
            var toRemove = _assets.Keys.Where(k => !used.Contains(k)).ToList();
            foreach (var key in toRemove) _assets.Remove(key);
            removed = toRemove.Count;
        }
        if (removed > 0) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    public void Clear()
    {
        lock (_sync) _assets.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>İçerik SHA-256 özetinin ilk 20 hex karakteri + küçük harf uzantı.</summary>
    public static string ComputeId(byte[] data, string? fileName)
    {
        string hash = Convert.ToHexStringLower(SHA256.HashData(data))[..20];
        string ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        if (ext.Length is < 2 or > 6 || ext.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.')) ext = ".bin";
        return hash + ext;
    }
}
