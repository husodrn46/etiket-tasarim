using AkelEtiket.Core;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Documents;

namespace AkelEtiket.Editor.Services;

/// <summary>Tuvaldeki yazı düzenleyicisinin ekrandaki yeri (DIP), döndürme açısı ve yazı boyutu.</summary>
/// <param name="Left">Döndürülmemiş kutunun sol kenarı (DIP).</param>
/// <param name="Top">Döndürülmemiş kutunun üst kenarı (DIP).</param>
/// <param name="Width">Genişlik (DIP).</param>
/// <param name="Height">Yükseklik (DIP).</param>
/// <param name="Angle">Kutu merkezi etrafında saat yönünde döndürme (derece).</param>
/// <param name="FontSizeDip">Ekranda yaklaşık yazı boyutu (DIP); 1-bit baskı çıktısı esas alınır.</param>
public readonly record struct TextEditBox(double Left, double Top, double Width, double Height, double Angle, double FontSizeDip);

/// <summary>
/// Tuvalde doğrudan yazı düzenleme kuralları: hangi öğe düzenlenebilir, düzenleyici nereye yerleşir, değişiklik
/// tek geri alma adımı olarak nasıl yazılır. Görünümden (WPF) bağımsızdır; birim testlerle doğrulanır.
/// </summary>
public static class InlineTextEditing
{
    /// <summary>Düzenleyicinin en küçük genişliği/yüksekliği (DIP): küçük kutularda da yazılabilsin.</summary>
    public const double MinWidthDip = 80;
    public const double MinHeightDip = 22;

    /// <summary>Yalnızca görünür ve kilitsiz metin öğeleri tuvalde düzenlenebilir.</summary>
    public static bool CanEdit(LabelElement? element) => element is TextElement { Locked: false, Visible: true };

    /// <summary>
    /// Düzenleyicinin konumu: öğe kutusu mm → DIP (<paramref name="scaleDipPerMm"/>, tuval kenar boşluğu
    /// <paramref name="marginDip"/>). Yazı boyutu: punto → mm → DIP. Kutu küçükse düzenleyici merkezden büyütülür.
    /// </summary>
    public static TextEditBox ComputeBox(TextElement element, double scaleDipPerMm, double marginDip)
    {
        double fontDip = Math.Max(6, Units.PointsToMm(element.FontSize) * scaleDipPerMm);
        double width = element.Width * scaleDipPerMm;
        double height = element.Height * scaleDipPerMm;
        double left = marginDip + element.X * scaleDipPerMm;
        double top = marginDip + element.Y * scaleDipPerMm;
        double minHeight = Math.Max(MinHeightDip, fontDip * 1.5);
        if (width < MinWidthDip)
        {
            left -= (MinWidthDip - width) / 2;
            width = MinWidthDip;
        }
        if (height < minHeight)
        {
            top -= (minHeight - height) / 2;
            height = minHeight;
        }
        return new TextEditBox(left, top, width, height, element.Rotation, fontDip);
    }

    /// <summary>Satır sonlarını modeldeki biçime (\n) çevirir.</summary>
    public static string Normalize(string? text) => (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>
    /// Yeni metni yazar: değiştiyse tek bir "Metin düzenlendi" geri alma adımı oluşur ve true döner. Öğe artık
    /// düzenlenemiyorsa (silindi, kilitlendi) hiçbir şey yapmaz.
    /// </summary>
    public static bool Commit(DocumentSession session, TextElement element, string? newText)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(element);
        if (!CanEdit(element) || session.Document.FindElement(element.Id) is not TextElement current || !ReferenceEquals(current, element))
            return false;
        string text = Normalize(newText);
        if (text == element.Text) return false;
        using (session.BeginTransaction("Metin düzenlendi"))
        {
            element.Text = text;
        }
        return true;
    }
}
