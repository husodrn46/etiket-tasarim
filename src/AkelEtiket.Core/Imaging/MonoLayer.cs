namespace AkelEtiket.Core.Imaging;

/// <summary>
/// Bir tasarım öğesinin yazıcı çözünürlüğündeki 1-bit katmanı.
/// <see cref="Ink"/>: siyah pikseller. <see cref="Mask"/>: öğenin "boyadığı" (örttüğü) pikseller;
/// maskede 1 olan yerde alttaki öğe tamamen örtülür (beyaz da bir boya rengidir).
/// Mask null ise katman dikdörtgeninin tamamı opaktır.
/// Ekran önizlemesi ve baskı aynı katmanları kullanır; bu yüzden ikisi birebir tutarlıdır.
/// </summary>
public sealed class MonoLayer
{
    public MonoLayer(MonoBitmap ink, MonoBitmap? mask, int offsetX, int offsetY)
    {
        ArgumentNullException.ThrowIfNull(ink);
        if (mask is not null && (mask.Width != ink.Width || mask.Height != ink.Height))
            throw new ArgumentException("Maske ve mürekkep katmanı aynı boyutta olmalı.", nameof(mask));
        Ink = ink;
        Mask = mask;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public MonoBitmap Ink { get; }
    public MonoBitmap? Mask { get; }

    /// <summary>Katmanın etiket üzerindeki sol üst köşesi (yazıcı noktası).</summary>
    public int OffsetX { get; }

    /// <summary>Katmanın etiket üzerindeki sol üst köşesi (yazıcı noktası).</summary>
    public int OffsetY { get; }

    public int Width => Ink.Width;
    public int Height => Ink.Height;

    /// <summary>Katmanı hedef etiket bitmap'ine (z-sırasına göre üstüne) yazar.</summary>
    public void CompositeOnto(MonoBitmap target) => target.DrawBitmap(Ink, OffsetX, OffsetY, Mask);

    public bool IsOpaqueAt(int x, int y) => Mask is null || Mask.GetPixel(x, y);

    public MonoLayer WithOffset(int offsetX, int offsetY) => new(Ink, Mask, offsetX, offsetY);
}
