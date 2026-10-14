using AkelEtiket.Core;
using AkelEtiket.Core.Automation;

namespace AkelEtiket.Design.Security;

/// <summary>İzin verilen bir klasör.</summary>
/// <param name="Alias">Kimliklerde kullanılan kısa ad (ör. "Şablonlar").</param>
/// <param name="Path">Tam, kanonik yol.</param>
/// <param name="Writable">Tasarım dosyası yazılabilir mi (yalnızca Şablonlar).</param>
public sealed record AllowedRoot(string Alias, string Path, bool Writable);

/// <summary>Dosya türü (izin ve boyut sınırı için).</summary>
public enum GuardedFileKind
{
    Design,
    Image,
    Data,
}

/// <summary>
/// Yapay zekâ araçlarının dosya erişimini izin verilen klasörlerle sınırlar. Yol kanonikleştirilir; sembolik
/// bağlantı/kavşak (junction) ile klasör dışına kaçış, aygıt yolları ve alternatif veri akışları reddedilir.
/// </summary>
public sealed class PathGuard
{
    public const string TemplatesAlias = "Şablonlar";
    public const string SamplesAlias = "Örnekler";

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".svg"];
    private static readonly string[] DataExtensions = [".xlsx", ".xlsm", ".xls", ".csv", ".txt"];
    private static readonly string[] DesignExtensions = [".akelabel"];

    public const long MaxImageBytes = 30L * 1024 * 1024;
    public const long MaxDataBytes = 50L * 1024 * 1024;
    public const long MaxDesignBytes = 100L * 1024 * 1024;

    public PathGuard(IReadOnlyList<AllowedRoot> roots)
    {
        Roots = roots;
    }

    public IReadOnlyList<AllowedRoot> Roots { get; }

    public AllowedRoot TemplatesRoot => Roots.First(r => r.Writable);

    /// <summary>Varsayılan kökler: Şablonlar (yazılabilir), uygulamanın Örnekler klasörü ve ayarlardaki ek klasörler.</summary>
    public static PathGuard FromSettings(AiSettings settings, string? templatesFolder = null, string? samplesFolder = null)
    {
        var roots = new List<AllowedRoot>();
        string templates = templatesFolder ?? AppIdentity.TemplatesFolder;
        Directory.CreateDirectory(templates);
        roots.Add(new AllowedRoot(TemplatesAlias, Canonicalize(templates), Writable: true));

        string samples = samplesFolder ?? System.IO.Path.Combine(AppContext.BaseDirectory, "Örnekler");
        if (Directory.Exists(samples)) roots.Add(new AllowedRoot(SamplesAlias, Canonicalize(samples), Writable: false));

        foreach (string folder in settings.AllowedFolders)
        {
            try
            {
                if (!Directory.Exists(folder)) continue;
                string canonical = Canonicalize(folder);
                if (roots.Any(r => SamePath(r.Path, canonical))) continue;
                string alias = UniqueAlias(new DirectoryInfo(canonical).Name, roots);
                roots.Add(new AllowedRoot(alias, canonical, Writable: false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or ToolException)
            {
                // Erişilemeyen klasör atlanır.
            }
        }
        return new PathGuard(roots);
    }

    /// <summary>
    /// Kullanıcının/ajanın verdiği yolu çözer: tam yol ya da "Takma ad/göreli/yol". İzin verilen bir kökün içinde,
    /// var olan ve türü uygun bir dosya olmalıdır.
    /// </summary>
    public string ResolveExistingFile(string input, GuardedFileKind kind)
    {
        string full = Resolve(input, out var root);
        if (!File.Exists(full))
            throw new ToolException(ToolErrorCodes.NotFound, $"Dosya bulunamadı: {Display(full, root)}");
        CheckExtension(full, kind);
        long size = new FileInfo(full).Length;
        long limit = kind switch { GuardedFileKind.Image => MaxImageBytes, GuardedFileKind.Data => MaxDataBytes, _ => MaxDesignBytes };
        if (size > limit)
            throw new ToolException(ToolErrorCodes.InvalidArgument, $"Dosya çok büyük ({size / 1024 / 1024} MB). Sınır {limit / 1024 / 1024} MB.");
        return full;
    }

    /// <summary>Yolu izin verilen bir köke göre çözer ve kanonikleştirir (dosyanın var olması gerekmez).</summary>
    public string Resolve(string input, out AllowedRoot root)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ToolException(ToolErrorCodes.InvalidArgument, "Dosya yolu boş.");
        string text = input.Trim().Trim('"');
        if (text.StartsWith(DesignId.FilePrefix, StringComparison.OrdinalIgnoreCase)) text = text[DesignId.FilePrefix.Length..];

        string candidate;
        if (System.IO.Path.IsPathFullyQualified(text))
        {
            candidate = text;
        }
        else
        {
            string normalized = text.Replace('\\', '/');
            int slash = normalized.IndexOf('/');
            string alias = slash < 0 ? normalized : normalized[..slash];
            var aliasRoot = Roots.FirstOrDefault(r => string.Equals(r.Alias, alias, StringComparison.OrdinalIgnoreCase));
            if (aliasRoot is null)
            {
                throw new ToolException(ToolErrorCodes.PermissionDenied,
                    $"\"{input}\" izin verilen bir klasörde değil. İzinli klasörler: {string.Join(", ", Roots.Select(r => r.Alias))}. " +
                    "Kullanıcı Etiket Tasarım → Ayarlar → Yapay Zekâ bölümünden klasör ekleyebilir.");
            }
            string relative = slash < 0 ? "" : normalized[(slash + 1)..];
            candidate = System.IO.Path.Combine(aliasRoot.Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        }

        string canonical = Canonicalize(candidate);
        root = Roots.FirstOrDefault(r => IsUnder(canonical, r.Path))
               ?? throw new ToolException(ToolErrorCodes.PermissionDenied,
                   $"\"{input}\" izin verilen klasörlerin dışında. İzinli klasörler: {string.Join(", ", Roots.Select(r => $"{r.Alias} ({r.Path})"))}.");
        return canonical;
    }

    /// <summary>Tam yolu "Takma ad/göreli/yol" biçimine çevirir (kimlik ve çıktılar için).</summary>
    public string ToRelativeId(string fullPath)
    {
        string canonical = Canonicalize(fullPath);
        var root = Roots.FirstOrDefault(r => IsUnder(canonical, r.Path))
                   ?? throw new ToolException(ToolErrorCodes.PermissionDenied, "Dosya izin verilen klasörlerin dışında.");
        string relative = System.IO.Path.GetRelativePath(root.Path, canonical).Replace(System.IO.Path.DirectorySeparatorChar, '/');
        return root.Alias + "/" + relative;
    }

    public bool TryToRelativeId(string fullPath, out string relativeId)
    {
        try
        {
            relativeId = ToRelativeId(fullPath);
            return true;
        }
        catch (Exception ex) when (ex is ToolException or IOException or ArgumentException or UnauthorizedAccessException)
        {
            relativeId = "";
            return false;
        }
    }

    public bool IsWritableDesignPath(string fullPath)
    {
        string canonical = Canonicalize(fullPath);
        return Roots.Any(r => r.Writable && IsUnder(canonical, r.Path));
    }

    public static void CheckExtension(string path, GuardedFileKind kind)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        string[] allowed = kind switch
        {
            GuardedFileKind.Image => ImageExtensions,
            GuardedFileKind.Data => DataExtensions,
            _ => DesignExtensions,
        };
        if (!allowed.Contains(ext))
        {
            string what = kind switch { GuardedFileKind.Image => "görsel", GuardedFileKind.Data => "veri", _ => "tasarım" };
            throw new ToolException(ToolErrorCodes.InvalidArgument,
                $"Bu dosya türü {what} olarak kullanılamaz ({ext}). İzin verilenler: {string.Join(", ", allowed)}.");
        }
    }

    /// <summary>
    /// Yolu tam yola çevirir, aygıt yollarını ve alternatif veri akışlarını reddeder, var olan her bileşendeki
    /// bağlantıyı (symlink/junction) gerçek hedefine çözer.
    /// </summary>
    public static string Canonicalize(string path)
    {
        string full;
        try
        {
            full = System.IO.Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ToolException(ToolErrorCodes.InvalidArgument, "Geçersiz dosya yolu.");
        }
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new ToolException(ToolErrorCodes.PermissionDenied, "Aygıt yolları kullanılamaz.");
        int colon = full.IndexOf(':', 2);
        if (colon >= 0)
            throw new ToolException(ToolErrorCodes.PermissionDenied, "Alternatif veri akışı içeren yollar kullanılamaz.");

        string root = System.IO.Path.GetPathRoot(full) ?? "";
        string current = root;
        string[] parts = full[root.Length..].Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            current = System.IO.Path.Combine(current, parts[i]);
            FileSystemInfo? info = Directory.Exists(current) ? new DirectoryInfo(current)
                : File.Exists(current) ? new FileInfo(current) : null;
            if (info is null) continue;
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not null) current = System.IO.Path.GetFullPath(target.FullName);
            }
        }
        return System.IO.Path.TrimEndingDirectorySeparator(current);
    }

    public static bool IsUnder(string path, string root)
    {
        string p = System.IO.Path.TrimEndingDirectorySeparator(path);
        string r = System.IO.Path.TrimEndingDirectorySeparator(root);
        return string.Equals(p, r, StringComparison.OrdinalIgnoreCase)
               || p.StartsWith(r + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(System.IO.Path.TrimEndingDirectorySeparator(a), System.IO.Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    private static string UniqueAlias(string name, List<AllowedRoot> roots)
    {
        string alias = string.IsNullOrWhiteSpace(name) ? "Klasör" : name.Replace('/', '_');
        string candidate = alias;
        for (int n = 2; roots.Any(r => string.Equals(r.Alias, candidate, StringComparison.OrdinalIgnoreCase)); n++)
            candidate = $"{alias} ({n})";
        return candidate;
    }

    private string Display(string full, AllowedRoot root)
    {
        string relative = System.IO.Path.GetRelativePath(root.Path, full).Replace(System.IO.Path.DirectorySeparatorChar, '/');
        return root.Alias + "/" + relative;
    }
}
