using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace AkelEtiket.Core.Model;

/// <summary>Etiket ölçüleri ve medya bilgisi (mm).</summary>
public sealed class LabelSetup : ObservableModel
{
    public const double MinSizeMm = 5;
    public const double MaxSizeMm = 1000;

    private double _widthMm = 100;
    private double _heightMm = 100;
    private double _gapMm = 3;
    private double _gapOffsetMm;
    private MediaType _media = MediaType.Gap;
    private double _cornerRadiusMm = 2;

    public double WidthMm
    {
        get => _widthMm;
        set => Set(ref _widthMm, Clamp(value, MinSizeMm, MaxSizeMm, 100));
    }

    public double HeightMm
    {
        get => _heightMm;
        set => Set(ref _heightMm, Clamp(value, MinSizeMm, MaxSizeMm, 100));
    }

    /// <summary>Etiketler arası boşluk veya siyah işaret yüksekliği (mm).</summary>
    public double GapMm
    {
        get => _gapMm;
        set => Set(ref _gapMm, Clamp(value, 0, 30, 3));
    }

    /// <summary>Boşluk/işaret ofseti (mm); standart etiketlerde 0.</summary>
    public double GapOffsetMm
    {
        get => _gapOffsetMm;
        set => Set(ref _gapOffsetMm, Clamp(value, 0, 30, 0));
    }

    public MediaType Media
    {
        get => _media;
        set => Set(ref _media, value);
    }

    /// <summary>Yalnızca ekranda gösterilen etiket köşe yuvarlaklığı (mm).</summary>
    public double CornerRadiusMm
    {
        get => _cornerRadiusMm;
        set => Set(ref _cornerRadiusMm, Clamp(value, 0, 20, 2));
    }

    public LabelSetup Clone() => new()
    {
        WidthMm = WidthMm,
        HeightMm = HeightMm,
        GapMm = GapMm,
        GapOffsetMm = GapOffsetMm,
        Media = Media,
        CornerRadiusMm = CornerRadiusMm,
    };
}

/// <summary>Kullanıcının cetvelden çektiği kılavuz çizgisi.</summary>
public sealed class GuideLine : ObservableModel
{
    private GuideOrientation _orientation;
    private double _position;

    public GuideOrientation Orientation
    {
        get => _orientation;
        set => Set(ref _orientation, value);
    }

    /// <summary>Etiketin sol/üst kenarından uzaklık (mm).</summary>
    public double Position
    {
        get => _position;
        set => Set(ref _position, Clamp(value, -1000, 2000, 0));
    }
}

/// <summary>Şablonun en son kullandığı veri kaynağı (yeniden açarken önermek için).</summary>
public sealed class DataSourceLink
{
    public string FilePath { get; set; } = "";
    public string? SheetName { get; set; }
    public string? Delimiter { get; set; }
    public string? EncodingName { get; set; }
}

/// <summary>Etiket tasarım belgesi. Öğeler listedeki sırayla çizilir (ilk öğe en altta).</summary>
public sealed class LabelDocument : ObservableModel
{
    /// <summary>Bu sürümün okuyabildiği en yüksek biçim.</summary>
    public const int CurrentFormatVersion = 3;
    public const int Version2Format = 2;

    /// <summary>v1 (AKEL Etiket 1.x) biçimi.</summary>
    public const int Version1Format = 1;

    private string _name = "Yeni Etiket";
    private string _description = "";
    private LabelFormatting? _formatting;
    public LabelFormatting? Formatting { get => _formatting; set => Set(ref _formatting,value); }

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public string Name
    {
        get => _name;
        set => Set(ref _name, string.IsNullOrWhiteSpace(value) ? "Adsız Etiket" : value.Trim());
    }

    public string Description
    {
        get => _description;
        set => Set(ref _description, value ?? "");
    }

    public LabelSetup Label { get; set; } = new();

    public ObservableCollection<LabelElement> Elements { get; set; } = [];

    public ObservableCollection<GuideLine> Guides { get; set; } = [];

    public DataSourceLink? DataSource { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public bool HasFieldPlaceholders => Elements.Any(e => e.HasFieldPlaceholders);

    public LabelElement? FindElement(string id) => Elements.FirstOrDefault(e => e.Id == id);

    /// <summary>
    /// Belgeyi doğru açmak için gereken en düşük biçim sürümü. v2'ye özgü bir özellik (ör. grup) kullanılmıyorsa
    /// 1 döner; böylece dosya v1'de uyarısız açılır.
    /// </summary>
    public int GetRequiredFormatVersion() =>
        Formatting is not null ? CurrentFormatVersion : Elements.Any(e => e.GroupId is not null || e.VisibleWhen is not null
                          || e is ImageElement { Sharpen: > 0 } or ImageElement { SourceField: not null }
                          || e is BarcodeElement { Symbology: BarcodeType.Gs1DataMatrix or BarcodeType.Gs1QrCode })
            ? Version2Format
            : Version1Format;

    /// <summary>Belgedeki görsellerin kullandığı varlık kimlikleri.</summary>
    public IEnumerable<string> GetReferencedAssetIds() =>
        Elements.OfType<ImageElement>().Select(e => e.AssetId).Where(id => id.Length > 0).Distinct();

    /// <summary>Varsayılan 100 × 100 mm boş etiket.</summary>
    public static LabelDocument CreateBlank(double widthMm = 100, double heightMm = 100, string? name = null)
    {
        var doc = new LabelDocument { Name = name ?? "Yeni Etiket" };
        doc.Label.WidthMm = widthMm;
        doc.Label.HeightMm = heightMm;
        return doc;
    }
}
