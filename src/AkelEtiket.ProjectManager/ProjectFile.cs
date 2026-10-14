using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;

namespace AkelEtiket.ProjectManager;

/// <summary>Paket içindeki varlık kaydı.</summary>
public sealed class ManifestAsset
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}

/// <summary>.akelabel paketinin özet bilgisi (şablon galerisinde dosyayı tamamen açmadan okunur).</summary>
public sealed class ProjectManifest
{
    public const string FormatName = "AKEL Etiket";

    public string Format { get; set; } = FormatName;
    public int FormatVersion { get; set; } = LabelDocument.CurrentFormatVersion;
    public string AppVersion { get; set; } = "";
    public string Name { get; set; } = "";
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public int ElementCount { get; set; }
    public bool HasFieldPlaceholders { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public string Document { get; set; } = ProjectFile.DocumentEntry;
    public List<ManifestAsset> Assets { get; set; } = [];
}

/// <summary>Proje yükleme sonucu (uyarılarla).</summary>
public sealed class ProjectLoadResult
{
    internal ProjectLoadResult(LabelProject project, IReadOnlyList<string> warnings)
    {
        Project = project;
        Warnings = warnings;
    }

    public LabelProject Project { get; }
    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>
/// .akelabel taşınabilir proje paketi (ZIP):
///   manifest.json  – biçim, sürüm, ölçüler, varlık listesi
///   label.json     – tasarım (öğeler mm cinsinden)
///   assets/&lt;id&gt;  – orijinal görsel dosyaları (dönüştürülmemiş)
///   preview.png    – küçük önizleme (isteğe bağlı)
/// Görseller pakete gömüldüğü için şablon başka bilgisayarda eksik görsel olmadan açılır.
/// Kayıt atomiktir: önce geçici dosyaya yazılır, sonra eskisinin yerine konur.
/// </summary>
public static class ProjectFile
{
    public const string Extension = ".akelabel";
    public const string FileFilter = "Etiket projesi (*.akelabel)|*.akelabel";
    public const string ManifestEntry = "manifest.json";
    public const string DocumentEntry = "label.json";
    public const string PreviewEntry = "preview.png";
    public const string AssetFolder = "assets/";

    /// <summary>Uygulama sürümü (manifest'e yazılır).</summary>
    public static string AppVersion { get; set; } = "1.0.0";

