namespace AkelEtiket.BarcodeEngine;

/// <summary>Kontrol basamağı hesaplamaları.</summary>
public static class CheckDigits
{
    private const string Code39Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";

    public static bool IsAllDigits(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return false;
        foreach (char c in value)
            if (c is < '0' or > '9') return false;
        return true;
    }

    /// <summary>
    /// GS1 mod 10 kontrol basamağı (EAN-13, EAN-8, UPC-A, ITF-14, GTIN, SSCC).
    /// Sağdan başlayarak ağırlıklar 3,1,3,1… uygulanır.
    /// </summary>
    public static int Gs1Mod10(ReadOnlySpan<char> digitsWithoutCheck)
    {
        if (!IsAllDigits(digitsWithoutCheck))
            throw new ArgumentException("Yalnızca rakam içermelidir.", nameof(digitsWithoutCheck));
        int sum = 0;
        bool weightThree = true;
        for (int i = digitsWithoutCheck.Length - 1; i >= 0; i--)
        {
            int d = digitsWithoutCheck[i] - '0';
            sum += weightThree ? d * 3 : d;
            weightThree = !weightThree;
        }
        return (10 - sum % 10) % 10;
    }

    /// <summary>Son hanesi kontrol basamağı olan GS1 numarasını doğrular.</summary>
    public static bool IsValidGs1(ReadOnlySpan<char> fullNumber) =>
        fullNumber.Length >= 2 && IsAllDigits(fullNumber) &&
        Gs1Mod10(fullNumber[..^1]) == fullNumber[^1] - '0';

    public static bool IsCode39Char(char c) => Code39Alphabet.IndexOf(c) >= 0;

    /// <summary>Code 39 mod 43 kontrol karakteri.</summary>
    public static char Code39Mod43(string data)
    {
        int sum = 0;
        foreach (char c in data)
        {
            int value = Code39Alphabet.IndexOf(c);
            if (value < 0) throw new ArgumentException($"Code 39 karakteri değil: '{c}'", nameof(data));
            sum += value;
        }
        return Code39Alphabet[sum % 43];
    }
}
