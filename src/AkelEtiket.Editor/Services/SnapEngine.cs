using AkelEtiket.Core.Geometry;
using AkelEtiket.Core.Model;

namespace AkelEtiket.Editor.Services;

/// <summary>Ekranda gösterilecek hizalama çizgisi (mm).</summary>
public readonly record struct SnapLine(GuideOrientation Orientation, double Position);

/// <summary>Yakalama sonucu: düzeltilmiş kayma ve gösterilecek çizgiler.</summary>
public readonly record struct SnapResult(double DeltaX, double DeltaY, IReadOnlyList<SnapLine> Lines);

/// <summary>
/// Taşıma sırasında yakalama: kılavuz çizgileri, etiket kenarları/merkezi, diğer öğelerin
/// kenar/merkezleri ve ızgara. Eşik ekran pikselinden mm'ye çevrilerek verilir (yakınlaştırmadan bağımsız his).
/// </summary>
public static class SnapEngine
{
    public sealed record Options
    {
        public double ThresholdMm { get; init; } = 1.0;
        public bool SnapToGrid { get; init; } = true;
        public double GridSizeMm { get; init; } = 1.0;
        public bool SnapToObjects { get; init; } = true;
        public bool SnapToGuides { get; init; } = true;
    }

    /// <summary>
    /// Taşınan seçimin özgün sınırı + istenen kayma için yakalanmış kaymayı hesaplar.
    /// Önce nesne/kılavuz yakalaması denenir; yoksa ızgaraya (sol üst köşe) yuvarlanır.
    /// </summary>
    public static SnapResult SnapMove(RectMm original, double deltaX, double deltaY, LabelSetup label,
        IEnumerable<GuideLine> guides, IEnumerable<RectMm> others, Options options)
    {
        var moved = new RectMm(original.X + deltaX, original.Y + deltaY, original.Width, original.Height);
        var verticalTargets = new List<double>();
        var horizontalTargets = new List<double>();

        if (options.SnapToObjects)
        {
            verticalTargets.AddRange([0, label.WidthMm / 2, label.WidthMm]);
            horizontalTargets.AddRange([0, label.HeightMm / 2, label.HeightMm]);
            foreach (var r in others)
            {
                verticalTargets.AddRange([r.X, r.CenterX, r.Right]);
                horizontalTargets.AddRange([r.Y, r.CenterY, r.Bottom]);
            }
        }
        if (options.SnapToGuides)
        {
            foreach (var g in guides)
            {
                if (g.Orientation == GuideOrientation.Vertical) verticalTargets.Add(g.Position);
                else horizontalTargets.Add(g.Position);
            }
        }

        var lines = new List<SnapLine>();
        double dx = deltaX, dy = deltaY;

        var bestX = FindBest([moved.X, moved.CenterX, moved.Right], verticalTargets, options.ThresholdMm);
        if (bestX is { } bx)
        {
            dx += bx.Offset;
            lines.Add(new SnapLine(GuideOrientation.Vertical, bx.Target));
        }
        else if (options.SnapToGrid && options.GridSizeMm > 0)
        {
            dx = RoundTo(original.X + deltaX, options.GridSizeMm) - original.X;
        }

        var bestY = FindBest([moved.Y, moved.CenterY, moved.Bottom], horizontalTargets, options.ThresholdMm);
        if (bestY is { } by)
        {
            dy += by.Offset;
            lines.Add(new SnapLine(GuideOrientation.Horizontal, by.Target));
        }
        else if (options.SnapToGrid && options.GridSizeMm > 0)
        {
            dy = RoundTo(original.Y + deltaY, options.GridSizeMm) - original.Y;
        }

        return new SnapResult(dx, dy, lines);
    }

    /// <summary>Tek bir değeri (ör. boyutlandırmada kenar) hedeflere/ızgaraya yakalar.</summary>
    public static (double Value, SnapLine? Line) SnapValue(double value, GuideOrientation orientation, IEnumerable<double> targets, Options options)
    {
        double best = double.MaxValue, bestTarget = 0;
        foreach (var t in targets)
        {
            double d = Math.Abs(t - value);
            if (d < best)
            {
                best = d;
                bestTarget = t;
            }
        }
        if (best <= options.ThresholdMm) return (bestTarget, new SnapLine(orientation, bestTarget));
        if (options.SnapToGrid && options.GridSizeMm > 0) return (RoundTo(value, options.GridSizeMm), null);
        return (value, null);
    }

    public static double RoundTo(double value, double step) => Math.Round(Math.Round(value / step) * step, 6);

    private static (double Offset, double Target)? FindBest(double[] sources, List<double> targets, double threshold)
    {
        (double Offset, double Target)? best = null;
        double bestDistance = threshold;
        foreach (var source in sources)
        {
            foreach (var target in targets)
            {
                double distance = Math.Abs(target - source);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = (target - source, target);
                }
            }
        }
        return best;
    }
}
