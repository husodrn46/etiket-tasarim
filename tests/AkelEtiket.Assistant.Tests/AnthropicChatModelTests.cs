using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using AkelEtiket.Assistant.Chat;
using AkelEtiket.Assistant.Providers;
using AkelEtiket.Core.Automation;

namespace AkelEtiket.Assistant.Tests;

public class AnthropicChatModelTests
{
    private static (AnthropicChatModel Model, FakeHttpHandler Handler) Create(
        AssistantProviderSettings? settings = null, ChatModelOptions? options = null)
    {
        var handler = new FakeHttpHandler();
        var model = new AnthropicChatModel(new HttpClient(handler), settings ?? TestData.Settings(AssistantProviderKind.Anthropic),
            TestData.AnthropicKey, options ?? TestData.Options());
        return (model, handler);
    }

    private static ChatRequest SimpleRequest(params ChatMessage[] messages) => new()
    {
        SystemPrompt = "Sistem istemi",
        Messages = messages.Length > 0 ? messages : [ChatMessage.UserText("Merhaba")],
        Tools = [TestData.Spec("add_text")],
    };

    [Fact]
    public async Task Request_HasRequiredHeadersAndBodyShape()
    {
        var (model, handler) = Create();
        handler.Json(TestData.AnthropicText("Tamam"));
        var user = new ChatMessage(ChatRole.User, [new ChatTextBlock("Logoyu büyüt"), new ChatImageBlock("image/png", TestData.Png)]);

        var response = await model.CompleteAsync(SimpleRequest(user), CancellationToken.None);

        Assert.Equal("Tamam", response.Text);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.Uri.ToString());
        Assert.Equal(TestData.AnthropicKey, request.Header("x-api-key"));
        Assert.Equal("2023-06-01", request.Header("anthropic-version"));
        Assert.StartsWith("application/json", request.Header("content-type"));
        // Yedek model varsayılan olarak kapalı: veri yalnızca seçilen modele gider.
        Assert.Null(request.Header("anthropic-beta"));

        var body = request.Json;
        Assert.Equal("claude-opus-5-5", (string?)body["model"]);
        Assert.Equal(16000, (int)body["max_tokens"]!);
        Assert.Equal("Sistem istemi", (string?)body["system"]);
        Assert.Null(body["fallbacks"]);
        Assert.Equal("ephemeral", (string?)body["cache_control"]!["type"]);
        Assert.Null(body["temperature"]);
        Assert.Null(body["thinking"]);
        Assert.Null(body["output_config"]);

        var tool = body["tools"]![0]!.AsObject();
        Assert.Equal("add_text", (string?)tool["name"]);
        Assert.Equal("Metin ekle. Etikete metin kutusu ekler.", (string?)tool["description"]);
        Assert.True(JsonNode.DeepEquals(TestData.Tool("add_text").InputSchema, tool["input_schema"]));

