using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AkelEtiket.Core;
using AkelEtiket.Core.Automation;

namespace AkelEtiket.Editor.Services;

/// <summary>
/// Destek paketi: sorun bildirmek için hata günlüklerini, Worker/yapay zekâ işlem günlüklerini, ayarları (gizli
/// bilgiler çıkarılmış) ve sistem bilgisini tek bir ZIP dosyasında toplar. Dosya yalnızca bu bilgisayara kaydedilir;
/// hiçbir yere gönderilmez.
/// </summary>
public static class SupportBundle
{
    public static string DefaultFolder => Path.Combine(AppIdentity.DocumentsFolder, "Destek");

    /// <summary>Paketi oluşturur ve yolunu döndürür. <paramref name="currentDocument"/> verilirse pakete eklenir.</summary>
    public static string Create(string? currentDocument, IEnumerable<string> printers, string? targetFolder = null)
    {
        string folder = targetFolder ?? DefaultFolder;
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"destek-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        var info = new StringBuilder();
        info.AppendLine($"Ürün: {AppIdentity.ProductName} {Assembly.GetEntryAssembly()?.GetName().Version}");
        info.AppendLine($"Tarih: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        info.AppendLine($"İşletim sistemi: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        info.AppendLine($".NET: {RuntimeInformation.FrameworkDescription}");
        info.AppendLine($"Veri klasörleri: {AppIdentity.LocalFolder} | {AppIdentity.DocumentsFolder}");
        info.AppendLine("Yazıcılar:");
        foreach (string printer in printers) info.AppendLine("  - " + printer);
        AddText(zip, "bilgi.txt", info.ToString());

        AddFiles(zip, AppIdentity.LogFolder, "*.log", "gunluk", TimeSpan.FromDays(14));
        AddFiles(zip, Path.Combine(AiSettingsStore.Folder, "Gunluk"), "*.*", "yapay-zeka-gunluk", TimeSpan.FromDays(7));
        string appSettings = Path.Combine(AppIdentity.RoamingFolder, "ayarlar.json");
        if (File.Exists(appSettings)) TryAdd(zip, appSettings, "ayarlar/ayarlar.json");
        if (File.Exists(AiSettingsStore.FilePath))
        {
            // Bağlantı anahtarı ve şifreli API anahtarı pakete girmez.
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(AiSettingsStore.FilePath)) as JsonObject;
                if (node is not null)
                {
                    node["token"] = "(gizlendi)";
                    if (node["assistant"] is JsonObject assistant) assistant["protectedApiKey"] = "(gizlendi)";
                    AddText(zip, "ayarlar/yapay-zeka.json", node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                AddText(zip, "ayarlar/yapay-zeka.txt", "Okunamadı: " + ex.Message);
            }
        }
        if (currentDocument is not null && File.Exists(currentDocument))
            TryAdd(zip, currentDocument, "tasarim/" + Path.GetFileName(currentDocument));
        return path;
    }

    private static void AddFiles(ZipArchive zip, string folder, string pattern, string prefix, TimeSpan maxAge)
    {
        if (!Directory.Exists(folder)) return;
        var limit = DateTime.Now - maxAge;
        foreach (var file in new DirectoryInfo(folder).GetFiles(pattern).Where(f => f.LastWriteTime >= limit))
            TryAdd(zip, file.FullName, prefix + "/" + file.Name);
    }

    private static void TryAdd(ZipArchive zip, string file, string entryName)
    {
        try
        {
            using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var target = entry.Open();
            source.CopyTo(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddText(zip, entryName + ".hata.txt", "Okunamadı: " + ex.Message);
        }
    }

    private static void AddText(ZipArchive zip, string entryName, string text)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(true));
        writer.Write(text);
    }
}
