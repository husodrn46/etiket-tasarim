using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AkelEtiket.Core;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;
using AkelEtiket.DataImport;
using AkelEtiket.Design.Data;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.Templates;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Tests;

[Collection("Yapay zekâ masaüstü")]
public sealed class OnboardingAndUsabilityTests : IDisposable
{
    private readonly string _folder = Path.Combine(TestDataRoot.Root, "kullanim-" + Guid.NewGuid().ToString("N"));
    public OnboardingAndUsabilityTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Setup_DraftIsIsolated_AndNeverInfersGs1()
    {
        var settings = new AppSettings { Company = new() { Name = "Önceki firma" } };
        var setup = new FirstSetupViewModel(settings, new ScriptedDialogs());
        setup.Profile.CompanyName = "Yeni firma";
        setup.Profile.DefaultWidthMm = 80; setup.Profile.DefaultHeightMm = 50;
        Assert.Equal("Önceki firma", settings.Company.Name);
        Assert.False(settings.OnboardingCompleted);
        setup.ApplyTo(settings);
        Assert.Equal("Yeni firma", settings.Company.Name);
        Assert.Equal(80, settings.DefaultLabelWidthMm);
        Assert.True(settings.OnboardingCompleted);
        Assert.Equal("", settings.Gs1CompanyPrefix);
        Assert.Equal(1, settings.NextItemNumber);
    }

    [Fact]
    public void Setup_PresetsAndWidthValidation_GuardProgressAndApply()
    {
        var setup = new FirstSetupViewModel(new AppSettings(), new ScriptedDialogs());
        setup.SelectedPreset = setup.Presets.Single(p => p.WidthMm == 58);
        Assert.Equal(58, setup.Profile.DefaultWidthMm);
        setup.NextCommand.Execute(null);
        setup.Profile.DefaultWidthMm = 120;
        Assert.NotNull(setup.ValidationProblem);
        Assert.False(setup.NextCommand.CanExecute(null));
        Assert.Throws<InvalidOperationException>(() => setup.ApplyTo(new AppSettings()));
        setup.Profile.MaxPrintWidthMm = 150;
        Assert.True(setup.NextCommand.CanExecute(null));
        setup.NextCommand.Execute(null);
        Assert.True(setup.IsFinishStep);
        Assert.False(setup.NextCommand.CanExecute(null));
    }

    [Fact]
    public void FirstRun_AppliesSetupBeforeCreatingLabel_AndDoesNotRepeatForExistingUsers()
    {
        var dialogs = new ScriptedDialogs
        {
            DialogAnswer = vm =>
            {
                if (vm is not FirstSetupViewModel setup) return false;
                setup.Profile.CompanyName = "Yeni firma";
                setup.Profile.DefaultWidthMm = 80; setup.Profile.DefaultHeightMm = 50;
                setup.OpenPreview = false; setup.AddTemplates = false;
                return true;
            },
        };
        using var main = CreateMain(dialogs);
        main.Initialize([]);
        Assert.Single(dialogs.Dialogs.OfType<FirstSetupViewModel>());
        Assert.True(main.Settings.OnboardingCompleted);
        Assert.Equal(80, main.Session.Document.Label.WidthMm);
        Assert.Equal(50, main.Session.Document.Label.HeightMm);
        Assert.Empty(main.Templates.Library.List());
        dialogs.Dialogs.Clear();
        main.Initialize([]);
        Assert.Empty(dialogs.Dialogs.OfType<FirstSetupViewModel>());
    }

    [Fact]
    public void ExistingInstallation_DoesNotForceNewWizard()
    {
        var dialogs = new ScriptedDialogs();
        using var main = CreateMain(dialogs);
        main.Settings.FirstRunCompleted = true;
        main.Initialize([]);
        Assert.Empty(dialogs.Dialogs.OfType<FirstSetupViewModel>());
    }

    [Theory]
    [InlineData(80, 50)] [InlineData(58, 40)] [InlineData(100, 150)] [InlineData(70, 35)]
    public void StarterProject_UsesChosenDimensions(double width, double height)
    {
        var project = SampleTemplates.CreateForSize(width, height, new() { Name = "Deneme" });
        Assert.Equal(width, project.Document.Label.WidthMm);
        Assert.Equal(height, project.Document.Label.HeightMm);
        Assert.All(project.Document.Elements, e =>
        {
            Assert.True(e.X >= 0 && e.Y >= 0);
            Assert.True(e.X + e.Width <= width && e.Y + e.Height <= height);
        });
    }

