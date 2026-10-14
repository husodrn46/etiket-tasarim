using System.Collections;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AkelEtiket.Rendering;

namespace AkelEtiket.App.Converters;

/// <summary>bool → Visibility (parametre "Invert" ile tersine).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is true;
        if (parameter as string == "Invert") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible ^ (parameter as string == "Invert");
}

/// <summary>null/boş → Collapsed (parametre "Invert" ile tersine).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool hasValue = value switch
        {
            null => false,
            string s => s.Length > 0,
            ICollection c => c.Count > 0,
            _ => true,
        };
        if (parameter as string == "Invert") hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Enum/değer eşitliği → bool (RadioButton grupları için). Geri dönüşte parametre değeri.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        if (value.GetType().IsEnum && parameter is string name) return string.Equals(value.ToString(), name, StringComparison.Ordinal);
        return Equals(value, parameter) || string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null) return Binding.DoNothing;
        if (targetType.IsEnum && parameter is string name) return Enum.Parse(targetType, name);
        return parameter;
    }
}

/// <summary>PNG baytları → dondurulmuş görüntü (şablon önizlemeleri).</summary>
public sealed class PngBytesToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes) return null;
        try
        {
            var image = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>0–1 oran ↔ 0–100 yüzde (kırpma kaydırıcıları).</summary>
public sealed class FractionToPercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? Math.Round(d * 100, 1) : 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? Math.Round(d / 100, 4) : 0.0;
}

/// <summary>Sorun önem derecesi veya rapor durumu → fırça.</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value switch
        {
            RenderIssueSeverity.Error => "Brush.Error",
            RenderIssueSeverity.Warning => "Brush.Warning",
            RenderIssueSeverity.Info => "Brush.Info",
            "error" => "Brush.Error",
            "warning" => "Brush.Warning",
            "ok" => "Brush.Success",
            "info" => "Brush.Info",
            string s when s.StartsWith("HATA", StringComparison.Ordinal) => "Brush.Error",
            string s when s.StartsWith("Uyarı", StringComparison.Ordinal) => "Brush.Warning",
            DataImport.ImportIssueSeverity.Error => "Brush.Error",
            DataImport.ImportIssueSeverity.Warning => "Brush.Warning",
            DataImport.ImportIssueSeverity.Info => "Brush.Info",
            Editor.ViewModels.RowStatus.Error => "Brush.Error",
            Editor.ViewModels.RowStatus.Warning => "Brush.Warning",
            Editor.ViewModels.RowStatus.Ok => "Brush.Success",
            true => "Brush.Success",
            false => "Brush.Error",
            _ => "Brush.TextSecondary",
        };
        return Application.Current.TryFindResource(key) as Brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Rapor/sorun durumu → Fluent glifi.</summary>
public sealed class StateToGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        RenderIssueSeverity.Error or "error" or false => "\uE783",
        RenderIssueSeverity.Warning or "warning" => "\uE7BA",
        "ok" or true => "\uE930",
        _ => "\uE946",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Öğe türü anahtarı → simge (Fluent glifi veya vektör).</summary>
public sealed class KindToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value switch
        {
            "Text" => "Glyph.Text",
            "QrCode" => "Glyph.QrCode",
            "Image" => "Glyph.Image",
            "Barcode" => "Icon.Barcode",
            "Ellipse" => "Icon.Circle",
            "Line" => "Icon.DLine",
            _ => "Icon.Rectangle",
        };
        return Application.Current.TryFindResource(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Değer sıfırdan büyükse görünür.</summary>
public sealed class PositiveToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool positive = value is int i && i > 0 || value is double d && d > 0;
        if (parameter as string == "Invert") positive = !positive;
        return positive ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Değer parametreye eşitse görünür (sol panel bölümleri).</summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Değer, virgülle ayrılmış listede varsa görünür (ör. barkod türüne özel ayarlar).</summary>
public sealed class InListToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? text = value?.ToString();
        bool found = text is not null && (parameter as string ?? "").Split(',', StringSplitOptions.TrimEntries).Contains(text);
        return found ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Bool tersine çevirir.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
