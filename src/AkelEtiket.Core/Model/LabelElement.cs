using System.Text.Json.Serialization;

namespace AkelEtiket.Core.Model;

/// <summary>
/// Etiket üzerindeki tüm nesnelerin ortak tabanı. Konum ve ölçüler milimetredir;
/// (X, Y) döndürülmemiş kutunun sol üst köşesidir, döndürme kutu merkezi etrafında saat yönündedir.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextElement), "text")]
[JsonDerivedType(typeof(BarcodeElement), "barcode")]
[JsonDerivedType(typeof(ImageElement), "image")]
[JsonDerivedType(typeof(ShapeElement), "shape")]
public abstract class LabelElement : ObservableModel
{
    public const double MinSizeMm = 0.1;
    public const double MaxSizeMm = 2000;
    public const double MaxPositionMm = 5000;

    private string _id = NewId();
    private string _name = "";
    private double _x;
    private double _y;
    private double _width = 30;
    private double _height = 10;
    private double _rotation;
    private bool _locked;
    private bool _visible = true;
    private string? _groupId;
    private VisibilityCondition? _visibleWhen;

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    public string Id
    {
        get => _id;
        set => Set(ref _id, string.IsNullOrWhiteSpace(value) ? NewId() : value);
    }

    /// <summary>Katmanlar panelinde görünen ad (boşsa türe göre otomatik ad gösterilir).</summary>
    public string Name
    {
        get => _name;
        set => Set(ref _name, value ?? "");
    }

    public double X
    {
        get => _x;
        set => Set(ref _x, Clamp(value, -MaxPositionMm, MaxPositionMm, 0));
    }

    public double Y
    {
        get => _y;
        set => Set(ref _y, Clamp(value, -MaxPositionMm, MaxPositionMm, 0));
    }

    public double Width
    {
        get => _width;
        set => Set(ref _width, Clamp(value, MinSizeMm, MaxSizeMm, 10));
    }

    public double Height
    {
        get => _height;
        set => Set(ref _height, Clamp(value, MinSizeMm, MaxSizeMm, 10));
    }

    /// <summary>Saat yönünde derece, [0, 360) aralığında saklanır.</summary>
    public double Rotation
    {
        get => _rotation;
        set => Set(ref _rotation, NormalizeRotation(value));
    }

    public bool Locked
    {
        get => _locked;
        set => Set(ref _locked, value);
    }

    public bool Visible
    {
        get => _visible;
        set => Set(ref _visible, value);
    }

    /// <summary>
    /// Grup kimliği (v2). Aynı kimliği taşıyan öğeler birlikte seçilir, taşınır, hizalanır ve boyutlandırılır.
    /// Boş/null = grupsuz. v1 bu alanı tanımaz ve yok sayar (öğeler tek tek görünür).
    /// </summary>
    /// <summary>
    /// Koşullu gösterme (v2): doluysa öğe yalnızca koşul sağlandığında basılır (ör. {Indirim} doluysa). Koşul her
    /// etikette o etiketin verisiyle değerlendirilir. Alan adı boşsa koşul yok sayılır. v1 bu alanı tanımaz.
    /// </summary>
    public VisibilityCondition? VisibleWhen
    {
        get => _visibleWhen;
        set
        {
            var cleaned = value is null || VisibilityCondition.CleanField(value.Field).Length == 0
                ? null
                : value with { Field = VisibilityCondition.CleanField(value.Field) };
            if (Set(ref _visibleWhen, cleaned)) OnPropertyChanged(nameof(HasFieldPlaceholders));
        }
    }

    public string? GroupId
    {
        get => _groupId;
        set => Set(ref _groupId, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    /// <summary>Türün Türkçe adı (Metin, Barkod, ...).</summary>
    [JsonIgnore]
    public abstract string KindName { get; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? KindName : Name;

    /// <summary>Öğe değişken veri alanı (ör. {UrunAdi}) içeriyor mu?</summary>
    [JsonIgnore]
    public virtual bool HasFieldPlaceholders => VisibleWhen is not null;

    protected virtual double NormalizeRotation(double degrees)
    {
        if (double.IsNaN(degrees) || double.IsInfinity(degrees)) return 0;
        double r = degrees % 360.0;
        if (r < 0) r += 360.0;
        if (r >= 360.0 - 1e-9) r = 0;
        return Math.Round(r, 4);
    }

    /// <summary>Konum ve boyutu birlikte değiştirir.</summary>
    public void SetBounds(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}
