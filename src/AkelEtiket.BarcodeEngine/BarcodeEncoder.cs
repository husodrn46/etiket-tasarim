using AkelEtiket.Core.Model;
using ZXing;
using ZXing.Common;
using ZXing.Datamatrix;
using ZXing.Datamatrix.Encoder;
using ZXing.OneD;
using ZXing.PDF417;
using ZXing.PDF417.Internal;
using ZXing.QrCode;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.BarcodeEngine;

/// <summary>Kodlama sonucu: doğrulama + (başarılıysa) sembol.</summary>
public sealed class BarcodeEncodeResult
{
    internal BarcodeEncodeResult(BarcodeValidationResult validation, BarcodeSymbol? symbol, string? error)
    {
        Validation = validation;
        Symbol = symbol;
        Error = error;
    }

    public BarcodeValidationResult Validation { get; }
    public BarcodeSymbol? Symbol { get; }
    public bool Success => Symbol is not null;

    /// <summary>Türkçe hata mesajı (başarısızsa).</summary>
    public string? Error { get; }
}

/// <summary>
/// Doğrulanmış içeriği ZXing.Net kodlayıcılarıyla modül desenine çevirir.
/// Görüntü üretmez; yazıcı noktasına hizalı çizim <see cref="BarcodeLayoutEngine"/> ile yapılır.
/// </summary>
public static class BarcodeEncoder
{
    public static BarcodeEncodeResult Encode(BarcodeType type, string? content, BarcodeSettings? settings = null)
    {
        settings ??= BarcodeSettings.Default;
        var validation = BarcodeValidator.Validate(type, content, settings);
        if (!validation.IsValid)
            return new BarcodeEncodeResult(validation, null, validation.FirstError);
        try
        {
            BarcodeSymbol symbol = type switch
            {
                BarcodeType.Ean13 => EncodeEan13(validation),
                BarcodeType.Ean8 => EncodeEan8(validation),
                BarcodeType.UpcA => EncodeUpcA(validation),
                BarcodeType.Code128 or BarcodeType.Gs1_128 => EncodeCode128(type, validation),
                BarcodeType.Code39 => EncodeLinear(type, validation, new Code39Writer().encode(validation.EncoderData), true, settings, 10),
                BarcodeType.Itf => EncodeLinear(type, validation, new ITFWriter().encode(validation.EncoderData), true, settings, 10),
                BarcodeType.Itf14 => EncodeItf14(validation, settings),
                BarcodeType.QrCode => EncodeQr(validation, settings),
                BarcodeType.DataMatrix => EncodeDataMatrix(validation, settings),
                BarcodeType.Pdf417 => EncodePdf417(validation, settings),
                BarcodeType.Gs1DataMatrix => EncodeGs1DataMatrix(validation, settings),
                BarcodeType.Gs1QrCode => EncodeGs1Qr(validation, settings),
                _ => throw new NotSupportedException(),
            };
            return new BarcodeEncodeResult(validation, symbol, null);
        }
        catch (Exception ex) when (ex is WriterException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            string message = ex.Message.Contains("too big", StringComparison.OrdinalIgnoreCase) ||
                             ex.Message.Contains("data bits", StringComparison.OrdinalIgnoreCase) ||
                             ex.Message.Contains("capacity", StringComparison.OrdinalIgnoreCase) ||
                             ex.Message.Contains("symbol arrangement", StringComparison.OrdinalIgnoreCase) ||
                             ex.Message.Contains("kapasite", StringComparison.OrdinalIgnoreCase)
                ? L10n.T("İçerik bu barkod türünün kapasitesini aşıyor; içeriği kısaltın veya hata düzeltme seviyesini düşürün.")
                : L10n.T($"Barkod oluşturulamadı: {ex.Message}");
            validation.Errors.Add(message);
            return new BarcodeEncodeResult(validation, null, message);
        }
    }

    private static LinearSymbol EncodeEan13(BarcodeValidationResult v)
    {
        bool[] modules = new EAN13Writer().encode(v.EncoderData);
        string d = v.EncoderData;
        var segments = new List<HriSegment> { new(d[..1], -8, -1, false) };
        for (int i = 1; i <= 6; i++) segments.Add(new HriSegment(d.Substring(i, 1), 3 + 7 * (i - 1), 3 + 7 * i, false));
        for (int i = 7; i <= 12; i++) segments.Add(new HriSegment(d.Substring(i, 1), 50 + 7 * (i - 7), 50 + 7 * (i - 6), false));
        return new LinearSymbol(BarcodeType.Ean13, v, modules, false, 1, 11,
            [(0, 3), (45, 50), (92, 95)], segments, false);
    }

