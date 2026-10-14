using System.Diagnostics;
using AkelEtiket.Design.Security;

namespace AkelEtiket.Design.Tools;

/// <summary>
/// Araç kaydı ve yürütücüsü: girdiyi şemaya göre okur, aracı çalıştırır, hataları Türkçe ve kodlu döndürür,
/// her çağrıyı günlüğe yazar. MCP sunucusu, yerel API ve uygulama içi asistan aynı kaydı kullanır.
/// </summary>
public sealed class ToolRegistry : IToolExecutor
{
    /// <summary>Tek araç çıktısındaki metnin üst sınırı (modelin bağlamını korumak için).</summary>
    public const int MaxTextLength = 60_000;

    private readonly Dictionary<string, Registration> _tools = new(StringComparer.Ordinal);
    private readonly List<ToolDefinition> _definitions = [];
    private readonly AuditLog? _audit;

    public ToolRegistry(AuditLog? audit = null)
    {
        _audit = audit;
    }

    public IReadOnlyList<ToolDefinition> Tools => _definitions;

    private sealed record Registration(ToolDefinition Definition, Func<JsonElement, ToolCallContext, CancellationToken, Task<ToolResult>> Invoke);

    public void Add<TArgs>(string name, string title, string description, Func<TArgs, ToolCallContext, CancellationToken, Task<ToolResult>> handler,
        bool readOnly = false, bool destructive = false, bool idempotent = false)
    {
        var definition = new ToolDefinition
        {
            Name = name,
            Title = title,
            Description = description,
            InputSchema = ToolJson.SchemaFor(typeof(TArgs)),
            ReadOnly = readOnly,
            Destructive = destructive,
            Idempotent = idempotent || readOnly,
        };
        _tools[name] = new Registration(definition, (json, ctx, ct) => handler(ToolJson.Deserialize<TArgs>(json), ctx, ct));
        _definitions.Add(definition);
    }

    public async Task<ToolResult> ExecuteAsync(string name, JsonElement arguments, ToolCallContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        string? designId = TryGetString(arguments, "design_id") ?? TryGetString(arguments, "draft_id");
        if (!_tools.TryGetValue(name ?? "", out var registration))
        {
            var unknown = Error(new ToolException(ToolErrorCodes.NotFound, $"Bilinmeyen araç: {name}. Araç listesi için tools/list kullanın."));
            Write(context, name ?? "?", designId, unknown, stopwatch, null);
            return unknown;
        }
        ToolResult result;
        try
        {
            result = await registration.Invoke(arguments, context, cancellationToken).ConfigureAwait(false);
        }
        catch (ToolException ex)
        {
            result = Error(ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            result = Error(new ToolException(ToolErrorCodes.Internal, "Beklenmeyen hata: " + ex.Message));
        }
        Write(context, name!, designId, result, stopwatch, (result.Structured as JsonObject)?["version"]?.ToString());
        return result;
    }

    private void Write(ToolCallContext context, string tool, string? designId, ToolResult result, Stopwatch stopwatch, string? version) =>
        _audit?.Write(new AuditEntry(DateTimeOffset.Now, context.ClientName, tool, designId, !result.IsError, result.ErrorCode, version,
            stopwatch.ElapsedMilliseconds, null));

    private static string? TryGetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // ───────────────────────── Sonuç yardımcıları ─────────────────────────

    public static ToolResult Ok(string summary, JsonObject data, IReadOnlyList<ToolImage>? images = null)
    {
        string json = data.ToJsonString(ToolJson.OutputOptions);
        string text = summary + "\n\n" + json;
        if (text.Length > MaxTextLength)
            text = text[..MaxTextLength] + "\n… (çıktı kısaltıldı; daha dar bir istekle tekrar deneyin)";
        return new ToolResult { Text = text, Structured = data, Images = images ?? [] };
    }

    public static ToolResult Error(ToolException ex)
    {
        var data = new JsonObject { ["error_code"] = ex.Code, ["message"] = ex.Message };
        if (ex.Details is not null)
        {
            try
            {
                data["details"] = JsonSerializer.SerializeToNode(ex.Details, ToolJson.OutputOptions);
            }
            catch (NotSupportedException)
            {
            }
        }
        return new ToolResult
        {
            IsError = true,
            ErrorCode = ex.Code,
            Text = $"HATA [{ex.Code}]: {ex.Message}" + (data["details"] is { } d ? "\n" + d.ToJsonString(ToolJson.OutputOptions) : ""),
            Structured = data,
        };
    }
}
