using AkelEtiket.Editor.Services;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace AkelEtiket.App.Mac.Services;

/// <summary>Koyu/açık tema ve vurgu rengi. "Sistem" seçiliyse işletim sisteminin görünümü izlenir.</summary>
public static class ThemeManager
{
    public static event EventHandler? ThemeChanged;

    /// <summary>Şu an etkin olan görünüm koyu mu?</summary>
    public static bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    public static void Configure(AppSettings settings)
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = settings.Theme switch
        {
            AppTheme.Dark => ThemeVariant.Dark,
            AppTheme.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };

        Color accent = Color.Parse(settings.Personalization.Accent switch
        {
            "blue" => "#2563EB",
            "teal" => "#087F8C",
            "purple" => "#6D28D9",
            _ => "#D7263D",
        });
        Set(app, "Accent", accent);
        Set(app, "AccentHover", Scale(accent, 0.94));
        Set(app, "AccentPressed", Scale(accent, 0.85));
        Set(app, "Selection", accent);
        Set(app, "SelectionSoft", Color.FromArgb(51, accent.R, accent.G, accent.B));
        // Fluent temasının kendi denetimleri (onay kutusu, kaydırıcı, odak çerçevesi) de aynı vurguyu kullanır.
        app.Resources["SystemAccentColor"] = accent;
        app.Resources["SystemAccentColorDark1"] = Scale(accent, 0.94);
        app.Resources["SystemAccentColorDark2"] = Scale(accent, 0.85);
        app.Resources["SystemAccentColorLight1"] = accent;
        app.Resources["SystemAccentColorLight2"] = accent;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void Set(Application app, string name, Color color)
    {
        app.Resources["Color." + name] = color;
        app.Resources["Brush." + name] = new SolidColorBrush(color);
    }

    private static Color Scale(Color c, double factor) =>
        Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));
}
