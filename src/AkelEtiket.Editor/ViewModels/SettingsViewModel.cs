using AkelEtiket.Core;
using AkelEtiket.Editor.Printing;
using AkelEtiket.Editor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AkelEtiket.ImageProcessor;
using SkiaSharp;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>Ayarlar penceresi (onaylanınca uygulanır).</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    public SettingsViewModel(AppSettings settings)
    {
        _settings = settings;
        UiLanguage = settings.UiLanguage ?? (settings.FirstRunCompleted ? "tr" : "auto");
        CompanyName = settings.Company.Name;
        CompanyAddress = settings.Company.Address;
        CompanyWebsite = settings.Company.Website;
        _logoPng = settings.Company.LogoPng?.ToArray();
        Theme = settings.Theme;
        GridSizeMm = settings.GridSizeMm;
        ShowGrid = settings.ShowGrid;
        SnapToGrid = settings.SnapToGrid;
        SnapToObjects = settings.SnapToObjects;
        ShowRulers = settings.ShowRulers;
        AutoRecoverySeconds = settings.AutoRecoverySeconds;
        DefaultWidthMm = settings.DefaultLabelWidthMm;
        DefaultHeightMm = settings.DefaultLabelHeightMm;
        VerifyBarcodes = settings.VerifyBarcodesBeforePrint;
        Resolution = PrintResolution.FromDotsPerMm(settings.Printer.DotsPerMm);
        MaxPrintWidthMm = settings.Printer.MaxPrintWidthMm;
        TemplatesFolder = settings.EffectiveTemplatesFolder;
        Gs1Prefix = settings.Gs1CompanyPrefix;
    }

    public IReadOnlyList<KeyValuePair<AppTheme, string>> ThemeChoices { get; } =
    [
        new(AppTheme.Dark, L10n.T("Koyu")),
        new(AppTheme.Light, L10n.T("Açık")),
    ];

    public IReadOnlyList<KeyValuePair<string, string>> LanguageChoices { get; } =
        [new("auto", "System / Otomatik"), new("en", "English"), new("tr", L10n.T("Türkçe"))];
    [ObservableProperty] public partial string UiLanguage { get; set; } = "auto";

    public IReadOnlyList<PrintResolution> Resolutions { get; } = PrintResolution.Standard;

    public IReadOnlyList<double> GridChoices { get; } = [0.5, 1, 2, 2.5, 5, 10];

    [ObservableProperty] public partial AppTheme Theme { get; set; }
    [ObservableProperty] public partial double GridSizeMm { get; set; }
    [ObservableProperty] public partial bool ShowGrid { get; set; }
    [ObservableProperty] public partial bool SnapToGrid { get; set; }
    [ObservableProperty] public partial bool SnapToObjects { get; set; }
    [ObservableProperty] public partial bool ShowRulers { get; set; }
    [ObservableProperty] public partial int AutoRecoverySeconds { get; set; }
    [ObservableProperty] public partial double DefaultWidthMm { get; set; }
    [ObservableProperty] public partial double DefaultHeightMm { get; set; }
    [ObservableProperty] public partial bool VerifyBarcodes { get; set; }
    [ObservableProperty] public partial PrintResolution Resolution { get; set; }
    [ObservableProperty] public partial double MaxPrintWidthMm { get; set; }
    [ObservableProperty] public partial string TemplatesFolder { get; set; }
    [ObservableProperty] public partial string Gs1Prefix { get; set; }
    [ObservableProperty] public partial string CompanyName { get; set; }
    [ObservableProperty] public partial string CompanyAddress { get; set; }
    [ObservableProperty] public partial string CompanyWebsite { get; set; }
    [ObservableProperty] public partial bool CreateCompanyTemplates { get; set; }
    private byte[]? _logoPng;
    public string LogoStatus => _logoPng is { Length: > 0 } ? L10n.T("Logo ayarlarda saklanıyor.") : L10n.T("Logo seçilmedi; firma adı kullanılır.");

    [RelayCommand]
    private void ChooseLogo()
    {
        string? path = Dialogs?.ShowOpenFileDialog(L10n.T("Firma logosunu seçin"), ImageLoader.OpenFileFilter);
        if (path is null) return;
        try
        {
            if (new FileInfo(path).Length > 10 * 1024 * 1024)
                throw new ImageLoadException("Logo en fazla 10 MB olabilir.");
            using var image = ImageLoader.Load(File.ReadAllBytes(path), Path.GetFileName(path));
            double scale = Math.Min(1, 1024.0 / Math.Max(image.Width, image.Height));
            using var bitmap = new SKBitmap(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                image.Draw(canvas, new SKRect(0, 0, bitmap.Width, bitmap.Height));
            }
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            _logoPng = png.ToArray();
            OnPropertyChanged(nameof(LogoStatus));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ImageLoadException)
        {
            Dialogs?.ShowMessage(L10n.T("Logo alınamadı"), ex.Message, MessageKind.Warning);
        }
    }

    [RelayCommand]
    private void ClearLogo()
    {
        _logoPng = null;
        OnPropertyChanged(nameof(LogoStatus));
    }

    public string SettingsFolder => ProjectManager.JsonSettingsStore<AppSettings>.DefaultFolder;

    /// <summary>GS1 Firma Profili penceresini açabilmek için diyalog servisi (yoksa düğme çalışmaz).</summary>
    public IDialogService? Dialogs { get; init; }

    /// <summary>GS1 Firma Profili (GLN, firma öneki, SSCC, kayıtlı GTIN'ler). Kendi Kaydet düğmesiyle hemen kaydedilir.</summary>
    [RelayCommand]
    private void OpenGs1Profile()
    {
        if (Dialogs is null) return;
        Dialogs.ShowDialog(new Gs1ProfileViewModel(Gs1Integration.Context, Dialogs));
    }

    /// <summary>Ayarları uygular. Tema veya çözünürlük değiştiyse true döner (görünüm yenilenmeli).</summary>
    public (bool ThemeChanged, bool ResolutionChanged, bool TemplatesFolderChanged) ApplyTo(AppSettings target)
    {
        bool themeChanged = target.Theme != Theme;
        bool resolutionChanged = Math.Abs(target.Printer.DotsPerMm - Resolution.DotsPerMm) > 1e-9;
        bool folderChanged = !string.Equals(target.EffectiveTemplatesFolder, TemplatesFolder, StringComparison.OrdinalIgnoreCase);
        target.UiLanguage = UiLanguage;
        target.Theme = Theme;
        target.Company = new CompanyProfile
        {
            Name = CompanyName, Address = CompanyAddress, Website = CompanyWebsite,
            LogoPng = _logoPng?.ToArray(),
        };
        target.Company.Normalize();
        target.GridSizeMm = GridSizeMm is > 0 and <= 50 ? GridSizeMm : 1;
        target.ShowGrid = ShowGrid;
        target.SnapToGrid = SnapToGrid;
        target.SnapToObjects = SnapToObjects;
        target.ShowRulers = ShowRulers;
        target.AutoRecoverySeconds = Math.Clamp(AutoRecoverySeconds, 0, 3600);
        target.DefaultLabelWidthMm = Math.Clamp(DefaultWidthMm, 5, 1000);
        target.DefaultLabelHeightMm = Math.Clamp(DefaultHeightMm, 5, 1000);
        target.VerifyBarcodesBeforePrint = VerifyBarcodes;
        target.Printer.DotsPerMm = Resolution.DotsPerMm;
        target.Printer.MaxPrintWidthMm = Math.Clamp(MaxPrintWidthMm, 20, 300);
        target.TemplatesFolder = string.IsNullOrWhiteSpace(TemplatesFolder) ? null : TemplatesFolder.Trim();
        target.Gs1CompanyPrefix = new string((Gs1Prefix ?? "").Where(char.IsAsciiDigit).ToArray());
        return (themeChanged, resolutionChanged, folderChanged);
    }
}
