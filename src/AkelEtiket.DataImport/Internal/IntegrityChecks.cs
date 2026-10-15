using System.Text.RegularExpressions;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.DataImport.Internal;

/// <summary>
/// Excel/CSV kaynaklı tipik veri bozulmalarını tespit eder. Sorunlar yalnızca raporlanır;
/// satırlar okunmaya devam eder (hatalı kayıt sağlam kayıtların baskısını engellemez).
/// </summary>
internal static partial class IntegrityChecks
{
    /// <summary>Baştaki sıfır kontrolü için en kısa ortak uzunluk (fiyat/adet gibi sütunlarda yanlış alarmı önler).</summary>
    public const int MinCodeLength = 5;

    /// <summary>Sütundaki yalnızca rakamdan oluşan değerlerin en az bu oranı aynı uzunlukta olmalı.</summary>
    public const double DominantLengthRatio = 0.8;

    [GeneratedRegex(@"^[+-]?\d+(?:[.,]\d+)?[eE][+-]?\d{1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex ScientificNotationRegex();

    public static bool IsScientificNotation(string value)
    {
        if (value.Length is 0 or > 32) return false;
        if (value.IndexOf('e') < 0 && value.IndexOf('E') < 0) return false;
        return ScientificNotationRegex().IsMatch(value.Trim());
    }

    public static bool IsDigitsOnly(string value)
    {
        if (value.Length == 0) return false;
        foreach (char c in value)
            if (c is < '0' or > '9') return false;
        return true;
    }

    public static void Run(IReadOnlyList<string> columns, IReadOnlyList<DataRecord> rows, List<ImportIssue> issues)
    {
        CheckScientificNotation(columns, rows, issues);
        CheckLeadingZeroLoss(columns, rows, issues);
    }

    private static void CheckScientificNotation(IReadOnlyList<string> columns, IReadOnlyList<DataRecord> rows, List<ImportIssue> issues)
    {
        foreach (var row in rows)
        {
            for (int c = 0; c < columns.Count; c++)
            {
                string value = row.GetValue(c);
                if (!IsScientificNotation(value)) continue;
                issues.Add(new ImportIssue(row.RowNumber, columns[c], ImportIssueSeverity.Warning,
                    L10n.T($"\"{value.Trim()}\" bilimsel gösterimde. Barkod veya uzun sayı büyük olasılıkla Excel tarafından yuvarlanmış ve bozulmuş. ") +
                    L10n.T("Excel'de sütunu 'Metin' olarak biçimlendirip değerleri yeniden girin ya da dosyayı yeniden dışa aktarın.")));
            }
        }
    }

    private static void CheckLeadingZeroLoss(IReadOnlyList<string> columns, IReadOnlyList<DataRecord> rows, List<ImportIssue> issues)
    {
        for (int c = 0; c < columns.Count; c++)
        {
            int nonEmpty = 0;
            var lengthCounts = new Dictionary<int, int>();
            int digitsOnly = 0;
            foreach (var row in rows)
            {
                string value = row.GetValue(c).Trim();
                if (value.Length == 0) continue;
                nonEmpty++;
                if (!IsDigitsOnly(value)) continue;
                digitsOnly++;
                lengthCounts[value.Length] = lengthCounts.GetValueOrDefault(value.Length) + 1;
            }

            if (digitsOnly < 3 || digitsOnly < DominantLengthRatio * nonEmpty) continue;
            var dominant = lengthCounts.OrderByDescending(kv => kv.Value).ThenByDescending(kv => kv.Key).First();
            int expectedLength = dominant.Key;
            if (expectedLength < MinCodeLength || dominant.Value < DominantLengthRatio * digitsOnly) continue;

            foreach (var row in rows)
            {
                string value = row.GetValue(c).Trim();
                if (!IsDigitsOnly(value) || value.Length >= expectedLength) continue;
                issues.Add(new ImportIssue(row.RowNumber, columns[c], ImportIssueSeverity.Warning,
                    L10n.T($"\"{value}\" {value.Length} haneli, sütundaki değerlerin çoğu {expectedLength} haneli: ") +
                    L10n.T($"baştaki sıfırlar kaybolmuş olabilir (beklenen {expectedLength} hane). ") +
                    L10n.T("Excel'de sütunu 'Metin' olarak biçimlendirip değerleri yeniden girin.")));
            }
        }
    }

    /// <summary>Sorunları satır, sütun ve önem sırasına göre dizer (kararlı sıralama).</summary>
    public static List<ImportIssue> Sort(IEnumerable<ImportIssue> issues, IReadOnlyList<string> columns)
    {
        var columnOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < columns.Count; i++) columnOrder.TryAdd(columns[i], i);
        return issues
            .Select((issue, index) => (issue, index))
            .OrderBy(x => x.issue.RowNumber)
            .ThenBy(x => x.issue.Column is not null && columnOrder.TryGetValue(x.issue.Column, out int ci) ? ci : -1)
            .ThenByDescending(x => x.issue.Severity)
            .ThenBy(x => x.index)
            .Select(x => x.issue)
            .ToList();
    }
}
