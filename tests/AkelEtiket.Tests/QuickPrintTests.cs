using System.Text;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.ProjectManager;
using AkelEtiket.PrintEngine;

namespace AkelEtiket.Tests;

/// <summary>Ürün listesi ve Hızlı Baskı: aktarma, arama, okutma, şablondan baskı penceresine geçiş (baskı yapılmaz).</summary>
[Collection("Yapay zekâ masaüstü")]
public sealed class QuickPrintTests : IDisposable
{
    private readonly RecordingDialogs _dialogs = new();
    private readonly MainViewModel _main;
    private readonly PrintingServices.PrinterListReader _previousDiscovery = PrintingServices.ReadPrinters;
    private readonly string _folder = Path.Combine(TestDataRoot.Root, "hizli-" + Guid.NewGuid().ToString("N")[..6]);

    public QuickPrintTests()
    {
        Directory.CreateDirectory(_folder);
        PrintingServices.ReadPrinters = (out IReadOnlyList<PrinterInfo> printers, out string? error) =>
        {
            printers = []; error = null; return true;
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
        PrintingServices.ReadPrinters = _previousDiscovery;
    }

    private string WriteCsv()
    {
        string path = Path.Combine(_folder, "urunler.csv");
        File.WriteAllText(path,
            "StokKodu;UrunAdi;Barkod\n" +
            "TST001;Melamin Tabak;8691234567890\n" +
            "TST002;Melamin Kase;8691234567807\n" +
            "TST010;Servis Tepsisi;8691234567814\n", new UTF8Encoding(true));
        return path;
    }

    private string WriteTemplate()
    {
        var doc = LabelDocument.CreateBlank(100, 50, "Hızlı Etiket");
        doc.Elements.Add(new TextElement { Text = "{UrunAdi}", X = 5, Y = 5, Width = 90, Height = 10 });
        doc.Elements.Add(new BarcodeElement { Symbology = BarcodeType.Ean13, Content = "{Barkod}", X = 20, Y = 20, Width = 50, Height = 25 });
        string path = Path.Combine(_main.Templates.Library.Folder, "Hızlı Etiket.akelabel");
        Directory.CreateDirectory(_main.Templates.Library.Folder);
        ProjectFile.Save(new LabelProject(doc), path);
        return path;
    }

    [Fact]
    public async Task Products_ImportAndSearch_TurkishInsensitive()
    {
        var products = new ProductsViewModel(_main.Products, _dialogs);
        Assert.True(await products.ImportFileAsync(WriteCsv(), keyColumn: null));
        Assert.Equal(3, products.Count);
        Assert.Equal("StokKodu", _main.Products.Data.KeyColumn);

        products.Query = "kase";
        Assert.Equal("TST002", Assert.Single(products.Results).Key);
        products.Query = "8691234567814";
        Assert.Equal("TST010", products.Results.First().Key);
        products.Query = "SERVİS";
        Assert.Equal("TST010", Assert.Single(products.Results).Key);
    }

    [Fact]
    public async Task QuickPrint_ScanSelectsProduct_AndOpensPrintDialogWithOneRow()
    {
        var products = new ProductsViewModel(_main.Products, _dialogs);
        await products.ImportFileAsync(WriteCsv(), keyColumn: null);
        string template = WriteTemplate();
        _main.Templates.Refresh();

        var quick = new QuickPrintViewModel(_main, new ProductsViewModel(_main.Products, _dialogs));
        quick.SelectedTemplate = quick.Templates.Single(t => t.Info.FilePath == template);
        Assert.False(quick.PrintCommand.CanExecute(null));   // ürün seçilmedi

        quick.ScanInput = "8691234567807";
        quick.ScanCommand.Execute(null);
        Assert.Equal("TST002", quick.Products.SelectedProduct?.Key);
        Assert.NotNull(quick.PreviewPng);
        Assert.Null(quick.Problem);
        quick.Copies = 3;
        Assert.True(quick.PrintCommand.CanExecute(null));

        bool closed = false;
        quick.CloseRequested += (_, _) => closed = true;
        quick.PrintCommand.Execute(null);

        Assert.True(closed);
        var print = Assert.IsType<PrintViewModel>(_dialogs.Shown.Last());
        Assert.Null(_main.Session.Project.FilePath);   // şablon dosyası değişmez: yeni belge
        Assert.Equal(2, _main.Session.Document.Elements.Count);
        var row = Assert.Single(_main.Data.Rows);
        Assert.Equal(3, row.Copies);
        Assert.True(row.IsSelected);
        Assert.Equal(3, _main.Data.SelectedLabelCount);
        _ = print;
    }

    [Fact]
    public async Task QuickPrint_InvalidBarcodeForProduct_BlocksPrint()
    {
        File.WriteAllText(Path.Combine(_folder, "hatali.csv"), "Kod;UrunAdi;Barkod\nX1;Hatalı;8691234567891\n", new UTF8Encoding(true));
        var products = new ProductsViewModel(_main.Products, _dialogs);
        await products.ImportFileAsync(Path.Combine(_folder, "hatali.csv"), keyColumn: null);
        string template = WriteTemplate();
        _main.Templates.Refresh();
        var quick = new QuickPrintViewModel(_main, new ProductsViewModel(_main.Products, _dialogs));
        quick.SelectedTemplate = quick.Templates.Single(t => t.Info.FilePath == template);
        quick.ScanInput = "X1";
        quick.ScanCommand.Execute(null);
        Assert.NotNull(quick.Problem);
        Assert.False(quick.PrintCommand.CanExecute(null));
    }

    [Fact]
    public async Task QuickPrint_UnknownAndPartialScans_ClearPreviousSelection()
    {
        var products = new ProductsViewModel(_main.Products, _dialogs);
        await products.ImportFileAsync(WriteCsv(), null);
        string template = WriteTemplate();
        var quick = new QuickPrintViewModel(_main, new ProductsViewModel(_main.Products, _dialogs));
        quick.SelectedTemplate = quick.Templates.Single(t => t.Info.FilePath == template);
        quick.ScanInput = "TST001"; quick.ScanCommand.Execute(null);
        Assert.True(quick.PrintCommand.CanExecute(null));
        quick.Copies = 10000; Assert.False(quick.PrintCommand.CanExecute(null));
        quick.Copies = 1;
        quick.ScanInput = "BULUNAMADI";
        Assert.False(quick.PrintCommand.CanExecute(null));
        quick.ScanCommand.Execute(null);
        Assert.Null(quick.Products.SelectedProduct);
        Assert.NotNull(quick.ScanMessage);
        Assert.False(quick.PrintCommand.CanExecute(null));
        quick.ScanInput = "TST01"; quick.ScanCommand.Execute(null);
        Assert.Null(quick.Products.SelectedProduct); // Kısmi kodla tek sonuç bile otomatik seçilmez.
        Assert.False(quick.PrintCommand.CanExecute(null));
        quick.Products.SelectedProduct = Assert.Single(quick.Products.Results);
        Assert.Equal("", quick.ScanInput);
        Assert.True(quick.PrintCommand.CanExecute(null));
    }

    [Fact]
    public async Task Products_FilterCannotLeaveInvisibleProductSelected()
    {
        var products = new ProductsViewModel(_main.Products, _dialogs);
        await products.ImportFileAsync(WriteCsv(), null);
        products.SelectedProduct = products.Results.First();
        products.Query = "BULUNAMADI";
        Assert.Empty(products.Results);
        Assert.Null(products.SelectedProduct);
    }

    [Fact]
    public void QuickPrint_DuplicateCodeOutsideVisibleResultsDoesNotSelectFirstMatch()
    {
        string[] columns = ["Kod", "Barkod"];
        var rows = new List<AkelEtiket.DataImport.DataRecord> { new(2, ["A", "12345678"], columns) };
        for (int i = 0; i < 300; i++) rows.Add(new(i + 3, ["B" + i.ToString("000"), "123456789"], columns));
        rows.Add(new(303, ["Z", "12345678"], columns));
        _main.Products.Import(new("demo.csv", "CSV", null, 1, columns, rows, []), "Kod");
        var products = new ProductsViewModel(_main.Products, _dialogs);
        var quick = new QuickPrintViewModel(_main, products) { ScanInput = "12345678" };
        quick.ScanCommand.Execute(null);
        Assert.Equal(ProductsViewModel.MaxShown, products.Results.Count);
        Assert.DoesNotContain(products.Results, r => r.Key == "Z");
        Assert.Null(products.SelectedProduct);
        Assert.Contains("aynı koda", quick.ScanMessage);
    }

    [Fact]
    public void QuickPrint_UniqueExactCodeOutsideLimitIsAddedToVisibleSelection()
    {
        string[] columns = ["Kod", "Barkod"];
        var rows = Enumerable.Range(0, 300).Select(i => new AkelEtiket.DataImport.DataRecord(i + 2,
            ["B" + i.ToString("000"), "123456789"], columns)).ToList();
        rows.Add(new(302, ["Z", "12345678"], columns));
        _main.Products.Import(new("demo.csv", "CSV", null, 1, columns, rows, []), "Kod");
        var products = new ProductsViewModel(_main.Products, _dialogs);
        var quick = new QuickPrintViewModel(_main, products) { ScanInput = "12345678" };
        quick.ScanCommand.Execute(null);
        Assert.Equal("Z", products.SelectedProduct?.Key);
        Assert.Contains(products.SelectedProduct!, products.Results);
        Assert.Equal(ProductsViewModel.MaxShown, products.Results.Count);
    }

    [Fact]
    public void QuickPrint_PriceValueIsNotAProductCode()
    {
        string[] columns = ["Kod", "Fiyat"];
        _main.Products.Import(new("demo.csv", "CSV", null, 1, columns, [new(2, ["P1", "12345678"], columns)], []), "Kod");
        var quick = new QuickPrintViewModel(_main, new ProductsViewModel(_main.Products, _dialogs)) { ScanInput = "12345678" };
        quick.ScanCommand.Execute(null);
        Assert.Null(quick.Products.SelectedProduct);
    }

    [Fact]
    public void QuickPrint_BuiltInOnlyTemplateDoesNotRequireProduct()
    {
        var project = LabelProject.CreateBlank(100, 50);
        project.Document.Elements.Add(new TextElement { Text = "{TARIH} {SIRA}", X = 2, Y = 2, Width = 90, Height = 10 });
        string path = _main.Templates.Library.SaveAsTemplate(project, "Tarih", null, false);
        var quick = new QuickPrintViewModel(_main, new ProductsViewModel(_main.Products, _dialogs));
        quick.SelectedTemplate = quick.Templates.Single(t => t.Info.FilePath == path);
        Assert.False(quick.RequiresProduct);
        Assert.Null(quick.Problem);
        Assert.True(quick.PrintCommand.CanExecute(null));
    }

    [Fact]
    public void QuickPrint_StaticInvalidBarcodeBlocksPrint()
    {
        var project = LabelProject.CreateBlank(100, 50);
        project.Document.Elements.Add(new BarcodeElement { Content = "8691234567891", Symbology = BarcodeType.Ean13, X = 5, Y = 5, Width = 70, Height = 30 });
        string path = _main.Templates.Library.SaveAsTemplate(project, "Hatalı sabit barkod", null, false);
        var quick = new QuickPrintViewModel(_main, new ProductsViewModel(_main.Products, _dialogs));
        quick.SelectedTemplate = quick.Templates.Single(t => t.Info.FilePath == path);
        Assert.NotNull(quick.Problem);
        Assert.False(quick.PrintCommand.CanExecute(null));
    }

    private sealed class RecordingDialogs : IDialogService
    {
        public List<object> Shown { get; } = [];
        public string? ShowOpenFileDialog(string title, string filter, string? initialDirectory = null) => null;
        public string? ShowSaveFileDialog(string title, string filter, string defaultFileName, string? initialDirectory = null) => null;
        public void ShowMessage(string title, string message, MessageKind kind = MessageKind.Info) { }
        public bool Confirm(string title, string message, string confirmText = "Evet", string cancelText = "Hayır") => true;
        public SaveChangesChoice AskSaveChanges(string documentName) => SaveChangesChoice.Discard;
        public string? Prompt(string title, string message, string defaultValue = "") => null;

        public bool ShowDialog(object viewModel)
        {
            Shown.Add(viewModel);
            return false;   // Baskı penceresi açılır ama onaylanmaz: hiçbir iş yazıcıya gitmez.
        }
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