    private static LinearSymbol EncodeEan8(BarcodeValidationResult v)
    {
        bool[] modules = new EAN8Writer().encode(v.EncoderData);
        string d = v.EncoderData;
        var segments = new List<HriSegment>();
        for (int i = 0; i < 4; i++) segments.Add(new HriSegment(d.Substring(i, 1), 3 + 7 * i, 10 + 7 * i, false));
        for (int i = 4; i < 8; i++) segments.Add(new HriSegment(d.Substring(i, 1), 36 + 7 * (i - 4), 43 + 7 * (i - 4), false));
        return new LinearSymbol(BarcodeType.Ean8, v, modules, false, 1, 7,
            [(0, 3), (31, 36), (64, 67)], segments, false);
    }

    private static LinearSymbol EncodeUpcA(BarcodeValidationResult v)
    {
        // UPC-A, başında 0 olan EAN-13 ile aynı çubuk desenine sahiptir.
        bool[] modules = new EAN13Writer().encode("0" + v.EncoderData);
        string d = v.EncoderData;
        var segments = new List<HriSegment> { new(d[..1], -8, -1, true) };
        for (int i = 1; i <= 5; i++) segments.Add(new HriSegment(d.Substring(i, 1), 10 + 7 * (i - 1), 10 + 7 * i, false));
        for (int i = 6; i <= 10; i++) segments.Add(new HriSegment(d.Substring(i, 1), 50 + 7 * (i - 6), 50 + 7 * (i - 5), false));
        segments.Add(new HriSegment(d[11..], 96, 103, true));
        return new LinearSymbol(BarcodeType.UpcA, v, modules, false, 1, 9,
            [(0, 10), (45, 50), (85, 95)], segments, false);
    }

