using System.Text.RegularExpressions;
using AkelEtiket.Core.Model;

namespace AkelEtiket.BarcodeEngine;

/// <summary>Barkod kodlama ayarları (modeldeki <see cref="BarcodeElement"/> özelliklerinden).</summary>
public sealed record BarcodeSettings
{
    public bool AppendCheckDigit { get; init; }
    public double WideToNarrowRatio { get; init; } = 2.5;
    public QrErrorCorrectionLevel QrErrorCorrection { get; init; } = QrErrorCorrectionLevel.M;
    public DataMatrixShape DataMatrixShape { get; init; } = DataMatrixShape.Square;
    public int Pdf417ErrorCorrection { get; init; } = -1;
    public int Pdf417Columns { get; init; }
    public bool BearerBars { get; init; } = true;

    public static BarcodeSettings Default { get; } = new();

    public static BarcodeSettings From(BarcodeElement element) => new()
    {
        AppendCheckDigit = element.AppendCheckDigit,
        WideToNarrowRatio = element.WideToNarrowRatio,
        QrErrorCorrection = element.QrErrorCorrection,
        DataMatrixShape = element.DataMatrixShape,
        Pdf417ErrorCorrection = element.Pdf417ErrorCorrection,
        Pdf417Columns = element.Pdf417Columns,
        BearerBars = element.BearerBars,
    };
}

/// <summary>Barkod içeriği doğrulama sonucu.</summary>
public sealed class BarcodeValidationResult
{
    internal BarcodeValidationResult(BarcodeType type, string input)
    {
        Type = type;
        Input = input;
    }

    public BarcodeType Type { get; }

    /// <summary>Kullanıcının girdiği (alanları doldurulmuş) değer.</summary>
    public string Input { get; }

    /// <summary>Kodlayıcıya gönderilen veri (kontrol basamağı eklenmiş, GS1 için FNC1 kaçışlı).</summary>
    public string EncoderData { get; internal set; } = "";

    /// <summary>Barkodun altında gösterilecek okunabilir metin.</summary>
    public string HumanReadableText { get; internal set; } = "";

    /// <summary>Okuyucunun döndürmesi beklenen metin (okunabilirlik kontrolü için).</summary>
    public string VerificationText { get; internal set; } = "";

    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Notes { get; } = [];

    public bool IsValid => Errors.Count == 0;

    public string? FirstError => Errors.Count > 0 ? Errors[0] : null;
}

/// <summary>
/// Barkod içeriği kuralları: uzunluk, karakter kümesi, kontrol basamağı (hesaplama veya doğrulama).
/// Kodlama öncesi her zaman çağrılır; hatalı içerik asla sessizce düzeltilmez.
/// </summary>
public static partial class BarcodeValidator
{
    /// <summary>Code 128 FNC1 kaçış karakteri (ZXing).</summary>
    internal const char Fnc1 = 'ñ';

