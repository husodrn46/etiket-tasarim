using AkelEtiket.Assistant.Chat;
using AkelEtiket.Assistant.Providers;
using AkelEtiket.Core.Automation;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Assistant;

/// <summary>Ayarlardan doğru sohbet modelini oluşturur.</summary>
public static class ChatModelFactory
{
    /// <summary>
    /// Ayarlara göre modeli oluşturur. <paramref name="apiKey"/> düz metin anahtardır (masaüstü uygulaması DPAPI ile
    /// çözer); yalnızca bellekte tutulur. <paramref name="http"/> paylaşılan bir istemci olmalı ve
    /// <see cref="HttpClient.Timeout"/> değeri <see cref="ChatModelOptions.RequestTimeout"/>'tan kısa olmamalıdır
    /// (önerilen: <see cref="Timeout.InfiniteTimeSpan"/>; bkz. <see cref="CreateHttpClient"/>).
    /// </summary>
    /// <exception cref="AssistantConfigurationException">Sağlayıcı seçilmemiş ya da gerekli alanlar eksik.</exception>
    public static IChatModel Create(AssistantProviderSettings settings, string? apiKey, HttpClient http, ChatModelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(http);
        return settings.Kind switch
        {
            AssistantProviderKind.Anthropic => new AnthropicChatModel(http, settings, apiKey ?? "", options),
            AssistantProviderKind.OpenAiCompatible => new OpenAiCompatibleChatModel(http, settings, apiKey, options),
            _ => throw new AssistantConfigurationException(
                L10n.T("Uygulama içi asistan için bir yapay zekâ sağlayıcısı seçilmedi. Ayarlar → Yapay Zekâ bölümünden Anthropic Claude ") +
                L10n.T("veya OpenAI uyumlu (yerel) bir model seçebilirsiniz. Dış ajanlar (Claude Desktop, Claude Code) MCP ile yine bağlanabilir.")),
        };
    }

    /// <summary>Asistan için uygun HttpClient (zaman aşımını model seçenekleri yönetir).</summary>
    public static HttpClient CreateHttpClient() => new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Sağlayıcı için önerilen model adları (ayarlar ekranında liste için).</summary>
    public static IReadOnlyList<string> SuggestedModels(AssistantProviderKind kind) => kind switch
    {
        AssistantProviderKind.Anthropic => AnthropicChatModel.SuggestedModels,
        _ => [],
    };

    /// <summary>Sağlayıcının varsayılan modeli (yoksa null).</summary>
    public static string? DefaultModel(AssistantProviderKind kind) =>
        kind == AssistantProviderKind.Anthropic ? AnthropicChatModel.DefaultModel : null;
}
