using System.Text.Json.Serialization;
using AkelEtiket.Core.Data;

namespace AkelEtiket.Core.Model;

/// <summary>Metin kutusu. Metin {Alan} biçiminde değişken veri alanları içerebilir.</summary>
public sealed class TextElement : LabelElement
{
    public const string DefaultFontFamily = "Arial";

    private string _text = "Metin";
    private string _fontFamily = DefaultFontFamily;
    private double _fontSize = 12;
    private bool _bold;
    private bool _italic;
    private bool _underline;
    private TextHorizontalAlignment _horizontalAlignment = TextHorizontalAlignment.Left;
    private TextVerticalAlignment _verticalAlignment = TextVerticalAlignment.Top;
    private bool _wordWrap = true;
    private bool _autoShrink;
    private double _lineSpacing = 1.0;
    private InkColor _textColor = InkColor.Black;
    private FillStyle _background = FillStyle.None;

    public TextElement()
    {
        Width = 40;
        Height = 8;
    }

    [JsonIgnore]
    public override string KindName => "Metin";

    public string Text
    {
        get => _text;
        set
        {
            if (Set(ref _text, value ?? "")) OnPropertyChanged(nameof(HasFieldPlaceholders));
        }
    }

    public string FontFamily
    {
        get => _fontFamily;
        set => Set(ref _fontFamily, string.IsNullOrWhiteSpace(value) ? DefaultFontFamily : value.Trim());
    }

    /// <summary>Yazı boyutu (punto). 1 pt = 0,3528 mm.</summary>
    public double FontSize
    {
        get => _fontSize;
        set => Set(ref _fontSize, Clamp(value, 2, 500, 12));
    }

    public bool Bold
    {
        get => _bold;
        set => Set(ref _bold, value);
    }

    public bool Italic
    {
        get => _italic;
        set => Set(ref _italic, value);
    }

    public bool Underline
    {
        get => _underline;
        set => Set(ref _underline, value);
    }

    public TextHorizontalAlignment HorizontalAlignment
    {
        get => _horizontalAlignment;
        set => Set(ref _horizontalAlignment, value);
    }

    public TextVerticalAlignment VerticalAlignment
    {
        get => _verticalAlignment;
        set => Set(ref _verticalAlignment, value);
    }

    /// <summary>Kutu genişliğinde satır kaydır.</summary>
    public bool WordWrap
    {
        get => _wordWrap;
        set => Set(ref _wordWrap, value);
    }

    /// <summary>Metin kutuya sığmazsa yazı boyutunu otomatik küçült.</summary>
    public bool AutoShrink
    {
        get => _autoShrink;
        set => Set(ref _autoShrink, value);
    }

    /// <summary>Satır aralığı çarpanı (1,0 = normal).</summary>
    public double LineSpacing
    {
        get => _lineSpacing;
        set => Set(ref _lineSpacing, Clamp(value, 0.5, 3.0, 1.0));
    }

    public InkColor TextColor
    {
        get => _textColor;
        set => Set(ref _textColor, value);
    }

    public FillStyle Background
    {
        get => _background;
        set => Set(ref _background, value);
    }

    [JsonIgnore]
    public override bool HasFieldPlaceholders => base.HasFieldPlaceholders || FieldTemplate.ContainsFields(Text);
}
