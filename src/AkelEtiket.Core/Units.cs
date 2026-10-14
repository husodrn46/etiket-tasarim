namespace AkelEtiket.Core;

/// <summary>
/// Fiziksel ölçü dönüşümleri. Tasarım modeli her zaman milimetre (mm) kullanır;
/// ekran (DIP) ve yazıcı noktası (dot) değerleri yalnızca çizim anında hesaplanır.
/// </summary>
public static class Units
{
    public const double MmPerInch = 25.4;
    public const double PointsPerInch = 72.0;
    public const double WpfDipPerInch = 96.0;

    public static double DpiToDotsPerMm(double dpi) => dpi / MmPerInch;

    public static double DotsPerMmToDpi(double dotsPerMm) => dotsPerMm * MmPerInch;

    public static double MmToDots(double mm, double dotsPerMm) => mm * dotsPerMm;

    /// <summary>mm değerini en yakın tam yazıcı noktasına yuvarlar (0,5 yukarı).</summary>
    public static int MmToDotsRounded(double mm, double dotsPerMm) =>
        (int)Math.Floor(mm * dotsPerMm + 0.5 + 1e-9);

    public static double DotsToMm(double dots, double dotsPerMm) => dots / dotsPerMm;

    public static double PointsToMm(double points) => points * MmPerInch / PointsPerInch;

    public static double MmToPoints(double mm) => mm * PointsPerInch / MmPerInch;

    public static double MmToDip(double mm) => mm * WpfDipPerInch / MmPerInch;

    public static double DipToMm(double dip) => dip * MmPerInch / WpfDipPerInch;

    /// <summary>1-bit satır için gereken bayt sayısı (8 noktaya yukarı yuvarlanır).</summary>
    public static int BytesPerRow(int widthDots) => (widthDots + 7) / 8;
}

/// <summary>
/// Yazıcı çözünürlüğü. 203 DPI termal yazıcılar gerçekte 8 nokta/mm (203,2 DPI) çalışır;
/// TSPL de mm değerlerini bu oranla noktaya çevirir. Bu nedenle 100 mm = 800 nokta.
/// </summary>
public sealed record PrintResolution(string Name, double DotsPerMm)
{
    public double Dpi => DotsPerMm * Units.MmPerInch;

    public static readonly PrintResolution Dpi203 = new("203 DPI (8 nokta/mm)", 8.0);
    public static readonly PrintResolution Dpi300 = new("300 DPI (12 nokta/mm)", 12.0);

    public static IReadOnlyList<PrintResolution> Standard { get; } = [Dpi203, Dpi300];

    public static PrintResolution FromDotsPerMm(double dotsPerMm) =>
        Standard.FirstOrDefault(r => Math.Abs(r.DotsPerMm - dotsPerMm) < 0.001)
        ?? new PrintResolution($"{dotsPerMm * Units.MmPerInch:0.#} DPI", dotsPerMm);

    public override string ToString() => Name;
}
