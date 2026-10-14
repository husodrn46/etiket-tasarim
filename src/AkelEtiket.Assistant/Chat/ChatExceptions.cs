namespace AkelEtiket.Assistant.Chat;

/// <summary>Sağlayıcı hatasının türü.</summary>
public enum ChatErrorKind
{
    /// <summary>İstek geçersiz (400).</summary>
    BadRequest,

    /// <summary>API anahtarı geçersiz veya eksik (401).</summary>
    Authentication,

    /// <summary>Ödeme/bakiye sorunu (402).</summary>
    Billing,

    /// <summary>Yetki yok (403).</summary>
    Permission,

    /// <summary>Model veya uç nokta bulunamadı (404).</summary>
    NotFound,

    /// <summary>İstek çok büyük (413).</summary>
    TooLarge,

    /// <summary>İstek sınırı aşıldı (429).</summary>
    RateLimited,

    /// <summary>Sağlayıcı aşırı yoğun (529 veya 503).</summary>
    Overloaded,

    /// <summary>Sağlayıcıda sunucu hatası (5xx).</summary>
    Server,

    /// <summary>Bağlantı kurulamadı (ağ, DNS, yerel sunucu kapalı).</summary>
    Network,

    /// <summary>Yanıt zamanında gelmedi.</summary>
    Timeout,

    /// <summary>Yanıt anlaşılamadı.</summary>
    InvalidResponse,

    /// <summary>Diğer HTTP hataları.</summary>
    Other,
}

/// <summary>
/// Model sağlayıcısından dönen hata. <see cref="Exception.Message"/> kullanıcıya gösterilebilecek Türkçe
/// açıklamadır ve hiçbir zaman API anahtarını içermez.
/// </summary>
public sealed class ChatModelException : Exception
{
    public ChatModelException(ChatErrorKind kind, string message, int? statusCode = null, string? providerErrorType = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        StatusCode = statusCode;
        ProviderErrorType = providerErrorType;
    }

    public ChatErrorKind Kind { get; }

    /// <summary>HTTP durum kodu (varsa).</summary>
    public int? StatusCode { get; }

    /// <summary>Sağlayıcının hata türü (ör. "rate_limit_error").</summary>
    public string? ProviderErrorType { get; }

    /// <summary>Kısa bir süre sonra tekrar denemek işe yarayabilir mi?</summary>
    public bool IsTransient => Kind is ChatErrorKind.RateLimited or ChatErrorKind.Overloaded or ChatErrorKind.Server
        or ChatErrorKind.Network or ChatErrorKind.Timeout;
}

/// <summary>
/// Kullanıcı, tasarım verilerinin bu sağlayıcıya gönderilmesine açıkça izin vermedi. Bu durumda hiçbir istek
/// gönderilmez.
/// </summary>
public sealed class AssistantConsentException : Exception
{
    public const string DefaultMessage =
        "Tasarım verilerinin bu yapay zekâ sağlayıcısına gönderilmesine izin verilmedi. " +
        "Ayarlar → Yapay Zekâ bölümünden sağlayıcıyı seçip veri gönderimine açıkça izin verin; izin verilmeden hiçbir veri gönderilmez.";

    public AssistantConsentException() : base(DefaultMessage)
    {
    }

    public AssistantConsentException(string message) : base(message)
    {
    }
}

/// <summary>Asistan ayarları eksik veya hatalı (ör. anahtar ya da adres girilmemiş).</summary>
public sealed class AssistantConfigurationException : Exception
{
    public AssistantConfigurationException(string message) : base(message)
    {
    }
}
