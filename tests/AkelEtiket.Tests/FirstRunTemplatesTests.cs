using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.Templates;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Tests;

/// <summary>İlk açılış: v1 şablonları kopyalanır, v1'de olmayan v2 örnekleri eklenir, v1'den gelen aynı adlı şablon korunur.</summary>
[Collection("Yapay zekâ masaüstü")]
public sealed class FirstRunTemplatesTests : IDisposable
{
    private readonly string _folder = Path.Combine(TestDataRoot.Root, "ilk-" + Guid.NewGuid().ToString("N")[..6]);
    private readonly MainViewModel _main;

    public FirstRunTemplatesTests()
    {
        Directory.CreateDirectory(_folder);
        _main = new MainViewModel(new AppServices
        {
            Dialogs = new ScriptedDialogs(),
            Clipboard = new NullClipboard(),
            Dispatcher = new ImmediateDispatcher(),
            ImageFactory = new FakeImageFactory(),
            SettingsStore = new JsonSettingsStore<AppSettings>(Path.Combine(_folder, "ayarlar.json")),
            RecoveryFolder = Path.Combine(_folder, "Kurtarma"),
        });
        _main.Templates.ChangeFolder(Path.Combine(_folder, "Şablonlar"));
    }

    public void Dispose() => _main.Dispose();

    private static void WriteTemplate(string folder, string name, string marker)
    {
        var doc = LabelDocument.CreateBlank(100, 100, name);
        doc.Elements.Add(new TextElement { Text = marker, X = 5, Y = 5, Width = 90, Height = 10 });
        ProjectFile.Save(new LabelProject(doc), Path.Combine(folder, name + ProjectFile.Extension));
    }

    [Fact]
    public void FirstRun_CopiesV1Templates_AndAddsNewSamples_KeepingV1Versions()
    {
        string legacy = Path.Combine(_folder, "v1-Şablonlar");
        Directory.CreateDirectory(legacy);
        WriteTemplate(legacy, SampleTemplates.ProductLabelName, "v1'de değiştirilmiş");
        WriteTemplate(legacy, "Benim Şablonum", "kullanıcının şablonu");
        var legacyFiles = Directory.GetFiles(legacy).ToDictionary(f => f, File.ReadAllBytes);

        _main.Templates.EnsureSamples(null, legacy);

        Assert.Equal(2, _main.Templates.ImportedLegacyCount);
        var names = _main.Templates.Library.List().Select(t => t.Name).ToList();
        Assert.Contains("Benim Şablonum", names);
        Assert.Contains(SampleTemplates.SsccCartonLabelName, names);
        Assert.Contains(SampleTemplates.Gs1DataMatrixLabelName, names);
        Assert.Equal(SampleTemplates.CreateAll(null).Count + 1, names.Count);

        // v1'den gelen aynı adlı şablon örnekle değiştirilmez.
        var product = ProjectFile.Load(_main.Templates.Library.GetPathForName(SampleTemplates.ProductLabelName)).Project;
        Assert.Contains(product.Document.Elements.OfType<TextElement>(), t => t.Text == "v1'de değiştirilmiş");

        // v1 klasörü olduğu gibi kalır.
        Assert.Equal(legacyFiles.Keys.Order(), Directory.GetFiles(legacy).Order());
        foreach (var (file, bytes) in legacyFiles) Assert.Equal(bytes, File.ReadAllBytes(file));
    }

    [Fact]
    public void LaterRuns_DoNotRecreateDeletedSamples()
    {
        _main.Templates.EnsureSamples(null, null);
        int count = _main.Templates.Library.List().Count;
        Assert.Equal(SampleTemplates.CreateAll(null).Count, count);
        Assert.Equal(0, _main.Templates.ImportedLegacyCount);

        File.Delete(_main.Templates.Library.GetPathForName(SampleTemplates.ShelfLabelName));
        _main.Templates.EnsureSamples(null, null);

        Assert.Equal(count - 1, _main.Templates.Library.List().Count);
    }

    private sealed class NullClipboard : IClipboardService
    {
        public void SetElements(string payloadJson, string plainText) { }
        public string? GetElements() => null;
        public bool ContainsElements() => false;
        public byte[]? GetImagePng() => null;
        public string? GetText() => null;
    }
}
