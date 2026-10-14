using System.Collections.ObjectModel;
using AkelEtiket.Core.Data;
using AkelEtiket.DataImport;
using AkelEtiket.Design.Data;
using AkelEtiket.Editor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>Ürün listesinde bir satır.</summary>
public sealed class ProductRowViewModel(ProductRecord record, string name, IReadOnlyList<string> extraColumns)
{
    public ProductRecord Record { get; } = record;
    public string Key => Record.Key;
    public string Name { get; } = name;

    /// <summary>Kod ve ad dışındaki ilk birkaç sütun (ör. "Barkod: 869… · Fiyat: 120,00").</summary>
    public string Details { get; } = string.Join(" · ", extraColumns
        .Where(c => record.Fields.TryGetValue(c, out var v) && v.Length > 0)
        .Take(4)
        .Select(c => $"{c}: {record.Fields[c]}"));
}

/// <summary>
/// Ürün listesi (Belgeler\Etiket Tasarım\Ürünler\urunler.json): Excel/CSV'den bir kez aktarılır; ad, kod veya barkodla
/// aranır. Hızlı Baskı ekranı ve yapay zekâ veri araçları aynı listeyi kullanır.
/// </summary>
public sealed partial class ProductsViewModel : ObservableObject
{
    public const int MaxShown = 300;

    private readonly ProductCatalog _catalog;
    private readonly IDialogService _dialogs;

    public ProductsViewModel(ProductCatalog catalog, IDialogService dialogs)
    {
        _catalog = catalog;
        _dialogs = dialogs;
        Refresh();
    }

    public ObservableCollection<ProductRowViewModel> Results { get; } = [];

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    [ObservableProperty]
    public partial ProductRowViewModel? SelectedProduct { get; set; }

    public int Count => _catalog.Data.Products.Count;

    partial void OnQueryChanged(string value) => Refresh();

    /// <summary>Arama sonuçlarını yeniler.</summary>
    public void Refresh()
    {
        var data = _catalog.Data;
        var found = _catalog.Search(Query ?? "", MaxShown);
        var extras = data.Columns.Where(c => !string.Equals(c, data.KeyColumn, StringComparison.OrdinalIgnoreCase)).ToList();
        Results.Clear();
        foreach (var product in found) Results.Add(new ProductRowViewModel(product, _catalog.DisplayName(product), extras));
        Summary = data.Products.Count == 0
            ? "Ürün listesi boş. \"Excel/CSV'den aktar\" ile ürünlerinizi bir kez aktarın."
            : $"{data.Products.Count} ürün · anahtar sütun: {data.KeyColumn} · kaynak: {Path.GetFileName(data.SourceFile)} " +
              $"({data.ImportedAt.LocalDateTime:dd.MM.yyyy HH:mm})" +
              (string.IsNullOrWhiteSpace(Query) ? "" : $" · {Results.Count} sonuç{(Results.Count >= MaxShown ? "+" : "")}");
        OnPropertyChanged(nameof(Count));
    }

    /// <summary>Excel/CSV dosyasından listeyi aktarır (var olan liste onayla değiştirilir).</summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        string? path = _dialogs.ShowOpenFileDialog("Ürün listesi (Excel/CSV)", DataImporter.OpenFileFilter);
        if (path is null) return;
        await ImportFileAsync(path, keyColumn: null);
    }

    /// <summary>Dosyayı içe aktarır; anahtar sütun verilmezse tahmin edilir (StokKodu, Kod, Barkod…).</summary>
    public async Task<bool> ImportFileAsync(string path, string? keyColumn)
    {
        if (_catalog.Data.Products.Count > 0 &&
            !_dialogs.Confirm("Ürün listesi", $"Mevcut {_catalog.Data.Products.Count} ürünlük liste, seçilen dosyadakilerle değiştirilsin mi?", "Değiştir", "Vazgeç"))
            return false;
        try
        {
            var table = await DataImporter.ImportAsync(path);
            if (table.Rows.Count == 0)
            {
                _dialogs.ShowMessage("Ürün listesi", "Dosyada ürün satırı bulunamadı.", MessageKind.Warning);
                return false;
            }
            var data = _catalog.Import(table, keyColumn);
            Query = "";
            Refresh();
            int errors = table.Issues.Count(i => i.Severity == ImportIssueSeverity.Error);
            _dialogs.ShowMessage("Ürün listesi", $"{data.Products.Count} ürün aktarıldı (anahtar sütun: {data.KeyColumn})." +
                (errors > 0 ? $"\n{errors} satırda veri uyarısı var; ayrıntılar Veri panelinde dosya açılınca görünür." : ""));
            return true;
        }
        catch (Exception ex) when (ex is DataImportException or IOException or Design.Tools.ToolException)
        {
            _dialogs.ShowMessage("Ürün listesi", "Aktarılamadı: " + ex.Message, MessageKind.Error);
            return false;
        }
    }

    /// <summary>Bir ürünün alan değerleri (önizleme/baskı için).</summary>
    public IFieldValueSource FieldsFor(ProductRecord product) => product.ToFieldSource(_catalog.Data.Columns);

    public IReadOnlyList<string> Columns => _catalog.Data.Columns;
}
