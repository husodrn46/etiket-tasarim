using L10n = AkelEtiket.Core.Localization.UiText;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AkelEtiket.Core.Model;

namespace AkelEtiket.Core.Serialization;

/// <summary>Tasarım modelinin JSON biçimi (proje dosyası, pano, geri al kayıtları için ortak).</summary>
public static class ModelJson
{
    public static JsonSerializerOptions FileOptions { get; } = Create(indented: true);

    public static JsonSerializerOptions CompactOptions { get; } = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = indented,
            AllowOutOfOrderMetadataProperties = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            // Türkçe karakterler dosyada okunabilir kalsın (ğ yerine ğ).
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static string Serialize(LabelDocument document, bool indented = true) =>
        JsonSerializer.Serialize(document, indented ? FileOptions : CompactOptions);

    public static LabelDocument DeserializeDocument(string json)
    {
        var doc = JsonSerializer.Deserialize<LabelDocument>(json, FileOptions)
                  ?? throw new JsonException(L10n.T("Etiket verisi boş."));
        doc.Label ??= new LabelSetup();
        doc.Elements ??= [];
        doc.Guides ??= [];
        // Bozuk/eksik öğeleri ayıkla ve tekrarlanan kimlikleri düzelt.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = doc.Elements.Count - 1; i >= 0; i--)
        {
            if (doc.Elements[i] is null) doc.Elements.RemoveAt(i);
        }
        foreach (var element in doc.Elements)
        {
            if (!seen.Add(element.Id)) element.Id = LabelElement.NewId();
        }
        return doc;
    }

    public static string SerializeElements(IEnumerable<LabelElement> elements) =>
        JsonSerializer.Serialize(elements.ToList(), CompactOptions);

    public static List<LabelElement> DeserializeElements(string json) =>
        (JsonSerializer.Deserialize<List<LabelElement>>(json, CompactOptions) ?? []).Where(e => e is not null).ToList();

    /// <summary>JSON üzerinden derin kopya (aynı kimlikle).</summary>
    public static T Clone<T>(T value) where T : class =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, CompactOptions), CompactOptions)
        ?? throw new InvalidOperationException(L10n.T("Kopyalama başarısız."));

    /// <summary>Öğenin derin kopyası. <paramref name="newId"/> true ise yeni kimlik verilir.</summary>
    public static LabelElement CloneElement(LabelElement element, bool newId)
    {
        var json = JsonSerializer.Serialize(element, CompactOptions);
        var copy = JsonSerializer.Deserialize<LabelElement>(json, CompactOptions)
                   ?? throw new InvalidOperationException(L10n.T("Kopyalama başarısız."));
        if (newId) copy.Id = LabelElement.NewId();
        return copy;
    }
}
