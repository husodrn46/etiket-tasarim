using System.Text.RegularExpressions;

namespace AkelEtiket.DataImport.Csv;

/// <summary>
/// CSV ayırıcısını tahmin eder. İlk ~20 kayıtta (tırnak içindeki karakterler hariç) her aday
/// ayırıcının kaç kez geçtiğini sayar; en tutarlı (her satırda aynı sayıda geçen) aday seçilir.
/// Türkçe dosyalarda ondalık virgüller satırdan satıra değiştiği için noktalı virgül doğru seçilir.
/// </summary>
internal static partial class DelimiterDetector
{
    /// <summary>Eşitlik durumunda öncelik sırası.</summary>
    internal static readonly char[] Candidates = ['\t', ';', ',', '|'];

    public const char Fallback = ',';

    [GeneratedRegex(@"^\s*sep\s*=\s*(.)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SepDirectiveRegex();

    /// <summary>
    /// Excel'in "sep=;" ilk satır yönergesini okur ve metinden çıkarır. Bulunursa
    /// <paramref name="lineOffset"/> 1 artırılır (satır numaraları dosyadaki gerçek satırları göstersin).
    /// </summary>
    public static char? TryStripSepDirective(ref string text, ref int lineOffset)
    {
        int end = text.IndexOfAny(['\r', '\n']);
        string firstLine = end < 0 ? text : text[..end];
        if (firstLine.Length > 12) return null;
        var match = SepDirectiveRegex().Match(firstLine);
        if (!match.Success) return null;

        char delimiter = match.Groups[1].Value[0];
        if (end < 0)
        {
            text = "";
        }
        else
        {
            int next = end + 1;
            if (text[end] == '\r' && next < text.Length && text[next] == '\n') next++;
            text = text[next..];
        }
        lineOffset++;
        return delimiter;
    }

    public static char Detect(string text, int maxRecords = 20)
    {
        var records = CountPerRecord(text, maxRecords);
        if (records.Count == 0) return Fallback;

        char best = Fallback;
        double bestConsistency = -1;
        int bestMode = 0;
        bool found = false;
        for (int k = 0; k < Candidates.Length; k++)
        {
            var nonZero = records.Select(r => r[k]).Where(n => n > 0).ToList();
            if (nonZero.Count == 0) continue;
            int mode = nonZero.GroupBy(n => n)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Key)
                .First().Key;
            double consistency = records.Count(r => r[k] == mode) / (double)records.Count;
            if (!found || consistency > bestConsistency + 1e-9 ||
                (Math.Abs(consistency - bestConsistency) <= 1e-9 && mode > bestMode))
            {
                best = Candidates[k];
                bestConsistency = consistency;
                bestMode = mode;
                found = true;
            }
        }
        return best;
    }

    /// <summary>Her (boş olmayan) kayıt için aday ayırıcıların tırnak dışı sayıları.</summary>
    private static List<int[]> CountPerRecord(string text, int maxRecords)
    {
        var records = new List<int[]>();
        var counts = new int[Candidates.Length];
        bool inQuotes = false, fieldStart = true, hasContent = false;

        for (int i = 0; i < text.Length && records.Count < maxRecords; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') i++;
                    else inQuotes = false;
                }
                continue;
            }

            if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                if (hasContent) records.Add((int[])counts.Clone());
                Array.Clear(counts);
                fieldStart = true;
                hasContent = false;
                continue;
            }

            if (ch == '"' && fieldStart)
            {
                inQuotes = true;
                fieldStart = false;
                hasContent = true;
                continue;
            }

            int index = Array.IndexOf(Candidates, ch);
            if (index >= 0)
            {
                counts[index]++;
                fieldStart = true;
                hasContent = true;
                continue;
            }

            if (!char.IsWhiteSpace(ch)) hasContent = true;
            fieldStart = false;
        }

        if (hasContent && records.Count < maxRecords) records.Add(counts);
        return records;
    }
}
