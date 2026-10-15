using System.Globalization;
using AkelEtiket.DataImport.Internal;
using ClosedXML.Excel;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.DataImport.Excel;

/// <summary>
/// Excel (.xlsx/.xlsm) okuyucu (ClosedXML). Hücre değerleri barkod güvenliği gözetilerek metne çevrilir:
/// metin hücreleri aynen, tam sayılar basamak basamak (bilimsel gösterim/binlik ayracı olmadan),
/// ondalıklı sayılar hücre biçimine göre Türkçe kültürle (kurallar <see cref="ExcelCellText"/>; .xls ile ortak).
/// </summary>
internal static class ExcelImporter
{
    /// <summary>Bellek koruması: en fazla 128 MB.</summary>
    public const long MaxFileBytes = 128L * 1024 * 1024;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static IReadOnlyList<string> GetSheetNames(string path)
    {
        using var workbook = OpenWorkbook(path);
        return workbook.Worksheets.Select(ws => ws.Name).ToList();
    }

    public static DataSourceTable Import(string path, ImportOptions options, CancellationToken cancellationToken)
    {
        using var workbook = OpenWorkbook(path);
        var sheet = SelectSheet(workbook, options.SheetName, path);

        var firstRowUsed = sheet.FirstRowUsed();
        var lastRowUsed = sheet.LastRowUsed();
        var firstColumnUsed = sheet.FirstColumnUsed();
        var lastColumnUsed = sheet.LastColumnUsed();
        if (firstRowUsed is null || lastRowUsed is null || firstColumnUsed is null || lastColumnUsed is null)
            throw new DataImportException(L10n.T($"\"{sheet.Name}\" sayfasında veri bulunamadı.")) { FilePath = path };

        int firstRow = firstRowUsed.RowNumber(), lastRow = lastRowUsed.RowNumber();
        int firstColumn = firstColumnUsed.ColumnNumber(), lastColumn = lastColumnUsed.ColumnNumber();
        int columnCount = lastColumn - firstColumn + 1;

        var issues = new List<ImportIssue>();
        int headerRow = FindHeaderRow(sheet, options.HeaderRow, firstRow, lastRow, firstColumn, lastColumn, path);

        var rawHeaders = new string[columnCount];
        for (int c = 0; c < columnCount; c++)
            rawHeaders[c] = ReadCell(sheet.Cell(headerRow, firstColumn + c), headerRow, null, null, trim: true);
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
                values[c] = ReadCell(sheet.Cell(r, firstColumn + c), r, columns[c], rowIssues, options.TrimValues);
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

    private static XLWorkbook OpenWorkbook(string path)
    {
        // Dosya belleğe alınır: tanıtıcı hemen bırakılır ve Excel'de açık dosyalar da okunur.
        byte[] bytes = FileAccessHelper.ReadAllBytes(path, MaxFileBytes);
        if (bytes.Length == 0)
            throw new DataImportException(L10n.T("Dosya boş; geçerli bir Excel dosyası değil.")) { FilePath = path };
        try
        {
            return new XLWorkbook(new MemoryStream(bytes, writable: false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DataImportException(
                L10n.T("Excel dosyası açılamadı. Dosya bozuk, parola korumalı ya da geçerli bir .xlsx dosyası değil. ") +
                L10n.T("Dosyayı Excel'de açıp \"Farklı Kaydet → Excel Çalışma Kitabı (.xlsx)\" ile yeniden kaydetmeyi deneyin."),
                ex) { FilePath = path };
        }
    }

    private static IXLWorksheet SelectSheet(XLWorkbook workbook, string? sheetName, string path)
    {
        var sheets = workbook.Worksheets.ToList();
        if (sheets.Count == 0)
            throw new DataImportException(L10n.T("Excel dosyasında çalışma sayfası yok.")) { FilePath = path };

        if (!string.IsNullOrWhiteSpace(sheetName))
        {
            string wanted = sheetName.Trim();
            var match = sheets.FirstOrDefault(s => string.Equals(s.Name, wanted, StringComparison.OrdinalIgnoreCase))
                        ?? sheets.FirstOrDefault(s => string.Compare(s.Name, wanted, Turkish, CompareOptions.IgnoreCase) == 0);
            return match ?? throw new DataImportException(
                L10n.T($"\"{wanted}\" adlı sayfa bulunamadı. Dosyadaki sayfalar: {string.Join(", ", sheets.Select(s => s.Name))}."))
                { FilePath = path };
        }

        return sheets.FirstOrDefault(s => s.Visibility == XLWorksheetVisibility.Visible && s.FirstRowUsed() is not null)
               ?? sheets.FirstOrDefault(s => s.FirstRowUsed() is not null)
               ?? throw new DataImportException(L10n.T("Excel dosyasında veri içeren bir sayfa bulunamadı.")) { FilePath = path };
    }

    private static int FindHeaderRow(IXLWorksheet sheet, int? requested, int firstRow, int lastRow, int firstColumn, int lastColumn, string path)
    {
        if (requested is not int row) return firstRow;
        if (row < 1)
            throw new DataImportException(L10n.T("Başlık satırı 1 veya daha büyük olmalı.")) { FilePath = path };
        if (row > lastRow)
        {
            throw new DataImportException(
                L10n.T($"Başlık satırı ({row}) sayfadaki son dolu satırdan ({lastRow}) sonra geliyor.")) { FilePath = path };
        }
        // İstenen satır boşsa sonraki ilk dolu satır başlık olur.
        for (int r = Math.Max(row, firstRow); r <= lastRow; r++)
        {
            for (int c = firstColumn; c <= lastColumn; c++)
                if (!string.IsNullOrWhiteSpace(ReadCell(sheet.Cell(r, c), r, null, null, trim: true))) return r;
        }
        throw new DataImportException(L10n.T($"Belirtilen başlık satırında ({row}) veya sonrasında veri bulunamadı.")) { FilePath = path };
    }

    /// <summary>Hücreyi metne çevirir. <paramref name="issues"/> null ise sorun kaydedilmez (başlıklar).</summary>
    private static string ReadCell(IXLCell cell, int row, string? column, List<ImportIssue>? issues, bool trim)
    {
        XLCellValue value;
        try
        {
            value = cell.HasFormula ? GetFormulaValue(cell) : cell.Value;
        }
        catch (Exception)
        {
            issues?.Add(new ImportIssue(row, column, ImportIssueSeverity.Warning,
                L10n.T("Formül hesaplanamadı; hücre boş kabul edildi. Dosyayı Excel'de açıp kaydederek formül sonuçlarının dosyaya yazılmasını sağlayın.")));
            return "";
        }

        string text;
        switch (value.Type)
        {
            case XLDataType.Blank:
                return "";
            case XLDataType.Text:
                text = value.GetText();
                break;
            case XLDataType.Boolean:
                text = ExcelCellText.Boolean(value.GetBoolean());
                break;
            case XLDataType.Number:
                text = ExcelCellText.Number(value.GetNumber(), cell.Style.NumberFormat.Format ?? "", cell.Style.NumberFormat.NumberFormatId,
                    () => SafeFormatted(cell), row, column, issues);
                break;
            case XLDataType.DateTime:
                text = ExcelCellText.Date(value.GetDateTime());
                break;
            case XLDataType.TimeSpan:
                text = ExcelCellText.Duration(value.GetTimeSpan());
                break;
            case XLDataType.Error:
                issues?.Add(ExcelCellText.ErrorValueIssue(row, column, ErrorText(value.GetError())));
                return "";
            default:
                text = value.ToString(Turkish);
                break;
        }
        return trim ? text.Trim() : text;
    }

    /// <summary>Formül hücresi: dosyadaki son hesaplanmış değer; yoksa ClosedXML ile hesaplanır.</summary>
    private static XLCellValue GetFormulaValue(IXLCell cell)
    {
        var cached = cell.CachedValue;
        if (!cell.NeedsRecalculation && !cached.IsBlank) return cached;
        try
        {
            return cell.Value;
        }
        catch (Exception) when (!cached.IsBlank)
        {
            return cached;
        }
    }

    private static string? SafeFormatted(IXLCell cell)
    {
        try
        {
            string formatted = cell.GetFormattedString(Turkish);
            return string.IsNullOrWhiteSpace(formatted) ? null : formatted;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ErrorText(XLError error) => error switch
    {
        XLError.CellReference => "#REF!",
        XLError.IncompatibleValue => "#VALUE!",
        XLError.DivisionByZero => "#DIV/0!",
        XLError.NameNotRecognized => "#NAME?",
        XLError.NoValueAvailable => "#N/A",
        XLError.NullValue => "#NULL!",
        XLError.NumberInvalid => "#NUM!",
        _ => error.ToString(),
    };
}
