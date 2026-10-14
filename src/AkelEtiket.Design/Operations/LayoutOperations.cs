using AkelEtiket.Core.Geometry;

namespace AkelEtiket.Design.Operations;

/// <summary>Taşıma, boyutlandırma, silme, gruplama, hizalama, dağıtma ve katman sırası (editörle aynı hesaplar).</summary>
public static class LayoutOperations
{
    /// <summary>Öğeleri (gruplarıyla birlikte) taşır; taşınan öğeleri döndürür.</summary>
    public static IReadOnlyList<LabelElement> Move(DesignEditContext ctx, IReadOnlyList<string> ids, double? dx, double? dy, double? x, double? y)
    {
        if (ids.Count == 0) throw new ToolException(ToolErrorCodes.InvalidArgument, "element_ids boş.");
        if (dx is null && dy is null && x is null && y is null)
            throw new ToolException(ToolErrorCodes.InvalidArgument, "dx/dy (göreli) ya da x/y (mutlak) verin.");
        var targets = ExpandGroups(ctx, ids.Select(id => ctx.GetElement(id, forWrite: true)).ToList());
        foreach (var t in targets) ctx.EnsureWritable(t);

        var first = ctx.GetElement(ids[0], forWrite: true);
        var reference = first.GroupId is { } g
            ? targets.Where(t => t.GroupId == g).Select(ElementGeometry.GetRotatedBoundsMm).Aggregate(RectMm.Union)
            : new RectMm(first.X, first.Y, first.Width, first.Height);
        double moveX = x is { } ax ? ax - reference.X : dx ?? 0;
        double moveY = y is { } ay ? ay - reference.Y : dy ?? 0;
        if (x is not null && dx is not null) moveX += dx.Value;
        if (y is not null && dy is not null) moveY += dy.Value;
        foreach (var element in targets)
        {
            element.X = Math.Round(element.X + moveX, 4);
            element.Y = Math.Round(element.Y + moveY, 4);
            ctx.MarkChanged(element);
        }
        return targets;
    }

    /// <summary>Öğeyi ya da bütün grubu boyutlandırır.</summary>
    public static IReadOnlyList<LabelElement> Resize(DesignEditContext ctx, ResizeElementArgs args)
    {
        List<LabelElement> targets;
        if (args.GroupId is { Length: > 0 } groupId)
        {
            targets = ctx.Document.Elements.Where(e => e.GroupId == groupId).ToList();
            if (targets.Count == 0) throw new ToolException(ToolErrorCodes.NotFound, $"Grup bulunamadı: {groupId}");
        }
        else
        {
            targets = [ctx.GetElement(args.ElementId, forWrite: true)];
        }
        foreach (var t in targets) ctx.EnsureWritable(t);

        var bounds = targets.Count == 1 && targets[0].GroupId is null
            ? new RectMm(targets[0].X, targets[0].Y, targets[0].Width, targets[0].Height)
            : targets.Select(ElementGeometry.GetRotatedBoundsMm).Aggregate(RectMm.Union);
        bool aspectDefault = targets.Count > 1 || targets[0] is ImageElement || targets[0] is BarcodeElement { Is2D: true };
        bool keepAspect = args.KeepAspect ?? aspectDefault;

        double sx, sy;
        if (args.Scale is { } scale)
        {
            if (scale is <= 0 or > 20) throw new ToolException(ToolErrorCodes.InvalidArgument, "scale 0 ile 20 arasında olmalı.");
            sx = sy = scale;
        }
        else
        {
            if (args.Width is null && args.Height is null)
                throw new ToolException(ToolErrorCodes.InvalidArgument, "width, height ya da scale verin.");
            sx = args.Width is { } w ? w / bounds.Width : double.NaN;
            sy = args.Height is { } h ? h / bounds.Height : double.NaN;
            if (double.IsNaN(sx)) sx = keepAspect ? sy : 1;
            if (double.IsNaN(sy)) sy = keepAspect ? sx : 1;
        }
        if (sx <= 0 || sy <= 0) throw new ToolException(ToolErrorCodes.InvalidArgument, "Boyutlar pozitif olmalı.");

        double newW = bounds.Width * sx, newH = bounds.Height * sy;
        var anchor = args.Anchor ?? ResizeAnchor.TopLeft;
        (double ox, double oy) = anchor switch
        {
            ResizeAnchor.Center => (bounds.CenterX - newW / 2, bounds.CenterY - newH / 2),
            ResizeAnchor.TopRight => (bounds.Right - newW, bounds.Y),
            ResizeAnchor.BottomLeft => (bounds.X, bounds.Bottom - newH),
            ResizeAnchor.BottomRight => (bounds.Right - newW, bounds.Bottom - newH),
            _ => (bounds.X, bounds.Y),
        };
        bool scaleFont = args.ScaleFont ?? true;
        double fontFactor = Math.Sqrt(sx * sy);
        foreach (var element in targets)
        {
            double relX = element.X - bounds.X, relY = element.Y - bounds.Y;
            element.SetBounds(
                Math.Round(ox + relX * sx, 4),
                Math.Round(oy + relY * sy, 4),
                Math.Max(LabelElement.MinSizeMm, Math.Round(element.Width * sx, 4)),
                Math.Max(LabelElement.MinSizeMm, Math.Round(element.Height * sy, 4)));
            if (scaleFont && args.Scale is not null && element is TextElement text)
                text.FontSize = Math.Round(text.FontSize * fontFactor, 1);
            ctx.MarkChanged(element);
        }
        return targets;
    }

