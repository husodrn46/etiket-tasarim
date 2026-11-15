namespace AkelEtiket.App.Mac.Localization;

/// <summary>
/// Arayüz metinleri Windows uygulamasıyla ortak katalogdan gelir ve yer yer Windows'u adıyla anar. macOS'ta bu
/// ifadeler yerinde değildir: kuyruk macOS'un kuyruğudur, kısayollar komut tuşuyladır. Yalnızca bilinen ifadeler
/// değiştirilir; "Windows-1254" gibi kodlama adlarına ve müşteri verisine dokunulmaz.
/// </summary>
public static class MacWording
{
    private static readonly (string From, string To)[] Phrases =
    [
        ("Windows yazdırma kuyruğ", "macOS yazdırma kuyruğ"),
        ("Windows yazıcı kuyruğ", "macOS yazıcı kuyruğ"),
        ("Windows kuyruğ", "macOS kuyruğ"),
        ("Windows işi", "macOS işi"),
        ("Windows'ta", "macOS'ta"),
        ("Windows hareket azaltma", "macOS hareket azaltma"),
        ("Windows print queue", "macOS print queue"),
        ("Windows printer queue", "macOS printer queue"),
        ("Windows queue", "macOS queue"),
        ("Windows job", "macOS job"),
        ("in Windows", "in macOS"),
        ("Windows reduced motion", "macOS reduced motion"),
    ];

    public static string Adapt(string text)
    {
        if (text.Length == 0) return text;
        if (text.Contains("Windows", StringComparison.Ordinal))
            foreach (var (from, to) in Phrases) text = text.Replace(from, to, StringComparison.Ordinal);
        return Shortcut(text);
    }

    /// <summary>"Ctrl+S" → "⌘S". Sekme geçişi macOS'ta da Ctrl+Tab'dır (⌘+Tab sistemindir).</summary>
    public static string Shortcut(string text)
    {
        if (!text.Contains("Ctrl+", StringComparison.Ordinal)) return text;
        const string keep = "\u0001";
        return text.Replace("Ctrl+Tab", keep + "Tab", StringComparison.Ordinal)
            .Replace("Ctrl+Shift+Tab", keep + "Shift+Tab", StringComparison.Ordinal)
            .Replace("Ctrl+", "⌘", StringComparison.Ordinal)
            .Replace(keep, "Ctrl+", StringComparison.Ordinal);
    }
}