    [Fact]
    public void Mapping_PreservesFormatsOffsetsConditionsAndOriginalData()
    {
        var document = LabelDocument.CreateBlank();
        document.Elements.Add(new TextElement { Text = "{UrunAdi:BUYUK} {SonTarih+30:dd.MM.yyyy} {TARIH}" });
        document.Elements.Add(new BarcodeElement { Content = "{Barkod}" });
        document.Elements.Add(new ImageElement { SourceField = "Resim", VisibleWhen = new("UrunAdi") });
        string[] columns = ["Title", "Expiry", "Code", "Picture"];
        var record = new DataRecord(2, ["örnek", "01.01.2026", "0001234567895", "resim.png"], columns);
        var table = new DataSourceTable("file.csv", "CSV", null, 1, columns, [record], []);
        var mapping = new ColumnMappingViewModel(document, table);
        Assert.False(mapping.IsValid);
        foreach (var row in mapping.Rows)
            row.SelectedColumn = row.Field switch { "UrunAdi" => "Title", "SonTarih" => "Expiry", "Barkod" => "Code", _ => "Picture" };
        Assert.True(mapping.IsValid);
        Assert.Equal("0001234567895", mapping.Rows.Single(r => r.Field == "Barkod").Example);
        var mapped = mapping.CreateMappedDocument();
        Assert.Equal("{Title:BUYUK} {Expiry+30:dd.MM.yyyy} {TARIH}", ((TextElement)mapped.Elements[0]).Text);
        Assert.Equal("{Code}", ((BarcodeElement)mapped.Elements[1]).Content);
        Assert.Equal("Picture", ((ImageElement)mapped.Elements[2]).SourceField);
        Assert.Equal("Title", mapped.Elements[2].VisibleWhen!.Field);
        Assert.Equal("{Barkod}", ((BarcodeElement)document.Elements[1]).Content);
        Assert.Equal("0001234567895", record.GetValue("Code"));
        using var session = EditorTestHelpers.CreateSession(new LabelProject(document));
        session.ReplaceContent(mapped, null, "Sütun eşleştirme");
        session.Undo();
        Assert.Equal("{Barkod}", ((BarcodeElement)session.Document.Elements[1]).Content);
    }

    [Fact]
    public void CompanyGallery_HasPreviewsAndSearch_PreservesExistingTemplate()
    {
        using var main = CreateMain();
        main.Settings.Company.Name = "Örnek Firma";
        Assert.Equal(8, main.Templates.AddCompanyTemplates());
        Assert.All(main.Templates.Items, item => Assert.NotEmpty(item.PreviewPng!));
        string first = main.Templates.Items[0].Info.FilePath;
        byte[] bytes = File.ReadAllBytes(first);
        Assert.Equal(8, main.Templates.AddCompanyTemplates());
        Assert.Equal(bytes, File.ReadAllBytes(first));
        main.Templates.Category = "Demirbaş";
        Assert.Equal(2, main.Templates.Items.Count);
        main.Templates.Query = "70";
        Assert.Equal(2, main.Templates.Items.Count);
        main.Templates.Query = "bulunamayan";
        Assert.Empty(main.Templates.Items);
    }

