using System.Text.Json;
using System.Text.Json.Nodes;
using AkelEtiket.Assistant.Chat;
using AkelEtiket.Core.Automation;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Assistant.Providers;

/// <summary>
/// Anthropic Claude (Messages API, <c>POST /v1/messages</c>). Kullanıcının kendi API anahtarıyla çalışır; anahtar
/// yalnızca bellekte tutulur, hiçbir iletiye veya günlüğe yazılmaz.
/// </summary>
/// <remarks>
/// Asistan mesajlarının ham içeriği (düşünme blokları ve imzaları dahil) bir sonraki istekte değiştirilmeden geri
/// gönderilir; sistem istemi ve araç listesi bir sohbet boyunca sabit kalmalıdır (bkz. <see cref="Assistant.DesignAssistantSession"/>).
/// </remarks>
public sealed class AnthropicChatModel : IChatModel
{
    public const string DefaultBaseUrl = "https://api.anthropic.com";
    public const string DefaultModel = "claude-opus-5-5";
    public const string ApiVersion = "2023-06-01";
    public const string ProviderTag = "anthropic";

    /// <summary>Sunucu tarafı yedek model (fallbacks: "default") için beta başlığı.</summary>
    public const string FallbackBeta = "server-side-fallback-2026-07-01";

    /// <summary>Ayarlar ekranında önerilecek modeller (en yetenekliden hızlıya).</summary>
    public static IReadOnlyList<string> SuggestedModels { get; } = ["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5"];

