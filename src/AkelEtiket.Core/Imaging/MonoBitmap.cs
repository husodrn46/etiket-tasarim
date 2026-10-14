using System.Numerics;

namespace AkelEtiket.Core.Imaging;

/// <summary>
/// Sıkıştırılmış 1-bit bitmap. Satır sıralı, her satır <see cref="Stride"/> bayttır.
/// Her baytta en anlamlı bit (MSB) en soldaki pikseldir. Bit = 1 → SİYAH (mürekkep), 0 → BEYAZ.
/// Satır sonundaki dolgu bitleri her zaman 0 (beyaz) tutulur.
/// Not: Yazıcı dillerinin kutupluluğu farklı olabilir; dönüşüm baskı motorunda yapılır.
/// </summary>
public sealed class MonoBitmap
{
    public MonoBitmap(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Genişlik sıfırdan büyük olmalı.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Yükseklik sıfırdan büyük olmalı.");
        Width = width;
        Height = height;
        Stride = (width + 7) >> 3;
        Data = new byte[checked(Stride * height)];
    }

    public MonoBitmap(int width, int height, byte[] data)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(data);
        Width = width;
        Height = height;
        Stride = (width + 7) >> 3;
        if (data.Length != Stride * height)
            throw new ArgumentException($"Veri uzunluğu {Stride * height} bayt olmalı, {data.Length} bayt verildi.", nameof(data));
        Data = data;
        ClearPaddingBits();
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Satır başına bayt sayısı.</summary>
    public int Stride { get; }

    /// <summary>Ham veri (bit 1 = siyah).</summary>
    public byte[] Data { get; }

    public bool this[int x, int y]
    {
        get => GetPixel(x, y);
        set => SetPixel(x, y, value);
    }

    public bool GetPixel(int x, int y) => (Data[y * Stride + (x >> 3)] & (0x80 >> (x & 7))) != 0;

    public void SetPixel(int x, int y, bool black)
    {
        int index = y * Stride + (x >> 3);
        byte mask = (byte)(0x80 >> (x & 7));
        if (black) Data[index] |= mask;
        else Data[index] &= (byte)~mask;
    }

    public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public void Clear() => Array.Clear(Data);

    public void Fill(bool black)
    {
        if (!black)
        {
            Clear();
            return;
        }
        Array.Fill(Data, (byte)0xFF);
        ClearPaddingBits();
    }

    /// <summary>Dikdörtgen doldurur; tuval dışına taşan kısım kırpılır.</summary>
    public void FillRect(int x, int y, int width, int height, bool black)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(Width, x + width), y1 = Math.Min(Height, y + height);
        if (x0 >= x1 || y0 >= y1) return;

