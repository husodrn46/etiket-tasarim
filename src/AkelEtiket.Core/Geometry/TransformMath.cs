using AkelEtiket.Core.Model;

namespace AkelEtiket.Core.Geometry;

/// <summary>Boyutlandırma tutamacı.</summary>
public enum ResizeHandle
{
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

/// <summary>Hizalama komutları.</summary>
public enum AlignMode
{
    Left,
    CenterHorizontal,
    Right,
    Top,
    CenterVertical,
    Bottom,
    DistributeHorizontal,
    DistributeVertical,
}

/// <summary>Hizalamanın neye göre yapılacağı.</summary>
public enum AlignReference
{
    /// <summary>Tek öğe (veya tek grup) → etikete; birden fazla → seçimin sınırlarına.</summary>
    Auto,
    /// <summary>Seçilen öğelerin ortak sınırları.</summary>
    Selection,
    /// <summary>Etiketin kendisi.</summary>
    Label,
}

/// <summary>
/// Taşıma/boyutlandırma/döndürme ve hizalama hesapları (birim testlerle doğrulanır). Editör (fare) ve yapay zekâ
/// araçları aynı hesapları kullanır. Aynı <see cref="LabelElement.GroupId"/> değerine sahip öğeler hizalama ve
/// dağıtmada tek bir birim gibi taşınır.
/// </summary>
public static class TransformMath
{
    public const double MinSizeMm = 0.5;

    /// <summary>
    /// Döndürülmüş bir öğeyi tutamaçtan boyutlandırır. Fare hareketi öğenin yerel eksenine çevrilir;
    /// karşı köşe/kenar sabit kalır. <paramref name="keepAspect"/> köşe tutamaçlarında oranı korur.
    /// </summary>
    public static (double X, double Y, double Width, double Height) Resize(
        (double X, double Y, double Width, double Height, double Rotation) start,
        ResizeHandle handle, double dragDeltaX, double dragDeltaY, bool keepAspect, double minSize = MinSizeMm)
    {
        double rad = start.Rotation * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        // Ekran kaymasını yerel eksene çevir.
        double localDx = dragDeltaX * cos + dragDeltaY * sin;
        double localDy = -dragDeltaX * sin + dragDeltaY * cos;

        int sx = handle switch
        {
            ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft => -1,
            ResizeHandle.TopRight or ResizeHandle.Right or ResizeHandle.BottomRight => 1,
            _ => 0,
        };
        int sy = handle switch
        {
            ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight => -1,
            ResizeHandle.BottomLeft or ResizeHandle.Bottom or ResizeHandle.BottomRight => 1,
            _ => 0,
        };

        double width = start.Width + sx * localDx;
        double height = start.Height + sy * localDy;
        if (keepAspect && sx != 0 && sy != 0 && start.Width > 0 && start.Height > 0)
        {
            double ratio = start.Width / start.Height;
            if (Math.Abs(width / start.Width) > Math.Abs(height / start.Height)) height = width / ratio;
            else width = height * ratio;
        }
        width = Math.Max(minSize, width);
        height = Math.Max(minSize, height);

        // Sabit kalan nokta (yerel): tutamacın karşısı. Merkez buna göre kayar.
        double anchorLocalX = -sx * start.Width / 2, anchorLocalY = -sy * start.Height / 2;
        double cx0 = start.X + start.Width / 2, cy0 = start.Y + start.Height / 2;
        double anchorX = cx0 + anchorLocalX * cos - anchorLocalY * sin;
        double anchorY = cy0 + anchorLocalX * sin + anchorLocalY * cos;
        double newAnchorLocalX = -sx * width / 2, newAnchorLocalY = -sy * height / 2;
        double cx = anchorX - (newAnchorLocalX * cos - newAnchorLocalY * sin);
        double cy = anchorY - (newAnchorLocalX * sin + newAnchorLocalY * cos);
        return (cx - width / 2, cy - height / 2, width, height);
    }

