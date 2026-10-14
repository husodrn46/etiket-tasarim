using System.Text.Json.Nodes;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;
using AkelEtiket.Rendering;

namespace AkelEtiket.Editor.Services;

/// <summary>Önbellekteki çizim: render sonucu + platform görüntüsü.</summary>
public sealed class RenderedElement
{
    public RenderedElement(ElementRenderResult result, object? image)
    {
        Result = result;
        Image = image;
    }

    public ElementRenderResult Result { get; }

    /// <summary>Ekran görüntüsü (WPF BitmapSource; dondurulmuş).</summary>
    public object? Image { get; }
}

/// <summary>
/// Öğe çizim önbelleği. Anahtar, öğenin KONUM HARİÇ tüm özellikleri + doldurulmuş alan değerleri + çözünürlüktür;
/// böylece taşıma ve geri al/ileri al yeniden çizim gerektirmez.
/// </summary>
public sealed class RenderCache
{
    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<(string Key, RenderedElement Value)>> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, RenderedElement Value)> _lru = new();
    private readonly Lock _sync = new();

    public RenderCache(int capacity = 300) => _capacity = Math.Max(16, capacity);

    public bool TryGet(string key, out RenderedElement? value)
    {
        lock (_sync)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }
        value = null;
        return false;
    }

    public void Add(string key, RenderedElement value)
    {
        lock (_sync)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
                _map.Remove(key);
            }
            var node = _lru.AddFirst((key, value));
            _map[key] = node;
            while (_map.Count > _capacity)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _map.Clear();
            _lru.Clear();
        }
    }

    /// <summary>Öğenin konumdan bağımsız içerik anahtarı.</summary>
    public static string ComputeKey(LabelElement element, IFieldValueSource? fields, double dotsPerMm)
    {
        var node = JsonNode.Parse(ModelJson.SerializeElements([element]))!.AsArray()[0]!.AsObject();
        node.Remove("x");
        node.Remove("y");
        node.Remove("name");
        node.Remove("locked");
        node.Remove("groupId");
        string resolved = element switch
        {
            TextElement t => FieldTemplate.Resolve(t.Text, fields).Text,
            BarcodeElement b => FieldTemplate.Resolve(b.Content, fields).Text,
            ImageElement { SourceField: { } field } => FieldTemplate.Resolve(FieldTemplate.MakePlaceholder(field), fields).Text,
            _ => "",
        };
        // Koşullu gösterme: sonuç (göster/gizle/alan yok) önizleme verisine bağlıdır.
        string condition = element.VisibleWhen is { } c
            ? VisibilityEvaluator.Evaluate(c, fields) + (fields is null ? ":0" : ":1")
            : "";
        // Konum, nokta ızgarasına yuvarlandığı için kesirli kısmı çizimi etkilemez; yine de alt-nokta
        // kaydırması olmadığından anahtar konumdan bağımsızdır.
        return $"{dotsPerMm:0.###}|{resolved}|{condition}|{node.ToJsonString()}";
    }
}
