using System.Text.Json.Serialization;
using AkelEtiket.Core.Data;

namespace AkelEtiket.Core.Model;

/// <summary>
/// 1D ve 2D barkod nesnesi. Barkod, kutu içine yazıcı noktasına hizalı tam sayı modül
/// genişliğiyle yerleştirilir (keskin baskı için). Döndürme yalnızca 90°'nin katlarıdır.
/// </summary>
public sealed class BarcodeElement : LabelElement
{
    private BarcodeType _symbology = BarcodeType.Code128;
    private string _content = "DEMO001";
    private bool _showText = true;
    private string _textFontFamily = TextElement.DefaultFontFamily;
    private double _textSize;
    private bool _quietZone = true;
    private double _wideToNarrowRatio = 2.5;
    private bool _appendCheckDigit;
    private QrErrorCorrectionLevel _qrErrorCorrection = QrErrorCorrectionLevel.M;
    private DataMatrixShape _dataMatrixShape = DataMatrixShape.Square;
    private int _pdf417ErrorCorrection = -1;
    private int _pdf417Columns;
    private bool _bearerBars = true;

    public BarcodeElement()
    {
        Width = 50;
        Height = 20;
    }

    [JsonIgnore]
    public override string KindName => Is2D ? Symbology switch
    {
        BarcodeType.QrCode => "QR Kod",
        BarcodeType.DataMatrix => "Data Matrix",
        BarcodeType.Gs1DataMatrix => "GS1 DataMatrix",
        BarcodeType.Gs1QrCode => "GS1 QR Kod",
        _ => "PDF417",
    } : "Barkod";

    [JsonIgnore]
    public bool Is2D => IsTwoDimensional(Symbology);

    public static bool IsTwoDimensional(BarcodeType type) =>
        type is BarcodeType.QrCode or BarcodeType.DataMatrix or BarcodeType.Pdf417 or BarcodeType.Gs1DataMatrix or BarcodeType.Gs1QrCode;

    /// <summary>İçeriği GS1 Uygulama Tanımlayıcılarıyla ((01)…(10)…) yazılan türler.</summary>
    public static bool IsGs1(BarcodeType type) =>
        type is BarcodeType.Gs1_128 or BarcodeType.Gs1DataMatrix or BarcodeType.Gs1QrCode;

    public BarcodeType Symbology
    {
        get => _symbology;
        set
        {
            if (!Set(ref _symbology, value)) return;
            OnPropertyChanged(nameof(Is2D));
            OnPropertyChanged(nameof(KindName));
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>Barkod içeriği; {Alan} değişkenleri içerebilir.</summary>
    public string Content
    {
        get => _content;
        set
        {
            if (Set(ref _content, value ?? "")) OnPropertyChanged(nameof(HasFieldPlaceholders));
        }
    }

    /// <summary>1D barkodun altında okunabilir metni göster.</summary>
    public bool ShowText
    {
        get => _showText;
        set => Set(ref _showText, value);
    }

    public string TextFontFamily
    {
        get => _textFontFamily;
        set => Set(ref _textFontFamily, string.IsNullOrWhiteSpace(value) ? TextElement.DefaultFontFamily : value.Trim());
    }

    /// <summary>Okunabilir metin boyutu (punto); 0 = modül genişliğine göre otomatik.</summary>
    public double TextSize
    {
        get => _textSize;
        set => Set(ref _textSize, Clamp(value, 0, 72, 0));
    }

    /// <summary>Standart sessiz bölgeleri (beyaz kenar boşluğu) kutunun içine dahil et.</summary>
    public bool QuietZone
    {
        get => _quietZone;
        set => Set(ref _quietZone, value);
    }

    /// <summary>Code 39 ve ITF için geniş/dar çubuk oranı (2,0–3,0).</summary>
    public double WideToNarrowRatio
    {
        get => _wideToNarrowRatio;
        set => Set(ref _wideToNarrowRatio, Clamp(value, 2.0, 3.0, 2.5));
    }

    /// <summary>İsteğe bağlı kontrol karakteri ekle (Code 39: mod 43, ITF: mod 10).</summary>
    public bool AppendCheckDigit
    {
        get => _appendCheckDigit;
        set => Set(ref _appendCheckDigit, value);
    }

    public QrErrorCorrectionLevel QrErrorCorrection
    {
        get => _qrErrorCorrection;
        set => Set(ref _qrErrorCorrection, value);
    }

    public DataMatrixShape DataMatrixShape
    {
        get => _dataMatrixShape;
        set => Set(ref _dataMatrixShape, value);
    }

    /// <summary>PDF417 hata düzeltme seviyesi 0–8; -1 = otomatik.</summary>
    public int Pdf417ErrorCorrection
    {
        get => _pdf417ErrorCorrection;
        set => Set(ref _pdf417ErrorCorrection, Math.Clamp(value, -1, 8));
    }

    /// <summary>PDF417 veri sütunu sayısı 1–30; 0 = otomatik.</summary>
    public int Pdf417Columns
    {
        get => _pdf417Columns;
        set => Set(ref _pdf417Columns, Math.Clamp(value, 0, 30));
    }

    /// <summary>ITF-14 taşıyıcı çerçeve (bearer bar).</summary>
    public bool BearerBars
    {
        get => _bearerBars;
        set => Set(ref _bearerBars, value);
    }

    [JsonIgnore]
    public override bool HasFieldPlaceholders => base.HasFieldPlaceholders || FieldTemplate.ContainsFields(Content);

    /// <summary>Barkodlar keskin kalsın diye döndürme 90°'nin katlarına yuvarlanır.</summary>
    protected override double NormalizeRotation(double degrees)
    {
        double r = base.NormalizeRotation(degrees);
        return (Math.Round(r / 90.0) * 90.0) % 360.0;
    }
}
