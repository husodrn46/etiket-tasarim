namespace AkelEtiket.Assistant.Providers;

/// <summary>Sohbet modeli seçenekleri (her iki sağlayıcı için ortak + Anthropic'e özgü olanlar).</summary>
public sealed class ChatModelOptions
{
    /// <summary>Yanıt başına en fazla çıktı belirteci (akışsız istekte zaman aşımı yaşanmaması için ~16 000).</summary>
    public int MaxOutputTokens { get; set; } = 16000;

    /// <summary>Tek bir isteğin en uzun süresi. HttpClient.Timeout bundan kısa olmamalıdır (önerilen: sonsuz).</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Geçici hatalarda (429, 5xx, 529, bağlantı hatası) en fazla yeniden deneme sayısı.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Bekleme işlevi (testlerde gerçekten beklememek için değiştirilir).</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    /// <summary>
    /// Anthropic: istem önbelleği (üst düzey <c>cache_control</c>). Araç döngüsünde her adımda aynı önek tekrar
    /// gönderildiği için maliyeti düşürür. Sağlayıcı reddederse oturum boyunca kapatılır.
    /// </summary>
    public bool EnablePromptCaching { get; set; } = true;

    /// <summary>
    /// Anthropic: güvenlik denetimi bir isteği reddederse sunucu tarafında Anthropic'in önerdiği yedek modelle
    /// tekrar denensin mi (<c>fallbacks: "default"</c>). Varsayılan kapalı (null/false): veri yalnızca seçilen modele gider.
    /// true verilse bile yalnızca resmî adreste ve destekleyen modellerde açılır.
    /// Sağlayıcı reddederse oturum boyunca kapatılır.
    /// </summary>
    public bool? UseServerSideFallback { get; set; }

    /// <summary>Anthropic: çaba düzeyi (low/medium/high/xhigh/max). null = modelin varsayılanı (gönderilmez).</summary>
    public string? Effort { get; set; }
}
