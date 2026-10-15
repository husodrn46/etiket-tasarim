using System.Globalization;
using AkelEtiket.DataImport.Internal;
using CsvHelper;
using CsvHelper.Configuration;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.DataImport.Csv;

/// <summary>
/// CSV okuyucu (CsvHelper). Değerler asla sayıya çevrilmez; satır numaraları, çok satırlı
/// tırnaklı alanlar dahil, kaydın dosyada başladığı fiziksel satırı gösterir.
/// </summary>
internal static class CsvImporter
{
    /// <summary>Bellek koruması: en fazla 256 MB.</summary>
    public const long MaxFileBytes = 256L * 1024 * 1024;

    public static DataSourceTable Import(string path, ImportOptions options, CancellationToken cancellationToken)
    {
        byte[] bytes = FileAccessHelper.ReadAllBytes(path, MaxFileBytes);
        if (bytes.Length == 0)
            throw new DataImportException(L10n.T("Dosya boş; içinde okunacak veri yok.")) { FilePath = path };

        var (text, encodingName) = TextDecoding.Decode(bytes, options.Encoding);
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];

        int lineOffset = 0;
        char? sepDirective = DelimiterDetector.TryStripSepDirective(ref text, ref lineOffset);
        char delimiter = options.Delimiter ?? sepDirective ?? DelimiterDetector.Detect(text);

        var issues = new List<ImportIssue>();
        var records = ReadRecords(path, text, delimiter, lineOffset, issues, cancellationToken);

        // Başlık: (varsa HeaderRow satırından itibaren) ilk dolu kayıt.
        int headerIndex = -1;
        for (int i = 0; i < records.Count; i++)
        {
            if (options.HeaderRow is int headerRow && records[i].Line < headerRow) continue;
            if (IsEmpty(records[i].Fields)) continue;
            headerIndex = i;
            break;
        }
        if (headerIndex < 0)
        {
            string message = options.HeaderRow is int requested
                ? L10n.T($"Belirtilen başlık satırında ({requested}) veya sonrasında veri bulunamadı.")
                : L10n.T("Dosyada okunabilir veri bulunamadı (yalnızca boş satırlar var).");
            throw new DataImportException(message) { FilePath = path };
        }

        var header = records[headerIndex];
        int columnCount = EffectiveFieldCount(header.Fields);
        for (int i = headerIndex + 1; i < records.Count; i++)
            columnCount = Math.Max(columnCount, EffectiveFieldCount(records[i].Fields));
        columnCount = Math.Max(columnCount, 1);

        var columns = HeaderBuilder.Build(header.Fields, columnCount, header.Line, issues);

        var rows = new List<DataRecord>();
        int skipped = 0;
        bool truncated = false;
        for (int i = headerIndex + 1; i < records.Count; i++)
        {
            var (line, fields) = records[i];
            if (IsEmpty(fields))
            {
                skipped++;
                continue;
            }
            if (rows.Count >= options.MaxRows)
            {
                truncated = true;
                break;
            }
            var values = new string[columnCount];
            for (int c = 0; c < columnCount; c++)
            {
                string value = c < fields.Length ? fields[c] ?? "" : "";
                values[c] = options.TrimValues ? value.Trim() : value;
            }
            rows.Add(new DataRecord(line, values, columns));
        }

        if (rows.Count == 0)
        {
            issues.Add(new ImportIssue(0, null, ImportIssueSeverity.Warning, L10n.T("Başlık satırından sonra veri satırı yok.")));
        }
        if (truncated)
        {
            issues.Add(new ImportIssue(0, null, ImportIssueSeverity.Warning,
                L10n.T($"Dosyada {options.MaxRows} satırdan fazla veri var; yalnızca ilk {options.MaxRows} satır okundu.")));
        }

        IntegrityChecks.Run(columns, rows, issues);

        return new DataSourceTable(
            path, DataSourceTable.KindCsv, sheetName: null, header.Line, columns, rows,
            IntegrityChecks.Sort(issues, columns), encodingName, delimiter, skipped, truncated);
    }

    private static List<(int Line, string[] Fields)> ReadRecords(
        string path, string text, char delimiter, int lineOffset, List<ImportIssue> issues, CancellationToken cancellationToken)
    {
        bool badData = false;
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter.ToString(),
            HasHeaderRecord = false,
            // Boş satırlar da kayıt olarak gelsin: satır numarası hesabı her fiziksel satırın bir kayda ait olmasına dayanır.
            IgnoreBlankLines = false,
            MissingFieldFound = null,
            BadDataFound = _ => badData = true,
            DetectColumnCountChanges = false,
            TrimOptions = TrimOptions.None,
            Mode = CsvMode.RFC4180,
            LineBreakInQuotedFieldIsBadData = false,
        };

        var records = new List<(int Line, string[] Fields)>();
        using var parser = new CsvParser(new StringReader(text), config);
        int previousRawRow = 0;
        while (true)
        {
            bool read;
            try
            {
                read = parser.Read();
            }
            catch (CsvHelperException ex)
            {
                throw new DataImportException(
                    L10n.T($"CSV dosyası {previousRawRow + 1 + lineOffset}. satır civarında okunamadı. Dosyanın bozuk olmadığını ve ayırıcının doğru seçildiğini kontrol edin."),
                    ex) { FilePath = path };
            }
            if (!read) break;

            // RawRow: kaydın son fiziksel satırı. Kayıt bir önceki kaydın bittiği satırın ardından başlar.
            int line = previousRawRow + 1 + lineOffset;
            previousRawRow = parser.RawRow;
            string[] fields = parser.Record ?? [];
            if (badData)
            {
                issues.Add(new ImportIssue(line, null, ImportIssueSeverity.Warning,
                    L10n.T("Hatalı tırnak (\") kullanımı; satır olduğu gibi okundu. Bu satırdaki değerleri kontrol edin.")));
                badData = false;
            }
            records.Add((line, fields));
            if ((records.Count & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
        }
        return records;
    }

    private static bool IsEmpty(string[] fields)
    {
        foreach (var field in fields)
            if (!string.IsNullOrWhiteSpace(field)) return false;
        return true;
    }

    /// <summary>Sondaki boş alanlar hariç alan sayısı ("a;b;;" → 2).</summary>
    private static int EffectiveFieldCount(string[] fields)
    {
        for (int i = fields.Length - 1; i >= 0; i--)
            if (!string.IsNullOrWhiteSpace(fields[i])) return i + 1;
        return 0;
    }
}
