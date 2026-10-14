using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AkelEtiket.Core.Automation;

/// <summary>Yapay zekâ değişikliklerinin mevcut tasarımlara nasıl uygulanacağı.</summary>
public enum AiChangeMode
{
    /// <summary>
    /// Varsayılan. Mevcut tasarımlar yalnızca taslakta değiştirilir; kalıcı olması için kullanıcının uygulamada
    /// öneriyi onaylaması gerekir. Yapay zekânın yeni oluşturduğu tasarımlar yeni dosya olarak kaydedilebilir.
    /// </summary>
    Approval,

    /// <summary>Değişiklikler açık tasarıma/dosyaya hemen uygulanır (geri alınabilir, dosyadan önce yedek alınır).</summary>
    Direct,
}

/// <summary>Uygulama içi asistanın kullandığı model sağlayıcısı.</summary>
public enum AssistantProviderKind
{
    /// <summary>Sağlayıcı yok: panel yalnızca dış ajanların önerilerini gösterir.</summary>
    None,

    /// <summary>Anthropic Claude (Messages API, kullanıcının kendi API anahtarı).</summary>
    Anthropic,

    /// <summary>OpenAI uyumlu sohbet uç noktası (Ollama, LM Studio gibi yerel modeller veya uyumlu servisler).</summary>
    OpenAiCompatible,
}

/// <summary>Uygulama içi asistan ayarları.</summary>
public sealed class AssistantProviderSettings
{
    public AssistantProviderKind Kind { get; set; } = AssistantProviderKind.None;

    /// <summary>Model adı (boşsa sağlayıcının varsayılanı).</summary>
    public string Model { get; set; } = "";

    /// <summary>Uç nokta adresi (OpenAI uyumlu için zorunlu, ör. http://127.0.0.1:11434/v1).</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>API anahtarı, Windows DPAPI ile şifrelenmiş (Base64). Düz metin anahtar dosyaya yazılmaz.</summary>
    public string ProtectedApiKey { get; set; } = "";

    /// <summary>Kullanıcı, tasarım verilerinin bu sağlayıcıya gönderilmesine açıkça izin verdi mi?</summary>
    public bool DataSharingConsent { get; set; }

    /// <summary>İznin verildiği an (bilgi için).</summary>
    public DateTimeOffset? ConsentGivenAt { get; set; }

    /// <summary>İzin hangi sağlayıcı + adres için verildi (sağlayıcı değişirse izin geçersizdir).</summary>
    public string ConsentScope { get; set; } = "";

    /// <summary>Önizleme görüntüsü de gönderilsin mi (görsel anlayan modeller için).</summary>
    public bool SendPreviewImages { get; set; } = true;

    /// <summary>İznin geçerli olduğu kapsam anahtarı.</summary>
    [JsonIgnore]
    public string CurrentScope => $"{Kind}|{BaseUrl.Trim().TrimEnd('/')}".ToLowerInvariant();

    /// <summary>Veri gönderimine geçerli bir izin var mı?</summary>
    [JsonIgnore]
    public bool HasValidConsent => Kind != AssistantProviderKind.None && DataSharingConsent && ConsentScope == CurrentScope;
}

/// <summary>
/// Yapay zekâ bağlantı ayarları (%LOCALAPPDATA%\Etiket Tasarım\YapayZeka\ayarlar.json). Masaüstü, Worker ve MCP
/// köprüsü aynı dosyayı okur. Varsayılan olarak her şey kapalıdır.
/// </summary>
public sealed class AiSettings
{
    public const int DefaultPort = 47820;

    /// <summary>Yerel API ve MCP bağlantısı açık mı? (Kapalıyken dış ajanlar bağlanamaz.)</summary>
    public bool Enabled { get; set; }

    /// <summary>Yerel API portu (yalnızca 127.0.0.1).</summary>
    public int Port { get; set; } = DefaultPort;

    /// <summary>Bağlantı anahtarı (Bearer). Boşsa ilk kullanımda üretilir.</summary>
    public string Token { get; set; } = "";

    public AiChangeMode ChangeMode { get; set; } = AiChangeMode.Approval;

    /// <summary>Uygulamada açık olan tasarım yapay zekâ ile paylaşılsın mı?</summary>
    public bool ShareOpenDocument { get; set; } = true;

    /// <summary>Görsel ve veri dosyalarının okunabileceği ek klasörler (Şablonlar klasörü her zaman izinlidir).</summary>
    public List<string> AllowedFolders { get; set; } = [];

    /// <summary>Yerel API üzerinde HTTP MCP uç noktası (/mcp) açık mı?</summary>
    public bool EnableHttpMcp { get; set; } = true;

    /// <summary>Worker'ın boşta kapanma süresi (dakika).</summary>
    public int WorkerIdleMinutes { get; set; } = 15;

    public AssistantProviderSettings Assistant { get; set; } = new();

    /// <summary>Anahtar yoksa güvenli rastgele bir anahtar üretir. Değişiklik olduysa true.</summary>
    public bool EnsureToken()
    {
        if (Token.Length >= 32) return false;
        Token = NewToken();
        return true;
    }

    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Normalize()
    {
        if (Port is < 1024 or > 65535) Port = DefaultPort;
        WorkerIdleMinutes = Math.Clamp(WorkerIdleMinutes, 1, 24 * 60);
        AllowedFolders = (AllowedFolders ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assistant ??= new AssistantProviderSettings();
        Token ??= "";
    }
}

/// <summary>Yapay zekâ ayar dosyasını okur/yazar (atomik yazım).</summary>
public static class AiSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Yapay zekâ çalışma klasörü (%LOCALAPPDATA%\Etiket Tasarım\YapayZeka).</summary>
    public static string Folder => Path.Combine(AppIdentity.LocalFolder, "YapayZeka");

    public static string FilePath => Path.Combine(Folder, "ayarlar.json");

    /// <summary>Ayarları okur; dosya yoksa veya bozuksa varsayılanları döndürür (bütün özellikler kapalı).</summary>
    public static AiSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AiSettings>(File.ReadAllText(FilePath), Options) ?? new AiSettings();
                settings.Normalize();
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Bozuk dosya: güvenli varsayılan (kapalı).
        }
        var fresh = new AiSettings();
        fresh.Normalize();
        return fresh;
    }

    public static void Save(AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        Directory.CreateDirectory(Folder);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>Anahtar yoksa üretip kaydeder ve ayarları döndürür.</summary>
    public static AiSettings LoadWithToken()
    {
        var settings = Load();
        if (settings.EnsureToken()) Save(settings);
        return settings;
    }
}
