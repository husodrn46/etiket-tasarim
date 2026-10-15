using AkelEtiket.BarcodeEngine;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Gs1;

/// <summary>GS1 kimlik numarası türleri.</summary>
public enum Gs1KeyType
{
    /// <summary>Küresel Lokasyon Numarası (13 hane). Bir yeri/tüzel kişiyi tanımlar; ürün barkodu değildir.</summary>
    Gln,

    /// <summary>GTIN-8 (EAN-8).</summary>
    Gtin8,

    /// <summary>GTIN-12 (UPC-A).</summary>
    Gtin12,

    /// <summary>GTIN-13 (EAN-13).</summary>
    Gtin13,

    /// <summary>GTIN-14 (ITF-14 / GS1-128 (01), (02)).</summary>
    Gtin14,

    /// <summary>Seri Nakliye Konteyner Kodu (18 hane), AI (00).</summary>
    Sscc,
}

/// <summary>Bir GS1 numarasının doğrulama sonucu.</summary>
/// <param name="IsValid">Uzunluk, rakam ve kontrol basamağı doğru mu?</param>
/// <param name="Normalized">Boşluk/tire temizlenmiş numara (doğrulama başarısızsa da dolu olabilir).</param>
/// <param name="Error">Türkçe hata açıklaması (geçerliyse null).</param>
/// <param name="Type">Numaranın türü (GTIN için uzunluktan belirlenir).</param>
public sealed record Gs1ValidationResult(bool IsValid, string Normalized, string? Error, Gs1KeyType? Type)
{
    public static Gs1ValidationResult Valid(string normalized, Gs1KeyType type) => new(true, normalized, null, type);

    public static Gs1ValidationResult Invalid(string normalized, string error, Gs1KeyType? type = null) => new(false, normalized, error, type);
}

/// <summary>
/// GS1 kimlik numaraları (GLN, GTIN-8/12/13/14, SSCC) için temizleme, kontrol basamağı hesaplama ve doğrulama.
/// Kontrol basamağı hesabı <see cref="CheckDigits.Gs1Mod10"/> ile barkod motoruyla ortaktır.
/// </summary>
public static class Gs1Identifiers
{
    public const int GlnLength = 13;
    public const int SsccLength = 18;
    public const int MinGcpLength = 6;
    public const int MaxGcpLength = 12;