    public static IReadOnlyList<string> Delete(DesignEditContext ctx, IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) throw new ToolException(ToolErrorCodes.InvalidArgument, "element_ids boş.");
        var targets = ids.Distinct().Select(id => ctx.GetElement(id, forWrite: true)).ToList();
        foreach (var element in targets) ctx.Document.Elements.Remove(element);
        return targets.Select(t => t.Id).ToList();
    }

    public static string Group(DesignEditContext ctx, IReadOnlyList<string> ids)
    {
        var targets = ids.Distinct().Select(id => ctx.GetElement(id, forWrite: true)).ToList();
        if (targets.Count < 2) throw new ToolException(ToolErrorCodes.InvalidArgument, "Gruplamak için en az 2 öğe gerekir.");
        string groupId = "g" + DesignId.NewShortId();
        foreach (var element in targets)
        {
            element.GroupId = groupId;
            ctx.MarkChanged(element);
        }
        return groupId;
    }

    public static IReadOnlyList<string> Ungroup(DesignEditContext ctx, IReadOnlyList<string>? ids, string? groupId)
    {
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        if (groupId is { Length: > 0 }) groupIds.Add(groupId);
        foreach (string id in ids ?? [])
        {
            var element = ctx.GetElement(id, forWrite: true);
            if (element.GroupId is { } g) groupIds.Add(g);
        }
        if (groupIds.Count == 0) throw new ToolException(ToolErrorCodes.InvalidArgument, "Çözülecek grup bulunamadı (group_id veya grup üyesi element_ids verin).");
        var members = ctx.Document.Elements.Where(e => e.GroupId is { } g && groupIds.Contains(g)).ToList();
        foreach (var element in members)
        {
            ctx.EnsureWritable(element);
            element.GroupId = null;
            ctx.MarkChanged(element);
        }
        return members.Select(m => m.Id).ToList();
    }

    public static void Align(DesignEditContext ctx, IReadOnlyList<string> ids, AlignEdge edge, AlignTo relativeTo)
    {
        var targets = ExpandGroups(ctx, ids.Distinct().Select(id => ctx.GetElement(id, forWrite: true)).ToList());
        foreach (var t in targets) ctx.EnsureWritable(t);
        var mode = edge switch
        {
            AlignEdge.Left => AlignMode.Left,
            AlignEdge.CenterHorizontal => AlignMode.CenterHorizontal,
            AlignEdge.Right => AlignMode.Right,
            AlignEdge.Top => AlignMode.Top,
            AlignEdge.CenterVertical => AlignMode.CenterVertical,
            _ => AlignMode.Bottom,
        };
        var reference = relativeTo switch
        {
            AlignTo.Selection => AlignReference.Selection,
            AlignTo.Label => AlignReference.Label,
            _ => AlignReference.Auto,
        };
        TransformMath.Align(targets, mode, ctx.Document.Label, reference);
        foreach (var t in targets) ctx.MarkChanged(t);
    }

    public static void Distribute(DesignEditContext ctx, IReadOnlyList<string> ids, Direction direction, double? spacing)
    {
        var targets = ExpandGroups(ctx, ids.Distinct().Select(id => ctx.GetElement(id, forWrite: true)).ToList());
        foreach (var t in targets) ctx.EnsureWritable(t);
        int units = targets.Select(t => t.GroupId ?? "#" + t.Id).Distinct().Count();
        if (units < (spacing.HasValue ? 2 : 3))
            throw new ToolException(ToolErrorCodes.InvalidArgument,
                spacing.HasValue ? "Sabit aralıkla dağıtmak için en az 2 öğe/grup gerekir." : "Eşit dağıtmak için en az 3 öğe/grup gerekir (ya da spacing_mm verin).");
        if (spacing is < 0) throw new ToolException(ToolErrorCodes.InvalidArgument, "spacing_mm negatif olamaz.");
        TransformMath.Distribute(targets, direction == Direction.Horizontal, spacing);
        foreach (var t in targets) ctx.MarkChanged(t);
    }

    /// <summary>Katman sırasını değiştirir; yeni sırayı (0 = en alt) döndürür.</summary>
    public static int SetLayerOrder(DesignEditContext ctx, string id, LayerAction? action, int? index)
    {
        var element = ctx.GetElement(id, forWrite: true);
        var list = ctx.Document.Elements;
        int current = list.IndexOf(element);
        int target;
        if (index is { } i)
        {
            target = Math.Clamp(i, 0, list.Count - 1);
        }
        else
        {
            target = (action ?? LayerAction.BringToFront) switch
            {
                LayerAction.BringToFront => list.Count - 1,
                LayerAction.SendToBack => 0,
                LayerAction.BringForward => Math.Min(list.Count - 1, current + 1),
                _ => Math.Max(0, current - 1),
            };
        }
        if (target != current)
        {
            list.Move(current, target);
            ctx.MarkChanged(element);
        }
        return target;
    }

    /// <summary>Verilen öğelerin gruplarının bütün üyelerini ekler.</summary>
    public static List<LabelElement> ExpandGroups(DesignEditContext ctx, List<LabelElement> elements)
    {
        var groups = elements.Select(e => e.GroupId).Where(g => g is not null).ToHashSet();
        var result = new List<LabelElement>(elements);
        foreach (var member in ctx.Document.Elements)
        {
            if (member.GroupId is { } g && groups.Contains(g) && !result.Contains(member)) result.Add(member);
        }
        return result;
    }
}