    [GeneratedRegex(@"^\s*[+-]?\d+([.,]\d+)?[eE][+-]?\d+\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ScientificNotationRegex();

    public static bool LooksLikeScientificNotation(string value) => ScientificNotationRegex().IsMatch(value);

    public static BarcodeValidationResult Validate(BarcodeType type, string? content, BarcodeSettings? settings = null)
    {
        settings ??= BarcodeSettings.Default;
        string input = content ?? "";
        var result = new BarcodeValidationResult(type, input);

        if (input.Length == 0 || (input.Trim().Length == 0 && !BarcodeElement.IsTwoDimensional(type)))
        {
            result.Errors.Add("Barkod içeriği boş.");
            return result;
        }
        if (LooksLikeScientificNotation(input))
        {
            result.Errors.Add($"Değer bilimsel gösterimde (\"{input.Trim()}\"). Excel sayıyı bozmuş olabilir; " +
                              "sütunu Metin olarak biçimlendirip barkodu yeniden girin.");
            return result;
        }

        switch (type)
        {
            case BarcodeType.Ean13:
                ValidateGtin(result, input, 13, "EAN-13");
                break;
            case BarcodeType.Ean8:
                ValidateGtin(result, input, 8, "EAN-8");
                break;
            case BarcodeType.UpcA:
                ValidateGtin(result, input, 12, "UPC-A");
                break;
            case BarcodeType.Itf14:
                ValidateGtin(result, input, 14, "ITF-14");
                break;
            case BarcodeType.Code128:
                ValidateCode128(result, input);
                break;
            case BarcodeType.Gs1_128:
                ValidateGs1_128(result, input);
                break;
            case BarcodeType.Gs1DataMatrix:
            case BarcodeType.Gs1QrCode:
                ValidateGs1TwoDimensional(result, input, type);
                break;
            case BarcodeType.Code39:
                ValidateCode39(result, input, settings);
                break;
            case BarcodeType.Itf:
                ValidateItf(result, input, settings);
                break;
            case BarcodeType.QrCode:
            case BarcodeType.DataMatrix:
            case BarcodeType.Pdf417:
                Validate2D(result, input, type);
                break;
            default:
                result.Errors.Add("Desteklenmeyen barkod türü.");
                break;
        }
        return result;
    }

    private static string StripSpaces(BarcodeValidationResult result, string input)
    {
        string compact = string.Concat(input.Where(c => !char.IsWhiteSpace(c)));
        if (compact.Length != input.Length && compact.Length > 0) result.Notes.Add("Boşluklar kaldırıldı.");
        return compact;
    }

    /// <summary>EAN-13, EAN-8, UPC-A, ITF-14: kontrol basamağı eksikse hesaplanır, varsa doğrulanır.</summary>
    private static void ValidateGtin(BarcodeValidationResult result, string input, int fullLength, string name)
    {
        string digits = StripSpaces(result, input);
        if (!CheckDigits.IsAllDigits(digits))
        {
            char bad = digits.FirstOrDefault(c => c is < '0' or > '9');
            result.Errors.Add($"{name} yalnızca rakam içerebilir (geçersiz karakter: '{bad}').");
            return;
        }
        if (digits.Length == fullLength - 1)
        {
            int check = CheckDigits.Gs1Mod10(digits);
            digits += (char)('0' + check);
            result.Notes.Add($"Kontrol basamağı otomatik hesaplandı: {check}");
        }
        else if (digits.Length == fullLength)
        {
            int expected = CheckDigits.Gs1Mod10(digits.AsSpan(0, fullLength - 1));
            int actual = digits[^1] - '0';
            if (expected != actual)
            {
                result.Errors.Add($"{name} kontrol basamağı hatalı: son hane {actual} girilmiş, {expected} olmalı " +
                                  $"(doğrusu {digits[..^1]}{expected}).");
                return;
            }
        }
        else
        {
            result.Errors.Add($"{name} için {fullLength - 1} veya {fullLength} rakam gerekir (girilen: {digits.Length} rakam).");
            return;
        }

        result.EncoderData = digits;
        result.HumanReadableText = digits;
        result.VerificationText = digits;
    }

    private static void ValidateCode128(BarcodeValidationResult result, string input)
    {
        foreach (char c in input)
        {
            if (c > 127)
            {
                result.Errors.Add(IsTurkishLetter(c)
                    ? $"Code 128 Türkçe karakterleri desteklemez ('{c}'). Türkçe karakter gerekiyorsa QR kod veya Data Matrix kullanın."
                    : $"Code 128 bu karakteri desteklemez: '{c}' (yalnızca ASCII).");
                return;
            }
        }
        if (input.Length > 80)
        {
            result.Errors.Add($"Code 128 içeriği en fazla 80 karakter olabilir (girilen {input.Length}).");
            return;
        }
        result.EncoderData = input;
        result.HumanReadableText = new string(input.Select(c => c < 32 ? ' ' : c).ToArray());
        result.VerificationText = input;
        if (input.Length > 30)
            result.Warnings.Add("Uzun Code 128 barkodu geniş yer kaplar; 2B barkod (QR/Data Matrix) düşünün.");
    }

    private static void ValidateGs1_128(BarcodeValidationResult result, string input)
    {
        var parsed = Gs1.Parse(input);
        if (!parsed.IsValid)
        {
            result.Errors.AddRange(parsed.Errors);
            return;
        }
        result.EncoderData = parsed.BuildEncoderData(Fnc1);
        result.HumanReadableText = parsed.HumanReadable;
        result.VerificationText = parsed.BuildDecodedForm("]C1");
    }

    /// <summary>
    /// GS1 DataMatrix / GS1 QR: AI kuralları GS1-128 ile aynıdır. Kodlayıcıya ve okuma kontrolüne GS (0x1D) ayırıcılı öğe
    /// dizisi gider; sembolde başta FNC1 bulunur (DataMatrix: FNC1 kod sözcüğü, QR: "FNC1 ilk konumda" kipi).
    /// </summary>
    private static void ValidateGs1TwoDimensional(BarcodeValidationResult result, string input, BarcodeType type)
    {
        string name = type == BarcodeType.Gs1DataMatrix ? "GS1 DataMatrix" : "GS1 QR";
        var parsed = Gs1.Parse(input, name, Gs1.TwoDimensionalMaxDataLength);
        if (!parsed.IsValid)
        {
            result.Errors.AddRange(parsed.Errors);
            return;
        }
        string elementString = parsed.BuildDecodedForm("");
        result.EncoderData = elementString;
        result.HumanReadableText = parsed.HumanReadable;
        result.VerificationText = elementString;
    }

    private static void ValidateCode39(BarcodeValidationResult result, string input, BarcodeSettings settings)
    {
        foreach (char c in input)
        {
            if (CheckDigits.IsCode39Char(c)) continue;
            if (char.IsLower(c) && CheckDigits.IsCode39Char(char.ToUpperInvariant(c)))
                result.Errors.Add($"Code 39 küçük harf desteklemez ('{c}'). Büyük harf kullanın: {input.ToUpperInvariant()}");
            else if (c == '*')
                result.Errors.Add("Code 39 içinde '*' kullanılamaz (başlangıç/bitiş karakteridir).");
            else
                result.Errors.Add($"Code 39 bu karakteri desteklemez: '{c}'. İzin verilenler: A–Z, 0–9, - . boşluk $ / + %");
            return;
        }
        string data = input;
        if (settings.AppendCheckDigit)
        {
            char check = CheckDigits.Code39Mod43(input);
            data += check;
            result.Notes.Add($"Mod 43 kontrol karakteri eklendi: {check}");
        }
        result.EncoderData = data;
        result.HumanReadableText = data;
        result.VerificationText = data;
    }

    private static void ValidateItf(BarcodeValidationResult result, string input, BarcodeSettings settings)
    {
        string digits = StripSpaces(result, input);
        if (!CheckDigits.IsAllDigits(digits))
        {
            result.Errors.Add("ITF yalnızca rakam içerebilir.");
            return;
        }
        if (settings.AppendCheckDigit)
        {
            int check = CheckDigits.Gs1Mod10(digits);
            digits += (char)('0' + check);
            result.Notes.Add($"Mod 10 kontrol basamağı eklendi: {check}");
        }
        if (digits.Length % 2 != 0)
        {
            result.Errors.Add(settings.AppendCheckDigit
                ? $"ITF toplam hane sayısı (kontrol basamağıyla {digits.Length}) çift olmalı; değerin başına 0 ekleyin."
                : $"ITF hane sayısı çift olmalı (girilen {digits.Length}); değerin başına 0 ekleyin.");
            return;
        }
        if (digits.Length < 2 || digits.Length > 80)
        {
            result.Errors.Add("ITF 2–80 rakam içermelidir.");
            return;
        }
        result.EncoderData = digits;
        result.HumanReadableText = digits;
        result.VerificationText = digits;
    }

    private static void Validate2D(BarcodeValidationResult result, string input, BarcodeType type)
    {
        int byteCount = System.Text.Encoding.UTF8.GetByteCount(input);
        int limit = type switch
        {
            BarcodeType.QrCode => 2900,
            BarcodeType.DataMatrix => 1500,
            _ => 1100,
        };
        if (byteCount > limit)
        {
            result.Errors.Add($"İçerik bu barkod türü için çok uzun ({byteCount} bayt, en fazla yaklaşık {limit}).");
            return;
        }
        result.EncoderData = input;
        result.HumanReadableText = input.ReplaceLineEndings(" ");
        result.VerificationText = input;
    }

    internal static bool IsTurkishLetter(char c) => "ğĞüÜşŞıİöÖçÇ".IndexOf(c) >= 0;
}
