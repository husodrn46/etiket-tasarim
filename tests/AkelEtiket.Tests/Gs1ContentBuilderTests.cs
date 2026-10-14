using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.Templates;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.Gs1;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;

namespace AkelEtiket.Tests;

/// <summary>
/// "GS1 içerik oluşturucu": satırlardan içerik kurma, var olan içeriği ayrıştırma, canlı doğrulama, kayıtlı GTIN ve
/// (00){SSCC} kısayolları; GS1 DataMatrix örnek şablonunun baskı görüntüsünden okunması.
/// </summary>
public sealed class Gs1ContentBuilderTests
{
    private const string Gtin14 = "08691234567890";

    private static Gs1ContentBuilderViewModel Builder(string? content, BarcodeType type = BarcodeType.Gs1DataMatrix,
        Gs1CompanyProfile? profile = null, IEnumerable<string>? fields = null) => new(type, content, profile, fields);

    private static bool HasMessage(Gs1ContentBuilderViewModel vm, Gs1MessageKind kind, string text) =>
        vm.Messages.Any(m => m.Kind == kind && m.Text.Contains(text, StringComparison.Ordinal));

    [Fact]
    public void Compose_WritesRowsInOrder_AndTrimsValues()
    {
        string content = Gs1ContentBuilderViewModel.Compose([("01", Gtin14), ("17", " 281231 "), ("10", "LOT1"), ("21", "{Seri}")]);
        Assert.Equal("(01)08691234567890(17)281231(10)LOT1(21){Seri}", content);
    }

    [Theory]
    [InlineData(BarcodeType.Gs1_128)]
    [InlineData(BarcodeType.Gs1DataMatrix)]
    [InlineData(BarcodeType.Gs1QrCode)]
    public void ExistingContent_IsSplitIntoRows_AndRebuiltUnchanged(BarcodeType type)
    {
        const string content = "(01)08691234567890(17)281231(10)ABC123";
        var vm = Builder(content, type);
        Assert.Null(vm.LoadNote);
        Assert.Equal(new[] { "01", "17", "10" }, vm.Rows.Select(r => r.Ai!.Ai));
        Assert.Equal(new[] { Gtin14, "281231", "ABC123" }, vm.Rows.Select(r => r.Value));
        Assert.Equal(content, vm.Content);
        Assert.True(vm.CanAccept);
        Assert.True(HasMessage(vm, Gs1MessageKind.Ok, "Okunur metin: (01)08691234567890(17)281231(10)ABC123"));
        Assert.All(vm.Rows, r => Assert.Null(r.Problem));
    }

    [Fact]
    public void EmptyContent_StartsWithBlankGtinRow_AndCannotBeApplied()
    {
        var vm = Builder("");
        var row = Assert.Single(vm.Rows);
        Assert.Equal("01", row.Ai!.Ai);
        Assert.Equal("Değer boş.", row.Problem);
        Assert.Equal(Gs1MessageKind.Error, row.ProblemKind);
        Assert.False(vm.CanAccept);
    }

    [Fact]
    public void NonGs1Content_IsReported_AndReplacedOnApply()
    {
        var vm = Builder("AKEL0001");
        Assert.NotNull(vm.LoadNote);
        Assert.Contains("AKEL0001", vm.LoadNote);
        Assert.Equal("01", Assert.Single(vm.Rows).Ai!.Ai);
    }

    [Fact]
    public void UnknownAi_KeepsRowWithoutAi_AndBlocksApply()
    {
        var vm = Builder("(01)08691234567890(999)X");
        Assert.Contains("(999)", vm.LoadNote);
        Assert.Null(vm.Rows[1].Ai);
        Assert.Equal("AI seçin.", vm.Rows[1].Problem);
        Assert.False(vm.CanAccept);
    }

