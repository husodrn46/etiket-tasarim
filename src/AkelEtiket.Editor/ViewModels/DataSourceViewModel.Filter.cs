using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

public sealed partial class DataRowViewModel
{
    private string? _searchKey;

    /// <summary>Arama için katlanmış hücre metinleri; ilk aramada bir kez hesaplanır (değerler değişmez).</summary>
    internal string SearchKey => _searchKey ??= DataSourceViewModel.SearchFold(string.Join('\u001F', Values));
}

/// <summary>
/// Veri tablosunda arama, süzme ve satır numarasıyla seçim. Süzgeç yalnızca gösterimi etkiler:
/// baskı her zaman tüm satırların seçimine bakar. Toplu seçimde özet tek kez yenilenir (10 000+ satırda hızlı).
/// </summary>
public sealed partial class DataSourceViewModel
{
    private int _bulkSelection;
    private bool _suppressFilter;
    private List<DataRowViewModel>? _visibleRows;

    /// <summary>Tabloda gösterilen satırlar (süzgeç yoksa <see cref="Rows"/>).</summary>
    public IReadOnlyList<DataRowViewModel> VisibleRows => _visibleRows ?? (IReadOnlyList<DataRowViewModel>)Rows;

    /// <summary>Tüm hücrelerde ve sorun metninde arar (Türkçe harf ve büyük/küçük harf duyarsız; sözcüklerin hepsi aranır).</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial bool OnlyErrorRows { get; set; }

    [ObservableProperty]
    public partial bool OnlySelectedRows { get; set; }

    /// <summary>"2-20, 35, 40-42" gibi satır numaraları (Excel/CSV'deki numaralar).</summary>
    [ObservableProperty]
    public partial string RowRangeText { get; set; } = "";

    [ObservableProperty]
    public partial string? RangeMessage { get; private set; }

    [ObservableProperty]
    public partial bool RangeMessageIsError { get; private set; }

    public bool IsFiltered => _visibleRows is not null;

    public string VisibleSummary => Rows.Count == 0 ? ""
        : IsFiltered ? L10n.T($"{VisibleRows.Count.ToString("N0", Turkish)} / {Rows.Count.ToString("N0", Turkish)} satır gösteriliyor")
        : L10n.T($"{Rows.Count.ToString("N0", Turkish)} satır");

    public string SelectVisibleLabel => IsFiltered ? L10n.T($"Görünen {VisibleRows.Count.ToString("N0", Turkish)} satırı seç") : L10n.T("Tümünü seç");

    public string DeselectVisibleLabel => IsFiltered ? L10n.T("Görünenlerin seçimini kaldır") : L10n.T("Hiçbirini seçme");

    partial void OnSearchTextChanged(string value) => RefreshVisibleRows();

    partial void OnOnlyErrorRowsChanged(bool value) => RefreshVisibleRows();

    partial void OnOnlySelectedRowsChanged(bool value) => RefreshVisibleRows();

