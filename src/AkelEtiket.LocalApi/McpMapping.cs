using System.Text.Json;
using System.Text.Json.Nodes;
using AkelEtiket.Design.Tools;
using ModelContextProtocol.Protocol;

namespace AkelEtiket.LocalApi;

/// <summary>Tasarım araçları ↔ MCP protokol türleri.</summary>
public static class McpMapping
{
    public static ListToolsResult ListTools(IToolExecutor tools) => new()
    {
        Tools = tools.Tools.Select(ToMcpTool).ToList(),
    };

    public static Tool ToMcpTool(ToolDefinition definition) => new()
    {
        Name = definition.Name,
        Title = definition.Title,
        Description = definition.Description,
        InputSchema = JsonSerializer.SerializeToElement(definition.InputSchema),
        Annotations = new ToolAnnotations
        {
            Title = definition.Title,
            ReadOnlyHint = definition.ReadOnly,
            DestructiveHint = definition.Destructive,
            IdempotentHint = definition.Idempotent,
            OpenWorldHint = false,
        },
    };

    public static CallToolResult ToCallToolResult(ToolResult result)
    {
        var content = new List<ContentBlock> { new TextContentBlock { Text = result.Text } };
        foreach (var image in result.Images) content.Add(ImageContentBlock.FromBytes(image.Data, image.MimeType));
        return new CallToolResult
        {
            Content = content,
            StructuredContent = result.Structured is JsonObject obj ? JsonSerializer.SerializeToElement(obj) : null,
            IsError = result.IsError,
        };
    }

    public static JsonElement ArgumentsToJson(IDictionary<string, JsonElement>? arguments)
    {
        var obj = new JsonObject();
        if (arguments is not null)
        {
            foreach (var (key, value) in arguments) obj[key] = JsonNode.Parse(value.GetRawText());
        }
        return JsonSerializer.SerializeToElement(obj);
    }
}
