using System.Text;
using SkiaSharp;
using Svg.Skia;

namespace AkelEtiket.ImageProcessor;

/// <summary>
/// Görsel dosyalarını çözer. PNG, JPG/JPEG, BMP, GIF (ilk kare), WEBP ve SVG desteklenir.
/// JPEG EXIF yönü uygulanır; çok büyük görseller uzun kenarı <see cref="MaxDimension"/> pikseli
/// geçmeyecek şekilde küçültülür (orijinal dosya proje içinde değişmeden saklanır).
/// </summary>
public static class ImageLoader
{
    /// <summary>Çözülmüş görselin uzun kenar sınırı (piksel). 100 mm etiket 300 DPI'da bile ~1200 pikseldir.</summary>
    public const int MaxDimension = 4096;

    /// <summary>Tam çözünürlükte açılabilecek en büyük görsel (piksel sayısı).</summary>
    public const long MaxDecodePixels = 80_000_000;

    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".svg"];

    private const string UnsupportedMessage =
        "Görsel dosyası okunamadı. Dosya bozuk olabilir ya da biçimi desteklenmiyor " +
        "(desteklenenler: PNG, JPG, BMP, GIF, WEBP, SVG).";

    public static IReadOnlyList<string> SupportedExtensions => Extensions;

    /// <summary>WPF OpenFileDialog için Türkçe filtre metni.</summary>
    public static string OpenFileFilter =>
        "Görseller (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.svg)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.svg|" +
        "PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|BMP (*.bmp)|*.bmp|GIF (*.gif)|*.gif|" +
        "WEBP (*.webp)|*.webp|SVG (*.svg)|*.svg|Tüm dosyalar (*.*)|*.*";

    /// <summary>Dosya uzantısı desteklenen bir görsel biçimi mi?</summary>
    public static bool IsSupportedFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string ext = Path.GetExtension(path);
        return Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Dosya baytlarını çözer. Biçim içerikten anlaşılır; <paramref name="fileNameHint"/> yalnızca
    /// yardımcıdır. Hata durumunda Türkçe mesajlı <see cref="ImageLoadException"/> fırlatır.
    /// </summary>
    public static LoadedImage Load(byte[] data, string? fileNameHint = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0) throw new ImageLoadException("Görsel dosyası boş.");

        try
        {
            return LooksLikeSvg(data, fileNameHint) ? SvgLoader.Load(data) : LoadRaster(data);
        }
        catch (ImageLoadException)
        {
            throw;
        }
        catch (OutOfMemoryException ex)
        {
            throw new ImageLoadException("Görsel açılırken bellek yetersiz kaldı. Daha küçük bir görsel deneyin.", ex);
        }
        catch (Exception ex)
        {
            throw new ImageLoadException(UnsupportedMessage, ex);
        }
    }

    private static LoadedImage LoadRaster(byte[] data)
    {
        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData) ?? throw new ImageLoadException(UnsupportedMessage);

        SKImageInfo encoded = codec.Info;
        int encW = encoded.Width, encH = encoded.Height;
        if (encW <= 0 || encH <= 0) throw new ImageLoadException(UnsupportedMessage);

        SKEncodedOrigin origin = codec.EncodedOrigin;
        bool swapAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        int originalW = swapAxes ? encH : encW;
        int originalH = swapAxes ? encW : encH;

        // Hedef boyut (uzun kenar ≤ MaxDimension, oran korunur).
        double scale = Math.Min(1.0, MaxDimension / (double)Math.Max(encW, encH));
        int targetW = scale < 1 ? Math.Max(1, (int)Math.Round(encW * scale)) : encW;
        int targetH = scale < 1 ? Math.Max(1, (int)Math.Round(encH * scale)) : encH;

        // JPEG gibi biçimler çözme sırasında hızlı küçültebilir (1/2, 1/4, 1/8).
        SKSizeI decodeSize = new(encW, encH);
        if (scale < 1)
        {
            SKSizeI native = codec.GetScaledDimensions((float)scale);
            if (native.Width >= targetW && native.Height >= targetH && native.Width <= encW && native.Height <= encH)
                decodeSize = native;
        }
        if ((long)decodeSize.Width * decodeSize.Height > MaxDecodePixels)
            throw new ImageLoadException(
                $"Görsel çok büyük ({encW}×{encH} piksel). En fazla {MaxDecodePixels / 1_000_000} megapiksel " +
                "desteklenir; görseli küçültüp yeniden deneyin.");

        // Paylaşılan sRGB nesnesi: elden çıkarılmaz (dispose edilmez).
        SKColorSpace srgb = SKColorSpace.CreateSrgb();
        var decodeInfo = new SKImageInfo(decodeSize.Width, decodeSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul, srgb);
        SKBitmap bitmap = new(decodeInfo);
        try
        {
            if (bitmap.GetPixels() == IntPtr.Zero)
                throw new ImageLoadException("Görsel açılırken bellek yetersiz kaldı. Daha küçük bir görsel deneyin.");

            SKCodecResult result = codec.GetPixels(decodeInfo, bitmap.GetPixels());
            string? warning = null;
            switch (result)
            {
                case SKCodecResult.Success:
                    break;
                case SKCodecResult.IncompleteInput:
                case SKCodecResult.ErrorInInput:
                    warning = "Görsel dosyası eksik veya kısmen hatalı olabilir; bazı bölgeler boş görünebilir.";
                    break;
                default:
                    throw new ImageLoadException($"Görsel çözülemedi ({result}). Dosya bozuk olabilir.");
            }

            if (bitmap.Width != targetW || bitmap.Height != targetH)
            {
                var resizeInfo = new SKImageInfo(targetW, targetH, SKColorType.Bgra8888, SKAlphaType.Premul, srgb);
                SKBitmap resized = bitmap.Resize(resizeInfo, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                                   ?? throw new ImageLoadException("Görsel küçültülemedi.");
                bitmap.Dispose();
                bitmap = resized;
            }

            if (origin != SKEncodedOrigin.TopLeft)
            {
                SKBitmap oriented = ApplyOrigin(bitmap, origin);
                bitmap.Dispose();
                bitmap = oriented;
            }

            bool hasAlpha = encoded.AlphaType != SKAlphaType.Opaque && HasTransparentPixel(bitmap);
            if (scale < 1)
            {
                string note = $"Görsel {originalW}×{originalH} pikselden {bitmap.Width}×{bitmap.Height} piksele küçültülerek açıldı " +
                              "(etiket baskısı için fazlasıyla yeterli; orijinal dosya projede saklanır).";
                warning = warning is null ? note : warning + " " + note;
            }

            bitmap.SetImmutable();
            SKImage image = SKImage.FromBitmap(bitmap) ?? throw new ImageLoadException(UnsupportedMessage);
            return new LoadedImage(image, FormatName(codec.EncodedFormat), hasAlpha, originalW, originalH, warning);
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    /// <summary>EXIF yönünü uygular (kayıpsız 90° döndürme / aynalama).</summary>
    private static SKBitmap ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        int w = source.Width, h = source.Height;
        bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        // x' = ScaleX·x + SkewX·y + TransX ;  y' = SkewY·x + ScaleY·y + TransY
        SKMatrix matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity,
        };

        var info = new SKImageInfo(swap ? h : w, swap ? w : h, source.ColorType, source.AlphaType, source.ColorSpace);
        var result = new SKBitmap(info);
        using var canvas = new SKCanvas(result);
        using var pixmap = source.PeekPixels();
        using var image = SKImage.FromPixelCopy(pixmap);
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(matrix);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
        canvas.Flush();
        return result;
    }

    private static bool HasTransparentPixel(SKBitmap bitmap)
    {
        ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
        int rowBytes = bitmap.RowBytes;
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<byte> row = pixels.Slice(y * rowBytes, bitmap.Width * 4);
            for (int i = 3; i < row.Length; i += 4)
                if (row[i] != 255) return true;
        }
        return false;
    }

    private static string FormatName(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Png => "PNG",
        SKEncodedImageFormat.Jpeg => "JPEG",
        SKEncodedImageFormat.Bmp => "BMP",
        SKEncodedImageFormat.Gif => "GIF",
        SKEncodedImageFormat.Webp => "WEBP",
        SKEncodedImageFormat.Ico => "ICO",
        _ => format.ToString().ToUpperInvariant(),
    };

    /// <summary>İçerik "&lt;svg" ile başlayan bir XML/SVG metni mi? (UTF-8/UTF-16 BOM'ları dikkate alınır.)</summary>
    internal static bool LooksLikeSvg(byte[] data, string? fileNameHint)
    {
        string head;
        int length = Math.Min(data.Length, 4096);
        if (length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            head = Encoding.Unicode.GetString(data, 2, length - 2);
        else if (length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            head = Encoding.BigEndianUnicode.GetString(data, 2, length - 2);
        else
        {
            int start = length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
            head = Encoding.UTF8.GetString(data, start, length - start);
        }

        string trimmed = head.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '<')
            return false;
        if (head.Contains("<svg", StringComparison.OrdinalIgnoreCase)) return true;
        // Uzun yorum/DOCTYPE başlıklı dosyalar: uzantı .svg ise yine SVG olarak dene.
        return fileNameHint is not null &&
               string.Equals(Path.GetExtension(fileNameHint), ".svg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Svg.Skia ile SVG okuma. Dış varlık/entity/görsel çözümleme güvenlik için kapalıdır.</summary>
    private static class SvgLoader
    {
        private static readonly Lock Sync = new();
        private static bool _configured;

        public static LoadedImage Load(byte[] data)
        {
            lock (Sync)
            {
                EnsureSecureDefaults();
                SKPicture? source;
                using var svg = new SKSvg();
                svg.Settings.EnableJavaScript = false;
                svg.Settings.EnableExternalJavaScript = false;
                try
                {
                    using var stream = new MemoryStream(data, writable: false);
                    source = svg.Load(stream);
                }
                catch (Exception ex)
                {
                    throw new ImageLoadException("SVG dosyası okunamadı: " + ex.Message, ex);
                }

                if (source is null)
                    throw new ImageLoadException("SVG dosyası okunamadı. Dosya bozuk olabilir ya da desteklenmeyen SVG özellikleri içeriyor.");

                SKRect bounds = source.CullRect;
                if (!float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height) ||
                    bounds.Width < 0.5f || bounds.Height < 0.5f || bounds.Width > 100_000 || bounds.Height > 100_000)
                    throw new ImageLoadException("SVG boyutu belirlenemedi. Dosyada geçerli width/height ya da viewBox tanımlı olmalı.");

                bool hasExternalLinks = HasExternalLinks(data);
                if (!HasVisibleContent(source, bounds))
                {
                    throw new ImageLoadException(hasExternalLinks
                        ? "SVG yalnızca dış dosyalara bağlı görseller içeriyor. Güvenlik ve taşınabilirlik için dış " +
                          "bağlantılar yüklenmez; görseli SVG içine gömülü olarak kaydedin ya da PNG olarak ekleyin."
                        : "SVG dosyasında görünür bir içerik bulunamadı (dosya boş ya da tüm öğeler görünür alanın dışında).");
                }

                // SKSvg nesnesinden bağımsız, bize ait bir resim kaydı oluştur.
                using var recorder = new SKPictureRecorder();
                SKCanvas canvas = recorder.BeginRecording(bounds);
                canvas.DrawPicture(source);
                SKPicture owned = recorder.EndRecording();
                string? warning = hasExternalLinks
                    ? "SVG içindeki dış dosya bağlantıları (harici görseller) güvenlik ve taşınabilirlik için yüklenmedi."
                    : null;
                return new LoadedImage(owned, bounds, "SVG", warning);
            }
        }

        /// <summary>href değeri "data:" veya "#" ile başlamayan (dış kaynağa işaret eden) bağlantı var mı?</summary>
        private static bool HasExternalLinks(byte[] data)
        {
            string text = Encoding.UTF8.GetString(data);
            int index = 0;
            while ((index = text.IndexOf("href", index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int i = index + 4;
                index = i;
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i >= text.Length || text[i] != '=') continue;
                i++;
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i >= text.Length || (text[i] != '"' && text[i] != '\'')) continue;
                i++;
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i >= text.Length) break;
                bool internalLink = text[i] == '#' ||
                                    text.AsSpan(i).StartsWith("data:", StringComparison.OrdinalIgnoreCase);
                if (!internalLink) return true;
            }
            return false;
        }

        /// <summary>SVG'yi küçük bir yoklama tuvaline çizip en az bir pikselin boyandığını doğrular.</summary>
        private static bool HasVisibleContent(SKPicture picture, SKRect bounds)
        {
            const int Probe = 128;
            float scale = Probe / Math.Max(bounds.Width, bounds.Height);
            int w = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
            int h = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));
            using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(scale);
                canvas.Translate(-bounds.Left, -bounds.Top);
                canvas.DrawPicture(picture);
                canvas.Flush();
            }
            ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
            for (int y = 0; y < h; y++)
            {
                ReadOnlySpan<byte> row = pixels.Slice(y * bitmap.RowBytes, w * 4);
                for (int i = 3; i < row.Length; i += 4)
                    if (row[i] != 0) return true;
            }
            return false;
        }

        private static void EnsureSecureDefaults()
        {
            if (_configured) return;
            global::Svg.SvgDocument.ResolveExternalXmlEntites = global::Svg.ExternalType.None;
            global::Svg.SvgDocument.ResolveExternalImages = global::Svg.ExternalType.None;
            global::Svg.SvgDocument.ResolveExternalElements = global::Svg.ExternalType.None;
            _configured = true;
        }
    }
}