    /// <summary>Merkez etrafında fare açısından döndürme; Shift ile 15° adımlar.</summary>
    public static double RotationFromPointer(double centerX, double centerY, double pointerX, double pointerY, bool snap)
    {
        double angle = Math.Atan2(pointerY - centerY, pointerX - centerX) * 180 / Math.PI + 90;
        if (snap) angle = Math.Round(angle / 15) * 15;
        angle %= 360;
        if (angle < 0) angle += 360;
        return Math.Round(angle, 2);
    }

    /// <summary>Seçili öğeleri hizalar/dağıtır. Tek öğe (veya tek grup) seçiliyse etikete göre hizalanır.</summary>
    public static void Align(IReadOnlyList<LabelElement> elements, AlignMode mode, LabelSetup label) =>
        Align(elements, mode, label, AlignReference.Auto);

    /// <summary>
    /// Öğeleri hizalar/dağıtır. Kilitli öğeler taşınmaz (kilitli üye içeren grup da taşınmaz) ama hizalama
    /// referansına katılır. Gruplar tek birim olarak taşınır.
    /// </summary>
    public static void Align(IReadOnlyList<LabelElement> elements, AlignMode mode, LabelSetup label, AlignReference reference)
    {
        var units = BuildUnits(elements);
        if (units.Count == 0) return;
        if (mode is AlignMode.DistributeHorizontal or AlignMode.DistributeVertical)
        {
            Distribute(elements, mode == AlignMode.DistributeHorizontal, null);
            return;
        }

        bool useLabel = reference == AlignReference.Label || (reference == AlignReference.Auto && units.Count == 1);
        RectMm target = useLabel
            ? new RectMm(0, 0, label.WidthMm, label.HeightMm)
            : units.Select(u => u.Bounds).Aggregate(RectMm.Union);

        foreach (var unit in units.Where(u => u.Movable))
        {
            var b = unit.Bounds;
            double dx = 0, dy = 0;
            switch (mode)
            {
                case AlignMode.Left: dx = target.X - b.X; break;
                case AlignMode.CenterHorizontal: dx = target.CenterX - b.CenterX; break;
                case AlignMode.Right: dx = target.Right - b.Right; break;
                case AlignMode.Top: dy = target.Y - b.Y; break;
                case AlignMode.CenterVertical: dy = target.CenterY - b.CenterY; break;
                case AlignMode.Bottom: dy = target.Bottom - b.Bottom; break;
            }
            unit.MoveBy(dx, dy);
        }
    }

    /// <summary>
    /// Öğeleri (grupları birim olarak) yatay veya dikey dağıtır. <paramref name="spacingMm"/> verilirse ilk birimden
    /// başlayarak aralarında tam bu kadar boşluk bırakılır (en az 2 birim); verilmezse ilk ve son birim sabit kalır,
    /// aradakiler eşit aralıklı olur (en az 3 birim).
    /// </summary>
    public static void Distribute(IReadOnlyList<LabelElement> elements, bool horizontal, double? spacingMm)
    {
        // Kilitli öğeler (ve kilitli üyesi olan gruplar) dağıtmaya katılmaz.
        var units = BuildUnits(elements).Where(u => u.Movable).ToList();
        if (units.Count < (spacingMm.HasValue ? 2 : 3)) return;
        var ordered = units.OrderBy(u => horizontal ? u.Bounds.X : u.Bounds.Y).ToList();
        double start = horizontal ? ordered[0].Bounds.X : ordered[0].Bounds.Y;
        double gap;
        if (spacingMm is { } fixedGap)
        {
            gap = fixedGap;
        }
        else
        {
            double end = horizontal ? ordered[^1].Bounds.Right : ordered[^1].Bounds.Bottom;
            double total = ordered.Sum(u => horizontal ? u.Bounds.Width : u.Bounds.Height);
            gap = (end - start - total) / (ordered.Count - 1);
        }
        double cursor = start;
        foreach (var unit in ordered)
        {
            double delta = cursor - (horizontal ? unit.Bounds.X : unit.Bounds.Y);
            if (horizontal) unit.MoveBy(delta, 0);
            else unit.MoveBy(0, delta);
            cursor += (horizontal ? unit.Bounds.Width : unit.Bounds.Height) + gap;
        }
    }