    [Theory]
    [InlineData("01", "08691234567891", "kontrol basamağı hatalı")]
    [InlineData("17", "281332", "tarih YYAAGG")]
    [InlineData("17", "28123", "6 karakter olmalı")]
    [InlineData("10", "ÇAY", "kullanılamaz")]
    [InlineData("10", "A(B", "parantez")]
    public void InvalidValue_IsRowError(string ai, string value, string expected)
    {
        var vm = Builder("");
        var row = vm.Rows[0];
        row.Ai = Gs1AiCatalog.Find(ai);
        row.Value = value;
        Assert.Equal(Gs1MessageKind.Error, row.ProblemKind);
        Assert.Contains(expected, row.Problem);
        Assert.True(HasMessage(vm, Gs1MessageKind.Error, "1 satırda hata var"));
        Assert.False(vm.CanAccept);
    }

    [Fact]
    public void DuplicateAi_IsError()
    {
        var vm = Builder("(01)08691234567890(10)A(10)B");
        Assert.True(HasMessage(vm, Gs1MessageKind.Error, "(10) birden fazla kez kullanılmış"));
        Assert.False(vm.CanAccept);
    }

    [Fact]
    public void FullValidation_UsesSymbologyLimits()
    {
        // 16 + 22 + ayırıcı + 22 = 61 veri karakteri: GS1-128'in 48 sınırını aşar, 2B GS1 için sorun değildir.
        const string content = "(01)08691234567890(10)ABCDEFGHIJKLMNOPQRST(21)ABCDEFGHIJKLMNOPQRST";
        var linear = Builder(content, BarcodeType.Gs1_128);
        Assert.True(HasMessage(linear, Gs1MessageKind.Error, "en fazla 48"));
        Assert.False(linear.CanAccept);

        var matrix = Builder(content, BarcodeType.Gs1DataMatrix);
        Assert.True(matrix.CanAccept);
        Assert.True(HasMessage(matrix, Gs1MessageKind.Ok, "Geçerli GS1 DataMatrix içeriği"));
    }

    [Fact]
    public void SsccShortcut_InsertsFirstRow_WithPlaceholder()
    {
        var vm = Builder("(02)08691234567890(37)24(10){Parti}", BarcodeType.Gs1_128);
        vm.AddSsccCommand.Execute(null);
        Assert.Equal("00", vm.Rows[0].Ai!.Ai);
        Assert.Equal("{SSCC}", vm.Rows[0].Value);
        Assert.Equal("(00){SSCC}(02)08691234567890(37)24(10){Parti}", vm.Content);
        Assert.True(vm.CanAccept);
        Assert.True(HasMessage(vm, Gs1MessageKind.Info, "baskı anında GS1 Firma Profili'nden ayrılır"));
        Assert.True(HasMessage(vm, Gs1MessageKind.Info, "veri alanı var"));
        Assert.False(HasMessage(vm, Gs1MessageKind.Info, "Lojistik etiket standardı"));

        // Aynı kısayol ikinci kez satır eklemez.
        vm.AddSsccCommand.Execute(null);
        Assert.Single(vm.Rows, r => r.Ai?.Ai == "00");
    }

    [Fact]
    public void SsccInTwoDimensional_RemindsThatLogisticLabelsNeedGs1_128()
    {
        var vm = Builder("(00){SSCC}", BarcodeType.Gs1DataMatrix);
        Assert.True(HasMessage(vm, Gs1MessageKind.Info, "Lojistik etiket standardı SSCC için GS1-128"));
    }

    [Fact]
    public void SsccField_IsOnlyAllowedWithAi00()
    {
        var vm = Builder("(01){SSCC}");
        Assert.Equal(Gs1MessageKind.Error, vm.Rows[0].ProblemKind);
        Assert.Contains("yalnızca (00)", vm.Rows[0].Problem);
        Assert.False(vm.CanAccept);
    }

    [Fact]
    public void FieldRows_AreDeferredToPrintTime_AndUnknownColumnsWarned()
    {
        var vm = Builder("(01)08691234567890(10){Lot}", fields: ["UrunAdi", "Parti"]);
        Assert.Equal(Gs1MessageKind.Warning, vm.Rows[1].ProblemKind);
        Assert.Contains("{Lot}", vm.Rows[1].Problem);
        Assert.True(vm.CanAccept);
        Assert.Contains("{Parti}", vm.Rows[1].Suggestions);

        vm.Rows[1].Value = "{Parti}";
        Assert.Equal(Gs1MessageKind.Info, vm.Rows[1].ProblemKind);
        Assert.Contains("{Parti}", vm.Rows[1].Problem);
    }

