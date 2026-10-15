using System.Windows;
using System.Windows.Media;
using AkelEtiket.App.Services;

namespace AkelEtiket.App.Views;

/// <summary>Tüm pencerelerin tabanı: tema renkleri, yazı tipi, koyu başlık çubuğu.</summary>
public class ThemedWindow : Window
{
    public ThemedWindow()
    {
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ThemeManager.ThemeChanged += OnThemeChanged;
        Loaded += (_, _) => ApplyUiScale();
        Closed += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) { ThemeManager.ApplyTitleBar(this); ApplyUiScale(); }
    private void ApplyUiScale()
    {
        if (Content is FrameworkElement root) root.LayoutTransform = new ScaleTransform(ThemeManager.UiScale, ThemeManager.UiScale);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeManager.ApplyTitleBar(this);
    }
}
