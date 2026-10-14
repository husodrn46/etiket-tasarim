using AkelEtiket.PrintEngine;
using AkelEtiket.ProjectManager;
using AkelEtiket.Core;

namespace AkelEtiket.Editor.Services;

public enum AppTheme
{
    Dark,
    Light,
}

/// <summary>Pencere konumu.</summary>
public sealed class WindowPlacementSettings
{
    /// <summary>null = henüz kaydedilmedi (ekranın ortasında açılır).</summary>
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 1440;
    public double Height { get; set; } = 900;
    public bool Maximized { get; set; } = true;
}

/// <summary>Uygulama ayarları (%APPDATA%\AKEL Etiket\ayarlar.json).</summary>
public sealed class AppSettings
{
    public int SettingsVersion { get; set; } = 1;
    public CompanyProfile Company { get; set; } = new();
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public LabelPrinterSettings Printer { get; set; } = new();
    public RecentFilesList Recent { get; set; } = new();

    public double GridSizeMm { get; set; } = 1.0;
    public bool ShowGrid { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public bool SnapToObjects { get; set; } = true;
    public bool ShowRulers { get; set; } = true;

    /// <summary>Otomatik kurtarma aralığı (saniye); 0 = kapalı.</summary>
    public int AutoRecoverySeconds { get; set; } = 60;

    public double DefaultLabelWidthMm { get; set; } = 100;
    public double DefaultLabelHeightMm { get; set; } = 100;

    /// <summary>Baskıdan önce her barkodu okuyucuyla doğrula.</summary>
    public bool VerifyBarcodesBeforePrint { get; set; } = true;

    /// <summary>Kullanıcının GS1 belgesinden girdiği firma öneki. Ülke kodu veya GLN'den türetilmez.</summary>
    public string Gs1CompanyPrefix { get; set; } = "";

    /// <summary>Otomatik EAN üretiminde sıradaki ürün numarası.</summary>
    public long NextItemNumber { get; set; } = 1;

    public string? TemplatesFolder { get; set; }
    public string? LastProjectFolder { get; set; }
    public string? LastImageFolder { get; set; }
    public string? LastDataFolder { get; set; }
    public string? LastQuickTemplatePath { get; set; }

    public bool FirstRunCompleted { get; set; }
    public bool OnboardingCompleted { get; set; }
    public WindowPlacementSettings Window { get; set; } = new();

    public string EffectiveTemplatesFolder =>
        string.IsNullOrWhiteSpace(TemplatesFolder) ? TemplateLibrary.DefaultFolder : TemplatesFolder;

    /// <summary>Okunan dosyadaki eksik/bozuk değerleri onarır.</summary>
    public void Normalize()
    {
        Company ??= new CompanyProfile();
        Company.Normalize();
        Gs1CompanyPrefix ??= "";
        Printer ??= new LabelPrinterSettings();
        Recent ??= new RecentFilesList();
        Recent.Items ??= [];
        Window ??= new WindowPlacementSettings();
        if (GridSizeMm is <= 0 or > 50 || double.IsNaN(GridSizeMm)) GridSizeMm = 1.0;
        if (AutoRecoverySeconds is < 0 or > 3600) AutoRecoverySeconds = 60;
        if (DefaultLabelWidthMm is < 5 or > 1000) DefaultLabelWidthMm = 100;
        if (DefaultLabelHeightMm is < 5 or > 1000) DefaultLabelHeightMm = 100;
        if (Printer.DotsPerMm is < 4 or > 24 || double.IsNaN(Printer.DotsPerMm)) Printer.DotsPerMm = 8.0;
    }
}
