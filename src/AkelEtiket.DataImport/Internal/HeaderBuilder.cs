using System.Text;

namespace AkelEtiket.DataImport.Internal;

/// <summary>
/// Sütun başlıklarını kullanılabilir hâle getirir: boş başlık → "Sütun N", tekrar eden başlık →
/// "Barkod (2)", değişken alanı ({Alan}) bozacak karakterler ({ } : satır sonu) temizlenir.
/// </summary>
internal static class HeaderBuilder
{
    public const int MaxHeaderLength = 80;

    public static List<string> Build(IReadOnlyList<string> rawHeaders, int columnCount, int headerRowNumber, List<ImportIssue> issues)
    {
        var result = new List<string>(columnCount);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columnCount; i++)
        {
            string raw = (i < rawHeaders.Count ? rawHeaders[i] : null)?.Trim() ?? "";
            string name = Sanitize(raw);
            if (name.Length == 0)
            {
                name = $"Sütun {i + 1}";
                issues.Add(new ImportIssue(headerRowNumber, name, ImportIssueSeverity.Info,
                    $"{i + 1}. sütunun başlığı boş; \"{name}\" adı verildi."));
            }
            else if (!string.Equals(name, raw, StringComparison.Ordinal))
            {
                issues.Add(new ImportIssue(headerRowNumber, name, ImportIssueSeverity.Info,
                    $"Sütun başlığı \"{raw}\" → \"{name}\" olarak düzenlendi (değişken alanlarda kullanılamayan karakterler kaldırıldı)."));
            }

            string unique = name;
            for (int n = 2; !used.Add(unique); n++)
                unique = $"{name} ({n})";
            if (!string.Equals(unique, name, StringComparison.Ordinal))
            {
                issues.Add(new ImportIssue(headerRowNumber, unique, ImportIssueSeverity.Info,
                    $"\"{name}\" başlığı birden fazla sütunda var; bu sütun \"{unique}\" olarak adlandırıldı."));
            }
            result.Add(unique);
        }
        return result;
    }

    /// <summary>Başlığı değişken alan adı olarak güvenli hâle getirir.</summary>
    public static string Sanitize(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        bool lastWasSpace = false;
        foreach (char ch in raw)
        {
            char c = ch switch
            {
                '{' => '(',
                '}' => ')',
                ':' => ' ',
                '\r' or '\n' or '\t' => ' ',
                _ => ch,
            };
            if (char.IsControl(c)) continue;
            if (char.IsWhiteSpace(c))
            {
                if (lastWasSpace || sb.Length == 0) continue;
                sb.Append(' ');
                lastWasSpace = true;
                continue;
            }
            sb.Append(c);
            lastWasSpace = false;
        }
        string result = sb.ToString().Trim();
        if (result.Length > MaxHeaderLength) result = result[..MaxHeaderLength].TrimEnd();
        return result;
    }
}
