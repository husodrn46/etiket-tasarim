using System.Collections.Concurrent;
using AkelEtiket.Core.Assets;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Rendering;

/// <summary>Veri satırındaki görsel değerini (dosya adı/yolu) bir görsel dosyasına çözer.</summary>
public interface IImageSourceResolver
{
    /// <summary>Görseli bulur; bulunamazsa null ve Türkçe açıklama döner.</summary>
    ImageAsset? Resolve(string value, out string? error);
}

/// <summary>
/// Görselleri verilen klasörlerde arar. Değer bir dosya adı ("AKL001.png"), uzantısız ad ("AKL001") veya
/// klasöre göreli yol olabilir. Tam yol yalnızca klasörlerden birinin içindeyse kabul edilir.
/// Okunan dosyalar yol + değişiklik zamanına göre önbelleğe alınır.
/// </summary>
public sealed class FolderImageResolver : IImageSourceResolver
{
    public const long MaxBytes = 30L * 1024 * 1024;
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".svg"];
    private readonly ConcurrentDictionary<string, (DateTime Stamp, ImageAsset Asset)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public FolderImageResolver(IEnumerable<string> folders)
    {
        Folders = folders.Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => Path.TrimEndingDirectorySeparator(Path.GetFullPath(f)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> Folders { get; }

    public ImageAsset? Resolve(string value, out string? error)
    {
        error = null;
        string text = (value ?? "").Trim().Trim('"');
        if (text.Length == 0)
        {
            error = L10n.T("Görsel alanı boş.");
            return null;
        }
        if (Folders.Count == 0)
        {
            error = L10n.T($"\"{text}\" görseli için görsel klasörü tanımlı değil.");
            return null;
        }
        if (text.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || text.Contains("..", StringComparison.Ordinal))
        {
            error = L10n.T($"\"{text}\" geçerli bir görsel adı değil.");
            return null;
        }

        foreach (string candidate in Candidates(text))
        {
            string full;
            try
            {
                full = Path.GetFullPath(candidate);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }
            if (!Folders.Any(f => IsUnder(full, f)) || !File.Exists(full)) continue;
            if (!Extensions.Contains(Path.GetExtension(full).ToLowerInvariant())) continue;
            try
            {
                var info = new FileInfo(full);
                if (info.Length > MaxBytes)
                {
                    error = L10n.T($"\"{text}\" görseli çok büyük.");
                    return null;
                }
                if (_cache.TryGetValue(full, out var cached) && cached.Stamp == info.LastWriteTimeUtc) return cached.Asset;
                byte[] data = File.ReadAllBytes(full);
                var asset = new ImageAsset(AssetStore.ComputeId(data, info.Name), info.Name, data);
                _cache[full] = (info.LastWriteTimeUtc, asset);
                return asset;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = L10n.T($"\"{text}\" görseli okunamadı: {ex.Message}");
                return null;
            }
        }
        error = L10n.T($"\"{text}\" görseli bulunamadı (aranan klasörler: {string.Join("; ", Folders)}).");
        return null;
    }

    private IEnumerable<string> Candidates(string text)
    {
        if (Path.IsPathFullyQualified(text))
        {
            yield return text;
            yield break;
        }
        string relative = text.Replace('/', Path.DirectorySeparatorChar);
        foreach (string folder in Folders)
        {
            string direct = Path.Combine(folder, relative);
            yield return direct;
            if (Path.HasExtension(relative)) continue;
            foreach (string ext in Extensions) yield return direct + ext;
        }
    }

    private static bool IsUnder(string path, string root) =>
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
