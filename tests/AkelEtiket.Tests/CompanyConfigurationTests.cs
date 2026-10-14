using AkelEtiket.Core;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.Templates;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.ProjectManager;
using AkelEtiket.ImageProcessor;
using SkiaSharp;

namespace AkelEtiket.Tests;

[Collection("Yapay zekâ masaüstü")]
public sealed class CompanyConfigurationTests
{
    [Fact]
    public void LogoImport_PreservesAspectRatio_EmbedsPng_AndRejectsInvalidReplacement()
    {
        string folder = Path.Combine(TestDataRoot.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "logo.png");
        using (var bitmap = new SKBitmap(1600, 800))
        {
            bitmap.Erase(SKColors.Black);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(path, data.ToArray());
        }
        var settings = new AppSettings();
        var dialogs = new ScriptedDialogs { OpenFileAnswer = path };
        var edit = new SettingsViewModel(settings) { Dialogs = dialogs };
        edit.ChooseLogoCommand.Execute(null);
        Assert.Null(settings.Company.LogoPng);
        edit.ApplyTo(settings);
        byte[] png = settings.Company.LogoPng!.ToArray();
        File.WriteAllText(path, "Bu bir görsel değil");
        edit.ChooseLogoCommand.Execute(null);
        Assert.Single(dialogs.Messages);
        edit.ApplyTo(settings);
        Assert.Equal(png, settings.Company.LogoPng);
        File.Delete(path);
        using var loaded = ImageLoader.Load(settings.Company.LogoPng!);
        Assert.Equal(1024, loaded.Width);
        Assert.Equal(512, loaded.Height);
    }

    [Fact]
    public void AddingCompanyTemplatesTwice_KeepsExistingBytes_AndHandlesLongNames()
    {
        string folder = Path.Combine(TestDataRoot.Root, Guid.NewGuid().ToString("N"));
        using var main = new MainViewModel(new AppServices
        {
            Dialogs = new ScriptedDialogs(), Clipboard = new EmptyClipboard(),
            Dispatcher = new ImmediateDispatcher(), ImageFactory = new FakeImageFactory(),
            SettingsStore = new JsonSettingsStore<AppSettings>(Path.Combine(folder, "ayarlar.json")),
            RecoveryFolder = Path.Combine(folder, "Kurtarma"),
        });
        main.Templates.ChangeFolder(Path.Combine(folder, "Şablonlar"));
        main.Settings.Company.Name = new string('A', 160) + ": Firma";
        main.Settings.Company.Address = "Örnek Sokak";
        Assert.Equal(6, main.Templates.AddCompanyTemplates());
        var first = Directory.GetFiles(main.Templates.Library.Folder).ToDictionary(f => f, File.ReadAllBytes);
        main.Settings.Company.Address = "Yeni Adres";
        Assert.Equal(6, main.Templates.AddCompanyTemplates());
        Assert.Equal(12, main.Templates.Library.List().Count);
        foreach (var (file, bytes) in first) Assert.Equal(bytes, File.ReadAllBytes(file));
        Assert.All(main.Templates.Library.List(), t => Assert.DoesNotContain(':', Path.GetFileName(t.FilePath)));
    }

    [Fact]
    public void OldSettings_KeepPrinterAndPreferences_WithoutInventingCompanyIdentity()
    {
        string folder = Path.Combine(TestDataRoot.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "ayarlar.json");
        const string json = """
            { "theme": "Light", "printer": { "printerName": "Mevcut Yazıcı", "density": 12 },
              "gs1CompanyPrefix": "1234567", "nextItemNumber": 82, "firstRunCompleted": true }
            """;
        File.WriteAllText(file, json);
        var store = new JsonSettingsStore<AppSettings>(file);
        var settings = store.Load();
        settings.Normalize();
        Assert.Null(store.LastLoadProblem);
        Assert.Equal("Mevcut Yazıcı", settings.Printer.PrinterName);
        Assert.Equal(12, settings.Printer.Density);
        Assert.Equal("1234567", settings.Gs1CompanyPrefix);
        Assert.Equal(82, settings.NextItemNumber);
        Assert.True(settings.FirstRunCompleted);
        Assert.Empty(settings.Company.Name);
        Assert.Null(settings.Company.LogoPng);
        Assert.Equal(json, File.ReadAllText(file));
    }

