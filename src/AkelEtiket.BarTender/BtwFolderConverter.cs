using System.Text;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.BarTender;

/// <summary>Toplu dönüştürmede tek dosyanın sonucu.</summary>
public sealed record BtwFileOutcome(string SourcePath, string? TargetPath, BtwFileStatus Status, IReadOnlyList<string> Notes);

public enum BtwFileStatus
{
    Converted,
    ConvertedWithNotes,
    SkippedExisting,
    Failed,
}

/// <summary>Toplu dönüştürme özeti.</summary>
public sealed class BtwFolderResult
{
    public required string SourceFolder { get; init; }
    public required string TargetFolder { get; init; }
    public required IReadOnlyList<BtwFileOutcome> Files { get; init; }
    public string? ReportPath { get; init; }
    public bool Cancelled { get; init; }

    public int Converted => Files.Count(f => f.Status is BtwFileStatus.Converted or BtwFileStatus.ConvertedWithNotes);
    public int WithNotes => Files.Count(f => f.Status == BtwFileStatus.ConvertedWithNotes);
    public int Skipped => Files.Count(f => f.Status == BtwFileStatus.SkippedExisting);
    public int Failed => Files.Count(f => f.Status == BtwFileStatus.Failed);
}

/// <summary>
/// Bir klasördeki (alt klasörler dahil) tüm BarTender etiketlerini LabelHarbor etiketine (.akelabel) çevirir.
/// Klasör yapısı korunur, özgün dosyalara dokunulmaz, hedefte aynı adlı dosya varsa üzerine yazılmaz.
/// Hedef klasöre "Aktarım raporu.txt" yazılır.
/// </summary>
public static class BtwFolderConverter
{
    public const string ReportFileName = "Aktarım raporu.txt";

    public static IReadOnlyList<string> FindFiles(string sourceFolder)
    {
        if (!Directory.Exists(sourceFolder)) return [];
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
        return Directory.EnumerateFiles(sourceFolder, "*" + BtwReader.Extension, options)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Hedef klasör kaynak klasörün içinde olamaz (dönüştürülen dosyalar yeniden taranmasın).</summary>
    public static bool IsInside(string folder, string possibleParent)
    {
        string a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(possibleParent)) + Path.DirectorySeparatorChar;
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
    }

    public static BtwFolderResult Convert(string sourceFolder, string targetFolder, IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancellationToken = default, FontProvider? fonts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolder);
        sourceFolder = Path.GetFullPath(sourceFolder);
        targetFolder = Path.GetFullPath(targetFolder);
        if (IsInside(targetFolder, sourceFolder))
            throw new ArgumentException(L10n.T("Hedef klasör, BarTender klasörünün içinde olamaz."), nameof(targetFolder));
        fonts ??= FontProvider.Default;
        var files = FindFiles(sourceFolder);
        var outcomes = new List<BtwFileOutcome>(files.Count);
        bool cancelled = false;
        Directory.CreateDirectory(targetFolder);
        for (int i = 0; i < files.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
            outcomes.Add(ConvertOne(files[i], sourceFolder, targetFolder, fonts));
            progress?.Report((i + 1, files.Count));
        }
        string? report = WriteReport(sourceFolder, targetFolder, outcomes, cancelled);
        return new BtwFolderResult
        {
            SourceFolder = sourceFolder,
            TargetFolder = targetFolder,
            Files = outcomes,
            ReportPath = report,
            Cancelled = cancelled,
        };
    }

    private static BtwFileOutcome ConvertOne(string file, string sourceFolder, string targetFolder, FontProvider fonts)
    {
        string relative = Path.GetRelativePath(sourceFolder, file);
        string target = Path.Combine(targetFolder, Path.ChangeExtension(relative, ProjectFile.Extension));
        if (File.Exists(target))
            return new BtwFileOutcome(file, target, BtwFileStatus.SkippedExisting, [L10n.T("Hedefte aynı adlı dosya var; üzerine yazılmadı.")]);
        try
        {
            var result = BtwImporter.Import(file, fonts);
            byte[]? preview = RenderPreview(result.Project, fonts);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            ProjectFile.Save(result.Project, target, preview);
            var status = result.Warnings.Count > 0 ? BtwFileStatus.ConvertedWithNotes : BtwFileStatus.Converted;
            return new BtwFileOutcome(file, target, status, result.Warnings);
        }
        catch (Exception ex) when (ex is BtwFormatException or ProjectFileException or IOException or UnauthorizedAccessException)
        {
            return new BtwFileOutcome(file, null, BtwFileStatus.Failed, [ex.Message]);
        }
    }

    /// <summary>Şablon galerisinde görünen küçük önizleme (4 nokta/mm), düzenleyicinin kaydettiğiyle aynı.</summary>
    private static byte[]? RenderPreview(LabelProject project, FontProvider fonts)
    {
        try
        {
            using var renderer = new LabelRenderer(project.Assets, fonts);
            var result = renderer.RenderLabel(project.Document, new RenderOptions { DotsPerMm = 4, Mode = RenderMode.Design });
            return MonoImageExport.EncodePng(result.Bitmap, 4 * 25.4);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? WriteReport(string sourceFolder, string targetFolder, IReadOnlyList<BtwFileOutcome> outcomes, bool cancelled)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(L10n.T("BarTender aktarım raporu"));
            sb.AppendLine(L10n.T($"Kaynak: {sourceFolder}"));
            sb.AppendLine(L10n.T($"Hedef: {targetFolder}"));
            sb.AppendLine(L10n.T($"Tarih: {DateTime.Now:yyyy-MM-dd HH:mm}"));
            int converted = outcomes.Count(o => o.Status is BtwFileStatus.Converted or BtwFileStatus.ConvertedWithNotes);
            sb.AppendLine(L10n.T($"Dönüştürülen: {converted}, notlu: {outcomes.Count(o => o.Status == BtwFileStatus.ConvertedWithNotes)}, atlanan: {outcomes.Count(o => o.Status == BtwFileStatus.SkippedExisting)}, açılamayan: {outcomes.Count(o => o.Status == BtwFileStatus.Failed)}"));
            if (cancelled) sb.AppendLine(L10n.T("İşlem yarıda bırakıldı."));
            sb.AppendLine();
            foreach (var o in outcomes.Where(o => o.Status != BtwFileStatus.Converted))
            {
                string status = o.Status switch
                {
                    BtwFileStatus.ConvertedWithNotes => L10n.T("NOT"),
                    BtwFileStatus.SkippedExisting => L10n.T("ATLANDI"),
                    _ => L10n.T("HATA"),
                };
                sb.AppendLine($"[{status}] {Path.GetRelativePath(sourceFolder, o.SourcePath)}");
                foreach (string note in o.Notes) sb.AppendLine("    - " + note);
            }
            string path = Path.Combine(targetFolder, ReportFileName);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
