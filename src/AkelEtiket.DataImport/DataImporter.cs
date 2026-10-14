using AkelEtiket.DataImport.Csv;
using AkelEtiket.DataImport.Excel;

namespace AkelEtiket.DataImport;

/// <summary>
/// Excel (XLSX/XLSM, eski XLS) ve CSV dosyalarından toplu etiket verisi okur.
/// Hatalı/şüpheli değerler <see cref="DataSourceTable.Issues"/> içinde satır numarasıyla raporlanır;
/// dosya hiç okunamazsa Türkçe açıklamalı <see cref="DataImportException"/> fırlatılır.
/// </summary>
public static class DataImporter
{
    private static readonly string[] ExcelExtensions = [".xlsx", ".xlsm", ".xls"];
    private static readonly string[] CsvExtensions = [".csv", ".txt", ".tsv"];

    /// <summary>Desteklenen uzantılar (küçük harf, noktalı).</summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } = [.. ExcelExtensions, .. CsvExtensions];

    /// <summary>Dosya açma penceresi filtresi.</summary>
    public static string OpenFileFilter =>
        "Veri dosyaları (*.xlsx; *.xls; *.csv; *.txt)|*.xlsx;*.xlsm;*.xls;*.csv;*.txt;*.tsv" +
        "|Excel dosyaları (*.xlsx; *.xlsm; *.xls)|*.xlsx;*.xlsm;*.xls" +
        "|CSV dosyaları (*.csv; *.txt)|*.csv;*.txt;*.tsv" +
        "|Tüm dosyalar (*.*)|*.*";

    public static bool IsSupportedFile(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());

    public static bool IsExcelFile(string path) =>
        ExcelExtensions.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());

    /// <summary>Dosyayı uzantısına göre Excel veya CSV olarak okur.</summary>
    /// <exception cref="DataImportException">Dosya okunamadığında (Türkçe açıklamalı).</exception>
    public static DataSourceTable Import(string path, ImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new DataImportException("Dosya yolu boş.");
        options ??= new ImportOptions();
        if (options.MaxRows < 1)
            throw new DataImportException("En fazla satır sayısı 1 veya daha büyük olmalı.") { FilePath = path };
        if (options.HeaderRow is < 1)
            throw new DataImportException("Başlık satırı 1 veya daha büyük olmalı.") { FilePath = path };

        string extension = Path.GetExtension(path).ToLowerInvariant();
        switch (extension)
        {
            case ".xls":
                return XlsImporter.Import(path, options, cancellationToken);
            case ".xlsb":
                throw new DataImportException(
                    "İkili Excel biçimi (.xlsb) desteklenmiyor. Dosyayı Excel'de .xlsx olarak kaydedin.") { FilePath = path };
            case ".ods":
                throw new DataImportException(
                    "OpenDocument (.ods) biçimi desteklenmiyor. Dosyayı .xlsx veya .csv olarak kaydedin.") { FilePath = path };
        }

        if (ExcelExtensions.Contains(extension))
            return ExcelImporter.Import(path, options, cancellationToken);
        if (CsvExtensions.Contains(extension))
            return CsvImporter.Import(path, options, cancellationToken);

        throw new DataImportException(
            $"\"{extension}\" uzantılı dosyalar desteklenmiyor. Excel (.xlsx, .xls) veya CSV (.csv) dosyası seçin.") { FilePath = path };
    }

    /// <summary>Arayüzü kilitlememek için arka planda okur.</summary>
    public static Task<DataSourceTable> ImportAsync(string path, ImportOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Import(path, options, cancellationToken), cancellationToken);

    /// <summary>Excel dosyasındaki sayfa adları (dosyadaki sırayla).</summary>
    /// <exception cref="DataImportException">Dosya açılamadığında.</exception>
    public static IReadOnlyList<string> GetSheetNames(string xlsxPath)
    {
        if (!IsExcelFile(xlsxPath))
            throw new DataImportException("Sayfa listesi yalnızca Excel (.xlsx/.xlsm/.xls) dosyaları için alınabilir.") { FilePath = xlsxPath };
        return Path.GetExtension(xlsxPath).Equals(".xls", StringComparison.OrdinalIgnoreCase)
            ? XlsImporter.GetSheetNames(xlsxPath)
            : ExcelImporter.GetSheetNames(xlsxPath);
    }
}