    [Fact]
    public void CompanyEdit_IsIsolatedUntilApply_AndEmbeddedLogoSurvivesSettingsRoundTrip()
    {
        var settings = new AppSettings
        {
            Company = new CompanyProfile { Name = "Eski Firma", LogoPng = [1, 2, 3] },
        };
        var edit = new SettingsViewModel(settings) { CompanyName = " Yeni Firma ", CompanyAddress = " Örnek Sokak 1 ", CompanyWebsite = "example.com" };
        Assert.Equal("Eski Firma", settings.Company.Name);
        edit.ApplyTo(settings);
        Assert.Equal("Yeni Firma", settings.Company.Name);
        string file = Path.Combine(TestDataRoot.Root, Guid.NewGuid().ToString("N"), "ayarlar.json");
        var store = new JsonSettingsStore<AppSettings>(file);
        store.Save(settings);
        var loaded = store.Load();
        Assert.Equal("Örnek Sokak 1", loaded.Company.Address);
        Assert.Equal("example.com", loaded.Company.Website);
        Assert.Equal(new byte[] { 1, 2, 3 }, loaded.Company.LogoPng);
        edit.ClearLogoCommand.Execute(null);
        Assert.NotNull(settings.Company.LogoPng); // Vazgeç işlemi kaydedilen logoyu etkilemez.
        edit.ApplyTo(settings);
        Assert.Null(settings.Company.LogoPng);
    }

    [Fact]
    public void CompanyTemplates_UseConfiguredIdentityWithoutChangingBarcodeOrCroppingLogo()
    {
        var company = new CompanyProfile { Name = "Örnek Firma", Address = "Örnek Sokak", Website = "example.com" };
        var baseline = SampleTemplates.CreateProductLabel(null);
        var customized = SampleTemplates.CreateProductLabel(null, company);
        Assert.Contains(customized.Document.Elements.OfType<TextElement>(), e => e.Text == company.Name);
        Assert.Contains(customized.Document.Elements.OfType<TextElement>(), e => e.Text.Contains(company.Address));
        Assert.Equal(baseline.Document.Elements.OfType<BarcodeElement>().Select(e => e.Content),
            customized.Document.Elements.OfType<BarcodeElement>().Select(e => e.Content));
        using (var renderer = new AkelEtiket.Rendering.LabelRenderer(customized.Assets))
            Assert.False(renderer.RenderLabel(customized.Document, new AkelEtiket.Rendering.RenderOptions { DotsPerMm = 8 }).HasErrors);
        var withLogo = SampleTemplates.CreateProductLabel([1, 2, 3], company);
        var logo = Assert.Single(withLogo.Document.Elements.OfType<ImageElement>());
        Assert.Equal(0, logo.CropTop);
        Assert.Equal(0, logo.CropBottom);
        Assert.Equal(0, logo.CropLeft);
        Assert.Equal(0, logo.CropRight);
        Assert.Equal("firma-logo.png", Assert.Single(withLogo.Assets.Assets).FileName);
        company.Website = "https://example.org/";
        var websiteLabel = SampleTemplates.CreateProductLabel(null, company);
        Assert.Equal(company.Website, websiteLabel.Document.Elements.OfType<BarcodeElement>().Single(b => b.Symbology == BarcodeType.QrCode).Content);
        Assert.Equal(baseline.Document.Elements.OfType<BarcodeElement>().Single(b => b.Symbology == BarcodeType.Ean13).Content,
            websiteLabel.Document.Elements.OfType<BarcodeElement>().Single(b => b.Symbology == BarcodeType.Ean13).Content);
    }

    [Fact]
    public void NewInstallation_HasSeparateFoldersAndNoAutomaticLegacyImport()
    {
        Assert.Equal("Etiket Tasarım", AppIdentity.DataFolderName);
        Assert.Equal("ETIKET_VERI", AppIdentity.DataRootVariable);
        Assert.Null(AppIdentity.LegacySettingsFile);
        Assert.Null(AppIdentity.LegacyTemplatesFolder);
        var settings = new AppSettings();
        Assert.Empty(settings.Gs1CompanyPrefix);
        Assert.Empty(settings.Company.Name);
        Assert.Empty(settings.Company.Address);
        Assert.Null(settings.Company.LogoPng);
        foreach (var (_, project) in SampleTemplates.CreateAll(null))
        {
            string content = string.Join(" ", project.Document.Elements.OfType<TextElement>().Select(t => t.Text));
            Assert.DoesNotContain("AKEL", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("1234567890128", content);
            Assert.Empty(project.Assets.Assets);
        }
    }

    private sealed class EmptyClipboard : IClipboardService
    {
        public void SetElements(string payloadJson, string plainText) { }
        public string? GetElements() => null;
        public bool ContainsElements() => false;
        public byte[]? GetImagePng() => null;
        public string? GetText() => null;
    }
}
