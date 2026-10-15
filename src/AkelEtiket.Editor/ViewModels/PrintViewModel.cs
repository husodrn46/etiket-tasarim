using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;
using AkelEtiket.Editor.Printing;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.Templates;
using AkelEtiket.Gs1;
using AkelEtiket.PrintEngine;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>Rapor satırı (aşama + durum).</summary>
public sealed record ReportLine(string State, string Text);

/// <summary>Baskı penceresinin hazır ayarları (geçmişten tekrar basma / kaldığı yerden devam).</summary>
public sealed record PrintPreset(
    bool? UseData = null,
    int? Copies = null,
    long? StartNumber = null,
    string? Banner = null,
    string? Title = null,
    string? PrinterName = null,
    string? ProfileName = null);

/// <summary>Baskı penceresinin kullandığı kalıcı depolar (uygulamada varsayılan klasörler; testler değiştirebilir).</summary>
public static class PrintingServices
{
    internal delegate bool PrinterListReader(out IReadOnlyList<PrinterInfo> printers, out string? error);
    // Üretimde Windows keşfi; arayüzsüz kabul testlerinde gerçek yazıcıya erişmeyen adaptör.
    internal static PrinterListReader ReadPrinters { get; set; } = PrinterDiscovery.TryGetPrinters;

    public static PrinterProfileStore Profiles { get; set; } = new();

    public static PrintHistoryStore History { get; set; } = new();

    /// <summary>TSPL baskı servisini oluşturur (belge adı, tek parça). Testler sahte kuyrukla değiştirir.</summary>
    internal static Func<string, bool, TsplPrintService> CreateTsplService { get; set; } =
        (documentName, singleJob) => new TsplPrintService { DocumentName = documentName, SingleJob = singleJob };
}

