using AkelEtiket.Assistant.Chat;

namespace AkelEtiket.Assistant;

/// <summary>Kullanıcının asistana tek bir isteği ve o anki tasarım bağlamı.</summary>
public sealed record AssistantRequest
{
    /// <summary>Kullanıcının doğal dil komutu.</summary>
    public required string UserText { get; init; }

    /// <summary>Tasarımın güncel hâli (ör. get_design çıktısı). Dış veri olarak işaretlenerek gönderilir.</summary>
    public string? DesignJson { get; init; }

    /// <summary>Baskı çözünürlüğünde önizleme (PNG). Ayarlarda görsel gönderimi kapalıysa sağlayıcıya gitmez.</summary>
    public byte[]? PreviewPng { get; init; }

    /// <summary>Bilgi için etiket ölçüsü (ör. "100 × 100 mm").</summary>
    public string? LabelSizeText { get; init; }
}

/// <summary>Asistan çalışmasının sonucu.</summary>
public enum AssistantRunStatus
{
    /// <summary>Model işini bitirip özet yazdı.</summary>
    Completed,

    /// <summary>Adım sınırına ulaşıldı.</summary>
    MaxIterationsReached,

    /// <summary>Yanıt uzunluk sınırına ulaştı.</summary>
    MaxTokensReached,

    /// <summary>Sağlayıcının güvenlik denetimi isteği reddetti.</summary>
    Refused,

    /// <summary>Kullanıcı durdurdu.</summary>
    Cancelled,

    /// <summary>Sağlayıcı veya ayar hatası.</summary>
    Failed,
}

/// <summary>Çalıştırılan bir araç çağrısının özeti (panelde adım listesi için).</summary>
public sealed record AssistantToolCallRecord(int Iteration, string Name, string ArgumentsSummary, bool IsError, string ResultSummary);

/// <summary>Asistan çalışmasının sonucu.</summary>
public sealed class AssistantRunResult
{
    public required AssistantRunStatus Status { get; init; }

    /// <summary>Kullanıcıya gösterilecek son metin (modelin özeti veya durumu açıklayan Türkçe ileti).</summary>
    public required string FinalText { get; init; }

    /// <summary>Hata durumunda ayrıntılı ileti.</summary>
    public string? ErrorMessage { get; init; }

    public IReadOnlyList<AssistantToolCallRecord> ToolCalls { get; init; } = [];

    /// <summary>Yapılan model çağrısı sayısı.</summary>
    public int Iterations { get; init; }

    public ChatUsage Usage { get; init; } = ChatUsage.Zero;
}

/// <summary>Panelde canlı gösterilecek olay türü.</summary>
public enum AssistantEventKind
{
    /// <summary>Modele istek gönderiliyor.</summary>
    ModelCallStarted,

    /// <summary>Model bir metin yazdı.</summary>
    AssistantText,

    /// <summary>Bir araç çalışmaya başladı.</summary>
    ToolCallStarted,

    /// <summary>Araç bitti (başarılı veya hatalı).</summary>
    ToolCallFinished,

    /// <summary>Çalışma bitti (bkz. <see cref="AssistantEvent.Status"/>).</summary>
    Completed,

    /// <summary>Kullanıcı durdurdu.</summary>
    Cancelled,

    /// <summary>Hata oluştu.</summary>
    Failed,
}

/// <summary>İlerleme olayı.</summary>
public sealed record AssistantEvent(
    AssistantEventKind Kind,
    int Iteration,
    string? ToolName = null,
    string? Text = null,
    bool IsError = false,
    AssistantRunStatus? Status = null);

/// <summary>Asistan oturumu seçenekleri.</summary>
public sealed class AssistantSessionOptions
{
    /// <summary>Bir istekte en fazla model çağrısı.</summary>
    public int MaxIterations { get; set; } = 20;

    /// <summary>Araç sonucundaki metnin modele gönderilecek en uzun hâli.</summary>
    public int MaxToolResultChars { get; set; } = 20_000;

    /// <summary>Kullanıcı mesajına eklenen tasarım verisinin en uzun hâli.</summary>
    public int MaxContextChars { get; set; } = 60_000;

    /// <summary>Yanıt başına çıktı belirteci (null = modelin seçeneği).</summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>Yazıcı çözünürlüğü (sistem istemi için).</summary>
    public double DotsPerMm { get; set; } = 8.0;
}