        int firstByte = x0 >> 3, lastByte = (x1 - 1) >> 3;
        byte firstMask = (byte)(0xFF >> (x0 & 7));
        byte lastMask = (byte)(0xFF << (7 - ((x1 - 1) & 7)));
        for (int row = y0; row < y1; row++)
        {
            int rowStart = row * Stride;
            if (firstByte == lastByte)
            {
                Apply(rowStart + firstByte, (byte)(firstMask & lastMask), black);
                continue;
            }
            Apply(rowStart + firstByte, firstMask, black);
            for (int b = firstByte + 1; b < lastByte; b++) Data[rowStart + b] = black ? (byte)0xFF : (byte)0;
            Apply(rowStart + lastByte, lastMask, black);
        }
    }

    private void Apply(int index, byte mask, bool black)
    {
        if (black) Data[index] |= mask;
        else Data[index] &= (byte)~mask;
    }

    /// <summary>Siyah piksel sayısı.</summary>
    public int CountBlack()
    {
        int count = 0;
        var span = Data.AsSpan();
        int i = 0;
        for (; i + 8 <= span.Length; i += 8)
            count += BitOperations.PopCount(BitConverter.ToUInt64(span.Slice(i, 8)));
        for (; i < span.Length; i++)
            count += BitOperations.PopCount(span[i]);
        return count;
    }

    public MonoBitmap Clone() => new(Width, Height, (byte[])Data.Clone());

    /// <summary>Tüm pikselleri ters çevirir (siyah ↔ beyaz).</summary>
    public void Invert()
    {
        for (int i = 0; i < Data.Length; i++) Data[i] = (byte)~Data[i];
        ClearPaddingBits();
    }

    /// <summary>Belirtilen bölgeyi yeni bitmap olarak keser. Taşan alanlar beyaz olur.</summary>
    public MonoBitmap Crop(int x, int y, int width, int height)
    {
        var result = new MonoBitmap(width, height);
        result.DrawBitmap(this, -x, -y);
        return result;
    }

    /// <summary>
    /// Bitmap'i saat yönünde 90°'nin katları kadar kayıpsız döndürür.
    /// </summary>
    public MonoBitmap RotateQuarterTurns(int quarterTurnsClockwise)
    {
        int q = ((quarterTurnsClockwise % 4) + 4) % 4;
        if (q == 0) return Clone();
        int newW = q == 2 ? Width : Height;
        int newH = q == 2 ? Height : Width;
        var result = new MonoBitmap(newW, newH);
        for (int y = 0; y < Height; y++)
        {
            int rowStart = y * Stride;
            for (int xb = 0; xb < Stride; xb++)
            {
                byte b = Data[rowStart + xb];
                if (b == 0) continue;
                int baseX = xb << 3;
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((b & (0x80 >> bit)) == 0) continue;
                    int x = baseX + bit;
                    if (x >= Width) break;
                    switch (q)
                    {
                        case 1: result.SetPixel(Height - 1 - y, x, true); break;
                        case 2: result.SetPixel(Width - 1 - x, Height - 1 - y, true); break;
                        default: result.SetPixel(y, Width - 1 - x, true); break;
                    }
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Başka bir bitmap'i (dx, dy) konumuna kopyalar. <paramref name="mask"/> verilirse yalnızca
    /// maskede 1 olan pikseller yazılır; null ise kaynak dikdörtgenin tamamı opak kabul edilir.
    /// </summary>
    public void DrawBitmap(MonoBitmap source, int dx, int dy, MonoBitmap? mask = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (mask is not null && (mask.Width != source.Width || mask.Height != source.Height))
            throw new ArgumentException("Maske ve kaynak aynı boyutta olmalı.", nameof(mask));

        int shift = dx & 7;              // negatif dx için de doğru (aritmetik)
        int destByteBase = dx >> 3;      // aritmetik kaydırma = aşağı yuvarlayan bölme
        byte lastSourceMask = (byte)(0xFF << ((source.Stride << 3) - source.Width));

        for (int sy = 0; sy < source.Height; sy++)
        {
            int ty = sy + dy;
            if ((uint)ty >= (uint)Height) continue;
            int srcRow = sy * source.Stride;
            int dstRow = ty * Stride;
            for (int sb = 0; sb < source.Stride; sb++)
            {
                byte ink = source.Data[srcRow + sb];
                byte m = mask is null
                    ? (sb == source.Stride - 1 ? lastSourceMask : (byte)0xFF)
                    : mask.Data[srcRow + sb];
                if (m == 0) continue;
                ink &= m;

                int d0 = destByteBase + sb;
                if (shift == 0)
                {
                    if ((uint)d0 < (uint)Stride)
                        Data[dstRow + d0] = (byte)((Data[dstRow + d0] & ~m) | ink);
                    continue;
                }

                byte m0 = (byte)(m >> shift), i0 = (byte)(ink >> shift);
                byte m1 = (byte)(m << (8 - shift)), i1 = (byte)(ink << (8 - shift));
                if (m0 != 0 && (uint)d0 < (uint)Stride)
                    Data[dstRow + d0] = (byte)((Data[dstRow + d0] & ~m0) | i0);
                int d1 = d0 + 1;
                if (m1 != 0 && (uint)d1 < (uint)Stride)
                    Data[dstRow + d1] = (byte)((Data[dstRow + d1] & ~m1) | i1);
            }
        }
        ClearPaddingBits();
    }

    /// <summary>Siyah piksellerin sınır kutusu; hiç siyah yoksa null.</summary>
    public (int X, int Y, int Width, int Height)? GetInkBounds()
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < Height; y++)
        {
            int rowStart = y * Stride;
            for (int xb = 0; xb < Stride; xb++)
            {
                byte b = Data[rowStart + xb];
                if (b == 0) continue;
                int left = (xb << 3) + BitOperations.LeadingZeroCount((uint)b) - 24;
                int right = (xb << 3) + 7 - BitOperations.TrailingZeroCount(b);
                if (left < minX) minX = left;
                if (right > maxX) maxX = right;
                if (y < minY) minY = y;
                maxY = y;
            }
        }
        return maxX < 0 ? null : (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>Gri tonlu (0 = siyah, 255 = beyaz) bayt dizisine çevirir.</summary>
    public byte[] ToGray8()
    {
        var gray = new byte[Width * Height];
        for (int y = 0; y < Height; y++)
        {
            int rowStart = y * Stride;
            int outRow = y * Width;
            for (int x = 0; x < Width; x++)
                gray[outRow + x] = (Data[rowStart + (x >> 3)] & (0x80 >> (x & 7))) != 0 ? (byte)0 : (byte)255;
        }
        return gray;
    }

    /// <summary>Gri tonlu görüntüden eşikleme ile oluşturur: değer &lt; eşik → siyah.</summary>
    public static MonoBitmap FromGray8(ReadOnlySpan<byte> gray, int width, int height, int threshold = 128)
    {
        if (gray.Length < width * height) throw new ArgumentException("Gri veri boyutu yetersiz.", nameof(gray));
        var result = new MonoBitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            int inRow = y * width;
            int outRow = y * result.Stride;
            for (int x = 0; x < width; x++)
                if (gray[inRow + x] < threshold)
                    result.Data[outRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
        }
        return result;
    }

    public bool ContentEquals(MonoBitmap other) =>
        other is not null && other.Width == Width && other.Height == Height && Data.AsSpan().SequenceEqual(other.Data);

    /// <summary>Satır sonundaki kullanılmayan bitleri 0 yapar.</summary>
    public void ClearPaddingBits()
    {
        int pad = (Stride << 3) - Width;
        if (pad == 0) return;
        byte keep = (byte)(0xFF << pad);
        for (int row = 0; row < Height; row++)
            Data[row * Stride + Stride - 1] &= keep;
    }

    public override string ToString() => $"MonoBitmap {Width}×{Height} ({Stride} bayt/satır)";
}
