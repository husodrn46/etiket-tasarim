using AkelEtiket.Core.Model;

namespace AkelEtiket.Core.Geometry;

/// <summary>Yazıcı noktası cinsinden tam sayı dikdörtgen.</summary>
public readonly record struct DotRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>Milimetre cinsinden dikdörtgen.</summary>
public readonly record struct RectMm(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;

    public bool IntersectsWith(RectMm other) =>
        other.X < Right && X < other.Right && other.Y < Bottom && Y < other.Bottom;

    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;

    public static RectMm Union(RectMm a, RectMm b)
    {
        double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
        return new RectMm(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
    }

    public static RectMm FromPoints(double x1, double y1, double x2, double y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
}

/// <summary>
/// Öğe geometrisi: mm → yazıcı noktası dönüşümü, döndürülmüş köşeler, isabet testi.
/// Öğe içeriği yalnızca kutu BOYUTUNA bağlıdır; konum yalnızca katmanı kaydırır.
/// Böylece sürükleme sırasında yeniden çizim gerekmez ve barkod modülleri değişmez.
/// </summary>
public static class ElementGeometry
{
    /// <summary>Öğenin döndürülmemiş kutusunun yazıcı noktası karşılığı.</summary>
    public static DotRect GetDotBox(LabelElement element, double dotsPerMm) =>
        new(Units.MmToDotsRounded(element.X, dotsPerMm),
            Units.MmToDotsRounded(element.Y, dotsPerMm),
            Math.Max(1, Units.MmToDotsRounded(element.Width, dotsPerMm)),
            Math.Max(1, Units.MmToDotsRounded(element.Height, dotsPerMm)));

    /// <summary>Döndürme 90°'nin katıysa çeyrek tur sayısı (0–3), değilse -1.</summary>
    public static int GetQuarterTurns(double rotationDegrees)
    {
        double r = rotationDegrees % 360.0;
        if (r < 0) r += 360.0;
        double q = Math.Round(r / 90.0);
        if (Math.Abs(r - q * 90.0) > 0.01) return -1;
        return (int)q % 4;
    }

    /// <summary>Döndürülmüş kutunun köşeleri (sol üst, sağ üst, sağ alt, sol alt), mm.</summary>
    public static (double X, double Y)[] GetCornersMm(LabelElement e) =>
        GetCorners(e.X, e.Y, e.Width, e.Height, e.Rotation);

    public static (double X, double Y)[] GetCorners(double x, double y, double w, double h, double rotationDegrees)
    {
        double cx = x + w / 2, cy = y + h / 2;
        double rad = rotationDegrees * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        (double X, double Y) Rot(double px, double py)
        {
            double dx = px - cx, dy = py - cy;
            return (cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
        }
        return [Rot(x, y), Rot(x + w, y), Rot(x + w, y + h), Rot(x, y + h)];
    }

    /// <summary>Döndürülmüş öğenin eksenlere hizalı sınır kutusu (mm).</summary>
    public static RectMm GetRotatedBoundsMm(LabelElement e)
    {
        if (e.Rotation == 0) return new RectMm(e.X, e.Y, e.Width, e.Height);
        var corners = GetCornersMm(e);
        double minX = corners.Min(c => c.X), maxX = corners.Max(c => c.X);
        double minY = corners.Min(c => c.Y), maxY = corners.Max(c => c.Y);
        return new RectMm(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>Etiket koordinatındaki noktayı öğenin döndürülmemiş yerel koordinatına çevirir (mm).</summary>
    public static (double X, double Y) ToLocal(LabelElement e, double xMm, double yMm)
    {
        double cx = e.X + e.Width / 2, cy = e.Y + e.Height / 2;
        double rad = -e.Rotation * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        double dx = xMm - cx, dy = yMm - cy;
        return (dx * cos - dy * sin + e.Width / 2, dx * sin + dy * cos + e.Height / 2);
    }

    /// <summary>Nokta öğenin (döndürülmüş) kutusunun içinde mi?</summary>
    public static bool HitTest(LabelElement e, double xMm, double yMm, double toleranceMm = 0)
    {
        var (lx, ly) = ToLocal(e, xMm, yMm);
        return lx >= -toleranceMm && ly >= -toleranceMm &&
               lx <= e.Width + toleranceMm && ly <= e.Height + toleranceMm;
    }
}