    private static LinearSymbol EncodeCode128(BarcodeType type, BarcodeValidationResult v)
    {
        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.CODE128_COMPACT] = true };
        bool[] modules = new Code128Writer().encode(v.EncoderData, hints);
        return new LinearSymbol(type, v, modules, false, 1, 10, [], [], false);
    }

    private static LinearSymbol EncodeItf14(BarcodeValidationResult v, BarcodeSettings settings)
    {
        bool[] modules = new ITFWriter().encode(v.EncoderData);
        return new LinearSymbol(BarcodeType.Itf14, v, modules, true, settings.WideToNarrowRatio, 10, [], [], settings.BearerBars);
    }

    private static LinearSymbol EncodeLinear(BarcodeType type, BarcodeValidationResult v, bool[] modules, bool wideNarrow,
        BarcodeSettings settings, int quietZone) =>
        new(type, v, modules, wideNarrow, settings.WideToNarrowRatio, quietZone, [], [], false);

    private static MatrixSymbol EncodeQr(BarcodeValidationResult v, BarcodeSettings settings)
    {
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.MARGIN] = 0,
            [EncodeHintType.ERROR_CORRECTION] = settings.QrErrorCorrection switch
            {
                QrErrorCorrectionLevel.L => ZXing.QrCode.Internal.ErrorCorrectionLevel.L,
                QrErrorCorrectionLevel.Q => ZXing.QrCode.Internal.ErrorCorrectionLevel.Q,
                QrErrorCorrectionLevel.H => ZXing.QrCode.Internal.ErrorCorrectionLevel.H,
                _ => ZXing.QrCode.Internal.ErrorCorrectionLevel.M,
            },
        };
        // Yalnızca ASCII dışı karakter varsa UTF-8 (ECI) kullan; saf ASCII'de ECI başlığı eklenmez.
        if (!IsAscii(v.EncoderData)) hints[EncodeHintType.CHARACTER_SET] = "UTF-8";
        var matrix = new QRCodeWriter().encode(v.EncoderData, BarcodeFormat.QR_CODE, 0, 0, hints);
        return ToMatrixSymbol(BarcodeType.QrCode, v, matrix, 4);
    }

    private static MatrixSymbol EncodeDataMatrix(BarcodeValidationResult v, BarcodeSettings settings)
    {
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.DATA_MATRIX_SHAPE] = settings.DataMatrixShape switch
            {
                DataMatrixShape.Rectangle => SymbolShapeHint.FORCE_RECTANGLE,
                DataMatrixShape.Auto => SymbolShapeHint.FORCE_NONE,
                _ => SymbolShapeHint.FORCE_SQUARE,
            },
        };
        if (!IsAscii(v.EncoderData)) hints[EncodeHintType.CHARACTER_SET] = "UTF-8";
        var matrix = new DataMatrixWriter().encode(v.EncoderData, BarcodeFormat.DATA_MATRIX, 0, 0, hints);
        return ToMatrixSymbol(BarcodeType.DataMatrix, v, matrix, 2);
    }

    /// <summary>GS1 DataMatrix: başta FNC1, AI ayırıcıları FNC1 (bkz. <see cref="Gs1DataMatrixEncoder"/>).</summary>
    private static MatrixSymbol EncodeGs1DataMatrix(BarcodeValidationResult v, BarcodeSettings settings)
    {
        var (width, height, cells) = Gs1DataMatrixEncoder.Encode(v.EncoderData, settings.DataMatrixShape);
        return new MatrixSymbol(BarcodeType.Gs1DataMatrix, v, width, height, cells, 2);
    }

    /// <summary>
    /// GS1 QR: ZXing'in GS1_FORMAT seçeneği "FNC1 ilk konumda" kip göstergesini ekler. ZXing tüm içerik için tek kip
    /// seçer (yalnız rakam → sayısal, yalnız alfasayısal tablo → alfasayısal, aksi → bayt). Alfasayısal kipte FNC1
    /// ayırıcısı "%" ile gösterildiğinden değerdeki "%" işareti "%%" yazılmalıdır (ISO/IEC 18004); ZXing bunu kendisi
    /// yapmadığı için burada yapılır. Bayt kipinde ayırıcı GS (0x1D) baytıdır ve "%" olduğu gibi kalır.
    /// </summary>
    private static MatrixSymbol EncodeGs1Qr(BarcodeValidationResult v, BarcodeSettings settings)
    {
        string data = v.EncoderData;
        bool numericOnly = data.All(char.IsAsciiDigit);
        if (!numericOnly && data.All(IsQrAlphanumeric)) data = data.Replace("%", "%%");
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.MARGIN] = 0,
            [EncodeHintType.GS1_FORMAT] = true,
            [EncodeHintType.ERROR_CORRECTION] = settings.QrErrorCorrection switch
            {
                QrErrorCorrectionLevel.L => ZXing.QrCode.Internal.ErrorCorrectionLevel.L,
                QrErrorCorrectionLevel.Q => ZXing.QrCode.Internal.ErrorCorrectionLevel.Q,
                QrErrorCorrectionLevel.H => ZXing.QrCode.Internal.ErrorCorrectionLevel.H,
                _ => ZXing.QrCode.Internal.ErrorCorrectionLevel.M,
            },
        };
        var matrix = new QRCodeWriter().encode(data, BarcodeFormat.QR_CODE, 0, 0, hints);
        return ToMatrixSymbol(BarcodeType.Gs1QrCode, v, matrix, 4);
    }

    /// <summary>QR alfasayısal kip tablosu: 0–9, A–Z, boşluk ve $ % * + - . / :</summary>
    private static bool IsQrAlphanumeric(char c) =>
        char.IsAsciiDigit(c) || c is >= 'A' and <= 'Z' || " $%*+-./:".Contains(c);

    private static MatrixSymbol EncodePdf417(BarcodeValidationResult v, BarcodeSettings settings)
    {
        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 0 };
        if (settings.Pdf417ErrorCorrection >= 0)
            hints[EncodeHintType.ERROR_CORRECTION] = (PDF417ErrorCorrectionLevel)settings.Pdf417ErrorCorrection;
        if (settings.Pdf417Columns > 0)
            hints[EncodeHintType.PDF417_DIMENSIONS] = new Dimensions(settings.Pdf417Columns, settings.Pdf417Columns, 3, 90);
        if (!IsAscii(v.EncoderData)) hints[EncodeHintType.CHARACTER_SET] = "UTF-8";
        var matrix = new PDF417Writer().encode(v.EncoderData, BarcodeFormat.PDF_417, 0, 0, hints);
        return ToMatrixSymbol(BarcodeType.Pdf417, v, matrix, 2);
    }

    private static MatrixSymbol ToMatrixSymbol(BarcodeType type, BarcodeValidationResult v, BitMatrix matrix, int quietZone)
    {
        var cells = new bool[matrix.Width * matrix.Height];
        for (int y = 0; y < matrix.Height; y++)
        for (int x = 0; x < matrix.Width; x++)
            cells[y * matrix.Width + x] = matrix[x, y];
        return new MatrixSymbol(type, v, matrix.Width, matrix.Height, cells, quietZone);
    }

    private static bool IsAscii(string s)
    {
        foreach (char c in s)
            if (c > 127) return false;
        return true;
    }
}
