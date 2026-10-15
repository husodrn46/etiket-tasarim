using System.Collections.ObjectModel;
using AkelEtiket.Core.Data;
using AkelEtiket.DataImport;
using AkelEtiket.Design.Data;
using AkelEtiket.Editor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

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

    public IReadOnlyList<ProductRowViewModel> FindScanMatches(string code)
    {
        var extras = _catalog.Data.Columns.Where(c => c != _catalog.Data.KeyColumn).ToList();
        return _catalog.FindScanMatches(code).Select(p => Results.FirstOrDefault(r => r.Key == p.Key)
            ?? new ProductRowViewModel(p, _catalog.DisplayName(p), extras)).ToList();
    }

    public int Count => _catalog.Data.Products.Count;

    partial void OnQueryChanged(string value) => Refresh();

    /// <summary>Arama sonuçlarını yeniler.</summary>
    public void Refresh()
    {
        var data = _catalog.Data;
        var found = _catalog.Search(Query ?? "", MaxShown);
        var extras = data.Columns.Where(c => !string.Equals(c, data.KeyColumn, StringComparison.OrdinalIgnoreCase)).ToList();
        string? selectedKey = SelectedProduct?.Key;
        SelectedProduct = null;
        Results.Clear();
        foreach (var product in found) Results.Add(new ProductRowViewModel(product, _catalog.DisplayName(product), extras));
        SelectedProduct = Results.FirstOrDefault(p => p.Key == selectedKey);
        Summary = data.Products.Count == 0
            ? L10n.T("Ürün listesi boş. \"Excel/CSV'den aktar\" ile ürünlerinizi bir kez aktarın.")
            : L10n.T($"{data.Products.Count} ürün · anahtar sütun: {data.KeyColumn} · kaynak: {Path.GetFileName(data.SourceFile)} ") +
              $"({data.ImportedAt.LocalDateTime:dd.MM.yyyy HH:mm})" +
              (string.IsNullOrWhiteSpace(Query) ? "" : L10n.T($" · {Results.Count} sonuç{(Results.Count >= MaxShown ? "+" : "")}"));
        OnPropertyChanged(nameof(Count));
    }

    /// <summary>Excel/CSV dosyasından listeyi aktarır (var olan liste onayla değiştirilir).</summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        string? path = _dialogs.ShowOpenFileDialog(L10n.T("Ürün listesi (Excel/CSV)"), DataImporter.OpenFileFilter);
        if (path is null) return;
        await ImportFileAsync(path, keyColumn: null);
    }

    /// <summary>Dosyayı içe aktarır; anahtar sütun verilmezse tahmin edilir (StokKodu, Kod, Barkod…).</summary>
    public async Task<bool> ImportFileAsync(string path, string? keyColumn)
    {
        if (_catalog.Data.Products.Count > 0 &&
            !_dialogs.Confirm(L10n.T("Ürün listesi"), L10n.T($"Mevcut {_catalog.Data.Products.Count} ürünlük liste, seçilen dosyadakilerle değiştirilsin mi?"), L10n.T("Değiştir"), L10n.T("Vazgeç")))
            return false;
        try
        {
            var table = await DataImporter.ImportAsync(path);
            if (table.Rows.Count == 0)
            {
                _dialogs.ShowMessage(L10n.T("Ürün listesi"), L10n.T("Dosyada ürün satırı bulunamadı."), MessageKind.Warning);
                return false;
            }
            var data = _catalog.Import(table, keyColumn);
            Query = "";
            Refresh();
            int errors = table.Issues.Count(i => i.Severity == ImportIssueSeverity.Error);
            _dialogs.ShowMessage(L10n.T("Ürün listesi"), L10n.T($"{data.Products.Count} ürün aktarıldı (anahtar sütun: {data.KeyColumn}).") +
                (errors > 0 ? L10n.T($"\n{errors} satırda veri uyarısı var; ayrıntılar Veri panelinde dosya açılınca görünür.") : ""));
            return true;
        }
        catch (Exception ex) when (ex is DataImportException or IOException or Design.Tools.ToolException)
        {
            _dialogs.ShowMessage(L10n.T("Ürün listesi"), L10n.T("Aktarılamadı: ") + ex.Message, MessageKind.Error);
            return false;
        }
    }

    /// <summary>Bir ürünün alan değerleri (önizleme/baskı için).</summary>
    public IFieldValueSource FieldsFor(ProductRecord product) => product.ToFieldSource(_catalog.Data.Columns);

    public IReadOnlyList<string> Columns => _catalog.Data.Columns;
}
