using AkelEtiket.Core.Model;

namespace AkelEtiket.App.Views;

/// <summary>Açılır listeler için Türkçe seçenek listeleri (XAML'de x:Static ile kullanılır).</summary>
public static class Choices
{
    public static IReadOnlyList<KeyValuePair<ImageConversionMethod, string>> ImageMethods { get; } =
    [
        new(ImageConversionMethod.Auto, "Otomatik (görsel türüne göre)"),
        new(ImageConversionMethod.Logo, "Logo / çizim (keskin kenar)"),
        new(ImageConversionMethod.Photo, "Fotoğraf (detay koruyan)"),
        new(ImageConversionMethod.Threshold, "Eşik değeri"),
        new(ImageConversionMethod.Otsu, "Otsu (otomatik eşik)"),
        new(ImageConversionMethod.FloydSteinberg, "Floyd–Steinberg dithering"),
        new(ImageConversionMethod.Ordered, "Düzenli (Bayer) dithering"),
    ];

    public static IReadOnlyList<KeyValuePair<ConditionOperator, string>> ConditionOperators { get; } =
    [
        new(ConditionOperator.NotEmpty, "dolu ise"),
        new(ConditionOperator.Empty, "boş ise"),
        new(ConditionOperator.IsEqual, "eşittir"),
        new(ConditionOperator.NotEqual, "eşit değildir"),
        new(ConditionOperator.Contains, "içerir"),
    ];

    public static IReadOnlyList<KeyValuePair<ImageFitMode, string>> FitModes { get; } =
    [
        new(ImageFitMode.Contain, "Sığdır (oran korunur)"),
        new(ImageFitMode.Cover, "Doldur (taşan kırpılır)"),
        new(ImageFitMode.Stretch, "Uzat (oran bozulur)"),
    ];

    public static IReadOnlyList<KeyValuePair<InkColor, string>> InkColors { get; } =
    [
        new(InkColor.Black, "Siyah"),
        new(InkColor.White, "Beyaz"),
    ];

    public static IReadOnlyList<KeyValuePair<FillStyle, string>> Fills { get; } =
    [
        new(FillStyle.None, "Yok (saydam)"),
        new(FillStyle.Black, "Siyah"),
        new(FillStyle.White, "Beyaz"),
    ];

    public static IReadOnlyList<KeyValuePair<ShapeKind, string>> ShapeKinds { get; } =
    [
        new(ShapeKind.Rectangle, "Dikdörtgen"),
        new(ShapeKind.Ellipse, "Elips / daire"),
        new(ShapeKind.Line, "Çizgi"),
    ];

    public static IReadOnlyList<KeyValuePair<LineOrientation, string>> LineOrientations { get; } =
    [
        new(LineOrientation.Horizontal, "Yatay"),
        new(LineOrientation.Vertical, "Dikey"),
        new(LineOrientation.DiagonalDown, "Çapraz (↘)"),
        new(LineOrientation.DiagonalUp, "Çapraz (↗)"),
    ];

    public static IReadOnlyList<KeyValuePair<QrErrorCorrectionLevel, string>> QrLevels { get; } =
    [
        new(QrErrorCorrectionLevel.L, "L – düşük (%7)"),
        new(QrErrorCorrectionLevel.M, "M – orta (%15)"),
        new(QrErrorCorrectionLevel.Q, "Q – yüksek (%25)"),
        new(QrErrorCorrectionLevel.H, "H – en yüksek (%30)"),
    ];

    public static IReadOnlyList<KeyValuePair<DataMatrixShape, string>> DataMatrixShapes { get; } =
    [
        new(DataMatrixShape.Square, "Kare"),
        new(DataMatrixShape.Rectangle, "Dikdörtgen"),
        new(DataMatrixShape.Auto, "Otomatik"),
    ];

    public static IReadOnlyList<KeyValuePair<int, string>> Pdf417Levels { get; } =
        [new(-1, "Otomatik"), .. Enumerable.Range(0, 9).Select(i => new KeyValuePair<int, string>(i, $"Seviye {i}"))];

    public static IReadOnlyList<KeyValuePair<double, string>> QuarterRotations { get; } =
    [
        new(0, "0°"),
        new(90, "90°"),
        new(180, "180°"),
        new(270, "270°"),
    ];
}