    /// <summary>Sunucu tarafı yedek modeli (varsayılan biçim) destekleyen modeller.</summary>
    private static readonly HashSet<string> FallbackModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "claude-opus-5-5", "claude-sonnet-5-5", "claude-opus-5", "claude-fable-5-1", "claude-fable-5",
    };

    private readonly HttpClient _http;
    private readonly AssistantProviderSettings _settings;
    private readonly string _apiKey;
    private readonly Uri _endpoint;
    private readonly ChatModelOptions _options;
    private bool _cachingEnabled;
    private bool _fallbackEnabled;

    public AnthropicChatModel(HttpClient http, AssistantProviderSettings settings, string apiKey, ChatModelOptions? options = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new AssistantConfigurationException(L10n.T("Anthropic API anahtarı girilmedi. Ayarlar → Yapay Zekâ bölümünden anahtarınızı girin."));
        _apiKey = apiKey.Trim();
        _options = options ?? new ChatModelOptions();
        Model = string.IsNullOrWhiteSpace(settings.Model) ? DefaultModel : settings.Model.Trim();

        string baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? DefaultBaseUrl : settings.BaseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl + "/v1/messages", UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
            throw new AssistantConfigurationException(L10n.T($"Anthropic adresi geçersiz: \"{settings.BaseUrl}\"."));
        _endpoint = endpoint;

        bool officialEndpoint = string.Equals(baseUrl, DefaultBaseUrl, StringComparison.OrdinalIgnoreCase);
        _cachingEnabled = _options.EnablePromptCaching;
        // Yedek model varsayılan olarak kapalı: kullanıcı hangi modeli seçtiyse veri yalnızca ona gider.
        _fallbackEnabled = (_options.UseServerSideFallback ?? false) && officialEndpoint && FallbackModels.Contains(Model);
    }

    public string ProviderName => "Anthropic Claude";

    public string Model { get; }

    /// <summary>İstem önbelleği şu an açık mı (sağlayıcı reddederse kapanır).</summary>
    public bool PromptCachingActive => _cachingEnabled;

    /// <summary>Sunucu tarafı yedek model şu an açık mı (sağlayıcı reddederse kapanır).</summary>
    public bool ServerSideFallbackActive => _fallbackEnabled;

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConsent();
        if (request.Messages.Count == 0) throw new ArgumentException("En az bir mesaj gerekli.", nameof(request));

        string body = await ProviderHttp.SendAsync(
            _http,
            () => BuildHttpRequest(request),
            _options,
            failure => ProviderHttp.MapStatus(failure, ProviderName, Model),
            TryDisableOptionalFeature,
            ex => new ChatModelException(ChatErrorKind.Network,
                L10n.T("Anthropic sunucusuna bağlanılamadı. İnternet bağlantınızı kontrol edin. Ayrıntı: ") +
                ProviderHttp.ScrubText(ex.Message, _apiKey), inner: ex),
            _apiKey,
            cancellationToken).ConfigureAwait(false);

        return ParseResponse(body);
    }

    private void EnsureConsent()
    {
        // Savunma derinliği: arayüz de denetler, ama izin yoksa buradan hiçbir veri çıkmaz.
        if (_settings.Kind != AssistantProviderKind.Anthropic || !_settings.HasValidConsent)
            throw new AssistantConsentException();
    }

    /// <summary>İsteğe bağlı bir özellik (yedek model, önbellek) reddedildiyse kapatıp yeniden dener.</summary>
    private bool TryDisableOptionalFeature(ProviderHttpFailure failure)
    {
        if (failure.StatusCode != 400) return false;
        string text = (failure.ErrorMessage ?? failure.Body).ToLowerInvariant();
        if (_fallbackEnabled && (text.Contains("fallback") || text.Contains("anthropic-beta")))
        {
            _fallbackEnabled = false;
            return true;
        }
        if (_cachingEnabled && text.Contains("cache_control"))
        {
            _cachingEnabled = false;
            return true;
        }
        return false;
    }

    private HttpRequestMessage BuildHttpRequest(ChatRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = ProviderHttp.JsonContent(BuildBody(request)),
        };
        message.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
        message.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);
        if (_fallbackEnabled) message.Headers.TryAddWithoutValidation("anthropic-beta", FallbackBeta);
        message.Headers.Accept.ParseAdd("application/json");
        return message;
    }

    /// <summary>İstek gövdesi (testler için içeriden erişilebilir).</summary>
    internal JsonObject BuildBody(ChatRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = request.MaxOutputTokens ?? _options.MaxOutputTokens,
        };
        if (!string.IsNullOrEmpty(request.SystemPrompt)) body["system"] = request.SystemPrompt;
        if (request.Tools.Count > 0)
        {
            var tools = new JsonArray();
            foreach (var tool in request.Tools)
            {
                tools.Add(new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["input_schema"] = tool.InputSchema.DeepClone(),
                });
            }
            body["tools"] = tools;
        }

        var messages = new JsonArray();
        foreach (var chatMessage in request.Messages) messages.Add(ToWire(chatMessage));
        body["messages"] = messages;

        if (!string.IsNullOrWhiteSpace(_options.Effort))
            body["output_config"] = new JsonObject { ["effort"] = _options.Effort.Trim().ToLowerInvariant() };
        if (_cachingEnabled) body["cache_control"] = new JsonObject { ["type"] = "ephemeral" };
        if (_fallbackEnabled) body["fallbacks"] = "default";
        return body;
    }

    private JsonObject ToWire(ChatMessage message)
    {
        if (message.Role == ChatRole.Assistant)
        {
            // Aynı sağlayıcının ham içeriği olduğu gibi geri gönderilir (thinking imzaları korunur).
            JsonNode content = message.ProviderTag == ProviderTag && message.ProviderContent is not null
                ? message.ProviderContent.DeepClone()
                : AssistantBlocks(message);
            return new JsonObject { ["role"] = "assistant", ["content"] = content };
        }

        var parts = new JsonArray();
        foreach (var block in message.Blocks)
        {
            switch (block)
            {
                case ChatToolResultBlock result:
                    parts.Add(ToolResult(result));
                    break;
                case ChatTextBlock text:
                    parts.Add(new JsonObject { ["type"] = "text", ["text"] = text.Text });
                    break;
                case ChatImageBlock image:
                    parts.Add(Image(image));
                    break;
                case ChatToolCallBlock:
                    throw new InvalidOperationException(L10n.T("Kullanıcı mesajında araç çağrısı olamaz."));
            }
        }
        return new JsonObject { ["role"] = "user", ["content"] = parts };
    }

    private static JsonArray AssistantBlocks(ChatMessage message)
    {
        var parts = new JsonArray();
        foreach (var block in message.Blocks)
        {
            switch (block)
            {
                case ChatTextBlock text when text.Text.Length > 0:
                    parts.Add(new JsonObject { ["type"] = "text", ["text"] = text.Text });
                    break;
                case ChatToolCallBlock call:
                    parts.Add(new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = call.Id,
                        ["name"] = call.Name,
                        ["input"] = JsonNode.Parse(call.Arguments.GetRawText()),
                    });
                    break;
            }
        }
        return parts;
    }

    private JsonObject ToolResult(ChatToolResultBlock result)
    {
        var content = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = result.Text.Length > 0 ? result.Text : L10n.T("(boş sonuç)") } };
        foreach (var image in result.Images) content.Add(Image(image));
        var node = new JsonObject
        {
            ["type"] = "tool_result",
            ["tool_use_id"] = result.ToolCallId,
            ["content"] = content,
        };
        if (result.IsError) node["is_error"] = true;
        return node;
    }

    private JsonObject Image(ChatImageBlock image)
    {
        if (!_settings.SendPreviewImages)
            return new JsonObject { ["type"] = "text", ["text"] = L10n.T("[Görsel gönderilmedi: ayarlarda görsel gönderimi kapalı.]") };
        return new JsonObject
        {
            ["type"] = "image",
            ["source"] = new JsonObject
            {
                ["type"] = "base64",
                ["media_type"] = image.MimeType,
                ["data"] = Convert.ToBase64String(image.Data),
            },
        };
    }

    /// <summary>Yanıtı çözümler (testler için içeriden erişilebilir).</summary>
    internal ChatResponse ParseResponse(string body)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(body) as JsonObject
                   ?? throw new ChatModelException(ChatErrorKind.InvalidResponse, L10n.T("Anthropic'ten anlaşılamayan bir yanıt geldi."));
        }
        catch (JsonException ex)
        {
            throw new ChatModelException(ChatErrorKind.InvalidResponse, L10n.T("Anthropic'ten anlaşılamayan bir yanıt geldi (JSON çözümlenemedi)."), inner: ex);
        }

        var content = root["content"] as JsonArray ?? [];
        var blocks = new List<ChatContentBlock>();
        foreach (var item in content)
        {
            if (item is not JsonObject block) continue;
            switch (ProviderHttp.AsString(block["type"]))
            {
                case "text":
                    blocks.Add(new ChatTextBlock(ProviderHttp.AsString(block["text"]) ?? ""));
                    break;
                case "tool_use":
                    string id = ProviderHttp.AsString(block["id"]) ?? "";
                    string name = ProviderHttp.AsString(block["name"]) ?? "";
                    blocks.Add(new ChatToolCallBlock(id, name, ToArgumentsElement(block["input"])));
                    break;
                // thinking, redacted_thinking, fallback vb. yalnızca ham içerikte korunur.
            }
        }

        string? rawStop = ProviderHttp.AsString(root["stop_reason"]);
        var stop = rawStop switch
        {
            "end_turn" or "stop_sequence" => ChatStopReason.EndTurn,
            "tool_use" => ChatStopReason.ToolUse,
            "max_tokens" or "model_context_window_exceeded" => ChatStopReason.MaxTokens,
            "refusal" => ChatStopReason.Refusal,
            _ => ChatStopReason.Other,
        };
        if (stop == ChatStopReason.Other && blocks.OfType<ChatToolCallBlock>().Any())
            stop = ChatStopReason.ToolUse;

        ChatUsage? usage = null;
        if (root["usage"] is JsonObject u)
        {
            usage = new ChatUsage(
                ReadInt(u["input_tokens"]), ReadInt(u["output_tokens"]),
                ReadInt(u["cache_read_input_tokens"]), ReadInt(u["cache_creation_input_tokens"]));
        }

        string? category = null;
        if (root["stop_details"] is JsonObject details) category = ProviderHttp.AsString(details["category"]);

        var raw = (JsonArray)content.DeepClone();
        return new ChatResponse
        {
            StopReason = stop,
            RawStopReason = rawStop,
            Message = new ChatMessage(ChatRole.Assistant, blocks, raw, ProviderTag),
            Usage = usage,
            ServedModel = ProviderHttp.AsString(root["model"]),
            RefusalCategory = category,
        };
    }

    private static JsonElement ToArgumentsElement(JsonNode? input)
    {
        if (input is JsonObject obj) return JsonSerializer.SerializeToElement(obj);
        return JsonSerializer.SerializeToElement(new JsonObject());
    }

    private static int ReadInt(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out int number) ? number : 0;
}
