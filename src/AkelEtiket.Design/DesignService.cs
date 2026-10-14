using System.Collections.Concurrent;
using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Automation;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Serialization;
using AkelEtiket.Design.Data;
using AkelEtiket.Design.Security;
using AkelEtiket.DataImport;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;

namespace AkelEtiket.Design;

/// <summary>Tasarım servisinin klasör ve sağlayıcı ayarları (testler kendi geçici klasörlerini verir).</summary>
public sealed class DesignServiceOptions
{
    public Func<AiSettings>? SettingsProvider { get; init; }
    public string? TemplatesFolder { get; init; }
    public string? SamplesFolder { get; init; }
    public string? WorkingFolder { get; init; }
    public string? ProposalsFolder { get; init; }
    public string? BackupFolder { get; init; }
    public string? ProductsFile { get; init; }
    public string? AuditFolder { get; init; }

    /// <summary>Yerleşik "firma-logo" görseli (yoksa gömülü kopya kullanılır).</summary>
    public Func<byte[]?>? LogoProvider { get; init; }

    /// <summary>Önizlemede kullanılacak ek yerleşik alanlar.</summary>
    public Func<IFieldValueSource?>? ExtraPreviewFields { get; init; }

    /// <summary>GS1 firma profili (varsayılan: Belgeler\Etiket Tasarım\GS1). Önizlemede {SSCC} numara ayırmadan gösterilir.</summary>
    public AkelEtiket.Gs1.Gs1Context? Gs1 { get; init; }
}

/// <summary>
/// Arayüzden bağımsız tasarım servisi: yapay zekâ araçları (MCP, yerel API, uygulama içi asistan) ve masaüstü bu
/// servis üzerinden tasarımları okur ve değiştirir. Tasarım dosyaları, açık belgeler (masaüstü varsa), taslaklar ve
/// yeni tasarımlar tek bir kimlik sistemiyle yönetilir; sürüm denetimi, onay modu ve izinli klasörler burada uygulanır.
/// </summary>
public sealed partial class DesignService
{
    private readonly DesignServiceOptions _options;
    private readonly Func<AiSettings> _settingsProvider;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (string? ProductKey, int? Row)> _livePreview = new(StringComparer.OrdinalIgnoreCase);
    private PathGuard? _guard;
    private string _guardKey = "";

    public DesignService(DesignServiceOptions? options = null)
    {
        _options = options ?? new DesignServiceOptions();
        _settingsProvider = _options.SettingsProvider ?? AiSettingsStore.Load;
        Working = new WorkingDesignStore(_options.WorkingFolder);
        Proposals = new ProposalStore(_options.ProposalsFolder);
        Backups = new BackupService(_options.BackupFolder);
        Products = new ProductCatalog(_options.ProductsFile);
        Audit = new AuditLog(_options.AuditFolder);
        Gs1 = _options.Gs1 ?? AkelEtiket.Gs1.Gs1Context.Default;
    }

    /// <summary>GS1 firma profili ve SSCC kaydı (yapay zekâ yalnızca okur; numara ayırmaz).</summary>
    public AkelEtiket.Gs1.Gs1Context Gs1 { get; }

    /// <summary>Masaüstü uygulaması açıkken canlı belge erişimi (Worker'da null).</summary>
    public ILiveDocumentHost? LiveHost { get; set; }

    public WorkingDesignStore Working { get; }
    public ProposalStore Proposals { get; }
    public BackupService Backups { get; }
    public ProductCatalog Products { get; }
    public DataSourceCache DataCache { get; } = new();
    public AuditLog Audit { get; }

    public AiSettings Settings => _settingsProvider();

    /// <summary>İzinli klasörler (ayarlar değişince yeniden kurulur).</summary>
    public PathGuard Guard
    {
        get
        {
            var settings = Settings;
            string key = string.Join("|", settings.AllowedFolders) + "|" + _options.TemplatesFolder + "|" + _options.SamplesFolder;
            var guard = _guard;
            if (guard is null || key != _guardKey)
            {
                guard = PathGuard.FromSettings(settings, _options.TemplatesFolder, _options.SamplesFolder);
                _guard = guard;
                _guardKey = key;
            }
            return guard;
        }
    }

    // ───────────────────────── Kimlik çözümleme ─────────────────────────

