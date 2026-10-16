using System.Collections.ObjectModel;
using System.ComponentModel;
using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core.Automation;
using AkelEtiket.Core.Model;
using AkelEtiket.Design;
using AkelEtiket.Editor.Ai;
using AkelEtiket.Editor.Documents;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.Templates;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>Sol paneldeki sekmeler.</summary>
public enum LeftPanel
{
    Text,
    Barcode,
    QrCode,
    Image,
    Shapes,
    Templates,
    Data,
    Layers,
}

/// <summary>Uygulamanın platform servisleri ve klasörleri.</summary>
public sealed class AppServices
{
    public required IDialogService Dialogs { get; init; }
    public required IClipboardService Clipboard { get; init; }
    public required IUiDispatcher Dispatcher { get; init; }
    public required ILayerImageFactory ImageFactory { get; init; }
    public required JsonSettingsStore<AppSettings> SettingsStore { get; init; }
    public AppSettings? InitialSettings { get; init; }
    public required string RecoveryFolder { get; init; }

    /// <summary>Örnek şablonlar için logo (PNG baytları).</summary>
    public Func<byte[]?> LogoProvider { get; init; } = () => null;

    /// <summary>Dosyayı Geri Dönüşüm Kutusu'na gönderir (yoksa kalıcı silinir).</summary>
    public Action<string>? RecycleFile { get; init; }

    /// <summary>Klasörü Windows Gezgini'nde açar.</summary>
    public Action<string>? OpenFolder { get; init; }

    /// <summary>Uygulama sürümü.</summary>
    public string AppVersion { get; init; } = "2.1.0";

    /// <summary>Bu açılışta v1'in ayarları devralındı (<see cref="LegacySettingsImport"/>).</summary>
    public bool ImportedLegacySettings { get; init; }
}