    [Fact]
    public void Backup_RoundTrip_EmbedsTemplatesAndProducts_LeavesSourcesUntouched()
    {
        using var main = CreateMain();
        main.Settings.Company.Name = "Örnek Firma";
        main.Settings.Gs1CompanyPrefix = "1234567";
        main.Settings.NextItemNumber = 42;
        main.Settings.LastDataFolder = _folder;
        main.Settings.Recent.Add(Path.Combine(_folder, "tasarim.akelabel"));
        main.Templates.Library.SaveAsTemplate(SampleTemplates.CreateAssetLabel(), "Demirbaş", null, false);
        string original = main.Templates.Library.GetPathForName("Demirbaş");
        byte[] originalBytes = File.ReadAllBytes(original);
        var products = new ProductCatalogData { Columns = ["Kod", "Barkod"], KeyColumn = "Kod", SourceFile = _folder,
            Products = [new() { Key = "P1", Fields = new() { ["Kod"] = "P1", ["Barkod"] = "0001234567895" } }] };
        string path = Path.Combine(_folder, "yedek.etiketyedek");
        WorkspaceBackup.Export(path, main.Settings, main.Templates.Library, products);
        var restored = WorkspaceBackup.Read(path);
        Assert.Equal("Örnek Firma", restored.Settings.Company.Name);
        Assert.Equal("", restored.Settings.Gs1CompanyPrefix);
        Assert.Equal(1, restored.Settings.NextItemNumber);
        Assert.Null(restored.Settings.LastDataFolder);
        Assert.Empty(restored.Settings.Recent.Items);
        Assert.Equal("", restored.Products.SourceFile);
        Assert.Equal("0001234567895", restored.Products.Products[0].Fields["Barkod"]);
        Assert.Equal(1, restored.TemplateCount);
        string output = Path.Combine(_folder, "geri");
        WorkspaceBackup.ExtractToNewFolder(restored, output);
        Assert.Equal(originalBytes, File.ReadAllBytes(Path.Combine(output, "templates", "Demirbaş.akelabel")));
        Assert.Throws<IOException>(() => WorkspaceBackup.ExtractToNewFolder(restored, output));
        Assert.Equal(originalBytes, File.ReadAllBytes(original));
        Assert.Equal(42, main.Settings.NextItemNumber);
        Assert.Throws<IOException>(() => WorkspaceBackup.Export(path, main.Settings, main.Templates.Library, products));
        Assert.NotEmpty(File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("../disari.txt")] [InlineData("templates/../disari.akelabel")] [InlineData("templates/a\\b.akelabel")] [InlineData("templates/CON.akelabel")]
    public void Backup_RejectsTraversal_BeforeAnyExtraction(string name)
    {
        var settings = JsonSerializer.SerializeToUtf8Bytes(new AppSettings(), ModelJson.FileOptions);
        var products = JsonSerializer.SerializeToUtf8Bytes(new ProductCatalogData(), ModelJson.FileOptions);
        var files = new Dictionary<string, byte[]> { ["settings.json"] = settings, ["products.json"] = products, [name] = [1] };
        string path = CreateArchive(files);
        Assert.Throws<InvalidDataException>(() => WorkspaceBackup.Read(path));
        Assert.False(File.Exists(Path.Combine(_folder, "disari.txt")));
    }

    [Fact]
    public void Backup_RejectsTamperedPayload()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["settings.json"] = JsonSerializer.SerializeToUtf8Bytes(new AppSettings(), ModelJson.FileOptions),
            ["products.json"] = JsonSerializer.SerializeToUtf8Bytes(new ProductCatalogData(), ModelJson.FileOptions),
        };
        string path = CreateArchive(files, tamper: true);
        Assert.Throws<InvalidDataException>(() => WorkspaceBackup.Read(path));
    }

    [Fact]
    public void CatalogRestore_PreservesPreviousListAndRejectsInvalidReplacement()
    {
        var catalog = new ProductCatalog(Path.Combine(_folder, "urunler.json"));
        var initial = new ProductCatalogData { Columns = ["Kod"], KeyColumn = "Kod", Products = [new() { Key = "Eski", Fields = new() { ["Kod"] = "Eski" } }] };
        catalog.RestoreBackup(initial);
        byte[] previous = File.ReadAllBytes(catalog.FilePath);
        var next = new ProductCatalogData { Columns = ["Kod"], KeyColumn = "Kod", Products = [new() { Key = "Yeni", Fields = new() { ["Kod"] = "Yeni" } }] };
        catalog.RestoreBackup(next);
        Assert.Equal("Yeni", Assert.Single(catalog.Data.Products).Key);
        Assert.Equal(previous, File.ReadAllBytes(Assert.Single(Directory.GetFiles(_folder, "*.onceki-*.json"))));
        Assert.Throws<InvalidDataException>(() => catalog.RestoreBackup(new() { Columns = ["Kod"], KeyColumn = "Kod", Products = [new() { Key = "" }] }));
        Assert.Equal("Yeni", Assert.Single(catalog.Data.Products).Key);
    }

    [Fact]
    public void BackupCommands_RestoreIntoNewLibrary_KeepOriginalFilesAndGs1Sequence()
    {
        var dialogs = new ScriptedDialogs();
        using var main = CreateMain(dialogs);
        main.Settings.Company.Name = "Yedek firma";
        main.Templates.Library.SaveAsTemplate(SampleTemplates.CreateAssetLabel(), "Envanter", null, false);
        string originalPath = main.Templates.Library.GetPathForName("Envanter");
        byte[] original = File.ReadAllBytes(originalPath);
        string[] columns = ["Kod"];
        main.Products.Import(new("kaynak.csv", "CSV", null, 1, columns, [new(2, ["YEDEK"], columns)], []), "Kod");
        string backupFile = Path.Combine(_folder, "aktar.etiketyedek");
        dialogs.SaveFileAnswer = backupFile;
        main.ExportWorkspaceBackupCommand.Execute(null);
        Assert.True(File.Exists(backupFile));
        main.Settings.Company.Name = "Mevcut firma";
        main.Settings.Gs1CompanyPrefix = "1234567";
        main.Settings.NextItemNumber = 900;
        main.Products.Import(new("mevcut.csv", "CSV", null, 1, columns, [new(2, ["MEVCUT"], columns)], []), "Kod");
        dialogs.OpenFileAnswer = backupFile;
        dialogs.ConfirmAnswer = _ => false;
        main.ImportWorkspaceBackupCommand.Execute(null);
        Assert.Equal("Mevcut firma", main.Settings.Company.Name);
        Assert.Equal("MEVCUT", Assert.Single(main.Products.Data.Products).Key);
        dialogs.ConfirmAnswer = _ => true;
        main.ImportWorkspaceBackupCommand.Execute(null);
        Assert.Equal("Yedek firma", main.Settings.Company.Name);
        Assert.Equal("YEDEK", Assert.Single(main.Products.Data.Products).Key);
        Assert.Equal("1234567", main.Settings.Gs1CompanyPrefix);
        Assert.Equal(900, main.Settings.NextItemNumber);
        Assert.NotEqual(Path.GetDirectoryName(originalPath), main.Settings.EffectiveTemplatesFolder);
        Assert.Single(main.Templates.Library.List());
        Assert.Equal(original, File.ReadAllBytes(originalPath));
        Assert.Equal(main.Settings.EffectiveTemplatesFolder,
            new JsonSettingsStore<AppSettings>(Path.Combine(_folder, "ayarlar.json")).Load().TemplatesFolder);
    }

    [Theory]
    [InlineData(-1, 0)] [InlineData(99, 2)]
    public void Setup_InvalidStepSelectionCannotIndexOutsideSteps(int requested, int expected)
    {
        var setup = new FirstSetupViewModel(new AppSettings(), new ScriptedDialogs()) { StepIndex = requested };
        Assert.Equal(expected, setup.StepIndex);
        Assert.Equal(setup.Steps[expected], setup.StepTitle);
    }

    [Fact]
    public void Setup_SaveFailureKeepsLiveSettingsAndAllowsRetry()
    {
        var dialogs = new ScriptedDialogs { DialogAnswer = vm =>
        {
            if (vm is not FirstSetupViewModel setup) return false;
            setup.Profile.CompanyName = "Kaydedilmemeli"; setup.OpenPreview = false; return true;
        }};
        using var main = CreateMain(dialogs);
        var previous = main.Settings;
        Directory.CreateDirectory(Path.Combine(_folder, "ayarlar.json.tmp"));
        main.Initialize([]);
        Assert.Same(previous, main.Settings);
        Assert.Equal("", main.Settings.Company.Name);
        Assert.False(main.Settings.FirstRunCompleted);
        Assert.False(main.Settings.OnboardingCompleted);
        Assert.Empty(main.Templates.Library.List());
        Assert.Contains(dialogs.Messages, m => m.Contains("Mevcut ayarlar korundu"));
    }

    [Fact]
    public void BackupCommands_SettingsSaveFailureRollsBackProductsAndDoesNotApplySettings()
    {
        var dialogs = new ScriptedDialogs();
        using var main = CreateMain(dialogs);
        main.Settings.Company.Name = "Yedek firma";
        string[] columns = ["Kod"];
        main.Products.Import(new("kaynak.csv", "CSV", null, 1, columns, [new(2, ["YEDEK"], columns)], []), "Kod");
        string backupFile = Path.Combine(_folder, "aktar.etiketyedek");
        dialogs.SaveFileAnswer = backupFile; main.ExportWorkspaceBackupCommand.Execute(null);
        main.Settings.Company.Name = "Mevcut firma";
        main.Products.Import(new("mevcut.csv", "CSV", null, 1, columns, [new(2, ["MEVCUT"], columns)], []), "Kod");
        main.SaveSettings();
        byte[] previousFile = File.ReadAllBytes(main.Products.FilePath);
        byte[] previousSettings = File.ReadAllBytes(Path.Combine(_folder, "ayarlar.json"));
        var previous = main.Settings;
        Directory.CreateDirectory(Path.Combine(_folder, "ayarlar.json.tmp"));
        dialogs.OpenFileAnswer = backupFile; main.ImportWorkspaceBackupCommand.Execute(null);
        Assert.Same(previous, main.Settings);
        Assert.Equal("Mevcut firma", main.Settings.Company.Name);
        Assert.Equal("MEVCUT", Assert.Single(main.Products.Data.Products).Key);
        Assert.Equal(previousFile, File.ReadAllBytes(main.Products.FilePath));
        Assert.Equal(previousSettings, File.ReadAllBytes(Path.Combine(_folder, "ayarlar.json")));
        Assert.DoesNotContain(dialogs.Messages, m => m.Contains("ürün geri yüklendi"));
    }

    [Fact]
    public void CatalogRestore_FailedCommitRollsBackFileAndMemory()
    {
        var catalog = new ProductCatalog(Path.Combine(_folder, "catalog.json"));
        var old = new ProductCatalogData { Columns = ["Kod"], KeyColumn = "Kod", Products = [new() { Key = "Eski", Fields = new() { ["Kod"] = "Eski" } }] };
        catalog.RestoreBackup(old);
        byte[] previous = File.ReadAllBytes(catalog.FilePath);
        Assert.Throws<IOException>(() => catalog.RestoreBackup(new(), () => throw new IOException("commit failed")));
        Assert.Equal("Eski", Assert.Single(catalog.Data.Products).Key);
        Assert.Equal(previous, File.ReadAllBytes(catalog.FilePath));
    }

    [Fact]
    public void Mapping_ExactColumnsWithSameFoldedNameRemainIndependentAndCanBeBackedUp()
    {
        var doc = LabelDocument.CreateBlank();
        doc.Elements.Add(new TextElement { Text = "{Ürün Kodu}|{UrunKodu}" });
        string[] columns = ["Ürün Kodu", "UrunKodu"];
        var record = new DataRecord(2, ["FIRST", "SECOND"], columns);
        var table = new DataSourceTable("test.csv", "CSV", null, 1, columns, [record], []);
        var mapping = new ColumnMappingViewModel(doc, table);
        Assert.Equal(2, mapping.Rows.Count);
        Assert.True(mapping.IsValid);
        var mapped = mapping.CreateMappedDocument();
        Assert.Equal("{Ürün Kodu}|{UrunKodu}", ((TextElement)mapped.Elements[0]).Text);
        Assert.Equal("FIRST|SECOND", FieldTemplate.Resolve(((TextElement)mapped.Elements[0]).Text, record.ToFieldSource()).Text);
        var catalog = new ProductCatalog(Path.Combine(_folder, "catalog.json"));
        var imported = catalog.Import(table, "Ürün Kodu");
        var backup = ProductCatalog.ValidateBackup(imported);
        Assert.Equal("FIRST", backup.Products[0].Fields["Ürün Kodu"]);
        Assert.Equal("SECOND", backup.Products[0].Fields["UrunKodu"]);
    }

    [Fact]
    public void Backup_DoesNotSerializeComputedMachineSpecificTemplatePath()
    {
        using var main = CreateMain();
        string path = Path.Combine(_folder, "tasima.etiketyedek");
        WorkspaceBackup.Export(path, main.Settings, main.Templates.Library, new ProductCatalogData());
        var backup = WorkspaceBackup.Read(path);
        using var json = JsonDocument.Parse(backup.Files["settings.json"]);
        Assert.False(json.RootElement.TryGetProperty("effectiveTemplatesFolder", out _));
        Assert.False(json.RootElement.TryGetProperty("templatesFolder", out _));
    }

    private string CreateArchive(Dictionary<string, byte[]> files, bool tamper = false)
    {
        string path = Path.Combine(_folder, Guid.NewGuid() + ".etiketyedek");
        var manifest = new WorkspaceBackupManifest { Files = files.ToDictionary(p => p.Key, p => Convert.ToHexStringLower(SHA256.HashData(p.Value))) };
        using var stream = File.Create(path); using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, bytes) in files)
        { using var output = zip.CreateEntry(name).Open(); output.Write(tamper && name == "products.json" ? [0] : bytes); }
        using var entry = zip.CreateEntry("manifest.json").Open();
        entry.Write(JsonSerializer.SerializeToUtf8Bytes(manifest, ModelJson.FileOptions));
        return path;
    }

    private MainViewModel CreateMain(ScriptedDialogs? dialogs = null)
    {
        var main = new MainViewModel(new AppServices { Dialogs = dialogs ?? new ScriptedDialogs(), Clipboard = new Clipboard(), Dispatcher = new ImmediateDispatcher(),
            ImageFactory = new FakeImageFactory(), SettingsStore = new(Path.Combine(_folder, "ayarlar.json")), RecoveryFolder = Path.Combine(_folder, "kurtarma") });
        main.Settings.TemplatesFolder = Path.Combine(_folder, "sablonlar");
        main.Templates.ChangeFolder(main.Settings.EffectiveTemplatesFolder);
        return main;
    }
    private sealed class Clipboard : IClipboardService
    {
        public void SetElements(string payloadJson, string plainText) { }
        public string? GetElements() => null;
        public bool ContainsElements() => false;
        public byte[]? GetImagePng() => null;
        public string? GetText() => null;
    }
}
