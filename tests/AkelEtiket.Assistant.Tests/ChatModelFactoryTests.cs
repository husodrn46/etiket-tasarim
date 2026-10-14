using AkelEtiket.Assistant.Chat;
using AkelEtiket.Assistant.Providers;
using AkelEtiket.Core.Automation;

namespace AkelEtiket.Assistant.Tests;

public class ChatModelFactoryTests
{
    private static readonly HttpClient Http = new(new FakeHttpHandler());

    [Fact]
    public void NoProvider_IsConfigurationError()
    {
        var ex = Assert.Throws<AssistantConfigurationException>(() =>
            ChatModelFactory.Create(new AssistantProviderSettings(), "anahtar", Http));
        Assert.Contains("sağlayıcı", ex.Message);
    }

    [Fact]
    public void Anthropic_UsesDefaultModel_AndRequiresKey()
    {
        var settings = TestData.Settings(AssistantProviderKind.Anthropic);
        var model = ChatModelFactory.Create(settings, TestData.AnthropicKey, Http);
        Assert.IsType<AnthropicChatModel>(model);
        Assert.Equal("claude-opus-5-5", model.Model);
        Assert.Throws<AssistantConfigurationException>(() => ChatModelFactory.Create(settings, null, Http));
    }

    [Fact]
    public void Anthropic_UsesConfiguredModel()
    {
        var settings = TestData.Settings(AssistantProviderKind.Anthropic, model: "claude-sonnet-5-5");
        Assert.Equal("claude-sonnet-5-5", ChatModelFactory.Create(settings, TestData.AnthropicKey, Http).Model);
    }

    [Fact]
    public void OpenAiCompatible_IsCreated_KeyOptional()
    {
        var settings = TestData.Settings(AssistantProviderKind.OpenAiCompatible, baseUrl: "http://127.0.0.1:1234/v1", model: "yerel-model");
        var model = ChatModelFactory.Create(settings, null, Http);
        Assert.IsType<OpenAiCompatibleChatModel>(model);
        Assert.Equal("yerel-model", model.Model);
    }

    [Fact]
    public void SuggestedModels_AndDefaults()
    {
        Assert.Equal(new[] { "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5" }, ChatModelFactory.SuggestedModels(AssistantProviderKind.Anthropic));
        Assert.Empty(ChatModelFactory.SuggestedModels(AssistantProviderKind.OpenAiCompatible));
        Assert.Equal("claude-opus-5-5", ChatModelFactory.DefaultModel(AssistantProviderKind.Anthropic));
        Assert.Null(ChatModelFactory.DefaultModel(AssistantProviderKind.OpenAiCompatible));
    }

    [Fact]
    public void HttpClient_HasNoOwnTimeout()
    {
        using var http = ChatModelFactory.CreateHttpClient();
        Assert.Equal(Timeout.InfiniteTimeSpan, http.Timeout);
    }
}