    [Fact]
    public void Suggestions_FollowTheAi()
    {
        var vm = Builder("(00){SSCC}(11)261013(412)8691234567890");
        Assert.Contains("{SSCC}", vm.Rows[0].Suggestions);
        Assert.Contains("{TARIH:yyMMdd}", vm.Rows[1].Suggestions);
        Assert.Contains("{GLN}", vm.Rows[2].Suggestions);
        Assert.DoesNotContain("{GLN}", vm.Rows[1].Suggestions);
    }

    [Fact]
    public void RegisteredGtins_ArePaddedTo14_AndInvalidOnesSkipped()
    {
        var profile = new Gs1CompanyProfile
        {
            RegisteredGtins =
            [
                new RegisteredGtin { Gtin = "8691234567890", ProductCode = "TST-1001", Description = "Servis tabağı" },
                new RegisteredGtin { Gtin = "8691234567891", Description = "Kontrol basamağı hatalı" },
                new RegisteredGtin { Gtin = "08691234567890", Description = "Aynı GTIN, 14 hane" },
            ],
        };
        var vm = Builder("", profile: profile);
        var choice = Assert.Single(vm.Gtins);
        Assert.Equal(Gtin14, choice.Gtin14);
        Assert.Equal("08691234567890 — TST-1001 · Servis tabağı", choice.Display);
        Assert.True(vm.HasGtins);

        Assert.False(vm.UseGtinAs01Command.CanExecute(null));
        vm.SelectedGtin = choice;
        Assert.True(vm.UseGtinAs01Command.CanExecute(null));
        vm.UseGtinAs01Command.Execute(null);
        Assert.Equal($"(01){Gtin14}", vm.Content);
        Assert.Single(vm.Rows);
        Assert.True(vm.CanAccept);
    }

    [Fact]
    public void GtinInContent_IsPreselected_WhenRegistered()
    {
        var profile = new Gs1CompanyProfile { RegisteredGtins = [new RegisteredGtin { Gtin = "8691234567890" }] };
        Assert.Equal(Gtin14, Builder($"(01){Gtin14}(10)A", profile: profile).SelectedGtin?.Gtin14);
        Assert.Null(Builder("(01)08691234567883", profile: profile).SelectedGtin);
    }

    [Fact]
    public void GtinAs02_ReusesBlankGtinRow_AndWarnsAboutPairs()
    {
        var profile = new Gs1CompanyProfile { RegisteredGtins = [new RegisteredGtin { Gtin = "8691234567890" }] };
        var vm = Builder("", profile: profile);
        vm.SelectedGtin = vm.Gtins[0];
        vm.UseGtinAs02Command.Execute(null);
        Assert.Equal("02", Assert.Single(vm.Rows).Ai!.Ai);
        Assert.True(HasMessage(vm, Gs1MessageKind.Warning, "(37) adet ile birlikte"));

        // SSCC satırından sonra yazılır; var olan (02) satırı güncellenir.
        vm.AddSsccCommand.Execute(null);
        vm.UseGtinAs02Command.Execute(null);
        Assert.Equal(new[] { "00", "02" }, vm.Rows.Select(r => r.Ai!.Ai));
    }

    [Fact]
    public void NoRegisteredGtins_ExplainsWhereToAddThem()
    {
        var vm = Builder("", profile: new Gs1CompanyProfile());
        Assert.False(vm.HasGtins);
        Assert.Contains("GS1 Firma Profili", vm.GtinNote);
    }

