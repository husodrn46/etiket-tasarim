using System.ComponentModel;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AkelEtiket.Design.Tools;

/// <summary>Araç girdi/çıktıları için JSON ayarları ve şema üretimi.</summary>
public static class ToolJson
{
    /// <summary>Girdiler: snake_case adlar, esnek enum okuma, sayılar metin olarak da kabul edilir, bilinmeyen alan hatadır.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions(lenientNumbers: true);

    /// <summary>Şema üretimi için: sayılar şemada yalnızca "number" görünsün (okurken metin de kabul edilir).</summary>
    private static JsonSerializerOptions SchemaOptions { get; } = CreateOptions(lenientNumbers: false);

    /// <summary>Çıktılar (insan okunur girintili JSON, Türkçe karakterler kaçışsız).</summary>
    public static JsonSerializerOptions OutputOptions { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static JsonSerializerOptions CreateOptions(bool lenientNumbers)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            NumberHandling = lenientNumbers ? JsonNumberHandling.AllowReadingFromString : JsonNumberHandling.Strict,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new LenientEnumConverterFactory());
        options.MakeReadOnly();
        return options;
    }

    public static T Deserialize<T>(JsonElement arguments)
    {
        try
        {
            var element = arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? JsonDocument.Parse("{}").RootElement
                : arguments;
            return element.Deserialize<T>(Options)
                   ?? throw new ToolException(ToolErrorCodes.InvalidArgument, "Girdi boş.");
        }
        catch (JsonException ex)
        {
            throw new ToolException(ToolErrorCodes.InvalidArgument, "Girdi okunamadı: " + Clean(ex.Message));
        }
    }

    /// <summary>Girdi türünden (açıklamalarıyla) JSON şeması üretir.</summary>
    public static JsonObject SchemaFor(Type argsType)
    {
        var exporterOptions = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = Transform,
        };
        var node = SchemaOptions.GetJsonSchemaAsNode(argsType, exporterOptions) as JsonObject ?? new JsonObject();
        node.Remove("$schema");
        if (!node.ContainsKey("type")) node["type"] = "object";
        if (!node.ContainsKey("properties")) node["properties"] = new JsonObject();
        // Zorunlu olmayan alanlarda "null" türünü kaldır: modeller için daha sade şema.
        if (node["properties"] is JsonObject props)
        {
            var required = (node["required"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToHashSet() ?? [];
            foreach (var (name, value) in props.ToList())
            {
                if (value is JsonObject prop && !required.Contains(name)) StripNull(prop);
            }
        }
        node["additionalProperties"] = false;
        return node;
    }

    private static JsonNode Transform(JsonSchemaExporterContext context, JsonNode schema)
    {
        Type type = context.TypeInfo.Type;
        Type? enumType = type.IsEnum ? type : Nullable.GetUnderlyingType(type) is { IsEnum: true } u ? u : null;
        // Özel dönüştürücülü türler (esnek enum, JsonObject) için dışa aktarıcı "true" (her şey) üretir: nesneye çevir.
        if (schema is not JsonObject obj)
        {
            if (enumType is null && type != typeof(JsonObject)) return schema;
            obj = new JsonObject();
        }
        if (enumType is not null)
        {
            obj.Clear();
            obj["type"] = Nullable.GetUnderlyingType(type) is null ? "string" : new JsonArray("string", "null");
            obj["enum"] = new JsonArray(Enum.GetNames(enumType).Select(n => (JsonNode)JsonNamingPolicy.SnakeCaseLower.ConvertName(n)).ToArray());
        }
        if (type == typeof(JsonObject))
        {
            obj.Clear();
            obj["type"] = "object";
        }
        var description = context.PropertyInfo?.AttributeProvider?.GetCustomAttributes(typeof(DescriptionAttribute), false)
            .OfType<DescriptionAttribute>().FirstOrDefault()?.Description;
        if (description is not null)
        {
            var copy = obj.ToList();
            obj.Clear();
            obj["description"] = description;
            foreach (var (key, value) in copy)
            {
                if (key == "description") continue;
                obj[key] = value?.DeepClone();
            }
        }
        return obj;
    }

    private static void StripNull(JsonObject prop)
    {
        if (prop["type"] is JsonArray types)
        {
            var kept = types.Select(t => t!.GetValue<string>()).Where(t => t != "null").ToList();
            prop["type"] = kept.Count == 1 ? kept[0] : new JsonArray(kept.Select(k => (JsonNode)k).ToArray());
        }
        if (prop["enum"] is JsonArray values)
        {
            foreach (var n in values.Where(v => v is null).ToList()) values.Remove(n);
        }
    }

    private static string Clean(string message)
    {
        int index = message.IndexOf(" Path:", StringComparison.Ordinal);
        return index > 0 ? message[..index] : message;
    }
}

/// <summary>
/// Esnek enum okuyucu: "qr_code", "QrCode", "qrcode", "QR-CODE" hepsi aynı değeri verir; yazarken snake_case.
/// (Dil modelleri farklı yazımlar kullanabilir.)
/// </summary>
public sealed class LenientEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(LenientEnumConverter<>).MakeGenericType(typeToConvert))!;

    /// <summary>Metni enum değerine çevirir (bulunamazsa false).</summary>
    public static bool TryParse(Type enumType, string text, out object? value)
    {
        string wanted = Normalize(text);
        foreach (string name in Enum.GetNames(enumType))
        {
            if (Normalize(name) == wanted)
            {
                value = Enum.Parse(enumType, name);
                return true;
            }
        }
        value = null;
        return false;
    }

    public static string Normalize(string text) =>
        new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private sealed class LenientEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int number) && Enum.IsDefined(typeof(T), number))
                return (T)Enum.ToObject(typeof(T), number);
            if (reader.TokenType == JsonTokenType.String && TryParse(typeof(T), reader.GetString() ?? "", out var value))
                return (T)value!;
            string allowed = string.Join(", ", Enum.GetNames<T>().Select(n => JsonNamingPolicy.SnakeCaseLower.ConvertName(n)));
            throw new JsonException($"Geçersiz değer. İzin verilenler: {allowed}.");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()));
    }
}
