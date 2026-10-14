using System.Text.Json.Serialization;

namespace AkelEtiket.Core.Model;

/// <summary>
/// Görsel nesnesi. Orijinal dosya proje içinde (<see cref="Assets.AssetStore"/>) saklanır;
/// tüm ayarlar her seferinde orijinal üzerinden yeniden uygulanır, kalite kaybı birikmez.
/// Nihai çıktı her zaman 1-bit siyah-beyazdır.
/// </summary>
public sealed class ImageElement : LabelElement
{
    private string _assetId = "";
    private ImageFitMode _fitMode = ImageFitMode.Contain;
    private double _cropLeft;
    private double _cropTop;
    private double _cropRight;
    private double _cropBottom;
    private ImageConversionMethod _method = ImageConversionMethod.Auto;
    private int _threshold = 128;
    private int _brightness;
    private int _contrast;
    private bool _invert;
    private int _sharpen;
    private string? _sourceField;

    public ImageElement()
    {
        Width = 30;
        Height = 30;
    }

    [JsonIgnore]
    public override string KindName => "Görsel";

    /// <summary>Proje içindeki orijinal görselin kimliği.</summary>
    public string AssetId
    {
        get => _assetId;
        set => Set(ref _assetId, value ?? "");
    }

    public ImageFitMode FitMode
    {
        get => _fitMode;
        set => Set(ref _fitMode, value);
    }

    /// <summary>Soldan kırpma oranı (0–0,9).</summary>
    public double CropLeft
    {
        get => _cropLeft;
        set => Set(ref _cropLeft, Clamp(value, 0, 0.9, 0));
    }

    public double CropTop
    {
        get => _cropTop;
        set => Set(ref _cropTop, Clamp(value, 0, 0.9, 0));
    }

    public double CropRight
    {
        get => _cropRight;
        set => Set(ref _cropRight, Clamp(value, 0, 0.9, 0));
    }

    public double CropBottom
    {
        get => _cropBottom;
        set => Set(ref _cropBottom, Clamp(value, 0, 0.9, 0));
    }

    [JsonIgnore]
    public bool HasCrop => _cropLeft > 0 || _cropTop > 0 || _cropRight > 0 || _cropBottom > 0;

    public ImageConversionMethod Method
    {
        get => _method;
        set => Set(ref _method, value);
    }

    /// <summary>Eşik değeri (1–254). Eşik altındaki parlaklık siyah olur.</summary>
    public int Threshold
    {
        get => _threshold;
        set => Set(ref _threshold, Math.Clamp(value, 1, 254));
    }

    /// <summary>Parlaklık (-100 … +100).</summary>
    public int Brightness
    {
        get => _brightness;
        set => Set(ref _brightness, Math.Clamp(value, -100, 100));
    }

    /// <summary>Kontrast (-100 … +100).</summary>
    public int Contrast
    {
        get => _contrast;
        set => Set(ref _contrast, Math.Clamp(value, -100, 100));
    }

    /// <summary>Siyah/beyaz ters çevir.</summary>
    public bool Invert
    {
        get => _invert;
        set => Set(ref _invert, value);
    }

    /// <summary>
    /// Satıra göre değişen görsel (v2): veri sütununun adı (ör. "Gorsel"). Doluysa her etikette bu sütundaki dosya
    /// adı/yolu kullanılır; <see cref="AssetId"/> tasarım önizlemesinde gösterilen yedek görseldir.
    /// </summary>
    public string? SourceField
    {
        get => _sourceField;
        set
        {
            if (Set(ref _sourceField, string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('{', '}').Trim()))
                OnPropertyChanged(nameof(HasFieldPlaceholders));
        }
    }

    [JsonIgnore]
    public override bool HasFieldPlaceholders => base.HasFieldPlaceholders || SourceField is not null;

    /// <summary>Kenar keskinleştirme (0 = kapalı … 100 = güçlü). v2; v1 bu alanı yok sayar.</summary>
    public int Sharpen
    {
        get => _sharpen;
        set => Set(ref _sharpen, Math.Clamp(value, 0, 100));
    }

    /// <summary>Kırpma oranlarının geçerli (görünür alan kalan) hâli.</summary>
    public (double Left, double Top, double Right, double Bottom) GetEffectiveCrop()
    {
        double l = _cropLeft, r = _cropRight, t = _cropTop, b = _cropBottom;
        if (l + r > 0.95)
        {
            double s = 0.95 / (l + r);
            l *= s;
            r *= s;
        }
        if (t + b > 0.95)
        {
            double s = 0.95 / (t + b);
            t *= s;
            b *= s;
        }
        return (l, t, r, b);
    }
}