    [Fact]
    public void PairingWarnings_FollowGs1Rules()
    {
        Assert.Empty(Gs1ContentBuilderViewModel.PairingWarnings(["01", "17", "10", "21"]));
        Assert.Empty(Gs1ContentBuilderViewModel.PairingWarnings(["00", "02", "37", "10"]));
        Assert.Contains(Gs1ContentBuilderViewModel.PairingWarnings(["01", "02", "37"]), w => w.StartsWith("(01) ve (02)", StringComparison.Ordinal));
        Assert.Contains(Gs1ContentBuilderViewModel.PairingWarnings(["01", "37"]), w => w.StartsWith("(37) adet", StringComparison.Ordinal));
        Assert.Contains(Gs1ContentBuilderViewModel.PairingWarnings(["00", "10"]), w => w.StartsWith("(10) bir ürün numarasına", StringComparison.Ordinal));
        Assert.Contains(Gs1ContentBuilderViewModel.PairingWarnings(["02", "37", "21"]), w => w.StartsWith("(21) seri numarası", StringComparison.Ordinal));
        Assert.Contains(Gs1ContentBuilderViewModel.PairingWarnings(["3103"]), w => w.StartsWith("(3103)", StringComparison.Ordinal));
    }

    [Fact]
    public void MovingRows_ChangesOrder_AndOrderingHint()
    {
        var vm = Builder("(10)ABC(01)08691234567890");
        Assert.True(HasMessage(vm, Gs1MessageKind.Info, "sabit uzunluklu AI'ları"));
        vm.MoveRowDownCommand.Execute(vm.Rows[0]);
        Assert.Equal("(01)08691234567890(10)ABC", vm.Content);
        Assert.False(HasMessage(vm, Gs1MessageKind.Info, "sabit uzunluklu AI'ları"));

        vm.MoveRowUpCommand.Execute(vm.Rows[1]);
        Assert.Equal("(10)ABC(01)08691234567890", vm.Content);
        vm.RemoveRowCommand.Execute(vm.Rows[0]);
        Assert.Equal("(01)08691234567890", vm.Content);
    }

    [Fact]
    public void RemovingAllRows_BlocksApply()
    {
        var vm = Builder("(01)08691234567890");
        vm.RemoveRowCommand.Execute(vm.Rows[0]);
        Assert.Equal("", vm.Content);
        Assert.True(HasMessage(vm, Gs1MessageKind.Error, "En az bir AI"));
        Assert.False(vm.CanAccept);
    }

    [Fact]
    public void NonGs1Symbology_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => Builder("(01)08691234567890", BarcodeType.DataMatrix));
        Assert.Throws<ArgumentException>(() => Builder("(01)08691234567890", BarcodeType.QrCode));
    }

    [Fact]
    public void Gs1DataMatrixSample_PrintImageDecodesAsGs1()
    {
        string logo = EditorTestHelpers.FindRepoFile("src", "AkelEtiket.App", "Assets", "AkelEtiket-256.png");
        var project = SampleTemplates.CreateGs1DataMatrixLabel(File.ReadAllBytes(logo));
        Assert.Contains(SampleTemplates.CreateAll(null), t => t.Name == SampleTemplates.Gs1DataMatrixLabelName);
        Assert.Equal(2, project.Document.GetRequiredFormatVersion());

        var barcode = Assert.Single(project.Document.Elements.OfType<BarcodeElement>());
        Assert.Equal(BarcodeType.Gs1DataMatrix, barcode.Symbology);
        var vm = Builder(barcode.Content);
        Assert.True(vm.CanAccept);
        Assert.Equal(barcode.Content, vm.Content);

        // Baskı kipinde 8 nokta/mm render; barkod birleşik 1-bit görüntüden kesilip okunur ("]d2" beklenir).
        using var producer = new LabelProducer(project.Document, project.Assets, verifyBarcodes: true);
        var (bitmap, problems, warnings) = producer.Produce(new PrintItem(1, null, 1, new BuiltInFieldSource(1, DateTime.Now)), 8);
        Assert.NotNull(bitmap);
        Assert.Empty(problems);
        Assert.DoesNotContain(warnings, w => !w.Contains("%80"));
    }
}

