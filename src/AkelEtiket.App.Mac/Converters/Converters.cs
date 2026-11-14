using System.Collections;
using System.Globalization;
using AkelEtiket.DataImport;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.Rendering;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AkelEtiket.App.Mac.Converters;

/// <summary>
/// Değer parametreye eşitse true (seçenek düğmeleri için). Geri dönüşte yalnız "seçildi" durumu kaynağa yazılır;
/// seçimi kalkan düğme kaynağı değiştirmez.
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null) return BindingOperations.DoNothing;
        if (parameter.GetType() == targetType) return parameter;
        Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return type.IsEnum ? Enum.Parse(type, parameter.ToString()!) : parameter;
    }
}

/// <summary>Değerin metni, virgülle ayrılmış parametre listesinde varsa true.</summary>
public sealed class InListConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? text = value?.ToString();
        return text is not null && (parameter as string ?? "").Split(',', StringSplitOptions.TrimEntries).Contains(text);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>Kesir (0–1) ↔ yüzde (0–100).</summary>
public sealed class FractionToPercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? Math.Round(d * 100, 1) : 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? Math.Round(d / 100, 4) : 0.0;
}

/// <summary>Önem derecesi ya da durum → tema fırçası.</summary>
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
            ImportIssueSeverity.Error => "Brush.Error",
            ImportIssueSeverity.Warning => "Brush.Warning",
            ImportIssueSeverity.Info => "Brush.Info",
            RowStatus.Error => "Brush.Error",
            RowStatus.Warning => "Brush.Warning",
            RowStatus.Ok => "Brush.Success",
            true => "Brush.Success",
            false => "Brush.Error",
            _ => "Brush.TextSecondary",
        };
        return AppResources.Find<IBrush>(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>Önem derecesi ya da durum → simge çizimi.</summary>
public sealed class StateToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => AppResources.Find<Geometry>(value switch
    {
        RenderIssueSeverity.Error or "error" or false => "Glyph.Error",
        RenderIssueSeverity.Warning or "warning" => "Glyph.Warning",
        "ok" or true => "Glyph.Success",
        _ => "Glyph.Info",
    });

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>Öğe türü adı → simge çizimi (katman listesi).</summary>
public sealed class KindToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => AppResources.Find<Geometry>(value switch
    {
        "Text" => "Glyph.Text",
        "QrCode" => "Glyph.QrCode",
        "Image" => "Glyph.Image",
        "Barcode" => "Icon.Barcode",
        "Ellipse" => "Icon.Circle",
        "Line" => "Icon.DLine",
        _ => "Icon.Rectangle",
    });

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>PNG baytları → görüntü (bozuk veri için boş).</summary>
public sealed class PngBytesToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes) return null;
        try
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>Uygulama kaynaklarından etkin temaya göre değer okur.</summary>
internal static class AppResources
{
    public static T? Find<T>(string key) where T : class =>
        Application.Current is { } app && app.TryFindResource(key, app.ActualThemeVariant, out object? value) ? value as T : null;
}

/// <summary>Değer üzerinde tek bir sınama yapan dönüştürücülerin tabanı (görünürlük bağlamaları için).</summary>
public abstract class TestConverter : IValueConverter
{
    protected abstract bool Test(object? value);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Test(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;

    protected static bool HasValue(object? value) => value switch
    {
        null => false,
        string s => s.Length > 0,
        ICollection c => c.Count > 0,
        _ => true,
    };

    protected static bool Positive(object? value) =>
        value is IConvertible number and not string && number.ToDouble(CultureInfo.InvariantCulture) > 0;
}

/// <summary>Değer null, boş metin ya da boş koleksiyon değilse true.</summary>
public sealed class NotEmptyConverter : TestConverter
{
    protected override bool Test(object? value) => HasValue(value);
}

public sealed class IsEmptyConverter : TestConverter
{
    protected override bool Test(object? value) => !HasValue(value);
}

/// <summary>Sayı sıfırdan büyükse true.</summary>
public sealed class IsPositiveConverter : TestConverter
{
    protected override bool Test(object? value) => Positive(value);
}

public sealed class NotPositiveConverter : TestConverter
{
    protected override bool Test(object? value) => !Positive(value);
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
