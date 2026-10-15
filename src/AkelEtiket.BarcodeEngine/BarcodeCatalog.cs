using AkelEtiket.Core.Model;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.BarcodeEngine;

/// <summary>Barkod türünün kullanıcıya gösterilen bilgileri.</summary>
public sealed record BarcodeTypeInfo(
    BarcodeType Type,
    string DisplayName,
    string Description,
    string Example,
    bool Is2D,
    bool SupportsCheckDigitOption,
    bool SupportsWideNarrowRatio);

/// <summary>Desteklenen barkod türlerinin listesi. Her tür gerçek bir kodlayıcıyla üretilir ve okunarak doğrulanır.</summary>
public static class BarcodeCatalog
{
    public static IReadOnlyList<BarcodeTypeInfo> All { get; } =
    [
        new(BarcodeType.Ean13, "EAN-13",
            L10n.T("Perakende ürün barkodu, 13 hane. 12 hane girilirse kontrol basamağı otomatik hesaplanır."),
            "8691234567890", false, false, false),
        new(BarcodeType.Ean8, "EAN-8",
            L10n.T("Küçük ambalajlar için 8 haneli ürün barkodu. 7 hane girilirse kontrol basamağı hesaplanır."),
            "96385074", false, false, false),
        new(BarcodeType.UpcA, "UPC-A",
            L10n.T("Kuzey Amerika perakende barkodu, 12 hane. 11 hane girilirse kontrol basamağı hesaplanır."),
            "036000291452", false, false, false),
        new(BarcodeType.Code128, "Code 128",
            L10n.T("Harf, rakam ve işaret içeren genel amaçlı barkod (yalnızca ASCII; Türkçe harf içermez)."),
            "DEMO001", false, false, false),
        new(BarcodeType.Gs1_128, "GS1-128",
            L10n.T("Lojistik barkodu: (01) GTIN, (10) parti no, (17) son kullanma gibi Uygulama Tanımlayıcıları (AI) içerir."),
            "(01)08691234567890(10)ABC123", false, false, false),
        new(BarcodeType.Code39, "Code 39",
            L10n.T("Büyük harf, rakam ve - . boşluk $ / + % karakterleri. İsteğe bağlı mod 43 kontrol karakteri."),
            "DEMO-001", false, true, true),
        new(BarcodeType.Itf, "ITF (Interleaved 2 of 5)",
            L10n.T("Yalnızca rakam, çift hane sayısı. İsteğe bağlı mod 10 kontrol basamağı."),
            "12345670", false, true, true),
        new(BarcodeType.Itf14, "ITF-14",
            L10n.T("Koli barkodu (GTIN-14). 13 hane girilirse kontrol basamağı hesaplanır."),
            "18691234567897", false, false, true),
        new(BarcodeType.QrCode, "QR Kod",
            L10n.T("Metin, web adresi ve Türkçe karakter. Hata düzeltme seviyesi seçilebilir."),
            "https://example.com", true, false, false),
        new(BarcodeType.DataMatrix, "Data Matrix",
            L10n.T("Küçük alanda yüksek veri yoğunluğu; Türkçe karakter desteklenir."),
            "DEMO001 8691234567890", true, false, false),
        new(BarcodeType.Pdf417, "PDF417",
            L10n.T("Çok satırlı 2B barkod; uzun metinler için."),
            "Örnek Firma DEMO001 8691234567890", true, false, false),
        new(BarcodeType.Gs1DataMatrix, "GS1 DataMatrix",
            L10n.T("GS1 Uygulama Tanımlayıcılarıyla (AI) 2B barkod: (01) GTIN, (17) son kullanma, (10) parti, (21) seri no. ") +
            L10n.T("Başta FNC1 vardır; GS1 okuyucuları veriyi AI'larıyla tanır. Düz Data Matrix ile aynı değildir."),
            "(01)08691234567890(17)281231(10)ABC123", true, false, false),
        new(BarcodeType.Gs1QrCode, "GS1 QR Kod",
            L10n.T("GS1 Uygulama Tanımlayıcılarıyla (AI) QR kod (FNC1 ilk konumda). Düz QR ile aynı değildir; ") +
            L10n.T("içerik (01)…(10)… biçiminde yazılır."),
            "(01)08691234567890(10)ABC123(21)DEMO0001", true, false, false),
    ];

    public static BarcodeTypeInfo Get(BarcodeType type) =>
        All.FirstOrDefault(i => i.Type == type) ?? throw new ArgumentOutOfRangeException(nameof(type));

    public static string GetDisplayName(BarcodeType type) => Get(type).DisplayName;
}
