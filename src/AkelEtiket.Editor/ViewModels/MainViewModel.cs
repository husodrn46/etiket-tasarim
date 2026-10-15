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
    public required string RecoveryFolder { get; init; }

    /// <summary>Örnek şablonlar için logo (PNG baytları).</summary>
    public Func<byte[]?> LogoProvider { get; init; } = () => null;

    /// <summary>Dosyayı Geri Dönüşüm Kutusu'na gönderir (yoksa kalıcı silinir).</summary>
    public Action<string>? RecycleFile { get; init; }

    /// <summary>Klasörü Windows Gezgini'nde açar.</summary>
    public Action<string>? OpenFolder { get; init; }

    /// <summary>Uygulama sürümü.</summary>
    public string AppVersion { get; init; } = "2.0.0";

    /// <summary>Bu açılışta v1'in ayarları devralındı (<see cref="LegacySettingsImport"/>).</summary>
    public bool ImportedLegacySettings { get; init; }
}

/// <summary>Ana pencerenin görünüm modeli.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public const double MinZoom = 0.25;
    public const double MaxZoom = 16;

    private readonly AppServices _services;
    private readonly AutoRecoveryService _recovery;
    private bool _recoveryDirty;
    private bool _disposed;

    public MainViewModel(AppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        Settings = services.SettingsStore.Load();
        Settings.Normalize();
        _recovery = new AutoRecoveryService(services.RecoveryFolder);
        Data = new DataSourceViewModel();
        Data.PreviewChanged += (_, _) => ApplyPreviewFields();
        Data.TableChanged += (_, _) =>
        {
            UpdateImageResolver();
            ValidateData();
        };
        Data.ReloadFailed += (_, message) => Dialogs.ShowMessage("Veri kaynağı", message, MessageKind.Error);
        Templates = new TemplatesViewModel(this);
        Data.MappingRequested += (_, _) => OpenColumnMappingCommand.Execute(null);
        foreach (var path in Settings.Recent.Items) RecentFiles.Add(path);
        // {SAYAC} (belgenin kendi sayacı) açık belgenin adını kullanır.
        Printing.CounterIntegration.DocumentName = () => Session?.Document.Name;
        Session = CreateSession(LabelProject.CreateBlank(Settings.DefaultLabelWidthMm, Settings.DefaultLabelHeightMm));

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
    public DataSourceViewModel Data { get; }
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
    public partial string StatusMessage { get; set; } = "Hazır";

    [ObservableProperty]
    public partial string CursorText { get; set; } = "";

    public string ZoomText => $"%{Zoom * 100:0}";

    public string WindowTitle => $"{Session.Title} – {AkelEtiket.Core.AppIdentity.ProductName}";

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
        if (value == LeftPanel.Templates) Templates.Refresh();
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
            oldValue.Dispose();
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
        0 => "Seçim yok",
        1 => Session.PrimarySelection!.Model.DisplayName,
        int n => $"{n} öğe seçili",
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
            Dialogs.ShowMessage("Ayarlar", problem, MessageKind.Warning);

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
                var sample = SampleTemplates.CreateForSize(Settings.DefaultLabelWidthMm, Settings.DefaultLabelHeightMm, Settings.Company);
                Session = CreateSession(sample);
                SetStatus("Hoş geldiniz! Etiketiniz hazır. Tasarımınızı oluşturabilir veya Şablonlar panelinden bir örnek seçebilirsiniz.");
            }
            if (_services.ImportedLegacySettings)
                SetStatus("AKEL Etiket (v1) ayarlarınız devralındı: yazıcı ayarları, son kullanılan dosyalar ve klasörler" +
                          (Templates.ImportedLegacyCount > 0 ? $"; {Templates.ImportedLegacyCount} şablon kopyalandı" : "") + " (v1'deki asıllar değişmedi).");
            else if (Templates.ImportedLegacyCount > 0)
                SetStatus($"AKEL Etiket (v1) şablonlarından {Templates.ImportedLegacyCount} tanesi kopyalandı (v1'deki asıllar değişmedi).");
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
            SetStatus($"Onayınızı bekleyen {Ai.PendingCount} yapay zekâ önerisi var (sağdaki Yapay Zekâ paneli).");
        }
        _ = Ai.Connection.ApplySettingsAsync();
    }

    private bool TryOfferRecovery()
    {
        IReadOnlyList<RecoveryEntry> orphans;
        try
        {
            orphans = _recovery.FindOrphanedSessions();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        if (orphans.Count == 0) return false;
        var latest = orphans[0];
        bool restore = Dialogs.Confirm("Otomatik kurtarma",
            $"Uygulama beklenmedik şekilde kapanmış görünüyor.\n\n\"{latest.DocumentName}\" ({latest.SavedAt:dd.MM.yyyy HH:mm}) " +
            "belgesinin kaydedilmemiş bir kopyası bulundu. Kurtarılsın mı?",
            "Kurtar", "Sil");
        bool opened = false;
        if (restore)
        {
            try
            {
                var result = _recovery.Recover(latest);
                Session = CreateSession(result.Project);
                Session.History.Clear();
                _recoveryDirty = true;
                opened = true;
                SetStatus("Kurtarılan belge açıldı. Lütfen kaydedin.");
            }
            catch (ProjectFileException ex)
            {
                Dialogs.ShowMessage("Kurtarma", ex.Message, MessageKind.Error);
            }
        }
        foreach (var entry in orphans) _recovery.Discard(entry);
        return opened;
    }

    /// <summary>Otomatik kurtarma zamanlayıcısı (arayüz her birkaç saniyede bir çağırır).</summary>
    public void AutoRecoveryTick()
    {
        if (!_recoveryDirty || Settings.AutoRecoverySeconds <= 0) return;
        try
        {
            if (Session.IsDirty) _recovery.Save(Session.Project);
            else _recovery.ClearSession();
            _recoveryDirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Otomatik kurtarma kopyası yazılamadı: " + ex.Message);
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
            _services.SettingsStore.Save(Settings);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ayar kaydı başarısız olsa bile uygulama çalışmaya devam etmeli.
            SetStatus("Ayarlar kaydedilemedi: " + ex.Message);
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
        _recovery.Dispose();
        Session.Dispose();
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
        SetStatus(Settings.SnapToGrid ? "Yakalama açık" : "Yakalama kapalı");
    }

    public void RequestFocusContentEditor() => FocusContentEditorRequested?.Invoke(this, EventArgs.Empty);
}
