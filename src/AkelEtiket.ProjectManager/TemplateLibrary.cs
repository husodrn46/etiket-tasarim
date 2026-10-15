using L10n = AkelEtiket.Core.Localization.UiText;
namespace AkelEtiket.ProjectManager;

/// <summary>Şablon galerisi kaydı.</summary>
public sealed class TemplateInfo
{
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public DateTime ModifiedAt { get; init; }
    public double WidthMm { get; init; }
    public double HeightMm { get; init; }
    public int ElementCount { get; init; }
    public bool HasFieldPlaceholders { get; init; }
    public byte[]? PreviewPng { get; init; }

    /// <summary>Okunamadıysa açıklama (dosya listede yine görünür).</summary>
    public string? Problem { get; init; }

    public string SizeText => $"{WidthMm:0.#} × {HeightMm:0.#} mm";
}

/// <summary>
/// Şablon klasörü yönetimi: listeleme, kaydetme, çoğaltma, yeniden adlandırma, silme.
/// Şablonlar normal .akelabel dosyalarıdır; varsayılan klasör Belgeler\Etiket Tasarım\Şablonlar.
/// </summary>
public sealed class TemplateLibrary
{
    public TemplateLibrary(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = Path.GetFullPath(folder);
    }

    public string Folder { get; }

    public static string DefaultFolder =>
        AkelEtiket.Core.AppIdentity.TemplatesFolder;

    public void EnsureFolder() => Directory.CreateDirectory(Folder);

    /// <summary>
    /// Başka bir klasördeki şablonları (ör. v1'in Belgeler\AKEL Etiket\Şablonlar klasörü) bu kütüphaneye
    /// <b>kopyalar</b>; aynı adlı dosyanın üzerine yazmaz, kaynak dosyalara dokunmaz. Kopyalanan dosya sayısını döndürür.
    /// </summary>
    public int ImportMissingFrom(string sourceFolder)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder)) return 0;
        if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceFolder)), Path.TrimEndingDirectorySeparator(Folder), StringComparison.OrdinalIgnoreCase)) return 0;
        EnsureFolder();
        int count = 0;
        foreach (var file in Directory.EnumerateFiles(sourceFolder, "*" + ProjectFile.Extension))
        {
            string name = Path.GetFileName(file);
            if (name.StartsWith('.')) continue;
            string target = Path.Combine(Folder, name);
            if (File.Exists(target)) continue;
            try
            {
                File.Copy(file, target, overwrite: false);
                count++;
            }
            catch (IOException)
            {
                // Kopyalanamayan dosya atlanır; diğerleri denenir.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return count;
    }

    public IReadOnlyList<TemplateInfo> List()
    {
        if (!Directory.Exists(Folder)) return [];
        var list = new List<TemplateInfo>();
        foreach (var file in Directory.EnumerateFiles(Folder, "*" + ProjectFile.Extension))
        {
            if (Path.GetFileName(file).StartsWith('.')) continue; // geçici dosyalar
            list.Add(ReadInfo(file));
        }
        return list.OrderBy(t => t.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), true)).ToList();
    }

    public static TemplateInfo ReadInfo(string file)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        try
        {
            var manifest = ProjectFile.ReadManifest(file);
            return new TemplateInfo
            {
                Name = name,
                FilePath = file,
                ModifiedAt = File.GetLastWriteTime(file),
                WidthMm = manifest.WidthMm,
                HeightMm = manifest.HeightMm,
                ElementCount = manifest.ElementCount,
                HasFieldPlaceholders = manifest.HasFieldPlaceholders,
                PreviewPng = ProjectFile.ReadPreview(file),
            };
        }
        catch (Exception ex) when (ex is ProjectFileException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return new TemplateInfo
            {
                Name = name,
                FilePath = file,
                ModifiedAt = File.Exists(file) ? File.GetLastWriteTime(file) : DateTime.MinValue,
                Problem = ex.Message,
            };
        }
    }

    /// <summary>Projeyi şablon olarak kaydeder. Aynı adda şablon varsa <paramref name="overwrite"/> false ise hata verir.</summary>
    public string SaveAsTemplate(LabelProject project, string name, byte[]? previewPng, bool overwrite)
    {
        EnsureFolder();
        string path = GetPathForName(name);
        if (File.Exists(path) && !overwrite)
            throw new ProjectFileException(L10n.T($"\"{SanitizeName(name)}\" adında bir şablon zaten var."));
        // Şablon kaydı açık projenin dosya yolunu değiştirmemeli.
        string? originalPath = project.FilePath;
        string originalName = project.Document.Name;
        project.Document.Name = SanitizeName(name);
        try
        {
            ProjectFile.Save(project, path, previewPng);
        }
        finally
        {
            project.FilePath = originalPath;
            project.Document.Name = originalName;
        }
        return path;
    }

    public string Duplicate(TemplateInfo template)
    {
        EnsureFolder();
        string baseName = template.Name + " (kopya)";
        string path = GetPathForName(baseName);
        for (int i = 2; File.Exists(path); i++) path = GetPathForName($"{template.Name} (kopya {i})");
        var loaded = ProjectFile.Load(template.FilePath).Project;
        loaded.Document.Name = Path.GetFileNameWithoutExtension(path);
        loaded.Document.CreatedAt = DateTimeOffset.Now;
        ProjectFile.Save(loaded, path, ProjectFile.ReadPreview(template.FilePath));
        return path;
    }

    public string Rename(TemplateInfo template, string newName)
    {
        string target = GetPathForName(newName);
        if (string.Equals(target, template.FilePath, StringComparison.OrdinalIgnoreCase)) return target;
        if (File.Exists(target)) throw new ProjectFileException(L10n.T($"\"{SanitizeName(newName)}\" adında bir şablon zaten var."));
        var loaded = ProjectFile.Load(template.FilePath).Project;
        loaded.Document.Name = SanitizeName(newName);
        ProjectFile.Save(loaded, target, ProjectFile.ReadPreview(template.FilePath));
        File.Delete(template.FilePath);
        return target;
    }

    /// <summary>Şablonu siler. <paramref name="deleteAction"/> verilirse (ör. Geri Dönüşüm Kutusu) o kullanılır.</summary>
    public void Delete(TemplateInfo template, Action<string>? deleteAction = null)
    {
        if (!File.Exists(template.FilePath)) return;
        if (deleteAction is not null) deleteAction(template.FilePath);
        else File.Delete(template.FilePath);
    }

    public string GetPathForName(string name) => Path.Combine(Folder, SanitizeName(name) + ProjectFile.Extension);

    /// <summary>Dosya adında kullanılamayan karakterleri temizler (Türkçe harfler korunur).</summary>
    public static string SanitizeName(string name)
    {
        // Şablonlar Windows'a taşınabilir; üreten işletim sisteminin karakter listesi yeterli değildir.
        var invalid = Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*").ToHashSet();
        string cleaned = new string((name ?? "").Select(c => c < 32 || invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (cleaned.Length == 0) cleaned = "Şablon";
        return cleaned.Length > 100 ? cleaned[..100] : cleaned;
    }
}