    /// <summary>Kimliği çözer: uygulama içi asistan için hedefi taslağa yönlendirir, açık dosyayı canlı belgeye eşler.</summary>
    private DesignId Resolve(string designId, ToolCallContext context, bool trusted = false)
    {
        var id = DesignId.Parse(designId);
        if (context.ForcedDraftId is { } forced && DesignId.TryParse(forced, out var forcedId) && forcedId.Kind == DesignKind.Draft && id != forcedId)
        {
            if (Working.TryGet(forcedId.Value, out var draft) && SameTarget(draft.Meta.TargetDesignId, id))
                return forcedId;
            if (id.Kind is DesignKind.File or DesignKind.Live)
                throw new ToolException(ToolErrorCodes.PermissionDenied,
                    $"Bu oturumda yalnızca açık tasarımın taslağı ({forced}) değiştirilebilir.");
        }
        if (id.Kind == DesignKind.File && LiveHost is not null && (trusted || Settings.ShareOpenDocument))
        {
            string path = Guard.ResolveExistingFile(id.Value, GuardedFileKind.Design);
            var live = LiveHost.GetOpenDocuments().FirstOrDefault(d => d.FilePath is { } p && PathGuard.IsUnder(PathGuard.Canonicalize(p), path));
            if (live is not null) return DesignId.Live(live.SessionNumber);
        }
        return id;
    }

    private bool SameTarget(string? targetId, DesignId candidate) =>
        targetId is not null && DesignId.TryParse(targetId, out var target) &&
        string.Equals(TargetKey(target), TargetKey(candidate), StringComparison.OrdinalIgnoreCase);

