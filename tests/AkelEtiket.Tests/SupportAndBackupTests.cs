using System.IO.Compression;
using AkelEtiket.Core.Automation;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Tests;

/// <summary>Kayıt öncesi yedek, yedekten açma ve destek paketi (gizli bilgiler çıkarılmış).</summary>
[Collection("Yapay zekâ masaüstü")]
public sealed class SupportAndBackupTests : IDisposable
{
    private readonly string _folder = Path.Combine(TestDataRoot.Root, "destek-" + Guid.NewGuid().ToString("N")[..6]);
    private readonly SavingDialogs _dialogs;
    private readonly MainViewModel _main;

    public SupportAndBackupTests()
    {
        Directory.CreateDirectory(_folder);
        _dialogs = new SavingDialogs(Path.Combine(_folder, "Etiket.akelabel"));
        _main = new MainViewModel(new AppServices
        {
            Dialogs = _dialogs,
            Clipboard = new NullClipboardService(),
            Dispatcher = new ImmediateDispatcher(),
            ImageFactory = new FakeImageFactory(),
            SettingsStore = new JsonSettingsStore<AppSettings>(Path.Combine(_folder, "ayarlar.json")),
            RecoveryFolder = Path.Combine(_folder, "Kurtarma"),
        });
    }

    public void Dispose() => _main.Dispose();

    [Fact]
    public void SavingOverExistingFile_KeepsBackup_AndBackupOpensAsNewDocument()
    {
        _main.Session.AddElement(new TextElement { Text = "Birinci sürüm", X = 5, Y = 5 });
        Assert.True(_main.SaveAs());
        string path = _dialogs.SavePath;
        Assert.Empty(_main.Backups.ListFor(path));

        ((TextElement)_main.Session.Document.Elements[0]).Text = "İkinci sürüm";
        Assert.True(_main.Save());
        var backups = _main.Backups.ListFor(path);
        Assert.Single(backups);

        var vm = new BackupsViewModel(_main, path);
        vm.Selected = vm.Items.Single();
        vm.OpenCommand.Execute(null);
        Assert.Null(_main.Session.Project.FilePath);
        Assert.Equal("Birinci sürüm", ((TextElement)_main.Session.Document.Elements[0]).Text);
        // Asıl dosya son kaydı korur.
        Assert.Equal("İkinci sürüm", ((TextElement)ProjectFile.Load(path).Project.Document.Elements[0]).Text);
    }

    [Fact]
    public void SupportBundle_ContainsInfoAndHidesSecrets()
    {
        var settings = AiSettingsStore.LoadWithToken();
        settings.Assistant.ProtectedApiKey = "GIZLI-SIFRELI-ANAHTAR";
        AiSettingsStore.Save(settings);
        string zipPath = SupportBundle.Create(null, ["barkod", "Microsoft Print to PDF"], Path.Combine(_folder, "Destek"));
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, e => e.FullName == "bilgi.txt");
        var ai = zip.Entries.Single(e => e.FullName == "ayarlar/yapay-zeka.json");
        using var reader = new StreamReader(ai.Open());
        string json = reader.ReadToEnd();
        Assert.DoesNotContain(settings.Token, json);
        Assert.DoesNotContain("GIZLI-SIFRELI-ANAHTAR", json);
        Assert.Contains("(gizlendi)", json);
    }

    private sealed class SavingDialogs(string savePath) : IDialogService
    {
        public string SavePath { get; } = savePath;
        public string? ShowOpenFileDialog(string title, string filter, string? initialDirectory = null) => null;
        public string? ShowSaveFileDialog(string title, string filter, string defaultFileName, string? initialDirectory = null) => SavePath;
        public void ShowMessage(string title, string message, MessageKind kind = MessageKind.Info) { }
        public bool Confirm(string title, string message, string confirmText = "Evet", string cancelText = "Hayır") => true;
        public SaveChangesChoice AskSaveChanges(string documentName) => SaveChangesChoice.Discard;
        public string? Prompt(string title, string message, string defaultValue = "") => null;
        public bool ShowDialog(object viewModel) => false;
    }

    private sealed class NullClipboardService : IClipboardService
    {
        public void SetElements(string payloadJson, string plainText) { }
        public string? GetElements() => null;
        public bool ContainsElements() => false;
        public byte[]? GetImagePng() => null;
        public string? GetText() => null;
    }
}
