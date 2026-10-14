using AkelEtiket.Editor.Services;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Tests;

/// <summary>v1'den geçiş: v1'in ayarları ilk açılışta devralınır; v1'in dosyası değişmez.</summary>
public sealed class LegacySettingsImportTests
{
    // v1 ayar dosyasının gerçek biçimi (kişisel yollar örnekle değiştirildi).
    private const string V1Json = """
        {
          "settingsVersion": 1,
          "theme": "Light",
          "printer": {
            "printerName": "barkod", "method": "TsplRaw", "dotsPerMm": 8, "maxPrintWidthMm": 108,
            "density": 12, "speed": 2, "rotate180": false, "offsetXMm": 0.25, "offsetYMm": -0.5,
            "tearMode": "Unchanged", "allowPrintWhenOffline": false
          },
          "recent": { "items": [ "C:\\Etiketler\\TST090 Ürün Etiketi 100x100.akelabel" ] },
          "gridSizeMm": 2, "showGrid": false, "snapToGrid": true, "snapToObjects": false, "showRulers": true,
          "autoRecoverySeconds": 120, "defaultLabelWidthMm": 100, "defaultLabelHeightMm": 100,
          "verifyBarcodesBeforePrint": true, "gs1CompanyPrefix": "868", "nextItemNumber": 5,
          "templatesFolder": "TEMPLATES",
          "lastProjectFolder": "C:\\Etiketler",
          "firstRunCompleted": true,
          "window": { "left": 257, "top": 47, "width": 1440, "height": 900, "maximized": true },
          "effectiveTemplatesFolder": "TEMPLATES"
        }
        """;

    private readonly string _folder = Path.Combine(TestDataRoot.Root, "v1ayar-" + Guid.NewGuid().ToString("N")[..6]);
    private readonly string _legacyFile;
    private readonly string _legacyTemplates;
    private readonly JsonSettingsStore<AppSettings> _target;

    public LegacySettingsImportTests()
    {
        string legacyFolder = Path.Combine(_folder, "v1");
        Directory.CreateDirectory(legacyFolder);
        _legacyFile = Path.Combine(legacyFolder, "ayarlar.json");
        _legacyTemplates = Path.Combine(_folder, "v1-Belgeler", "Şablonlar");
        _target = new JsonSettingsStore<AppSettings>(Path.Combine(_folder, "v2", "ayarlar.json"));
    }

    private void WriteLegacy(string templatesFolder) =>
        File.WriteAllText(_legacyFile, V1Json.Replace("TEMPLATES", templatesFolder.Replace("\\", "\\\\")));

    [Fact]
    public void FirstRun_TakesOverV1Settings_ButNotItsTemplateFolder()
    {
        WriteLegacy(_legacyTemplates);
        byte[] before = File.ReadAllBytes(_legacyFile);

        Assert.True(LegacySettingsImport.TryImport(_target, _legacyFile, _legacyTemplates));

        var s = _target.Load();
        Assert.Equal("barkod", s.Printer.PrinterName);
        Assert.Equal(12, s.Printer.Density);
        Assert.Equal(2, s.Printer.Speed);
        Assert.False(s.Printer.Rotate180);
        Assert.Equal(0.25, s.Printer.OffsetXMm);
        Assert.Equal(-0.5, s.Printer.OffsetYMm);
        Assert.Equal(AppTheme.Light, s.Theme);
        Assert.Equal(new[] { @"C:\Etiketler\TST090 Ürün Etiketi 100x100.akelabel" }, s.Recent.Items);
        Assert.Equal(@"C:\Etiketler", s.LastProjectFolder);
        Assert.Equal(2, s.GridSizeMm);
        Assert.False(s.ShowGrid);
        Assert.Equal(120, s.AutoRecoverySeconds);
        Assert.Equal("868", s.Gs1CompanyPrefix);
        Assert.Equal(5, s.NextItemNumber);
        // v2 kendi şablon klasörünü kullanır; ilk açılış akışı (şablon kopyalama, hoş geldiniz) yine çalışır.
        Assert.Null(s.TemplatesFolder);
        Assert.False(s.FirstRunCompleted);

        // v1'in dosyası değişmez, yanına dosya yazılmaz.
        Assert.Equal(before, File.ReadAllBytes(_legacyFile));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(_legacyFile)!));
    }

    [Fact]
    public void CustomTemplateFolder_IsKept()
    {
        string shared = Path.Combine(_folder, "Ortak Şablonlar");
        WriteLegacy(shared);

        Assert.True(LegacySettingsImport.TryImport(_target, _legacyFile, _legacyTemplates));

        Assert.Equal(shared, _target.Load().TemplatesFolder);
    }

    [Fact]
    public void ExistingV2Settings_AreNeverOverwritten()
    {
        WriteLegacy(_legacyTemplates);
        _target.Save(new AppSettings { Printer = { Density = 3 }, FirstRunCompleted = true });

        Assert.False(LegacySettingsImport.TryImport(_target, _legacyFile, _legacyTemplates));

        var s = _target.Load();
        Assert.Equal(3, s.Printer.Density);
        Assert.True(s.FirstRunCompleted);
    }

    [Fact]
    public void BrokenOrMissingV1File_IsIgnored_AndLeftAsIs()
    {
        File.WriteAllText(_legacyFile, "{ bozuk");

        Assert.False(LegacySettingsImport.TryImport(_target, _legacyFile, _legacyTemplates));
        Assert.False(LegacySettingsImport.TryImport(_target, Path.Combine(_folder, "yok.json"), _legacyTemplates));
        Assert.False(LegacySettingsImport.TryImport(_target, null, _legacyTemplates));

        Assert.False(File.Exists(_target.FilePath));
        Assert.Equal("{ bozuk", File.ReadAllText(_legacyFile));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(_legacyFile)!));
    }
}
