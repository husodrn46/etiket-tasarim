using System.Globalization;
using AkelEtiket.Core.Serialization;

namespace AkelEtiket.Design.Workspace;

/// <summary>Bir öğedeki değişiklik.</summary>
/// <param name="Kind">added, removed, modified</param>
public sealed record ElementChange(string ElementId, string ElementName, string Kind, IReadOnlyList<string> ChangedProperties, string Description);

/// <summary>İki belge arasındaki farklar (öneri incelemesi ve özetler için).</summary>
public sealed record DocumentChangeSet(IReadOnlyList<ElementChange> Elements, IReadOnlyList<string> DocumentChanges, bool OrderChanged)
{
    public bool IsEmpty => Elements.Count == 0 && DocumentChanges.Count == 0 && !OrderChanged;

    /// <summary>Kullanıcıya gösterilecek Türkçe satırlar.</summary>
    public IReadOnlyList<string> ToLines()
    {
        var lines = new List<string>(DocumentChanges);
        lines.AddRange(Elements.Select(e => e.Description));
        if (OrderChanged) lines.Add("Katman sırası değişti.");
        return lines;
    }
}

/// <summary>Üç yönlü birleştirme sonucu.</summary>
public sealed record MergeResult(LabelDocument Merged, IReadOnlyList<string> Conflicts);

/// <summary>Belge karşılaştırma ve öğe düzeyinde üç yönlü birleştirme.</summary>
public static class DocumentDiff
{
    private static readonly string[] IgnoredProperties = [];

    public static DocumentChangeSet Compare(LabelDocument before, LabelDocument after)
    {
        var beforeMap = ToMap(before);
        var afterMap = ToMap(after);
        var changes = new List<ElementChange>();

        foreach (var (id, node) in afterMap)
        {
            var element = after.FindElement(id)!;
            if (!beforeMap.TryGetValue(id, out var old))
            {
                changes.Add(new ElementChange(id, element.DisplayName, "added", [], $"Eklendi: {Describe(element)}"));
                continue;
            }
            var changed = ChangedProperties(old, node);
            if (changed.Count == 0) continue;
            var oldElement = before.FindElement(id)!;
            string details = string.Join(", ", changed.Take(6).Select(p => $"{p}: {Short(old[p])} → {Short(node[p])}"));
            if (changed.Count > 6) details += $" (+{changed.Count - 6})";
            changes.Add(new ElementChange(id, element.DisplayName, "modified", changed,
                $"Değişti: {oldElement.KindName} \"{element.DisplayName}\" — {details}"));
        }
        foreach (var (id, _) in beforeMap)
        {
            if (afterMap.ContainsKey(id)) continue;
            var element = before.FindElement(id)!;
            changes.Add(new ElementChange(id, element.DisplayName, "removed", [], $"Silindi: {Describe(element)}"));
        }

        var docChanges = new List<string>();
        if (before.Name != after.Name) docChanges.Add($"Ad: \"{before.Name}\" → \"{after.Name}\"");
        var a = before.Label;
        var b = after.Label;
        if (!Same(a.WidthMm, b.WidthMm) || !Same(a.HeightMm, b.HeightMm))
            docChanges.Add($"Etiket ölçüsü: {Mm(a.WidthMm)} × {Mm(a.HeightMm)} → {Mm(b.WidthMm)} × {Mm(b.HeightMm)} mm");
        if (a.Media != b.Media || !Same(a.GapMm, b.GapMm) || !Same(a.GapOffsetMm, b.GapOffsetMm))
            docChanges.Add("Etiket medyası/boşluk ayarı değişti");
        if ((before.DataSource?.FilePath ?? "") != (after.DataSource?.FilePath ?? ""))
            docChanges.Add($"Veri kaynağı: {Path.GetFileName(after.DataSource?.FilePath ?? "(yok)")}");

        var commonBefore = before.Elements.Select(e => e.Id).Where(afterMap.ContainsKey).ToList();
        var commonAfter = after.Elements.Select(e => e.Id).Where(beforeMap.ContainsKey).ToList();
        bool orderChanged = !commonBefore.SequenceEqual(commonAfter);

        return new DocumentChangeSet(changes, docChanges, orderChanged);
    }

