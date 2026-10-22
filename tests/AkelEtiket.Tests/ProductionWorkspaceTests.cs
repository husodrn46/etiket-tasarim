using System.Text.Json;
using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.DataImport;
using AkelEtiket.Editor.Production;
using AkelEtiket.Editor.Production.Export;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;

namespace AkelEtiket.Tests;

[Collection("Yapay zekâ masaüstü")]
public sealed class ProductionWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(TestDataRoot.Root, "production-" + Guid.NewGuid().ToString("N"));
    public ProductionWorkspaceTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private ProductionRecipeStore Store() => new(Path.Combine(_root, "recipes.json"));
    private ProductionRecipe Recipe(string name = "Retail") => new() { Name = name, ProjectPath = Path.Combine(_root, "label.akelabel") };
    private MainViewModel Main(Dialogs? dialogs = null) => new(new AppServices
    {
        Dialogs = dialogs ?? new(), Clipboard = new Clipboard(), Dispatcher = new ImmediateDispatcher(), ImageFactory = new FakeImageFactory(),
        SettingsStore = new JsonSettingsStore<AppSettings>(Path.Combine(_root, "settings.json")),
        InitialSettings = new() { FirstRunCompleted = true, OnboardingCompleted = true, TemplatesFolder = Path.Combine(_root, "templates") },
        RecoveryFolder = Path.Combine(_root, "recovery")
    });
    private string Project(string text = "Label")
    {
        var project = LabelProject.CreateBlank(100, 60);
        project.Document.Elements.Add(new TextElement { Text = text, X = 5, Y = 5, Width = 70, Height = 20 });
        string path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ProjectFile.Extension);
        ProjectFile.Save(project, path); return path;
    }
    [Fact] public void StoreMergesUpdatesAndDeletesWithoutTouchingReferencedFiles()
    {
        var store = Store(); var a = Recipe(); var b = Recipe("Warehouse");
        File.WriteAllText(a.ProjectPath, "keep"); store.Save(a); new ProductionRecipeStore(store.FilePath).Save(b);
        store.Save(a with { Name = "Updated" }); Assert.Equal(2, store.Load().Count);
        Assert.Contains(store.Load(), r => r.Name == "Updated"); store.Delete(a.Id);
        Assert.Single(store.Load()); Assert.Equal("keep", File.ReadAllText(a.ProjectPath));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }
    [Theory] [InlineData("invalid-json")] [InlineData("{\"Version\":99,\"Recipes\":[]}")]
    [InlineData("{\"Version\":1,\"Recipes\":[null]}")]
    public void InvalidOrNewerRecipeFileIsPreserved(string content)
    {
        var store = Store(); File.WriteAllText(store.FilePath, content);
        Assert.Throws<IOException>(() => store.Load()); Assert.Throws<IOException>(() => store.Save(Recipe()));
        Assert.Equal(content, File.ReadAllText(store.FilePath)); Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }
    [Fact] public void StoreRejectsInvalidCountsDuplicateRowsAndRelativePaths()
    {
        var store = Store(); var recipe = Recipe();
        Assert.Throws<IOException>(() => store.Save(recipe with { Copies = 10001 }));
        Assert.Throws<IOException>(() => store.Save(recipe with { ProjectPath = "relative.akelabel" }));
        Assert.Throws<IOException>(() => store.Save(recipe with { Rows = [new(2, 1), new(2, 2)] }));
        Assert.Throws<IOException>(() => store.Save(recipe with { Rows = [new(2, 9999), new(3, 2)] }));
        Assert.Throws<IOException>(() => store.Save(recipe with { Format = (BatchExportFormat)99 }));
        Assert.False(File.Exists(store.FilePath));
    }
    [Fact] public void RecipeFileContainsOnlySettingsAndReferences()
    {
        var store = Store(); store.Save(Recipe() with { UseData = true, Import = new(null, 1, "utf-8", ','), DataPath = Path.Combine(_root, "stock.csv"), Rows = [new(2, 4)] });
        using var json = JsonDocument.Parse(File.ReadAllText(store.FilePath));
        var recipe = json.RootElement.GetProperty("Recipes")[0];
        Assert.Equal(4, recipe.GetProperty("Rows")[0].GetProperty("Copies").GetInt32());
        Assert.False(recipe.TryGetProperty("Values", out _)); Assert.False(recipe.TryGetProperty("Credentials", out _));
    }
    [Fact] public void FrozenImagesKeepExactBytesAndCacheErrors()
    {
        var source = new MutableImages(); var frozen = new SnapshotImageResolver(source);
        var a = frozen.Resolve("image", out _); source.Bytes[0] = 9;
        Assert.Equal(1, frozen.Resolve("image", out _)!.Data[0]); Assert.NotSame(a!.Data, source.Bytes);
        frozen.Resolve("missing", out var first); source.Missing = false; frozen.Resolve("missing", out var second);
        Assert.Equal(first, second); Assert.Equal(2, source.Calls);
    }
    [Fact] public void FrozenImageLimitsFailClosedWithoutReadingUnboundedEntries()
    {
        var source = new MutableImages(); var frozen = new SnapshotImageResolver(source, maxBytes: 2, maxEntries: 1);
        Assert.Null(frozen.Resolve("image", out var error)); Assert.NotNull(error);
        Assert.Null(frozen.Resolve("other", out error)); Assert.NotNull(error); Assert.Equal(1, source.Calls);
    }
    [Fact] public void CapturePreservesLeadingZerosAndDoesNotFollowDocumentOrRecordChanges()
    {
        using var main = Main(); main.OpenProject(Project("{SKU}"), true);
        var values = new List<string> { "001234" };
        main.Data.ApplyTable(new("stock.csv", DataSourceTable.KindCsv, null, 1, ["SKU"], [new DataRecord(2, values, ["SKU"])], []));
        main.Data.Rows[0].Copies = 3;
        var snapshot = ProductionSnapshot.Capture(main, true, 1, 1);
        ((TextElement)main.Session.Document.Elements[0]).Text = "changed"; values[0] = "999999";
        Assert.Equal("{SKU}", ((TextElement)snapshot.Project.Document.Elements[0]).Text);
        Assert.Equal("001234", FieldTemplate.Resolve("{SKU}", snapshot.Items[0].Fields).Text);
        Assert.Equal(3, snapshot.Items[0].Copies); Assert.Null(snapshot.Project.FilePath);
    }
    [Fact] public void SnapshotRetainsBuiltInDateSemanticsUnderEnglishRegionalInput()
    {
        using var main = Main(); main.OpenProject(Project("{TARIH+30:DATE} {SIRA:0000}"), true);
        main.Session.Document.Formatting = new() { CultureName = "en-US", InputCultureName = "en-US", DateFormat = "yyyy-MM-dd" };
        var snapshot = ProductionSnapshot.Capture(main, false, 1, 7);
        var regional = new RegionalFieldSource(snapshot.Items[0].Fields, snapshot.Project.Document.Formatting!);
        var result = FieldTemplate.Resolve("{TARIH+30:DATE} {SIRA:0000}", regional);
        Assert.Contains(DateTime.Today.AddDays(30).ToString("yyyy-MM-dd"), result.Text); Assert.EndsWith("0007", result.Text);
    }
    [Theory] [InlineData("{SSCC}")] [InlineData("{SAYAC}")]
    public void ReservedSerialJobsAreRejectedBeforeFieldEvaluation(string text)
    {
        using var main = Main(); main.OpenProject(Project(text), true);
        Assert.Throws<InvalidOperationException>(() => ProductionSnapshot.Capture(main, false, 1, 1));
    }
    [Theory] [InlineData(0, 1)] [InlineData(10001, 1)] [InlineData(1, 0)] [InlineData(1, int.MaxValue)]
    public void InvalidJobBoundsAreRejectedBeforePlanning(int copies, int start)
    {
        using var main = Main(); Assert.Throws<InvalidOperationException>(() => ProductionSnapshot.Capture(main, false, copies, start));
    }
    [Fact] public async Task FullQualityReportEnablesExportAndDocumentChangeInvalidatesIt()
    {
        using var main = Main(); main.OpenProject(Project(), true);
        using var vm = new ProductionViewModel(main, Store());
        Assert.False(vm.CanExport); await vm.CheckQualityCommand.ExecuteAsync(null);
        Assert.True(vm.CanExport); Assert.False(vm.IsBusy);
        ((TextElement)main.Session.Document.Elements[0]).Text = "Edited";
        Assert.False(vm.CanExport); Assert.Empty(vm.Findings);
    }
    [Fact] public async Task OptionAndRowChangesInvalidateExport()
    {
        using var main = Main(); main.OpenProject(Project(), true);
        using var vm = new ProductionViewModel(main, Store());
        await vm.CheckQualityCommand.ExecuteAsync(null); Assert.True(vm.CanExport);
        vm.Copies = 2; Assert.False(vm.CanExport);
        await vm.CheckQualityCommand.ExecuteAsync(null); Assert.True(vm.CanExport);
        main.Data.ApplyTable(new("stock.csv", DataSourceTable.KindCsv, null, 1, ["SKU"], [new DataRecord(2, ["001"], ["SKU"])], []));
        Assert.False(vm.CanExport);
        vm.UseData = true; await vm.CheckQualityCommand.ExecuteAsync(null); Assert.True(vm.CanExport);
        main.Data.Rows[0].Copies = 4; Assert.False(vm.CanExport);
    }
    [Fact] public void SaveRecipeCapturesOnlySelectedRowsAndRequiresFreshValidationOnOpen()
    {
        var dialogs = new Dialogs(); using var main = Main(dialogs); main.OpenProject(Project("{SKU}"), true);
        string csv = Path.Combine(_root, "stock.csv"); File.WriteAllText(csv, "SKU\n001\n002\n");
        main.Data.ApplyTable(new(csv, DataSourceTable.KindCsv, null, 1, ["SKU"], [new DataRecord(2, ["001"], ["SKU"]), new DataRecord(3, ["002"], ["SKU"])], []));
        main.Data.Rows[0].Copies = 4; main.Data.Rows[1].IsSelected = false;
        using var vm = new ProductionViewModel(main, Store()); vm.SaveRecipeCommand.Execute(null);
        var recipe = Assert.Single(vm.Recipes); Assert.Equal(new ProductionRecipeRow(2, 4), Assert.Single(recipe.Rows));
        Assert.False(vm.CanExport);
    }
    [Fact] public async Task RecipeUsesCapturedWorksheetEvenAfterProfileIsRemoved()
    {
        string bookPath = Path.Combine(_root, "stock.xlsx");
        using (var book = new ClosedXML.Excel.XLWorkbook())
        {
            var wrong = book.AddWorksheet("First"); wrong.Cell(1, 1).Value = "SKU"; wrong.Cell(2, 1).Value = "WRONG";
            var correct = book.AddWorksheet("Warehouse"); correct.Cell(2, 1).Value = "SKU"; correct.Cell(3, 1).Value = "000123";
            book.SaveAs(bookPath);
        }
        var recipe = Recipe() with { ProjectPath = Project("{SKU}"), UseData = true, DataPath = bookPath,
            ImportProfileId = "removed-profile", Import = new("Warehouse", 2, null, null),
            DataLength = new FileInfo(bookPath).Length, DataWriteUtc = File.GetLastWriteTimeUtc(bookPath), Rows = [new(3, 4)] };
        var store = Store(); store.Save(recipe);
        using var main = Main(); using var vm = new ProductionViewModel(main, store); vm.SelectedRecipe = vm.Recipes.Single();
        await vm.OpenRecipeCommand.ExecuteAsync(null);
        Assert.Equal("Warehouse", main.Data.Table?.SheetName); Assert.Equal(2, main.Data.Table?.HeaderRowNumber);
        Assert.Equal("000123", main.Data.Rows.Single().Record.GetValue("SKU")); Assert.Equal(4, main.Data.Rows.Single().Copies);
        Assert.False(vm.CanExport);
    }
    [Fact] public async Task ChangedFileRequiresConfirmationBeforeApplyingRecipe()
    {
        string csv = Path.Combine(_root, "changed.csv"); File.WriteAllText(csv, "SKU\n000123\n");
        var recipe = Recipe() with { ProjectPath = Project("{SKU}"), UseData = true, DataPath = csv,
            Import = new(null, 1, "utf-8", ','), DataLength = 1, DataWriteUtc = DateTime.MinValue, Rows = [new(2, 1)] };
        var store = Store(); store.Save(recipe); var dialogs = new Dialogs { ConfirmResult = false };
        using var main = Main(dialogs); var original = main.ActiveDocument;
        using var vm = new ProductionViewModel(main, store); vm.SelectedRecipe = vm.Recipes.Single();
        await vm.OpenRecipeCommand.ExecuteAsync(null);
        Assert.Same(original, main.ActiveDocument); Assert.False(main.Data.HasData); Assert.False(vm.CanExport);
    }
    [Fact] public async Task OverwriteDialogSourceChangeCannotExportStaleSnapshot()
    {
        var dialogs = new Dialogs(); using var main = Main(dialogs); main.OpenProject(Project(), true);
        using var vm = new ProductionViewModel(main, Store()); await vm.CheckQualityCommand.ExecuteAsync(null); Assert.True(vm.CanExport);
        string output = Path.Combine(_root, "output.pdf"); File.WriteAllText(output, "previous"); dialogs.SavePath = output;
        dialogs.OnConfirm = () => ((TextElement)main.Session.Document.Elements[0]).Text = "Changed while confirming";
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.Equal("previous", File.ReadAllText(output)); Assert.False(vm.CanExport);
    }
    [Fact] public async Task InvalidInputAndDisposalDisablePreviouslyValidatedOutput()
    {
        using var main = Main(); main.OpenProject(Project(), true);
        using var vm = new ProductionViewModel(main, Store()); await vm.CheckQualityCommand.ExecuteAsync(null); Assert.True(vm.CanExport);
        vm.HasInputErrors = true; Assert.False(vm.CanExport); Assert.False(vm.CheckQualityCommand.CanExecute(null));
        vm.HasInputErrors = false; await vm.CheckQualityCommand.ExecuteAsync(null); Assert.True(vm.CanExport);
        vm.Dispose(); Assert.False(vm.CanExport);
    }
    [Fact] public void SnapshotFieldWorkBudgetRejectsExcessBeforeUnboundedAllocation()
    {
        using var main = Main(); main.OpenProject(Project("{SIRA} {A} {B} {C} {D} {E} {F} {G} {H} {I} {J}"), true);
        Assert.Throws<InvalidOperationException>(() => ProductionSnapshot.Capture(main, false, 10000, 1));
    }
    [Fact] public void OversizedFormattedRecipeCommitPreservesExistingReadableFile()
    {
        var store = Store();
        var rows = Enumerable.Range(2, 1000).Select(i => new ProductionRecipeRow(i, 1)).ToList();
        var recipes = Enumerable.Range(1, 160).Select(i => Recipe("Job " + i) with
        { UseData = true, DataPath = Path.Combine(_root, "stock.csv"), Import = new(null, 1, "utf-8", ','), Rows = rows }).ToList();
        string previous = JsonSerializer.Serialize(new { Version = 1, Recipes = recipes });
        File.WriteAllText(store.FilePath, previous);
        Assert.True(new FileInfo(store.FilePath).Length < 8 * 1024 * 1024);
        Assert.Throws<IOException>(() => store.Save(recipes[0] with { Name = "Updated" }));
        Assert.Equal(previous, File.ReadAllText(store.FilePath)); Assert.Equal(160, store.Load().Count);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }
    [Fact] public async Task CancelledWholeJobNeverEnablesExport()
    {
        using var main = Main(); main.OpenProject(Project("{SIRA}"), true);
        using var vm = new ProductionViewModel(main, Store()) { Copies = 500 };
        var running = vm.CheckQualityCommand.ExecuteAsync(null);
        vm.CancelCommand.Execute(null); await running;
        Assert.False(vm.CanExport); Assert.False(vm.IsBusy);
    }
    [Fact] public void RecipeSelectionRestoresCopiesWithOneSummaryNotification()
    {
        var data = new DataSourceViewModel();
        data.ApplyTable(new("stock.csv", DataSourceTable.KindCsv, null, 1, ["SKU"],
            Enumerable.Range(2, 100).Select(i => new DataRecord(i, ["00" + i], ["SKU"])).ToArray(), []));
        int summaries = 0;
        data.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DataSourceViewModel.SelectedLabelCount)) summaries++; };
        data.ApplyRowSelection(new Dictionary<int, int> { [4] = 7, [99] = 2 });
        Assert.Equal(1, summaries); Assert.Equal(9, data.SelectedLabelCount); Assert.Equal(2, data.Rows.Count(r => r.IsSelected));
    }
    private sealed class MutableImages : IImageSourceResolver
    {
        public byte[] Bytes = [1, 2, 3]; public int Calls; public bool Missing = true;
        public ImageAsset? Resolve(string value, out string? error)
        { Calls++; if (value == "missing" && Missing) { error = "missing"; return null; } error = null; return new("image.png", "image.png", Bytes); }
    }
    private sealed class Dialogs : IDialogService
    {
        public string? SavePath;
        public bool ConfirmResult = true;
        public Action? OnConfirm;
        public string? ShowOpenFileDialog(string title, string filter, string? initialDirectory = null) => null;
        public string? ShowSaveFileDialog(string title, string filter, string defaultFileName, string? initialDirectory = null) => SavePath;
        public void ShowMessage(string title, string message, MessageKind kind = MessageKind.Info) { }
        public bool Confirm(string title, string message, string confirmText = "Evet", string cancelText = "Hayır") { OnConfirm?.Invoke(); return ConfirmResult; }
        public SaveChangesChoice AskSaveChanges(string documentName) => SaveChangesChoice.Cancel;
        public string? Prompt(string title, string message, string defaultValue = "") => "Local job";
        public bool ShowDialog(object viewModel) => false;
    }
    private sealed class Clipboard : IClipboardService
    {
        public void SetElements(string payloadJson, string plainText) { }
        public string? GetElements() => null; public bool ContainsElements() => false;
        public byte[]? GetImagePng() => null; public string? GetText() => null;
    }
}
