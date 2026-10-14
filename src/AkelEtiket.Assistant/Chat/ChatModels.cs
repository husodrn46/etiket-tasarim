using System.Text.Json;
using System.Text.Json.Nodes;
using AkelEtiket.Design.Tools;

namespace AkelEtiket.Assistant.Chat;

/// <summary>Sohbet mesajının rolü. Sistem istemi ayrı tutulur (<see cref="ChatRequest.SystemPrompt"/>).</summary>
public enum ChatRole
{
    User,
    Assistant,
}

/// <summary>Mesaj içerik bloğu (sağlayıcıdan bağımsız).</summary>
public abstract record ChatContentBlock;

/// <summary>Düz metin.</summary>
public sealed record ChatTextBlock(string Text) : ChatContentBlock;

/// <summary>Görüntü (ör. baskı çözünürlüğünde etiket önizlemesi).</summary>
public sealed record ChatImageBlock(string MimeType, byte[] Data) : ChatContentBlock;

/// <summary>Modelin istediği araç çağrısı. <see cref="Arguments"/> her zaman bir JSON nesnesidir.</summary>
public sealed record ChatToolCallBlock(string Id, string Name, JsonElement Arguments) : ChatContentBlock;

/// <summary>Bir araç çağrısının sonucu (kullanıcı mesajında modele geri gönderilir).</summary>
public sealed record ChatToolResultBlock(string ToolCallId, string Text, IReadOnlyList<ChatImageBlock> Images, bool IsError)
    : ChatContentBlock;

/// <summary>
/// Sohbet mesajı. Asistan mesajları için sağlayıcının döndürdüğü ham içerik (<see cref="ProviderContent"/>) de
/// saklanır: Anthropic, düşünme (thinking) bloklarının imzalı ve <b>değiştirilmeden</b> geri gönderilmesini ister;
/// geçmiş yalnızca sona ekleme yapılarak büyütülür.
/// </summary>
public sealed class ChatMessage
{
    public ChatMessage(ChatRole role, IReadOnlyList<ChatContentBlock> blocks, JsonArray? providerContent = null, string? providerTag = null)
    {
        Role = role;
        Blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        ProviderContent = providerContent;
        ProviderTag = providerTag;
    }

    public ChatRole Role { get; }

    /// <summary>Normalleştirilmiş içerik (metin, görüntü, araç çağrısı, araç sonucu).</summary>
    public IReadOnlyList<ChatContentBlock> Blocks { get; }

    /// <summary>Sağlayıcının ham içerik dizisi (yalnızca aynı sağlayıcıya geri gönderilir; değiştirilmez).</summary>
    public JsonArray? ProviderContent { get; }

    /// <summary>Ham içeriğin hangi sağlayıcıya ait olduğu (ör. "anthropic").</summary>
    public string? ProviderTag { get; }

    /// <summary>Mesajdaki metinlerin birleşimi.</summary>
    public string Text => string.Join("\n", Blocks.OfType<ChatTextBlock>().Select(b => b.Text).Where(t => t.Length > 0));

    /// <summary>Mesajdaki araç çağrıları.</summary>
    public IReadOnlyList<ChatToolCallBlock> ToolCalls => Blocks.OfType<ChatToolCallBlock>().ToList();

    public static ChatMessage UserText(string text) => new(ChatRole.User, [new ChatTextBlock(text)]);
}

/// <summary>Modele sunulan araç (ToolDefinition'dan türetilir).</summary>
public sealed record ChatToolSpec(string Name, string Description, JsonObject InputSchema)
{
    public static ChatToolSpec From(ToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        string description = string.IsNullOrWhiteSpace(definition.Title)
            ? definition.Description
            : $"{definition.Title}. {definition.Description}";
        return new ChatToolSpec(definition.Name, description, definition.InputSchema);
    }
}

/// <summary>Model isteği.</summary>
public sealed class ChatRequest
{
    public required string SystemPrompt { get; init; }

    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    public IReadOnlyList<ChatToolSpec> Tools { get; init; } = [];

    /// <summary>En fazla üretilecek çıktı belirteci; null ise modelin seçeneklerindeki değer kullanılır.</summary>
    public int? MaxOutputTokens { get; init; }
}

/// <summary>Modelin yanıtı neden bitirdiği.</summary>
public enum ChatStopReason
{
    /// <summary>Yanıt tamamlandı.</summary>
    EndTurn,

    /// <summary>Model araç çağırmak istiyor.</summary>
    ToolUse,

    /// <summary>Çıktı belirteci sınırına ulaşıldı.</summary>
    MaxTokens,

    /// <summary>Sağlayıcının güvenlik denetimi isteği reddetti.</summary>
    Refusal,

    /// <summary>Diğer/bilinmeyen.</summary>
    Other,
}

/// <summary>Belirteç kullanımı (sağlayıcı bildirdiyse).</summary>
public sealed record ChatUsage(int InputTokens, int OutputTokens, int CacheReadTokens = 0, int CacheWriteTokens = 0)
{
    public static ChatUsage Zero { get; } = new(0, 0);

    public ChatUsage Add(ChatUsage? other) => other is null
        ? this
        : new ChatUsage(InputTokens + other.InputTokens, OutputTokens + other.OutputTokens,
            CacheReadTokens + other.CacheReadTokens, CacheWriteTokens + other.CacheWriteTokens);
}

/// <summary>Model yanıtı.</summary>
public sealed class ChatResponse
{
    public required ChatStopReason StopReason { get; init; }

    /// <summary>Sağlayıcının ham bitiş nedeni (ör. "end_turn", "tool_calls").</summary>
    public string? RawStopReason { get; init; }

    /// <summary>Geçmişe olduğu gibi eklenecek asistan mesajı.</summary>
    public required ChatMessage Message { get; init; }

    public ChatUsage? Usage { get; init; }

    /// <summary>Yanıtı gerçekte üreten model (sunucu tarafı yedek modele geçtiyse farklı olabilir).</summary>
    public string? ServedModel { get; init; }

    /// <summary>Ret durumunda sağlayıcının bildirdiği kategori (bilgi amaçlı).</summary>
    public string? RefusalCategory { get; init; }

    public string Text => Message.Text;

    public IReadOnlyList<ChatToolCallBlock> ToolCalls => Message.ToolCalls;
}

/// <summary>Sağlayıcıdan bağımsız sohbet modeli.</summary>
public interface IChatModel
{
    /// <summary>Kullanıcıya gösterilecek sağlayıcı adı (ör. "Anthropic Claude").</summary>
    string ProviderName { get; }

    /// <summary>Kullanılan model adı.</summary>
    string Model { get; }

    /// <summary>
    /// Bir istek gönderir. İzin yoksa <see cref="AssistantConsentException"/>, sağlayıcı hatasında
    /// <see cref="ChatModelException"/> fırlatır; iptalde <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken);
}
