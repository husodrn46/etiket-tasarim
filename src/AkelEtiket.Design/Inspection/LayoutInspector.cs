using System.Globalization;
using AkelEtiket.Core.Geometry;
using AkelEtiket.Rendering;

namespace AkelEtiket.Design.Inspection;

/// <summary>Yerleşim sorunu.</summary>
/// <param name="Severity">error, warning, info</param>
/// <param name="Code">OUT_OF_BOUNDS, NEAR_EDGE, OVERLAP, BARCODE_OVERLAP, TEXT_OVERFLOW, TEXT_TOO_SMALL, BARCODE_MODULE_SMALL, MISALIGNED, UNEVEN_SPACING, HIDDEN, EMPTY_TEXT, RENDER</param>
public sealed record LayoutIssue(string Severity, string Code, string Message, IReadOnlyList<string> ElementIds, JsonObject? Suggestion = null)
{
    public JsonObject ToJson()
    {
        var o = new JsonObject
        {
            ["severity"] = Severity,
            ["code"] = Code,
            ["message"] = Message,
            ["element_ids"] = new JsonArray(ElementIds.Select(i => (JsonNode)i).ToArray()),
        };
        if (Suggestion is not null) o["suggestion"] = Suggestion.DeepClone();
        return o;
    }
}

/// <summary>
/// Tasarımın yerleşimini inceler: etiket dışına taşma, kenara çok yakınlık, çakışmalar (barkod üstüne binme
/// hata sayılır), metin taşması, çok küçük yazı, ince barkod modülü, neredeyse hizalı kenarlar ve düzensiz aralıklar.
/// Öneriler doğrudan çağrılabilecek araç girdileri olarak verilir.
/// </summary>
public static class LayoutInspector
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static IReadOnlyList<LayoutIssue> Inspect(LabelDocument doc, LabelRenderResult render, double marginMm = 1.5, string? designId = null)
    {
        var issues = new List<LayoutIssue>();
        var label = new RectMm(0, 0, doc.Label.WidthMm, doc.Label.HeightMm);
        var visible = doc.Elements.Where(e => e.Visible).ToList();

        foreach (var element in doc.Elements)
        {
            if (!element.Visible)
            {
                issues.Add(new("info", "HIDDEN", $"\"{element.DisplayName}\" gizli (baskıda çıkmaz).", [element.Id]));
                continue;
            }
            var b = ElementGeometry.GetRotatedBoundsMm(element);
            const double tol = 0.05;
            if (b.X < -tol || b.Y < -tol || b.Right > label.Width + tol || b.Bottom > label.Height + tol)
            {
                issues.Add(new("error", "OUT_OF_BOUNDS",
                    $"\"{element.DisplayName}\" etiket dışına taşıyor ({Mm(b.X)}, {Mm(b.Y)} – {Mm(b.Right)}, {Mm(b.Bottom)} mm; etiket {Mm(label.Width)} × {Mm(label.Height)} mm).",
                    [element.Id], MoveSuggestion(designId, element, b, label, marginMm)));
            }
            else if (!IsFrame(element, label) && (b.X < marginMm - tol || b.Y < marginMm - tol || b.Right > label.Width - marginMm + tol || b.Bottom > label.Height - marginMm + tol))
            {
                issues.Add(new("warning", "NEAR_EDGE",
                    $"\"{element.DisplayName}\" etiket kenarına {Mm(marginMm)} mm'den yakın; kesim/kayma toleransı için kenar boşluğu önerilir.",
                    [element.Id], MoveSuggestion(designId, element, b, label, marginMm)));
            }

            var info = render.Elements.FirstOrDefault(r => r.ElementId == element.Id);
            if (element is TextElement text)
            {
                if (string.IsNullOrWhiteSpace(text.Text))
                    issues.Add(new("warning", "EMPTY_TEXT", $"\"{element.DisplayName}\" metni boş.", [element.Id]));
                if (info?.Text is { Overflow: true })
                    issues.Add(new("error", "TEXT_OVERFLOW", $"\"{element.DisplayName}\" metni kutusuna sığmıyor; kutuyu büyütün, yazıyı küçültün veya autoShrink açın.",
                        [element.Id], Suggest(designId, "update_element", new JsonObject { ["element_id"] = element.Id, ["changes"] = new JsonObject { ["autoShrink"] = true } })));
                double usedPt = info?.Text?.EffectiveFontSizePt ?? text.FontSize;
                if (usedPt < 6)
                    issues.Add(new("warning", "TEXT_TOO_SMALL", $"\"{element.DisplayName}\" yazısı {Mm(usedPt)} pt; 203 dpi baskıda 6 pt altı zor okunur.", [element.Id]));
            }
            if (element is BarcodeElement && info?.Barcode is { } bc)
            {
                if (!bc.IsValid && bc.Validation.FirstError is { } err)
                    issues.Add(new("error", "BARCODE_INVALID", $"\"{element.DisplayName}\": {err}", [element.Id]));
                else if (bc.Layout is { Fits: false, Problem: { } problem })
                    issues.Add(new("error", "BARCODE_DOES_NOT_FIT", $"\"{element.DisplayName}\": {problem}", [element.Id]));
                else if (bc.ModuleDots is > 0 and < 2 && element is BarcodeElement { Is2D: false })
                    issues.Add(new("warning", "BARCODE_MODULE_SMALL", $"\"{element.DisplayName}\" modülü {bc.ModuleDots} nokta ({Mm(bc.ModuleMm)} mm); okuyucular için en az 2 nokta önerilir — barkodu genişletin.", [element.Id]));
                else if (bc.ModuleDots is > 0 and < 3 && element is BarcodeElement { Is2D: true })
                    issues.Add(new("warning", "BARCODE_MODULE_SMALL", $"\"{element.DisplayName}\" modülü {bc.ModuleDots} nokta; 2D barkodlarda en az 3 nokta önerilir — barkodu büyütün.", [element.Id]));
            }
            if (info is not null)
            {
                foreach (var issue in info.Issues.Where(i => i.Severity == RenderIssueSeverity.Error))
                {
                    if (issues.Any(x => x.ElementIds.Contains(element.Id) && x.Message.Contains(issue.Message, StringComparison.Ordinal))) continue;
                    issues.Add(new("error", "RENDER", $"\"{element.DisplayName}\": {issue.Message}", [element.Id]));
                }
            }
        }

        // Çakışmalar
        for (int i = 0; i < visible.Count; i++)
        {
            for (int j = i + 1; j < visible.Count; j++)
            {
                var a = visible[i];
                var c = visible[j];
                if (a.GroupId is not null && a.GroupId == c.GroupId && a is not BarcodeElement && c is not BarcodeElement) continue;
                var ba = ElementGeometry.GetRotatedBoundsMm(a);
                var bb = ElementGeometry.GetRotatedBoundsMm(c);
                var overlap = Intersect(ba, bb);
                if (overlap is null || overlap.Value.Width < 0.2 || overlap.Value.Height < 0.2) continue;
                if (IsBackdrop(a, c) || IsBackdrop(c, a)) continue;
                bool barcode = a is BarcodeElement || c is BarcodeElement;
                issues.Add(new(barcode ? "error" : "warning", barcode ? "BARCODE_OVERLAP" : "OVERLAP",
                    barcode
                        ? $"\"{a.DisplayName}\" ile \"{c.DisplayName}\" üst üste biniyor; barkodun (ve sessiz bölgesinin) üstüne öğe binmesi okunmasını bozar."
                        : $"\"{a.DisplayName}\" ile \"{c.DisplayName}\" üst üste biniyor ({Mm(overlap.Value.Width)} × {Mm(overlap.Value.Height)} mm).",
                    [a.Id, c.Id]));
            }
        }

        AddAlignmentHints(visible, issues, designId);
        return issues;
    }

    /// <summary>Kenarları 0,15–1,5 mm farkla neredeyse hizalı öğeler ve düzensiz dikey aralıklar için öneriler.</summary>
    private static void AddAlignmentHints(List<LabelElement> elements, List<LayoutIssue> issues, string? designId)
    {
        var free = elements.Where(e => !e.Locked).ToList();
        var edges = new (string Name, AlignEdge Edge, Func<RectMm, double> Selector)[]
        {
            ("sol kenar", AlignEdge.Left, r => r.X),
            ("yatay merkez", AlignEdge.CenterHorizontal, r => r.CenterX),
            ("sağ kenar", AlignEdge.Right, r => r.Right),
        };
        for (int i = 0; i < free.Count; i++)
        {
            for (int j = i + 1; j < free.Count; j++)
            {
                var a = ElementGeometry.GetRotatedBoundsMm(free[i]);
                var b = ElementGeometry.GetRotatedBoundsMm(free[j]);
                var distances = edges.Select(e => (e.Name, e.Edge, D: Math.Abs(e.Selector(a) - e.Selector(b)))).ToList();
                // Herhangi bir kenarda zaten hizalıysa (ör. sola hizalı farklı genişlikte metinler) öneri yok.
                if (distances.Any(d => d.D < 0.15)) continue;
                var near = distances.Where(d => d.D <= 1.5).OrderBy(d => d.D).FirstOrDefault();
                if (near.Name is null) continue;
                issues.Add(new("info", "MISALIGNED",
                    $"\"{free[i].DisplayName}\" ve \"{free[j].DisplayName}\" {near.Name} bakımından {Mm(near.D)} mm farklı; hizalamak düzenli görünür.",
                    [free[i].Id, free[j].Id],
                    Suggest(designId, "align_elements", new JsonObject
                    {
                        ["element_ids"] = new JsonArray(free[i].Id, free[j].Id),
                        ["edge"] = JsonNamingPolicy.SnakeCaseLower.ConvertName(near.Edge.ToString()),
                        ["relative_to"] = "selection",
                    })));
            }
        }

        // Dikey aralık düzensizliği (yatayda çakışan yığınlar)
        var stack = free.Where(e => e is TextElement or BarcodeElement or ImageElement)
            .Select(e => (Element: e, Bounds: ElementGeometry.GetRotatedBoundsMm(e)))
            .OrderBy(t => t.Bounds.Y).ToList();
        if (stack.Count >= 3)
        {
            var gaps = new List<double>();
            for (int i = 1; i < stack.Count; i++) gaps.Add(stack[i].Bounds.Y - stack[i - 1].Bounds.Bottom);
            var positive = gaps.Where(g => g > 0).ToList();
            if (positive.Count >= 2 && positive.Max() - positive.Min() > 2.5 && gaps.All(g => g > -0.2))
            {
                issues.Add(new("info", "UNEVEN_SPACING",
                    $"Alt alta öğeler arasındaki boşluklar düzensiz ({string.Join(", ", gaps.Select(Mm))} mm). Eşit dağıtmak daha düzenli görünür.",
                    stack.Select(s => s.Element.Id).ToList(),
                    Suggest(designId, "distribute_elements", new JsonObject
                    {
                        ["element_ids"] = new JsonArray(stack.Select(s => (JsonNode)s.Element.Id).ToArray()),
                        ["direction"] = "vertical",
                    })));
            }
        }
    }

    private static JsonObject? MoveSuggestion(string? designId, LabelElement element, RectMm b, RectMm label, double margin)
    {
        double dx = 0, dy = 0;
        if (b.X < margin) dx = margin - b.X;
        else if (b.Right > label.Width - margin) dx = label.Width - margin - b.Right;
        if (b.Y < margin) dy = margin - b.Y;
        else if (b.Bottom > label.Height - margin) dy = label.Height - margin - b.Bottom;
        if (b.Width > label.Width - 2 * margin || b.Height > label.Height - 2 * margin) return null;
        if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01) return null;
        return Suggest(designId, "move_element", new JsonObject
        {
            ["element_ids"] = new JsonArray(element.Id),
            ["dx"] = Math.Round(dx, 2),
            ["dy"] = Math.Round(dy, 2),
        });
    }

    private static JsonObject Suggest(string? designId, string tool, JsonObject args)
    {
        if (designId is not null) args.Insert(0, "design_id", designId);
        return new JsonObject { ["tool"] = tool, ["arguments"] = args };
    }

    /// <summary>Etiketin tamamını çevreleyen çerçeve (kenara yakın olması doğaldır).</summary>
    private static bool IsFrame(LabelElement element, RectMm label) =>
        element is ShapeElement { Kind: ShapeKind.Rectangle or ShapeKind.Ellipse, Fill: FillStyle.None }
        && element.Width >= label.Width * 0.8 && element.Height >= label.Height * 0.8;

    /// <summary>Arka plan şekli veya çerçeve: içi boş şekil ya da siyah zemin üzerinde beyaz metin bilerek üst üstedir.</summary>
    private static bool IsBackdrop(LabelElement back, LabelElement front)
    {
        if (back is ShapeElement { Kind: not ShapeKind.Line } shape)
        {
            var bb = ElementGeometry.GetRotatedBoundsMm(back);
            var fb = ElementGeometry.GetRotatedBoundsMm(front);
            bool contains = fb.X >= bb.X - 0.05 && fb.Y >= bb.Y - 0.05 && fb.Right <= bb.Right + 0.05 && fb.Bottom <= bb.Bottom + 0.05;
            if (contains && shape.Fill == FillStyle.None) return true;
            if (contains && shape.Fill == FillStyle.Black && front is TextElement { TextColor: InkColor.White }) return true;
            if (contains && shape.Fill == FillStyle.White) return true;
        }
        return false;
    }

    private static RectMm? Intersect(RectMm a, RectMm b)
    {
        double x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
        double r = Math.Min(a.Right, b.Right), btm = Math.Min(a.Bottom, b.Bottom);
        return r > x && btm > y ? new RectMm(x, y, r - x, btm - y) : null;
    }

    private static string Mm(double value) => value.ToString("0.##", Tr);
}
