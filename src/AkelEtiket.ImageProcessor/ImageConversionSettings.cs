using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;

namespace AkelEtiket.ImageProcessor;

/// <summary>
/// 1-bit dönüşüm ayarları. Kayıt (record) olduğu için önbellek anahtarı olarak kullanılabilir.
/// <para>
/// <see cref="Threshold"/>: "Eşik" yönteminde mutlak eşiktir (parlaklık &lt; eşik → siyah).
/// Diğer yöntemlerde 128'e göre kaydırma (bias) olarak çalışır: değeri artırmak her yöntemde
/// görüntüyü koyulaştırır, azaltmak açar.
/// </para>
/// </summary>
public sealed record ImageConversionSettings(
    ImageConversionMethod Method,
    int Threshold = 128,
    int Brightness = 0,
    int Contrast = 0,
    bool Invert = false,
    int Sharpen = 0)
{
    public static ImageConversionSettings Default { get; } = new(ImageConversionMethod.Auto);

    public static ImageConversionSettings From(ImageElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return new ImageConversionSettings(element.Method, element.Threshold, element.Brightness, element.Contrast, element.Invert, element.Sharpen);
    }
}

/// <summary>Görsel türü (otomatik yöntem seçimi için).</summary>
public enum ImageKind
{
    Unknown,
    /// <summary>Logo, çizim, simge, metin: düz renkler ve keskin kenarlar.</summary>
    Graphic,
    /// <summary>Fotoğraf veya geçişli (sürekli tonlu) görsel.</summary>
    Photo,
}

/// <summary>1-bit dönüşüm sonucu.</summary>
public sealed class MonoConversionResult
{
    public MonoConversionResult(MonoBitmap ink, MonoBitmap? mask, ImageConversionMethod appliedMethod, ImageKind detectedKind, int? effectiveThreshold)
    {
        Ink = ink ?? throw new ArgumentNullException(nameof(ink));
        Mask = mask;
        AppliedMethod = appliedMethod;
        DetectedKind = detectedKind;
        EffectiveThreshold = effectiveThreshold;
    }

    /// <summary>Siyah pikseller (1 = siyah). Maske dışında hiçbir zaman mürekkep yoktur.</summary>
    public MonoBitmap Ink { get; }

    /// <summary>Örtülen pikseller (kapsama ≥ 128 → 1); kaynakta kapsama yoksa null (tamamı opak).</summary>
    public MonoBitmap? Mask { get; }

    /// <summary>Gerçekte uygulanan yöntem (Otomatik çözülmüş hâli; asla <see cref="ImageConversionMethod.Auto"/> değildir).</summary>
    public ImageConversionMethod AppliedMethod { get; }

    /// <summary>Otomatik seçimde belirlenen tür; manuel yöntemde <see cref="ImageKind.Unknown"/>.</summary>
    public ImageKind DetectedKind { get; }

    /// <summary>Eşik, Otsu ve Logo yöntemlerinde kullanılan eşik (parlaklık &lt; eşik → siyah); diğerlerinde null.</summary>
    public int? EffectiveThreshold { get; }
}