    /// <summary>
    /// Kopyala-yapıştır kaynaklı boşluk, tire, nokta ve sekmeleri atar. Harf gibi başka karakterler korunur
    /// (doğrulama onları "yalnızca rakam" hatasıyla bildirir).
    /// </summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var chars = new List<char>(input.Length);
        foreach (char c in input)
        {
            if (c is ' ' or '\t' or '-' or '.' or '\u00A0' or '\r' or '\n') continue;
            chars.Add(c);
        }
        return new string(chars.ToArray());
    }

    /// <summary>Kontrol basamağı olmayan rakam dizisinin GS1 mod 10 kontrol basamağı.</summary>
    public static int ComputeCheckDigit(string digitsWithoutCheck)
    {
        if (string.IsNullOrEmpty(digitsWithoutCheck) || !CheckDigits.IsAllDigits(digitsWithoutCheck))
            throw new ArgumentException(L10n.T("Kontrol basamağı yalnızca rakamlardan hesaplanabilir."), nameof(digitsWithoutCheck));
        return CheckDigits.Gs1Mod10(digitsWithoutCheck);
    }

    /// <summary>Rakam dizisinin sonuna kontrol basamağını ekler.</summary>
    public static string AppendCheckDigit(string digitsWithoutCheck) =>
        digitsWithoutCheck + (char)('0' + ComputeCheckDigit(digitsWithoutCheck));

    /// <summary>GLN (13 hane) doğrulaması.</summary>
    public static Gs1ValidationResult ValidateGln(string? input) => ValidateFixed(input, GlnLength, Gs1KeyType.Gln, "GLN");

    /// <summary>SSCC (18 hane) doğrulaması.</summary>
    public static Gs1ValidationResult ValidateSscc(string? input) => ValidateFixed(input, SsccLength, Gs1KeyType.Sscc, "SSCC");

    /// <summary>GTIN doğrulaması: 8, 12, 13 veya 14 hane; tür uzunluktan belirlenir.</summary>
    public static Gs1ValidationResult ValidateGtin(string? input)
    {
        string value = Normalize(input);
        if (value.Length == 0) return Gs1ValidationResult.Invalid(value, L10n.T("GTIN boş."));
        if (!CheckDigits.IsAllDigits(value)) return Gs1ValidationResult.Invalid(value, L10n.T("GTIN yalnızca rakam içermeli."));
        Gs1KeyType? type = value.Length switch
        {
            8 => Gs1KeyType.Gtin8,
            12 => Gs1KeyType.Gtin12,
            13 => Gs1KeyType.Gtin13,
            14 => Gs1KeyType.Gtin14,
            _ => null,
        };
        if (type is null)
            return Gs1ValidationResult.Invalid(value, L10n.T($"GTIN 8, 12, 13 veya 14 hane olmalı (girilen {value.Length} hane)."));
        return CheckDigitResult(value, type.Value, "GTIN");
    }

    /// <summary>Türüne göre doğrular (GTIN türlerinde uzunluk da denetlenir).</summary>
    public static Gs1ValidationResult Validate(Gs1KeyType type, string? input) => type switch
    {
        Gs1KeyType.Gln => ValidateGln(input),
        Gs1KeyType.Sscc => ValidateSscc(input),
        Gs1KeyType.Gtin8 => ValidateFixed(input, 8, type, "GTIN-8"),
        Gs1KeyType.Gtin12 => ValidateFixed(input, 12, type, "GTIN-12"),
        Gs1KeyType.Gtin13 => ValidateFixed(input, 13, type, "GTIN-13"),
        Gs1KeyType.Gtin14 => ValidateFixed(input, 14, type, "GTIN-14"),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>
    /// GTIN'i 14 haneye tamamlar (soluna sıfır eklenir; kontrol basamağı değişmez). Geçersiz GTIN için null.
    /// GS1-128'de (01)/(02) 14 hane ister.
    /// </summary>
    public static string? ToGtin14(string? gtin)
    {
        var result = ValidateGtin(gtin);
        return result.IsValid ? result.Normalized.PadLeft(14, '0') : null;
    }

    /// <summary>
    /// GS1 firma öneki (GCP) biçim denetimi: yalnızca kullanıcının üyelik belgesinden yazdığı 6–12 rakam.
    /// Uzunluk ya da değer başka bir numaradan (ör. GLN) <b>türetilmez</b>.
    /// </summary>
    public static bool IsValidGcpFormat(string? gcp, out string? error)
    {
        string value = Normalize(gcp);
        if (value.Length == 0)
        {
            error = L10n.T("Firma öneki girilmedi.");
            return false;
        }
        if (!CheckDigits.IsAllDigits(value))
        {
            error = L10n.T("Firma öneki yalnızca rakam içermeli.");
            return false;
        }
        if (value.Length is < MinGcpLength or > MaxGcpLength)
        {
            error = L10n.T($"Firma öneki {MinGcpLength}–{MaxGcpLength} hane olmalı (girilen {value.Length} hane). Uzunluğu GS1 Türkiye üyelik belgenizden kontrol edin.");
            return false;
        }
        error = null;
        return true;
    }

    /// <summary>SSCC'de seri referansının hane sayısı: 17 − 1 (uzantı basamağı) − firma öneki uzunluğu.</summary>
    public static int SsccSerialLength(int gcpLength) => SsccLength - 1 - 1 - gcpLength;

    /// <summary>SSCC oluşturur: uzantı basamağı + firma öneki + sıfır dolgulu seri referansı + kontrol basamağı.</summary>
    public static string BuildSscc(int extensionDigit, string gcp, long serialReference)
    {
        if (extensionDigit is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(extensionDigit), L10n.T("Uzantı basamağı 0–9 olmalı."));
        if (!IsValidGcpFormat(gcp, out string? error)) throw new ArgumentException(error, nameof(gcp));
        string prefix = Normalize(gcp);
        int serialLength = SsccSerialLength(prefix.Length);
        if (serialReference < 0 || serialReference > MaxSerial(serialLength))
            throw new ArgumentOutOfRangeException(nameof(serialReference), L10n.T($"Seri referansı 0–{MaxSerial(serialLength)} aralığında olmalı."));
        string body = extensionDigit.ToString(System.Globalization.CultureInfo.InvariantCulture) + prefix +
                      serialReference.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(serialLength, '0');
        return AppendCheckDigit(body);
    }

    /// <summary>Belirtilen hane sayısındaki en büyük seri referansı (ör. 7 hane → 9.999.999).</summary>
    public static long MaxSerial(int serialLength)
    {
        if (serialLength is < 1 or > 15) throw new ArgumentOutOfRangeException(nameof(serialLength));
        long max = 1;
        for (int i = 0; i < serialLength; i++) max *= 10;
        return max - 1;
    }

    /// <summary>Numara verilen önekle başlıyor mu? (Yalnızca tutarlılık ipucu içindir, hiçbir şey otomatik doldurulmaz.)</summary>
    public static bool StartsWithPrefix(string? number, string? prefix)
    {
        string n = Normalize(number), p = Normalize(prefix);
        return p.Length > 0 && n.StartsWith(p, StringComparison.Ordinal);
    }

    private static Gs1ValidationResult ValidateFixed(string? input, int length, Gs1KeyType type, string label)
    {
        string value = Normalize(input);
        if (value.Length == 0) return Gs1ValidationResult.Invalid(value, L10n.T($"{label} boş."), type);
        if (!CheckDigits.IsAllDigits(value)) return Gs1ValidationResult.Invalid(value, L10n.T($"{label} yalnızca rakam içermeli."), type);
        if (value.Length != length)
            return Gs1ValidationResult.Invalid(value, L10n.T($"{label} {length} hane olmalı (girilen {value.Length} hane)."), type);
        return CheckDigitResult(value, type, label);
    }

    private static Gs1ValidationResult CheckDigitResult(string value, Gs1KeyType type, string label)
    {
        int expected = CheckDigits.Gs1Mod10(value.AsSpan(0, value.Length - 1));
        if (expected != value[^1] - '0')
            return Gs1ValidationResult.Invalid(value,
                L10n.T($"{label} kontrol basamağı hatalı: son hane {expected} olmalı ({value[..^1]}{expected})."), type);
        return Gs1ValidationResult.Valid(value, type);
    }
}
