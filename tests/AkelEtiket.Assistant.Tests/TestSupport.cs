using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AkelEtiket.Assistant;
using AkelEtiket.Assistant.Chat;
using AkelEtiket.Assistant.Providers;
using AkelEtiket.Core.Automation;
using AkelEtiket.Design.Tools;

namespace AkelEtiket.Assistant.Tests;

/// <summary>Gönderilen isteği (gövde dahil) yakalayan kayıt.</summary>
internal sealed record CapturedRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body)
{
    public JsonObject Json => (JsonObject)JsonNode.Parse(Body)!;

    public string? Header(string name) => Headers.TryGetValue(name.ToLowerInvariant(), out var value) ? value : null;
}

/// <summary>Gerçek ağa çıkmayan, sırayla hazır yanıt veren sahte HTTP işleyici.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public List<CapturedRequest> Requests { get; } = [];

    public FakeHttpHandler Json(string body, HttpStatusCode status = HttpStatusCode.OK, Action<HttpResponseMessage>? configure = null)
    {
        _responses.Enqueue((_, _) =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            configure?.Invoke(response);
            return Task.FromResult(response);
        });
        return this;
    }

    public FakeHttpHandler Throw(Exception exception)
    {
        _responses.Enqueue((_, _) => Task.FromException<HttpResponseMessage>(exception));
        return this;
    }

    public FakeHttpHandler Hang()
    {
        _responses.Enqueue(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("ulaşılamaz");
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers) headers[header.Key.ToLowerInvariant()] = string.Join(",", header.Value);
        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers) headers[header.Key.ToLowerInvariant()] = string.Join(",", header.Value);
        }
        Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, headers, body));
        if (_responses.Count == 0) throw new InvalidOperationException("Sahte işleyicide yanıt kalmadı.");
        return await _responses.Dequeue()(request, cancellationToken);
    }
}

internal static class TestData
{
    public const string AnthropicKey = "sk-ant-test-GIZLI-ANAHTAR-0123456789";
    public const string OpenAiKey = "lm-test-GIZLI-9876543210";

    public static AssistantProviderSettings Settings(AssistantProviderKind kind, bool consent = true, bool sendImages = true,
        string baseUrl = "", string model = "")
    {
        var settings = new AssistantProviderSettings
        {
            Kind = kind,
            BaseUrl = baseUrl,
            Model = model,
            SendPreviewImages = sendImages,
        };
        if (consent)
        {
            settings.DataSharingConsent = true;
            settings.ConsentScope = settings.CurrentScope;
            settings.ConsentGivenAt = DateTimeOffset.Now;
        }
        return settings;
    }

    /// <summary>Beklemeleri kaydeden ama gerçekten beklemeyen seçenekler.</summary>
    public static ChatModelOptions Options(List<TimeSpan>? delays = null) => new()
    {
        Delay = (wait, _) =>
        {
            delays?.Add(wait);
            return Task.CompletedTask;
        },
    };

    public static ToolDefinition Tool(string name, string title = "Başlık", string description = "Açıklama") => new()
    {
        Name = name,
        Title = title,
        Description = description,
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["design_id"] = new JsonObject { ["type"] = "string" },
                ["text"] = new JsonObject { ["type"] = "string", ["description"] = "Metin" },
            },
            ["required"] = new JsonArray("design_id"),
        },
    };

    public static ChatToolSpec Spec(string name) => ChatToolSpec.From(Tool(name, "Metin ekle", "Etikete metin kutusu ekler."));

    public static JsonElement Args(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    public static string AnthropicText(string text, string stop = "end_turn") => new JsonObject
    {
        ["id"] = "msg_1",
        ["type"] = "message",
        ["role"] = "assistant",
        ["model"] = "claude-opus-5-5",
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["stop_reason"] = stop,
        ["usage"] = new JsonObject { ["input_tokens"] = 10, ["output_tokens"] = 5 },
    }.ToJsonString();
}

/// <summary>Sırayla hazır yanıt veren sahte model; gönderilen istekleri kaydeder.</summary>
internal sealed class FakeChatModel : IChatModel
{
    private readonly Queue<Func<ChatRequest, CancellationToken, ChatResponse>> _script = new();

    public List<ChatRequest> Requests { get; } = [];

    public Func<ChatRequest, CancellationToken, ChatResponse>? Fallback { get; set; }

    public string ProviderName => "Sahte";

    public string Model => "sahte-model";

    public FakeChatModel Reply(Func<ChatRequest, CancellationToken, ChatResponse> step)
    {
        _script.Enqueue(step);
        return this;
    }

    public FakeChatModel ReplyText(string text) => Reply((_, _) => Text(text));

    public FakeChatModel ReplyTools(params (string Id, string Name, string Args)[] calls) => Reply((_, _) => Tools(null, calls));

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        var step = _script.Count > 0 ? _script.Dequeue() : Fallback ?? throw new InvalidOperationException("Sahte modelde yanıt kalmadı.");
        return Task.FromResult(step(request, cancellationToken));
    }

    public static ChatResponse Text(string text, ChatStopReason stop = ChatStopReason.EndTurn) => new()
    {
        StopReason = stop,
        Message = new ChatMessage(ChatRole.Assistant, [new ChatTextBlock(text)]),
        Usage = new ChatUsage(100, 20),
    };

    public static ChatResponse Tools(string? text, params (string Id, string Name, string Args)[] calls)
    {
        var blocks = new List<ChatContentBlock>();
        if (text is not null) blocks.Add(new ChatTextBlock(text));
        blocks.AddRange(calls.Select(c => new ChatToolCallBlock(c.Id, c.Name, TestData.Args(c.Args))));
        return new ChatResponse
        {
            StopReason = ChatStopReason.ToolUse,
            Message = new ChatMessage(ChatRole.Assistant, blocks),
            Usage = new ChatUsage(100, 20),
        };
    }
}

/// <summary>Araçları kaydeden sahte yürütücü.</summary>
internal sealed class FakeToolExecutor : IToolExecutor
{
    public FakeToolExecutor(params string[] names) => Tools = names.Select(n => TestData.Tool(n)).ToList();

    public IReadOnlyList<ToolDefinition> Tools { get; }

    public List<(string Name, string Args, ToolCallContext Context)> Calls { get; } = [];

    public Func<string, JsonElement, CancellationToken, ToolResult>? Handler { get; set; }

    public Task<ToolResult> ExecuteAsync(string name, JsonElement arguments, ToolCallContext context, CancellationToken cancellationToken)
    {
        Calls.Add((name, arguments.GetRawText(), context));
        var result = Handler?.Invoke(name, arguments, cancellationToken)
                     ?? new ToolResult { Text = $"{{\"ok\":true,\"tool\":\"{name}\"}}" };
        return Task.FromResult(result);
    }
}

/// <summary>Olayları eşzamanlı toplayan ilerleme alıcısı (Progress&lt;T&gt; gibi iş parçacığına göndermez).</summary>
internal sealed class EventLog : IProgress<AssistantEvent>
{
    public List<AssistantEvent> Events { get; } = [];

    public void Report(AssistantEvent value) => Events.Add(value);
}
