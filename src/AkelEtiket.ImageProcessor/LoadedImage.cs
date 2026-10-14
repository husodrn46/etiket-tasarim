using SkiaSharp;

namespace AkelEtiket.ImageProcessor;

/// <summary>
/// Belleğe açılmış kaynak görsel (raster ya da SVG vektör). Değişmezdir; aynı nesne birden çok
/// iş parçacığından aynı anda farklı tuvallere çizilebilir. Orijinal dosya baytları proje varlık
/// deposunda kalır; bu nesne yalnızca çizim için çözülmüş hâlidir.
/// </summary>
public sealed class LoadedImage : IDisposable
{
    private readonly SKImage? _raster;
    private readonly SKPicture? _picture;
    private readonly SKRect _pictureBounds;
    private volatile bool _disposed;

    internal LoadedImage(SKImage raster, string formatName, bool hasAlpha, int originalWidth, int originalHeight, string? warning)
    {
        _raster = raster ?? throw new ArgumentNullException(nameof(raster));
        Width = raster.Width;
        Height = raster.Height;
        FormatName = formatName;
        HasAlpha = hasAlpha;
        OriginalWidth = originalWidth;
        OriginalHeight = originalHeight;
        Warning = warning;
    }

    internal LoadedImage(SKPicture picture, SKRect bounds, string formatName, string? warning = null)
    {
        _picture = picture ?? throw new ArgumentNullException(nameof(picture));
        _pictureBounds = bounds;
        Width = Math.Max(1, (int)Math.Ceiling(bounds.Width - 0.001f));
        Height = Math.Max(1, (int)Math.Ceiling(bounds.Height - 0.001f));
        FormatName = formatName;
        HasAlpha = true;
        OriginalWidth = Width;
        OriginalHeight = Height;
        Warning = warning;
    }

    /// <summary>Çizim için kullanılan genişlik (piksel; EXIF yönü uygulanmış, gerekirse küçültülmüş).</summary>
    public int Width { get; }

    /// <summary>Çizim için kullanılan yükseklik (piksel).</summary>
    public int Height { get; }

    /// <summary>Dosyadaki özgün piksel genişliği (EXIF yönü uygulanmış, küçültme öncesi).</summary>
    public int OriginalWidth { get; }

    /// <summary>Dosyadaki özgün piksel yüksekliği.</summary>
    public int OriginalHeight { get; }

    /// <summary>Yükleme sırasında boyut sınırı nedeniyle küçültüldü mü?</summary>
    public bool WasDownsampled => OriginalWidth != Width || OriginalHeight != Height;

    /// <summary>SVG gibi vektör kaynak mı? (Her ölçekte keskin çizilir.)</summary>
    public bool IsVector => _picture is not null;

    /// <summary>Görselde saydam (alfa &lt; 255) piksel var mı? Saydam alanlar baskıda beyaz kabul edilir.</summary>
    public bool HasAlpha { get; }

    /// <summary>"PNG", "JPEG", "BMP", "GIF", "WEBP", "SVG" ...</summary>
    public string FormatName { get; }

    /// <summary>Kullanıcıya gösterilebilecek uyarı (ör. küçültme bilgisi); yoksa null.</summary>
    public string? Warning { get; }

    /// <summary>
    /// Kaynak bölgeyi (<paramref name="sourceRect"/>, kaynak piksel koordinatı; 0…Width × 0…Height)
    /// hedef dikdörtgene çizer. Küçültmede mipmap (üç doğrusal), büyütmede kübik (Mitchell)
    /// örnekleme kullanılır; vektörler hedef çözünürlükte keskin çizilir. Tuvalin mevcut dönüşümü
    /// (ör. döndürme) hesaba katılır. Birden çok iş parçacığından eşzamanlı çağrılabilir.
    /// </summary>
    public void Draw(SKCanvas canvas, SKRect sourceRect, SKRect destRect)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(canvas);
        if (!(sourceRect.Width > 0) || !(sourceRect.Height > 0) || !(destRect.Width > 0) || !(destRect.Height > 0))
            return;

        if (_raster is not null)
        {
            canvas.DrawImage(_raster, sourceRect, destRect, ChooseSampling(canvas.TotalMatrix, sourceRect, destRect), null);
            return;
        }

        float sx = destRect.Width / sourceRect.Width;
        float sy = destRect.Height / sourceRect.Height;
        int saveCount = canvas.Save();
        try
        {
            canvas.ClipRect(destRect, SKClipOperation.Intersect, antialias: true);
            canvas.Translate(destRect.Left, destRect.Top);
            canvas.Scale(sx, sy);
            canvas.Translate(-(sourceRect.Left + _pictureBounds.Left), -(sourceRect.Top + _pictureBounds.Top));
            canvas.DrawPicture(_picture!);
        }
        finally
        {
            canvas.RestoreToCount(saveCount);
        }
    }

    /// <summary>Tüm görseli hedef dikdörtgene çizer.</summary>
    public void Draw(SKCanvas canvas, SKRect destRect) => Draw(canvas, new SKRect(0, 0, Width, Height), destRect);

    private static SKSamplingOptions ChooseSampling(SKMatrix total, SKRect source, SKRect dest)
    {
        double matrixScaleX = Math.Sqrt(total.ScaleX * total.ScaleX + total.SkewY * total.SkewY);
        double matrixScaleY = Math.Sqrt(total.SkewX * total.SkewX + total.ScaleY * total.ScaleY);
        double effX = matrixScaleX * dest.Width / source.Width;
        double effY = matrixScaleY * dest.Height / source.Height;

        if (Math.Min(effX, effY) < 0.999)
            return new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        if (Math.Abs(effX - 1) < 0.001 && Math.Abs(effY - 1) < 0.001)
            return new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
        return new SKSamplingOptions(SKCubicResampler.Mitchell);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _raster?.Dispose();
        _picture?.Dispose();
    }

    public override string ToString() => $"{FormatName} {Width}×{Height}{(IsVector ? " (vektör)" : "")}";
}
