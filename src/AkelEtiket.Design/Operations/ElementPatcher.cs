using System.Reflection;
using System.Text.Json.Serialization;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Serialization;

namespace AkelEtiket.Design.Operations;

/// <summary>
/// update_element: öğeye kısmi değişiklik uygular. Anahtarlar get_design çıktısındaki (dosyadaki) adlardır;
/// snake_case ve büyük/küçük harf farkı tolere edilir. Değişiklikler önce bir kopyada denenir, doğrulanır,
/// sonra yalnızca değişen özellikler asıl öğeye yazılır (editör canlı güncellenir).
/// </summary>
public static class ElementPatcher
{
    private static readonly HashSet<string> Immutable = new(StringComparer.OrdinalIgnoreCase) { "id", "type", "z_index", "computed" };

    /// <summary>Sık kullanılan eş anlamlılar → model özellik adı.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fontsizept"] = "FontSize",
        ["font_size_pt"] = "FontSize",
        ["align"] = "HorizontalAlignment",
        ["valign"] = "VerticalAlignment",
        ["verticalalign"] = "VerticalAlignment",
        ["strokewidthmm"] = "StrokeWidth",
        ["cornerradiusmm"] = "CornerRadius",
        ["barcodetype"] = "Symbology",
        ["value"] = "Content",
    };

    /// <summary>Değişiklikleri uygular; değişen özellik adlarını (model JSON adlarıyla) döndürür.</summary>
    public static IReadOnlyList<string> Apply(LabelElement element, JsonObject changes, DesignEditContext context)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0)
            throw new ToolException(ToolErrorCodes.InvalidArgument, "changes boş.");

        var writable = WritableProperties(element.GetType());
        var current = (JsonObject)JsonSerializer.SerializeToNode(element, ModelJson.CompactOptions)!;
        var touched = new List<PropertyInfo>();

        foreach (var (rawKey, value) in changes)
        {
            if (Immutable.Contains(rawKey) || Immutable.Contains(Compact(rawKey)))
                throw new ToolException(ToolErrorCodes.InvalidArgument, $"\"{rawKey}\" değiştirilemez.");
            var property = Find(writable, rawKey)
                           ?? throw new ToolException(ToolErrorCodes.InvalidArgument,
                               $"{element.KindName} öğesinde \"{rawKey}\" özelliği yok. Kullanılabilenler: " +
                               string.Join(", ", writable.Keys.Order()) + ".");
            string jsonName = JsonName(property);
            current[jsonName] = NormalizeValue(property, value?.DeepClone(), rawKey);
            touched.Add(property);
        }

        LabelElement candidate;
        try
        {
            candidate = JsonSerializer.Deserialize<LabelElement>(current.ToJsonString(), ModelJson.CompactOptions)
                        ?? throw new ToolException(ToolErrorCodes.InvalidArgument, "Değişiklik uygulanamadı.");
        }
        catch (JsonException ex)
        {
            throw new ToolException(ToolErrorCodes.InvalidArgument, "Değer biçimi hatalı: " + ex.Message.Split(" Path:")[0]);
        }

        Validate(candidate, touched, context);

        var changed = new List<string>();
        foreach (var property in touched.Distinct())
        {
            object? oldValue = property.GetValue(element);
            object? newValue = property.GetValue(candidate);
            if (Equals(oldValue, newValue)) continue;
            property.SetValue(element, newValue);
            changed.Add(JsonName(property));
        }
        if (changed.Count > 0) context.MarkChanged(element);
        return changed;
    }

    private static void Validate(LabelElement candidate, List<PropertyInfo> touched, DesignEditContext context)
    {
        bool contentTouched = touched.Any(p => p.Name is nameof(BarcodeElement.Content) or nameof(BarcodeElement.Symbology)
            or nameof(BarcodeElement.AppendCheckDigit) or nameof(BarcodeElement.WideToNarrowRatio));
        if (candidate is BarcodeElement barcode && contentTouched)
            ElementFactory.ValidateBarcodeContent(barcode, context.PreviewFields, out _);

        if (candidate is ImageElement image && touched.Any(p => p.Name == nameof(ImageElement.AssetId)) && !context.Assets.Contains(image.AssetId))
            throw new ToolException(ToolErrorCodes.NotFound, $"assetId \"{image.AssetId}\" bu tasarımda yok. Yeni görsel için add_image kullanın.");

        if (candidate is TextElement text && touched.Any(p => p.Name == nameof(TextElement.Text)) && text.Text.Length == 0)
            context.Warnings.Add("Metin boş bırakıldı.");

        if (touched.Any(p => p.Name == nameof(LabelElement.Locked)) && candidate.Locked)
            context.Warnings.Add("Öğe kilitlendi: bundan sonra yapay zekâ bu öğeyi değiştiremez.");
    }

    /// <summary>Enum değerlerini esnek okur ("qr_code" → QrCode); diğerlerini olduğu gibi bırakır.</summary>
    private static JsonNode? NormalizeValue(PropertyInfo property, JsonNode? value, string key)
    {
        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type.IsEnum && value is JsonValue v && v.TryGetValue(out string? text))
        {
            if (!LenientEnumConverterFactory.TryParse(type, text, out var parsed))
                throw new ToolException(ToolErrorCodes.InvalidArgument,
                    $"\"{key}\" için geçersiz değer \"{text}\". İzin verilenler: {string.Join(", ", Enum.GetNames(type))}.");
            return JsonValue.Create(parsed!.ToString());
        }
        if (type == typeof(string) && value is JsonValue sv && !sv.TryGetValue(out string? _))
            return JsonValue.Create(sv.ToJsonString().Trim('"'));
        // Koşullu gösterme: işleç esnek okunur ("not_empty" → NotEmpty), alan adı zorunludur.
        if (type == typeof(VisibilityCondition) && value is JsonObject condition)
        {
            var normalized = new JsonObject();
            foreach (var (conditionKey, conditionValue) in condition)
                normalized[JsonNamingPolicy.CamelCase.ConvertName(conditionKey.Replace("_", ""))] = conditionValue?.DeepClone();
            if (normalized["field"] is not JsonValue fieldValue || !fieldValue.TryGetValue(out string? field) || string.IsNullOrWhiteSpace(field))
                throw new ToolException(ToolErrorCodes.InvalidArgument, $"\"{key}\" için \"field\" (veri sütunu adı) gerekli.");
            if (normalized["operator"] is JsonValue op && op.TryGetValue(out string? opText))
            {
                if (!LenientEnumConverterFactory.TryParse(typeof(ConditionOperator), opText, out var parsedOp))
                    throw new ToolException(ToolErrorCodes.InvalidArgument,
                        $"Geçersiz koşul işleci \"{opText}\". İzin verilenler: {string.Join(", ", Enum.GetNames<ConditionOperator>())}.");
                normalized["operator"] = parsedOp!.ToString();
            }
            return normalized;
        }
        return value;
    }

    private static PropertyInfo? Find(Dictionary<string, PropertyInfo> writable, string key)
    {
        if (writable.TryGetValue(key, out var p)) return p;
        string compact = Compact(key);
        if (Aliases.TryGetValue(compact, out var alias) || Aliases.TryGetValue(key, out alias))
            return writable.Values.FirstOrDefault(x => x.Name == alias);
        return writable.Values.FirstOrDefault(x => Compact(x.Name) == compact);
    }

    private static string Compact(string key) => new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static Dictionary<string, PropertyInfo> WritableProperties(Type type)
    {
        var map = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanWrite || p.GetSetMethod() is null || p.GetCustomAttribute<JsonIgnoreAttribute>() is not null) continue;
            if (p.Name is nameof(LabelElement.Id)) continue;
            map[JsonName(p)] = p;
        }
        return map;
    }

    private static string JsonName(PropertyInfo p) => JsonNamingPolicy.CamelCase.ConvertName(p.Name);

    /// <summary>Metindeki {Alan} adlarını doğrular (var olmayan sütun için uyarı).</summary>
    public static void WarnUnknownFields(LabelElement element, IReadOnlyList<string>? columns, DesignEditContext context)
    {
        if (columns is null || columns.Count == 0) return;
        foreach (string field in DesignViews.FieldsOf(element))
        {
            // {TARIH+30} gibi ifadelerde temel ad da denenir ("Model-2" gibi sütun adları yazıldığı gibi eşleşir).
            string baseName = FieldExpression.Parse(field, null).BaseName;
            if (IsBuiltIn(field) || IsBuiltIn(baseName)) continue;
            if (FieldNames.FindIndex(columns, field) < 0 && FieldNames.FindIndex(columns, baseName) < 0)
                context.Warnings.Add($"{{{field}}} veri kaynağında yok. Sütunlar: {string.Join(", ", columns.Take(20))}.");
        }
    }

    /// <summary>Yerleşik alanlar: sıra, tarih/saat, GS1 ve kalıcı sayaçlar ({SAYAC}, {SAYAC.Koli}).</summary>
    private static bool IsBuiltIn(string field) =>
        field is "SIRA" or "TARIH" or "SAAT" or "SSCC" or "GLN" or "GCP" or "SAYAC"
        || field.StartsWith("SAYAC.", StringComparison.Ordinal);
}
