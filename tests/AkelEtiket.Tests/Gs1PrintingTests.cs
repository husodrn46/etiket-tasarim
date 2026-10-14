using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.DataImport;
using AkelEtiket.Editor.Printing;
using AkelEtiket.Editor.Templates;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.Gs1;
using AkelEtiket.Rendering;

namespace AkelEtiket.Tests;

/// <summary>
/// GS1/SSCC baskı entegrasyonu: plan her etiketi ayrı üretir, önizleme numara ayırmaz, baskıda her etikete yeni ve
/// kayda geçmiş bir SSCC verilir, gönderilmeyenler "kullanılmadı" olur. (Fiziksel yazıcıya gönderim YOK.)
/// </summary>
public sealed class Gs1PrintingTests : IDisposable
{
    private const string TestGcp = "8691234"; // uydurma test öneki (GLN'den türetilmedi)
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "AkelEtiket2-Gs1PrintTests", Guid.NewGuid().ToString("N"));

    public Gs1PrintingTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private Gs1Context VerifiedContext()
    {
        var context = new Gs1Context(_folder);
        context.Save(new Gs1CompanyProfile { Gln = "1234567890128", GcpPrefix = TestGcp, GcpVerified = true, GcpVerifiedAt = DateTimeOffset.Now });
        return context;
    }

    private static LabelDocument SsccDocument()
    {
        var doc = LabelDocument.CreateBlank();
        doc.Elements.Add(new BarcodeElement { Symbology = BarcodeType.Gs1_128, Content = "(00){SSCC}", X = 4, Y = 10, Width = 92, Height = 30 });
        return doc;
    }

    private static readonly string[] SampleColumns = ["UrunAdi", "GTIN", "KoliIciAdet", "Parti", "Adet"];

    private static RecordFieldSource SampleRecord() =>
        new(SampleColumns, ["Melamin Servis Tabağı Ø 26 cm", "08691234567890", "24", "ABC123", "1"]);

    [Fact]
    public void Planner_SingleMode_ExpandsCopies_WhenSsccUsed()
    {
        var data = new DataSourceViewModel();
        var plan = PrintPlanner.Plan(SsccDocument(), data, useData: false, copies: 3, startNumber: 1, DateTime.Now);
        Assert.True(plan.UsesSscc);
        Assert.Equal(3, plan.Items.Count);
        Assert.All(plan.Items, i => Assert.Equal(1, i.Copies));
        Assert.Equal(3, plan.TotalLabels);

        var plain = LabelDocument.CreateBlank();
        plain.Elements.Add(new TextElement { Text = "Sabit" });
        var plainPlan = PrintPlanner.Plan(plain, data, useData: false, copies: 3, startNumber: 1, DateTime.Now);
        Assert.False(plainPlan.UsesSscc);
        Assert.Equal(3, Assert.Single(plainPlan.Items).Copies);
    }

    [Fact]
    public void Planner_DataMode_ExpandsRowCopies_AndPreviewValidatesSscc()
    {
        var doc = SsccDocument();
        doc.Elements.Add(new TextElement { Text = "{UrunAdi}" });
        IReadOnlyList<string> columns = SampleColumns;
        var rows = new List<DataRecord>
        {
            new(2, ["Tabak", "08691234567890", "24", "A1", "2"], columns),
            new(3, ["Kase", "08691234567890", "12", "A2", "1"], columns),
        };
        var table = new DataSourceTable("bellek.csv", DataSourceTable.KindCsv, null, 1, columns, rows, []);
        var data = new DataSourceViewModel();
        data.ApplyTable(table);
        Assert.Equal("Adet", data.CopiesColumn);
        data.ValidateAgainst(doc);
        Assert.All(data.Rows, r => Assert.NotEqual(RowStatus.Error, r.Status));

        var plan = PrintPlanner.Plan(doc, data, useData: true, copies: 1, startNumber: 1, DateTime.Now);
        Assert.True(plan.UsesSscc);
        Assert.Equal(3, plan.Items.Count);
        Assert.Equal([2, 2, 3], plan.Items.Select(i => i.RowNumber!.Value));
        Assert.All(plan.Items, i => Assert.Equal(1, i.Copies));
        // Plan önizleme alanlarıyla kurulur: SSCC çözülür ama numara ayrılmaz.
        Assert.True(plan.Items[0].Fields.TryGetValue("SSCC", null, out var preview));
        Assert.Equal(Gs1FieldSource.PreviewSscc, preview);
    }

    [Fact]
    public void SsccSampleTemplate_RendersCleanly_InPreview_WithoutAllocating()
    {
        var context = new Gs1Context(_folder);
        var project = SampleTemplates.CreateSsccCartonLabel(logoPng: null);
        var fields = new CompositeFieldSource(SampleRecord(), new BuiltInFieldSource(1, DateTime.Now), context.PreviewFields);
        using var renderer = new LabelRenderer(project.Assets);
        var result = renderer.RenderLabel(project.Document, new RenderOptions { DotsPerMm = 8, Mode = RenderMode.Print, Fields = fields });
        Assert.False(result.HasErrors, string.Join("; ", result.Errors));
        Assert.Empty(Directory.GetFiles(_folder, "sscc*"));
        Assert.Contains(SampleTemplates.CreateAll(null), t => t.Name == SampleTemplates.SsccCartonLabelName);
    }

    [Fact]
    public void Tracker_AllocatesUniqueSsccPerLabel_ThatEndsUpInTheBarcode()
    {
        var context = VerifiedContext();
        var project = SampleTemplates.CreateSsccCartonLabel(logoPng: null);
        var tracker = SsccPrintTracker.Begin(context, "Baskı: test");
        Assert.True(tracker.IsActive);
        var baseFields = new CompositeFieldSource(SampleRecord(), new BuiltInFieldSource(1, DateTime.Now), context.PreviewFields);
        var numbers = new List<string>();
        using (var producer = new LabelProducer(project.Document, project.Assets, verifyBarcodes: true))
        {
            for (int i = 0; i < 3; i++)
            {
                var (item, sscc) = tracker.Prepare(new PrintItem(i + 1, 2, 1, baseFields));
                Assert.NotNull(sscc);
                Assert.True(item.Fields.TryGetValue("SSCC", null, out var resolved));
                Assert.Equal(sscc, resolved);
                // Okunabilirlik kontrolü açık: barkod çözülüp beklenen (ayrılan) SSCC ile karşılaştırılır.
                var (bitmap, problems, _) = producer.Produce(item, 8.0);
                Assert.True(bitmap is not null, string.Join("; ", problems));
                numbers.Add(sscc!);
            }
        }
        Assert.Equal(3, numbers.Distinct().Count());
        Assert.All(numbers, n => Assert.Equal(SsccStatus.Allocated, context.Registry.GetEntry(n)!.Status));

        var result = tracker.Complete(spooledLabels: 2, canceled: false);
        Assert.NotNull(result);
        Assert.False(result!.Value.Failed);
        Assert.Contains("2 numara yazıcı kuyruğuna gönderildi", result.Value.Text);
        Assert.Contains("1 numara kullanılmadı", result.Value.Text);
        Assert.Equal(SsccStatus.Sent, context.Registry.GetEntry(numbers[0])!.Status);
        Assert.Equal(SsccStatus.Sent, context.Registry.GetEntry(numbers[1])!.Status);
        Assert.Equal(SsccStatus.Voided, context.Registry.GetEntry(numbers[2])!.Status);
        Assert.Null(tracker.Complete(3, false));
    }

    [Fact]
    public void Tracker_ProductionFailure_VoidsImmediately()
    {
        var context = VerifiedContext();
        var tracker = SsccPrintTracker.Begin(context, "Baskı");
        var (_, first) = tracker.Prepare(new PrintItem(1, null, 1, new BuiltInFieldSource(1, DateTime.Now)));
        var (_, second) = tracker.Prepare(new PrintItem(2, null, 1, new BuiltInFieldSource(2, DateTime.Now)));
        tracker.ProductionFailed(first, "barkod sığmadı");
        Assert.Equal(SsccStatus.Voided, context.Registry.GetEntry(first!)!.Status);
        Assert.Contains("barkod sığmadı", context.Registry.GetEntry(first!)!.Note);
        tracker.Complete(spooledLabels: 1, canceled: false);
        Assert.Equal(SsccStatus.Sent, context.Registry.GetEntry(second!)!.Status);
    }

    [Fact]
    public void Tracker_CanceledJob_VoidsEverything()
    {
        var context = VerifiedContext();
        var tracker = SsccPrintTracker.Begin(context, "Baskı");
        var (_, sscc) = tracker.Prepare(new PrintItem(1, null, 1, new BuiltInFieldSource(1, DateTime.Now)));
        var result = tracker.Complete(spooledLabels: 0, canceled: true);
        Assert.Contains("0 numara", result!.Value.Text);
        var entry = context.Registry.GetEntry(sscc!)!;
        Assert.Equal(SsccStatus.Voided, entry.Status);
        Assert.Contains("iptal", entry.Note);
    }

    [Fact]
    public void Tracker_RequiresVerifiedPrefix_AndInactiveTrackerAllocatesNothing()
    {
        var unverified = new Gs1Context(_folder);
        Assert.Throws<Gs1Exception>(() => SsccPrintTracker.Begin(unverified, "Baskı"));

        var inactive = SsccPrintTracker.Inactive(unverified);
        var item = new PrintItem(1, null, 5, new BuiltInFieldSource(1, DateTime.Now));
        var (same, sscc) = inactive.Prepare(item);
        Assert.Same(item, same);
        Assert.Null(sscc);
        Assert.Null(inactive.Complete(1, false));
        Assert.Empty(Directory.GetFiles(_folder, "sscc*"));
    }

    [Fact]
    public void ProfileViewModel_ResetsVerification_WhenPrefixChanges_AndBlocksInvalidSave()
    {
        var context = VerifiedContext();
        var dialogs = new RecordingDialogs();
        var vm = new Gs1ProfileViewModel(context, dialogs);
        Assert.True(vm.GcpVerified);
        Assert.Equal("1234567890128", vm.Gln);

        vm.GcpPrefix = "8699999";
        Assert.False(vm.GcpVerified);
        Assert.NotNull(vm.VerificationResetNote);

        vm.Gln = "1234567890120";
        Assert.False(vm.GlnIsValid);
        Assert.False(vm.Save());
        Assert.Contains("Kaydedilmedi", dialogs.LastMessage);
        // Diskteki profil değişmedi.
        Assert.Equal(TestGcp, new Gs1Context(_folder).Profile.GcpPrefix);
    }

    [Fact]
    public void ProfileViewModel_SavesValidProfile_AndNeverInventsPrefix()
    {
        var context = new Gs1Context(_folder);
        var dialogs = new RecordingDialogs();
        var vm = new Gs1ProfileViewModel(context, dialogs);
        Assert.Equal("", vm.GcpPrefix);
        vm.CompanyName = "Örnek Firma";
        Assert.True(vm.Save());
        var saved = new Gs1Context(_folder).Profile;
        Assert.Equal("", saved.GcpPrefix);
        Assert.False(saved.GcpVerified);
        Assert.Empty(saved.Gln);
    }

    private sealed class RecordingDialogs : Editor.Services.IDialogService
    {
        public string LastMessage { get; private set; } = "";
        public string? ShowOpenFileDialog(string title, string filter, string? initialDirectory = null) => null;
        public string? ShowSaveFileDialog(string title, string filter, string defaultFileName, string? initialDirectory = null) => null;
        public void ShowMessage(string title, string message, Editor.Services.MessageKind kind = Editor.Services.MessageKind.Info) => LastMessage = message;
        public bool Confirm(string title, string message, string confirmText = "Evet", string cancelText = "Hayır") => true;
        public Editor.Services.SaveChangesChoice AskSaveChanges(string documentName) => Editor.Services.SaveChangesChoice.Cancel;
        public string? Prompt(string title, string message, string defaultValue = "") => null;
        public bool ShowDialog(object viewModel) => false;
    }
}
