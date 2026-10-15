using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Model;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Rendering.Symbols;

/// <summary>Hazır simge bilgisi.</summary>
/// <param name="Key">Kalıcı anahtar (ör. "gida-uygun"); yapay zekâ araçlarında "sembol:gida-uygun".</param>
/// <param name="Name">Türkçe ad (eklenen öğenin adı olur).</param>
/// <param name="ShortName">Paneldeki kısa etiket.</param>
/// <param name="Category">Grup: "Ürün kullanımı" veya "Koli işaretleri".</param>
/// <param name="Description">Ne zaman kullanılacağına dair kısa açıklama.</param>
public sealed record SymbolInfo(string Key, string Name, string ShortName, string Category, string Description)
{
    /// <summary>Simgenin dosya adı (görsel varlığı olarak eklenirken).</summary>
    public string FileName => Key + ".svg";
}

/// <summary>
/// Uygulamayla gelen vektör simgeler (SVG). Hepsi bu proje için çizilmiştir; ticari marka içermez (Yeşil Nokta,
/// CE gibi işaretler bilerek yoktur). Kalın çizgilerle 203 dpi 1-bit baskıda keskin çıkacak şekilde tasarlanmıştır.
/// Tuvale "Logo" yöntemiyle 1-bit görsel olarak eklenir; editörde normal görsel gibi düzenlenebilir.
/// </summary>
public static class SymbolLibrary
{
    public const string BuiltinPrefix = "sembol:";

    private static readonly ConcurrentDictionary<string, byte[]> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<SymbolInfo> All { get; } =
    [
        new("gida-uygun", L10n.T("Gıda ile temasa uygun"), L10n.T("Gıdaya uygun"), L10n.T("Ürün kullanımı"), L10n.T("Kadeh ve çatal işareti: ürün gıda ile temas için uygundur.")),
        new("bulasik-makinesi", L10n.T("Bulaşık makinesinde yıkanabilir"), L10n.T("Bulaşık makinesi"), L10n.T("Ürün kullanımı"), L10n.T("Ürün bulaşık makinesinde yıkanabilir.")),
        new("mikrodalga-yok", L10n.T("Mikrodalgada kullanmayınız"), "Mikrodalga yok", L10n.T("Ürün kullanımı"), L10n.T("Ürün mikrodalga fırında kullanılmamalıdır (melamin için önerilir).")),
        new("firin-yok", L10n.T("Fırında kullanmayınız"), L10n.T("Fırın yok"), L10n.T("Ürün kullanımı"), L10n.T("Ürün fırında kullanılmamalıdır.")),
        new("dondurucu", "Derin dondurucuya uygun", "Dondurucu", L10n.T("Ürün kullanımı"), L10n.T("Kar tanesi: ürün dondurucuda kullanılabilir.")),
        new("geri-donusum", L10n.T("Geri dönüştürülebilir"), L10n.T("Geri dönüşüm"), L10n.T("Ürün kullanımı"), L10n.T("Üç oklu geri dönüşüm işareti (genel).")),
        new("bu-taraf-yukari", L10n.T("Bu taraf yukarı"), L10n.T("Bu taraf yukarı"), L10n.T("Koli işaretleri"), L10n.T("Koli bu yönü yukarıda tutularak taşınmalıdır.")),
        new("kirilacak", L10n.T("Kırılacak eşya"), L10n.T("Kırılacak"), L10n.T("Koli işaretleri"), L10n.T("İçerik kırılabilir; dikkatli taşıyın.")),
        new("nemden-koru", "Nemden koruyunuz", "Nemden koru", L10n.T("Koli işaretleri"), L10n.T("Koli kuru tutulmalıdır.")),
        new("gunesten-koru", L10n.T("Güneşten koruyunuz"), L10n.T("Güneşten koru"), L10n.T("Koli işaretleri"), L10n.T("Koli doğrudan güneş ışığından korunmalıdır.")),
        new("istif-siniri", L10n.T("İstif sınırı"), L10n.T("İstif sınırı"), L10n.T("Koli işaretleri"), L10n.T("Üst üste en fazla belirtilen sayıda koli istiflenebilir.")),
    ];

    public static bool TryGet(string key, [NotNullWhen(true)] out SymbolInfo? info)
    {
        string normalized = Normalize(key);
        info = All.FirstOrDefault(s => string.Equals(s.Key, normalized, StringComparison.Ordinal));
        return info is not null;
    }

    /// <summary>"sembol:gida-uygun" veya "gida-uygun" → "gida-uygun".</summary>
    public static string Normalize(string key)
    {
        string text = (key ?? "").Trim();
        if (text.StartsWith(BuiltinPrefix, StringComparison.OrdinalIgnoreCase)) text = text[BuiltinPrefix.Length..];
        return text.Trim().ToLowerInvariant();
    }

    /// <summary>Simgenin SVG baytları (gömülü kaynak).</summary>
    public static byte[] GetSvg(string key)
    {
        if (!TryGet(key, out var info)) throw new KeyNotFoundException(L10n.T($"Simge bulunamadı: {key}"));
        return Cache.GetOrAdd(info.Key, k =>
        {
            using var stream = typeof(SymbolLibrary).Assembly.GetManifestResourceStream($"AkelEtiket.Rendering.Symbols.{k}.svg")
                               ?? throw new InvalidOperationException(L10n.T($"Simge kaynağı eksik: {k}"));
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        });
    }

    /// <summary>Simgeyi görsel varlığı olarak döndürür (kimlik içerikten türetilir).</summary>
    public static ImageAsset CreateAsset(string key)
    {
        var info = TryGet(key, out var found) ? found : throw new KeyNotFoundException(L10n.T($"Simge bulunamadı: {key}"));
        byte[] data = GetSvg(info.Key);
        return new ImageAsset(AssetStore.ComputeId(data, info.FileName), info.FileName, data);
    }

    /// <summary>
    /// Simgeyi tuvale eklenecek görsel öğesi olarak hazırlar: kare kutu, "Logo" yöntemi (keskin eşik), adı simgenin adı.
    /// Varlığı çağıran projeye eklemelidir (<see cref="CreateAsset"/>).
    /// </summary>
    public static ImageElement CreateElement(SymbolInfo info, ImageAsset asset, double sizeMm = 20) => new()
    {
        AssetId = asset.Id,
        Width = sizeMm,
        Height = sizeMm,
        Method = ImageConversionMethod.Logo,
        FitMode = ImageFitMode.Contain,
        Name = info.Name,
    };

    /// <summary>
    /// Simgenin baskıdaki gibi 1-bit önizlemesi (PNG). Ortak render motoruyla çizilir; paneldeki küçük resimler için.
    /// </summary>
    public static byte[] RenderPreviewPng(string key, double sizeMm = 12, double dotsPerMm = 8)
    {
        if (!TryGet(key, out var info)) throw new KeyNotFoundException(L10n.T($"Simge bulunamadı: {key}"));
        var asset = CreateAsset(info.Key);
        var assets = new AssetStore();
        assets.AddExisting(asset);
        var document = LabelDocument.CreateBlank(sizeMm, sizeMm);
        var element = CreateElement(info, asset, sizeMm);
        document.Elements.Add(element);
        using var renderer = new LabelRenderer(assets);
        var result = renderer.RenderLabel(document, new RenderOptions { DotsPerMm = dotsPerMm });
        return MonoImageExport.EncodePng(result.Bitmap, dotsPerMm * 25.4);
    }
}
