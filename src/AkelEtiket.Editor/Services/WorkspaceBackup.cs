using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AkelEtiket.Core.Serialization;
using AkelEtiket.Design.Data;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Editor.Services;

public sealed class WorkspaceBackupManifest
{
    public string Format { get; set; } = "etiket-workspace";
    public int Version { get; set; } = 1;
    public Dictionary<string, string> Files { get; set; } = [];
}

public sealed record WorkspaceBackupContent(AppSettings Settings, ProductCatalogData Products,
    IReadOnlyDictionary<string, byte[]> Files)
{
    public int TemplateCount => Files.Keys.Count(n => n.StartsWith("templates/", StringComparison.Ordinal));
}

/// <summary>Yalnız firma/arayüz ayarları, gömülü görselli şablonlar ve ürün listesi. Anahtar, GS1 tahsisleri,
/// sayaçlar, baskı geçmişi ve harici veri/görsel dosyaları bu taşınabilir pakete girmez.</summary>
public static class WorkspaceBackup
{
    public const string FileFilter = "Firma ve şablon yedeği (*.etiketyedek)|*.etiketyedek";
    public const long MaxBytes = 128L * 1024 * 1024;
    public const int MaxFiles = 1000;
    private static byte[] Json<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, ModelJson.FileOptions);
    private static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public static void Export(string path, AppSettings settings, TemplateLibrary templates, ProductCatalogData products)
    {
        var portable = JsonSerializer.Deserialize<AppSettings>(Json(settings), ModelJson.FileOptions)!;
        portable.Normalize();
        portable.Recent.Items.Clear(); portable.TemplatesFolder = null;
        portable.LastDataFolder = null; portable.LastImageFolder = null; portable.LastProjectFolder = null;
        portable.LastQuickTemplatePath = null; portable.Window = new();
        portable.Printer.PrinterName = "";
        portable.Gs1CompanyPrefix = ""; portable.NextItemNumber = 1;
        portable.OnboardingCompleted = false;
        var productCopy = ProductCatalog.ValidateBackup(products);
        productCopy.SourceFile = "";
        var files = new Dictionary<string, byte[]> { ["settings.json"] = Json(portable), ["products.json"] = Json(productCopy) };
        foreach (var item in templates.List())
        {
            if (item.Problem is not null) throw new InvalidDataException("Okunamayan şablon yedeklenemedi: " + item.Name);
            string name = "templates/" + Path.GetFileName(item.FilePath);
            if (!AllowedName(name)) throw new InvalidDataException("Şablon dosyasının adını Windows ile uyumlu olacak şekilde değiştirin: " + item.Name);
            files.Add(name, File.ReadAllBytes(item.FilePath));
        }
        if (files.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            throw new InvalidDataException("Şablon dosya adları Windows'ta birbirinden farklı olmalı.");
        if (files.Count > MaxFiles || files.Values.Sum(b => (long)b.Length) > MaxBytes)
            throw new InvalidDataException("Yedek en fazla 1000 dosya ve 128 MB olabilir.");
        var manifest = new WorkspaceBackupManifest { Files = files.ToDictionary(p => p.Key, p => Hash(p.Value)) };
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (File.Exists(path)) throw new IOException("Bu adla bir dosya var. Yedeğe yeni bir dosya adı verin.");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (var (name, data) in files.Append(new("manifest.json", Json(manifest))))
                { using var entry = zip.CreateEntry(name).Open(); entry.Write(data); }
            }
            File.Move(temporary, path, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static WorkspaceBackupContent Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > MaxFiles + 1 || zip.Entries.Where(e => e.FullName != "manifest.json").Sum(e => e.Length) > MaxBytes ||
            zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count)
            throw new InvalidDataException("Yedek boyutu veya dosya listesi geçersiz.");
        var manifestEntry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Yedek manifesti eksik.");
        if (manifestEntry.Length > 1024 * 1024) throw new InvalidDataException("Yedek manifesti çok büyük.");
        var manifest = JsonSerializer.Deserialize<WorkspaceBackupManifest>(ReadEntry(manifestEntry, 1024 * 1024), ModelJson.FileOptions);
        if (manifest is not { Format: "etiket-workspace", Version: 1 } || manifest.Files is null ||
            manifest.Files.Count != zip.Entries.Count - 1 || !manifest.Files.ContainsKey("settings.json") || !manifest.Files.ContainsKey("products.json"))
            throw new InvalidDataException("Bu yedek biçimi desteklenmiyor veya dosya listesi eksik.");
        var files = new Dictionary<string, byte[]>();
        long totalRead = 0;
        foreach (var (name, expected) in manifest.Files)
        {
            if (!AllowedName(name)) throw new InvalidDataException("Yedekte geçersiz dosya yolu var.");
            var entry = zip.GetEntry(name) ?? throw new InvalidDataException("Yedekte dosya eksik: " + name);
            byte[] data = ReadEntry(entry, MaxBytes - totalRead);
            totalRead += data.Length;
            if (Hash(data) != expected) throw new InvalidDataException("Yedek dosyasının bütünlüğü doğrulanamadı: " + name);
            files.Add(name, data);
        }
        var settings = JsonSerializer.Deserialize<AppSettings>(files["settings.json"], ModelJson.FileOptions)
            ?? throw new InvalidDataException("Yedek ayarları boş.");
        settings.Normalize();
        var products = JsonSerializer.Deserialize<ProductCatalogData>(files["products.json"], ModelJson.FileOptions)
            ?? throw new InvalidDataException("Yedek ürün listesi boş.");
        products = ProductCatalog.ValidateBackup(products);
        // Şablon paketleri de kullanıcı ayarlarına dokunulmadan açılıp doğrulanır.
        foreach (var (name, data) in files.Where(p => p.Key.StartsWith("templates/", StringComparison.Ordinal)))
        { using var input = new MemoryStream(data); ProjectFile.Load(input); }
        return new WorkspaceBackupContent(settings, products, files);
    }

    private static bool AllowedName(string name)
    {
        if (name is "settings.json" or "products.json") return true;
        if (!name.StartsWith("templates/", StringComparison.Ordinal)) return false;
        string file = name[10..];
        string stem = file.Split('.')[0].TrimEnd(' ', '.').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '0' and <= '9') return false;
        return file.Length is > 0 and <= 140 && file.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase) &&
            !file.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)) && !file.StartsWith('.') && !file.EndsWith('.') && !file.EndsWith(' ');
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, long limit)
    {
        using var input = entry.Open(); using var memory = new MemoryStream();
        byte[] buffer = new byte[81920];
        int count;
        while ((count = input.Read(buffer)) > 0)
        {
            if (memory.Length + count > limit) throw new InvalidDataException("Yedek açıldığında boyut sınırını aşıyor.");
            memory.Write(buffer, 0, count);
        }
        if (memory.Length != entry.Length) throw new InvalidDataException("Yedek dosyasının boyutu uyuşmuyor.");
        return memory.ToArray();
    }

    /// <summary>Her zaman yeni bir klasöre çıkarır; var olan ayar veya dosyaların üzerine yazmaz.</summary>
    public static void ExtractToNewFolder(WorkspaceBackupContent content, string folder)
    {
        if (Directory.Exists(folder) || File.Exists(folder)) throw new IOException("Geri yükleme klasörü zaten var.");
        if (content.Files.Keys.Any(name => !AllowedName(name))) throw new InvalidDataException("Geçersiz yedek yolu.");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "templates"));
        foreach (var (name, data) in content.Files)
        {
            using var output = new FileStream(Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar)), FileMode.CreateNew, FileAccess.Write);
            output.Write(data);
        }
    }
}
