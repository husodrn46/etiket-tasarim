using System.Globalization;
using System.Text;
using AkelEtiket.DataImport.Csv;
using AkelEtiket.DataImport.Internal;
using ExcelDataReader;
using ExcelDataReader.Exceptions;
using ExcelNumberFormat;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.DataImport.Excel;

/// <summary>
/// Eski Excel (.xls, Excel 97–2003) okuyucu (ExcelDataReader). Sayfa seçimi, başlık satırı, gerçek Excel satır
/// numaraları, boş satırların atlanması ve hücre dönüşümü .xlsx okuyucusuyla aynıdır (<see cref="ExcelCellText"/>):
/// metin hücreleri aynen (baştaki sıfırlar korunur), sayılar hücre biçimine göre. Formüllerde dosyadaki son hesaplanmış
/// değer kullanılır. Uzantısı .xls olan .xlsx, HTML ve metin (CSV/sekmeli) dosyaları ayrıca tanınır.
/// </summary>
internal static class XlsImporter
{
    private static readonly byte[] CompoundSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private static readonly CultureInfo Turkish = ExcelCellText.Turkish;

    // Windows-1254: kod sayfası kaydı olmayan eski (BIFF2-5) dosyalar için Türkçe varsayılır.
    static XlsImporter() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static IReadOnlyList<string> GetSheetNames(string path)
    {
        byte[] bytes = ReadBytes(path);
        switch (Detect(bytes, path))
        {
            case Content.Xlsx:
                return ExcelImporter.GetSheetNames(path);
            case Content.Text:
                return [];
        }
        return Guard(path, () =>
        {
            using var reader = OpenReader(bytes, path);
            return SheetNames(reader);
        });
    }

    public static DataSourceTable Import(string path, ImportOptions options, CancellationToken cancellationToken)
    {
        byte[] bytes = ReadBytes(path);
        switch (Detect(bytes, path))
        {
            case Content.Xlsx:
                // Bazı programlar yeni biçimi (.xlsx) .xls uzantısıyla kaydeder.
                return ExcelImporter.Import(path, options, cancellationToken);
            case Content.Text:
                return ImportAsText(path, options, cancellationToken);
        }
        return Guard(path, () =>
        {
            using var reader = OpenReader(bytes, path);
            var sheet = SelectSheet(reader, options.SheetName, path, cancellationToken);
            return Build(sheet, path, options, cancellationToken);
        });
    }

    private static DataSourceTable Build(XlsSheet sheet, string path, ImportOptions options, CancellationToken cancellationToken)
    {
        if (sheet.IsEmpty)
            throw new DataImportException(L10n.T($"\"{sheet.Name}\" sayfasında veri bulunamadı.")) { FilePath = path };

        int firstRow = sheet.FirstRow, lastRow = sheet.LastRow;
        int firstColumn = sheet.FirstColumn, lastColumn = sheet.LastColumn;
        int columnCount = lastColumn - firstColumn + 1;
        var formats = new NumberFormatCache();

        var issues = new List<ImportIssue>();
        int headerRow = FindHeaderRow(sheet, options.HeaderRow, formats, path);

        var rawHeaders = new string[columnCount];
        for (int c = 0; c < columnCount; c++)
            rawHeaders[c] = ReadCell(sheet.Get(headerRow, firstColumn + c), headerRow, null, null, trim: true, formats);
        var columns = HeaderBuilder.Build(rawHeaders, columnCount, headerRow, issues);

        var rows = new List<DataRecord>();
        int skipped = 0;
        bool truncated = false;
        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            if ((r & 511) == 0) cancellationToken.ThrowIfCancellationRequested();
            var rowIssues = new List<ImportIssue>();
            var values = new string[columnCount];
            bool hasValue = false;
            for (int c = 0; c < columnCount; c++)
            {
                values[c] = ReadCell(sheet.Get(r, firstColumn + c), r, columns[c], rowIssues, options.TrimValues, formats);
                if (!string.IsNullOrWhiteSpace(values[c])) hasValue = true;
            }
            if (!hasValue)
            {
                skipped++;
                continue;
            }
            if (rows.Count >= options.MaxRows)
            {
                truncated = true;
                break;
            }
            issues.AddRange(rowIssues);
            rows.Add(new DataRecord(r, values, columns));
        }

