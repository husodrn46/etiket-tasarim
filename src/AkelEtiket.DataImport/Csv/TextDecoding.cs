using System.Text;

namespace AkelEtiket.DataImport.Csv;

/// <summary>
/// CSV metin kodlaması algılama: BOM (UTF-8 / UTF-16 / UTF-32) → BOM'suz UTF-16 sezgisi →
/// katı UTF-8 → Windows-1254 (Türkçe ANSI). Türkçe Excel'in "CSV (virgülle ayrılmış)" çıktısı
/// Windows-1254 olduğu için son çare budur.
/// </summary>
internal static class TextDecoding
{
    static TextDecoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Encoding Windows1254 => Encoding.GetEncoding(1254);

    public static (string Text, string EncodingName) Decode(byte[] bytes, Encoding? forced)
    {
        if (forced is not null)
        {
            byte[] preamble = forced.GetPreamble();
            int skip = preamble.Length > 0 && StartsWith(bytes, preamble) ? preamble.Length : 0;
            return (forced.GetString(bytes, skip, bytes.Length - skip), FriendlyName(forced));
        }

        if (StartsWith(bytes, [0xEF, 0xBB, 0xBF]))
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "UTF-8");
        if (StartsWith(bytes, [0xFF, 0xFE, 0x00, 0x00]))
            return (Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4), "UTF-32 LE");
        if (StartsWith(bytes, [0xFF, 0xFE]))
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 LE");
        if (StartsWith(bytes, [0xFE, 0xFF]))
            return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 BE");

        if (LooksLikeUtf16WithoutBom(bytes, out bool bigEndian))
        {
            return bigEndian
                ? (Encoding.BigEndianUnicode.GetString(bytes), "UTF-16 BE")
                : (Encoding.Unicode.GetString(bytes), "UTF-16 LE");
        }

        try
        {
            var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return (strictUtf8.GetString(bytes), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            return (Windows1254.GetString(bytes), FriendlyName(Windows1254));
        }
    }

    public static string FriendlyName(Encoding encoding) => encoding.CodePage switch
    {
        65001 => "UTF-8",
        1254 => "Windows-1254 (Türkçe)",
        28599 => "ISO-8859-9 (Türkçe)",
        857 => "DOS-857 (Türkçe)",
        1200 => "UTF-16 LE",
        1201 => "UTF-16 BE",
        12000 => "UTF-32 LE",
        _ => encoding.WebName.ToUpperInvariant(),
    };

    private static bool StartsWith(byte[] bytes, ReadOnlySpan<byte> prefix) =>
        bytes.Length >= prefix.Length && bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    /// <summary>BOM'suz UTF-16: ASCII ağırlıklı metinde her iki bayttan biri 0 olur.</summary>
    private static bool LooksLikeUtf16WithoutBom(byte[] bytes, out bool bigEndian)
    {
        bigEndian = false;
        int length = Math.Min(bytes.Length, 4096) & ~1;
        if (length < 4) return false;
        int evenZeros = 0, oddZeros = 0;
        for (int i = 0; i < length; i += 2)
        {
            if (bytes[i] == 0) evenZeros++;
            if (bytes[i + 1] == 0) oddZeros++;
        }
        int pairs = length / 2;
        if (oddZeros >= pairs * 0.4 && evenZeros < pairs * 0.05) return true;
        if (evenZeros >= pairs * 0.4 && oddZeros < pairs * 0.05)
        {
            bigEndian = true;
            return true;
        }
        return false;
    }
}
