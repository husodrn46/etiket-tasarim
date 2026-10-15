using System.Collections.ObjectModel;
using System.Globalization;
using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.DataImport;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AkelEtiket.Editor.ViewModels;

public enum RowStatus
{
    Ok,
    Warning,
    Error,
}

/// <summary>Veri tablosundaki bir satır: yazdırma seçimi, adet ve doğrulama durumu.</summary>
public sealed partial class DataRowViewModel : ObservableObject
{
    public DataRowViewModel(DataRecord record, int index)
    {
        Record = record;
        Index = index;
    }

    public DataRecord Record { get; }

    /// <summary>Tablodaki sıra (0 tabanlı).</summary>
    public int Index { get; }

    /// <summary>Kaynak dosyadaki satır numarası (Excel'de görünen).</summary>
    public int RowNumber => Record.RowNumber;

    public IReadOnlyList<string> Values => Record.Values;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = true;

    private int _copies = 1;

    /// <summary>Bu satırdan basılacak etiket adedi (0–9999).</summary>
    public int Copies
    {
        get => _copies;
        set => SetProperty(ref _copies, Math.Clamp(value, 0, 9999));
    }

    [ObservableProperty]
    public partial RowStatus Status { get; set; }

    [ObservableProperty]
    public partial string? Problem { get; set; }

    public string StatusText => Status switch
    {
        RowStatus.Error => "Hatalı",
        RowStatus.Warning => "Uyarı",
        _ => "Hazır",
    };

    partial void OnStatusChanged(RowStatus value) => OnPropertyChanged(nameof(StatusText));
}