/// <summary>Ana pencerenin görünüm modeli.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public const double MinZoom = 0.25;
    public const double MaxZoom = 16;

    private readonly AppServices _services;
    private readonly AutoRecoveryService _initialRecovery;
    private AutoRecoveryService _recovery => ActiveDocument?.Recovery ?? _initialRecovery;
    private bool _recoveryDirty
    {
        get => ActiveDocument?.RecoveryDirty ?? false;
        set { if (ActiveDocument is not null) ActiveDocument.RecoveryDirty = value; }
    }
    private bool _disposed;

    public MainViewModel(AppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        Settings = services.InitialSettings ?? services.SettingsStore.Load();
        Settings.Normalize();
        _initialRecovery = new AutoRecoveryService(services.RecoveryFolder);
        if (Settings.Personalization.RememberPanelLayout)
        {
            ActivePanel = Enum.Parse<LeftPanel>(Settings.Personalization.ActivePanel);
            IsPanelOpen = Settings.Personalization.IsPanelOpen; IsAiPanelOpen = Settings.Personalization.IsAiPanelOpen;
        }
        Data = new DataSourceViewModel();
        Templates = new TemplatesViewModel(this);
        foreach (var path in Settings.Recent.Items) RecentFiles.Add(path);
        // {SAYAC} (belgenin kendi sayacı) açık belgenin adını kullanır.
        Printing.CounterIntegration.DocumentName = () => Session?.Document.Name;
        Session = CreateSession(LabelProject.CreateBlank(Settings.DefaultLabelWidthMm, Settings.DefaultLabelHeightMm));
        InitializeDocuments();

        // Yapay zekâ: tasarım servisi (araçlar), açık belge köprüsü, yerel API bağlantısı ve panel.
        AiSettings = new AiSettingsMonitor();
        var designService = new DesignService(new DesignServiceOptions
        {
            SettingsProvider = () => AiSettings.Current,
            LogoProvider = () => Settings.Company.LogoPng ?? services.LogoProvider(),
        });
        var liveHost = new DesktopLiveDocumentHost(this, services.Dispatcher);
        designService.LiveHost = liveHost;
        var tools = new AkelEtiket.Design.Tools.DesignToolset(designService);
        var connection = new AiConnectionService(tools.Registry, AiSettings, services.AppVersion);
        Ai = new AiPanelViewModel(this, services.Dispatcher, AiSettings, designService, liveHost, connection);
        liveHost.Refresh();
    }

    /// <summary>Yapay zekâ ayarları (dosya değişince yeniden okunur).</summary>
    public AiSettingsMonitor AiSettings { get; }

    /// <summary>Yapay Zekâ paneli: öneriler, uygulama içi asistan, bağlantı durumu.</summary>
    public AiPanelViewModel Ai { get; }

    /// <summary>Sağ sütunda Özellikler yerine Yapay Zekâ paneli gösterilsin mi?</summary>
    [ObservableProperty]
    public partial bool IsAiPanelOpen { get; set; }

    [RelayCommand]
    private void ToggleAiPanel() => IsAiPanelOpen = !IsAiPanelOpen;

    /// <summary>
    /// Satıra göre değişen görsellerin aranacağı klasörler: veri dosyasının klasörü ve onun "Görseller" alt klasörü,
    /// şablon klasörü ve yapay zekâ ayarlarındaki izinli klasörler.
    /// </summary>
    internal void UpdateImageResolver(DocumentSession? session = null)
    {
        var folders = new List<string>();
        string? dataFile = Data.Table?.SourcePath ?? (session ?? Session)?.Document.DataSource?.FilePath;
        if (!string.IsNullOrWhiteSpace(dataFile) && Path.GetDirectoryName(dataFile) is { } dataFolder)
        {
            folders.Add(Path.Combine(dataFolder, "Görseller"));
            folders.Add(dataFolder);
        }
        folders.Add(Settings.EffectiveTemplatesFolder);
        if (AiSettings is not null) folders.AddRange(AiSettings.Current.AllowedFolders);
        (session ?? Session)?.SetImageResolver(new Rendering.FolderImageResolver(folders.Where(Directory.Exists)));
    }

    /// <summary>Durum çubuğunda ileti gösterir.</summary>
    public void ShowStatus(string message) => StatusMessage = message;

    internal AppServices Services => _services;
    internal IDialogService Dialogs => _services.Dialogs;

    public AppSettings Settings { get; private set; }
    [ObservableProperty]
    public partial DataSourceViewModel Data { get; private set; }

    partial void OnDataChanged(DataSourceViewModel oldValue, DataSourceViewModel newValue)
    {
        if (oldValue is not null)
        {
            oldValue.TableChanged -= OnDataTableChanged;
            oldValue.ReloadFailed -= OnDataReloadFailed;
            oldValue.MappingRequested -= OnDataMappingRequested;
        }
        newValue.TableChanged += OnDataTableChanged;
        newValue.ReloadFailed += OnDataReloadFailed;
        newValue.MappingRequested += OnDataMappingRequested;
    }
    private void OnDataTableChanged(object? sender, EventArgs e) { UpdateImageResolver(); ValidateData(); }
    private void OnDataReloadFailed(object? sender, string message) => Dialogs.ShowMessage(L10n.T("Veri kaynağı"), message, MessageKind.Error);
    private void OnDataMappingRequested(object? sender, EventArgs e) => OpenColumnMappingCommand.Execute(null);
    public TemplatesViewModel Templates { get; }
    public ObservableCollection<string> RecentFiles { get; } = [];

    public IReadOnlyList<BarcodeTypeInfo> LinearBarcodeTypes { get; } = BarcodeCatalog.All.Where(t => !t.Is2D).ToList();
    public IReadOnlyList<BarcodeTypeInfo> MatrixBarcodeTypes { get; } = BarcodeCatalog.All.Where(t => t.Is2D).ToList();
    public IReadOnlyList<BarcodeTypeInfo> AllBarcodeTypes { get; } = BarcodeCatalog.All;
    public IReadOnlyList<string> FontFamilies => FontProvider.Default.Families;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle), nameof(LabelSizeText))]
    public partial DocumentSession Session { get; private set; }

    [ObservableProperty]
    public partial LeftPanel ActivePanel { get; set; } = LeftPanel.Text;

    [ObservableProperty]
    public partial bool IsPanelOpen { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomText))]
    public partial double Zoom { get; set; } = 2.0;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = L10n.T("Hazır");

    [ObservableProperty]
    public partial string CursorText { get; set; } = "";

    public string ZoomText => Zoom.ToString("P0", System.Globalization.CultureInfo.CurrentCulture);

    public string WindowTitle => $"{Session.Title} – {AkelEtiket.Core.Localization.UiText.T(AkelEtiket.Core.AppIdentity.ProductName)}";

    public string LabelSizeText =>
        $"{Session.Document.Label.WidthMm:0.##} × {Session.Document.Label.HeightMm:0.##} mm • {Settings.Printer.DotsPerMm * 25.4:0.#} DPI";

    public double DotsPerMm => Settings.Printer.DotsPerMm;

    /// <summary>Tema değişti (görünüm kaynak sözlüklerini değiştirir).</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>Görünüm "pencereye sığdır" yakınlaştırmasını hesaplamalı.</summary>
    public event EventHandler? FitRequested;

    /// <summary>Metin içeriği düzenleyicisine odaklanılmalı (çift tıklama).</summary>
    public event EventHandler? FocusContentEditorRequested;

    partial void OnActivePanelChanged(LeftPanel value)
    {
        IsPanelOpen = true;
        if (value == LeftPanel.Templates) Templates?.Refresh();
    }

    partial void OnZoomChanged(double value)
    {
        double clamped = Math.Clamp(value, MinZoom, MaxZoom);
        if (Math.Abs(clamped - value) > 1e-9) Zoom = clamped;
    }

    partial void OnSessionChanged(DocumentSession oldValue, DocumentSession newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnSessionPropertyChanged;
            oldValue.Changed -= OnSessionContentChanged;
            oldValue.SelectionChanged -= OnSelectionChanged;
            oldValue.LabelSetupChanged -= OnLabelSetupChanged;
            if (!Documents.Any(t => ReferenceEquals(t.Session,oldValue))) oldValue.Dispose();
        }
        newValue.PropertyChanged += OnSessionPropertyChanged;
        newValue.Changed += OnSessionContentChanged;
        newValue.SelectionChanged += OnSelectionChanged;
        newValue.LabelSetupChanged += OnLabelSetupChanged;
        Ai?.LiveHost.Refresh();
        UpdateImageResolver(newValue);
        ApplyPreviewFields();
        ValidateData();
        NotifyCommands();
        OnPropertyChanged(nameof(SelectedElement));
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentSession.Title) or nameof(DocumentSession.IsDirty))
        {
            OnPropertyChanged(nameof(WindowTitle));
            Ai?.LiveHost.Refresh();
        }
        if (e.PropertyName is nameof(DocumentSession.CanUndo) or nameof(DocumentSession.CanRedo))
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        }
        if (e.PropertyName == nameof(DocumentSession.PrimarySelection))
            OnPropertyChanged(nameof(SelectedElement));
    }

    private void OnSessionContentChanged(object? sender, EventArgs e)
    {
        _recoveryDirty = true;
        Ai?.LiveHost.Refresh();
        ValidateData();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        Ai?.LiveHost.Refresh();
        Ai?.OnSelectionChanged();
        OnPropertyChanged(nameof(SelectedElement));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasMultipleSelection));
        OnPropertyChanged(nameof(SelectionText));
        NotifyCommands();
    }

    private void OnLabelSetupChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(LabelSizeText));

    /// <summary>Özellik panelinin bağlandığı tek seçili öğe.</summary>
    public ElementViewModel? SelectedElement => Session.SelectedElements.Count == 1 ? Session.PrimarySelection : null;

    public bool HasSelection => Session.SelectedElements.Count > 0;
    public bool HasMultipleSelection => Session.SelectedElements.Count > 1;

    public string SelectionText => Session.SelectedElements.Count switch
    {
        0 => L10n.T("Seçim yok"),
        1 => Session.PrimarySelection!.DisplayName,
        int n => L10n.T($"{n} öğe seçili"),
    };

    private void NotifyCommands()
    {
        foreach (var command in new IRelayCommand[]
                 {
                     CutCommand, CopyCommand, DeleteCommand, DuplicateCommand, ToggleLockCommand,
                     BringToFrontCommand, SendToBackCommand, BringForwardCommand, SendBackwardCommand,
                     UndoCommand, RedoCommand, CopyStyleCommand, PasteStyleCommand,
                 })
            command.NotifyCanExecuteChanged();
        AlignCommand.NotifyCanExecuteChanged();
    }

    private DocumentSession CreateSession(LabelProject project) =>
        new(project, new EditorContext(_services.Dispatcher, _services.ImageFactory), Settings.Printer.DotsPerMm);

    /// <summary>Önizleme alanlarını (seçili veri satırı + {TARIH}, {SIRA}) oturuma uygular.</summary>
    private void ApplyPreviewFields() => Session.SetFields(Data.GetPreviewFields(DateTime.Now));

    private void ValidateData()
    {
        if (Data.HasData) Data.ValidateAgainst(Session.Document);
    }

    private void SetStatus(string message) => StatusMessage = message;

    // ───────────────────────── Başlangıç / kapanış ─────────────────────────

    /// <summary>Uygulama açılışı: kurtarma, ilk çalıştırma örnekleri, komut satırındaki dosya.</summary>
    public void Initialize(IReadOnlyList<string> args)
    {
        _recovery.Start();
        if (_services.SettingsStore.LastLoadProblem is { } problem)
            Dialogs.ShowMessage(L10n.T("Ayarlar"), problem, MessageKind.Warning);

        bool recovered = TryOfferRecovery();
        if (!Settings.FirstRunCompleted)
        {
            // Eski sürümde kurulumunu tamamlayan kullanıcılar yeniden kesintiye uğramaz.
            // Kurtarılan veya dışarıdan açılan bir tasarımın üzerine başlangıç tasarımı yazılmaz.
            if (!recovered && args.Count == 0) RunFirstSetup(firstRun: true);
            if (!_setupSaveFailed)
            {
                if (_setupAddTemplates) Templates.EnsureSamples(Settings.Company.LogoPng ?? _services.LogoProvider());
                Settings.FirstRunCompleted = true;
                SaveSettings();
            }
            if (!recovered)
            {
                var sample = InternationalSamples.CreateForSize(Settings.DefaultLabelWidthMm, Settings.DefaultLabelHeightMm, Settings.Company, L10n.Language);
                OpenDocument(sample);
                SetStatus(L10n.T("Hoş geldiniz! Etiketiniz hazır. Tasarımınızı oluşturabilir veya Şablonlar panelinden bir örnek seçebilirsiniz."));
            }
            if (_services.ImportedLegacySettings)
                SetStatus(L10n.T("AKEL Etiket (v1) ayarlarınız devralındı: yazıcı ayarları, son kullanılan dosyalar ve klasörler") +
                          (Templates.ImportedLegacyCount > 0 ? L10n.T($"; {Templates.ImportedLegacyCount} şablon kopyalandı") : "") + L10n.T(" (v1'deki asıllar değişmedi)."));
            else if (Templates.ImportedLegacyCount > 0)
                SetStatus(L10n.T($"AKEL Etiket (v1) şablonlarından {Templates.ImportedLegacyCount} tanesi kopyalandı (v1'deki asıllar değişmedi)."));
        }

        if (!recovered && args.Count == 0 && Settings.Personalization.StartupTemplatePath is { } startup)
        {
            try { OpenProjectAsNew(ProjectFile.Load(startup).Project, L10n.T("Başlangıç şablonu açıldı.")); }
            catch (Exception ex) when (ex is ProjectFileException or IOException or UnauthorizedAccessException)
            { Dialogs.ShowMessage(L10n.T("Başlangıç şablonu açılamadı"), ex.Message, MessageKind.Warning); }
        }
        var fileArgs = args.Where(File.Exists).ToList();
        if (fileArgs.Count > 0) HandleFileDrop(fileArgs, null, null);
        Templates.Refresh();
        RequestFit();

        if (_setupPreviewRequested)
        {
            _setupPreviewRequested = false;
            // Önizleme penceresi Yazdır'a ayrıca basılmadan hiçbir baskı başlatmaz.
            PreviewCommand.Execute(null);
        }

        // Yapay zekâ: bekleyen öneriler (uygulama kapalıyken gelenler dahil) ve bağlantı (ayarlarda açıksa).
        Ai.Initialize();
        if (Ai.PendingCount > 0)
        {
            IsAiPanelOpen = true;
            SetStatus(L10n.T($"Onayınızı bekleyen {Ai.PendingCount} yapay zekâ önerisi var (sağdaki Yapay Zekâ paneli)."));
        }
        _ = Ai.Connection.ApplySettingsAsync();
    }

    private bool TryOfferRecovery()
    {
        IReadOnlyList<RecoveryEntry> orphans;
        try { orphans = _recovery.FindOrphanedSessions(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        if (orphans.Count == 0) return false;
        bool restore = Dialogs.Confirm(L10n.T("Otomatik kurtarma"),
            L10n.T($"{orphans.Count} kaydedilmemiş etiket bulundu. Kurtarılabilen etiketler ayrı sekmelerde açılsın mı?"),
            L10n.T("Kurtar"),L10n.T("Sil"));
        bool opened = false;
        foreach (var entry in orphans)
        {
            if (!restore) { _initialRecovery.Discard(entry); continue; }
            try
            {
                var result = _initialRecovery.Recover(entry);
                var tab = OpenDocument(result.Project,forceNewTab:opened);
                if (tab is null) break; // Unopened recovery files remain available.
                tab.Session.History.Clear();
                tab.Session.MarkUnsaved();
                tab.Recovery.Save(result.Project); // Persist the new copy before discarding its orphan.
                tab.RecoveryDirty = true;
                _initialRecovery.Discard(entry);
                opened = true;
            }
            catch (Exception ex) when (ex is ProjectFileException or IOException or UnauthorizedAccessException)
            { Dialogs.ShowMessage(L10n.T("Kurtarma"),ex.Message,MessageKind.Error); }
        }
        if (opened) SetStatus(L10n.T("Kurtarılan etiketler açıldı. Lütfen kaydedin."));
        return opened;
    }

    /// <summary>Otomatik kurtarma zamanlayıcısı (arayüz her birkaç saniyede bir çağırır).</summary>
    public void AutoRecoveryTick()
    {
        if (Settings.AutoRecoverySeconds <= 0) return;
        foreach (var tab in Documents.Where(t => t.RecoveryDirty && !t.IsDisposed).ToArray())
        {
            try
            {
                if (tab.Session.IsDirty) tab.Recovery.Save(tab.Session.Project);
                else tab.Recovery.ClearSession();
                tab.RecoveryDirty = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { SetStatus(L10n.T("Otomatik kurtarma kopyası yazılamadı: ") + ex.Message); }
        }
    }

    /// <summary>Pencere kapanırken: kaydedilmemiş değişiklik varsa sorar. false = kapatma iptal.</summary>
    public bool ConfirmCloseDocument()
    {
        if (!Session.IsDirty) return true;
        switch (Dialogs.AskSaveChanges(Session.Title.TrimEnd(' ', '•')))
        {
            case SaveChangesChoice.Save:
                return Save();
            case SaveChangesChoice.Discard:
                return true;
            default:
                return false;
        }
    }

    public void SaveSettings()
    {
        Settings.Recent.Items = RecentFiles.ToList();
        try
        {
            if (Settings.Personalization.RememberPanelLayout)
            {
                Settings.Personalization.ActivePanel = ActivePanel.ToString(); Settings.Personalization.IsPanelOpen = IsPanelOpen;
                Settings.Personalization.IsAiPanelOpen = IsAiPanelOpen;
            }
            _services.SettingsStore.Save(Settings);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ayar kaydı başarısız olsa bile uygulama çalışmaya devam etmeli.
            SetStatus(L10n.T("Ayarlar kaydedilemedi: ") + ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SaveSettings();
        try
        {
            // Yerel API'yi kapat ve barındırıcı kilidini bırak (Worker gerekirse devralır).
            Ai.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }
        foreach (var tab in Documents.ToArray()) { tab.Changed -= OnAnyDocumentChanged; tab.Dispose(); }
        _initialRecovery.Dispose();
    }

    // ───────────────────────── Görünüm ─────────────────────────

    public void RequestFit() => FitRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ZoomIn() => Zoom = Math.Min(MaxZoom, Zoom * 1.25);

    [RelayCommand]
    private void ZoomOut() => Zoom = Math.Max(MinZoom, Zoom / 1.25);

    [RelayCommand]
    private void ZoomActualSize() => Zoom = 1.0;

    [RelayCommand]
    private void ZoomFit() => RequestFit();

    [RelayCommand]
    private void ShowPanel(LeftPanel panel)
    {
        if (ActivePanel == panel && IsPanelOpen) IsPanelOpen = false;
        else
        {
            ActivePanel = panel;
            IsPanelOpen = true;
        }
        if (panel == LeftPanel.Templates) Templates.Refresh();
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        Settings.Theme = Settings.Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        SaveSettings();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleGrid()
    {
        Settings.ShowGrid = !Settings.ShowGrid;
        OnPropertyChanged(nameof(Settings));
    }

    [RelayCommand]
    private void ToggleSnap()
    {
        Settings.SnapToGrid = !Settings.SnapToGrid;
        Settings.SnapToObjects = Settings.SnapToGrid;
        OnPropertyChanged(nameof(Settings));
        SetStatus(Settings.SnapToGrid ? L10n.T("Yakalama açık") : L10n.T("Yakalama kapalı"));
    }

    public void RequestFocusContentEditor() => FocusContentEditorRequested?.Invoke(this, EventArgs.Empty);
}
