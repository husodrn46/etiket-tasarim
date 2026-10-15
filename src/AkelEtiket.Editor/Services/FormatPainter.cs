using System.Reflection;
using AkelEtiket.Core.Model;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.Services;

/// <summary>
/// Biçim boyacısı: bir öğenin biçimini (içeriğini değil) kopyalayıp aynı türdeki diğer öğelere uygular.
/// Metin: yazı tipi, boyut, kalın/italik/altı çizili, hizalama, satır kaydırma, sığdırma, satır aralığı, renk, zemin.
/// Barkod: okunabilir metin ve türe özgü seçenekler (tür ve içerik değişmez).
/// Görsel: 1-bit dönüşüm ayarları ve yerleşim (görselin kendisi ve kırpma değişmez).
/// Şekil: çizgi kalınlığı, çizgi rengi, dolgu, köşe yarıçapı.
/// Konum, ölçü, ad, kilit ve görünürlük hiçbir zaman kopyalanmaz.
/// </summary>
public sealed class ElementStyle
{
    private static readonly Dictionary<Type, string[]> StyleProperties = new()
    {
        [typeof(TextElement)] =
        [
            nameof(TextElement.FontFamily), nameof(TextElement.FontSize), nameof(TextElement.Bold), nameof(TextElement.Italic),
            nameof(TextElement.Underline), nameof(TextElement.HorizontalAlignment), nameof(TextElement.VerticalAlignment),
            nameof(TextElement.WordWrap), nameof(TextElement.AutoShrink), nameof(TextElement.LineSpacing),
            nameof(TextElement.TextColor), nameof(TextElement.Background),
        ],
        [typeof(BarcodeElement)] =
        [
            nameof(BarcodeElement.ShowText), nameof(BarcodeElement.TextFontFamily), nameof(BarcodeElement.TextSize),
            nameof(BarcodeElement.QuietZone), nameof(BarcodeElement.WideToNarrowRatio), nameof(BarcodeElement.AppendCheckDigit),
            nameof(BarcodeElement.QrErrorCorrection), nameof(BarcodeElement.DataMatrixShape), nameof(BarcodeElement.Pdf417ErrorCorrection),
            nameof(BarcodeElement.Pdf417Columns), nameof(BarcodeElement.BearerBars),
        ],
        [typeof(ImageElement)] =
        [
            nameof(ImageElement.Method), nameof(ImageElement.Threshold), nameof(ImageElement.Brightness), nameof(ImageElement.Contrast),
            nameof(ImageElement.Invert), nameof(ImageElement.Sharpen), nameof(ImageElement.FitMode),
        ],
        [typeof(ShapeElement)] =
        [
            nameof(ShapeElement.StrokeWidth), nameof(ShapeElement.StrokeColor), nameof(ShapeElement.Fill), nameof(ShapeElement.CornerRadius),
        ],
    };

    private readonly IReadOnlyList<(PropertyInfo Property, object? Value)> _values;

    private ElementStyle(Type elementType, string kindName, IReadOnlyList<(PropertyInfo, object?)> values)
    {
        ElementType = elementType;
        KindName = kindName;
        _values = values;
    }

    /// <summary>Biçimin alındığı öğe türü (yalnızca aynı türe uygulanır).</summary>
    public Type ElementType { get; }

    /// <summary>Türün Türkçe adı (ör. "Metin").</summary>
    public string KindName { get; }

    /// <summary>Kopyalanan özelliklerin adları.</summary>
    public IReadOnlyList<string> PropertyNames => _values.Select(v => v.Property.Name).ToList();

    /// <summary>Öğenin biçimini alır.</summary>
    public static ElementStyle Capture(LabelElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var type = element.GetType();
        var values = new List<(PropertyInfo, object?)>();
        if (StyleProperties.TryGetValue(type, out var names))
        {
            foreach (string name in names)
            {
                var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property is { CanRead: true, CanWrite: true }) values.Add((property, property.GetValue(element)));
            }
        }
        string kind = element switch
        {
            TextElement => "Metin",
            BarcodeElement => "Barkod",
            ImageElement => L10n.T("Görsel"),
            _ => L10n.T("Şekil"),
        };
        return new ElementStyle(type, kind, values);
    }

    /// <summary>Bu biçim bu öğeye uygulanabilir mi (aynı tür, kilitsiz)?</summary>
    public bool CanApplyTo(LabelElement element) => element.GetType() == ElementType && !element.Locked;

    /// <summary>Biçimi uygun öğelere uygular; uygulanan öğe sayısını döndürür.</summary>
    public int ApplyTo(IEnumerable<LabelElement> targets)
    {
        int count = 0;
        foreach (var target in targets)
        {
            if (!CanApplyTo(target)) continue;
            foreach (var (property, value) in _values) property.SetValue(target, value);
            count++;
        }
        return count;
    }
}
