using System.IO.Compression;
using AkelEtiket.Core.Model;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Core.Tests.Projects;

public sealed class ProjectFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "akel-proje-test-" + Guid.NewGuid().ToString("N"));

    public ProjectFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static LabelProject CreateSampleProject()
    {
        var project = LabelProject.CreateBlank();
        var doc = project.Document;
        doc.Name = "Ürün Etiketi";
        doc.Elements.Add(new TextElement { X = 4.125, Y = 6.5, Width = 70.25, Height = 9.75, Text = "Çay Tabağı {UrunAdi}", FontSize = 16, Bold = true });
        doc.Elements.Add(new BarcodeElement { X = 10, Y = 40, Width = 60, Height = 25, Symbology = BarcodeType.Ean13, Content = "8691234567890" });
        var asset = project.Assets.Add([0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4], "logo şekil.png");
        doc.Elements.Add(new ImageElement { X = 70, Y = 70, Width = 25, Height = 25, AssetId = asset.Id, Method = ImageConversionMethod.Ordered });
        doc.Elements.Add(new ShapeElement { X = 1, Y = 1, Width = 98, Height = 98, Kind = ShapeKind.Rectangle, CornerRadius = 2 });
        return project;
    }

    [Fact]
    public void SaveAndLoad_PreservesAllCoordinatesElementsAndAssets()
    {
        var project = CreateSampleProject();
        string path = Path.Combine(_dir, "test.akelabel");
        ProjectFile.Save(project, path, previewPng: [1, 2, 3]);
        Assert.Equal(path, project.FilePath);

        var loaded = ProjectFile.Load(path);
        Assert.Empty(loaded.Warnings);
        var doc = loaded.Project.Document;
        Assert.Equal("Ürün Etiketi", doc.Name);
        Assert.Equal(100, doc.Label.WidthMm);
        Assert.Equal(4, doc.Elements.Count);
        for (int i = 0; i < 4; i++)
        {
            var a = project.Document.Elements[i];
            var b = doc.Elements[i];
            Assert.Equal(a.GetType(), b.GetType());
            Assert.Equal((a.Id, a.X, a.Y, a.Width, a.Height, a.Rotation), (b.Id, b.X, b.Y, b.Width, b.Height, b.Rotation));
        }
        var image = Assert.IsType<ImageElement>(doc.Elements[2]);
        Assert.True(loaded.Project.Assets.TryGet(image.AssetId, out var asset));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 }, asset.Data);
        Assert.Equal("logo şekil.png", asset.FileName);
        Assert.Equal(new byte[] { 1, 2, 3 }, ProjectFile.ReadPreview(path));
        Assert.Equal(4, ProjectFile.ReadManifest(path).ElementCount);
    }

    [Fact]
    public void Save_OverwritesExistingFileAtomically_AndPrunesUnusedAssets()
    {
        var project = CreateSampleProject();
        string path = Path.Combine(_dir, "atomik.akelabel");
        ProjectFile.Save(project, path);
        project.Assets.Add([9, 9, 9], "kullanilmayan.png");
        project.Document.Elements.RemoveAt(0);
        ProjectFile.Save(project, path);

        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        var loaded = ProjectFile.Load(path).Project;
        Assert.Equal(3, loaded.Document.Elements.Count);
        Assert.Equal(1, loaded.Assets.Count); // kullanılmayan görsel pakete yazılmaz
    }

    [Fact]
    public void Load_PackageWithMissingAsset_LoadsWithWarning()
    {
        var project = CreateSampleProject();
        var image = project.Document.Elements.OfType<ImageElement>().First();
        image.AssetId = "yok.png";
        string path = Path.Combine(_dir, "eksik.akelabel");
        ProjectFile.Save(project, path);
        var loaded = ProjectFile.Load(path);
        Assert.Contains(loaded.Warnings, w => w.Contains("bulunamadı"));
        Assert.Single(loaded.Project.GetMissingAssetIds());
    }

    [Fact]
    public void Load_InvalidFiles_ThrowTurkishProjectFileException()
    {
        string notZip = Path.Combine(_dir, "bozuk.akelabel");
        File.WriteAllText(notZip, "bu bir zip değil");
        var ex1 = Assert.Throws<ProjectFileException>(() => ProjectFile.Load(notZip));
        Assert.Contains("bozuk", ex1.Message);

        string noManifest = Path.Combine(_dir, "manifestsiz.akelabel");
        using (var zip = ZipFile.Open(noManifest, ZipArchiveMode.Create)) zip.CreateEntry("label.json");
        Assert.Throws<ProjectFileException>(() => ProjectFile.Load(noManifest));

        Assert.Throws<ProjectFileException>(() => ProjectFile.Load(Path.Combine(_dir, "olmayan.akelabel")));
    }

    [Fact]
    public void TemplateLibrary_SaveListDuplicateRenameDelete()
    {
        var library = new TemplateLibrary(Path.Combine(_dir, "Şablonlar"));
        var project = CreateSampleProject();
        project.FilePath = @"C:\bir\yer\proje.akelabel";
        string saved = library.SaveAsTemplate(project, "Raf Etiketi: 100/100", [7], overwrite: false);
        Assert.Equal(@"C:\bir\yer\proje.akelabel", project.FilePath); // açık projenin yolu değişmez
        Assert.EndsWith("Raf Etiketi_ 100_100.akelabel", saved);
        Assert.Throws<ProjectFileException>(() => library.SaveAsTemplate(project, "Raf Etiketi: 100/100", null, overwrite: false));

        var list = library.List();
        var info = Assert.Single(list);
        Assert.Equal(4, info.ElementCount);
        Assert.Equal(new byte[] { 7 }, info.PreviewPng);
        Assert.True(info.HasFieldPlaceholders);

        string copy = library.Duplicate(info);
        Assert.Contains("(kopya)", copy);
        Assert.Equal(2, library.List().Count);

        var copyInfo = library.List().First(t => t.FilePath == copy);
        string renamed = library.Rename(copyInfo, "Yeni Ad");
        Assert.True(File.Exists(renamed));
        Assert.False(File.Exists(copy));
        Assert.Equal("Yeni Ad", ProjectFile.Load(renamed).Project.Document.Name);

        library.Delete(library.List().First(t => t.Name == "Yeni Ad"));
        Assert.Single(library.List());
    }

    [Fact]
    public void AutoRecovery_FindsOnlyOrphanedSessions()
    {
        string folder = Path.Combine(_dir, "Kurtarma");
        using var live = new AutoRecoveryService(folder);
        live.Start();
        live.Save(CreateSampleProject());

        // Çökmüş bir oturumu taklit et: kilitsiz kurtarma dosyası.
        Directory.CreateDirectory(folder);
        var crashed = CreateSampleProject();
        crashed.Document.Name = "Çöken Oturum";
        using (var stream = File.Create(Path.Combine(folder, "oturum-crashed1.akelabel")))
            ProjectFile.Write(crashed, stream);

        using var next = new AutoRecoveryService(folder);
        var orphans = next.FindOrphanedSessions();
        var entry = Assert.Single(orphans); // canlı oturum (kilitli) listelenmez
        var recovered = next.Recover(entry);
        Assert.Equal("Çöken Oturum", recovered.Project.Document.Name);
        next.Discard(entry);
        Assert.Empty(next.FindOrphanedSessions());
    }

    [Fact]
    public void SettingsStore_CorruptFile_FallsBackToDefaults()
    {
        string file = Path.Combine(_dir, "ayarlar.json");
        File.WriteAllText(file, "{ bozuk json");
        var store = new JsonSettingsStore<RecentFilesList>(file);
        var settings = store.Load();
        Assert.NotNull(settings);
        Assert.NotNull(store.LastLoadProblem);
        settings.Add(Path.Combine(_dir, "a.akelabel"));
        store.Save(settings);
        Assert.Single(store.Load().Items);
    }

    [Fact]
    public void RecentFiles_KeepsNewestFirstWithoutDuplicates()
    {
        var recent = new RecentFilesList();
        for (int i = 0; i < 15; i++) recent.Add(Path.Combine(_dir, $"{i}.akelabel"));
        recent.Add(Path.Combine(_dir, "3.akelabel"));
        Assert.Equal(RecentFilesList.MaxCount, recent.Items.Count);
        Assert.Equal("3.akelabel", Path.GetFileName(recent.Items[0]));
        Assert.Single(recent.Items, p => Path.GetFileName(p) == "3.akelabel");
    }
}