        var message = body["messages"]![0]!.AsObject();
        Assert.Equal("user", (string?)message["role"]);
        var content = message["content"]!.AsArray();
        Assert.Equal("text", (string?)content[0]!["type"]);
        Assert.Equal("Logoyu büyüt", (string?)content[0]!["text"]);
        Assert.Equal("image", (string?)content[1]!["type"]);
        Assert.Equal("base64", (string?)content[1]!["source"]!["type"]);
        Assert.Equal("image/png", (string?)content[1]!["source"]!["media_type"]);
        Assert.Equal(Convert.ToBase64String(TestData.Png), (string?)content[1]!["source"]!["data"]);
    }

    [Fact]
    public async Task Fallback_IsNotSent_ForHaikuOrCustomEndpoint()
    {
        var (haiku, haikuHandler) = Create(TestData.Settings(AssistantProviderKind.Anthropic, model: "claude-haiku-5-5"));
        haikuHandler.Json(TestData.AnthropicText("ok"));
        await haiku.CompleteAsync(SimpleRequest(), CancellationToken.None);
        Assert.Null(haikuHandler.Requests[0].Header("anthropic-beta"));
        Assert.Null(haikuHandler.Requests[0].Json["fallbacks"]);
        Assert.Equal("claude-haiku-5-5", (string?)haikuHandler.Requests[0].Json["model"]);

        var (proxy, proxyHandler) = Create(TestData.Settings(AssistantProviderKind.Anthropic, baseUrl: "https://vekil.example.com/"));
        proxyHandler.Json(TestData.AnthropicText("ok"));
        await proxy.CompleteAsync(SimpleRequest(), CancellationToken.None);
        Assert.Equal("https://vekil.example.com/v1/messages", proxyHandler.Requests[0].Uri.ToString());
        Assert.Null(proxyHandler.Requests[0].Header("anthropic-beta"));
        Assert.Null(proxyHandler.Requests[0].Json["fallbacks"]);
    }

    [Fact]
    public async Task EffortOption_IsSentInOutputConfig()
    {
        var options = TestData.Options();
        options.Effort = "High";
        var (model, handler) = Create(options: options);
        handler.Json(TestData.AnthropicText("ok"));
        await model.CompleteAsync(SimpleRequest(), CancellationToken.None);
        Assert.Equal("high", (string?)handler.Requests[0].Json["output_config"]!["effort"]);
    }

    [Fact]
    public void ParseResponse_TextOnly_ReadsUsageAndStop()
    {
        var (model, _) = Create();
        string body = """
        {"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5",
         "content":[{"type":"thinking","thinking":"","signature":"imza"},{"type":"text","text":"Bitti."}],
         "stop_reason":"end_turn",
         "usage":{"input_tokens":120,"output_tokens":30,"cache_read_input_tokens":80,"cache_creation_input_tokens":40}}
        """;
        var response = model.ParseResponse(body);
        Assert.Equal(ChatStopReason.EndTurn, response.StopReason);
        Assert.Equal("Bitti.", response.Text);
        Assert.Empty(response.ToolCalls);
        Assert.Equal(new ChatUsage(120, 30, 80, 40), response.Usage);
        Assert.Equal("claude-opus-5-5", response.ServedModel);
        Assert.Equal(AnthropicChatModel.ProviderTag, response.Message.ProviderTag);
        Assert.Equal(2, response.Message.ProviderContent!.Count);
    }

    [Fact]
    public void ParseResponse_MultipleToolCalls()
    {
        var (model, _) = Create();
        string body = """
        {"content":[
          {"type":"text","text":"İki adım yapacağım."},
          {"type":"tool_use","id":"toolu_1","name":"move_element","input":{"element_id":"a1","dx":5}},
          {"type":"tool_use","id":"toolu_2","name":"update_element","input":{"element_id":"b2","changes":{"fontSize":14}}}],
         "stop_reason":"tool_use","model":"claude-opus-5-5"}
        """;
        var response = model.ParseResponse(body);
        Assert.Equal(ChatStopReason.ToolUse, response.StopReason);
        Assert.Equal(2, response.ToolCalls.Count);
        Assert.Equal("toolu_1", response.ToolCalls[0].Id);
        Assert.Equal("move_element", response.ToolCalls[0].Name);
        Assert.Equal(5, response.ToolCalls[0].Arguments.GetProperty("dx").GetInt32());
        Assert.Equal(14, response.ToolCalls[1].Arguments.GetProperty("changes").GetProperty("fontSize").GetInt32());
    }

    [Fact]
    public void ParseResponse_Refusal_ReportsCategory()
    {
        var (model, _) = Create();
        var response = model.ParseResponse("""{"content":[],"stop_reason":"refusal","stop_details":{"type":"refusal","category":"cyber"}}""");
        Assert.Equal(ChatStopReason.Refusal, response.StopReason);
        Assert.Equal("cyber", response.RefusalCategory);
    }

    [Fact]
    public void ParseResponse_InvalidJson_ThrowsInvalidResponse()
    {
        var (model, _) = Create();
        var ex = Assert.Throws<ChatModelException>(() => model.ParseResponse("<html>bozuk</html>"));
        Assert.Equal(ChatErrorKind.InvalidResponse, ex.Kind);
    }

    [Fact]
    public async Task AssistantRawContent_IsReplayedUnchanged_WithToolResults()
    {
        var (model, handler) = Create();
        string first = """
        {"content":[
          {"type":"thinking","thinking":"","signature":"EqQBCkYIBhgCKkA+imza/2=="},
          {"type":"tool_use","id":"toolu_9","name":"render_preview","input":{"design_id":"taslak:1"}}],
         "stop_reason":"tool_use","model":"claude-opus-5-5"}
        """;
        var parsed = model.ParseResponse(first);
        var toolResult = new ChatMessage(ChatRole.User,
        [
            new ChatToolResultBlock("toolu_9", "Önizleme hazır", [new ChatImageBlock("image/png", TestData.Png)], false),
            new ChatToolResultBlock("toolu_x", "Bulunamadı", [], true),
        ]);
        handler.Json(TestData.AnthropicText("Tamam"));

        await model.CompleteAsync(SimpleRequest(ChatMessage.UserText("Önizle"), parsed.Message, toolResult), CancellationToken.None);

        var messages = handler.Requests[0].Json["messages"]!.AsArray();
        Assert.Equal(3, messages.Count);
        Assert.Equal("assistant", (string?)messages[1]!["role"]);
        var expected = JsonNode.Parse(first)!["content"];
        Assert.True(JsonNode.DeepEquals(expected, messages[1]!["content"]), "Asistan içeriği (thinking imzası dahil) değiştirilmeden gönderilmeli.");

        var results = messages[2]!["content"]!.AsArray();
        Assert.Equal("tool_result", (string?)results[0]!["type"]);
        Assert.Equal("toolu_9", (string?)results[0]!["tool_use_id"]);
        Assert.Null(results[0]!["is_error"]);
        Assert.Equal("Önizleme hazır", (string?)results[0]!["content"]![0]!["text"]);
        Assert.Equal("image", (string?)results[0]!["content"]![1]!["type"]);
        Assert.True((bool)results[1]!["is_error"]!);
    }

    [Fact]
    public async Task AssistantMessageWithoutRaw_IsBuiltFromBlocks()
    {
        var (model, handler) = Create();
        handler.Json(TestData.AnthropicText("ok"));
        var assistant = new ChatMessage(ChatRole.Assistant,
            [new ChatTextBlock("Taşıyorum"), new ChatToolCallBlock("t1", "move_element", TestData.Args("""{"dx":2}"""))]);
        var results = new ChatMessage(ChatRole.User, [new ChatToolResultBlock("t1", "ok", [], false)]);
        await model.CompleteAsync(SimpleRequest(ChatMessage.UserText("a"), assistant, results), CancellationToken.None);
        var content = handler.Requests[0].Json["messages"]![1]!["content"]!.AsArray();
        Assert.Equal("text", (string?)content[0]!["type"]);
        Assert.Equal("tool_use", (string?)content[1]!["type"]);
        Assert.Equal(2, (int)content[1]!["input"]!["dx"]!);
    }

    [Fact]
    public async Task Images_AreReplacedWithNote_WhenImageSendingDisabled()
    {
        var (model, handler) = Create(TestData.Settings(AssistantProviderKind.Anthropic, sendImages: false));
        handler.Json(TestData.AnthropicText("ok"));
        var user = new ChatMessage(ChatRole.User, [new ChatTextBlock("Bak"), new ChatImageBlock("image/png", TestData.Png)]);
        await model.CompleteAsync(SimpleRequest(user), CancellationToken.None);
        string body = handler.Requests[0].Body;
        Assert.DoesNotContain(Convert.ToBase64String(TestData.Png), body);
        Assert.Contains("Görsel gönderilmedi", handler.Requests[0].Json["messages"]![0]!["content"]![1]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task NoConsent_ThrowsWithoutSendingAnything()
    {
        var (model, handler) = Create(TestData.Settings(AssistantProviderKind.Anthropic, consent: false));
        var ex = await Assert.ThrowsAsync<AssistantConsentException>(() => model.CompleteAsync(SimpleRequest(), CancellationToken.None));
        Assert.Contains("izin", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ConsentForDifferentProvider_IsNotAccepted()
    {
        var settings = TestData.Settings(AssistantProviderKind.Anthropic);
        settings.BaseUrl = "https://baska.example.com"; // izin eski adres için verilmişti
        var handler = new FakeHttpHandler();
        var model = new AnthropicChatModel(new HttpClient(handler), settings, TestData.AnthropicKey, TestData.Options());
        await Assert.ThrowsAsync<AssistantConsentException>(() => model.CompleteAsync(SimpleRequest(), CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Unauthorized_MapsToTurkishMessage_AndNeverLeaksKey()
    {
        var (model, handler) = Create();
        handler.Json($$$"""{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key {{{TestData.AnthropicKey}}}"}}""",
            HttpStatusCode.Unauthorized);
        var ex = await Assert.ThrowsAsync<ChatModelException>(() => model.CompleteAsync(SimpleRequest(), CancellationToken.None));
        Assert.Equal(ChatErrorKind.Authentication, ex.Kind);
        Assert.Equal(401, ex.StatusCode);
        Assert.Contains("API anahtarı", ex.Message);
        Assert.DoesNotContain(TestData.AnthropicKey, ex.Message);
        Assert.DoesNotContain(TestData.AnthropicKey, ex.ToString());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RateLimited_HonoursRetryAfter_ThenSucceeds()
    {
        var delays = new List<TimeSpan>();
        var (model, handler) = Create(options: TestData.Options(delays));
        handler.Json("""{"type":"error","error":{"type":"rate_limit_error","message":"slow down"}}""", (HttpStatusCode)429,
            r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(3)));
        handler.Json(TestData.AnthropicText("Sonunda"));

        var response = await model.CompleteAsync(SimpleRequest(), CancellationToken.None);

        Assert.Equal("Sonunda", response.Text);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(new[] { TimeSpan.FromSeconds(3) }, delays);
    }

    [Fact]
    public async Task Overloaded_IsRetriedTwice_ThenReported()
    {
        var delays = new List<TimeSpan>();
        var (model, handler) = Create(options: TestData.Options(delays));
        for (int i = 0; i < 3; i++)
            handler.Json("""{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""", (HttpStatusCode)529);

        var ex = await Assert.ThrowsAsync<ChatModelException>(() => model.CompleteAsync(SimpleRequest(), CancellationToken.None));

        Assert.Equal(ChatErrorKind.Overloaded, ex.Kind);
        Assert.True(ex.IsTransient);
        Assert.Contains("529", ex.Message);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task Overloaded_ThenSuccess()
    {
        var (model, handler) = Create();
        handler.Json("""{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""", (HttpStatusCode)529);
        handler.Json(TestData.AnthropicText("ok"));
        var response = await model.CompleteAsync(SimpleRequest(), CancellationToken.None);
        Assert.Equal("ok", response.Text);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(400, ChatErrorKind.BadRequest)]
    [InlineData(402, ChatErrorKind.Billing)]
    [InlineData(403, ChatErrorKind.Permission)]
    [InlineData(404, ChatErrorKind.NotFound)]
    [InlineData(413, ChatErrorKind.TooLarge)]
    public async Task NonRetryableErrors_AreMappedAndNotRetried(int status, ChatErrorKind kind)
    {
        var (model, handler) = Create();
        handler.Json("""{"type":"error","error":{"type":"x","message":"sağlayıcı ayrıntısı"}}""", (HttpStatusCode)status);
        var ex = await Assert.ThrowsAsync<ChatModelException>(() => model.CompleteAsync(SimpleRequest(), CancellationToken.None));
        Assert.Equal(kind, ex.Kind);
        Assert.Equal(status, ex.StatusCode);
        Assert.Contains("sağlayıcı ayrıntısı", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ServerError500_IsRetried()
    {
        var (model, handler) = Create();
        handler.Json("""{"type":"error","error":{"type":"api_error","message":"Internal"}}""", HttpStatusCode.InternalServerError);
        handler.Json(TestData.AnthropicText("ok"));
        var response = await model.CompleteAsync(SimpleRequest(), CancellationToken.None);
        Assert.Equal("ok", response.Text);
    }

    [Fact]
    public async Task Fallback_IsSent_OnlyWhenExplicitlyEnabled()
    {
        var options = TestData.Options();
        options.UseServerSideFallback = true;
        var (model, handler) = Create(options: options);
        handler.Json(TestData.AnthropicText("ok"));

        await model.CompleteAsync(SimpleRequest(), CancellationToken.None);

        Assert.Equal(AnthropicChatModel.FallbackBeta, handler.Requests[0].Header("anthropic-beta"));
        Assert.Equal("default", (string?)handler.Requests[0].Json["fallbacks"]);
    }

    [Fact]
    public async Task RejectedFallbackBeta_IsDisabledAndRequestRetried()
    {
        var options = TestData.Options();
        options.UseServerSideFallback = true;
        var (model, handler) = Create(options: options);
        handler.Json("""{"type":"error","error":{"type":"invalid_request_error","message":"Unexpected value(s) `server-side-fallback-2026-07-01` for the `anthropic-beta` header."}}""",
            HttpStatusCode.BadRequest);
        handler.Json(TestData.AnthropicText("ok"));

        var response = await model.CompleteAsync(SimpleRequest(), CancellationToken.None);

        Assert.Equal("ok", response.Text);
        Assert.Equal(2, handler.Requests.Count);
        Assert.NotNull(handler.Requests[0].Json["fallbacks"]);
        Assert.Null(handler.Requests[1].Json["fallbacks"]);
        Assert.Null(handler.Requests[1].Header("anthropic-beta"));
        Assert.False(model.ServerSideFallbackActive);
    }

    [Fact]
    public async Task RejectedCacheControl_IsDisabledAndRequestRetried()
    {
        var (model, handler) = Create(TestData.Settings(AssistantProviderKind.Anthropic, model: "claude-haiku-5-5"));
        handler.Json("""{"type":"error","error":{"type":"invalid_request_error","message":"cache_control: Extra inputs are not permitted"}}""",
            HttpStatusCode.BadRequest);
        handler.Json(TestData.AnthropicText("ok"));
        await model.CompleteAsync(SimpleRequest(), CancellationToken.None);
        Assert.Null(handler.Requests[1].Json["cache_control"]);
        Assert.False(model.PromptCachingActive);
    }

    [Fact]
    public async Task NetworkError_IsRetriedThenReportedInTurkish()
    {
        var (model, handler) = Create();
        for (int i = 0; i < 3; i++)
            handler.Throw(new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known."));
        var ex = await Assert.ThrowsAsync<ChatModelException>(() => model.CompleteAsync(SimpleRequest(), CancellationToken.None));
        Assert.Equal(ChatErrorKind.Network, ex.Kind);
        Assert.Contains("bağlanılamadı", ex.Message);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Timeout_IsReportedAsTimeout()
    {
        var options = TestData.Options();
        options.RequestTimeout = TimeSpan.FromMilliseconds(100);
        options.MaxRetries = 0;
        var (model, handler) = Create(options: options);
        handler.Hang();
        var ex = await Assert.ThrowsAsync<ChatModelException>(() => model.CompleteAsync(SimpleRequest(), CancellationToken.None));
        Assert.Equal(ChatErrorKind.Timeout, ex.Kind);
        Assert.Contains("zaman aşımı", ex.Message);
    }

    [Fact]
    public async Task UserCancellation_PropagatesAsOperationCanceled()
    {
        var (model, handler) = Create();
        handler.Hang();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => model.CompleteAsync(SimpleRequest(), cts.Token));
    }

    [Fact]
    public void MissingKey_IsConfigurationError()
    {
        var ex = Assert.Throws<AssistantConfigurationException>(() =>
            new AnthropicChatModel(new HttpClient(new FakeHttpHandler()), TestData.Settings(AssistantProviderKind.Anthropic), " "));
        Assert.Contains("anahtar", ex.Message);
    }
}
