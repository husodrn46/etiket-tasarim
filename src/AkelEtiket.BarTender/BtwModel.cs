namespace AkelEtiket.BarTender;

/// <summary>
/// BarTender bağlantı noktası: nesnenin kendi (döndürülmemiş) kutusunda X/Y'nin gösterdiği yer.
/// 3 × 3 ızgara, satır satır: 0 sol üst … 8 sağ alt.
/// </summary>
public enum BtwAnchor
{
    TopLeft = 0,
    TopCenter = 1,
    TopRight = 2,
    MiddleLeft = 3,
    MiddleCenter = 4,
    MiddleRight = 5,
    BottomLeft = 6,
    BottomCenter = 7,
    BottomRight = 8,
}

/// <summary>Bir nesnenin etiket üzerindeki yeri. Uzunluklar inç/1000 (mil) cinsindendir.</summary>
public sealed record BtwPlacement(int X, int Y, BtwAnchor Anchor, double RotationDegrees, bool Found);

/// <summary>Windows LOGFONT alanları (BarTender yazı tipini bu yapıda saklar).</summary>
public sealed record BtwFont(int Height, int Escapement, int Weight, bool Italic, bool Underline, string Face)
{
    /// <summary>Punto. BarTender yüksekliği 1/20 punto (twip) olarak saklar.</summary>
    public double SizePoints => Math.Abs(Height) / 20.0;

    public bool Bold => Weight >= 600;
}

/// <summary>Etiket nesnesi (okunan ham bilgi).</summary>
public abstract record BtwObject(string Name, BtwPlacement Placement);

/// <summary>Yazı nesnesi.</summary>
/// <param name="FixedWidth">Sabit genişlikli metin kutusu (mil); null = metne göre otomatik genişlik.</param>
/// <param name="Alignment">0 sola, 1 ortaya, 2 sağa hizalı.</param>
/// <param name="WidthScale">Karakter genişliği, binde (1000 = %100).</param>
public sealed record BtwText(
    string Name,
    BtwPlacement Placement,
    string Content,
    BtwFont? Font,
    int? FixedWidth,
    int Alignment,
    int WidthScale,
    BtwBorder? Border) : BtwObject(Name, Placement);

/// <summary>Yazının çerçevesi.</summary>
/// <param name="Corner">0 düz köşe, 1 yuvarlak, 2 kesik köşe.</param>
public sealed record BtwBorder(int Thickness, int Corner, int CornerRadius);

/// <summary>Barkod nesnesi.</summary>
/// <param name="ClassName">BarTender sınıf adı (ör. BcEAN13Data).</param>
/// <param name="BarsWidth">Çubuk alanının genişliği (mil); 0 = okunamadı.</param>
/// <param name="BarHeight">Çubuk yüksekliği (mil); 0 = okunamadı.</param>
public sealed record BtwBarcode(
    string Name,
    BtwPlacement Placement,
    string ClassName,
    string Data,
    int BarsWidth,
    int BarHeight,
    BtwFont? TextFont) : BtwObject(Name, Placement);

/// <summary>Gömülü görsel.</summary>
public sealed record BtwPicture(
    string Name,
    BtwPlacement Placement,
    int Width,
    int Height,
    byte[] Data,
    string Extension,
    string? OriginalPath) : BtwObject(Name, Placement);

/// <summary>Kutu (dikdörtgen).</summary>
public sealed record BtwBox(
    string Name,
    BtwPlacement Placement,
    int Width,
    int Height,
    int Thickness,
    int Corner,
    int CornerRadius) : BtwObject(Name, Placement);

/// <summary>Çizgi. Bu klasördeki dosyalarda yalnızca basılmayan yardımcı çizgiler bulunduğu için aktarılmaz.</summary>
public sealed record BtwLine(string Name, BtwPlacement Placement) : BtwObject(Name, Placement);

/// <summary>Tanınmayan nesne (aktarılmaz, raporda gösterilir).</summary>
public sealed record BtwUnknown(string Name, BtwPlacement Placement, string ClassName) : BtwObject(Name, Placement);

/// <summary>Bir .btw dosyasından okunan etiket.</summary>
public sealed class BtwLabel
{
    /// <summary>Dosya başlığındaki uygulama sürümü (ör. "2016 R7", "10.1 SR4").</summary>
    public string? Application { get; init; }
    public string? Printer { get; init; }

    /// <summary>Dosyadaki Windows yazıcı ayarından (DEVMODE) okunan yazıcı çözünürlüğü; okunamadıysa null.</summary>
    public int? PrinterDpi { get; init; }
    public string? Title { get; init; }

    /// <summary>Tek etiketin ölçüsü (mil).</summary>
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>Rulo üzerinde yan yana (sütun) ve alt alta (satır) etiket sayısı.</summary>
    public int Columns { get; init; } = 1;
    public int Rows { get; init; } = 1;

    /// <summary>BarTender sayfasının (yan yana rulonun) genişliği, mil; okunamadıysa 0.</summary>
    public int PageWidth { get; init; }

    /// <summary>Sayfanın yan kenar payı, mil.</summary>
    public int SideMargin { get; init; }

    /// <summary>Yan yana etiketler arası boşluk (mil): sayfa genişliğinden kenar payları ve etiketler çıkınca kalan.</summary>
    public int ColumnGap => Columns > 1 && PageWidth > 0
        ? Math.Max(0, (PageWidth - 2 * SideMargin - Columns * Width) / (Columns - 1))
        : 0;

    /// <summary>Etiket ölçüsü dosyanın içinden değil, başlıktaki özetten (veya varsayılandan) alındıysa true.</summary>
    public bool SizeFromHeader { get; init; }

    public IReadOnlyList<BtwObject> Objects { get; init; } = [];

    /// <summary>Okuma sırasında atlanan veya tahmin edilen şeyler (Türkçe kaynak; arayüzde çevrilir).</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    public static double MilsToMm(double mils) => mils * 0.0254;
}

/// <summary>Dosya BarTender biçiminde değil ya da okunamıyor.</summary>
public sealed class BtwFormatException : Exception
{
    public BtwFormatException(string message, Exception? inner = null) : base(message, inner) { }
}
