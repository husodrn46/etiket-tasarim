using System.Text.Json;
using System.Text.Json.Nodes;
using AkelEtiket.Assistant.Chat;
using AkelEtiket.Core.Automation;

namespace AkelEtiket.Assistant.Providers;

/// <summary>
/// OpenAI uyumlu sohbet uç noktası (<c>POST {adres}/chat/completions</c>): Ollama, LM Studio gibi yerel modeller veya
/// uyumlu servisler. Anahtar isteğe bağlıdır (Bearer). Araç sonuçlarındaki görseller, çoğu yerel sunucu "tool"
/// mesajında görsel kabul etmediği için ayrı bir kullanıcı mesajıyla ve yalnızca ayarlarda izin varsa gönderilir.
/// </summary>
public sealed class OpenAiCompatibleChatModel : IChatModel
{
    /// <summary>Seçenek verilmezse yanıt başına çıktı sınırı.</summary>
    public const int DefaultMaxOutputTokens = 4096;

    private readonly HttpClient _http;
    private readonly AssistantProviderSettings _settings;
    private readonly string? _apiKey;
    private readonly Uri _endpoint;
    private readonly ChatModelOptions _options;
    private readonly string _displayAddress;

    public OpenAiCompatibleChatModel(HttpClient http, AssistantProviderSettings settings, string? apiKey, ChatModelOptions? options = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        // Yerel modellerin bağlam penceresi genellikle küçüktür; varsayılan çıktı sınırı daha temkinli tutulur.
        _options = options ?? new ChatModelOptions { MaxOutputTokens = DefaultMaxOutputTokens };
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            throw new AssistantConfigurationException(
                "OpenAI uyumlu sağlayıcı için adres girilmedi (ör. Ollama: http://127.0.0.1:11434/v1, LM Studio: http://127.0.0.1:1234/v1).");
        if (string.IsNullOrWhiteSpace(settings.Model))
            throw new AssistantConfigurationException("OpenAI uyumlu sağlayıcı için model adı girilmedi (ör. qwen2.5 veya llama3.2-vision).");

        string baseUrl = settings.BaseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl + "/chat/completions", UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
            throw new AssistantConfigurationException($"Model sunucusu adresi geçersiz: \"{settings.BaseUrl}\".");
        _endpoint = endpoint;
        _displayAddress = baseUrl;
        Model = settings.Model.Trim();
    }

    public string ProviderName => "OpenAI uyumlu model";