    /// <summary>Süzgeci yeniden uygular (arama, hata/seçim süzgeci). Sonuç değişmediyse bildirim yapılmaz.</summary>
    public void RefreshVisibleRows()
    {
        if (_suppressFilter) return;
        string[] terms = SearchFold(SearchText ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        List<DataRowViewModel>? next = null;
        if (terms.Length > 0 || OnlyErrorRows || OnlySelectedRows)
        {
            next = new List<DataRowViewModel>();
            foreach (var row in Rows)
                if (Matches(row, terms)) next.Add(row);
        }
        bool same = next is null ? _visibleRows is null : _visibleRows is not null && next.SequenceEqual(_visibleRows);
        if (same) return;
        _visibleRows = next;
        OnPropertyChanged(nameof(VisibleRows));
        OnPropertyChanged(nameof(IsFiltered));
        OnPropertyChanged(nameof(VisibleSummary));
        OnPropertyChanged(nameof(SelectVisibleLabel));
        OnPropertyChanged(nameof(DeselectVisibleLabel));
    }

    private bool Matches(DataRowViewModel row, string[] terms)
    {
        if (OnlyErrorRows && row.Status != RowStatus.Error) return false;
        if (OnlySelectedRows && !row.IsSelected) return false;
        if (terms.Length == 0) return true;
        string key = row.SearchKey;
        string? problem = string.IsNullOrEmpty(row.Problem) ? null : SearchFold(row.Problem);
        foreach (string term in terms)
        {
            if (!key.Contains(term, StringComparison.Ordinal) && (problem is null || !problem.Contains(term, StringComparison.Ordinal)))
                return false;
        }
        return true;
    }

    /// <summary>Arama katlaması: Türkçe küçük harf, ı/ğ/ü/ş/ö/ç ve şapkalı harfler sadeleşir; diğer karakterler korunur.</summary>
    internal static string SearchFold(string text)
    {
        string lower = text.ToLower(Turkish);
        var sb = new StringBuilder(lower.Length);
        foreach (char ch in lower)
        {
            sb.Append(ch switch
            {
                'ı' => 'i',
                'ğ' => 'g',
                'ü' => 'u',
                'ş' => 's',
                'ö' => 'o',
                'ç' => 'c',
                'â' => 'a',
                'î' => 'i',
                'û' => 'u',
                _ => ch,
            });
        }
        return sb.ToString();
    }

    /// <summary>Görünen (süzülmüş) satırların hepsini seçer.</summary>
    [RelayCommand]
    public void SelectVisibleRows() => SetSelection(VisibleRows, true);

    /// <summary>Görünen (süzülmüş) satırların seçimini kaldırır.</summary>
    [RelayCommand]
    public void DeselectVisibleRows() => SetSelection(VisibleRows, false);

    private void SetSelection(IReadOnlyList<DataRowViewModel> rows, bool selected)
    {
        using (BeginBulkSelection())
        {
            foreach (var row in rows) row.IsSelected = selected;
        }
        if (OnlySelectedRows) RefreshVisibleRows();
    }

    /// <summary>
    /// Yazılan satır numaralarını seçer, diğerlerinin seçimini kaldırır (numaralar Excel/CSV'deki satırlardır;
    /// süzgeçten bağımsızdır). Geçersiz yazımda ya da hiçbir satır eşleşmezse seçim değişmez.
    /// </summary>
    [RelayCommand]
    public void SelectRowRange()
    {
        if (!RowRangeSet.TryParse(RowRangeText, out var set, out string? error))
        {
            SetRangeMessage(error, isError: true);
            return;
        }
        if (Rows.Count == 0)
        {
            SetRangeMessage(L10n.T("Önce bir veri dosyası yükleyin."), isError: true);
            return;
        }
        int[] numbers = Rows.Select(r => r.RowNumber).Order().ToArray();
        var unmatched = set!.Ranges.Where(r => !AnyRowIn(numbers, r)).Select(DescribeRange).Distinct().ToList();
        int matched = Rows.Count(r => set.Contains(r.RowNumber));
        if (matched == 0)
        {
            SetRangeMessage(L10n.T($"Bu numaralarda veri satırı yok ({string.Join(", ", unmatched)}). Dosyadaki veri satırları ") +
                            L10n.T($"{numbers[0]}–{numbers[^1]} arasında; seçim değiştirilmedi."), isError: true);
            return;
        }
        int errorRows = 0;
        using (BeginBulkSelection())
        {
            foreach (var row in Rows)
            {
                bool selected = set.Contains(row.RowNumber);
                row.IsSelected = selected;
                if (selected && row.Status == RowStatus.Error) errorRows++;
            }
        }
        var message = new StringBuilder(L10n.T($"{matched.ToString("N0", Turkish)} satır seçildi"));
        if (errorRows > 0) message.Append(L10n.T($"; {errorRows.ToString("N0", Turkish)} tanesi hatalı olduğu için basılmaz"));
        message.Append('.');
        if (unmatched.Count > 0) message.Append(L10n.T(" Dosyada veri satırı olmayan: ")).Append(string.Join(", ", unmatched)).Append('.');
        SetRangeMessage(message.ToString(), isError: false);
        if (OnlySelectedRows) RefreshVisibleRows();
    }

    private string DescribeRange(RowRange range) =>
        range.Start == range.End && Table is not null && range.Start == Table.HeaderRowNumber
            ? L10n.T($"{range} (başlık satırı)")
            : range.ToString();

    private static bool AnyRowIn(int[] sortedNumbers, RowRange range)
    {
        int index = Array.BinarySearch(sortedNumbers, range.Start);
        if (index < 0) index = ~index;
        return index < sortedNumbers.Length && sortedNumbers[index] <= range.End;
    }

    private void SetRangeMessage(string? message, bool isError)
    {
        RangeMessageIsError = isError;
        RangeMessage = message;
    }

    /// <summary>Arama/süzgeçleri temizler (yeni tablo yüklenince).</summary>
    private void ResetFilters()
    {
        _suppressFilter = true;
        try
        {
            SearchText = "";
            OnlyErrorRows = false;
            OnlySelectedRows = false;
            SetRangeMessage(null, isError: false);
        }
        finally
        {
            _suppressFilter = false;
        }
        _visibleRows = null;
        OnPropertyChanged(nameof(VisibleRows));
        OnPropertyChanged(nameof(IsFiltered));
        OnPropertyChanged(nameof(VisibleSummary));
        OnPropertyChanged(nameof(SelectVisibleLabel));
        OnPropertyChanged(nameof(DeselectVisibleLabel));
    }

    /// <summary>Restore an explicit selection in one notification, without quadratic summary refreshes.</summary>
    public void ApplyRowSelection(IReadOnlyDictionary<int, int> copiesByRow)
    {
        ArgumentNullException.ThrowIfNull(copiesByRow);
        if (copiesByRow.Any(p => p.Key < 1 || p.Value is < 1 or > 9999)) throw new ArgumentOutOfRangeException(nameof(copiesByRow));
        using (BeginBulkSelection())
        {
            foreach (var row in Rows)
            {
                row.IsSelected = copiesByRow.TryGetValue(row.RowNumber, out int count);
                row.Copies = row.IsSelected ? count : 1;
            }
        }
        if (OnlySelectedRows) RefreshVisibleRows();
    }

    /// <summary>Toplu seçim: satır başına özet yenilenmez, kapsam bitince bir kez yenilenir.</summary>
    private BulkSelectionScope BeginBulkSelection()
    {
        _bulkSelection++;
        return new BulkSelectionScope(this);
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_bulkSelection == 0 && e.PropertyName is nameof(DataRowViewModel.IsSelected) or nameof(DataRowViewModel.Copies))
            OnPropertyChanged(nameof(SelectedLabelCount));
    }

    private sealed class BulkSelectionScope(DataSourceViewModel owner) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            if (--owner._bulkSelection == 0) owner.OnPropertyChanged(nameof(SelectedLabelCount));
        }
    }
}
