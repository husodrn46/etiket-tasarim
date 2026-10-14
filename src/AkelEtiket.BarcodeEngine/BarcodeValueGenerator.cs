using System.Globalization;
using System.Text;
using AkelEtiket.Core.Model;

namespace AkelEtiket.BarcodeEngine;

/// <summary>
/// Otomatik barkod değeri üretimi: ön ek + sıra numarası + kontrol basamağı.
/// Gerçek ürünlerde EAN numaraları GS1 Türkiye'den alınan firma önekiyle verilmelidir.
/// </summary>
public static class BarcodeValueGenerator
{
    /// <summary>
    /// Ön ek (ör. "869" + firma kodu) ve ürün numarasından geçerli EAN-13/EAN-8/UPC-A/ITF-14 üretir.
    /// Ürün numarası, kalan haneye sıfır doldurularak yazılır.
    /// </summary>
    public static string GenerateGtin(BarcodeType type, string prefix, long itemNumber)
    {
        int length = type switch
        {
            BarcodeType.Ean13 => 13,
            BarcodeType.Ean8 => 8,
            BarcodeType.UpcA => 12,
            BarcodeType.Itf14 => 14,
            _ => throw new ArgumentException("Bu tür için GTIN üretilemez.", nameof(type)),
        };
        prefix = (prefix ?? "").Trim();
        if (prefix.Length > 0 && !CheckDigits.IsAllDigits(prefix))
            throw new ArgumentException("Ön ek yalnızca rakam içermeli.", nameof(prefix));
        int itemDigits = length - 1 - prefix.Length;
        if (itemDigits < 1)
            throw new ArgumentException($"Ön ek çok uzun; {length - 2} haneden kısa olmalı.", nameof(prefix));
        if (itemNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(itemNumber), "Ürün numarası negatif olamaz.");
        string item = itemNumber.ToString(CultureInfo.InvariantCulture);
        if (item.Length > itemDigits)
            throw new ArgumentOutOfRangeException(nameof(itemNumber), $"Ürün numarası en fazla {itemDigits} hane olabilir.");
        string body = prefix + item.PadLeft(itemDigits, '0');
        return body + (char)('0' + CheckDigits.Gs1Mod10(body));
    }

    /// <summary>Türüne uygun geçerli bir örnek değer (yeni barkod eklerken).</summary>
    public static string GetSample(BarcodeType type) => BarcodeCatalog.Get(type).Example;

    /// <summary>Test ve deneme amaçlı rastgele ama geçerli değer.</summary>
    public static string GenerateRandom(BarcodeType type, Random random)
    {
        string Digits(int count)
        {
            var sb = new StringBuilder(count);
            for (int i = 0; i < count; i++) sb.Append((char)('0' + random.Next(10)));
            return sb.ToString();
        }
        string WithCheck(string body) => body + (char)('0' + CheckDigits.Gs1Mod10(body));
        const string Alnum = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        string Alpha(int count) => new(Enumerable.Range(0, count).Select(_ => Alnum[random.Next(Alnum.Length)]).ToArray());

        return type switch
        {
            BarcodeType.Ean13 => WithCheck("869" + Digits(9)),
            BarcodeType.Ean8 => WithCheck(Digits(7)),
            BarcodeType.UpcA => WithCheck("0" + Digits(10)),
            BarcodeType.Itf14 => WithCheck("1869" + Digits(9)),
            BarcodeType.Itf => Digits(2 * random.Next(2, 8)),
            BarcodeType.Code39 => Alpha(random.Next(4, 12)),
            BarcodeType.Code128 => "DEMO-" + Alpha(random.Next(3, 10)).ToLowerInvariant() + Digits(3),
            BarcodeType.Gs1_128 => $"(01){WithCheck("0869" + Digits(9))}(10){Alpha(6)}(17){26 + random.Next(4):00}{random.Next(1, 13):00}{random.Next(1, 29):00}",
            BarcodeType.QrCode => "Örnek Firma • Şekerlik ğüşıöç " + Digits(6),
            BarcodeType.DataMatrix => "DEMO" + Digits(10) + " Çay Tabağı",
            BarcodeType.Pdf417 => "Örnek Firma Ürün " + Digits(8) + " İstanbul",
            BarcodeType.Gs1DataMatrix or BarcodeType.Gs1QrCode =>
                $"(01){WithCheck("0869" + Digits(9))}(17){26 + random.Next(4):00}{random.Next(1, 13):00}{random.Next(1, 29):00}" +
                $"(10){Alpha(random.Next(3, 9))}(21){Alpha(random.Next(4, 12)).ToLowerInvariant()}",
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }
}