        if (rows.Count == 0)
        {
            issues.Add(new ImportIssue(0, null, ImportIssueSeverity.Warning, L10n.T("Başlık satırından sonra veri satırı yok.")));
        }
        if (truncated)
        {
            issues.Add(new ImportIssue(0, null, ImportIssueSeverity.Warning,
                L10n.T($"Sayfada {options.MaxRows} satırdan fazla veri var; yalnızca ilk {options.MaxRows} satır okundu.")));
        }

        IntegrityChecks.Run(columns, rows, issues);

        return new DataSourceTable(
            path, DataSourceTable.KindExcel, sheet.Name, headerRow, columns, rows,
            IntegrityChecks.Sort(issues, columns), encodingName: null, delimiter: null, skipped, truncated);
    }

    // ───────────────────────── Dosya ─────────────────────────

    private enum Content
    {
        Xls,
        Xlsx,
        Text,
    }

    private static byte[] ReadBytes(string path)
    {
        // Dosya belleğe alınır: tanıtıcı hemen bırakılır ve Excel'de açık dosyalar da okunur.
        byte[] bytes = FileAccessHelper.ReadAllBytes(path, ExcelImporter.MaxFileBytes);
        if (bytes.Length == 0)
            throw new DataImportException(L10n.T("Dosya boş; geçerli bir Excel dosyası değil.")) { FilePath = path };
        return bytes;
    }

    private static Content Detect(byte[] bytes, string path)
    {
        var span = bytes.AsSpan();
        // Yarım kalmış bir başlık da (ilk 4 bayt) bozuk .xls sayılır; ExcelDataReader açıklayıcı hata verir.
        if (span.StartsWith(CompoundSignature.AsSpan(0, Math.Min(span.Length, 4)))) return Content.Xls;
        if (span.StartsWith("PK\u0003\u0004"u8)) return Content.Xlsx;
        if (span.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || span.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])) return Content.Text;

        int start = span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        while (start < bytes.Length && bytes[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') start++;
        if (start < bytes.Length && bytes[start] == (byte)'<')
        {
            throw new DataImportException(
                L10n.T("Bu .xls dosyası gerçek bir Excel dosyası değil; içinde web sayfası (HTML) ya da XML tablo var (bazı programlar ") +
                L10n.T("dışa aktarımı böyle kaydeder). Dosyayı Excel'de açıp \"Farklı Kaydet → Excel Çalışma Kitabı (.xlsx)\" ile kaydedin ") +
                L10n.T("ya da CSV olarak dışa aktarın.")) { FilePath = path };
        }
        return LooksLikeText(span[..Math.Min(span.Length, 8192)]) ? Content.Text : Content.Xls;
    }

    /// <summary>Sekme/satır sonu dışında denetim karakteri yoksa içerik metindir (sekmeli/CSV dışa aktarımı .xls uzantısıyla).</summary>
    private static bool LooksLikeText(ReadOnlySpan<byte> head)
    {
        foreach (byte b in head)
            if (b < 0x20 && b is not ((byte)'\t' or (byte)'\r' or (byte)'\n' or 0x0C)) return false;
        return true;
    }

    private static DataSourceTable ImportAsText(string path, ImportOptions options, CancellationToken cancellationToken)
    {
        var table = CsvImporter.Import(path, options, cancellationToken);
        var note = new ImportIssue(0, null, ImportIssueSeverity.Info,
            L10n.T("Dosyanın uzantısı .xls ama içeriği metin (CSV/sekmeli); metin dosyası olarak okundu."));
        return new DataSourceTable(
            table.SourcePath, table.SourceKind, table.SheetName, table.HeaderRowNumber, table.Columns, table.Rows,
            [note, .. table.Issues], table.EncodingName, table.Delimiter, table.SkippedEmptyRows, table.WasTruncated);
    }

    private static IExcelDataReader OpenReader(byte[] bytes, string path) =>
        ExcelReaderFactory.CreateBinaryReader(new MemoryStream(bytes, writable: false),
            new ExcelReaderConfiguration { FallbackEncoding = Encoding.GetEncoding(1254) });

    /// <summary>ExcelDataReader hatalarını Türkçe açıklamaya çevirir.</summary>
    private static T Guard<T>(string path, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is DataImportException or OperationCanceledException)
        {
            throw;
        }
        catch (InvalidPasswordException ex)
        {
            throw new DataImportException(
                L10n.T("Excel dosyası parola korumalı. Dosyayı Excel'de açıp parolayı kaldırarak yeniden kaydedin."), ex) { FilePath = path };
        }
        catch (Exception ex)
        {
            throw new DataImportException(
                L10n.T("Eski Excel (.xls) dosyası açılamadı. Dosya bozuk, parola korumalı ya da geçerli bir .xls dosyası değil. ") +
                L10n.T("Dosyayı Excel'de açıp \"Farklı Kaydet → Excel Çalışma Kitabı (.xlsx)\" ile yeniden kaydetmeyi deneyin."),
                ex) { FilePath = path };
        }
    }

    // ───────────────────────── Sayfalar ─────────────────────────

    private static List<string> SheetNames(IExcelDataReader reader)
    {
        var names = new List<string>();
        if (reader.ResultsCount == 0) return names;
        do names.Add(reader.Name ?? $"Sayfa{names.Count + 1}");
        while (reader.NextResult());
        return names;
    }

    private static XlsSheet SelectSheet(IExcelDataReader reader, string? sheetName, string path, CancellationToken cancellationToken)
    {
        var names = SheetNames(reader);
        if (names.Count == 0)
            throw new DataImportException(L10n.T("Excel dosyasında çalışma sayfası yok.")) { FilePath = path };
        reader.Reset();

        if (!string.IsNullOrWhiteSpace(sheetName))
        {
            string wanted = sheetName.Trim();
            int index = names.FindIndex(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase));
            if (index < 0) index = names.FindIndex(n => string.Compare(n, wanted, Turkish, CompareOptions.IgnoreCase) == 0);
            if (index < 0)
            {
                throw new DataImportException(
                    L10n.T($"\"{wanted}\" adlı sayfa bulunamadı. Dosyadaki sayfalar: {string.Join(", ", names)}.")) { FilePath = path };
            }
            for (int i = 0; i < index; i++) reader.NextResult();
            return XlsSheet.Load(reader, cancellationToken);
        }

        // Adı verilmediyse: veri içeren ilk görünür sayfa, yoksa veri içeren ilk sayfa.
        XlsSheet? firstWithData = null;
        do
        {
            var sheet = XlsSheet.Load(reader, cancellationToken);
            if (sheet.IsEmpty) continue;
            if (sheet.IsVisible) return sheet;
            firstWithData ??= sheet;
        }
        while (reader.NextResult());
        return firstWithData ?? throw new DataImportException(L10n.T("Excel dosyasında veri içeren bir sayfa bulunamadı.")) { FilePath = path };
    }

    private static int FindHeaderRow(XlsSheet sheet, int? requested, NumberFormatCache formats, string path)
    {
        if (requested is not int row) return sheet.FirstRow;
        if (row < 1)
            throw new DataImportException(L10n.T("Başlık satırı 1 veya daha büyük olmalı.")) { FilePath = path };
        if (row > sheet.LastRow)
        {
            throw new DataImportException(
                L10n.T($"Başlık satırı ({row}) sayfadaki son dolu satırdan ({sheet.LastRow}) sonra geliyor.")) { FilePath = path };
        }
        // İstenen satır boşsa sonraki ilk dolu satır başlık olur.
        for (int r = Math.Max(row, sheet.FirstRow); r <= sheet.LastRow; r++)
        {
            for (int c = sheet.FirstColumn; c <= sheet.LastColumn; c++)
                if (!string.IsNullOrWhiteSpace(ReadCell(sheet.Get(r, c), r, null, null, trim: true, formats))) return r;
        }
        throw new DataImportException(L10n.T($"Belirtilen başlık satırında ({row}) veya sonrasında veri bulunamadı.")) { FilePath = path };
    }

    // ───────────────────────── Hücreler ─────────────────────────

    /// <summary>Hücreyi metne çevirir. <paramref name="issues"/> null ise sorun kaydedilmez (başlıklar).</summary>
    private static string ReadCell(XlsCell cell, int row, string? column, List<ImportIssue>? issues, bool trim, NumberFormatCache formats)
    {
        if (cell.Error is { } error)
        {
            issues?.Add(ExcelCellText.ErrorValueIssue(row, column, ErrorText(error)));
            return "";
        }
        string text;
        switch (cell.Value)
        {
            case null:
                return "";
            case string s:
                text = s;
                break;
            case bool b:
                text = ExcelCellText.Boolean(b);
                break;
            case DateTime date:
                text = ExcelCellText.Date(date);
                break;
            case TimeSpan span:
                text = ExcelCellText.Duration(span);
                break;
            case double or float or int or long or short or decimal:
                double number = Convert.ToDouble(cell.Value, CultureInfo.InvariantCulture);
                string format = cell.Format ?? "";
                text = ExcelCellText.Number(number, format, cell.FormatIndex, () => formats.Format(number, format), row, column, issues);
                break;
            default:
                text = Convert.ToString(cell.Value, Turkish) ?? "";
                break;
        }
        return trim ? text.Trim() : text;
    }

    private static string ErrorText(CellError error) => error switch
    {
        CellError.NULL => "#NULL!",
        CellError.DIV0 => "#DIV/0!",
        CellError.VALUE => "#VALUE!",
        CellError.REF => "#REF!",
        CellError.NAME => "#NAME?",
        CellError.NUM => "#NUM!",
        CellError.NA => "#N/A",
        CellError.GETTING_DATA => "#GETTING_DATA",
        _ => error.ToString(),
    };

    /// <summary>Hücre biçimini Excel'in gösterdiği gibi uygular (ClosedXML ile aynı motor); biçimler bir kez çözümlenir.</summary>
    private sealed class NumberFormatCache
    {
        private readonly Dictionary<string, NumberFormat?> _formats = new(StringComparer.Ordinal);

        public string? Format(double value, string format)
        {
            if (format.Length == 0) return null;
            if (!_formats.TryGetValue(format, out var numberFormat))
            {
                try
                {
                    numberFormat = new NumberFormat(format);
                    if (!numberFormat.IsValid) numberFormat = null;
                }
                catch (Exception)
                {
                    numberFormat = null;
                }
                _formats[format] = numberFormat;
            }
            if (numberFormat is null) return null;
            try
            {
                string text = numberFormat.Format(value, Turkish);
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    private readonly record struct XlsCell(object? Value, CellError? Error, string? Format, int FormatIndex);

    /// <summary>Belleğe alınmış sayfa: yalnızca dolu hücreler tutulur (satır/sütun numaraları 1'den başlar).</summary>
    private sealed class XlsSheet
    {
        private readonly List<XlsCell[]> _rows;

        private XlsSheet(string name, bool visible, List<XlsCell[]> rows, int firstRow, int lastRow, int firstColumn, int lastColumn)
        {
            Name = name;
            IsVisible = visible;
            _rows = rows;
            FirstRow = firstRow;
            LastRow = lastRow;
            FirstColumn = firstColumn;
            LastColumn = lastColumn;
        }

        public string Name { get; }
        public bool IsVisible { get; }
        public int FirstRow { get; }
        public int LastRow { get; }
        public int FirstColumn { get; }
        public int LastColumn { get; }
        public bool IsEmpty => LastRow == 0;

        public XlsCell Get(int row, int column)
        {
            if (row < 1 || row > _rows.Count) return default;
            var cells = _rows[row - 1];
            return column >= 1 && column <= cells.Length ? cells[column - 1] : default;
        }

        /// <summary>Okuyucunun bulunduğu sayfayı okur. ExcelDataReader boş satırları da sırayla verir; satır numarası korunur.</summary>
        public static XlsSheet Load(IExcelDataReader reader, CancellationToken cancellationToken)
        {
            var rows = new List<XlsCell[]>();
            int firstRow = 0, lastRow = 0, firstColumn = int.MaxValue, lastColumn = 0;
            while (reader.Read())
            {
                int rowNumber = rows.Count + 1;
                if ((rowNumber & 511) == 0) cancellationToken.ThrowIfCancellationRequested();
                XlsCell[]? buffer = null;
                int first = -1, last = -1;
                for (int c = 0; c < reader.FieldCount; c++)
                {
                    object? value = reader.GetValue(c);
                    CellError? error = reader.GetCellError(c);
                    if (error is null && (value is null || value is string { Length: 0 })) continue;
                    buffer ??= new XlsCell[reader.FieldCount];
                    buffer[c] = new XlsCell(value, error, reader.GetNumberFormatString(c), reader.GetNumberFormatIndex(c));
                    if (first < 0) first = c;
                    last = c;
                }
                if (buffer is null)
                {
                    rows.Add([]);
                    continue;
                }
                rows.Add(buffer[..(last + 1)]);
                if (firstRow == 0) firstRow = rowNumber;
                lastRow = rowNumber;
                firstColumn = Math.Min(firstColumn, first + 1);
                lastColumn = Math.Max(lastColumn, last + 1);
            }
            bool visible = string.IsNullOrEmpty(reader.VisibleState) || reader.VisibleState.Equals("visible", StringComparison.OrdinalIgnoreCase);
            return new XlsSheet(reader.Name ?? "", visible, rows, firstRow, lastRow, lastRow == 0 ? 0 : firstColumn, lastColumn);
        }
    }
}