    public static void Save(LabelProject project, string path, byte[]? previewPng = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath) ?? throw new ProjectFileException("Geçersiz kayıt yolu.");
        string tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                Write(project, stream, previewPng);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(fullPath)) File.Replace(tempPath, fullPath, null, ignoreMetadataErrors: true);
            else File.Move(tempPath, fullPath);
            project.FilePath = fullPath;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ProjectFileException($"Dosyaya yazma izni yok: {fullPath}", ex);
        }
        catch (IOException ex)
        {
            throw new ProjectFileException($"Proje kaydedilemedi ({ex.Message}). Dosya başka bir programda açık olabilir veya disk dolu olabilir.", ex);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>Paketi bir akışa yazar (pano, otomatik kurtarma, testler için de kullanılır).</summary>
    public static void Write(LabelProject project, Stream stream, byte[]? previewPng = null)
    {
        var doc = project.Document;
        doc.ModifiedAt = DateTimeOffset.Now;
        // v2'ye özgü özellik yoksa biçim 1 yazılır: dosya v1'de de uyarısız açılır.
        doc.FormatVersion = doc.GetRequiredFormatVersion();
        var used = doc.GetReferencedAssetIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assets = project.Assets.Assets.Where(a => used.Contains(a.Id)).ToList();

        var manifest = new ProjectManifest
        {
            FormatVersion = doc.FormatVersion,
            AppVersion = AppVersion,
            Name = doc.Name,
            WidthMm = doc.Label.WidthMm,
            HeightMm = doc.Label.HeightMm,
            ElementCount = doc.Elements.Count,
            HasFieldPlaceholders = doc.HasFieldPlaceholders,
            CreatedAt = doc.CreatedAt,
            ModifiedAt = doc.ModifiedAt,
            Assets = assets.Select(a => new ManifestAsset
            {
                Id = a.Id,
                FileName = a.FileName,
                Size = a.Size,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(a.Data)),
            }).ToList(),
        };

        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);
        WriteText(zip, ManifestEntry, JsonSerializer.Serialize(manifest, ModelJson.FileOptions));
        WriteText(zip, DocumentEntry, ModelJson.Serialize(doc));
        foreach (var asset in assets)
        {
            // Sıkıştırılmış görseller (PNG/JPG) tekrar sıkıştırılmaz.
            var level = asset.Extension is ".bmp" or ".svg" ? CompressionLevel.Optimal : CompressionLevel.NoCompression;
            var entry = zip.CreateEntry(AssetFolder + asset.Id, level);
            using var entryStream = entry.Open();
            entryStream.Write(asset.Data);
        }
        if (previewPng is { Length: > 0 })
        {
            var entry = zip.CreateEntry(PreviewEntry, CompressionLevel.NoCompression);
            using var entryStream = entry.Open();
            entryStream.Write(previewPng);
        }
    }

    public static ProjectLoadResult Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new ProjectFileException($"Dosya bulunamadı: {fullPath}");
        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var result = Load(stream);
            result.Project.FilePath = fullPath;
            return result;
        }
        catch (ProjectFileException)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ProjectFileException($"Dosyayı okuma izni yok: {fullPath}", ex);
        }
        catch (IOException ex)
        {
            throw new ProjectFileException($"Dosya okunamadı: {ex.Message}", ex);
        }
    }

    public static ProjectLoadResult Load(Stream stream)
    {
        var warnings = new List<string>();
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true, Encoding.UTF8);
            var manifest = ReadManifest(zip);
            if (manifest.FormatVersion > LabelDocument.CurrentFormatVersion)
                warnings.Add($"Bu dosya daha yeni bir Etiket Tasarım sürümüyle kaydedilmiş (biçim {manifest.FormatVersion}). " +
                             "Bazı özellikler eksik görünebilir.");

            var docEntry = zip.GetEntry(string.IsNullOrWhiteSpace(manifest.Document) ? DocumentEntry : manifest.Document)
                           ?? zip.GetEntry(DocumentEntry)
                           ?? throw new ProjectFileException("Proje dosyasında tasarım verisi (label.json) yok; dosya bozuk.");
            LabelDocument document;
            using (var reader = new StreamReader(docEntry.Open(), Encoding.UTF8))
                document = ModelJson.DeserializeDocument(reader.ReadToEnd());

            var store = new AssetStore();
            var manifestAssets = manifest.Assets.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(AssetFolder, StringComparison.OrdinalIgnoreCase) && e.Name.Length > 0))
            {
                string id = entry.Name;
                byte[] data = ReadAll(entry);
                if (manifestAssets.TryGetValue(id, out var info) && info.Sha256.Length > 0 &&
                    !string.Equals(info.Sha256, Convert.ToHexStringLower(SHA256.HashData(data)), StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"Görsel bozuk olabilir (özet uyuşmuyor): {info.FileName}");
                }
                string fileName = info?.FileName ?? id;
                store.AddExisting(new ImageAsset(id, fileName, data));
            }

            var project = new LabelProject(document, store);
            foreach (var missing in project.GetMissingAssetIds())
            {
                string name = manifestAssets.TryGetValue(missing, out var info) ? info.FileName : missing;
                warnings.Add($"Görsel dosyası pakette bulunamadı: {name}");
            }
            return new ProjectLoadResult(project, warnings);
        }
        catch (InvalidDataException ex)
        {
            throw new ProjectFileException("Dosya geçerli bir Etiket projesi değil veya bozuk (ZIP okunamadı).", ex);
        }
        catch (JsonException ex)
        {
            throw new ProjectFileException($"Proje verisi okunamadı (JSON hatası: {ex.Message}).", ex);
        }
    }

    /// <summary>Paketin özet bilgisini okur (galeri için hızlı).</summary>
    public static ProjectManifest ReadManifest(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            return ReadManifest(zip);
        }
        catch (InvalidDataException ex)
        {
            throw new ProjectFileException("Dosya geçerli bir Etiket projesi değil.", ex);
        }
    }

    /// <summary>Paketteki önizleme resmini okur; yoksa null.</summary>
    public static byte[]? ReadPreview(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = zip.GetEntry(PreviewEntry);
            return entry is null ? null : ReadAll(entry);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static ProjectManifest ReadManifest(ZipArchive zip)
    {
        var entry = zip.GetEntry(ManifestEntry)
                    ?? throw new ProjectFileException("Bu dosya bir Etiket projesi değil (manifest.json yok).");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var manifest = JsonSerializer.Deserialize<ProjectManifest>(reader.ReadToEnd(), ModelJson.FileOptions)
                       ?? throw new ProjectFileException("Proje özet bilgisi boş.");
        if (!string.Equals(manifest.Format, ProjectManifest.FormatName, StringComparison.Ordinal))
            throw new ProjectFileException("Bu dosya bir Etiket projesi değil.");
        manifest.Assets ??= [];
        return manifest;
    }

    private static void WriteText(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var source = entry.Open();
        using var buffer = new MemoryStream(entry.Length > 0 && entry.Length < int.MaxValue ? (int)entry.Length : 0);
        source.CopyTo(buffer);
        return buffer.ToArray();
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
}
