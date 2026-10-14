using System.Collections.Concurrent;
using AkelEtiket.Core.Assets;
using AkelEtiket.Rendering;

namespace AkelEtiket.Design.Security;

/// <summary>
/// Satıra göre değişen görselleri yalnızca izin verilen klasörlerden çözer. Aranan yerler: veri dosyasının klasörü
/// ve "Görseller" alt klasörü (izinliyse), ardından bütün izinli klasörler. Her aday <see cref="PathGuard"/> ile
/// kanonikleştirilip denetlenir (bağlantıyla kaçış engellenir).
/// </summary>
public sealed class GuardedImageResolver : IImageSourceResolver
{
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".svg"];
    private static readonly ConcurrentDictionary<string, (DateTime Stamp, ImageAsset Asset)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly PathGuard _guard;
    private readonly List<string> _folders = [];

    public GuardedImageResolver(PathGuard guard, string? dataFilePath)
    {
        _guard = guard;
        if (dataFilePath is { Length: > 0 } && Path.GetDirectoryName(dataFilePath) is { } dataFolder)
        {
            _folders.Add(Path.Combine(dataFolder, "Görseller"));
            _folders.Add(dataFolder);
        }
        _folders.AddRange(guard.Roots.Select(r => r.Path));
    }

    public ImageAsset? Resolve(string value, out string? error)
    {
        error = null;
        string text = (value ?? "").Trim().Trim('"');
        if (text.Length == 0)
        {
            error = "Görsel alanı boş.";
            return null;
        }
        if (text.Contains("..", StringComparison.Ordinal))
        {
            error = $"\"{text}\" geçerli bir görsel adı değil.";
            return null;
        }
        foreach (string candidate in Candidates(text))
        {
            string full;
            try
            {
                full = _guard.ResolveExistingFile(candidate, GuardedFileKind.Image);
            }
            catch (ToolException)
            {
                continue;
            }
            try
            {
                var info = new FileInfo(full);
                if (Cache.TryGetValue(full, out var cached) && cached.Stamp == info.LastWriteTimeUtc) return cached.Asset;
                byte[] data = File.ReadAllBytes(full);
                var asset = new ImageAsset(AssetStore.ComputeId(data, info.Name), info.Name, data);
                Cache[full] = (info.LastWriteTimeUtc, asset);
                return asset;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = $"\"{text}\" görseli okunamadı: {ex.Message}";
                return null;
            }
        }
        error = $"\"{text}\" görseli izinli klasörlerde bulunamadı.";
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
        foreach (string folder in _folders)
        {
            string direct = Path.Combine(folder, relative);
            yield return direct;
            if (Path.HasExtension(relative)) continue;
            foreach (string ext in Extensions) yield return direct + ext;
        }
    }
}