/// <summary>Özellikler panelindeki "GS1 içerik oluşturucu…" komutu: içerik tek geri alma adımıyla barkoda yazılır.</summary>
[Collection("Yapay zekâ masaüstü")]
public sealed class Gs1BuilderCommandTests : IDisposable
{
    private readonly BuilderDialogs _dialogs = new();
    private readonly MainViewModel _main;

    public Gs1BuilderCommandTests()
    {
        string folder = Path.Combine(TestDataRoot.Root, "gs1-builder-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        _main = new MainViewModel(new AppServices
        {
            Dialogs = _dialogs,
            Clipboard = new NoClipboard(),
            Dispatcher = new ImmediateDispatcher(),
            ImageFactory = new FakeImageFactory(),
            SettingsStore = new JsonSettingsStore<AppSettings>(Path.Combine(folder, "ayarlar.json")),
            RecoveryFolder = Path.Combine(folder, "Kurtarma"),
        });
    }

    public void Dispose() => _main.Dispose();

    private BarcodeElement AddBarcode(BarcodeType type)
    {
        _main.AddBarcodeCommand.Execute(type);
        return Assert.IsType<BarcodeElement>(_main.SelectedElement?.Model);
    }

    [Fact]
    public void Apply_WritesContent_AsOneUndoStep()
    {
        var barcode = AddBarcode(BarcodeType.Gs1DataMatrix);
        string original = barcode.Content;
        Assert.Equal("(01)08691234567890(17)281231(10)ABC123", original);
        _dialogs.OnShowDialog = vm =>
        {
            var builder = Assert.IsType<Gs1ContentBuilderViewModel>(vm);
            Assert.Equal(original, builder.Content);
            builder.Rows.Single(r => r.Ai?.Ai == "10").Value = "LOT9";
            return builder.CanAccept;
        };
        _main.OpenGs1BuilderCommand.Execute(null);
        Assert.Equal("(01)08691234567890(17)281231(10)LOT9", barcode.Content);

        // Geri al belgeyi kayıttan yeniden kurar: öğe kimliğiyle yeniden bulunur; barkod silinmez, yalnızca içerik döner.
        _main.UndoCommand.Execute(null);
        var restored = Assert.IsType<BarcodeElement>(_main.Session.Document.FindElement(barcode.Id));
        Assert.Equal(original, restored.Content);
    }

    [Fact]
    public void Cancel_LeavesContentUnchanged()
    {
        var barcode = AddBarcode(BarcodeType.Gs1_128);
        string original = barcode.Content;
        _dialogs.OnShowDialog = vm =>
        {
            ((Gs1ContentBuilderViewModel)vm).Rows[0].Value = "123";
            return false;
        };
        _main.OpenGs1BuilderCommand.Execute(null);
        Assert.Equal(original, barcode.Content);
    }

    [Fact]
    public void PlainDataMatrix_DoesNotOpenBuilder()
    {
        AddBarcode(BarcodeType.DataMatrix);
        bool opened = false;
        _dialogs.OnShowDialog = _ => opened = true;
        _main.OpenGs1BuilderCommand.Execute(null);
        Assert.False(opened);
    }

    private sealed class BuilderDialogs : IDialogService
    {
        public Func<object, bool> OnShowDialog { get; set; } = _ => false;
        public string? ShowOpenFileDialog(string title, string filter, string? initialDirectory = null) => null;
        public string? ShowSaveFileDialog(string title, string filter, string defaultFileName, string? initialDirectory = null) => null;
        public void ShowMessage(string title, string message, MessageKind kind = MessageKind.Info) { }
        public bool Confirm(string title, string message, string confirmText = "Evet", string cancelText = "Hayır") => true;
        public SaveChangesChoice AskSaveChanges(string documentName) => SaveChangesChoice.Discard;
        public string? Prompt(string title, string message, string defaultValue = "") => null;
        public bool ShowDialog(object viewModel) => OnShowDialog(viewModel);
    }

    private sealed class NoClipboard : IClipboardService
    {
        public void SetElements(string payloadJson, string plainText) { }
        public string? GetElements() => null;
        public bool ContainsElements() => false;
        public byte[]? GetImagePng() => null;
        public string? GetText() => null;
    }
}
