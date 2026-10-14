using System.Globalization;

namespace AkelEtiket.SampleData;

/// <summary>Örnek ürün satırı.</summary>
internal sealed record SampleProduct(string Code, string Name, string Barcode, decimal Price, int Quantity);

/// <summary>
/// Deterministik örnek ürün listesi (her çalıştırmada aynı veri).
/// Tüm satırlar sentetiktir; DEMO002'nin EAN-13 kontrol basamağı bilerek hatalıdır
/// (doğrusu ...0 olmalı) — uygulamanın hatalı satırı satır numarasıyla raporladığını göstermek için.
/// </summary>
internal static class SampleProducts
{
    public const int Count = 120;

    private static readonly string[] BaseNames =
    [
        "Yemek Tabağı 27 cm", "Pilav Tabağı 24 cm", "Çorba Kasesi 15 cm", "Salata Kasesi 20 cm",
        "Pasta Tabağı 19 cm", "Çay Tabağı 13 cm", "Oval Servis Tabağı 36 cm", "Kayık Tabak 30 cm",
        "Meze Tabağı 4 Bölmeli", "Kahvaltılık 6 Gözlü", "Şekerlik Kapaklı", "Sosluk 8 cm",
        "Çerezlik Üçlü", "Zeytinlik Yaprak", "Reçellik İkili", "Ekmek Sepeti Oval",
        "Börek Tepsisi 40 cm", "Dikdörtgen Tepsi 35x50 cm", "Yuvarlak Tepsi 40 cm", "Çocuk Tabağı Bölmeli",
        "Kupa 300 ml", "Fincan Takımı 6'lı", "Sürahi 1,5 L", "Peçetelik",
        "Buz Kovası", "Lokum Tabağı", "Kek Servis Tabağı", "Kare Mezelik 12 cm",
        "Bardak Altlığı 6'lı", "Baharatlık Seti",
    ];

    private static readonly string[] Variants =
    [
        "Beyaz", "Krem", "İnci Beyazı", "Ahşap Desen", "Mermer Desen",
        "Çiçek Desenli", "Siyah", "Gökkuşağı", "Lacivert Şerit", "Bakır Yaldız",
    ];

    private static readonly decimal[] PriceEndings = [0.00m, 0.50m, 0.90m];

    public static IReadOnlyList<SampleProduct> Create()
    {
        var products = new List<SampleProduct>(Count)
        {
            new("DEMO001", "Örnek Ürün A", "8691234567890", 120m, 1),
            new("DEMO002", "Örnek Ürün B", "8691234567891", 160m, 2),
        };

        var random = new Random(20261013);
        for (int i = 3, k = 0; i <= Count; i++, k++)
        {
            string baseName = BaseNames[k % BaseNames.Length];
            // Aynı temel ada düşen satırlar farklı varyant alır (k, k+30, k+60, k+90 → ardışık varyantlar).
            string variant = Variants[(k / BaseNames.Length + k) % Variants.Length];
            string barcode = Ean13WithCheckDigit("8691234" + (10000 + i).ToString("D5", CultureInfo.InvariantCulture));
            decimal price = random.Next(35, 650) + PriceEndings[random.Next(PriceEndings.Length)];
            int quantity = 1 + random.Next(3);
            products.Add(new SampleProduct($"DEMO{i:000}", $"{baseName} - {variant}", barcode, price, quantity));
        }
        return products;
    }

    /// <summary>12 haneye GS1 mod-10 kontrol basamağını ekler.</summary>
    public static string Ean13WithCheckDigit(string twelveDigits)
    {
        if (twelveDigits.Length != 12 || !twelveDigits.All(char.IsAsciiDigit))
            throw new ArgumentException("EAN-13 için 12 rakam gerekli.", nameof(twelveDigits));
        int sum = 0;
        for (int i = 0; i < 12; i++)
            sum += (twelveDigits[i] - '0') * (i % 2 == 0 ? 1 : 3);
        int check = (10 - sum % 10) % 10;
        return twelveDigits + (char)('0' + check);
    }
}
