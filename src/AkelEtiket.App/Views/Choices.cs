using AkelEtiket.Core.Model;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.App.Views;

/// <summary>Açılır listeler için Türkçe seçenek listeleri (XAML'de x:Static ile kullanılır).</summary>
public static class Choices
{
    public static IReadOnlyList<KeyValuePair<ImageConversionMethod, string>> ImageMethods { get; } =
    [
        new(ImageConversionMethod.Auto, L10n.T("Otomatik (görsel türüne göre)")),
        new(ImageConversionMethod.Logo, L10n.T("Logo / çizim (keskin kenar)")),
        new(ImageConversionMethod.Photo, L10n.T("Fotoğraf (detay koruyan)")),
        new(ImageConversionMethod.Threshold, L10n.T("Eşik değeri")),
        new(ImageConversionMethod.Otsu, L10n.T("Otsu (otomatik eşik)")),
        new(ImageConversionMethod.FloydSteinberg, "Floyd–Steinberg dithering"),
        new(ImageConversionMethod.Ordered, L10n.T("Düzenli (Bayer) dithering")),
    ];

    public static IReadOnlyList<KeyValuePair<ConditionOperator, string>> ConditionOperators { get; } =
    [
        new(ConditionOperator.NotEmpty, L10n.T("dolu ise")),
        new(ConditionOperator.Empty, L10n.T("boş ise")),
        new(ConditionOperator.IsEqual, L10n.T("eşittir")),
        new(ConditionOperator.NotEqual, L10n.T("eşit değildir")),
        new(ConditionOperator.Contains, L10n.T("içerir")),
    ];

    public static IReadOnlyList<KeyValuePair<ImageFitMode, string>> FitModes { get; } =
    [
        new(ImageFitMode.Contain, L10n.T("Sığdır (oran korunur)")),
        new(ImageFitMode.Cover, L10n.T("Doldur (taşan kırpılır)")),
        new(ImageFitMode.Stretch, L10n.T("Uzat (oran bozulur)")),
    ];

    public static IReadOnlyList<KeyValuePair<InkColor, string>> InkColors { get; } =
    [
        new(InkColor.Black, L10n.T("Siyah")),
        new(InkColor.White, L10n.T("Beyaz")),
    ];

    public static IReadOnlyList<KeyValuePair<FillStyle, string>> Fills { get; } =
    [
        new(FillStyle.None, L10n.T("Yok (saydam)")),
        new(FillStyle.Black, L10n.T("Siyah")),
        new(FillStyle.White, L10n.T("Beyaz")),
    ];

    public static IReadOnlyList<KeyValuePair<ShapeKind, string>> ShapeKinds { get; } =
    [
        new(ShapeKind.Rectangle, L10n.T("Dikdörtgen")),
        new(ShapeKind.Ellipse, "Elips / daire"),
        new(ShapeKind.Line, L10n.T("Çizgi")),
    ];

    public static IReadOnlyList<KeyValuePair<LineOrientation, string>> LineOrientations { get; } =
    [
        new(LineOrientation.Horizontal, L10n.T("Yatay")),
        new(LineOrientation.Vertical, L10n.T("Dikey")),
        new(LineOrientation.DiagonalDown, L10n.T("Çapraz (↘)")),
        new(LineOrientation.DiagonalUp, L10n.T("Çapraz (↗)")),
    ];

    public static IReadOnlyList<KeyValuePair<QrErrorCorrectionLevel, string>> QrLevels { get; } =
    [
        new(QrErrorCorrectionLevel.L, L10n.T("L – düşük (%7)")),
        new(QrErrorCorrectionLevel.M, L10n.T("M – orta (%15)")),
        new(QrErrorCorrectionLevel.Q, L10n.T("Q – yüksek (%25)")),
        new(QrErrorCorrectionLevel.H, L10n.T("H – en yüksek (%30)")),
    ];

    public static IReadOnlyList<KeyValuePair<DataMatrixShape, string>> DataMatrixShapes { get; } =
    [
        new(DataMatrixShape.Square, L10n.T("Kare")),
        new(DataMatrixShape.Rectangle, L10n.T("Dikdörtgen")),
        new(DataMatrixShape.Auto, L10n.T("Otomatik")),
    ];

    public static IReadOnlyList<KeyValuePair<int, string>> Pdf417Levels { get; } =
        [new(-1, L10n.T("Otomatik")), .. Enumerable.Range(0, 9).Select(i => new KeyValuePair<int, string>(i, $"Seviye {i}"))];

    public static IReadOnlyList<KeyValuePair<double, string>> QuarterRotations { get; } =
    [
        new(0, "0°"),
        new(90, "90°"),
        new(180, "180°"),
        new(270, "270°"),
    ];
}
