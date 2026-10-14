using System.Globalization;
using System.Text.RegularExpressions;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>Satır numarası aralığı (iki uç dahil). <see cref="End"/> = <see cref="int.MaxValue"/> → sona kadar.</summary>
public readonly record struct RowRange(int Start, int End)
{
    public bool Contains(int rowNumber) => rowNumber >= Start && rowNumber <= End;

    public override string ToString() => End == Start ? Start.ToString(CultureInfo.InvariantCulture)
        : End == int.MaxValue ? $"{Start}-"
        : $"{Start}-{End}";
}

/// <summary>
/// "2-20, 35, 40-42" biçimindeki satır numarası listesi (Excel/CSV dosyasındaki numaralar).
/// Ayırıcı virgül, noktalı virgül veya boşluk; "40-" 40'tan sona kadar demektir. Çok sayıda aralıkta da
/// üyelik denetimi hızlıdır (birleştirilmiş aralıklarda ikili arama).
/// </summary>
public sealed partial class RowRangeSet
{
    /// <summary>Kullanıcıya gösterilen örnek.</summary>
    public const string Example = "2-20, 35, 40-42";

    private readonly RowRange[] _merged;

    private RowRangeSet(IReadOnlyList<RowRange> ranges)
    {
        Ranges = ranges;
        _merged = Merge(ranges);
    }

    /// <summary>Yazıldığı sırayla aralıklar.</summary>
    public IReadOnlyList<RowRange> Ranges { get; }

    public bool Contains(int rowNumber)
    {
        int lo = 0, hi = _merged.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            var range = _merged[mid];
            if (rowNumber < range.Start) hi = mid - 1;
            else if (rowNumber > range.End) lo = mid + 1;
            else return true;
        }
        return false;
    }

    /// <summary>Metni okur; anlaşılamazsa Türkçe hata iletisiyle false döner.</summary>
    public static bool TryParse(string? text, out RowRangeSet? set, out string? error)
    {
        set = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = $"Satır numaralarını yazın (ör. {Example}).";
            return false;
        }
        string normalized = DashWithSpaces().Replace(text.Trim(), "-");
        var ranges = new List<RowRange>();
        foreach (string token in normalized.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Token().Match(token);
            if (!match.Success)
            {
                error = $"\"{token}\" anlaşılamadı. Örnek: {Example} (40- yazılırsa 40'tan sona kadar).";
                return false;
            }
            int start = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            int end = !token.Contains('-') ? start
                : match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)
                : int.MaxValue;
            if (start < 1 || end < 1)
            {
                error = $"\"{token}\": satır numaraları 1'den başlar.";
                return false;
            }
            if (end < start)
            {
                error = $"\"{token}\": aralığın başı sonundan büyük olamaz.";
                return false;
            }
            ranges.Add(new RowRange(start, end));
        }
        set = new RowRangeSet(ranges);
        return true;
    }

    private static RowRange[] Merge(IReadOnlyList<RowRange> ranges)
    {
        var merged = new List<RowRange>();
        foreach (var range in ranges.OrderBy(r => r.Start))
        {
            if (merged.Count > 0 && range.Start <= (long)merged[^1].End + 1)
            {
                if (range.End > merged[^1].End) merged[^1] = merged[^1] with { End = range.End };
                continue;
            }
            merged.Add(range);
        }
        return [.. merged];
    }

    [GeneratedRegex(@"\s*[-–—]\s*")]
    private static partial Regex DashWithSpaces();

    [GeneratedRegex(@"^(\d{1,9})(?:-(\d{1,9})?)?$")]
    private static partial Regex Token();
}