    public string Model { get; }

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_settings.Kind != AssistantProviderKind.OpenAiCompatible || !_settings.HasValidConsent)
            throw new AssistantConsentException();
        if (request.Messages.Count == 0) throw new ArgumentException("En az bir mesaj gerekli.", nameof(request));

        string body = await ProviderHttp.SendAsync(
            _http,
            () => BuildHttpRequest(request),
            _options,
            failure => ProviderHttp.MapStatus(failure, $"Model sunucusu ({_displayAddress})", Model),
            null,
            ex => new ChatModelException(ChatErrorKind.Network,
                ProviderHttp.IsConnectionRefused(ex)
                    ? $"Model sunucusuna bağlanılamadı ({_displayAddress}). Ollama/LM Studio çalışıyor mu, adres doğru mu?"
                    : $"Model sunucusuna bağlanılamadı ({_displayAddress}). Ayrıntı: {ProviderHttp.ScrubText(ex.Message, _apiKey)}",
                inner: ex),
            _apiKey,
            cancellationToken).ConfigureAwait(false);

        return ParseResponse(body);
    }

    private HttpRequestMessage BuildHttpRequest(ChatRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = ProviderHttp.JsonContent(BuildBody(request)),
        };
        if (_apiKey is not null) message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _apiKey);
        message.Headers.Accept.ParseAdd("application/json");
        return message;
    }

    /// <summary>İstek gövdesi (testler için içeriden erişilebilir).</summary>
    internal JsonObject BuildBody(ChatRequest request)
    {
        var messages = new JsonArray();
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt });
        foreach (var chatMessage in request.Messages)
        {
            foreach (var wire in ToWire(chatMessage)) messages.Add(wire);
        }

        var body = new JsonObject
        {
            ["model"] = Model,
            ["messages"] = messages,
            ["max_tokens"] = request.MaxOutputTokens ?? _options.MaxOutputTokens,
            ["stream"] = false,
        };
        if (request.Tools.Count > 0)
        {
            var tools = new JsonArray();
            foreach (var tool in request.Tools)
            {
                tools.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = tool.InputSchema.DeepClone(),
                    },
                });
            }
            body["tools"] = tools;
        }
        return body;
    }

    private IEnumerable<JsonObject> ToWire(ChatMessage message)
    {
        if (message.Role == ChatRole.Assistant)
        {
            var assistant = new JsonObject { ["role"] = "assistant" };
            string text = message.Text;
            assistant["content"] = text.Length > 0 ? text : null;
            var calls = message.ToolCalls;
            if (calls.Count > 0)
            {
                var array = new JsonArray();
                foreach (var call in calls)
                {
                    array.Add(new JsonObject
                    {
                        ["id"] = call.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = call.Name,
                            ["arguments"] = call.Arguments.GetRawText(),
                        },
                    });
                }
                assistant["tool_calls"] = array;
            }
            yield return assistant;
            yield break;
        }

        // Kullanıcı mesajı: önce araç sonuçları ("tool" rolü), sonra metin/görseller ve araç görselleri.
        var followUpImages = new List<ChatImageBlock>();
        foreach (var result in message.Blocks.OfType<ChatToolResultBlock>())
        {
            string content = result.IsError ? "HATA: " + result.Text : result.Text;
            if (result.Images.Count > 0 && !_settings.SendPreviewImages)
                content += "\n[Görsel gönderilmedi: ayarlarda görsel gönderimi kapalı.]";
            yield return new JsonObject
            {
                ["role"] = "tool",
                ["tool_call_id"] = result.ToolCallId,
                ["content"] = content.Length > 0 ? content : "(boş sonuç)",
            };
            if (_settings.SendPreviewImages) followUpImages.AddRange(result.Images);
        }

        var userParts = message.Blocks.Where(b => b is ChatTextBlock or ChatImageBlock).ToList();
        if (followUpImages.Count > 0)
        {
            userParts.Insert(0, new ChatTextBlock("Araç sonuçlarındaki görseller:"));
            userParts.AddRange(followUpImages);
        }
        if (userParts.Count == 0) yield break;

        bool hasImage = userParts.Any(b => b is ChatImageBlock) && _settings.SendPreviewImages;
        if (!hasImage)
        {
            string text = string.Join("\n\n", userParts.Select(p => p switch
            {
                ChatTextBlock t => t.Text,
                _ => "[Görsel gönderilmedi: ayarlarda görsel gönderimi kapalı.]",
            }));
            yield return new JsonObject { ["role"] = "user", ["content"] = text };
            yield break;
        }

        var parts = new JsonArray();
        foreach (var part in userParts)
        {
            switch (part)
            {
                case ChatTextBlock t:
                    parts.Add(new JsonObject { ["type"] = "text", ["text"] = t.Text });
                    break;
                case ChatImageBlock image:
                    parts.Add(new JsonObject
                    {
                        ["type"] = "image_url",
                        ["image_url"] = new JsonObject
                        {
                            ["url"] = $"data:{image.MimeType};base64,{Convert.ToBase64String(image.Data)}",
                        },
                    });
                    break;
            }
        }
        yield return new JsonObject { ["role"] = "user", ["content"] = parts };
    }

    /// <summary>Yanıtı çözümler (testler için içeriden erişilebilir).</summary>
    internal ChatResponse ParseResponse(string body)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(body) as JsonObject
                   ?? throw new ChatModelException(ChatErrorKind.InvalidResponse, "Model sunucusundan anlaşılamayan bir yanıt geldi.");
        }
        catch (JsonException ex)
        {
            throw new ChatModelException(ChatErrorKind.InvalidResponse, "Model sunucusundan anlaşılamayan bir yanıt geldi (JSON çözümlenemedi).", inner: ex);
        }

        if (root["choices"] is not JsonArray { Count: > 0 } choices || choices[0] is not JsonObject choice)
            throw new ChatModelException(ChatErrorKind.InvalidResponse, "Model sunucusunun yanıtında seçenek (choices) yok.");

        var message = choice["message"] as JsonObject ?? new JsonObject();
        var blocks = new List<ChatContentBlock>();
        string text = ReadContentText(message["content"]);
        if (text.Length > 0) blocks.Add(new ChatTextBlock(text));

        if (message["tool_calls"] is JsonArray calls)
        {
            int index = 0;
            foreach (var item in calls)
            {
                index++;
                if (item is not JsonObject call) continue;
                var function = call["function"] as JsonObject;
                string name = ProviderHttp.AsString(function?["name"]) ?? ProviderHttp.AsString(call["name"]) ?? "";
                if (name.Length == 0) continue;
                string id = ProviderHttp.AsString(call["id"]) is { Length: > 0 } given ? given : $"call_{index}_{Guid.NewGuid():N}"[..20];
                blocks.Add(new ChatToolCallBlock(id, name, ParseArguments(function?["arguments"] ?? call["arguments"])));
            }
        }

        string? rawStop = ProviderHttp.AsString(choice["finish_reason"]);
        bool hasCalls = blocks.OfType<ChatToolCallBlock>().Any();
        var stop = rawStop switch
        {
            "tool_calls" or "function_call" => ChatStopReason.ToolUse,
            "length" => ChatStopReason.MaxTokens,
            "content_filter" => ChatStopReason.Refusal,
            "stop" or null => hasCalls ? ChatStopReason.ToolUse : ChatStopReason.EndTurn,
            _ => hasCalls ? ChatStopReason.ToolUse : ChatStopReason.Other,
        };

        ChatUsage? usage = null;
        if (root["usage"] is JsonObject u)
            usage = new ChatUsage(ReadInt(u["prompt_tokens"]), ReadInt(u["completion_tokens"]));

        return new ChatResponse
        {
            StopReason = stop,
            RawStopReason = rawStop,
            Message = new ChatMessage(ChatRole.Assistant, blocks),
            Usage = usage,
            ServedModel = ProviderHttp.AsString(root["model"]),
        };
    }

    private static string ReadContentText(JsonNode? content)
    {
        switch (content)
        {
            case JsonValue value:
                return ProviderHttp.AsString(value) ?? "";
            case JsonArray parts:
                return string.Join("", parts.OfType<JsonObject>()
                    .Where(p => ProviderHttp.AsString(p["type"]) is null or "text")
                    .Select(p => ProviderHttp.AsString(p["text"]) ?? ""));
            default:
                return "";
        }
    }

    /// <summary>Argümanlar metin (JSON dizesi) veya doğrudan nesne olabilir; bozuksa boş nesne.</summary>
    private static JsonElement ParseArguments(JsonNode? arguments)
    {
        try
        {
            switch (arguments)
            {
                case JsonObject obj:
                    return JsonSerializer.SerializeToElement(obj);
                case JsonValue value when ProviderHttp.AsString(value) is { } text && text.Trim().Length > 0:
                    using (var document = JsonDocument.Parse(text))
                    {
                        if (document.RootElement.ValueKind == JsonValueKind.Object) return document.RootElement.Clone();
                    }
                    break;
            }
        }
        catch (JsonException)
        {
            // Bozuk argüman: boş nesneyle devam edilir; araç doğrulaması hatayı açıklar.
        }
        using var empty = JsonDocument.Parse("{}");
        return empty.RootElement.Clone();
    }

    private static int ReadInt(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out int number) ? number : 0;
}
