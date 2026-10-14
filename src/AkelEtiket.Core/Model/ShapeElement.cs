using System.Text.Json.Serialization;

namespace AkelEtiket.Core.Model;

/// <summary>Dikdörtgen, elips/daire ve çizgi.</summary>
public sealed class ShapeElement : LabelElement
{
    private ShapeKind _kind = ShapeKind.Rectangle;
    private double _strokeWidth = 0.5;
    private InkColor _strokeColor = InkColor.Black;
    private FillStyle _fill = FillStyle.None;
    private double _cornerRadius;
    private LineOrientation _lineOrientation = LineOrientation.Horizontal;

    public ShapeElement()
    {
        Width = 30;
        Height = 20;
    }

    [JsonIgnore]
    public override string KindName => Kind switch
    {
        ShapeKind.Ellipse => "Elips",
        ShapeKind.Line => "Çizgi",
        _ => "Dikdörtgen",
    };

    public ShapeKind Kind
    {
        get => _kind;
        set
        {
            if (!Set(ref _kind, value)) return;
            OnPropertyChanged(nameof(KindName));
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>Çizgi kalınlığı (mm). 0 = kontur yok.</summary>
    public double StrokeWidth
    {
        get => _strokeWidth;
        set => Set(ref _strokeWidth, Clamp(value, 0, 50, 0.5));
    }

    public InkColor StrokeColor
    {
        get => _strokeColor;
        set => Set(ref _strokeColor, value);
    }

    public FillStyle Fill
    {
        get => _fill;
        set => Set(ref _fill, value);
    }

    /// <summary>Dikdörtgen köşe yarıçapı (mm).</summary>
    public double CornerRadius
    {
        get => _cornerRadius;
        set => Set(ref _cornerRadius, Clamp(value, 0, 500, 0));
    }

    /// <summary>Çizgi yönü (yalnızca <see cref="ShapeKind.Line"/>).</summary>
    public LineOrientation LineOrientation
    {
        get => _lineOrientation;
        set => Set(ref _lineOrientation, value);
    }
}