    /// <summary>Aynı belgeyi gösteren kimlikler için ortak anahtar: dosya yolu (varsa) ya da açık belge numarası.</summary>
    private string TargetKey(DesignId id)
    {
        try
        {
            if (id.Kind == DesignKind.File) return "f|" + Guard.ResolveExistingFile(id.Value, GuardedFileKind.Design);
            if (id.Kind == DesignKind.Live && LiveHost is not null)
            {
                var doc = LiveHost.GetOpenDocuments().FirstOrDefault(d => d.SessionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) == id.Value);
                if (doc?.FilePath is { } p && Guard.TryToRelativeId(p, out _)) return "f|" + PathGuard.Canonicalize(p);
            }
        }
        catch (ToolException)
        {
        }
        return id.ToString();
    }

    private ILiveDocumentHost RequireLiveHost(bool trusted = false)
    {
        if (LiveHost is null)
            throw new ToolException(ToolErrorCodes.Unavailable,
                "Etiket Tasarım uygulaması açık değil; açık belgelere erişilemez. Dosya tasarımlarıyla (dosya:…) çalışabilirsiniz.");
        if (!trusted && !Settings.ShareOpenDocument)
            throw new ToolException(ToolErrorCodes.PermissionDenied,
                "Kullanıcı açık tasarımı yapay zekâ ile paylaşmayı kapattı (Ayarlar → Yapay Zekâ).");
        return LiveHost;
    }

    private static int LiveNumber(DesignId id) => int.Parse(id.Value, System.Globalization.CultureInfo.InvariantCulture);

    // ───────────────────────── Okuma ─────────────────────────

    public Task<DesignState> ReadAsync(string designId, ToolCallContext context, CancellationToken cancellationToken) =>
        ReadCoreAsync(designId, context, trusted: false, cancellationToken);

    /// <summary>
    /// Masaüstünün kendi işlemleri için okuma (öneri önizlemesi, uygulama içi asistanın taslağı): "açık tasarımı paylaş"
    /// ayarı yalnızca dış ajanları sınırlar.
    /// </summary>
    internal Task<DesignState> ReadTrustedAsync(string designId, CancellationToken cancellationToken) =>
        ReadCoreAsync(designId, new ToolCallContext { ClientName = HostClientName }, trusted: true, cancellationToken);

    /// <summary>Masaüstünün kendi adına yaptığı işlemlerde kullanılan istemci adı.</summary>
    public const string HostClientName = "Etiket Tasarım";

    private async Task<DesignState> ReadCoreAsync(string designId, ToolCallContext context, bool trusted, CancellationToken cancellationToken)
    {
        var id = Resolve(designId, context, trusted);
        var settings = Settings;
        switch (id.Kind)
        {
            case DesignKind.Draft:
            case DesignKind.New:
            {
                var item = GetWorking(id);
                await item.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var doc = ModelJson.Clone(item.Project.Document);
                    var assets = CopyAssets(item.Project.Assets);
                    bool owner = IsOwner(item, context);
                    bool open = !item.IsDraft || item.Meta.State == DraftState.Open;
                    var fields = PreviewFieldsFor(doc, item.Meta.PreviewProductKey, item.Meta.PreviewRow, null, out string? source);
                    return new DesignState
                    {
                        DesignId = item.DesignId,
                        Kind = id.Kind,
                        Name = doc.Name,
                        Version = item.Version,
                        Document = doc,
                        Assets = assets,
                        Editable = owner && open,
                        EditHint = !owner ? $"Bu çalışma kopyası \"{item.Meta.Owner}\" istemcisine ait; değiştirilemez."
                            : !open ? $"Taslak {item.Meta.State} durumunda; değişiklik için yeni taslak açın (begin_draft)." : null,
                        PreviewFields = fields,
                        PreviewSource = source,
                        BaseDocument = item.GetBaseDocument(),
                        WorkingMeta = item.Meta,
                        FilePath = item.Meta.SavedPath,
                    };
                }
                finally
                {
                    item.Gate.Release();
                }
            }
            case DesignKind.Live:
            {
                var host = RequireLiveHost(trusted);
                var snapshot = await host.SnapshotAsync(LiveNumber(id), cancellationToken).ConfigureAwait(false);
                var assets = new AssetStore();
                foreach (var asset in snapshot.Assets) assets.AddExisting(asset);
                _livePreview.TryGetValue(id.ToString(), out var preview);
                var fields = PreviewFieldsFor(snapshot.Document, preview.ProductKey, preview.Row, snapshot.PreviewFields, out string? source);
                bool direct = settings.ChangeMode == AiChangeMode.Direct;
                return new DesignState
                {
                    DesignId = id.ToString(),
                    Kind = DesignKind.Live,
                    Name = snapshot.Info.Name,
                    Version = snapshot.Info.Version,
                    Document = snapshot.Document,
                    Assets = assets,
                    FilePath = snapshot.Info.FilePath,
                    RelativePath = snapshot.Info.FilePath is { } p && Guard.TryToRelativeId(p, out var rel) ? rel : null,
                    Editable = direct,
                    EditHint = direct ? null : ApprovalHint(id.ToString()),
                    PreviewFields = fields,
                    PreviewSource = source,
                    IsOpenInApp = true,
                    SelectedElementIds = snapshot.Info.SelectedElementIds,
                };
            }
            default:
            {
                string path = Guard.ResolveExistingFile(id.Value, GuardedFileKind.Design);
                var gate = _fileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var loaded = LoadProject(path);
                    var doc = loaded.Project.Document;
                    bool writable = Guard.IsWritableDesignPath(path);
                    bool direct = settings.ChangeMode == AiChangeMode.Direct;
                    var fields = PreviewFieldsFor(doc, null, null, null, out string? source);
                    return new DesignState
                    {
                        DesignId = DesignId.File(Guard.ToRelativeId(path)).ToString(),
                        Kind = DesignKind.File,
                        Name = doc.Name,
                        Version = FileVersions.Compute(path),
                        Document = doc,
                        Assets = loaded.Project.Assets,
                        FilePath = path,
                        RelativePath = Guard.ToRelativeId(path),
                        Editable = direct && writable,
                        EditHint = !writable ? "Bu klasör salt okunur; değiştirmek için duplicate_design ile kopyasını oluşturun."
                            : direct ? null : ApprovalHint(DesignId.File(Guard.ToRelativeId(path)).ToString()),
                        PreviewFields = fields,
                        PreviewSource = source,
                    };
                }
                finally
                {
                    gate.Release();
                }
            }
        }
    }

    private static string ApprovalHint(string designId) =>
        $"Onaylı mod: bu tasarımı değiştirmek için begin_draft(\"{designId}\") ile taslak açın, değişiklikleri taslakta yapın ve " +
        "submit_draft ile kullanıcı onayına sunun. Kullanıcı kabul ederse tek adımda uygulanır.";

    // ───────────────────────── Değiştirme ─────────────────────────

    /// <summary>
    /// Tasarımı değiştirir. Taslaklar/yeni tasarımlar her zaman (sahibi tarafından) değiştirilebilir; açık belgeler ve
    /// dosyalar yalnızca doğrudan modda. <paramref name="expectedVersion"/> verilirse sürüm denetlenir.
    /// </summary>
    public async Task<MutationResult<T>> MutateAsync<T>(string designId, string? expectedVersion, ToolCallContext context, string description,
        Func<DesignEditContext, T> action, CancellationToken cancellationToken)
    {
        var id = Resolve(designId, context);
        var settings = Settings;
        switch (id.Kind)
        {
            case DesignKind.Draft:
            case DesignKind.New:
            {
                var item = GetWorking(id);
                if (!IsOwner(item, context))
                    throw new ToolException(ToolErrorCodes.PermissionDenied, $"Bu çalışma kopyası \"{item.Meta.Owner}\" istemcisine ait.");
                if (item.IsDraft && item.Meta.State != DraftState.Open)
                    throw new ToolException(ToolErrorCodes.PermissionDenied,
                        $"Taslak {item.Meta.State} durumunda; artık değiştirilemez. Yeni değişiklikler için yeni taslak açın.");
                await item.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    CheckVersion(expectedVersion, item.Version, item.DesignId);
                    var doc = item.Project.Document;
                    var fields = PreviewFieldsFor(doc, item.Meta.PreviewProductKey, item.Meta.PreviewRow, null, out _);
                    var edit = new DesignEditContext(doc, item.Project.Assets, context.AllowedElementIds, item.GetBaseDocument(), fields);
                    string before = ModelJson.Serialize(doc, indented: false);
                    T value;
                    try
                    {
                        value = action(edit);
                    }
                    catch
                    {
                        // Başarısız işlem yarım değişiklik bırakmasın.
                        RestoreDocument(item, before);
                        throw;
                    }
                    bool changed = ModelJson.Serialize(doc, indented: false) != before;
                    if (changed)
                    {
                        item.Meta.Revision++;
                        Working.Persist(item);
                    }
                    return new MutationResult<T>(value, item.DesignId, item.Version, changed, edit.Warnings);
                }
                finally
                {
                    item.Gate.Release();
                }
            }
            case DesignKind.Live:
            {
                var host = RequireLiveHost();
                if (settings.ChangeMode != AiChangeMode.Direct)
                    throw new ToolException(ToolErrorCodes.ApprovalRequired, ApprovalHint(id.ToString()));
                _livePreview.TryGetValue(id.ToString(), out var preview);
                var snapshotFields = (await host.SnapshotAsync(LiveNumber(id), cancellationToken).ConfigureAwait(false)).PreviewFields;
                var result = await host.MutateAsync(LiveNumber(id), expectedVersion, $"Yapay zekâ ({context.ClientName}): {description}",
                    (doc, assets) =>
                    {
                        var fields = PreviewFieldsFor(doc, preview.ProductKey, preview.Row, snapshotFields, out _);
                        var edit = new DesignEditContext(doc, assets, context.AllowedElementIds, null, fields);
                        var value = action(edit);
                        return (value, edit.Warnings);
                    }, cancellationToken).ConfigureAwait(false);
                return new MutationResult<T>(result.Value.value, id.ToString(), result.NewVersion, result.Changed, result.Value.Warnings);
            }
            default:
            {
                if (settings.ChangeMode != AiChangeMode.Direct)
                    throw new ToolException(ToolErrorCodes.ApprovalRequired, ApprovalHint(id.ToString()));
                string path = Guard.ResolveExistingFile(id.Value, GuardedFileKind.Design);
                if (!Guard.IsWritableDesignPath(path))
                    throw new ToolException(ToolErrorCodes.PermissionDenied, "Bu klasör salt okunur; duplicate_design ile kopyasını oluşturun.");
                var gate = _fileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    string version = FileVersions.Compute(path);
                    CheckVersion(expectedVersion, version, designId);
                    var loaded = LoadProject(path);
                    var doc = loaded.Project.Document;
                    var fields = PreviewFieldsFor(doc, null, null, null, out _);
                    var edit = new DesignEditContext(doc, loaded.Project.Assets, context.AllowedElementIds, null, fields);
                    string before = ModelJson.Serialize(doc, indented: false);
                    T value = action(edit);
                    bool changed = ModelJson.Serialize(doc, indented: false) != before;
                    if (changed)
                    {
                        Backups.Backup(path);
                        SaveProject(loaded.Project, path);
                        version = FileVersions.Compute(path);
                    }
                    return new MutationResult<T>(value, DesignId.File(Guard.ToRelativeId(path)).ToString(), version, changed, edit.Warnings);
                }
                finally
                {
                    gate.Release();
                }
            }
        }
    }

    private static void RestoreDocument(WorkingDesign item, string json)
    {
        var restored = ModelJson.DeserializeDocument(json);
        var doc = item.Project.Document;
        doc.Name = restored.Name;
        doc.Description = restored.Description;
        doc.Label = restored.Label;
        doc.Elements.Clear();
        foreach (var e in restored.Elements) doc.Elements.Add(e);
        doc.DataSource = restored.DataSource;
    }

    private static void CheckVersion(string? expected, string actual, string designId)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.Equals(expected.Trim(), actual, StringComparison.Ordinal)) return;
        throw new ToolException(ToolErrorCodes.VersionConflict,
            $"Tasarım siz okuduktan sonra değişti (beklenen sürüm {expected}, güncel {actual}). get_design ile yeniden okuyun.",
            new { design_id = designId, current_version = actual });
    }

    // ───────────────────────── Yardımcılar ─────────────────────────

    private WorkingDesign GetWorking(DesignId id) =>
        Working.TryGet(id.Value, out var item) && item.Meta.Kind == id.Kind
            ? item
            : throw new ToolException(ToolErrorCodes.NotFound, $"Tasarım bulunamadı: {id}");

    private static bool IsOwner(WorkingDesign item, ToolCallContext context) =>
        string.IsNullOrEmpty(item.Meta.Owner) || string.Equals(item.Meta.Owner, context.ClientName, StringComparison.OrdinalIgnoreCase);

    public static AssetStore CopyAssets(AssetStore source)
    {
        var copy = new AssetStore();
        foreach (var asset in source.Assets) copy.AddExisting(asset);
        return copy;
    }

    internal static ProjectLoadResult LoadProject(string path)
    {
        try
        {
            return ProjectFile.Load(path);
        }
        catch (ProjectFileException ex)
        {
            throw new ToolException(ToolErrorCodes.InvalidArgument, $"Tasarım dosyası okunamadı: {ex.Message}");
        }
    }

    internal static void SaveProject(LabelProject project, string path)
    {
        byte[]? preview = null;
        try
        {
            using var renderer = new LabelRenderer(project.Assets);
            var result = renderer.RenderLabel(project.Document, new RenderOptions { DotsPerMm = 4 });
            preview = MonoImageExport.EncodePng(result.Bitmap, 4 * 25.4);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Önizleme oluşturulamazsa dosya yine kaydedilir.
        }
        try
        {
            ProjectFile.Save(project, path, preview);
        }
        catch (ProjectFileException ex)
        {
            throw new ToolException(ToolErrorCodes.Unavailable, ex.Message);
        }
    }

    /// <summary>Önizleme alan değerleri: ürün → veri satırı → masaüstünün önizleme satırı → yerleşik alanlar.</summary>
    internal IFieldValueSource? PreviewFieldsFor(LabelDocument doc, string? productKey, int? row, IFieldValueSource? hostFields, out string? source)
    {
        source = null;
        var sources = new List<IFieldValueSource?>();
        if (productKey is { Length: > 0 } && Products.Find(productKey) is { } product)
        {
            sources.Add(product.ToFieldSource(Products.Data.Columns));
            source = $"Ürün {product.Key}";
        }
        if (row is { } r && TryGetDataTable(doc, out var table) && table is not null)
        {
            var record = table.Rows.FirstOrDefault(x => x.RowNumber == r) ?? (r >= 1 && r <= table.Rows.Count ? table.Rows[r - 1] : null);
            if (record is not null)
            {
                sources.Add(record.ToFieldSource());
                source ??= $"Veri satırı {record.RowNumber}";
            }
        }
        if (hostFields is not null)
        {
            sources.Add(hostFields);
            source ??= "Uygulamadaki önizleme satırı";
        }
        if (source is null && doc.HasFieldPlaceholders && TryGetDataTable(doc, out var first) && first is { Rows.Count: > 0 })
        {
            sources.Add(first.Rows[0].ToFieldSource());
            source = $"Veri satırı {first.Rows[0].RowNumber}";
        }
        sources.Add(_options.ExtraPreviewFields?.Invoke());
        sources.Add(Gs1.PreviewFields);
        sources.Add(new BuiltInFieldSource(1, DateTime.Now));
        return new CompositeFieldSource(sources.ToArray());
    }

    /// <summary>Belgenin bağlı veri kaynağını (izinli klasördeyse) yükler.</summary>
    internal bool TryGetDataTable(LabelDocument doc, out DataSourceTable? table)
    {
        table = null;
        if (doc.DataSource is not { FilePath.Length: > 0 } link) return false;
        try
        {
            string path = Guard.ResolveExistingFile(link.FilePath, GuardedFileKind.Data);
            table = DataCache.Load(path, link.SheetName);
            return true;
        }
        catch (ToolException)
        {
            return false;
        }
    }

    /// <summary>Satıra göre değişen görseller için izinli klasörlerle sınırlı çözücü.</summary>
    public IImageSourceResolver ImageResolverFor(LabelDocument doc) =>
        new GuardedImageResolver(Guard, doc.DataSource?.FilePath);
}
