using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace AkelEtiket.App.Mac.Converters;

/// <summary>
/// Değer parametreye eşitse true (seçenek düğmeleri için). Geri dönüşte yalnız "seçildi" durumu kaynağa yazılır;
/// seçimi kalkan düğme kaynağı değiştirmez.
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null) return BindingOperations.DoNothing;
        if (parameter.GetType() == targetType) return parameter;
        return targetType.IsEnum ? Enum.Parse(targetType, parameter.ToString()!) : parameter;
    }
}

/// <summary>Durumsuz küçük dönüştürücüler (<c>{x:Static converters:Convert.…}</c>).</summary>
public static class Convert
{
    /// <summary>Sayı sıfırdan büyükse true.</summary>
    public static readonly IValueConverter IsPositive =
        new FuncValueConverter<object?, bool>(value => value is IConvertible number && number.ToDouble(CultureInfo.InvariantCulture) > 0);
}