/// <summary>
/// Baskı penceresi: yazıcı seçimi, profil, yöntem (TSPL RAW / Windows sürücüsü), yoğunluk, hız, ofsetler,
/// medya, kopya, veri satırları, baskı öncesi doğrulama, ilerleme/iptal, aşamalı Türkçe rapor, baskı geçmişi kaydı ve
/// yarım kalan işin kaldığı yerden sürdürülmesi.
/// </summary>
public sealed partial class PrintViewModel : ObservableObject
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly MainViewModel _main;
    private readonly string _title;
    private CancellationTokenSource? _cts;
    private bool _applyingProfile;
    private LastRun? _lastRun;

    /// <summary>Yarım kalan son iş (kaldığı yerden devam için).</summary>
    private sealed record LastRun(LabelDocument Document, PrintPlan Plan, IReadOnlyList<PrintItem> Remaining);

    public PrintViewModel(MainViewModel main, PrintPreset? preset = null)
    {
        _main = main;
        var s = main.Settings.Printer;
        Method = s.Method;
        Density = s.Density;
        Speed = s.Speed;
        Rotate180 = s.Rotate180;
        OffsetXMm = s.OffsetXMm;
        OffsetYMm = s.OffsetYMm;
        TearMode = s.TearMode;
        AllowWhenOffline = s.AllowPrintWhenOffline;
        SingleJob = s.SingleJob;
        VerifyBarcodes = main.Settings.VerifyBarcodesBeforePrint;
        UseData = main.Data.HasData && main.Session.Document.HasFieldPlaceholders;
        _title = preset?.Title ?? L10n.T("Baskı");
        if (preset is not null)
        {
            if (preset.UseData is { } useData) UseData = useData && main.Data.HasData;
            if (preset.Copies is { } copies) Copies = Math.Clamp(copies, 1, 99999);
            if (preset.StartNumber is { } start) StartNumber = (int)Math.Clamp(start, 0, 99_999_999);
            Banner = preset.Banner;
        }
        RefreshPrinters();
        // Son kullanılan profil, ayarlar hâlâ onunla aynıysa seçili gösterilir (yeniden uygulanmaz).
        string? last = PrintingServices.Profiles.LastUsed;
        LoadProfiles(last is not null && PrintingServices.Profiles.Find(last) is { } lastProfile && Matches(lastProfile) ? last : null);
        if (preset?.PrinterName is { Length: > 0 } printerName &&
            Printers.FirstOrDefault(p => string.Equals(p.Name, printerName, StringComparison.OrdinalIgnoreCase)) is { } presetPrinter)
            SelectedPrinter = presetPrinter;
        if (preset?.ProfileName is { Length: > 0 } profileName && PrintingServices.Profiles.Find(profileName) is { } presetProfile)
            ApplyProfile(presetProfile);
        ProfileWarning ??= PrintingServices.Profiles.LoadProblem;
        RunPreflight();
    }

    public LabelSetup Label => _main.Session.Document.Label;
    public DataSourceViewModel Data => _main.Data;
    public bool CanUseData => _main.Data.HasData;

    public ObservableCollection<PrinterInfo> Printers { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrinterStatusText), nameof(PrinterHint))]
    public partial PrinterInfo? SelectedPrinter { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTspl), nameof(MethodHint))]
    public partial PrintMethod Method { get; set; }

    public bool IsTspl => Method == PrintMethod.TsplRaw;

    [ObservableProperty]
    public partial int Density { get; set; }

    [ObservableProperty]
    public partial int Speed { get; set; }

    [ObservableProperty]
    public partial bool Rotate180 { get; set; }

    [ObservableProperty]
    public partial double OffsetXMm { get; set; }

    [ObservableProperty]
    public partial double OffsetYMm { get; set; }

    [ObservableProperty]
    public partial TearMode TearMode { get; set; }

    [ObservableProperty]
    public partial bool AllowWhenOffline { get; set; }

    [ObservableProperty]
    public partial bool VerifyBarcodes { get; set; }

    /// <summary>
    /// Toplu baskı tek Windows işi olarak gönderilir: aynı yazıcıya sipariş sisteminin etiketleri gibi başka bir iş araya
    /// giremez. İptal, iş kuyruğa yazılmayı bitirene kadar etkilidir.
    /// </summary>
    [ObservableProperty]
    public partial bool SingleJob { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlanSummary))]
    public partial int Copies { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlanSummary))]
    public partial int StartNumber { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlanSummary))]
    public partial bool UseData { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand), nameof(TestPrintCommand), nameof(CalibrateCommand), nameof(FeedCommand), nameof(ResumeCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial double ProgressValue { get; private set; }

    [ObservableProperty]
    public partial string ProgressText { get; private set; } = "";

    [ObservableProperty]
    public partial string ReportText { get; private set; } = "";

    [ObservableProperty]
    public partial bool? LastSuccess { get; private set; }

    /// <summary>Geçmişten açıldıysa açıklama (ör. "Kaldığı yerden devam: 37 etiket").</summary>
    [ObservableProperty]
    public partial string? Banner { get; set; }

    /// <summary>Son iş yarım kaldıysa "kaldığı yerden devam" mümkün.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    public partial bool CanResume { get; private set; }

    [ObservableProperty]
    public partial string ResumeText { get; private set; } = "";

    public ObservableCollection<ReportLine> ReportLines { get; } = [];

    /// <summary>Baskı öncesi kontrol sonuçları (hatalar/uyarılar).</summary>
    public ObservableCollection<string> PreflightIssues { get; } = [];

    [ObservableProperty]
    public partial bool PreflightHasErrors { get; private set; }

    public IReadOnlyList<int> SpeedChoices { get; } = [2, 3, 4, 5, 6];

    public IReadOnlyList<KeyValuePair<PrintMethod, string>> MethodChoices { get; } =
    [
        new(PrintMethod.TsplRaw, L10n.T("TSPL doğrudan (RAW) — önerilen")),
        new(PrintMethod.WindowsDriver, L10n.T("Windows sürücüsü üzerinden")),
    ];

    public IReadOnlyList<KeyValuePair<TearMode, string>> TearChoices { get; } =
    [
        new(TearMode.Unchanged, L10n.T("Yazıcı ayarını değiştirme")),
        new(TearMode.On, L10n.T("Yırtma konumu açık")),
        new(TearMode.Off, L10n.T("Yırtma konumu kapalı")),
    ];

    public IReadOnlyList<KeyValuePair<MediaType, string>> MediaChoices { get; } =
    [
        new(MediaType.Gap, L10n.T("Boşluklu etiket (gap)")),
        new(MediaType.BlackMark, L10n.T("Siyah işaretli")),
        new(MediaType.Continuous, L10n.T("Sürekli rulo")),
    ];

    public string MethodHint => IsTspl
        ? L10n.T("Etiket yazıcı çözünürlüğünde 1-bit görüntü olarak üretilir ve TSPL BITMAP komutuyla doğrudan yazıcıya gönderilir. Ölçekleme yapılmaz.")
        : L10n.T("Görüntü sürücünün bildirdiği gerçek çözünürlükte üretilip ölçeklemeden basılır. Sürücünün kağıt ayarları ve kenar boşlukları geçerlidir.");

    public string PrinterStatusText => SelectedPrinter is null ? L10n.T("Yazıcı seçilmedi") : SelectedPrinter.StatusText;

    public string? PrinterHint => SelectedPrinter switch
    {
        null => null,
        { IsXp470B: true } => L10n.T("Xprinter XP-470B algılandı (203 DPI, en fazla 108 mm baskı genişliği)."),
        { IsLikelyTsplPrinter: false } when IsTspl => L10n.T("Bu yazıcı TSPL desteklemiyor olabilir; TSPL yalnızca etiket yazıcılarında çalışır. Normal yazıcılar için \"Windows sürücüsü\" yöntemini seçin."),
        _ => null,
    };

    public string PlanSummary
    {
        get
        {
            if (UseData && _main.Data.HasData)
            {
                int rows = _main.Data.Rows.Count(r => r.IsSelected && r.Copies > 0 && r.Status != RowStatus.Error);
                int errors = _main.Data.Rows.Count(r => r.IsSelected && r.Status == RowStatus.Error);
                return L10n.T($"{rows} satırdan toplam {_main.Data.SelectedLabelCount} etiket basılacak") + (errors > 0 ? L10n.T($" ({errors} hatalı satır atlanacak).") : ".");
            }
            return Copies == 1 ? L10n.T("1 etiket basılacak.") : L10n.T($"{Copies} etiket basılacak.");
        }
    }

    // ───────────────────────── Profiller ─────────────────────────

    public ObservableCollection<PrinterProfile> ProfileList { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileNotes), nameof(IsUserProfileSelected))]
    public partial PrinterProfile? SelectedProfile { get; set; }

    /// <summary>Profil uygulanırken oluşan uyarı (yazıcı bulunamadı, rulo ölçüsü farklı...).</summary>
    [ObservableProperty]
    public partial string? ProfileWarning { get; private set; }

    public string? ProfileNotes => SelectedProfile is null ? null : SelectedProfile.Summary + (SelectedProfile.Notes.Length > 0 ? "\n" + SelectedProfile.Notes : "");

    public bool IsUserProfileSelected => SelectedProfile is { IsBuiltIn: false };

    /// <summary>Penceredeki ayarlar profille aynı mı? (Yazıcı adı yalnızca profilde verilmişse karşılaştırılır.)</summary>
    private bool Matches(PrinterProfile p) =>
        (p.PrinterName.Length == 0 || string.Equals(p.PrinterName, SelectedPrinter?.Name, StringComparison.OrdinalIgnoreCase)) &&
        p.Method == Method && p.Density == Density && p.Speed == Speed && p.Rotate180 == Rotate180 &&
        Math.Abs(p.OffsetXMm - OffsetXMm) < 1e-6 && Math.Abs(p.OffsetYMm - OffsetYMm) < 1e-6 &&
        p.TearMode == TearMode && p.SingleJob == SingleJob && p.Media == Label.Media && Math.Abs(p.GapMm - Label.GapMm) < 1e-6;

    private void LoadProfiles(string? select = null)
    {
        _applyingProfile = true;
        try
        {
            ProfileList.Clear();
            foreach (var profile in PrintingServices.Profiles.All) ProfileList.Add(profile);
            SelectedProfile = select is null ? null : ProfileList.FirstOrDefault(p => string.Equals(p.Name, select, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _applyingProfile = false;
        }
    }

    partial void OnSelectedProfileChanged(PrinterProfile? value)
    {
        if (_applyingProfile || value is null) return;
        ApplyProfile(value);
    }

    /// <summary>Profili baskı ayarlarına ve etiketin medya bilgisine uygular (etiket ölçüsünü değiştirmez).</summary>
    public void ApplyProfile(PrinterProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var warnings = new List<string>();
        if (profile.PrinterName.Length > 0)
        {
            var printer = Printers.FirstOrDefault(p => string.Equals(p.Name, profile.PrinterName, StringComparison.OrdinalIgnoreCase));
            if (printer is not null) SelectedPrinter = printer;
            else warnings.Add(L10n.T($"Profildeki \"{profile.PrinterName}\" yazıcısı bu bilgisayarda bulunamadı; mevcut yazıcı seçimi korundu."));
        }
        Method = profile.Method;
        Density = Math.Clamp(profile.Density, LabelPrinterSettings.MinDensity, LabelPrinterSettings.MaxDensity);
        Speed = Math.Clamp(profile.Speed, LabelPrinterSettings.MinSpeed, LabelPrinterSettings.MaxSpeed);
        Rotate180 = profile.Rotate180;
        OffsetXMm = profile.OffsetXMm;
        OffsetYMm = profile.OffsetYMm;
        TearMode = profile.TearMode;
        AllowWhenOffline = profile.AllowPrintWhenOffline;
        SingleJob = profile.SingleJob;
        var label = Label;
        if (label.Media != profile.Media || Math.Abs(label.GapMm - profile.GapMm) > 1e-9 || Math.Abs(label.GapOffsetMm - profile.GapOffsetMm) > 1e-9)
        {
            using (_main.Session.BeginTransaction(L10n.T("Yazıcı profili: medya ayarı")))
            {
                label.Media = profile.Media;
                label.GapMm = profile.GapMm;
                label.GapOffsetMm = profile.GapOffsetMm;
            }
        }
        if (profile.LabelWidthMm is { } w && profile.LabelHeightMm is { } h)
        {
            // Rulo ölçüsü yeni etiketlerin varsayılan ölçüsü olur; açık tasarımın ölçüsü değiştirilmez.
            if (w is >= 5 and <= 1000 && h is >= 5 and <= 1000 &&
                (Math.Abs(_main.Settings.DefaultLabelWidthMm - w) > 1e-9 || Math.Abs(_main.Settings.DefaultLabelHeightMm - h) > 1e-9))
            {
                _main.Settings.DefaultLabelWidthMm = w;
                _main.Settings.DefaultLabelHeightMm = h;
                _main.SaveSettings();
            }
            if (Math.Abs(w - label.WidthMm) > 0.05 || Math.Abs(h - label.HeightMm) > 0.05)
                warnings.Add(L10n.T($"Profilin rulo ölçüsü {w:0.##} × {h:0.##} mm, tasarım {label.WidthMm:0.##} × {label.HeightMm:0.##} mm. Rulo ile tasarımın aynı ölçüde olduğundan emin olun."));
        }
        ProfileWarning = warnings.Count == 0 ? null : string.Join("\n", warnings);
        PrintingServices.Profiles.LastUsed = profile.Name;
        if (!_applyingProfile && !ReferenceEquals(SelectedProfile, profile))
        {
            _applyingProfile = true;
            SelectedProfile = ProfileList.FirstOrDefault(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            _applyingProfile = false;
        }
        RunPreflight();
    }

    /// <summary>Penceredeki ayarlardan profil oluşturur (rulo ölçüsü olarak etiketin ölçüsü kaydedilir).</summary>
    public PrinterProfile CaptureProfile(string name) => new()
    {
        Name = name.Trim(),
        PrinterName = SelectedPrinter?.Name ?? "",
        Method = Method,
        Density = Density,
        Speed = Speed,
        Rotate180 = Rotate180,
        OffsetXMm = OffsetXMm,
        OffsetYMm = OffsetYMm,
        TearMode = TearMode,
        AllowPrintWhenOffline = AllowWhenOffline,
        SingleJob = SingleJob,
        Media = Label.Media,
        GapMm = Label.GapMm,
        GapOffsetMm = Label.GapOffsetMm,
        LabelWidthMm = Label.WidthMm,
        LabelHeightMm = Label.HeightMm,
    };

    [RelayCommand]
    private void SaveProfileAs()
    {
        string suggestion = SelectedProfile is { IsBuiltIn: false } p ? p.Name : L10n.T("XP-470B profilim");
        string? name = _main.Dialogs.Prompt(L10n.T("Profil olarak kaydet"), L10n.T("Profil adı (yazıcı, yöntem, yoğunluk, hız, yön, ofset ve rulo bilgisi kaydedilir):"), suggestion);
        if (string.IsNullOrWhiteSpace(name)) return;
        if (PrinterProfileStore.IsBuiltInName(name))
        {
            _main.Dialogs.ShowMessage(L10n.T("Profil"), L10n.T("Bu ad hazır bir profile ait; farklı bir ad seçin."), MessageKind.Warning);
            return;
        }
        if (PrintingServices.Profiles.Find(name) is { IsBuiltIn: false } &&
            !_main.Dialogs.Confirm(L10n.T("Profil"), L10n.T($"\"{name.Trim()}\" profili zaten var. Üzerine yazılsın mı?"), L10n.T("Üzerine yaz"), L10n.T("Vazgeç")))
            return;
        SaveProfile(CaptureProfile(name));
    }

    [RelayCommand]
    private void UpdateProfile()
    {
        if (SelectedProfile is not { IsBuiltIn: false } selected)
        {
            SaveProfileAs();
            return;
        }
        if (!_main.Dialogs.Confirm(L10n.T("Profil"), L10n.T($"\"{selected.Name}\" profili penceredeki ayarlarla güncellensin mi?"), L10n.T("Güncelle"), L10n.T("Vazgeç"))) return;
        var profile = CaptureProfile(selected.Name);
        profile.Notes = selected.Notes;
        SaveProfile(profile);
    }

    /// <summary>
    /// Kalibrasyon sonucunu uygular: ofsetler penceredeki ve kalıcı yazıcı ayarlarına yazılır. Seçili kullanıcı profili
    /// varsa o profilin ofsetleri güncellenir; hazır profil seçiliyse ya da profil yoksa yeni profil adı sorulur
    /// (vazgeçilirse ofsetler yalnızca ayarlarda kalır). Kullanıcıya gösterilecek özeti döndürür.
    /// </summary>
    public string StoreCalibration(double offsetXMm, double offsetYMm)
    {
        OffsetXMm = Math.Round(Math.Clamp(offsetXMm, -LabelPrinterSettings.MaxOffsetMm, LabelPrinterSettings.MaxOffsetMm), 3);
        OffsetYMm = Math.Round(Math.Clamp(offsetYMm, -LabelPrinterSettings.MaxOffsetMm, LabelPrinterSettings.MaxOffsetMm), 3);
        SaveCurrentSettings();
        string offsets = L10n.T($"sağ/aşağı ofset {OffsetXMm:0.###} / {OffsetYMm:0.###} mm");
        if (SelectedProfile is { IsBuiltIn: false } selected)
        {
            try
            {
                var profile = PrintingServices.Profiles.Find(selected.Name) ?? selected.Clone();
                profile.OffsetXMm = OffsetXMm;
                profile.OffsetYMm = OffsetYMm;
                profile.Rotate180 = Rotate180;
                PrintingServices.Profiles.Save(profile);
                LoadProfiles(profile.Name);
                return L10n.T($"Uygulandı ve \"{profile.Name}\" profiline kaydedildi ({offsets}).");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                return L10n.T($"Ofsetler uygulandı ({offsets}) ama profil kaydedilemedi: {ex.Message}");
            }
        }
        string suggestion = (SelectedProfile is not null ? "XP-470B" : SelectedPrinter?.Name ?? L10n.T("Yazıcı")) + L10n.T(" – kalibre");
        string? name = _main.Dialogs.Prompt(L10n.T("Kalibrasyonu profile kaydet"),
            L10n.T("Ofsetler uygulandı. Hazır profil değiştirilemediği (ya da profil seçilmediği) için ayarlar yeni bir profile kaydedilecek. Profil adı:"),
            suggestion);
        if (string.IsNullOrWhiteSpace(name)) return L10n.T($"Ofsetler uygulandı ({offsets}); profile kaydedilmedi.");
        if (PrinterProfileStore.IsBuiltInName(name)) return L10n.T($"Ofsetler uygulandı ({offsets}); hazır profilin adı kullanılamaz, profile kaydedilmedi.");
        if (PrintingServices.Profiles.Find(name) is { IsBuiltIn: false } &&
            !_main.Dialogs.Confirm(L10n.T("Profil"), L10n.T($"\"{name.Trim()}\" profili zaten var. Üzerine yazılsın mı?"), L10n.T("Üzerine yaz"), L10n.T("Vazgeç")))
            return L10n.T($"Ofsetler uygulandı ({offsets}); profile kaydedilmedi.");
        return SaveProfile(CaptureProfile(name))
            ? L10n.T($"Uygulandı ve \"{name.Trim()}\" profiline kaydedildi ({offsets}).")
            : L10n.T($"Ofsetler uygulandı ({offsets}) ama profil kaydedilemedi.");
    }

    /// <summary>Penceredeki ayarları kalıcı yazıcı ayarlarına yazar.</summary>
    internal void SaveCurrentSettings() => PersistSettings(BuildSettings());

    private bool SaveProfile(PrinterProfile profile)
    {
        try
        {
            PrintingServices.Profiles.Save(profile);
            PrintingServices.Profiles.LastUsed = profile.Name;
            LoadProfiles(profile.Name);
            _main.StatusMessage = L10n.T($"Yazıcı profili kaydedildi: {profile.Name}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _main.Dialogs.ShowMessage(L10n.T("Profil kaydedilemedi"), ex.Message, MessageKind.Error);
            return false;
        }
    }

    [RelayCommand]
    private void RenameProfile()
    {
        if (SelectedProfile is not { IsBuiltIn: false } selected) return;
        string? name = _main.Dialogs.Prompt(L10n.T("Profili yeniden adlandır"), L10n.T("Yeni ad:"), selected.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == selected.Name) return;
        try
        {
            PrintingServices.Profiles.Rename(selected.Name, name);
            LoadProfiles(name.Trim());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _main.Dialogs.ShowMessage(L10n.T("Profil"), ex.Message, MessageKind.Warning);
        }
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is not { IsBuiltIn: false } selected) return;
        if (!_main.Dialogs.Confirm(L10n.T("Profil"), $"\"{selected.Name}\" profili silinsin mi?", L10n.T("Sil"), L10n.T("Vazgeç"))) return;
        try
        {
            PrintingServices.Profiles.Delete(selected.Name);
            LoadProfiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _main.Dialogs.ShowMessage(L10n.T("Profil"), ex.Message, MessageKind.Warning);
        }
    }

    // ───────────────────────── Yazıcılar ve kontrol ─────────────────────────

    [RelayCommand]
    public void RefreshPrinters()
    {
        string? current = SelectedPrinter?.Name ?? _main.Settings.Printer.PrinterName;
        Printers.Clear();
        if (PrintingServices.ReadPrinters(out var printers, out var error))
        {
            foreach (var p in printers) Printers.Add(p);
        }
        else
        {
            ReportText = error ?? L10n.T("Yazıcılar listelenemedi.");
        }
        SelectedPrinter = Printers.FirstOrDefault(p => string.Equals(p.Name, current, StringComparison.OrdinalIgnoreCase))
                          ?? Printers.FirstOrDefault(p => p.IsXp470B)
                          ?? Printers.FirstOrDefault(p => p.IsLikelyTsplPrinter)
                          ?? Printers.FirstOrDefault(p => p.IsDefault)
                          ?? Printers.FirstOrDefault();
    }

    /// <summary>Baskı öncesi doğrulama: önizleme satırıyla etiketi baskı kipinde çizer, sorunları listeler.</summary>
    [RelayCommand]
    public void RunPreflight()
    {
        PreflightIssues.Clear();
        var fields = _main.Data.GetPreviewFields(DateTime.Now);
        var result = _main.Session.RenderLabel(UseData ? RenderMode.Print : RenderMode.Design, fields, _main.Settings.Printer.DotsPerMm);
        foreach (var issue in result.Issues.Where(i => i.Severity >= RenderIssueSeverity.Warning).DistinctBy(i => i.ToString()))
            PreflightIssues.Add((issue.Severity == RenderIssueSeverity.Error ? L10n.T("HATA: ") : L10n.T("Uyarı: ")) + issue);
        if (!UseData && _main.Session.Document.HasFieldPlaceholders)
            PreflightIssues.Add(L10n.T("Uyarı: Etikette değişken alan var ancak veri kaynağı kullanılmıyor; alanlar boş/yer tutucu basılabilir."));
        if (UseData && _main.Data.ValidationSummary.Length > 0)
            PreflightIssues.Add(L10n.T("Veri: ") + _main.Data.ValidationSummary);
        foreach (var problem in BuildSettings().Validate()) PreflightIssues.Add(L10n.T("Uyarı: ") + problem);
        foreach (var problem in LabelMedia.FromSetup(Label).Validate()) PreflightIssues.Add(L10n.T("HATA: ") + problem);
        var counters = CounterFields.CountersUsedBy(_main.Session.Document);
        if (counters.Count > 0)
        {
            var parts = counters.Select(c =>
            {
                try
                {
                    return L10n.T($"\"{c}\" sıradaki {CounterIntegration.Store.Peek(c)}");
                }
                catch (CounterException ex)
                {
                    return $"\"{c}\": {ex.Message}";
                }
            });
            PreflightIssues.Add(L10n.T("Bilgi: Sayaç kullanılıyor (") + string.Join(", ", parts) + L10n.T("). Her etiket ayrı numara alır; yalnızca kuyruğa gönderilen etiketler sayacı ilerletir."));
        }
        PreflightHasErrors = PreflightIssues.Any(i => i.StartsWith("HATA", StringComparison.Ordinal));
        if (PreflightIssues.Count == 0) PreflightIssues.Add(L10n.T("Sorun bulunmadı."));
    }

    partial void OnUseDataChanged(bool value) => RunPreflight();

    private LabelPrinterSettings BuildSettings()
    {
        var s = _main.Settings.Printer.Clone();
        s.PrinterName = SelectedPrinter?.Name ?? "";
        s.Method = Method;
        s.Density = Math.Clamp(Density, LabelPrinterSettings.MinDensity, LabelPrinterSettings.MaxDensity);
        s.Speed = Math.Clamp(Speed, LabelPrinterSettings.MinSpeed, LabelPrinterSettings.MaxSpeed);
        s.Rotate180 = Rotate180;
        s.OffsetXMm = Math.Clamp(OffsetXMm, -LabelPrinterSettings.MaxOffsetMm, LabelPrinterSettings.MaxOffsetMm);
        s.OffsetYMm = Math.Clamp(OffsetYMm, -LabelPrinterSettings.MaxOffsetMm, LabelPrinterSettings.MaxOffsetMm);
        s.TearMode = TearMode;
        s.AllowPrintWhenOffline = AllowWhenOffline;
        s.SingleJob = SingleJob;
        return s;
    }

    private void PersistSettings(LabelPrinterSettings settings)
    {
        _main.Settings.Printer = settings;
        _main.Settings.VerifyBarcodesBeforePrint = VerifyBarcodes;
        _main.SaveSettings();
    }

    private bool CanStart() => !IsBusy;

    // ───────────────────────── Baskı ─────────────────────────

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Print()
    {
        if (SelectedPrinter is null)
        {
            _main.Dialogs.ShowMessage(L10n.T("Yazdır"), L10n.T("Lütfen bir yazıcı seçin."), MessageKind.Warning);
            return;
        }
        RunPreflight();
        var plan = PrintPlanner.Plan(_main.Session.Document, _main.Data, UseData, Copies, StartNumber, DateTime.Now);
        if (plan.Items.Count == 0)
        {
            _main.Dialogs.ShowMessage(L10n.T("Yazdır"), plan.Skipped.Count > 0
                ? L10n.T("Basılabilecek geçerli satır yok. Hatalı satırlar:\n") + string.Join("\n", plan.Skipped.Take(15))
                : L10n.T("Basılacak etiket seçilmedi."), MessageKind.Warning);
            return;
        }
        if (plan.Skipped.Count > 0 && !_main.Dialogs.Confirm(L10n.T("Hatalı satırlar"),
                L10n.T($"{plan.Skipped.Count} hatalı satır atlanacak, {plan.TotalLabels} etiket basılacak.\n\n") +
                string.Join("\n", plan.Skipped.Take(10)) + (plan.Skipped.Count > 10 ? "\n…" : "") + "\n\nDevam edilsin mi?",
                L10n.T("Yazdır"), L10n.T("Vazgeç")))
            return;
        if (plan.TotalLabels > 200 && !_main.Dialogs.Confirm(L10n.T("Toplu baskı"), L10n.T($"{plan.TotalLabels} etiket basılacak. Devam edilsin mi?"), L10n.T("Yazdır"), L10n.T("Vazgeç")))
            return;
        if (plan.UsesSscc && !ConfirmSscc(plan.TotalLabels))
            return;

        var settings = BuildSettings();
        PersistSettings(settings);
        var document = ModelJson.Clone(_main.Session.Document);
        await RunPrintAsync(document, plan, settings, _title, PrintKind.Production);
    }

    /// <summary>Etiket ölçüsünde test etiketi basar (çerçeve, cetvel, merkez, Türkçe karakter, barkodlar).</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task TestPrint()
    {
        if (SelectedPrinter is null)
        {
            _main.Dialogs.ShowMessage(L10n.T("Test baskısı"), L10n.T("Lütfen bir yazıcı seçin."), MessageKind.Warning);
            return;
        }
        var settings = BuildSettings();
        PersistSettings(settings);
        string method = IsTspl ? "TSPL" : L10n.T("Sürücü");
        var document = TestLabelFactory.Create(Label, SelectedPrinter.Name, method, settings.DotsPerMm * 25.4, DateTime.Now);
        var plan = PrintPlanner.Plan(document, _main.Data, useData: false, copies: 1, startNumber: 1, DateTime.Now);
        await RunPrintAsync(document, plan, settings, L10n.T("Test baskısı"), PrintKind.Test);
    }

    /// <summary>
    /// Kalibrasyon (cetvel) etiketini mevcut ayarlarla basar. Çağıran (kalibrasyon yardımcısı) kullanıcıdan onay almış
    /// olmalıdır. Rapor bu pencerede gösterilir.
    /// </summary>
    public async Task<bool> PrintCalibrationLabelAsync()
    {
        if (SelectedPrinter is null || IsBusy) return false;
        var settings = BuildSettings();
        PersistSettings(settings);
        var document = CalibrationLabelFactory.Create(Label, SelectedPrinter.Name, OffsetXMm, OffsetYMm, Rotate180, DateTime.Now);
        var plan = PrintPlanner.Plan(document, _main.Data, useData: false, copies: 1, startNumber: 1, DateTime.Now);
        await RunPrintAsync(document, plan, settings, L10n.T("Kalibrasyon etiketi"), PrintKind.Test);
        return LastSuccess == true;
    }

    private bool CanResumeNow() => !IsBusy && CanResume;

    /// <summary>Yarım kalan son işin gönderilmemiş etiketlerini basar (gönderilenler tekrar basılmaz).</summary>
    [RelayCommand(CanExecute = nameof(CanResumeNow))]
    private async Task Resume()
    {
        if (_lastRun is null || SelectedPrinter is null) return;
        int labels = _lastRun.Remaining.Sum(i => i.Copies);
        string numbering = (_lastRun.Plan.UsesSscc ? L10n.T("Bu etiketlere yeni SSCC numaraları verilir (gönderilmeyenlerin numaraları kullanılmadı olarak kaldı). ") : "") +
                           (CounterFields.DocumentUsesCounters(_lastRun.Document) ? L10n.T("Sayaçlar kaldığı değerden sürer. ") : "");
        if (!_main.Dialogs.Confirm(L10n.T("Kaldığı yerden devam"),
                L10n.T($"Gönderilmemiş {labels} etiket basılacak. Daha önce yazıcı kuyruğuna gönderilen etiketler tekrar basılmaz. {numbering}\n\n") +
                L10n.T("Yazıcıdan çıkan son etiketi kontrol edin: kağıt bitmesi gibi bir sorunda kuyruğa gitmiş ama basılamamış etiketler ") +
                L10n.T("olabilir; bunları Baskı geçmişinden ayrıca basabilirsiniz.\n\nDevam edilsin mi?"), L10n.T("Yazdır"), L10n.T("Vazgeç")))
            return;
        var plan = new PrintPlan { UsesData = _lastRun.Plan.UsesData, UsesSscc = _lastRun.Plan.UsesSscc };
        plan.Items.AddRange(_lastRun.Remaining);
        var settings = BuildSettings();
        PersistSettings(settings);
        await RunPrintAsync(_lastRun.Document, plan, settings, L10n.T("Devam baskısı"), PrintKind.Production);
    }

    /// <summary>
    /// {SSCC} kullanan tasarım: GS1 profili hazır mı (önek girilmiş ve doğrulanmış) ve kullanıcı numara ayrılmasını
    /// onaylıyor mu? Numaralar baskı anında ayrılır; basılamayanlar da tekrar kullanılmaz.
    /// </summary>
    private bool ConfirmSscc(int labelCount)
    {
        var context = Gs1Integration.Context;
        var blockers = context.Profile.GetSsccBlockers();
        if (blockers.Count > 0)
        {
            _main.Dialogs.ShowMessage("SSCC",
                L10n.T("Bu etikette {SSCC} alanı var: her etikete baskı anında yeni bir SSCC numarası verilir.\n\n") +
                string.Join("\n", blockers) + L10n.T("\n\nAyarlar → GS1 Firma Profili bölümünden firma önekini girip üyelik belgenizle doğrulayın."),
                MessageKind.Warning);
            return false;
        }
        long? next = null;
        try
        {
            next = context.Registry.PeekNextSerial(context.Profile);
        }
        catch (Gs1Exception)
        {
            // Bilgi amaçlı; ayırma sırasında hata ayrıca raporlanır.
        }
        return _main.Dialogs.Confirm("SSCC",
            L10n.T($"{labelCount} etikete yeni SSCC numarası verilecek") + (next is { } n ? L10n.T($" (sıradaki seri: {n:N0})") : "") + ".\n\n" +
            L10n.T("Numaralar etiket üretilirken kalıcı olarak kaydedilir. Basılamayan veya iptal edilen numaralar da tekrar kullanılmaz.\n\nDevam edilsin mi?"),
            L10n.T("Yazdır"), L10n.T("Vazgeç"));
    }

    private enum PrintKind
    {
        Production,
        Test,
    }

    private async Task RunPrintAsync(LabelDocument document, PrintPlan plan, LabelPrinterSettings settings, string title, PrintKind kind)
    {
        // Sayaçlar baskı boyunca kilitlenir (başka baskı aynı sayaçtan değer alamaz).
        CounterPrintTracker counters;
        try
        {
            counters = CounterPrintTracker.Begin(CounterIntegration.Store, document, TimeSpan.FromSeconds(3));
        }
        catch (CounterException ex)
        {
            _main.Dialogs.ShowMessage(title, ex.Message, MessageKind.Warning);
            return;
        }

        SsccPrintTracker tracker;
        try
        {
            tracker = plan.UsesSscc
                ? SsccPrintTracker.Begin(Gs1Integration.Context, $"{title}: {document.Name}")
                : SsccPrintTracker.Inactive(Gs1Integration.Context);
        }
        catch (Gs1Exception ex)
        {
            counters.Dispose();
            _main.Dialogs.ShowMessage(title, ex.Message, MessageKind.Warning);
            return;
        }

        IsBusy = true;
        LastSuccess = null;
        ReportLines.Clear();
        ReportText = "";
        ProgressValue = 0;
        ProgressText = L10n.T("Hazırlanıyor…");
        _cts = new CancellationTokenSource();
        var media = LabelMedia.FromSetup(document.Label);
        var skipped = new List<string>(plan.Skipped);
        var renderWarnings = new HashSet<string>();
        var recorder = new PrintJobRecorder(plan.Items);
        int spooledPages = 0;
        int ssccSpooled = 0;
        var progress = new Progress<PrintProgress>(p =>
        {
            ProgressValue = p.Fraction * 100;
            ProgressText = p.ToString();
        });
        PrintReport report;
        try
        {
            using var producer = new LabelProducer(document, _main.Session.Assets, VerifyBarcodes, _main.Session.ImageResolver);
            if (settings.Method == PrintMethod.TsplRaw)
            {
                IEnumerable<LabelPage> Pages()
                {
                    for (int index = 0; index < plan.Items.Count; index++)
                    {
                        var item = plan.Items[index];
                        // {SSCC} ve sayaç değerleri tam bu etiket üretilmeden önce verilir.
                        var (labelItem, sscc) = tracker.Prepare(item);
                        labelItem = counters.Prepare(labelItem);
                        var (bitmap, problems, warnings) = producer.Produce(labelItem, settings.DotsPerMm);
                        lock (renderWarnings) foreach (var w in warnings) renderWarnings.Add(w);
                        if (bitmap is null)
                        {
                            string reason = string.Join("; ", problems);
                            tracker.ProductionFailed(sscc, reason);
                            counters.ProductionFailed();
                            recorder.Failed(index, reason);
                            lock (skipped) skipped.Add($"{item.Describe()}: {reason}");
                            continue;
                        }
                        recorder.Produced(index);
                        yield return new LabelPage(bitmap, labelItem.Copies);
                    }
                }
                var service = PrintingServices.CreateTsplService(L10n.T($"Etiket Tasarım – {document.Name}"), settings.SingleJob);
                report = await service.PrintAsync(media, settings, Pages(), plan.TotalLabels, progress, _cts.Token);
                spooledPages = report.LabelsSpooled;
                ssccSpooled = report.LabelsSpooled;
            }
            else
            {
                // Sürücü yolu: sürücünün gerçek çözünürlüğü sorgulanır, geçersiz etiketler önceden ayıklanır.
                string? probeError = null;
                var info = await Task.Run(() => DriverPrintService.Probe(settings.PrinterName, media, out probeError));
                double dpmm = info is { RenderDotsPerMmX: > 0 } page ? page.RenderDotsPerMmX : settings.DotsPerMm;
                ProgressText = L10n.T("Etiketler doğrulanıyor…");
                var valid = await Task.Run(() =>
                {
                    var list = new List<int>();
                    for (int index = 0; index < plan.Items.Count; index++)
                    {
                        _cts.Token.ThrowIfCancellationRequested();
                        var item = plan.Items[index];
                        var (bitmap, problems, warnings) = producer.Produce(item, dpmm);
                        lock (renderWarnings) foreach (var w in warnings) renderWarnings.Add(w);
                        if (bitmap is null)
                        {
                            string reason = string.Join("; ", problems);
                            recorder.Failed(index, reason);
                            lock (skipped) skipped.Add($"{item.Describe()}: {reason}");
                        }
                        else
                        {
                            list.Add(index);
                        }
                    }
                    return list;
                });
                foreach (int index in valid) recorder.Produced(index);
                var service = new DriverPrintService { DocumentName = L10n.T($"Etiket Tasarım – {document.Name}"), AllowPrintWhenOffline = settings.AllowPrintWhenOffline };
                // {SSCC} ve sayaç: doğrulama önizleme değerleriyle yapıldı; gerçek değerler sayfa sürücüye çizilirken (bir kez) verilir.
                var prepared = new Dictionary<int, PrintItem>();
                PrintItem ItemFor(int pageIndex)
                {
                    var original = plan.Items[valid[pageIndex]];
                    if (!tracker.IsActive && !counters.IsActive) return original;
                    lock (prepared)
                    {
                        if (!prepared.TryGetValue(pageIndex, out var item))
                        {
                            item = counters.Prepare(tracker.Prepare(original).Item);
                            prepared[pageIndex] = item;
                        }
                        return item;
                    }
                }
                bool numbered = tracker.IsActive || counters.IsActive;
                report = await service.PrintAsync(settings.PrinterName, media, valid.Count,
                    (pageIndex, dx, dy) => producer.Produce(ItemFor(pageIndex), dx).Bitmap
                                           ?? (numbered ? throw new InvalidOperationException(L10n.T("Etiket görüntüsü üretilemedi.")) : new Core.Imaging.MonoBitmap(1, 1)),
                    pageIndex => plan.Items[valid[pageIndex]].Copies, progress, _cts.Token);
                // Sürücü işi bütündür: ya tüm sayfalar kuyruğa girer ya hiçbiri.
                spooledPages = report.Spooled ? valid.Count : 0;
                ssccSpooled = report.Spooled ? report.LabelsSpooled : 0;
                if (probeError is not null) report.Warnings.Add(probeError);
            }
        }
        catch (OperationCanceledException)
        {
            report = new PrintReport { Method = settings.Method, PrinterName = settings.PrinterName, Canceled = true };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            report = new PrintReport { Method = settings.Method, PrinterName = settings.PrinterName };
            report.Errors.Add(L10n.T("Beklenmeyen baskı hatası: ") + ex.Message);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }

        foreach (var w in renderWarnings.Take(10)) report.Warnings.Add(w);
        var sscc = tracker.Complete(ssccSpooled, report.Canceled);
        var counterResult = counters.Complete(spooledPages);
        counters.Dispose();

        // Kaldığı yerden devam: gönderilmemiş öğeler.
        var remaining = recorder.Remaining(spooledPages);
        _lastRun = kind == PrintKind.Production && remaining.Count > 0 ? new LastRun(document, plan, remaining) : null;
        CanResume = _lastRun is not null;
        ResumeText = _lastRun is null ? "" : L10n.T($"Kaldığı yerden devam ({remaining.Sum(i => i.Copies)} etiket)");

        ShowReport(title, report, skipped, sscc, counterResult);
        if (_lastRun is not null)
            ReportLines.Add(new ReportLine("warning", L10n.T($"{remaining.Sum(i => i.Copies)} etiket gönderilmedi. \"{ResumeText}\" ile yalnızca bunları basabilirsiniz.")));

        string? historyProblem = RecordHistory(title, kind, document, plan, settings, report, recorder.ToHistory(spooledPages), skipped, sscc, counterResult,
            tracker.SentNumbers, counters.UsedRanges);
        if (historyProblem is not null) ReportLines.Add(new ReportLine("warning", historyProblem));
        if (counters.IsActive) _main.RefreshPreviewFields();
        _main.StatusMessage = report.Success ? L10n.T($"{title}: yazıcı kuyruğuna iletildi. Etiketi yazıcıdan kontrol edin.") : L10n.T($"{title}: sorun var, raporu inceleyin.");
    }

    /// <summary>İşi baskı geçmişine yazar; yazılamazsa uyarı metni döndürür (baskı sonucu etkilenmez).</summary>
    private string? RecordHistory(string title, PrintKind kind, LabelDocument document, PrintPlan plan, LabelPrinterSettings settings,
        PrintReport report, List<PrintHistoryItem> items, List<string> skipped, (string Text, bool Failed)? sscc, (string Text, bool Failed)? counters,
        IReadOnlyList<string> ssccSent, IReadOnlyDictionary<string, string> counterRanges)
    {
        try
        {
            var entry = new PrintHistoryEntry
            {
                Title = title,
                DocumentName = document.Name,
                UseData = plan.UsesData,
                Copies = plan.UsesData ? 1 : plan.TotalLabels,
                StartNumber = items.Count > 0 ? items.Min(i => i.Sequence) : StartNumber,
                TotalLabels = plan.TotalLabels,
                PrinterName = settings.PrinterName,
                Method = settings.Method,
                Density = settings.EffectiveDensity,
                Speed = settings.EffectiveSpeed,
                Rotate180 = settings.Rotate180,
                OffsetXMm = settings.OffsetXMm,
                OffsetYMm = settings.OffsetYMm,
                SingleJob = settings.SingleJob,
                ProfileName = SelectedProfile is { } profile && Matches(profile) ? profile.Name : null,
                DataGenerated = report.DataGenerated,
                LabelsGenerated = report.LabelsGenerated,
                CopiesTotal = report.CopiesTotal,
                Spooled = report.Spooled,
                LabelsSpooled = report.LabelsSpooled,
                JobIds = [.. report.JobIds],
                QueueOutcome = report.QueueOutcome?.ToString(),
                QueueMessage = report.QueueMessage,
                Canceled = report.Canceled,
                Success = report.Success,
                Errors = [.. report.Errors],
                Warnings = [.. report.Warnings.Distinct().Take(30)],
                Skipped = [.. skipped.Take(200)],
                SsccSummary = sscc?.Text,
                CounterSummary = counters?.Text,
                SsccFirst = ssccSent.Count > 0 ? ssccSent[0] : null,
                SsccLast = ssccSent.Count > 0 ? ssccSent[^1] : null,
                SsccSentCount = ssccSent.Count,
                CounterRanges = counterRanges.Count > 0 ? new Dictionary<string, string>(counterRanges) : null,
                Items = items,
            };
            if (kind == PrintKind.Production)
            {
                string? templatePath = _main.Session.Project.FilePath;
                entry.TemplatePath = templatePath;
                entry.TemplateHash = PrintHistoryStore.HashFile(templatePath);
                entry.SnapshotFile = PrintingServices.History.SaveSnapshot(new LabelProject(document, _main.Session.Assets));
                if (plan.UsesData && _main.Data.Table is { } table)
                {
                    entry.DataFile = table.SourcePath;
                    entry.DataSheet = table.SheetName;
                    entry.DataDelimiter = table.Delimiter?.ToString();
                    entry.DataEncoding = table.EncodingName;
                    try
                    {
                        var info = new FileInfo(table.SourcePath);
                        if (info.Exists)
                        {
                            entry.DataFileLength = info.Length;
                            entry.DataFileWriteTimeUtc = info.LastWriteTimeUtc;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                    {
                    }
                }
            }
            PrintingServices.History.Append(entry);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return L10n.T("Baskı geçmişine yazılamadı: ") + ex.Message;
        }
    }

    private void ShowReport(string title, PrintReport report, List<string> skipped, (string Text, bool Failed)? sscc = null,
        (string Text, bool Failed)? counters = null)
    {
        ReportLines.Clear();
        ReportLines.Add(new ReportLine(report.DataGenerated ? "ok" : "error",
            report.DataGenerated
                ? L10n.T($"Baskı verisi üretildi: {report.LabelsGenerated} etiket görüntüsü, toplam {report.CopiesTotal} adet ({FormatBytes(report.BytesGenerated)}).")
                : L10n.T("Baskı verisi üretilemedi.")));
        ReportLines.Add(new ReportLine(report.Spooled ? "ok" : "error",
            report.Spooled
                ? L10n.T($"Windows yazdırma kuyruğuna gönderildi (iş no: {string.Join(", ", report.JobIds)}).")
                : L10n.T("Windows kuyruğuna gönderilmedi.")));
        if (report.QueueOutcome is { } outcome)
        {
            bool ok = outcome is SpoolOutcome.LeftQueue or SpoolOutcome.Printed;
            ReportLines.Add(new ReportLine(ok ? "ok" : outcome == SpoolOutcome.TimedOut ? "warning" : "error", L10n.T("Kuyruk durumu: ") + report.QueueMessage));
        }
        ReportLines.Add(new ReportLine("info", PrintReport.PhysicalVerificationText));
        if (sscc is { } ssccResult) ReportLines.Add(new ReportLine(ssccResult.Failed ? "error" : "info", ssccResult.Text));
        if (counters is { } counterResult) ReportLines.Add(new ReportLine(counterResult.Failed ? "error" : "info", counterResult.Text));
        if (report.Canceled) ReportLines.Add(new ReportLine("warning", L10n.T("Baskı kullanıcı tarafından iptal edildi; gönderilmiş etiketler basılmaya devam edebilir.")));
        foreach (var e in report.Errors) ReportLines.Add(new ReportLine("error", e));
        foreach (var w in report.Warnings) ReportLines.Add(new ReportLine("warning", w));
        foreach (var s in skipped.Take(50)) ReportLines.Add(new ReportLine("error", L10n.T("Atlandı – ") + s));
        if (skipped.Count > 50) ReportLines.Add(new ReportLine("error", L10n.T($"… ve {skipped.Count - 50} satır daha atlandı.")));

        var sb = new StringBuilder();
        sb.AppendLine(title + L10n.T(" raporu"));
        foreach (var line in ReportLines) sb.AppendLine("• " + line.Text);
        ReportText = sb.ToString();
        LastSuccess = report.Success && skipped.Count == 0;
        ProgressValue = report.Success ? 100 : ProgressValue;
        ProgressText = report.Success ? L10n.T("Tamamlandı") : report.Canceled ? L10n.T("İptal edildi") : L10n.T("Sorun var");
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        ProgressText = SingleJob
            ? L10n.T("İptal ediliyor (tek parça: o ana kadar yazılan etiketler tek iş olarak kuyruğa verilir)…")
            : L10n.T("İptal ediliyor (gönderimi süren etiket tamamlanınca durur)…");
    }

    /// <summary>Medya sensörü kalibrasyonu: TSPL GAPDETECT/BLINEDETECT gönderir (yazıcı birkaç etiket ilerletir).</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Calibrate() => await DetectMediaAsync();

    /// <summary>
    /// Sensör kalibrasyonunu kullanıcı onayıyla gönderir. Vazgeçilirse null, gönderildiyse kuyruğa iletilip iletilmediği.
    /// </summary>
    public async Task<bool?> DetectMediaAsync()
    {
        if (SelectedPrinter is null || IsBusy) return null;
        string command = Label.Media == MediaType.BlackMark ? "BLINEDETECT" : "GAPDETECT";
        if (!_main.Dialogs.Confirm(L10n.T("Sensör kalibrasyonu"),
                L10n.T($"Yazıcıya otomatik etiket/boşluk algılama komutu ({command}) gönderilecek. Yazıcı birkaç etiket ilerletebilir.\n\n") +
                L10n.T("Alternatif (düğmeyle): yazıcıyı kapatın, FEED düğmesini basılı tutarak açın; ışık kırmızı yanıp sönerken bırakın.\n\nKomut gönderilsin mi?"),
                L10n.T("Gönder"), L10n.T("Vazgeç")))
            return null;
        await SendRawAsync(TsplCommandBuilder.BuildGapDetect(LabelMedia.FromSetup(Label)), L10n.T("Sensör kalibrasyonu"));
        return LastSuccess == true;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Feed()
    {
        if (SelectedPrinter is null) return;
        await SendRawAsync(TsplCommandBuilder.BuildFormFeed(), L10n.T("Etiket ilerletme"));
    }

    private async Task SendRawAsync(byte[] data, string title)
    {
        IsBusy = true;
        try
        {
            var report = await PrintingServices.CreateTsplService(L10n.T("Etiket Tasarım – ") + title, false)
                .SendRawAsync(SelectedPrinter!.Name, data, L10n.T("Etiket Tasarım – ") + title, default, AllowWhenOffline);
            ShowReport(title, report, []);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Kalibrasyon yardımcısı: sensör kalibrasyonu, cetvelli etiket ve ölçüme göre ofset.</summary>
    [RelayCommand]
    private void OpenCalibration()
    {
        var calibration = new CalibrationViewModel(this, _main.Dialogs);
        try
        {
            _main.Dialogs.ShowDialog(calibration);
        }
        finally
        {
            calibration.Close();
        }
    }

    /// <summary>Kalıcı sayaçları göster/ayarla.</summary>
    [RelayCommand]
    private void OpenCounters()
    {
        _main.Dialogs.ShowDialog(new CountersViewModel(CounterIntegration.Store, _main.Dialogs, _main.Session.Document));
        _main.RefreshPreviewFields();
        RunPreflight();
    }

    /// <summary>Tanılama: mevcut etiketin TSPL komut özetini gösterir (ikili veri hariç).</summary>
    [RelayCommand]
    private void ShowTspl()
    {
        var settings = BuildSettings();
        var media = LabelMedia.FromSetup(Label);
        var result = _main.Session.RenderLabel(RenderMode.Design, _main.Data.GetPreviewFields(DateTime.Now), settings.DotsPerMm);
        var data = TsplCommandBuilder.BuildJob(media, settings, [new LabelPage(result.Bitmap, 1)]);
        _main.Dialogs.ShowMessage(L10n.T("TSPL komutları"), TsplInspector.Describe(data), MessageKind.Info);
    }

    private static string FormatBytes(long bytes) =>
        bytes >= 1024 * 1024 ? (bytes / 1024.0 / 1024.0).ToString("0.0", Turkish) + " MB" : (bytes / 1024.0).ToString("0", Turkish) + " KB";
}
