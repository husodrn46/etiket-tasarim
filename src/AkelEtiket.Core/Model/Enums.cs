namespace AkelEtiket.Core.Model;

/// <summary>Desteklenen barkod türleri. JSON'da adıyla saklanır; sıralamayı değiştirmek dosyaları bozmaz.</summary>
public enum BarcodeType
{
    Ean13,
    Ean8,
    UpcA,
    Code128,
    /// <summary>GS1-128: Code 128 + FNC1 + Uygulama Tanımlayıcıları (AI). Code 128 ile aynı şey değildir.</summary>
    Gs1_128,
    Code39,
    /// <summary>Interleaved 2 of 5 (genel).</summary>
    Itf,
    /// <summary>ITF-14 (GTIN-14, kontrol basamaklı, isteğe bağlı taşıyıcı çerçeve).</summary>
    Itf14,
    QrCode,
    DataMatrix,
    Pdf417,
    /// <summary>
    /// GS1 DataMatrix (v2): Data Matrix + başta FNC1 + Uygulama Tanımlayıcıları (AI). Düz Data Matrix ile aynı değildir;
    /// okuyucu "]d2" sembol tanımlayıcısıyla GS1 verisi olarak tanır. v1 bu türü okuyamaz (biçim 2).
    /// </summary>
    Gs1DataMatrix,
    /// <summary>
    /// GS1 QR Kod (v2): QR + "FNC1 ilk konumda" kipi + Uygulama Tanımlayıcıları (AI). Okuyucu "]Q3" ile GS1 verisi
    /// olarak tanır. v1 bu türü okuyamaz (biçim 2).
    /// </summary>
    Gs1QrCode,
}

/// <summary>QR kod hata düzeltme seviyesi (L %7, M %15, Q %25, H %30).</summary>
public enum QrErrorCorrectionLevel
{
    L,
    M,
    Q,
    H,
}

public enum DataMatrixShape
{
    Square,
    Rectangle,
    Auto,
}

/// <summary>Görselin 1-bit siyah-beyaza dönüştürülme yöntemi.</summary>
public enum ImageConversionMethod
{
    /// <summary>Görsel türüne göre otomatik (logo → keskin eşik, fotoğraf → detay koruyan dithering).</summary>
    Auto,
    /// <summary>Sabit eşik değeri.</summary>
    Threshold,
    /// <summary>Otsu otomatik eşikleme.</summary>
    Otsu,
    /// <summary>Floyd–Steinberg hata yayma.</summary>
    FloydSteinberg,
    /// <summary>Düzenli (Bayer) dithering.</summary>
    Ordered,
    /// <summary>Fotoğraf için detay koruyan yöntem.</summary>
    Photo,
    /// <summary>Logo ve çizimler için keskin kenarlı yöntem.</summary>
    Logo,
}

/// <summary>Görselin kutuya yerleşimi.</summary>
public enum ImageFitMode
{
    /// <summary>Sığdır: oran korunur, tamamı görünür.</summary>
    Contain,
    /// <summary>Doldur: oran korunur, taşan kısım kırpılır.</summary>
    Cover,
    /// <summary>Uzat: oran korunmaz.</summary>
    Stretch,
}

public enum TextHorizontalAlignment
{
    Left,
    Center,
    Right,
}

public enum TextVerticalAlignment
{
    Top,
    Middle,
    Bottom,
}

public enum ShapeKind
{
    Rectangle,
    Ellipse,
    Line,
}

public enum LineOrientation
{
    Horizontal,
    Vertical,
    DiagonalDown,
    DiagonalUp,
}

/// <summary>1-bit baskıda yalnızca iki renk vardır.</summary>
public enum InkColor
{
    Black,
    White,
}

public enum FillStyle
{
    None,
    Black,
    White,
}

/// <summary>Etiket medyası türü (yazıcı sensörü).</summary>
public enum MediaType
{
    /// <summary>Etiketler arası boşluklu (gap) etiket.</summary>
    Gap,
    /// <summary>Arkası siyah işaretli (black mark) etiket.</summary>
    BlackMark,
    /// <summary>Sürekli rulo (boşluksuz).</summary>
    Continuous,
}

public enum GuideOrientation
{
    Horizontal,
    Vertical,
}
