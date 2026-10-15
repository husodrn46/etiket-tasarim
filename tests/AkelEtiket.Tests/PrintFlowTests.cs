using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Printing;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.Templates;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.PrintEngine;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;

namespace AkelEtiket.Tests;

/// <summary>Baskı servislerini (geçmiş, profil, sayaç deposu, TSPL kuyruğu) değiştiren testler sırayla çalışır.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PrintServicesCollection
{
    public const string Name = "Baskı servisleri";
}

/// <summary>
/// Baskı penceresinin uçtan uca akışı SAHTE Windows kuyruğuyla: sayaçlar, baskı geçmişi, yarım kalan işin kaldığı yerden
/// sürdürülmesi, geçmişten tekrar basma, yazıcı profilleri, kalibrasyon yardımcısı ve PDF. Hiçbir yazıcıya gönderim
/// yapılmaz: TSPL servisi sahte kuyrukla kurulur ve seçili yazıcı da var olmayan "Sahte XP-470B"dir.
/// </summary>
[Collection(PrintServicesCollection.Name)]
public sealed partial class PrintFlowTests : IDisposable
{
    private readonly string _folder = Path.Combine(TestDataRoot.Root, "baski-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly ScriptedDialogs _dialogs = new();
    private readonly FakeSpooler _spooler = new();
    private readonly MainViewModel _main;
    private readonly PrintHistoryStore _previousHistory = PrintingServices.History;
    private readonly PrinterProfileStore _previousProfiles = PrintingServices.Profiles;
    private readonly CounterStore _previousCounters = CounterIntegration.Store;
    private readonly Func<string, bool, TsplPrintService> _previousFactory = PrintingServices.CreateTsplService;
    private readonly PrintingServices.PrinterListReader _previousDiscovery = PrintingServices.ReadPrinters;

    public PrintFlowTests()
    {
        Directory.CreateDirectory(_folder);
        PrintingServices.ReadPrinters = (out IReadOnlyList<PrinterInfo> printers, out string? error) =>
        {
            printers = [FakePrinter()]; error = null; return true;
        };
        PrintingServices.History = new PrintHistoryStore(Path.Combine(_folder, "gecmis"));
        PrintingServices.Profiles = new PrinterProfileStore(Path.Combine(_folder, "yazici-profilleri.json"));
        CounterIntegration.Store = new CounterStore(Path.Combine(_folder, "Sayaclar"));
        PrintingServices.CreateTsplService = (name, singleJob) => new TsplPrintService(_spooler)
        {
            DocumentName = name,
            SingleJob = singleJob,
            MaxLabelsPerJob = 10,
            QueueWaitTimeout = TimeSpan.FromMilliseconds(50),
        };
        _main = new MainViewModel(new AppServices
        {
            Dialogs = _dialogs,
            Clipboard = new NullClipboard(),
            Dispatcher = new ImmediateDispatcher(),
            ImageFactory = new FakeImageFactory(),
            SettingsStore = new JsonSettingsStore<AppSettings>(Path.Combine(_folder, "ayarlar.json")),
            RecoveryFolder = Path.Combine(_folder, "Kurtarma"),
        });
    }

    public void Dispose()
    {
        _main.Dispose();
        PrintingServices.History = _previousHistory;
        PrintingServices.Profiles = _previousProfiles;
        CounterIntegration.Store = _previousCounters;
        PrintingServices.CreateTsplService = _previousFactory;
        PrintingServices.ReadPrinters = _previousDiscovery;
    }

    private static PrinterInfo FakePrinter() =>
        new() { Name = "Sahte XP-470B", DriverName = "Xprinter XP-470B", PortName = "USB001", UsbConnected = true };

    private PrintViewModel NewPrintWindow(PrintPreset? preset = null)
    {
        var vm = new PrintViewModel(_main, preset);
        vm.SelectedPrinter = FakePrinter();
        vm.Method = PrintMethod.TsplRaw;
        return vm;
    }

    [Fact]
    public async Task GenericCompanySetup_TemplateAndBarcode_ToUserRequestedTsplJob()
    {
        var setup = new SettingsViewModel(_main.Settings)
        {
            CompanyName = "Örnek Firma", CompanyAddress = "Örnek Sokak 1", CompanyWebsite = "https://example.com/",
        };
        setup.ApplyTo(_main.Settings);
        var project = AkelEtiket.Editor.Templates.SampleTemplates.CreateProductLabel(null, _main.Settings.Company);
        _main.OpenProjectAsNew(project, "Firma etiketi");
        var vm = NewPrintWindow();
        vm.UseData = false;
        vm.Copies = 2;
        Assert.Empty(_spooler.Jobs); // Açmak ve hazırlamak baskı başlatmaz.
        Assert.True(vm.PrintCommand.CanExecute(null));
        await vm.PrintCommand.ExecuteAsync(null);
        Assert.True(vm.LastSuccess, vm.ReportText);
        var job = Assert.Single(_spooler.Jobs);
        Assert.True(job.Completed);
        var print = Assert.Single(TsplInspector.Parse(job.Data.ToArray()), c => c.Name == "PRINT");
        Assert.Equal("1,2", print.Arguments);
        Assert.Equal(2, PrintingServices.History.ReadAll().Single().TotalLabels);
        Assert.Contains(project.Document.Elements.OfType<TextElement>(), e => e.Text.Contains("Örnek Firma"));
    }

    [Fact]
    public async Task FailedBatch_IsRecorded_ResumeStartsAtFirstUnsentLabel_AndCountersFollowSpooledLabels()
    {
        CounterIntegration.Store.Set("Koli", 1);
        _main.Session.AddElement(new TextElement { Text = "Koli {SAYAC.Koli:0000} / {SIRA}", X = 2, Y = 2, Width = 70, Height = 10 });
        var vm = NewPrintWindow();
        vm.UseData = false;
        vm.Copies = 30;
        // 10'arlı işler: ilk iki iş (20 etiket) kuyruğa gider, üçüncü işte yazma hatası olur.
        _spooler.FailOnJob = 3;

        await vm.PrintCommand.ExecuteAsync(null);

        Assert.False(vm.LastSuccess);
        Assert.True(vm.CanResume);
        Assert.Contains("10 etiket", vm.ResumeText);
        Assert.Equal(20, _spooler.CompletedLabels);
        Assert.Equal(21, CounterIntegration.Store.Peek("Koli"));
        Assert.Contains(vm.ReportLines, l => l.Text.Contains("10 etiket gönderilmedi"));
        var first = Assert.Single(PrintingServices.History.ReadAll());
        Assert.Equal("Baskı", first.Title);
        Assert.Equal(30, first.TotalLabels);
        Assert.Equal(20, first.LabelsSent);
        Assert.Equal(10, first.LabelsRemaining);
        Assert.Equal(20, first.LabelsSpooled);
        Assert.Equal("Sahte XP-470B", first.PrinterName);
        Assert.Contains("1–20", first.CounterSummary);
        Assert.Equal("1–20", first.CounterRanges!["Koli"]);
        Assert.NotEmpty(first.Errors);
        Assert.NotNull(PrintingServices.History.SnapshotPath(first));
        Assert.Equal(21, first.Items.First(i => i.State == PrintItemState.NotSent).Sequence);

        // Kaldığı yerden devam: yalnızca gönderilmeyen 10 etiket, sayaç 21'den sürer.
        _spooler.FailOnJob = null;
        await vm.ResumeCommand.ExecuteAsync(null);

        Assert.True(vm.LastSuccess, vm.ReportText);
        Assert.False(vm.CanResume);
        Assert.Equal(30, _spooler.CompletedLabels);
        Assert.Equal(31, CounterIntegration.Store.Peek("Koli"));
        Assert.Contains(_dialogs.Confirmations, c => c.Title == "Kaldığı yerden devam");
        var history = PrintingServices.History.ReadAll();
        Assert.Equal(2, history.Count);
        Assert.Equal("Devam baskısı", history[0].Title);
        Assert.Equal(10, history[0].LabelsSent);
        Assert.Equal(21, history[0].StartNumber);
        Assert.Contains("21–30", history[0].CounterSummary);
    }

    [Fact]
    public async Task SingleJob_SendsWholeBatchAsOneSpoolerJob()
    {
        _main.Session.AddElement(new TextElement { Text = "No {SIRA}", X = 2, Y = 2, Width = 60, Height = 10 });
        var vm = NewPrintWindow();
        vm.UseData = false;
        vm.Copies = 25;
        vm.SingleJob = true;
        await vm.PrintCommand.ExecuteAsync(null);
        Assert.True(vm.LastSuccess, vm.ReportText);
        var job = Assert.Single(_spooler.Jobs);
        Assert.Equal(25, job.Labels);
        Assert.True(PrintingServices.History.ReadAll()[0].SingleJob);
        Assert.True(_main.Settings.Printer.SingleJob);
    }

    [Fact]
    public async Task ResumeFromHistory_ReopensTemplateAndData_AndSelectsOnlyUnsentRows()
    {
        string csv = Path.Combine(_folder, "siparis.csv");
        File.WriteAllText(csv, "UrunAdi;Adet\nTabak;2\nKase;1\nBardak;3\n", new UTF8Encoding(true));
        var project = LabelProject.CreateBlank(100, 50);
        project.Document.Name = "Ürün etiketi";
        project.Document.Elements.Add(new TextElement { Text = "{UrunAdi}", X = 2, Y = 2, Width = 60, Height = 10 });
        string template = Path.Combine(_folder, "urun.akelabel");
        ProjectFile.Save(project, template);

        await _main.LoadDataFileAsync(csv, null);
        var table = _main.Data.Table!;
        int[] rows = [.. _main.Data.Rows.Select(r => r.RowNumber)];
        Assert.Equal(3, rows.Length);
        var info = new FileInfo(csv);
        var entry = new PrintHistoryEntry
        {
            Title = "Baskı",
            DocumentName = "Ürün etiketi",
            TemplatePath = template,
            TemplateHash = PrintHistoryStore.HashFile(template),
            UseData = true,
            DataFile = csv,
            DataSheet = table.SheetName,
            DataDelimiter = table.Delimiter?.ToString(),
            DataEncoding = table.EncodingName,
            DataFileLength = info.Length,
            DataFileWriteTimeUtc = info.LastWriteTimeUtc,
            PrinterName = "Sahte XP-470B",
            TotalLabels = 6,
            StartNumber = 1,
            Items =
            [
                new PrintHistoryItem { Sequence = 1, Row = rows[0], Copies = 2, State = PrintItemState.Sent },
                new PrintHistoryItem { Sequence = 2, Row = rows[1], Copies = 1, State = PrintItemState.NotSent },
                new PrintHistoryItem { Sequence = 3, Row = rows[2], Copies = 3, State = PrintItemState.NotSent },
            ],
        };
        _main.Data.Clear();

        Assert.True(await _main.ReprintFromHistoryAsync(entry, resumeOnly: true));

        Assert.Equal(template, _main.Session.Project.FilePath);
        Assert.Equal("Ürün etiketi", _main.Session.Document.Name);
        Assert.Equal(csv, _main.Data.Table!.SourcePath);
        Assert.Equal([false, true, true], _main.Data.Rows.Select(r => r.IsSelected));
        Assert.Equal(1, _main.Data.Rows[1].Copies);
        Assert.Equal(3, _main.Data.Rows[2].Copies);
        var print = Assert.IsType<PrintViewModel>(_dialogs.Dialogs.Last());
        Assert.True(print.UseData);
        Assert.Equal(2, print.StartNumber);
        Assert.Contains("Kaldığı yerden devam", print.Banner);
        Assert.Contains("4 etiket", print.PlanSummary);
        // Pencere yalnızca açıldı: onaysız hiçbir şey kuyruğa gönderilmedi.
        Assert.Empty(_spooler.Jobs);
    }

    [Fact]
    public async Task Reprint_ChangedTemplate_CanUseThePrintedSnapshot()
    {
        var project = LabelProject.CreateBlank(60, 40);
        project.Document.Name = "Raf etiketi";
        project.Document.Elements.Add(new TextElement { Text = "Basılan hâl", X = 2, Y = 2, Width = 50, Height = 10 });
        string template = Path.Combine(_folder, "raf.akelabel");
        ProjectFile.Save(project, template);
        string? hash = PrintHistoryStore.HashFile(template);
        var entry = new PrintHistoryEntry
        {
            Title = "Baskı",
            DocumentName = "Raf etiketi",
            TemplatePath = template,
            TemplateHash = hash,
            Copies = 7,
            StartNumber = 5,
            TotalLabels = 7,
            PrinterName = "Sahte XP-470B",
            Items = [new PrintHistoryItem { Sequence = 5, Copies = 7, State = PrintItemState.Sent }],
        };
        entry.SnapshotFile = PrintingServices.History.SaveSnapshot(project);

        project.Document.Elements.Add(new TextElement { Text = "Sonradan eklendi", X = 2, Y = 20, Width = 50, Height = 10 });
        ProjectFile.Save(project, template);

        // "Şablon değişmiş" sorusuna "Baskıdaki hâli" yanıtı.
        _dialogs.ConfirmAnswer = _ => true;
        Assert.True(await _main.ReprintFromHistoryAsync(entry, resumeOnly: false));

        Assert.Contains(_dialogs.Confirmations, c => c.Title == "Şablon değişmiş");
        Assert.Null(_main.Session.Project.FilePath);
        Assert.DoesNotContain(_main.Session.Document.Elements, e => e is TextElement { Text: "Sonradan eklendi" });
        Assert.Contains(_main.Session.Document.Elements, e => e is TextElement { Text: "Basılan hâl" });
        var print = Assert.IsType<PrintViewModel>(_dialogs.Dialogs.Last());
        Assert.False(print.UseData);
        Assert.Equal(7, print.Copies);
        Assert.Equal(5, print.StartNumber);
        Assert.Contains("Tekrar baskı", print.Banner);
        Assert.Empty(_spooler.Jobs);

        // "Güncel şablon" seçilirse dosyanın şimdiki hâli açılır.
        _dialogs.ConfirmAnswer = title => title != "Şablon değişmiş";
        Assert.True(await _main.ReprintFromHistoryAsync(entry, resumeOnly: false));
        Assert.Equal(template, _main.Session.Project.FilePath);
        Assert.Contains(_main.Session.Document.Elements, e => e is TextElement { Text: "Sonradan eklendi" });
    }

    [Fact]
    public void BuiltInProfile_AppliesNeutralSettings_AndCanBeSavedAsUserProfile()
    {
        _main.Session.Document.Label.GapMm = 3;
        var vm = NewPrintWindow();
        var akel = vm.ProfileList.First(p => p.IsBuiltIn);
        vm.SelectedProfile = akel;

        Assert.Equal(PrintMethod.TsplRaw, vm.Method);
        Assert.Equal(4, vm.Speed);
        Assert.Equal(8, vm.Density);
        Assert.False(vm.Rotate180);
        Assert.Equal(3, _main.Session.Document.Label.GapMm);
        Assert.Equal(MediaType.Gap, _main.Session.Document.Label.Media);
        Assert.Contains(6, vm.SpeedChoices);
        Assert.Null(vm.ProfileWarning);
        Assert.Equal("Sahte XP-470B", vm.SelectedPrinter!.Name);
        vm.SelectedPrinter = FakePrinter();

        vm.OffsetXMm = -0.5;
        _dialogs.PromptAnswer = "Depo XP-470B";
        vm.SaveProfileAsCommand.Execute(null);
        var saved = PrintingServices.Profiles.Find("Depo XP-470B")!;
        Assert.False(saved.IsBuiltIn);
        Assert.Equal(-0.5, saved.OffsetXMm);
        Assert.Equal(4, saved.Speed);
        Assert.Equal(3, saved.GapMm);
        Assert.Equal("Sahte XP-470B", saved.PrinterName);
        Assert.Equal("Depo XP-470B", vm.SelectedProfile!.Name);
        Assert.True(vm.IsUserProfileSelected);
        Assert.Equal(_main.Session.Document.Label.WidthMm, saved.LabelWidthMm);

        // Profilin rulo ölçüsü yeni etiketlerin varsayılan ölçüsü olur (açık tasarım değişmez).
        _main.Settings.DefaultLabelWidthMm = 40;
        _main.Settings.DefaultLabelHeightMm = 30;
        vm.ApplyProfile(saved);
        Assert.Equal(saved.LabelWidthMm, _main.Settings.DefaultLabelWidthMm);
        Assert.Equal(saved.LabelHeightMm, _main.Settings.DefaultLabelHeightMm);
        Assert.Equal(saved.LabelWidthMm, _main.Session.Document.Label.WidthMm);

        // Hazır profilin adıyla kayıt yapılamaz.
        _dialogs.PromptAnswer = PrinterProfileStore.AkelProfileName;
        vm.SaveProfileAsCommand.Execute(null);
        Assert.Contains("hazır bir profile ait", _dialogs.Messages.Last());
        Assert.Equal(2, PrintingServices.Profiles.All.Count);
    }

    [Fact]
    public async Task CalibrationHelper_UsesConfirmedPrintPath_AndStoresMeasuredOffsetsInProfile()
    {
        var vm = NewPrintWindow();
        var calibration = new CalibrationViewModel(vm, _dialogs);
        try
        {
            await calibration.PrintLabelCommand.ExecuteAsync(null);
            Assert.Contains(_dialogs.Confirmations, c => c.Title == "Kalibrasyon etiketi");
            Assert.Contains("kuyruğuna gönderildi", calibration.Status);
            var job = Assert.Single(_spooler.Jobs);
            Assert.Equal(1, job.Labels);
            Assert.Contains("BITMAP", job.Text);
            var entry = PrintingServices.History.ReadAll()[0];
            Assert.Equal("Kalibrasyon etiketi", entry.Title);
            Assert.Null(entry.SnapshotFile);
            Assert.False(vm.CanResume);

            // Çerçeve soldan 3 mm, üstten 1,5 mm ölçüldü: içerik 1 mm sağa, 0,5 mm yukarı kaymış.
            calibration.MeasuredLeftMm = 3.0;
            calibration.MeasuredTopMm = 1.5;
            Assert.Equal(-1.0, calibration.NewOffsetXMm, 3);
            Assert.Equal(0.5, calibration.NewOffsetYMm, 3);
            Assert.Contains("sağa", calibration.ShiftText);
            _dialogs.PromptAnswer = "XP-470B kalibre";
            calibration.ApplyCommand.Execute(null);

            Assert.Equal(-1.0, vm.OffsetXMm, 3);
            Assert.Equal(0.5, vm.OffsetYMm, 3);
            Assert.Equal(-1.0, _main.Settings.Printer.OffsetXMm, 3);
            var profile = PrintingServices.Profiles.Find("XP-470B kalibre")!;
            Assert.Equal(-1.0, profile.OffsetXMm, 3);
            Assert.Equal(0.5, profile.OffsetYMm, 3);
            Assert.Contains("profiline kaydedildi", calibration.Status);
            Assert.Equal(CalibrationLabelFactory.FrameInsetMm, calibration.MeasuredLeftMm);

            // Seçili kullanıcı profili varsa ölçüm doğrudan o profile yazılır.
            calibration.MeasuredLeftMm = 2.25;
            calibration.ApplyCommand.Execute(null);
            Assert.Equal(-1.25, PrintingServices.Profiles.Find("XP-470B kalibre")!.OffsetXMm, 3);

            // Yön ters çıktıysa 180° çevrilir.
            bool before = vm.Rotate180;
            calibration.FlipDirectionCommand.Execute(null);
            Assert.NotEqual(before, vm.Rotate180);
            Assert.Equal(vm.Rotate180, _main.Settings.Printer.Rotate180);

            // Sensör kalibrasyonu da onaylı ve sahte kuyruk üzerinden.
            await calibration.DetectSensorCommand.ExecuteAsync(null);
            Assert.Contains(_dialogs.Confirmations, c => c.Title == "Sensör kalibrasyonu" && c.Message.Contains("GAPDETECT"));
            Assert.Equal(2, _spooler.Jobs.Count);
            Assert.Contains("GAPDETECT", _spooler.Jobs[1].Text);
        }
        finally
        {
            calibration.Close();
        }
    }

    [Fact]
    public void CalibrationLabel_RendersCleanly_ForCommonRollSizes()
    {
        foreach (var (w, h) in new[] { (100.0, 50.0), (40.0, 30.0), (104.0, 150.0), (58.0, 40.0) })
        {
            var setup = new LabelSetup { WidthMm = w, HeightMm = h, GapMm = 2 };
            var doc = CalibrationLabelFactory.Create(setup, "Sahte XP-470B", -0.5, 0.25, rotate180: true, DateTime.Now);
            Assert.Equal(w, doc.Label.WidthMm);
            Assert.Equal(2, doc.Label.GapMm);
            using var renderer = new LabelRenderer(new LabelProject(doc).Assets);
            var result = renderer.RenderLabel(doc, new RenderOptions { DotsPerMm = 8, Mode = RenderMode.Print });
            Assert.False(result.HasErrors, $"{w}×{h}: " + string.Join("; ", result.Errors));
            Assert.Equal((int)Math.Round(w * 8), result.Bitmap.Width);
            // Çerçevenin dış kenarı tam 2 mm (16 nokta) içeride: 15. sütun boş, 16. sütun siyah.
            int y = result.Bitmap.Height / 2 + 3;
            Assert.False(result.Bitmap[15, y], $"{w}×{h}: çerçeve 2 mm'den önce başlıyor");
            Assert.True(result.Bitmap[16, y], $"{w}×{h}: çerçeve 2 mm'de değil");
            Assert.Contains(doc.Elements, e => e is TextElement { Text: "▲ ÜST" });
        }
    }

    [Theory]
    [InlineData(0, 2.0, 0)]
    [InlineData(0, 3.0, -1.0)]
    [InlineData(0, 1.25, 0.75)]
    [InlineData(0.5, 2.5, 0)]
    [InlineData(-1.0, 1.0, 0)]
    public void CorrectedOffset_MovesContentBackToTheFrame(double current, double measured, double expected) =>
        Assert.Equal(expected, CalibrationLabelFactory.CorrectedOffset(current, measured), 3);

    [GeneratedRegex(@"/Type\s*/Page(?![a-zA-Z])")]
    private static partial Regex PageRegex();

    [Fact]
    public async Task ExportPdf_SelectedRows_OnePagePerLabel_WithoutConsumingCounters()
    {
        CounterIntegration.Store.Set("Koli", 7);
        string csv = Path.Combine(_folder, "pdf.csv");
        File.WriteAllText(csv, "UrunAdi;Adet\nTabak;2\nKase;1\nBardak;3\n", new UTF8Encoding(true));
        _main.Session.AddElement(new TextElement { Text = "{UrunAdi} {SAYAC.Koli}", X = 2, Y = 2, Width = 60, Height = 10 });
        await _main.LoadDataFileAsync(csv, null);
        Assert.Equal(6, _main.Data.SelectedLabelCount);

        string pdf = Path.Combine(_folder, "etiketler.pdf");
        _dialogs.SaveFileAnswer = pdf;
        await _main.ExportPdfCommand.ExecuteAsync(null);

        Assert.True(File.Exists(pdf), _main.StatusMessage);
        string content = Encoding.Latin1.GetString(File.ReadAllBytes(pdf));
        Assert.Equal(6, PageRegex().Matches(content).Count);
        var box = Regex.Match(content, @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)\s*\]");
        Assert.True(box.Success);
        double widthPt = double.Parse(box.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.Equal(_main.Session.Document.Label.WidthMm / 25.4 * 72, widthPt, 1);
        Assert.Contains(_dialogs.Confirmations, c => c.Message.Contains("önizleme değeriyle"));
        Assert.Equal(7, CounterIntegration.Store.Peek("Koli"));
        Assert.Empty(_spooler.Jobs);
    }

    [Fact]
    public void CountersWindow_SetsAndResetsValues()
    {
        _main.Session.Document.Name = "Koli Etiketi";
        _main.Session.AddElement(new TextElement { Text = "{SAYAC:000000}", X = 2, Y = 2, Width = 60, Height = 10 });
        var vm = new CountersViewModel(CounterIntegration.Store, _dialogs, _main.Session.Document);
        var row = Assert.Single(vm.Counters);
        Assert.Equal("Koli Etiketi", row.Name);
        Assert.False(row.Exists);
        Assert.True(row.UsedByDocument);

        _dialogs.PromptAnswer = "500";
        vm.SetNextCommand.Execute(null);
        Assert.Equal(500, CounterIntegration.Store.Peek("Koli Etiketi"));
        Assert.True(vm.SelectedCounter!.Exists);

        _dialogs.PromptAnswer = "5";
        vm.SetStepCommand.Execute(null);
        Assert.Equal(5, CounterIntegration.Store.Get("Koli Etiketi").Step);

        vm.ResetCommand.Execute(null);
        Assert.Equal(1, CounterIntegration.Store.Peek("Koli Etiketi"));
        Assert.Equal(5, CounterIntegration.Store.Get("Koli Etiketi").Step);

        _dialogs.PromptAnswer = "abc";
        vm.SetNextCommand.Execute(null);
        Assert.Contains("tam sayı", _dialogs.Messages.Last());

        vm.DeleteCommand.Execute(null);
        Assert.Empty(CounterIntegration.Store.List());
        Assert.False(Assert.Single(vm.Counters).Exists);
    }

    private sealed class NullClipboard : IClipboardService
    {
        public void SetElements(string payloadJson, string plainText)
        {
        }

        public string? GetElements() => null;
        public bool ContainsElements() => false;
        public byte[]? GetImagePng() => null;
        public string? GetText() => null;
    }
}

/// <summary>Kullanıcı yanıtlarını taklit eden diyalog servisi (sorulanları kaydeder).</summary>
internal sealed class ScriptedDialogs : IDialogService
{
    public List<(string Title, string Message)> Confirmations { get; } = [];
    public List<string> Messages { get; } = [];
    public List<object> Dialogs { get; } = [];
    public Func<string, bool> ConfirmAnswer { get; set; } = _ => true;
    public Func<object, bool> DialogAnswer { get; set; } = _ => false;
    public string? PromptAnswer { get; set; }
    public string? SaveFileAnswer { get; set; }
    public string? OpenFileAnswer { get; set; }

    public string? ShowOpenFileDialog(string title, string filter, string? initialDirectory = null) => OpenFileAnswer;
    public string? ShowSaveFileDialog(string title, string filter, string defaultFileName, string? initialDirectory = null) => SaveFileAnswer;
    public void ShowMessage(string title, string message, MessageKind kind = MessageKind.Info) => Messages.Add(message);

    public bool Confirm(string title, string message, string confirmText = "Evet", string cancelText = "Hayır")
    {
        Confirmations.Add((title, message));
        return ConfirmAnswer(title);
    }

    public SaveChangesChoice AskSaveChanges(string documentName) => SaveChangesChoice.Discard;
    public string? Prompt(string title, string message, string defaultValue = "") => PromptAnswer ?? defaultValue;

    public bool ShowDialog(object viewModel)
    {
        Dialogs.Add(viewModel);
        return DialogAnswer(viewModel);
    }
}
