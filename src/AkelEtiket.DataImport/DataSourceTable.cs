using AkelEtiket.Core.Data;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.DataImport;

/// <summary>
/// Dosyadaki tek veri satırı. Değerler her zaman metindir (barkodlar asla sayıya çevrilmez,
/// baştaki sıfırlar korunur); değer sayısı tablonun sütun sayısına eşittir.
/// </summary>
public sealed class DataRecord
{
    private readonly IReadOnlyList<string> _columns;
    private readonly string[] _values;

    public DataRecord(int rowNumber, IReadOnlyList<string> values, IReadOnlyList<string> columns)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(columns);
        if (rowNumber < 1) throw new ArgumentOutOfRangeException(nameof(rowNumber), L10n.T("Satır numarası 1 veya daha büyük olmalı."));
        RowNumber = rowNumber;
        _columns = columns;
        _values = new string[columns.Count];
        for (int i = 0; i < _values.Length; i++)
            _values[i] = i < values.Count ? values[i] ?? "" : "";
    }

    /// <summary>Kaynak dosyadaki gerçek satır numarası (Excel satırı / CSV'de kaydın başladığı fiziksel satır).</summary>
    public int RowNumber { get; }

    /// <summary>Sütun sırasıyla değerler.</summary>
    public IReadOnlyList<string> Values => _values;

    public string GetValue(int columnIndex) =>
        (uint)columnIndex < (uint)_values.Length ? _values[columnIndex] : "";

    /// <summary>Sütun adıyla değer (Türkçe karakter/büyük-küçük harf duyarsız eşleşme); yoksa boş metin.</summary>
    public string GetValue(string columnName)
    {
        int index = FieldNames.FindIndex(_columns, columnName);
        return index < 0 ? "" : _values[index];
    }

    /// <summary>{Alan} değişkenlerini bu satırın değerleriyle dolduran kaynak.</summary>
    public RecordFieldSource ToFieldSource() => new(_columns, _values);

    /// <summary>{Alan} değişkenlerini verilen sütun adlarıyla eşleştirerek dolduran kaynak.</summary>
    public RecordFieldSource ToFieldSource(IReadOnlyList<string> columns) => new(columns, _values);

    public override string ToString() => L10n.T($"Satır {RowNumber}: {string.Join(" | ", _values)}");
}

/// <summary>Excel veya CSV dosyasından okunmuş tablo.</summary>
public sealed class DataSourceTable
{
    public const string KindExcel = "Excel";
    public const string KindCsv = "CSV";

    public DataSourceTable(
        string sourcePath,
        string sourceKind,
        string? sheetName,
        int headerRowNumber,
        IReadOnlyList<string> columns,
        IReadOnlyList<DataRecord> rows,
        IReadOnlyList<ImportIssue> issues,
        string? encodingName = null,
        char? delimiter = null,
        int skippedEmptyRows = 0,
        bool wasTruncated = false)
    {
        SourcePath = sourcePath ?? "";
        SourceKind = sourceKind ?? "";
        SheetName = sheetName;
        HeaderRowNumber = headerRowNumber;
        Columns = columns ?? throw new ArgumentNullException(nameof(columns));
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
        Issues = issues ?? [];
        EncodingName = encodingName;
        Delimiter = delimiter;
        SkippedEmptyRows = skippedEmptyRows;
        WasTruncated = wasTruncated;
    }

    public string SourcePath { get; }

    public string FileName => Path.GetFileName(SourcePath);

    /// <summary><see cref="KindExcel"/> veya <see cref="KindCsv"/>.</summary>
    public string SourceKind { get; }

    /// <summary>Excel sayfa adı (CSV için null).</summary>
    public string? SheetName { get; }

    /// <summary>Başlık satırının dosyadaki numarası.</summary>
    public int HeaderRowNumber { get; }

    /// <summary>Benzersiz ve değişken alanlarda kullanılabilir sütun adları.</summary>
    public IReadOnlyList<string> Columns { get; }

    public IReadOnlyList<DataRecord> Rows { get; }

    /// <summary>Satır numarasına göre sıralı sorunlar.</summary>
    public IReadOnlyList<ImportIssue> Issues { get; }

    /// <summary>CSV kodlaması ("UTF-8", "Windows-1254 (Türkçe)" …); Excel için null.</summary>
    public string? EncodingName { get; }

    /// <summary>CSV ayırıcısı; Excel için null.</summary>
    public char? Delimiter { get; }

    /// <summary>Atlanan tamamen boş satır sayısı.</summary>
    public int SkippedEmptyRows { get; }

    /// <summary><see cref="ImportOptions.MaxRows"/> sınırı nedeniyle kalan satırlar okunmadı.</summary>
    public bool WasTruncated { get; }

    public int ErrorCount => Issues.Count(i => i.Severity == ImportIssueSeverity.Error);

    public int WarningCount => Issues.Count(i => i.Severity == ImportIssueSeverity.Warning);

    /// <summary>Sütun adını bulur (Türkçe karakter/büyük-küçük harf duyarsız); yoksa -1.</summary>
    public int IndexOfColumn(string name) => FieldNames.FindIndex(Columns, name);

    public IEnumerable<ImportIssue> GetIssuesForRow(int rowNumber) => Issues.Where(i => i.RowNumber == rowNumber);

    /// <summary>"120 satır, 5 sütun okundu. 1 hata, 2 uyarı." gibi özet.</summary>
    public string Summary
    {
        get
        {
            string text = L10n.T($"{Rows.Count} satır, {Columns.Count} sütun okundu.");
            int errors = ErrorCount, warnings = WarningCount;
            if (errors > 0 || warnings > 0)
                text += L10n.T($" {errors} hata, {warnings} uyarı.");
            if (WasTruncated)
                text += L10n.T(" Satır sınırı nedeniyle dosyanın tamamı okunmadı.");
            return text;
        }
    }

    public override string ToString() => $"{FileName} ({SourceKind}): {Summary}";
}
