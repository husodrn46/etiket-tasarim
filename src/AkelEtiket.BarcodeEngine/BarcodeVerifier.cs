using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;
using ZXing;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.BarcodeEngine;

/// <summary>Okunabilirlik kontrolü sonucu.</summary>
public sealed record BarcodeVerificationResult(bool Success, string? DecodedText, string Message)
{
    /// <summary>Okuyucunun bildirdiği AIM sembol tanımlayıcısı (ör. "]d2" GS1 DataMatrix, "]Q3" GS1 QR).</summary>
    public string? SymbologyIdentifier { get; init; }
}

/// <summary>
/// Baskı öncesi okunabilirlik kontrolü: yazıcı çözünürlüğünde üretilmiş GERÇEK 1-bit görüntüyü
/// bağımsız bir çözücüyle (ZXing okuyucu) okur ve içeriği beklenen değerle karşılaştırır.
/// Bu kontrol fiziksel baskı/tarayıcı testinin yerini tutmaz; render hatalarını yakalar.
/// </summary>
public static class BarcodeVerifier
{
    private const int Padding = 32;

    public static BarcodeFormat ToZxingFormat(BarcodeType type) => type switch
    {
        BarcodeType.Ean13 => BarcodeFormat.EAN_13,
        BarcodeType.Ean8 => BarcodeFormat.EAN_8,
        BarcodeType.UpcA => BarcodeFormat.UPC_A,
        BarcodeType.Code128 or BarcodeType.Gs1_128 => BarcodeFormat.CODE_128,
        BarcodeType.Code39 => BarcodeFormat.CODE_39,
        BarcodeType.Itf or BarcodeType.Itf14 => BarcodeFormat.ITF,
        BarcodeType.QrCode or BarcodeType.Gs1QrCode => BarcodeFormat.QR_CODE,
        BarcodeType.DataMatrix or BarcodeType.Gs1DataMatrix => BarcodeFormat.DATA_MATRIX,
        BarcodeType.Pdf417 => BarcodeFormat.PDF_417,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>Görüntüdeki barkodu çözer ve <paramref name="expectedText"/> ile karşılaştırır.</summary>
    public static BarcodeVerificationResult Verify(MonoBitmap image, BarcodeType type, string expectedText)
    {
        ArgumentNullException.ThrowIfNull(image);
        int w = image.Width + 2 * Padding, h = image.Height + 2 * Padding;
        var gray = new byte[w * h];
        Array.Fill(gray, (byte)255);
        for (int y = 0; y < image.Height; y++)
        {
            int row = (y + Padding) * w + Padding;
            for (int x = 0; x < image.Width; x++)
                if (image.GetPixel(x, y)) gray[row + x] = 0;
        }

        var result = TryDecode(gray, w, h, type, expectedText, pure: false)
                     ?? (BarcodeElement.IsTwoDimensional(type) ? TryDecode(gray, w, h, type, expectedText, pure: true) : null);
        if (result?.Text is not { } decoded)
            return new BarcodeVerificationResult(false, null,
                L10n.T("Barkod çözülemedi. Barkodu büyütün, modül genişliğini artırın veya üstüne binen nesneleri kaldırın."));
        string? symbology = result.ResultMetadata is { } meta && meta.TryGetValue(ResultMetadataType.SYMBOLOGY_IDENTIFIER, out var id)
            ? id?.ToString()
            : null;

        if (type is BarcodeType.Gs1DataMatrix or BarcodeType.Gs1QrCode)
        {
            // GS1 2B: sembolde FNC1 (ilk konumda) bulunmalı; yoksa okuyucu veriyi GS1 olarak değil düz metin olarak verir.
            string required = type == BarcodeType.Gs1DataMatrix ? "]d2" : "]Q3";
            if (symbology != required)
                return new BarcodeVerificationResult(false, decoded,
                    L10n.T($"Barkod okundu ama GS1 işareti (başta FNC1) bulunamadı (okuyucu: {symbology ?? "?"}, beklenen {required})."))
                {
                    SymbologyIdentifier = symbology,
                };
            // ZXing.Net, DataMatrix'te ilk konumdaki FNC1'i metnin başına GS olarak da yazıyor; sembol tanımlayıcısı bunu zaten bildirir.
            if (type == BarcodeType.Gs1DataMatrix && decoded.StartsWith('\u001d')) decoded = decoded[1..];
        }

        if (Matches(type, decoded, expectedText))
            return new BarcodeVerificationResult(true, decoded, L10n.T("Okunabilirlik kontrolü başarılı: barkod çözüldü ve içerik eşleşti."))
            {
                SymbologyIdentifier = symbology,
            };

        return new BarcodeVerificationResult(false, decoded,
            L10n.T($"Çözülen içerik beklenenden farklı: \"{Printable(decoded)}\" (beklenen \"{Printable(expectedText)}\")."))
        {
            SymbologyIdentifier = symbology,
        };
    }

    private static Result? TryDecode(byte[] gray, int w, int h, BarcodeType type, string expected, bool pure)
    {
        var reader = new BarcodeReaderGeneric { AutoRotate = true };
        reader.Options.TryInverted = false;
        reader.Options.TryHarder = true;
        reader.Options.PureBarcode = pure;
        reader.Options.PossibleFormats = [ToZxingFormat(type)];
        if (type == BarcodeType.Gs1_128) reader.Options.AssumeGS1 = true;
        if (type is BarcodeType.Itf or BarcodeType.Itf14) reader.Options.AllowedLengths = [expected.Length];
        try
        {
            return reader.Decode(new RGBLuminanceSource(gray, w, h, RGBLuminanceSource.BitmapFormat.Gray8));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static bool Matches(BarcodeType type, string decoded, string expected)
    {
        if (string.Equals(decoded, expected, StringComparison.Ordinal)) return true;
        // Bazı okuyucular UPC-A'yı başında 0 olan EAN-13 olarak döndürebilir.
        if (type == BarcodeType.UpcA && decoded.Length == 13 && decoded[0] == '0' && decoded[1..] == expected) return true;
        // ZXing okuyucusu, Code 128 kontrol karakterinin değeri 102 (FNC1 ile aynı kod) olduğunda GS1 kipinde
        // sona fazladan GS ekler. GS1-128 asla FNC1 ile bitmediği için bu tek GS göz ardı edilebilir.
        if (type == BarcodeType.Gs1_128 && decoded.Length == expected.Length + 1 && decoded[^1] == '\u001d' &&
            decoded.StartsWith(expected, StringComparison.Ordinal)) return true;
        return false;
    }

    private static string Printable(string s) => s.Replace("\u001d", "<GS>");
}
