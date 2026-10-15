using System.Collections.Concurrent;
using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;
using AkelEtiket.ImageProcessor;
using SkiaSharp;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Rendering;

/// <summary>
/// Çözülmüş görsellerin önbelleği (varlık kimliği başına bir kez çözülür). Orijinal dosya baytları
/// <see cref="AssetStore"/>'da değişmeden kalır; her ayar değişikliğinde bu orijinalden yeniden işlenir.
/// </summary>
public sealed class ImageCache : IDisposable
{
    private readonly AssetStore _assets;
    private readonly ConcurrentDictionary<string, Lazy<Entry>> _entries = new(StringComparer.OrdinalIgnoreCase);

    public ImageCache(AssetStore assets) => _assets = assets;

    private sealed class Entry
    {
        public LoadedImage? Image { get; init; }
        public string? Error { get; init; }
        public Lazy<ImageKind>? Kind { get; init; }
    }

    public bool TryGet(string assetId, out LoadedImage? image, out ImageKind kind, out string? error)
    {
        var entry = _entries.GetOrAdd(assetId, id => new Lazy<Entry>(() => Load(id), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        image = entry.Image;
        error = entry.Error;
        kind = entry.Kind?.Value ?? ImageKind.Unknown;
        return image is not null;
    }

    /// <summary>Projede olmayan (satırdan çözülen) bir görseli içerik kimliğiyle önbelleğe alır.</summary>
    public bool TryGet(ImageAsset asset, out LoadedImage? image, out ImageKind kind, out string? error)
    {
        var entry = _entries.GetOrAdd("dis:" + asset.Id, _ => new Lazy<Entry>(() => LoadAsset(asset), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        image = entry.Image;
        error = entry.Error;
        kind = entry.Kind?.Value ?? ImageKind.Unknown;
        return image is not null;
    }

    private static Entry LoadAsset(ImageAsset asset)
    {
        try
        {
            var image = ImageLoader.Load(asset.Data, asset.FileName);
            return new Entry
            {
                Image = image,
                Kind = new Lazy<ImageKind>(() => ImageAnalysis.ClassifySource(image), LazyThreadSafetyMode.ExecutionAndPublication),
            };
        }
        catch (ImageLoadException ex)
        {
            return new Entry { Error = $"{asset.FileName}: {ex.Message}" };
        }
    }

    private Entry Load(string assetId)
    {
        if (!_assets.TryGet(assetId, out var asset))
            return new Entry { Error = L10n.T("Görsel dosyası projede bulunamadı.") };
        try
        {
            var image = ImageLoader.Load(asset.Data, asset.FileName);
            return new Entry
            {
                Image = image,
                Kind = new Lazy<ImageKind>(() => ImageAnalysis.ClassifySource(image), LazyThreadSafetyMode.ExecutionAndPublication),
            };
        }
        catch (ImageLoadException ex)
        {
            return new Entry { Error = ex.Message };
        }
    }

    /// <summary>Kullanılmayan görselleri bellekten atar.</summary>
    public void EvictExcept(IEnumerable<string> usedAssetIds)
    {
        var used = new HashSet<string>(usedAssetIds, StringComparer.OrdinalIgnoreCase);
        foreach (var key in _entries.Keys)
        {
            if (used.Contains(key) || key.StartsWith("dis:", StringComparison.Ordinal)) continue;
            if (_entries.TryRemove(key, out var lazy) && lazy.IsValueCreated) lazy.Value.Image?.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var lazy in _entries.Values)
            if (lazy.IsValueCreated) lazy.Value.Image?.Dispose();
        _entries.Clear();
    }
}

/// <summary>
/// Görsel öğesi: kırpma + yerleşim (sığdır/doldur/uzat) → yazıcı çözünürlüğünde gri ton → 1-bit dönüşüm.
/// Dönüşüm nihai cihaz uzayında yapılır (döndürme dahil), böylece ekrandaki noktalar baskıyla birebir aynıdır.
/// Şeffaf alanlar beyaz kabul edilir.
/// </summary>
internal static class ImageElementRenderer
{
    public sealed record Output(LocalLayer? Layer, ImageRenderInfo? Info, string? Error);

    public static Output Render(ImageElement element, int boxWidth, int boxHeight, double rotation, int quarterTurns, ImageCache cache,
        ImageAsset? overrideAsset = null)
    {
        LoadedImage? image;
        ImageKind kind;
        string? error;
        if (overrideAsset is not null)
        {
            if (!cache.TryGet(overrideAsset, out image, out kind, out error) || image is null)
                return new Output(null, null, error ?? L10n.T("Görsel okunamadı."));
        }
        else
        {
            if (string.IsNullOrEmpty(element.AssetId))
                return new Output(null, null, L10n.T("Görsel seçilmemiş."));
            if (!cache.TryGet(element.AssetId, out image, out kind, out error) || image is null)
                return new Output(null, null, error ?? L10n.T("Görsel okunamadı."));
        }

        var (src, dest) = ComputeRects(element, image.Width, image.Height, boxWidth, boxHeight);
        var settings = ImageConversionSettings.From(element);

        LocalLayer layer;
        MonoConversionResult result;
        if (quarterTurns >= 0)
        {
            using var bitmap = RasterHelpers.CreateSurfaceBitmap(boxWidth, boxHeight);
            using (var canvas = new SKCanvas(bitmap)) DrawImage(canvas, image, src, dest);
            result = MonoConverter.Convert(GrayImage.FromBitmap(bitmap), settings, kind);
            layer = RasterHelpers.RotateQuarter(result.Ink, result.Mask, boxWidth, boxHeight, quarterTurns);
        }
        else
        {
            var frame = RasterHelpers.GetRotatedFrame(boxWidth, boxHeight, rotation);
            using var bitmap = RasterHelpers.CreateSurfaceBitmap(frame.Width, frame.Height);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.SetMatrix(frame.Transform);
                DrawImage(canvas, image, src, dest);
            }
            result = MonoConverter.Convert(GrayImage.FromBitmap(bitmap), settings, kind);
            layer = new LocalLayer(result.Ink, result.Mask, frame.OffsetX, frame.OffsetY);
        }

        var info = new ImageRenderInfo
        {
            AppliedMethod = result.AppliedMethod,
            DetectedKindText = result.DetectedKind switch
            {
                ImageKind.Graphic => L10n.T("Logo / çizim"),
                ImageKind.Photo => L10n.T("Fotoğraf"),
                _ => "",
            },
            EffectiveThreshold = result.EffectiveThreshold,
            SourceWidth = image.Width,
            SourceHeight = image.Height,
            FormatName = image.FormatName,
        };
        return new Output(layer, info, null);
    }

    private static void DrawImage(SKCanvas canvas, LoadedImage image, SKRect src, SKRect dest)
    {
        // Görsel alanının altına opak beyaz: şeffaf pikseller beyaz basılır.
        using (var white = RasterHelpers.FillPaint(RasterHelpers.White))
            canvas.DrawRect(dest, white);
        image.Draw(canvas, src, dest);
    }

    /// <summary>Kırpma ve yerleşim kipine göre kaynak/hedef dikdörtgenleri.</summary>
    internal static (SKRect Source, SKRect Dest) ComputeRects(ImageElement element, int imageWidth, int imageHeight, int boxWidth, int boxHeight)
    {
        var (cl, ct, cr, cb) = element.GetEffectiveCrop();
        float sx = (float)(imageWidth * cl), sy = (float)(imageHeight * ct);
        float sw = (float)(imageWidth * (1 - cl - cr)), sh = (float)(imageHeight * (1 - ct - cb));
        sw = Math.Max(1, sw);
        sh = Math.Max(1, sh);
        var source = new SKRect(sx, sy, sx + sw, sy + sh);
        var box = new SKRect(0, 0, boxWidth, boxHeight);

        switch (element.FitMode)
        {
            case ImageFitMode.Stretch:
                return (source, box);
            case ImageFitMode.Cover:
            {
                float scale = Math.Max(boxWidth / sw, boxHeight / sh);
                float visibleW = boxWidth / scale, visibleH = boxHeight / scale;
                float vx = sx + (sw - visibleW) / 2, vy = sy + (sh - visibleH) / 2;
                return (new SKRect(vx, vy, vx + visibleW, vy + visibleH), box);
            }
            default:
            {
                float scale = Math.Min(boxWidth / sw, boxHeight / sh);
                float dw = sw * scale, dh = sh * scale;
                float dx = MathF.Round((boxWidth - dw) / 2), dy = MathF.Round((boxHeight - dh) / 2);
                return (source, new SKRect(dx, dy, dx + MathF.Round(dw), dy + MathF.Round(dh)));
            }
        }
    }
}