    /// <summary>
    /// Taslağı (<paramref name="draft"/>) güncel belgeye (<paramref name="current"/>) uygular. Ortak ata
    /// <paramref name="baseDoc"/>: taslak açıldığı andaki belge. Taslağın dokunmadığı öğelerde kullanıcının sonraki
    /// değişiklikleri korunur; aynı öğe iki tarafta da farklı değiştiyse taslak kazanır ve çakışma olarak bildirilir.
    /// </summary>
    public static MergeResult Merge(LabelDocument baseDoc, LabelDocument current, LabelDocument draft)
    {
        var baseMap = ToMap(baseDoc);
        var currentMap = ToMap(current);
        var draftMap = ToMap(draft);
        var conflicts = new List<string>();
        var resultJson = new Dictionary<string, JsonObject>(StringComparer.Ordinal);

        foreach (string id in baseMap.Keys.Union(currentMap.Keys).Union(draftMap.Keys))
        {
            baseMap.TryGetValue(id, out var b);
            currentMap.TryGetValue(id, out var c);
            draftMap.TryGetValue(id, out var d);
            JsonObject? chosen;
            if (NodeEquals(d, b)) chosen = c;                // taslak dokunmadı
            else if (NodeEquals(c, b)) chosen = d;           // kullanıcı dokunmadı
            else if (NodeEquals(c, d)) chosen = c;           // ikisi aynı sonuca vardı
            else
            {
                chosen = d;
                string name = (draft.FindElement(id) ?? current.FindElement(id) ?? baseDoc.FindElement(id))?.DisplayName ?? id;
                conflicts.Add(d is null
                    ? $"\"{name}\": siz değiştirdiniz, yapay zekâ sildi (silinecek)."
                    : c is null
                        ? $"\"{name}\": siz sildiniz, yapay zekâ değiştirdi (geri gelecek)."
                        : $"\"{name}\": hem siz hem yapay zekâ değiştirdi (yapay zekânın sürümü uygulanacak).");
            }
            if (chosen is not null) resultJson[id] = chosen;
        }

        // Sıra: taslak sırayı değiştirdiyse ve kullanıcı değiştirmediyse taslağınki; aksi halde güncel sıra.
        bool draftReordered = !Filter(draft, baseMap).SequenceEqual(Filter(baseDoc, draftMap));
        bool currentReordered = !Filter(current, baseMap).SequenceEqual(Filter(baseDoc, currentMap));
        var primary = (draftReordered && !currentReordered ? draft : current).Elements.Select(e => e.Id).ToList();
        var secondary = (draftReordered && !currentReordered ? current : draft).Elements.Select(e => e.Id).ToList();
        var order = primary.Where(resultJson.ContainsKey).ToList();
        foreach (string id in secondary)
        {
            if (!resultJson.ContainsKey(id) || order.Contains(id)) continue;
            int index = secondary.IndexOf(id);
            string? predecessor = secondary.Take(index).LastOrDefault(order.Contains);
            int insertAt = predecessor is null ? 0 : order.IndexOf(predecessor) + 1;
            order.Insert(insertAt, id);
        }
        foreach (string id in resultJson.Keys)
        {
            if (!order.Contains(id)) order.Add(id);
        }

        var merged = ModelJson.Clone(current);
        merged.Elements.Clear();
        foreach (string id in order)
        {
            var element = JsonSerializer.Deserialize<LabelElement>(resultJson[id].ToJsonString(), ModelJson.CompactOptions)!;
            merged.Elements.Add(element);
        }

        // Belge düzeyindeki alanlar: taslak değiştirdiyse taslağınki.
        if (draft.Name != baseDoc.Name) merged.Name = draft.Name;
        if (draft.Description != baseDoc.Description) merged.Description = draft.Description;
        var bl = baseDoc.Label;
        var dl = draft.Label;
        var ml = merged.Label;
        if (!Same(dl.WidthMm, bl.WidthMm)) ml.WidthMm = dl.WidthMm;
        if (!Same(dl.HeightMm, bl.HeightMm)) ml.HeightMm = dl.HeightMm;
        if (!Same(dl.GapMm, bl.GapMm)) ml.GapMm = dl.GapMm;
        if (!Same(dl.GapOffsetMm, bl.GapOffsetMm)) ml.GapOffsetMm = dl.GapOffsetMm;
        if (dl.Media != bl.Media) ml.Media = dl.Media;
        if ((draft.DataSource?.FilePath ?? "") != (baseDoc.DataSource?.FilePath ?? "")) merged.DataSource = draft.DataSource;
        return new MergeResult(merged, conflicts);
    }

    private static List<string> Filter(LabelDocument doc, Dictionary<string, JsonObject> keep) =>
        doc.Elements.Select(e => e.Id).Where(keep.ContainsKey).ToList();

    private static Dictionary<string, JsonObject> ToMap(LabelDocument doc)
    {
        var map = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var element in doc.Elements)
        {
            var node = JsonSerializer.SerializeToNode(element, ModelJson.CompactOptions) as JsonObject ?? [];
            map[element.Id] = node;
        }
        return map;
    }

    private static bool NodeEquals(JsonObject? a, JsonObject? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return JsonNode.DeepEquals(a, b);
    }

    private static List<string> ChangedProperties(JsonObject old, JsonObject now)
    {
        var names = old.Select(p => p.Key).Union(now.Select(p => p.Key)).ToList();
        return names.Where(n => !IgnoredProperties.Contains(n) && !JsonNode.DeepEquals(old[n], now[n])).ToList();
    }

    private static string Describe(LabelElement e)
    {
        string content = e switch
        {
            TextElement t => $" \"{Truncate(t.Text, 40)}\"",
            BarcodeElement b => $" {b.Symbology} \"{Truncate(b.Content, 40)}\"",
            _ => "",
        };
        return $"{e.KindName}{content} ({Mm(e.X)}, {Mm(e.Y)} mm; {Mm(e.Width)} × {Mm(e.Height)} mm)";
    }

    private static string Short(JsonNode? node)
    {
        if (node is null) return "—";
        string text = node switch
        {
            JsonValue v when v.TryGetValue(out string? s) => $"\"{Truncate(s, 30)}\"",
            JsonValue v when v.TryGetValue(out double d) => Mm(d),
            JsonValue v when v.TryGetValue(out bool b) => b ? "evet" : "hayır",
            _ => node.ToJsonString(),
        };
        return text.Length > 40 ? text[..40] + "…" : text;
    }

    private static string Truncate(string text, int max) =>
        string.IsNullOrEmpty(text) ? "" : text.Length <= max ? text.Replace('\n', ' ') : text[..max].Replace('\n', ' ') + "…";

    private static string Mm(double value) => value.ToString("0.##", CultureInfo.GetCultureInfo("tr-TR"));

    private static bool Same(double a, double b) => Math.Abs(a - b) < 1e-6;
}
