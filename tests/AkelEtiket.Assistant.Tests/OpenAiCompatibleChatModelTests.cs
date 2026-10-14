using System.Net;
using System.Text.Json.Nodes;
using AkelEtiket.Assistant.Chat;
using AkelEtiket.Assistant.Providers;
using AkelEtiket.Core.Automation;

namespace AkelEtiket.Assistant.Tests;

public class OpenAiCompatibleChatModelTests
{
    private const string BaseUrl = "http://127.0.0.1:11434/v1";

    private static (OpenAiCompatibleChatModel Model, FakeHttpHandler Handler) Create(
        string? key = TestData.OpenAiKey, bool sendImages = true, bool consent = true)
    {
        var handler = new FakeHttpHandler();
        var settings = TestData.Settings(AssistantProviderKind.OpenAiCompatible, consent, sendImages, BaseUrl + "/", "qwen2.5-vl");
        var model = new OpenAiCompatibleChatModel(new HttpClient(handler), settings, key, TestData.Options());
        return (model, handler);
    }

    private static string Completion(JsonObject message, string finish = "stop") => new JsonObject
    {
        ["id"] = "chatcmpl-1",
        ["model"] = "qwen2.5-vl",
        ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["message"] = message, ["finish_reason"] = finish }),
        ["usage"] = new JsonObject { ["prompt_tokens"] = 50, ["completion_tokens"] = 7 },
    }.ToJsonString();

    private static ChatRequest Request(params ChatMessage[] messages) => new()
    {
        SystemPrompt = "Sistem",
        Messages = messages.Length > 0 ? messages : [ChatMessage.UserText("Merhaba")],
        Tools = [TestData.Spec("add_text")],
    };

    [Fact]
    public async Task Request_HasEndpointAuthSystemToolsAndTextContent()
    {
        var (model, handler) = Create();
        handler.Json(Completion(new JsonObject { ["role"] = "assistant", ["content"] = "Merhaba!" }));

        var response = await model.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal("Merhaba!", response.Text);
        Assert.Equal(ChatStopReason.EndTurn, response.StopReason);
        Assert.Equal(new ChatUsage(50, 7), response.Usage);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(BaseUrl + "/chat/completions", request.Uri.ToString());
        Assert.Equal("Bearer " + TestData.OpenAiKey, request.Header("authorization"));

        var body = request.Json;
        Assert.Equal("qwen2.5-vl", (string?)body["model"]);
        Assert.False((bool)body["stream"]!);
        Assert.Equal(16000, (int)body["max_tokens"]!);
        var messages = body["messages"]!.AsArray();
        Assert.Equal("system", (string?)messages[0]!["role"]);
        Assert.Equal("Sistem", (string?)messages[0]!["content"]);
        Assert.Equal("user", (string?)messages[1]!["role"]);
        Assert.Equal("Merhaba", (string?)messages[1]!["content"]);

        var tool = body["tools"]![0]!.AsObject();
        Assert.Equal("function", (string?)tool["type"]);
        Assert.Equal("add_text", (string?)tool["function"]!["name"]);
        Assert.Equal("Metin ekle. Etikete metin kutusu ekler.", (string?)tool["function"]!["description"]);
        Assert.True(JsonNode.DeepEquals(TestData.Tool("add_text").InputSchema, tool["function"]!["parameters"]));
    }

    [Fact]
    public async Task WithoutOptions_UsesConservativeOutputLimit()
    {
        var handler = new FakeHttpHandler();
        handler.Json(Completion(new JsonObject { ["content"] = "ok" }));
        var settings = TestData.Settings(AssistantProviderKind.OpenAiCompatible, baseUrl: BaseUrl, model: "m");
        var model = new OpenAiCompatibleChatModel(new HttpClient(handler), settings, null);
        await model.CompleteAsync(Request(), CancellationToken.None);
        Assert.Equal(OpenAiCompatibleChatModel.DefaultMaxOutputTokens, (int)handler.Requests[0].Json["max_tokens"]!);
    }

    [Fact]
    public async Task WithoutKey_NoAuthorizationHeader()
    {
        var (model, handler) = Create(key: null);
        handler.Json(Completion(new JsonObject { ["content"] = "ok" }));
        await model.CompleteAsync(Request(), CancellationToken.None);
        Assert.Null(handler.Requests[0].Header("authorization"));
    }

    [Fact]
    public async Task UserImage_IsSentAsDataUri()
    {
        var (model, handler) = Create();
        handler.Json(Completion(new JsonObject { ["content"] = "ok" }));
        var user = new ChatMessage(ChatRole.User, [new ChatTextBlock("Bak"), new ChatImageBlock("image/png", TestData.Png)]);
        await model.CompleteAsync(Request(user), CancellationToken.None);
        var content = handler.Requests[0].Json["messages"]![1]!["content"]!.AsArray();
        Assert.Equal("text", (string?)content[0]!["type"]);
        Assert.Equal("image_url", (string?)content[1]!["type"]);
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(TestData.Png), (string?)content[1]!["image_url"]!["url"]);
    }

    [Fact]
    public async Task ToolCallsAndResults_RoundTrip_WithFollowUpImages()
    {
        var (model, handler) = Create();
        handler.Json(Completion(new JsonObject { ["content"] = "Tamam" }));
        var assistant = new ChatMessage(ChatRole.Assistant,
        [
            new ChatTextBlock("İki araç çağırıyorum."),
            new ChatToolCallBlock("call_a", "render_preview", TestData.Args("""{"design_id":"taslak:1"}""")),
            new ChatToolCallBlock("call_b", "delete_element", TestData.Args("""{"element_id":"x"}""")),
        ]);
        var results = new ChatMessage(ChatRole.User,
        [
            new ChatToolResultBlock("call_a", "Önizleme", [new ChatImageBlock("image/png", TestData.Png)], false),
            new ChatToolResultBlock("call_b", "Kilitli öğe silinemez", [], true),
        ]);

        await model.CompleteAsync(Request(ChatMessage.UserText("Başla"), assistant, results), CancellationToken.None);

        var messages = handler.Requests[0].Json["messages"]!.AsArray();
        // system, user, assistant, tool, tool, user (görseller)
        Assert.Equal(6, messages.Count);
        var wireAssistant = messages[2]!.AsObject();
        Assert.Equal("assistant", (string?)wireAssistant["role"]);
        Assert.Equal("İki araç çağırıyorum.", (string?)wireAssistant["content"]);
        var calls = wireAssistant["tool_calls"]!.AsArray();
        Assert.Equal("call_a", (string?)calls[0]!["id"]);
        Assert.Equal("function", (string?)calls[0]!["type"]);
        Assert.Equal("render_preview", (string?)calls[0]!["function"]!["name"]);
        string arguments = (string)calls[0]!["function"]!["arguments"]!;
        Assert.Equal("taslak:1", (string?)JsonNode.Parse(arguments)!["design_id"]);

        Assert.Equal("tool", (string?)messages[3]!["role"]);
        Assert.Equal("call_a", (string?)messages[3]!["tool_call_id"]);
        Assert.Equal("Önizleme", (string?)messages[3]!["content"]);
        Assert.Equal("call_b", (string?)messages[4]!["tool_call_id"]);
        Assert.StartsWith("HATA:", (string?)messages[4]!["content"]);

        var followUp = messages[5]!.AsObject();
        Assert.Equal("user", (string?)followUp["role"]);
        var parts = followUp["content"]!.AsArray();
        Assert.Equal("Araç sonuçlarındaki görseller:", (string?)parts[0]!["text"]);
        Assert.Equal("image_url", (string?)parts[1]!["type"]);
    }

    [Fact]
    public async Task ToolImages_AreNotSent_WhenImageSendingDisabled()
    {
        var (model, handler) = Create(sendImages: false);
        handler.Json(Completion(new JsonObject { ["content"] = "Tamam" }));
        var assistant = new ChatMessage(ChatRole.Assistant, [new ChatToolCallBlock("c1", "render_preview", TestData.Args("{}"))]);
        var results = new ChatMessage(ChatRole.User, [new ChatToolResultBlock("c1", "Önizleme", [new ChatImageBlock("image/png", TestData.Png)], false)]);

        await model.CompleteAsync(Request(ChatMessage.UserText("a"), assistant, results), CancellationToken.None);

        var messages = handler.Requests[0].Json["messages"]!.AsArray();
        Assert.Equal(4, messages.Count); // system, user, assistant, tool — görsel mesajı yok
        Assert.Contains("Görsel gönderilmedi", (string?)messages[3]!["content"]);
        Assert.DoesNotContain(Convert.ToBase64String(TestData.Png), handler.Requests[0].Body);
        Assert.Null(messages[2]!["content"]); // metinsiz asistan mesajı: content null
    }

    [Fact]
    public void ParseResponse_ToolCalls_ArgumentsAsStringObjectAndMissingIds()
    {
        var (model, _) = Create();
        string body = """
        {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[
           {"id":"call_1","type":"function","function":{"name":"move_element","arguments":"{\"element_id\":\"a\",\"dx\":3}"}},
           {"type":"function","function":{"name":"update_element","arguments":{"element_id":"b","changes":{"bold":true}}}},
           {"function":{"name":"inspect_layout","arguments":"bozuk json"}}
         ]},"finish_reason":"tool_calls"}]}
        """;
        var response = model.ParseResponse(body);
        Assert.Equal(ChatStopReason.ToolUse, response.StopReason);
        Assert.Equal(3, response.ToolCalls.Count);
        Assert.Equal("call_1", response.ToolCalls[0].Id);
        Assert.Equal(3, response.ToolCalls[0].Arguments.GetProperty("dx").GetInt32());
        Assert.True(response.ToolCalls[1].Arguments.GetProperty("changes").GetProperty("bold").GetBoolean());
        Assert.False(string.IsNullOrEmpty(response.ToolCalls[1].Id));
        Assert.NotEqual(response.ToolCalls[1].Id, response.ToolCalls[2].Id);
        Assert.Equal("{}", response.ToolCalls[2].Arguments.GetRawText());
    }

    [Theory]
    [InlineData("stop", false, ChatStopReason.EndTurn)]
    [InlineData("stop", true, ChatStopReason.ToolUse)]
    [InlineData("length", false, ChatStopReason.MaxTokens)]
    [InlineData("content_filter", false, ChatStopReason.Refusal)]
    [InlineData("tool_calls", true, ChatStopReason.ToolUse)]
    public void ParseResponse_FinishReasonMapping(string finish, bool withCall, ChatStopReason expected)
    {
        var (model, _) = Create();
        var message = new JsonObject { ["content"] = "metin" };
        if (withCall)
            message["tool_calls"] = new JsonArray(new JsonObject { ["id"] = "c", ["function"] = new JsonObject { ["name"] = "get_design", ["arguments"] = "{}" } });
        Assert.Equal(expected, model.ParseResponse(Completion(message, finish)).StopReason);
    }

    [Fact]
    public void ParseResponse_ContentParts_AreJoined()
    {
        var (model, _) = Create();
        var message = new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "Bir " }, new JsonObject { ["type"] = "text", ["text"] = "iki" }),
        };
        Assert.Equal("Bir iki", model.ParseResponse(Completion(message)).Text);
    }

    [Fact]
    public void ParseResponse_WithoutChoices_IsInvalidResponse()
    {
        var (model, _) = Create();
        var ex = Assert.Throws<ChatModelException>(() => model.ParseResponse("""{"object":"error"}"""));
        Assert.Equal(ChatErrorKind.InvalidResponse, ex.Kind);
    }

    [Fact]
    public async Task ConnectionRefused_IsRetriedAndExplained()
    {
        var (model, handler) = Create();
        for (int i = 0; i < 3; i++)
            handler.Throw(new HttpRequestException(HttpRequestError.ConnectionError, "No connection could be made (127.0.0.1:11434)"));
        var ex = await Assert.ThrowsAsync<ChatModelException>(() => model.CompleteAsync(Request(), CancellationToken.None));
        Assert.Equal(ChatErrorKind.Network, ex.Kind);
        Assert.Contains(BaseUrl, ex.Message);
        Assert.Contains("Ollama", ex.Message);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task OllamaStyleError_IsMapped()
    {
        var (model, handler) = Create();
        handler.Json("""{"error":"model \"qwen2.5-vl\" not found, try pulling it first"}""", HttpStatusCode.NotFound);
        var ex = await Assert.ThrowsAsync<ChatModelException>(() => model.CompleteAsync(Request(), CancellationToken.None));
        Assert.Equal(ChatErrorKind.NotFound, ex.Kind);
        Assert.Contains("not found", ex.Message);
        Assert.Contains("qwen2.5-vl", ex.Message);
    }

    [Fact]
    public async Task ErrorBodyContainingKey_IsScrubbed()
    {
        var (model, handler) = Create();
        handler.Json($$$"""{"error":{"message":"bad key {{{TestData.OpenAiKey}}}","type":"invalid_request_error"}}""", HttpStatusCode.Unauthorized);
        var ex = await Assert.ThrowsAsync<ChatModelException>(() => model.CompleteAsync(Request(), CancellationToken.None));
        Assert.Equal(ChatErrorKind.Authentication, ex.Kind);
        Assert.DoesNotContain(TestData.OpenAiKey, ex.Message);
        Assert.Contains("***", ex.Message);
    }

    [Fact]
    public async Task NoConsent_ThrowsWithoutRequest()
    {
        var (model, handler) = Create(consent: false);
        await Assert.ThrowsAsync<AssistantConsentException>(() => model.CompleteAsync(Request(), CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("", "qwen")]
    [InlineData("http://127.0.0.1:11434/v1", "")]
    [InlineData("ftp://sunucu/v1", "qwen")]
    public void MissingOrInvalidConfiguration_Throws(string baseUrl, string modelName)
    {
        var settings = TestData.Settings(AssistantProviderKind.OpenAiCompatible, baseUrl: baseUrl, model: modelName);
        Assert.Throws<AssistantConfigurationException>(() => new OpenAiCompatibleChatModel(new HttpClient(new FakeHttpHandler()), settings, null));
    }
}
