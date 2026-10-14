using AkelEtiket.Core.Automation;
using AkelEtiket.Core.Serialization;
using AkelEtiket.Design.Data;
using AkelEtiket.Design.Security;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Design;

public sealed partial class DesignService
{
    /// <summary>Alt klasörlerde arama: bağlantı/kavşak klasörlerine girilmez (izinli klasör dışına taşmasın), gizli/sistem dosyaları atlanır.</summary>
    private static readonly EnumerationOptions SafeRecursive = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
    };

    /// <summary>Erişilebilir bütün tasarımlar: açık belgeler, izinli klasörlerdeki dosyalar, bu istemcinin taslakları ve yeni tasarımları.</summary>
    public IReadOnlyList<JsonObject> ListDesigns(string? query, ToolCallContext context)
    {
        string? filter = query is { Length: > 0 } ? ProductCatalog.Fold(query) : null;
        bool Match(string name) => filter is null || ProductCatalog.Fold(name).Contains(filter, StringComparison.Ordinal);
        var result = new List<JsonObject>();
        var settings = Settings;
        var openPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (LiveHost is not null && settings.ShareOpenDocument)
        {
            foreach (var doc in LiveHost.GetOpenDocuments())
            {
                if (doc.FilePath is { } p) openPaths.Add(PathGuard.Canonicalize(p));
                if (!Match(doc.Name)) continue;
                var o = new JsonObject
                {
                    ["design_id"] = doc.DesignId,
                    ["kind"] = "open_document",
                    ["name"] = doc.Name,
                    ["version"] = doc.Version,
                    ["unsaved_changes"] = doc.IsDirty,
                    ["editable"] = settings.ChangeMode == AiChangeMode.Direct,
                };
                if (doc.FilePath is { } path && Guard.TryToRelativeId(path, out var rel)) o["file"] = rel;
                if (doc.SelectedElementIds.Count > 0) o["selected_element_count"] = doc.SelectedElementIds.Count;
                result.Add(o);
            }
        }

        foreach (var root in Guard.Roots)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root.Path, "*" + ProjectFile.Extension, SafeRecursive)
                    .Where(f => !Path.GetFileName(f).StartsWith('.')).Take(500).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (string file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!Match(name)) continue;
                var info = TemplateLibrary.ReadInfo(file);
                string rel = root.Alias + "/" + Path.GetRelativePath(root.Path, file).Replace(Path.DirectorySeparatorChar, '/');
                var o = new JsonObject
                {
                    ["design_id"] = DesignId.File(rel).ToString(),
                    ["kind"] = "file",
                    ["name"] = info.Name,
                    ["size_mm"] = info.Problem is null ? $"{info.WidthMm:0.#} × {info.HeightMm:0.#}" : null,
                    ["element_count"] = info.ElementCount,
                    ["uses_data_fields"] = info.HasFieldPlaceholders,
                    ["modified"] = info.ModifiedAt.ToString("yyyy-MM-dd HH:mm"),
                    ["read_only_folder"] = !root.Writable,
                };
                if (openPaths.Contains(PathGuard.Canonicalize(file))) o["open_in_app"] = true;
                if (info.Problem is { } problem) o["problem"] = problem;
                result.Add(o);
            }
        }

        foreach (var item in Working.All)
        {
            if (item.IsDraft && item.Meta.State is DraftState.Applied or DraftState.Discarded or DraftState.Rejected) continue;
            if (!IsOwner(item, context)) continue;
            string name = item.Project.Document.Name;
            if (!Match(name)) continue;
            var o = new JsonObject
            {
                ["design_id"] = item.DesignId,
                ["kind"] = item.IsDraft ? "draft" : "new",
                ["name"] = name,
                ["version"] = item.Version,
                ["modified"] = item.Meta.ModifiedAt.ToString("yyyy-MM-dd HH:mm"),
            };
            if (item.IsDraft)
            {
                o["target_design_id"] = item.Meta.TargetDesignId;
                o["state"] = item.Meta.State.ToString();
            }
            else if (item.Meta.SavedPath is { } saved)
            {
                o["saved_file"] = Guard.TryToRelativeId(saved, out var rel) ? rel : Path.GetFileName(saved);
            }
            result.Add(o);
        }
        return result;
    }

    /// <summary>Yeni boş tasarım (yapay zekâya ait çalışma kopyası; save_design ile dosya olur).</summary>
    public WorkingDesign CreateDesign(CreateDesignArgs args, ToolCallContext context)
    {
        if (string.IsNullOrWhiteSpace(args.Name)) throw new ToolException(ToolErrorCodes.InvalidArgument, "name gerekli.");
        double width = args.WidthMm ?? 100, height = args.HeightMm ?? 100;
        if (width is < LabelSetup.MinSizeMm or > 108 || height is < LabelSetup.MinSizeMm or > LabelSetup.MaxSizeMm)
            throw new ToolException(ToolErrorCodes.InvalidArgument,
                $"Etiket ölçüsü geçersiz. Genişlik {LabelSetup.MinSizeMm}–108 mm (XP-470B baskı genişliği), yükseklik {LabelSetup.MinSizeMm}–{LabelSetup.MaxSizeMm} mm olmalı.");
        var doc = LabelDocument.CreateBlank(width, height, args.Name.Trim());
        doc.Description = args.Description ?? $"Yapay zekâ ({context.ClientName}) tarafından oluşturuldu.";
        return Working.Create(DesignKind.New, new LabelProject(doc), context.ClientName);
    }

    /// <summary>Var olan bir tasarımın kopyasını yeni tasarım olarak oluşturur (asıl değişmez).</summary>
    public async Task<WorkingDesign> DuplicateAsync(string sourceDesignId, string name, ToolCallContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ToolException(ToolErrorCodes.InvalidArgument, "name gerekli.");
        var state = await ReadAsync(sourceDesignId, new ToolCallContext { ClientName = context.ClientName, ClientId = context.ClientId }, cancellationToken)
            .ConfigureAwait(false);
        var doc = ModelJson.Clone(state.Document);
        doc.Name = name.Trim();
        foreach (var element in doc.Elements) element.Id = LabelElement.NewId();
        return Working.Create(DesignKind.New, new LabelProject(doc, CopyAssets(state.Assets)), context.ClientName);
    }

    /// <summary>
    /// Kaydeder. Yeni tasarım ilk kayıtta Şablonlar klasörüne yeni dosya olarak yazılır (var olanın üzerine asla);
    /// sonraki kayıtlar yalnızca o dosya başkası tarafından değiştirilmediyse aynı dosyaya (yedekli) yazılır.
    /// </summary>
    public async Task<(string DesignId, string FilePath, string Version, bool Created)> SaveAsync(string designId, string? fileName,
        ToolCallContext context, CancellationToken cancellationToken)
    {
        var id = Resolve(designId, context);
        switch (id.Kind)
        {
            case DesignKind.New:
            {
                var item = GetWorking(id);
                if (!IsOwner(item, context))
                    throw new ToolException(ToolErrorCodes.PermissionDenied, $"Bu tasarım \"{item.Meta.Owner}\" istemcisine ait.");
                await item.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    string? target = item.Meta.SavedPath;
                    bool created = false;
                    if (target is null || (fileName is { Length: > 0 } && !string.Equals(Path.GetFileNameWithoutExtension(target),
                            TemplateLibrary.SanitizeName(fileName), StringComparison.OrdinalIgnoreCase)))
                    {
                        string baseName = TemplateLibrary.SanitizeName(string.IsNullOrWhiteSpace(fileName) ? item.Project.Document.Name : fileName);
                        target = Path.Combine(Guard.TemplatesRoot.Path, baseName + ProjectFile.Extension);
                        if (File.Exists(target))
                            throw new ToolException(ToolErrorCodes.InvalidArgument,
                                $"\"{baseName}{ProjectFile.Extension}\" zaten var; var olan dosyanın üzerine yazılmaz. Başka bir file_name verin.");
                        created = true;
                    }
                    else
                    {
                        string? current = FileVersions.TryCompute(target);
                        if (current is not null && current != item.Meta.SavedFileVersion)
                            throw new ToolException(ToolErrorCodes.VersionConflict,
                                "Dosya siz kaydettikten sonra başka biri tarafından değiştirildi; üzerine yazılmadı. Farklı bir file_name ile kaydedin.");
                        Backups.Backup(target);
                    }
                    var copy = new LabelProject(ModelJson.Clone(item.Project.Document), CopyAssets(item.Project.Assets));
                    SaveProject(copy, target);
                    item.Meta.SavedPath = target;
                    item.Meta.SavedFileVersion = FileVersions.Compute(target);
                    Working.Persist(item);
                    string fileId = DesignId.File(Guard.ToRelativeId(target)).ToString();
                    LiveHost?.DesignSaved(fileId, item.Project.Document.Name, target, context.ClientName);
                    return (fileId, target, item.Meta.SavedFileVersion, created);
                }
                finally
                {
                    item.Gate.Release();
                }
            }
            case DesignKind.File:
            {
                string path = Guard.ResolveExistingFile(id.Value, GuardedFileKind.Design);
                if (Settings.ChangeMode != AiChangeMode.Direct)
                    throw new ToolException(ToolErrorCodes.ApprovalRequired,
                        "Var olan dosyalar yalnızca kullanıcı onayıyla değişir: begin_draft → düzenle → submit_draft.");
                // Doğrudan modda dosya değişiklikleri zaten her adımda kaydedilir.
                return (DesignId.File(Guard.ToRelativeId(path)).ToString(), path, FileVersions.Compute(path), false);
            }
            case DesignKind.Live:
                throw new ToolException(ToolErrorCodes.PermissionDenied,
                    "Uygulamada açık belgeyi kullanıcı kaydeder (Ctrl+S). Ayrı bir kopya için duplicate_design + save_design kullanın.");
            default:
                throw new ToolException(ToolErrorCodes.InvalidArgument, "Taslaklar kaydedilmez; submit_draft ile kullanıcı onayına sunun.");
        }
    }

    /// <summary>
    /// Tasarımın uygulamada açılmasını kullanıcıya önerir (Yapay Zekâ panelinde "Aç" düğmesi). Uygulama kapalıysa
    /// <see cref="ToolErrorCodes.Unavailable"/>.
    /// </summary>
    public string RequestOpenInApp(string designId, ToolCallContext context)
    {
        var id = Resolve(designId, context);
        if (LiveHost is null)
            throw new ToolException(ToolErrorCodes.Unavailable,
                "Etiket Tasarım uygulaması açık değil. Kaydedilen tasarımlar uygulama açılınca Şablonlar panelinde görünür.");
        string name;
        string target;
        switch (id.Kind)
        {
            case DesignKind.File:
                string path = Guard.ResolveExistingFile(id.Value, GuardedFileKind.Design);
                name = Path.GetFileNameWithoutExtension(path);
                target = DesignId.File(Guard.ToRelativeId(path)).ToString();
                break;
            case DesignKind.New:
                var item = GetWorking(id);
                name = item.Project.Document.Name;
                target = item.DesignId;
                break;
            case DesignKind.Live:
                return "Tasarım zaten uygulamada açık.";
            default:
                throw new ToolException(ToolErrorCodes.InvalidArgument, "Taslaklar açılmaz; submit_draft ile öneri olarak sunun.");
        }
        LiveHost.OpenRequested(target, name, context.ClientName);
        return $"Kullanıcıya \"{name}\" tasarımını açması önerildi (Yapay Zekâ panelinde \"Aç\" düğmesi).";
    }

    /// <summary>Yeni (kaydedilmemiş) bir tasarımın kopyası: masaüstünde yeni belge olarak açmak için.</summary>
    public LabelProject? GetNewDesignCopy(string designId)
    {
        if (!DesignId.TryParse(designId, out var id) || id.Kind != DesignKind.New || !Working.TryGet(id.Value, out var item)) return null;
        return new LabelProject(ModelJson.Clone(item.Project.Document), CopyAssets(item.Project.Assets));
    }

    /// <summary>Dosya kimliğinin tam yolu (masaüstü açarken).</summary>
    public string? ResolveFilePath(string designId)
    {
        if (!DesignId.TryParse(designId, out var id) || id.Kind != DesignKind.File) return null;
        try
        {
            return Guard.ResolveExistingFile(id.Value, GuardedFileKind.Design);
        }
        catch (ToolException)
        {
            return null;
        }
    }

    /// <summary>İzinli klasörlerdeki görsel/veri/tasarım dosyalarını listeler.</summary>
    public IReadOnlyList<JsonObject> ListFiles(FileListKind kind, string? query, int limit)
    {
        string[] patterns = kind switch
        {
            FileListKind.Images => ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp", "*.svg"],
            FileListKind.Data => ["*.xlsx", "*.xlsm", "*.xls", "*.csv"],
            _ => ["*" + ProjectFile.Extension],
        };
        string? filter = query is { Length: > 0 } ? ProductCatalog.Fold(query) : null;
        var result = new List<JsonObject>();
        // Bir dosya birden çok desene uyarsa (ör. .NET Framework'te "*.xls" ".xlsx"i de bulurdu) yalnızca bir kez listelenir.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in Guard.Roots)
        {
            foreach (string pattern in patterns)
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(root.Path, pattern, SafeRecursive).Take(2000).ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                foreach (string file in files)
                {
                    if (Path.GetFileName(file).StartsWith('.') || !seen.Add(file)) continue;
                    string rel = root.Alias + "/" + Path.GetRelativePath(root.Path, file).Replace(Path.DirectorySeparatorChar, '/');
                    if (filter is not null && !ProductCatalog.Fold(rel).Contains(filter, StringComparison.Ordinal)) continue;
                    var info = new FileInfo(file);
                    result.Add(new JsonObject
                    {
                        ["path"] = rel,
                        ["size_kb"] = Math.Round(info.Length / 1024.0, 1),
                        ["modified"] = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                    });
                    if (result.Count >= limit) return result;
                }
            }
        }
        return result;
    }
}
