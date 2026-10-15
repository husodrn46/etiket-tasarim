using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Geometry;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;
using AkelEtiket.Editor.Services;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;
using CommunityToolkit.Mvvm.ComponentModel;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.Documents;

/// <summary>Editörün platform servisleri.</summary>
public sealed record EditorContext(IUiDispatcher Dispatcher, ILayerImageFactory ImageFactory);

public enum ZOrderChange
{
    BringToFront,
    BringForward,
    SendBackward,
    SendToBack,
}

/// <summary>
/// Açık belge oturumu: model değişikliklerini izler (geri al, kaydedilmedi işareti), öğeleri arka planda
/// yazıcı çözünürlüğünde render eder, seçimi ve öğe işlemlerini yönetir. Arayüzden bağımsızdır.
/// </summary>
public sealed partial class DocumentSession : ObservableObject, IDisposable
{
    private readonly EditorContext _context;
    private readonly RenderCache _cache = new();
    private readonly Dictionary<LabelElement, ElementViewModel> _byModel = new(ReferenceEqualityComparer.Instance);
    private string _committedState;
    private string _savedState;
    private int _suppressTracking;
    private int _transactionDepth;
    private string? _transactionBefore;
    private bool _transactionChanged;
    private string? _transactionDescription;
    private string? _transactionFallbackDescription;
    private bool _disposed;
    private static int s_sessionCounter;

    public DocumentSession(LabelProject project, EditorContext context, double dotsPerMm)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        DotsPerMm = dotsPerMm;
        Renderer = new LabelRenderer(project.Assets);

        foreach (var element in Document.Elements) Elements.Add(CreateViewModel(element));
        Document.Elements.CollectionChanged += OnElementsCollectionChanged;
        Document.Label.PropertyChanged += OnLabelPropertyChanged;
        Document.Guides.CollectionChanged += OnGuidesChanged;
        foreach (var guide in Document.Guides) guide.PropertyChanged += OnGuidePropertyChanged;
        Document.PropertyChanged += OnDocumentPropertyChanged;