    /// <summary>Grup boyutlandırmada bir üyenin başlangıç durumu.</summary>
    public readonly record struct GroupMemberStart(LabelElement Element, double X, double Y, double Width, double Height, double FontSize)
    {
        public static GroupMemberStart Of(LabelElement element) =>
            new(element, element.X, element.Y, element.Width, element.Height, element is TextElement t ? t.FontSize : 0);
    }

    /// <summary>
    /// Bir grubu <paramref name="from"/> sınırlarından <paramref name="to"/> sınırlarına orantılı ölçekler: üyelerin
    /// merkezleri sınır içindeki göreli yerlerini korur, ölçüleri yatay/dikey katsayıyla büyür (90°/270° döndürülmüş
    /// öğelerde eksenler yer değiştirir, diğer açılarda ortalama katsayı). Metinlerde yazı boyutu küçük katsayıyla
    /// ölçeklenir (sığma korunur). Her zaman başlangıç durumundan hesaplanır; sürüklerken hata birikmez.
    /// </summary>
    public static void ScaleGroup(IReadOnlyList<GroupMemberStart> members, RectMm from, RectMm to, double minSize = MinSizeMm)
    {
        if (members.Count == 0 || from.Width <= 0 || from.Height <= 0) return;
        double sx = Math.Max(1e-4, to.Width / from.Width);
        double sy = Math.Max(1e-4, to.Height / from.Height);
        double uniform = Math.Sqrt(sx * sy);
        foreach (var m in members)
        {
            double cx = m.X + m.Width / 2, cy = m.Y + m.Height / 2;
            double ncx = to.X + (cx - from.X) * sx;
            double ncy = to.Y + (cy - from.Y) * sy;
            int quarter = ElementGeometry.GetQuarterTurns(m.Element.Rotation);
            (double fw, double fh) = quarter switch
            {
                0 or 2 => (sx, sy),
                1 or 3 => (sy, sx),
                _ => (uniform, uniform),
            };
            double w = Math.Max(minSize, m.Width * fw);
            double h = Math.Max(minSize, m.Height * fh);
            m.Element.SetBounds(Math.Round(ncx - w / 2, 4), Math.Round(ncy - h / 2, 4), Math.Round(w, 4), Math.Round(h, 4));
            if (m.Element is TextElement text && m.FontSize > 0)
                text.FontSize = Math.Round(m.FontSize * Math.Min(sx, sy), 2);
        }
    }

    /// <summary>Öğeleri grup kimliğine göre birimlere ayırır (grupsuz her öğe kendi birimidir).</summary>
    private static List<LayoutUnit> BuildUnits(IReadOnlyList<LabelElement> elements)
    {
        var units = new List<LayoutUnit>();
        var byGroup = new Dictionary<string, LayoutUnit>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            if (element.GroupId is { Length: > 0 } groupId)
            {
                if (!byGroup.TryGetValue(groupId, out var unit))
                {
                    unit = new LayoutUnit();
                    byGroup[groupId] = unit;
                    units.Add(unit);
                }
                unit.Members.Add(element);
            }
            else
            {
                var unit = new LayoutUnit();
                unit.Members.Add(element);
                units.Add(unit);
            }
        }
        foreach (var unit in units) unit.Bounds = unit.Members.Select(ElementGeometry.GetRotatedBoundsMm).Aggregate(RectMm.Union);
        return units;
    }

    private sealed class LayoutUnit
    {
        public List<LabelElement> Members { get; } = [];
        public RectMm Bounds { get; set; }
        public bool Movable => Members.All(m => !m.Locked);

        public void MoveBy(double dx, double dy)
        {
            if (dx == 0 && dy == 0) return;
            foreach (var member in Members)
            {
                member.X = Math.Round(member.X + dx, 4);
                member.Y = Math.Round(member.Y + dy, 4);
            }
        }
    }
}
