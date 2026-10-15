using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using AkelEtiket.Core.Imaging;
using SkiaSharp;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Rendering;

/// <summary>1-bit bitmap'leri PNG'ye ve ekran piksellerine çevirir.</summary>
public static class MonoImageExport
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>
    /// PNG olarak kodlar ve pHYs (fiziksel çözünürlük) bilgisini ekler; böylece başka bir programda
    /// açıldığında etiket gerçek ölçüsünde (ör. 100 × 100 mm) görünür/basılır.
    /// </summary>
    public static byte[] EncodePng(MonoBitmap bitmap, double dpi)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        using var skBitmap = new SKBitmap(info);
        byte[] gray = bitmap.ToGray8();
        IntPtr pixels = skBitmap.GetPixels();
        int rowBytes = skBitmap.RowBytes;
        for (int y = 0; y < bitmap.Height; y++)
            Marshal.Copy(gray, y * bitmap.Width, pixels + y * rowBytes, bitmap.Width);
        using var data = skBitmap.Encode(SKEncodedImageFormat.Png, 100)
                         ?? throw new InvalidOperationException(L10n.T("PNG kodlanamadı."));
        return InsertPhysChunk(data.ToArray(), dpi);
    }

    /// <summary>
    /// Ekran için BGRA32 pikseller: siyah mürekkep → opak siyah, örtülen beyaz → opak beyaz, örtülmeyen → saydam.
    /// </summary>
    public static byte[] ToBgra32(MonoBitmap ink, MonoBitmap? mask)
    {
        int w = ink.Width, h = ink.Height;
        var result = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int rowIn = y * ink.Stride;
            int rowOut = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int bit = 0x80 >> (x & 7);
                int byteIndex = rowIn + (x >> 3);
                bool covered = mask is null || (mask.Data[byteIndex] & bit) != 0;
                if (!covered) continue;
                int o = rowOut + x * 4;
                byte v = (ink.Data[byteIndex] & bit) != 0 ? (byte)0 : (byte)255;
                result[o] = v;
                result[o + 1] = v;
                result[o + 2] = v;
                result[o + 3] = 255;
            }
        }
        return result;
    }

    internal static byte[] InsertPhysChunk(byte[] png, double dpi)
    {
        // İmza (8) + IHDR (4 uzunluk + 4 tür + 13 veri + 4 CRC) = 33 bayt; pHYs IHDR'den hemen sonra gelir.
        const int insertAt = 33;
        if (png.Length < insertAt || Encoding.ASCII.GetString(png, 12, 4) != "IHDR") return png;
        uint pixelsPerMeter = (uint)Math.Round(dpi / 0.0254);
        var chunk = new byte[4 + 4 + 9 + 4];
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(0), 9);
        Encoding.ASCII.GetBytes("pHYs").CopyTo(chunk, 4);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8), pixelsPerMeter);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(12), pixelsPerMeter);
        chunk[16] = 1; // birim: metre
        uint crc = Crc32(chunk.AsSpan(4, 4 + 9));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(17), crc);

        var result = new byte[png.Length + chunk.Length];
        Buffer.BlockCopy(png, 0, result, 0, insertAt);
        Buffer.BlockCopy(chunk, 0, result, insertAt, chunk.Length);
        Buffer.BlockCopy(png, insertAt, result, insertAt + chunk.Length, png.Length - insertAt);
        return result;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
