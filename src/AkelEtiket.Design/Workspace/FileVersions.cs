using System.Security.Cryptography;
using AkelEtiket.Core;

namespace AkelEtiket.Design.Workspace;

/// <summary>Dosya sürüm etiketi: içeriğin SHA-256 özetinden (dosya değişince değişir).</summary>
public static class FileVersions
{
    public static string Compute(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] hash = SHA256.HashData(stream);
        return "f" + Convert.ToHexStringLower(hash)[..12];
    }

    public static string? TryCompute(string path)
    {
        try
        {
            return File.Exists(path) ? Compute(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// Bir tasarım dosyasının üzerine yazılmadan önce yedeğini alır
/// (%LOCALAPPDATA%\Etiket Tasarım\Yedekler\&lt;ad&gt;\yyyyMMdd-HHmmss.akelabel). Ad başına son 20 yedek tutulur.
/// </summary>
public sealed class BackupService
{
    public const int KeepPerDesign = 20;

    public BackupService(string? folder = null)
    {
        Folder = folder ?? Path.Combine(AppIdentity.LocalFolder, "Yedekler");
    }

    public string Folder { get; }

    /// <summary>Dosyanın yedeğini alır; yedek yolunu döndürür (dosya yoksa null).</summary>
    public string? Backup(string designPath)
    {
        if (!File.Exists(designPath)) return null;
        string name = Sanitize(Path.GetFileNameWithoutExtension(designPath));
        string dir = Path.Combine(Folder, name);
        Directory.CreateDirectory(dir);
        string target = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}{Path.GetExtension(designPath)}");
        File.Copy(designPath, target, overwrite: false);
        Prune(dir);
        return target;
    }

    /// <summary>Bir tasarım dosyasının yedekleri (en yenisi önce).</summary>
    public IReadOnlyList<FileInfo> ListFor(string designPath)
    {
        string dir = Path.Combine(Folder, Sanitize(Path.GetFileNameWithoutExtension(designPath)));
        if (!Directory.Exists(dir)) return [];
        return new DirectoryInfo(dir).GetFiles().OrderByDescending(f => f.Name, StringComparer.Ordinal).ToList();
    }

    private static void Prune(string dir)
    {
        var files = new DirectoryInfo(dir).GetFiles().OrderByDescending(f => f.Name, StringComparer.Ordinal).ToList();
        foreach (var old in files.Skip(KeepPerDesign))
        {
            try
            {
                old.Delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "tasarim" : cleaned;
    }
}