/// <summary>
/// Excel/CSV veri kaynağı: dosya yükleme, sütunlar, satır seçimi, satır bazlı adet, önizleme satırı ve
/// satır bazlı doğrulama (barkod kontrol basamağı, eksik sütun...). Hatalı satırlar diğerlerini engellemez.
/// </summary>
public sealed partial class DataSourceViewModel : ObservableObject
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public ObservableCollection<DataRowViewModel> Rows { get; } = [];
    public ObservableCollection<ImportIssue> Issues { get; } = [];
    public ObservableCollection<string> SheetNames { get; } = [];

    public IReadOnlyList<EncodingChoice> EncodingChoices => ImportChoices.Encodings;
    public IReadOnlyList<DelimiterChoice> DelimiterChoices => ImportChoices.Delimiters;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasData), nameof(Columns), nameof(Summary), nameof(IsExcel), nameof(IsCsv), nameof(FileName))]
    public partial DataSourceTable? Table { get; private set; }

    [ObservableProperty]
    public partial string? SelectedSheet { get; set; }

    [ObservableProperty]
    public partial EncodingChoice? SelectedEncoding { get; set; }

    [ObservableProperty]
    public partial DelimiterChoice? SelectedDelimiter { get; set; }

    /// <summary>Önizlenen satırın tablo içindeki sırası.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewRow), nameof(PreviewText))]
    public partial int PreviewIndex { get; set; } = -1;

    /// <summary>Adetlerin okunacağı sütun (boş = elle girilir).</summary>
    [ObservableProperty]
    public partial string? CopiesColumn { get; set; }

    [ObservableProperty]
    public partial string ValidationSummary { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    public bool HasData => Table is { Rows.Count: > 0 };
    public bool IsExcel => Table?.SourceKind == DataSourceTable.KindExcel;
    public bool IsCsv => Table?.SourceKind == DataSourceTable.KindCsv;
    public string? FileName => Table?.FileName;
    public IReadOnlyList<string> Columns => Table?.Columns ?? [];

    public string Summary => Table is null
        ? "Veri kaynağı yüklenmedi."
        : $"{Table.FileName} • {Table.Rows.Count} satır • {Table.Columns.Count} sütun" +
          (Table.ErrorCount > 0 ? $" • {Table.ErrorCount} hata" : "") +
          (Table.WarningCount > 0 ? $" • {Table.WarningCount} uyarı" : "");

    public DataRowViewModel? PreviewRow => PreviewIndex >= 0 && PreviewIndex < Rows.Count ? Rows[PreviewIndex] : null;

    public string PreviewText => PreviewRow is null ? "—" : $"{PreviewIndex + 1} / {Rows.Count} (satır {PreviewRow.RowNumber})";

    public int SelectedLabelCount => Rows.Where(r => r.IsSelected && r.Status != RowStatus.Error).Sum(r => r.Copies);

    /// <summary>Önizleme satırı veya tablo değişti.</summary>
    public event EventHandler? PreviewChanged;

    /// <summary>Tablo yüklendi/temizlendi.</summary>
    public event EventHandler? TableChanged;
    public event EventHandler? MappingRequested;
    [RelayCommand] private void MapColumns() => MappingRequested?.Invoke(this, EventArgs.Empty);

    private bool _suppressReload;

    /// <summary>Dosyayı yükler (arka planda okur). Hata olursa Türkçe açıklamalı istisna fırlatır.</summary>
    public async Task LoadAsync(string path, ImportOptions? options = null)
    {
        IsLoading = true;
        try
        {
            options ??= new ImportOptions();
            var table = await DataImporter.ImportAsync(path, options);
            IReadOnlyList<string> sheets = DataImporter.IsExcelFile(path) ? DataImporter.GetSheetNames(path) : [];
            ApplyTable(table, sheets);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Yüklü tabloyu dışarıdan uygular (testler ve senkron kullanım için).</summary>
    public void ApplyTable(DataSourceTable table, IReadOnlyList<string>? sheetNames = null)
    {
        _suppressReload = true;
        try
        {
            Table = table;
            SheetNames.Clear();
            foreach (var sheet in sheetNames ?? []) SheetNames.Add(sheet);
            SelectedSheet = table.SheetName;
            SelectedEncoding = EncodingChoices.FirstOrDefault(c => c.Encoding is null) ?? EncodingChoices.FirstOrDefault();
            SelectedDelimiter = DelimiterChoices.FirstOrDefault(c => c.Delimiter is null) ?? DelimiterChoices.FirstOrDefault();
            Rows.Clear();
            int index = 0;
            foreach (var record in table.Rows) Rows.Add(new DataRowViewModel(record, index++));
            foreach (var row in Rows) row.PropertyChanged += OnRowPropertyChanged;
            Issues.Clear();
            foreach (var issue in table.Issues) Issues.Add(issue);
            CopiesColumn = table.Columns.FirstOrDefault(c => FieldNames.Normalize(c) is "adet" or "miktar" or "kopya" or "etiketadedi" or "etiketsayisi");
            PreviewIndex = Rows.Count > 0 ? 0 : -1;
        }
        finally
        {
            _suppressReload = false;
        }
        // PreviewIndex zaten 0 ise değişiklik bildirimi gelmez; önizleme metnini her durumda yenile.
        OnPropertyChanged(nameof(PreviewRow));
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(SelectedLabelCount));
        ResetFilters();
        TableChanged?.Invoke(this, EventArgs.Empty);
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void Clear()
    {
        Table = null;
        Rows.Clear();
        Issues.Clear();
        SheetNames.Clear();
        PreviewIndex = -1;
        ValidationSummary = "";
        OnPropertyChanged(nameof(SelectedLabelCount));
        ResetFilters();
        TableChanged?.Invoke(this, EventArgs.Empty);
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Seçilen sayfa/kodlama/ayırıcı ile dosyayı yeniden okur.</summary>
    public async Task ReloadAsync()
    {
        if (Table is null) return;
        var options = new ImportOptions
        {
            SheetName = IsExcel ? SelectedSheet : null,
            Encoding = IsCsv ? SelectedEncoding?.Encoding : null,
            Delimiter = IsCsv ? SelectedDelimiter?.Delimiter : null,
        };
        await LoadAsync(Table.SourcePath, options);
    }

    partial void OnSelectedSheetChanged(string? value)
    {
        if (!_suppressReload && Table is not null && IsExcel && value != Table.SheetName) _ = SafeReloadAsync();
    }

    partial void OnSelectedEncodingChanged(EncodingChoice? value)
    {
        if (!_suppressReload && Table is not null && IsCsv) _ = SafeReloadAsync();
    }

    partial void OnSelectedDelimiterChanged(DelimiterChoice? value)
    {
        if (!_suppressReload && Table is not null && IsCsv) _ = SafeReloadAsync();
    }

    /// <summary>Yeniden yükleme hatası (arayüzde gösterilir).</summary>
    public event EventHandler<string>? ReloadFailed;

    private async Task SafeReloadAsync()
    {
        try
        {
            await ReloadAsync();
        }
        catch (DataImportException ex)
        {
            ReloadFailed?.Invoke(this, ex.Message);
        }
    }

    partial void OnPreviewIndexChanged(int value) => PreviewChanged?.Invoke(this, EventArgs.Empty);

    partial void OnCopiesColumnChanged(string? value) => ApplyCopiesFromColumn();

    [RelayCommand]
    public void NextPreview()
    {
        if (Rows.Count > 0) PreviewIndex = (PreviewIndex + 1) % Rows.Count;
    }

    [RelayCommand]
    public void PreviousPreview()
    {
        if (Rows.Count > 0) PreviewIndex = (PreviewIndex - 1 + Rows.Count) % Rows.Count;
    }

    [RelayCommand]
    public void SelectAllRows() => SetSelection(Rows, true);

    [RelayCommand]
    public void SelectNoRows() => SetSelection(Rows, false);

    [RelayCommand]
    public void SelectValidRows()
    {
        using (BeginBulkSelection())
        {
            foreach (var row in Rows) row.IsSelected = row.Status != RowStatus.Error;
        }
        if (OnlySelectedRows) RefreshVisibleRows();
    }

    /// <summary>Adet sütunu seçiliyse her satırın adedini oradan okur (geçersiz/boş → 1).</summary>
    public void ApplyCopiesFromColumn()
    {
        if (Table is null || string.IsNullOrEmpty(CopiesColumn)) return;
        int column = Table.IndexOfColumn(CopiesColumn);
        if (column < 0) return;
        using (BeginBulkSelection())
        {
            foreach (var row in Rows)
            {
                string raw = row.Record.GetValue(column).Trim();
                row.Copies = int.TryParse(raw, NumberStyles.Integer, Turkish, out int n) || int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)
                    ? Math.Clamp(n, 0, 9999)
                    : 1;
            }
        }
    }

    /// <summary>Önizleme satırının alan kaynağı (yerleşik alanlarla birlikte).</summary>
    public IFieldValueSource? GetPreviewFields(DateTime now)
    {
        var builtIns = Printing.EditorFields.BuiltIns(Math.Max(1, PreviewIndex + 1), now);
        if (Table is null || PreviewRow is null) return builtIns;
        return new CompositeFieldSource(PreviewRow.Record.ToFieldSource(Table.Columns), builtIns);
    }

    /// <summary>Öğenin başvurduğu alanlar: (yazıldığı gibi, temel ad). Görsel sütunu ve koşul alanı dahil.</summary>
    internal static IEnumerable<(string Raw, string Base)> FieldReferencesOf(LabelElement element)
    {
        IEnumerable<FieldToken> tokens = element switch
        {
            TextElement t => FieldTemplate.GetTokens(t.Text),
            BarcodeElement b => FieldTemplate.GetTokens(b.Content),
            _ => [],
        };
        foreach (var token in tokens) yield return (token.Name, token.BaseName);
        if (element is ImageElement { SourceField: { } field }) yield return (field, field);
        if (element.VisibleWhen is { } condition) yield return (condition.Field, condition.Field);
    }

    /// <summary>
    /// Tüm satırları belgeye göre doğrular: tabloda olmayan alanlar, geçersiz barkod içerikleri,
    /// içe aktarma hataları. Sonuç satır durumlarına ve özet metne yazılır.
    /// </summary>
    public void ValidateAgainst(LabelDocument document)
    {
        if (Table is null)
        {
            ValidationSummary = "";
            return;
        }
        var columns = Table.Columns;
        var builtInProbe = Printing.EditorFields.BuiltIns(1, DateTime.Now);
        bool Known(string name) => FieldNames.FindIndex(columns, name) >= 0 || builtInProbe.TryGetValue(name, null, out _);
        // {TARIH+30} gibi ifadelerde hem yazıldığı gibi hem de temel ad denenir; görsel ve koşul alanları da sayılır.
        var missingColumns = document.Elements
            .SelectMany(FieldReferencesOf)
            .Where(r => !Known(r.Raw) && !Known(r.Base))
            .Select(r => r.Raw)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var barcodes = document.Elements.OfType<BarcodeElement>().Where(b => b.HasFieldPlaceholders && b.Visible).ToList();
        var dateTexts = document.Elements.OfType<TextElement>()
            .Where(t => t.Visible && FieldTemplate.GetTokens(t.Text).Any(k => k.Expression.Offset is not null))
            .ToList();

        int errors = 0, warnings = 0;
        foreach (var row in Rows)
        {
            var problems = new List<string>();
            var status = RowStatus.Ok;
            foreach (var issue in Table.GetIssuesForRow(row.RowNumber))
            {
                if (issue.Severity == ImportIssueSeverity.Error) status = RowStatus.Error;
                else if (issue.Severity == ImportIssueSeverity.Warning && status == RowStatus.Ok) status = RowStatus.Warning;
                if (issue.Severity != ImportIssueSeverity.Info) problems.Add(issue.Message);
            }
            if (missingColumns.Count > 0)
            {
                status = RowStatus.Error;
                problems.Add("Veride olmayan alan: " + string.Join(", ", missingColumns.Select(FieldTemplate.MakePlaceholder)));
            }
            if (barcodes.Count > 0 || dateTexts.Count > 0)
            {
                var fields = new CompositeFieldSource(row.Record.ToFieldSource(columns), Printing.EditorFields.BuiltIns(row.Index + 1, DateTime.Now));
                foreach (var text in dateTexts)
                {
                    if (VisibilityEvaluator.Evaluate(text.VisibleWhen, fields) == ConditionResult.Hide) continue;
                    var resolved = FieldTemplate.Resolve(text.Text, fields);
                    if (resolved.Problems.Count == 0) continue;
                    status = RowStatus.Error;
                    problems.AddRange(resolved.Problems);
                }
                foreach (var barcode in barcodes)
                {
                    // Koşul nedeniyle bu satırda basılmayacak barkod denetlenmez.
                    if (VisibilityEvaluator.Evaluate(barcode.VisibleWhen, fields) == ConditionResult.Hide) continue;
                    var resolved = FieldTemplate.Resolve(barcode.Content, fields);
                    if (resolved.Problems.Count > 0)
                    {
                        status = RowStatus.Error;
                        problems.AddRange(resolved.Problems);
                        continue;
                    }
                    if (!resolved.IsComplete) continue; // eksik sütun yukarıda raporlandı
                    var validation = BarcodeValidator.Validate(barcode.Symbology, resolved.Text, BarcodeSettings.From(barcode));
                    if (!validation.IsValid)
                    {
                        status = RowStatus.Error;
                        problems.Add($"{BarcodeCatalog.GetDisplayName(barcode.Symbology)}: {validation.FirstError}");
                    }
                }
            }
            row.Status = status;
            row.Problem = problems.Count > 0 ? string.Join(" • ", problems.Distinct()) : null;
            if (status == RowStatus.Error) errors++;
            else if (status == RowStatus.Warning) warnings++;
        }
        ValidationSummary = errors == 0 && warnings == 0
            ? $"{Rows.Count} satırın tamamı yazdırmaya hazır."
            : $"{Rows.Count - errors} satır yazdırılabilir, {errors} satır hatalı" + (warnings > 0 ? $", {warnings} satırda uyarı var." : ".");
        OnPropertyChanged(nameof(SelectedLabelCount));
        RefreshVisibleRows(); // durum ve sorun metni değişti: "yalnızca hatalı" ve arama sonucu güncellenir
    }
}