        _committedState = _savedState = Serialize();
        History.StateChanged += (_, _) => OnPropertyChanged(nameof(CanUndo));
        History.StateChanged += (_, _) => OnPropertyChanged(nameof(CanRedo));
        RenderAll();
    }

    public LabelProject Project { get; }
    public LabelDocument Document => Project.Document;
    public AssetStore Assets => Project.Assets;
    public LabelRenderer Renderer { get; }
    public UndoManager History { get; } = new();

    /// <summary>Uygulama içinde bu oturumun sıra numarası (yapay zekâ araçları için "acik:N" kimliği).</summary>
    public int SessionNumber { get; } = Interlocked.Increment(ref s_sessionCounter);

    /// <summary>Belgede her kayda geçen değişiklikte (geri al/ileri al dahil) artar.</summary>
    public long Revision { get; private set; }

    /// <summary>Sürüm etiketi: yapay zekâ araçları değişiklikten önce bu değeri karşılaştırır.</summary>
    public string Version => $"s{SessionNumber}-r{Revision}";

    /// <summary>Tuvaldeki öğeler (belgedeki z-sırasıyla; ilk öğe en altta).</summary>
    public ObservableCollection<ElementViewModel> Elements { get; } = [];

    public ObservableCollection<ElementViewModel> SelectedElements { get; } = [];

    [ObservableProperty]
    public partial ElementViewModel? PrimarySelection { get; private set; }

    [ObservableProperty]
    public partial bool IsDirty { get; private set; }

    /// <summary>Ekran/baskı render çözünürlüğü (nokta/mm).</summary>
    public double DotsPerMm { get; private set; }

    /// <summary>Önizleme için değişken alan değerleri (seçili veri satırı + yerleşik alanlar).</summary>
    public IFieldValueSource? Fields { get; private set; }

    public bool CanUndo => History.CanUndo;
    public bool CanRedo => History.CanRedo;

    public string Title => (Project.FilePath is null ? Document.Name : Path.GetFileNameWithoutExtension(Project.FilePath)) + (IsDirty ? " •" : "");

    public event EventHandler? SelectionChanged;
    public event EventHandler? LabelSetupChanged;
    public event EventHandler? GuidesChanged;

    /// <summary>Belgede herhangi bir değişiklik (otomatik kurtarma için).</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<LabelElement> SelectedModels => SelectedElements.Select(e => e.Model).ToList();

    public ElementViewModel? GetViewModel(LabelElement model) => _byModel.GetValueOrDefault(model);

    // ───────────────────────── Seçim ─────────────────────────

    public void Select(ElementViewModel? vm, bool add = false, bool toggle = false)
    {
        if (vm is null)
        {
            if (!add) ClearSelection();
            return;
        }
        // Gruplu öğe tıklanınca bütün grup seçilir (birlikte taşınır/hizalanır).
        var members = GroupMembers(vm);
        if (toggle && vm.IsSelected)
        {
            foreach (var member in members) SetSelected(member, false);
        }
        else
        {
            if (!add && !toggle)
            {
                foreach (var other in SelectedElements.ToList())
                    if (!members.Contains(other)) SetSelected(other, false);
            }
            foreach (var member in members) SetSelected(member, true);
            // Tıklanan öğe birincil seçim olsun.
            if (members.Count > 1)
            {
                SelectedElements.Remove(vm);
                SelectedElements.Add(vm);
            }
        }
        UpdatePrimary();
    }

    private List<ElementViewModel> GroupMembers(ElementViewModel vm) =>
        vm.Model.GroupId is { } groupId
            ? Elements.Where(e => e.Model.GroupId == groupId).ToList()
            : [vm];

    /// <summary>Seçili öğeleri gruplar (en az 2). Tek geri alma adımı.</summary>
    public bool GroupSelection()
    {
        var models = SelectedModels;
        if (models.Count < 2 || models.Any(m => m.Locked)) return false;
        string groupId = "g" + Guid.NewGuid().ToString("N")[..8];
        using (BeginTransaction(L10n.T("Gruplandı")))
        {
            foreach (var model in models) model.GroupId = groupId;
        }
        return true;
    }

    /// <summary>Seçili öğelerin gruplarını çözer. Tek geri alma adımı.</summary>
    public bool UngroupSelection()
    {
        var groups = SelectedModels.Select(m => m.GroupId).Where(g => g is not null).ToHashSet();
        if (groups.Count == 0) return false;
        using (BeginTransaction(L10n.T("Grup çözüldü")))
        {
            foreach (var model in Document.Elements.Where(e => e.GroupId is { } g && groups.Contains(g))) model.GroupId = null;
        }
        return true;
    }

    public bool SelectionHasGroup => SelectedElements.Any(e => e.Model.GroupId is not null);

    public void SelectMany(IEnumerable<ElementViewModel> items, bool add = false)
    {
        if (!add) foreach (var other in SelectedElements.ToList()) SetSelected(other, false);
        foreach (var vm in items) SetSelected(vm, true);
        UpdatePrimary();
    }

    public void SelectAll() => SelectMany(Elements.Where(e => e.Model.Visible));

    public void ClearSelection()
    {
        foreach (var vm in SelectedElements.ToList()) SetSelected(vm, false);
        UpdatePrimary();
    }

    public void SelectByIds(IEnumerable<string> ids)
    {
        var set = ids.ToHashSet();
        SelectMany(Elements.Where(e => set.Contains(e.Id)));
    }

    private void SetSelected(ElementViewModel vm, bool selected)
    {
        if (vm.IsSelected == selected && SelectedElements.Contains(vm) == selected) return;
        vm.IsSelected = selected;
        if (selected) SelectedElements.Add(vm);
        else SelectedElements.Remove(vm);
    }

    private void UpdatePrimary()
    {
        PrimarySelection = SelectedElements.Count > 0 ? SelectedElements[^1] : null;
        History.BreakCoalescing();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Noktadaki en üstteki görünür öğe.</summary>
    public ElementViewModel? HitTest(double xMm, double yMm, double toleranceMm = 0)
    {
        for (int i = Elements.Count - 1; i >= 0; i--)
        {
            var vm = Elements[i];
            if (!vm.Model.Visible) continue;
            if (ElementGeometry.HitTest(vm.Model, xMm, yMm, toleranceMm)) return vm;
        }
        return null;
    }

    /// <summary>Dikdörtgenle kesişen görünür öğeler (lastik bant seçimi).</summary>
    public IEnumerable<ElementViewModel> ElementsInRect(RectMm rect) =>
        Elements.Where(e => e.Model.Visible && rect.IntersectsWith(ElementGeometry.GetRotatedBoundsMm(e.Model)));

    // ───────────────────────── Değişiklik takibi / geri al ─────────────────────────

    private string Serialize() => ModelJson.Serialize(Document, indented: false);

    /// <summary>
    /// Birden çok değişikliği tek geri al adımında toplar (sürükleme, hizalama...).
    /// İç içe çağrılabilir; en dıştaki kapanınca kayıt yapılır.
    /// </summary>
    public IDisposable BeginTransaction() => BeginTransaction(null);

    /// <summary>
    /// <see cref="BeginTransaction()"/> ile aynı; en dıştaki işlemin <paramref name="description"/> açıklaması
    /// geri al kaydına yazılır (ör. "Yapay zekâ: logo taşındı").
    /// </summary>
    public IDisposable BeginTransaction(string? description)
    {
        if (_transactionDepth++ == 0)
        {
            _transactionBefore = _committedState;
            _transactionChanged = false;
            _transactionDescription = description;
            _transactionFallbackDescription = null;
        }
        return new TransactionScope(this);
    }

    private void EndTransaction()
    {
        if (--_transactionDepth > 0) return;
        if (!_transactionChanged) return;
        string now = Serialize();
        if (now != _transactionBefore)
        {
            History.Record(_transactionBefore!, description: _transactionDescription ?? _transactionFallbackDescription);
            _committedState = now;
            Revision++;
            UpdateDirty();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        _transactionBefore = null;
        _transactionDescription = null;
        _transactionFallbackDescription = null;
    }

    private sealed class TransactionScope(DocumentSession session) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            session.EndTransaction();
        }
    }

    private void TrackChange(string? coalesceKey, string? description = null)
    {
        if (_suppressTracking > 0) return;
        if (_transactionDepth > 0)
        {
            _transactionChanged = true;
            _transactionFallbackDescription ??= description;
            return;
        }
        string now = Serialize();
        if (now == _committedState) return;
        History.Record(_committedState, coalesceKey, description: description);
        _committedState = now;
        Revision++;
        UpdateDirty();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateDirty()
    {
        IsDirty = _committedState != _savedState;
        OnPropertyChanged(nameof(Title));
    }

    public void Undo()
    {
        if (_transactionDepth > 0) return;
        string? state = History.Undo(_committedState);
        if (state is not null) Restore(state);
    }

    public void Redo()
    {
        if (_transactionDepth > 0) return;
        string? state = History.Redo(_committedState);
        if (state is not null) Restore(state);
    }

    /// <summary>Geri alma geçmişi (en yeni önce): açıklama, zaman ve o noktaya dönmek için geri alınacak adım sayısı.</summary>
    public IReadOnlyList<UndoHistoryItem> GetUndoHistory(int max = 40)
    {
        var entries = History.UndoEntries;
        var items = new List<UndoHistoryItem>();
        for (int i = entries.Count - 1, steps = 1; i >= 0 && items.Count < max; i--, steps++)
            items.Add(new UndoHistoryItem(entries[i].Description ?? L10n.T("Değişiklik"), entries[i].Time.ToLocalTime(), steps));
        return items;
    }

    /// <summary>Belirtilen sayıda adımı birden geri alır (geçmiş listesinden bir noktaya dönmek için).</summary>
    public int UndoSteps(int steps)
    {
        int done = 0;
        while (done < steps && CanUndo && _transactionDepth == 0)
        {
            Undo();
            done++;
        }
        return done;
    }

    private void Restore(string json)
    {
        var selectedIds = SelectedElements.Select(e => e.Id).ToList();
        var restored = ModelJson.DeserializeDocument(json);
        _suppressTracking++;
        try
        {
            CopyContentFrom(restored);
        }
        finally
        {
            _suppressTracking--;
        }
        _committedState = json;
        Revision++;
        UpdateDirty();
        SelectByIds(selectedIds);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Belge içeriğini (ad, açıklama, etiket ölçüsü, öğeler, kılavuzlar) başka bir belgeden alır.</summary>
    private void CopyContentFrom(LabelDocument source)
    {
        Document.Name = source.Name;
        Document.Description = source.Description;
        var label = Document.Label;
        label.WidthMm = source.Label.WidthMm;
        label.HeightMm = source.Label.HeightMm;
        label.GapMm = source.Label.GapMm;
        label.GapOffsetMm = source.Label.GapOffsetMm;
        label.Media = source.Label.Media;
        label.CornerRadiusMm = source.Label.CornerRadiusMm;
        Document.Elements.Clear();
        foreach (var element in source.Elements) Document.Elements.Add(element);
        Document.Guides.Clear();
        foreach (var guide in source.Guides) Document.Guides.Add(guide);
        Document.DataSource = source.DataSource;
    }

    /// <summary>
    /// Belgenin içeriğini <paramref name="source"/> ile değiştirir ve bunu <b>tek</b> bir geri alınabilir adım olarak
    /// kaydeder (ör. onaylanan yapay zekâ önerisi). Kaynaktaki görseller projeye eklenir. Kaynak belge nesnesi
    /// kopyalanarak kullanılır; çağıran onu değiştirmeye devam edebilir.
    /// </summary>
    public void ReplaceContent(LabelDocument source, IEnumerable<ImageAsset>? assets, string description)
    {
        ArgumentNullException.ThrowIfNull(source);
        var copy = ModelJson.Clone(source);
        if (assets is not null)
        {
            foreach (var asset in assets)
            {
                if (!Assets.Contains(asset.Id)) Assets.AddExisting(asset);
            }
        }
        var selectedIds = SelectedElements.Select(e => e.Id).ToList();
        using (BeginTransaction(description))
        {
            CopyContentFrom(copy);
        }
        SelectByIds(selectedIds);
    }

    /// <summary>Kaydedildikten sonra çağrılır.</summary>
    public void MarkSaved()
    {
        _committedState = _savedState = Serialize();
        UpdateDirty();
        OnPropertyChanged(nameof(Title));
    }

    private void OnElementsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncViewModels();
        string? description = e.Action switch
        {
            NotifyCollectionChangedAction.Add when e.NewItems is { Count: > 0 } added && added[0] is LabelElement first =>
                L10n.T($"{first.KindName} eklendi"),
            NotifyCollectionChangedAction.Remove when e.OldItems is { Count: > 0 } removed && removed[0] is LabelElement gone =>
                L10n.T($"{gone.DisplayName} silindi"),
            NotifyCollectionChangedAction.Move => L10n.T("Katman sırası değişti"),
            _ => null,
        };
        TrackChange(null, description);
    }

    private void SyncViewModels()
    {
        var desired = Document.Elements.ToList();
        var desiredSet = new HashSet<LabelElement>(desired, ReferenceEqualityComparer.Instance);
        bool selectionChanged = false;
        foreach (var vm in Elements.ToList())
        {
            if (desiredSet.Contains(vm.Model)) continue;
            vm.Model.PropertyChanged -= OnElementPropertyChanged;
            _byModel.Remove(vm.Model);
            Elements.Remove(vm);
            if (vm.IsSelected)
            {
                SetSelected(vm, false);
                selectionChanged = true;
            }
        }
        for (int i = 0; i < desired.Count; i++)
        {
            var model = desired[i];
            if (!_byModel.TryGetValue(model, out var vm))
            {
                vm = CreateViewModel(model);
                Elements.Insert(i, vm);
                RequestRender(vm);
                continue;
            }
            int current = Elements.IndexOf(vm);
            if (current != i) Elements.Move(current, i);
        }
        if (selectionChanged) UpdatePrimary();
    }

    private ElementViewModel CreateViewModel(LabelElement model)
    {
        var vm = new ElementViewModel(model, this);
        _byModel[model] = vm;
        model.PropertyChanged += OnElementPropertyChanged;
        return vm;
    }

    private void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not LabelElement model || !_byModel.TryGetValue(model, out var vm)) return;
        vm.RaiseModelDisplay(e);
        switch (e.PropertyName)
        {
            case nameof(LabelElement.X):
            case nameof(LabelElement.Y):
                vm.RaiseGeometry();
                break;
            case nameof(LabelElement.Name):
            case nameof(LabelElement.Locked):
            case nameof(LabelElement.Visible):
            case nameof(LabelElement.DisplayName):
            case nameof(LabelElement.KindName):
            case nameof(LabelElement.HasFieldPlaceholders):
            case nameof(BarcodeElement.Is2D):
                break;
            default:
                RequestRender(vm);
                break;
        }
        TrackChange($"{model.Id}:{e.PropertyName}", DescribeChange(model, e.PropertyName));
    }

    /// <summary>Geri alma geçmişinde görünecek kısa açıklama (ör. "Ürün adı: yazı boyutu").</summary>
    private static string DescribeChange(LabelElement model, string? propertyName)
    {
        string what = propertyName switch
        {
            nameof(LabelElement.X) or nameof(LabelElement.Y) => L10n.T("taşındı"),
            nameof(LabelElement.Width) or nameof(LabelElement.Height) => L10n.T("boyutlandırıldı"),
            nameof(LabelElement.Rotation) => L10n.T("döndürüldü"),
            nameof(LabelElement.Locked) => L10n.T("kilit"),
            nameof(LabelElement.Visible) => L10n.T("görünürlük"),
            nameof(LabelElement.Name) => L10n.T("ad"),
            nameof(LabelElement.GroupId) => L10n.T("grup"),
            nameof(TextElement.Text) => L10n.T("metin"),
            nameof(BarcodeElement.Content) => L10n.T("içerik"),
            nameof(BarcodeElement.Symbology) => L10n.T("barkod türü"),
            nameof(TextElement.FontSize) => L10n.T("yazı boyutu"),
            nameof(TextElement.FontFamily) => L10n.T("yazı tipi"),
            nameof(TextElement.Bold) or nameof(TextElement.Italic) or nameof(TextElement.Underline) => L10n.T("yazı stili"),
            nameof(TextElement.HorizontalAlignment) or nameof(TextElement.VerticalAlignment) => L10n.T("hizalama"),
            nameof(ImageElement.Method) or nameof(ImageElement.Threshold) or nameof(ImageElement.Brightness)
                or nameof(ImageElement.Contrast) or nameof(ImageElement.Invert) or nameof(ImageElement.Sharpen) => L10n.T("siyah-beyaz ayarı"),
            nameof(ImageElement.CropLeft) or nameof(ImageElement.CropTop) or nameof(ImageElement.CropRight)
                or nameof(ImageElement.CropBottom) => L10n.T("kırpma"),
            _ => L10n.T("ayar"),
        };
        return $"{model.DisplayName}: {what}";
    }

    private void OnLabelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        LabelSetupChanged?.Invoke(this, EventArgs.Empty);
        TrackChange($"label:{e.PropertyName}", L10n.T("Etiket ölçüsü/medya"));
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LabelDocument.Name) or nameof(LabelDocument.Description))
        {
            OnPropertyChanged(nameof(Title));
            TrackChange($"doc:{e.PropertyName}");
        }
    }

    private void OnGuidesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null) foreach (GuideLine g in e.NewItems) g.PropertyChanged += OnGuidePropertyChanged;
        if (e.OldItems is not null) foreach (GuideLine g in e.OldItems) g.PropertyChanged -= OnGuidePropertyChanged;
        if (e.Action == NotifyCollectionChangedAction.Reset)
            foreach (var g in Document.Guides) { g.PropertyChanged -= OnGuidePropertyChanged; g.PropertyChanged += OnGuidePropertyChanged; }
        GuidesChanged?.Invoke(this, EventArgs.Empty);
        TrackChange(null, L10n.T("Kılavuz çizgisi"));
    }

    private void OnGuidePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        GuidesChanged?.Invoke(this, EventArgs.Empty);
        TrackChange(sender is null ? null : $"guide:{sender.GetHashCode()}", L10n.T("Kılavuz çizgisi taşındı"));
    }

    // ───────────────────────── Render ─────────────────────────

    /// <summary>Önizleme alanlarını değiştirir (veri satırı seçimi); etkilenen öğeler yeniden çizilir.</summary>
    public void SetFields(IFieldValueSource? fields)
    {
        Fields = fields;
        foreach (var vm in Elements) RequestRender(vm);
    }

    /// <summary>Satıra göre değişen görsellerin (ImageElement.SourceField) aranacağı klasörler.</summary>
    public IImageSourceResolver? ImageResolver { get; private set; }

    public void SetImageResolver(IImageSourceResolver? resolver)
    {
        ImageResolver = resolver;
        foreach (var vm in Elements.Where(e => e.Model is ImageElement { SourceField: not null })) RequestRender(vm);
    }

    public void SetDotsPerMm(double dotsPerMm)
    {
        if (Math.Abs(dotsPerMm - DotsPerMm) < 1e-9) return;
        DotsPerMm = dotsPerMm;
        _cache.Clear();
        RenderAll();
    }

    public void RenderAll()
    {
        foreach (var vm in Elements) RequestRender(vm);
    }

    /// <summary>Öğeyi (gerekirse) arka planda yeniden çizer. Aynı öğe için en son istek geçerlidir.</summary>
    public void RequestRender(ElementViewModel vm)
    {
        if (_disposed) return;
        var fields = Fields;
        double dpmm = DotsPerMm;
        string key = RenderCache.ComputeKey(vm.Model, fields, dpmm);
        if (key == vm.RenderedKey && !vm.IsRendering)
        {
            vm.RaiseGeometry();
            return;
        }
        if (_cache.TryGet(key, out var cached) && cached is not null)
        {
            vm.RequestedVersion++;
            vm.ApplyRender(new RenderedElementSnapshot(key, cached.Result, cached.Image));
            return;
        }
        if (vm.IsRendering)
        {
            vm.PendingRender = true;
            return;
        }

        var snapshot = ModelJson.CloneElement(vm.Model, newId: false);
        int version = ++vm.RequestedVersion;
        vm.IsRendering = true;
        var options = new RenderOptions { DotsPerMm = dpmm, Fields = fields, Mode = RenderMode.Design, ImageResolver = ImageResolver };
        var renderer = Renderer;
        var factory = _context.ImageFactory;
        Task.Run(() =>
        {
            RenderedElement rendered;
            try
            {
                var result = renderer.RenderElement(snapshot, options);
                object? image = result.Layer is { } layer
                    ? factory.Create(MonoImageExport.ToBgra32(layer.Ink, layer.Mask), layer.Width, layer.Height)
                    : null;
                rendered = new RenderedElement(result, image);
                _cache.Add(key, rendered);
            }
            catch (Exception ex)
            {
                rendered = new RenderedElement(new ElementRenderResult
                {
                    ElementId = snapshot.Id,
                    Issues = [new RenderIssue(snapshot.Id, snapshot.DisplayName, RenderIssueSeverity.Error, L10n.T("Çizim hatası: ") + ex.Message)],
                }, null);
            }
            _context.Dispatcher.Post(() =>
            {
                vm.IsRendering = false;
                if (_disposed) return;
                if (vm.RequestedVersion == version) vm.ApplyRender(new RenderedElementSnapshot(key, rendered.Result, rendered.Image));
                if (vm.PendingRender)
                {
                    vm.PendingRender = false;
                    RequestRender(vm);
                }
            });
        });
    }

    /// <summary>Tüm etiketi eşzamanlı render eder (önizleme, PNG, baskı doğrulaması).</summary>
    public LabelRenderResult RenderLabel(RenderMode mode, IFieldValueSource? fields, double? dotsPerMm = null) =>
        Renderer.RenderLabel(Document, new RenderOptions { DotsPerMm = dotsPerMm ?? DotsPerMm, Fields = fields, Mode = mode, ImageResolver = ImageResolver });

    /// <summary>Seçili barkodların okunabilirlik kontrolü (render edilmiş gerçek 1-bit katman çözülür).</summary>
    public void VerifyBarcode(ElementViewModel vm)
    {
        if (vm.Model is not BarcodeElement barcode) return;
        var result = Renderer.RenderElement(barcode, new RenderOptions { DotsPerMm = DotsPerMm, Fields = Fields, Mode = RenderMode.Design });
        if (result.IsPlaceholder)
        {
            vm.VerificationSuccess = null;
            vm.VerificationMessage = L10n.T("Değişken içerikli barkod: veri kaynağı yüklenince her satır baskı öncesi ayrıca doğrulanır.");
            return;
        }
        if (result.Layer is null || result.Barcode is null || !result.Barcode.IsValid)
        {
            vm.VerificationSuccess = false;
            vm.VerificationMessage = result.Issues.FirstOrDefault(i => i.Severity == RenderIssueSeverity.Error)?.Message ?? L10n.T("Barkod oluşturulamadı.");
            return;
        }
        var verification = BarcodeVerifier.Verify(result.Layer.Ink, barcode.Symbology, result.Barcode.VerificationText);
        vm.VerificationSuccess = verification.Success;
        vm.VerificationMessage = verification.Message;
    }

    // ───────────────────────── Öğe işlemleri ─────────────────────────

    public ElementViewModel AddElement(LabelElement element, bool select = true)
    {
        Document.Elements.Add(element);
        var vm = _byModel[element];
        if (select) Select(vm);
        return vm;
    }

    /// <summary>Öğeyi etiketin ortasına yerleştirip ekler.</summary>
    public ElementViewModel AddCentered(LabelElement element)
    {
        element.X = Math.Round(Math.Max(0, (Document.Label.WidthMm - element.Width) / 2), 2);
        element.Y = Math.Round(Math.Max(0, (Document.Label.HeightMm - element.Height) / 2), 2);
        return AddElement(element);
    }

    public void DeleteSelection()
    {
        var targets = SelectedElements.Where(e => !e.Model.Locked).Select(e => e.Model).ToList();
        if (targets.Count == 0) return;
        using (BeginTransaction(targets.Count == 1 ? L10n.T($"{targets[0].DisplayName} silindi") : L10n.T($"{targets.Count} öğe silindi")))
            foreach (var model in targets) Document.Elements.Remove(model);
    }

    public void DuplicateSelection(double offsetMm = 2)
    {
        var sources = SelectedElements.Select(e => e.Model).ToList();
        if (sources.Count == 0) return;
        var copies = new List<LabelElement>();
        var groupMap = new Dictionary<string, string>(StringComparer.Ordinal);
        using (BeginTransaction(sources.Count == 1 ? L10n.T($"{sources[0].DisplayName} çoğaltıldı") : L10n.T($"{sources.Count} öğe çoğaltıldı")))
        {
            foreach (var source in sources)
            {
                var copy = ModelJson.CloneElement(source, newId: true);
                copy.X += offsetMm;
                copy.Y += offsetMm;
                copy.Locked = false;
                copy.GroupId = RemapGroup(copy.GroupId, groupMap);
                Document.Elements.Add(copy);
                copies.Add(copy);
            }
        }
        SelectMany(copies.Select(c => _byModel[c]));
    }

    public void MoveSelection(double dxMm, double dyMm)
    {
        var targets = SelectedElements.Where(e => !e.Model.Locked).Select(e => e.Model).ToList();
        if (targets.Count == 0) return;
        using (BeginTransaction(targets.Count == 1 ? L10n.T($"{targets[0].DisplayName} taşındı") : L10n.T($"{targets.Count} öğe taşındı")))
        {
            foreach (var model in targets)
            {
                model.X = Math.Round(model.X + dxMm, 4);
                model.Y = Math.Round(model.Y + dyMm, 4);
            }
        }
    }

    public void AlignSelection(AlignMode mode)
    {
        var models = SelectedModels;
        if (models.Count == 0) return;
        using (BeginTransaction(mode is AlignMode.DistributeHorizontal or AlignMode.DistributeVertical ? L10n.T("Eşit dağıtıldı") : L10n.T("Hizalandı")))
            TransformMath.Align(models, mode, Document.Label);
    }

    public void ToggleLockSelection()
    {
        var models = SelectedModels;
        if (models.Count == 0) return;
        bool lockAll = models.Any(m => !m.Locked);
        using (BeginTransaction(lockAll ? L10n.T("Kilitlendi") : L10n.T("Kilit açıldı")))
            foreach (var model in models) model.Locked = lockAll;
    }

    public void ChangeZOrder(ZOrderChange change)
    {
        var selected = SelectedModels.ToHashSet(ReferenceEqualityComparer.Instance);
        if (selected.Count == 0) return;
        var list = Document.Elements.ToList();
        List<LabelElement> reordered;
        switch (change)
        {
            case ZOrderChange.BringToFront:
                reordered = list.Where(e => !selected.Contains(e)).Concat(list.Where(selected.Contains)).ToList();
                break;
            case ZOrderChange.SendToBack:
                reordered = list.Where(selected.Contains).Concat(list.Where(e => !selected.Contains(e))).ToList();
                break;
            case ZOrderChange.BringForward:
                reordered = [.. list];
                for (int i = reordered.Count - 2; i >= 0; i--)
                    if (selected.Contains(reordered[i]) && !selected.Contains(reordered[i + 1]))
                        (reordered[i], reordered[i + 1]) = (reordered[i + 1], reordered[i]);
                break;
            default:
                reordered = [.. list];
                for (int i = 1; i < reordered.Count; i++)
                    if (selected.Contains(reordered[i]) && !selected.Contains(reordered[i - 1]))
                        (reordered[i], reordered[i - 1]) = (reordered[i - 1], reordered[i]);
                break;
        }
        ApplyOrder(reordered);
    }

    /// <summary>Katmanlar panelinden sürükle-bırak ile sıra değişikliği (z-sırası indeksi).</summary>
    public void MoveElementToIndex(LabelElement model, int newIndex)
    {
        var list = Document.Elements.ToList();
        if (!list.Remove(model)) return;
        list.Insert(Math.Clamp(newIndex, 0, list.Count), model);
        ApplyOrder(list);
    }

    private void ApplyOrder(List<LabelElement> order)
    {
        if (order.SequenceEqual(Document.Elements)) return;
        using (BeginTransaction(L10n.T("Katman sırası değişti")))
        {
            for (int i = 0; i < order.Count; i++)
            {
                int current = Document.Elements.IndexOf(order[i]);
                if (current != i) Document.Elements.Move(current, i);
            }
        }
    }

    // ───────────────────────── Pano ─────────────────────────

    private sealed class ClipboardPayload
    {
        public string Format { get; set; } = "AkelEtiket.Elements.v1";
        public List<LabelElement> Elements { get; set; } = [];
        public List<ClipboardAsset> Assets { get; set; } = [];
    }

    private sealed class ClipboardAsset
    {
        public string Id { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Data { get; set; } = "";
    }

    /// <summary>Seçili öğeleri (kullandıkları görsellerle birlikte) pano verisine çevirir.</summary>
    public string? BuildClipboardPayload()
    {
        var models = SelectedModels;
        if (models.Count == 0) return null;
        var payload = new ClipboardPayload { Elements = models.ToList() };
        foreach (var id in models.OfType<ImageElement>().Select(i => i.AssetId).Distinct())
        {
            if (Assets.TryGet(id, out var asset))
                payload.Assets.Add(new ClipboardAsset { Id = asset.Id, FileName = asset.FileName, Data = Convert.ToBase64String(asset.Data) });
        }
        return JsonSerializer.Serialize(payload, ModelJson.CompactOptions);
    }

    /// <summary>Pano verisini yapıştırır (yeni kimliklerle, kaydırılmış). Başarısızsa false.</summary>
    public bool PastePayload(string json, double offsetMm)
    {
        ClipboardPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ClipboardPayload>(json, ModelJson.CompactOptions);
        }
        catch (JsonException)
        {
            return false;
        }
        if (payload?.Elements is not { Count: > 0 }) return false;
        foreach (var asset in payload.Assets ?? [])
        {
            if (!Assets.Contains(asset.Id))
                Assets.AddExisting(new ImageAsset(asset.Id, asset.FileName, Convert.FromBase64String(asset.Data)));
        }
        var added = new List<LabelElement>();
        var groupMap = new Dictionary<string, string>(StringComparer.Ordinal);
        using (BeginTransaction(L10n.T("Yapıştırıldı")))
        {
            foreach (var element in payload.Elements.Where(e => e is not null))
            {
                element.Id = LabelElement.NewId();
                element.X += offsetMm;
                element.Y += offsetMm;
                element.GroupId = RemapGroup(element.GroupId, groupMap);
                Document.Elements.Add(element);
                added.Add(element);
            }
        }
        SelectMany(added.Select(a => _byModel[a]));
        return true;
    }

    /// <summary>Kopyalanan gruplar yeni grup kimliği alır (kopyalar asıl grubun parçası olmasın).</summary>
    private static string? RemapGroup(string? groupId, Dictionary<string, string> map)
    {
        if (groupId is null) return null;
        if (!map.TryGetValue(groupId, out var mapped))
        {
            mapped = "g" + Guid.NewGuid().ToString("N")[..8];
            map[groupId] = mapped;
        }
        return mapped;
    }

    // ───────────────────────── Kılavuzlar ─────────────────────────

    public GuideLine AddGuide(GuideOrientation orientation, double positionMm)
    {
        var guide = new GuideLine { Orientation = orientation, Position = Math.Round(positionMm, 2) };
        Document.Guides.Add(guide);
        return guide;
    }

    public void RemoveGuide(GuideLine guide) => Document.Guides.Remove(guide);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Document.Elements.CollectionChanged -= OnElementsCollectionChanged;
        Document.Label.PropertyChanged -= OnLabelPropertyChanged;
        Document.Guides.CollectionChanged -= OnGuidesChanged;
        Document.PropertyChanged -= OnDocumentPropertyChanged;
        foreach (var vm in Elements) vm.Model.PropertyChanged -= OnElementPropertyChanged;
        foreach (var guide in Document.Guides) guide.PropertyChanged -= OnGuidePropertyChanged;
        _cache.Clear();
        Renderer.Dispose();
    }
}

/// <summary>Geri alma geçmişindeki bir satır.</summary>
/// <param name="Steps">Bu değişikliği de geri almak için kaç adım geri gidilmeli.</param>
public sealed record UndoHistoryItem(string Description, DateTime Time, int Steps);
