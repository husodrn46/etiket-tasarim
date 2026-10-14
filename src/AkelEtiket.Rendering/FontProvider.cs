using System.Collections.Concurrent;
using System.Globalization;
using SkiaSharp;

namespace AkelEtiket.Rendering;

/// <summary>Çözümlenmiş yazı tipi: gerçek tip yüzü + yapay kalın/italik gerekip gerekmediği.</summary>
public sealed record FontMatch(SKTypeface Typeface, string RequestedFamily, bool IsExactFamily, bool FakeBold, bool FakeItalic)
{
    /// <summary>
    /// Termal baskı için yumuşatmasız (alias) ve ipuçlu (hinting) yazı: 1-bit çıktıda gövde kalınlıkları
    /// piksel ızgarasına oturur, küçük puntolar daha okunaklı basılır.
    /// </summary>
    public SKFont CreateFont(float sizeDots) => new(Typeface, sizeDots)
    {
        Edging = SKFontEdging.Alias,
        Hinting = SKFontHinting.Normal,
        Subpixel = false,
        BaselineSnap = true,
        Embolden = FakeBold,
        SkewX = FakeItalic ? -0.2f : 0f,
    };
}

/// <summary>
/// Sistem yazı tiplerine erişim (Skia/DirectWrite). İş parçacığı güvenlidir; tip yüzleri önbelleğe alınır.
/// Şablondaki yazı tipi bu bilgisayarda yoksa <see cref="FallbackFamily"/> kullanılır ve uyarı verilir.
/// </summary>
public sealed class FontProvider
{
    public const string FallbackFamily = "Arial";
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private readonly ConcurrentDictionary<(string Family, bool Bold, bool Italic), FontMatch> _cache = new();
    private readonly Lazy<IReadOnlyList<string>> _families = new(LoadFamilies);

    public static FontProvider Default { get; } = new();

    /// <summary>Yüklü yazı tipi aileleri (alfabetik).</summary>
    public IReadOnlyList<string> Families => _families.Value;

    public bool IsInstalled(string family) =>
        Families.Any(f => string.Equals(f, family, StringComparison.OrdinalIgnoreCase));

    public FontMatch Get(string family, bool bold, bool italic)
    {
        string requested = string.IsNullOrWhiteSpace(family) ? FallbackFamily : family.Trim();
        return _cache.GetOrAdd((requested.ToLowerInvariant(), bold, italic), _ => Resolve(requested, bold, italic));
    }

    private static FontMatch Resolve(string family, bool bold, bool italic)
    {
        var weight = bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        SKTypeface? requested = SKTypeface.FromFamilyName(family, weight, SKFontStyleWidth.Normal, slant);
        bool exact = requested is not null && string.Equals(requested.FamilyName, family, StringComparison.OrdinalIgnoreCase);
        SKTypeface typeface;
        if (exact)
        {
            typeface = requested!;
        }
        else
        {
            requested?.Dispose();
            typeface = SKTypeface.FromFamilyName(FallbackFamily, weight, SKFontStyleWidth.Normal, slant) ?? SKTypeface.Default;
        }
        bool fakeBold = bold && typeface.FontWeight < (int)SKFontStyleWeight.SemiBold;
        bool fakeItalic = italic && typeface.FontSlant == SKFontStyleSlant.Upright;
        return new FontMatch(typeface, family, exact, fakeBold, fakeItalic);
    }

    private static IReadOnlyList<string> LoadFamilies()
    {
        try
        {
            return SKFontManager.Default.FontFamilies
                .Where(f => !string.IsNullOrWhiteSpace(f) && !f.StartsWith('@'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.Create(Turkish, true))
                .ToList();
        }
        catch (Exception)
        {
            return [FallbackFamily];
        }
    }
}
