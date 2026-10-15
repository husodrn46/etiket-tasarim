using System.Collections.ObjectModel;
using AkelEtiket.Assistant;
using AkelEtiket.Core.Automation;
using AkelEtiket.Editor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.Ai;

/// <summary>
/// Yapay Zekâ ayarları penceresi: dış ajan bağlantısı (yerel API + MCP), değişiklik modu, izinli klasörler ve
/// uygulama içi asistan sağlayıcısı. Varsayılan olarak her şey kapalıdır; dış servise veri gönderimi için açık izin
/// gerekir. Kaydedilince bağlantı yeni ayarlarla yeniden başlatılır.
/// </summary>
public sealed partial class AiSettingsViewModel : ObservableObject
{
    private readonly AiSettings _original;
    private readonly IDialogService _dialogs;

    public AiSettingsViewModel(AiSettings current, IDialogService dialogs)
    {
        _original = current;
        _dialogs = dialogs;
        Enabled = current.Enabled;
        Port = current.Port;
        Token = current.Token;
        DirectMode = current.ChangeMode == AiChangeMode.Direct;
        ShareOpenDocument = current.ShareOpenDocument;
        EnableHttpMcp = current.EnableHttpMcp;
        WorkerIdleMinutes = current.WorkerIdleMinutes;
        foreach (var folder in current.AllowedFolders) AllowedFolders.Add(folder);
        ProviderKind = current.Assistant.Kind;
        Model = current.Assistant.Model;
        BaseUrl = current.Assistant.BaseUrl;
        HasStoredKey = current.Assistant.ProtectedApiKey.Length > 0;
        Consent = current.Assistant.HasValidConsent;
        SendPreviewImages = current.Assistant.SendPreviewImages;
    }

    public IReadOnlyList<KeyValuePair<AssistantProviderKind, string>> ProviderChoices { get; } =
    [
        new(AssistantProviderKind.None, L10n.T("Yok (yalnızca dış ajanlar)")),
        new(AssistantProviderKind.Anthropic, L10n.T("Anthropic Claude (kendi API anahtarınız)")),
        new(AssistantProviderKind.OpenAiCompatible, L10n.T("OpenAI uyumlu uç nokta (Ollama, LM Studio, yerel model…)")),
    ];

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial int Port { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TokenDisplay), nameof(ClaudeCodeHttpCommand))]
    public partial string Token { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TokenDisplay))]
    public partial bool ShowToken { get; set; }

    [ObservableProperty]
    public partial bool DirectMode { get; set; }

    [ObservableProperty]
    public partial bool ShareOpenDocument { get; set; }

    [ObservableProperty]
    public partial bool EnableHttpMcp { get; set; }

    [ObservableProperty]
    public partial int WorkerIdleMinutes { get; set; }

    public ObservableCollection<string> AllowedFolders { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProviderSelected), nameof(NeedsBaseUrl), nameof(ModelSuggestions), nameof(ConsentText))]
    public partial AssistantProviderKind ProviderKind { get; set; }

    [ObservableProperty]
    public partial string Model { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConsentText))]
    public partial string BaseUrl { get; set; }

    /// <summary>Yeni girilen anahtar (boşsa kayıtlı anahtar korunur).</summary>
    [ObservableProperty]
    public partial string NewApiKey { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyStatus))]
    public partial bool HasStoredKey { get; set; }

    [ObservableProperty]
    public partial bool Consent { get; set; }

    [ObservableProperty]
    public partial bool SendPreviewImages { get; set; }

    [ObservableProperty]
    public partial string? SelectedFolder { get; set; }

    public bool IsProviderSelected => ProviderKind != AssistantProviderKind.None;
    public bool NeedsBaseUrl => ProviderKind == AssistantProviderKind.OpenAiCompatible;
    public IReadOnlyList<string> ModelSuggestions => ChatModelFactory.SuggestedModels(ProviderKind);
    public string KeyStatus => HasStoredKey ? L10n.T("Kayıtlı anahtar var (Windows hesabınıza şifreli saklanır).") : "Anahtar girilmedi.";
    public string TokenDisplay => ShowToken ? Token : new string('•', Math.Min(24, Token.Length));

    public string ConsentText => ProviderKind switch
    {
        AssistantProviderKind.Anthropic =>
            L10n.T("Asistanı kullandığımda açık tasarımın içeriğinin (metinler, barkod içerikleri, öğe konumları, isteğe bağlı önizleme görüntüsü ") +
            L10n.T("ve asistanın araçla okuduğu veri satırlarının) Anthropic'e gönderilmesine izin veriyorum."),
        AssistantProviderKind.OpenAiCompatible =>
            L10n.T($"Asistanı kullandığımda açık tasarımın içeriğinin {(string.IsNullOrWhiteSpace(BaseUrl) ? "girilen adrese" : BaseUrl.Trim() + " adresine")} ") +
            L10n.T("gönderilmesine izin veriyorum. (Yerel bir model ise veri bu bilgisayardan çıkmaz.)"),
        _ => "",
    };

    /// <summary>Bu bilgisayardaki MCP sunucusu (Claude Desktop / Claude Code bunu başlatır).</summary>
    public string McpExePath => Path.Combine(AppContext.BaseDirectory, "AkelEtiket.Mcp.exe");

    public string ClaudeCodeCommand => $"claude mcp add --scope user akel-etiket -- \"{McpExePath}\"";

    public string ClaudeCodeHttpCommand =>
        $"claude mcp add --transport http akel-etiket http://127.0.0.1:{Port}/mcp --header \"Authorization: Bearer {Token}\"";

    public string ClaudeDesktopJson =>
        "{\n  \"mcpServers\": {\n    \"akel-etiket\": {\n      \"command\": \"" + McpExePath.Replace("\\", "\\\\") + "\"\n    }\n  }\n}";

    [RelayCommand]
    private void RegenerateToken()
    {
        if (!_dialogs.Confirm(L10n.T("Bağlantı anahtarı"),
                L10n.T("Yeni anahtar oluşturulsun mu? Anahtarı elle girdiğiniz HTTP MCP / yerel API istemcilerini yeni anahtarla güncellemeniz gerekir. ") +
                L10n.T("(stdio MCP köprüsü anahtarı kendisi okur.)"), "Yenile", L10n.T("Vazgeç")))
            return;
        Token = AiSettings.NewToken();
        ShowToken = true;
    }

    public void AddFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        if (AllowedFolders.Any(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase))) return;
        AllowedFolders.Add(folder);
    }

    [RelayCommand]
    private void RemoveFolder()
    {
        if (SelectedFolder is { } folder) AllowedFolders.Remove(folder);
    }

    [RelayCommand]
    private void ClearKey()
    {
        NewApiKey = "";
        HasStoredKey = false;
        _clearKey = true;
    }

    private bool _clearKey;

    /// <summary>Doğrular; sorun varsa Türkçe açıklama döner.</summary>
    public string? Validate()
    {
        if (Port is < 1024 or > 65535) return L10n.T("Port 1024–65535 arasında olmalı.");
        if (ProviderKind == AssistantProviderKind.OpenAiCompatible &&
            !Uri.TryCreate(BaseUrl?.Trim(), UriKind.Absolute, out var uri))
            return L10n.T("OpenAI uyumlu uç nokta için geçerli bir adres girin (ör. http://127.0.0.1:11434/v1).");
        return null;
    }

    /// <summary>Kaydedilecek ayarları oluşturur (anahtar DPAPI ile şifrelenir; izin yalnızca işaretliyse ve sağlayıcı için verilir).</summary>
    public AiSettings BuildSettings()
    {
        var result = new AiSettings
        {
            Enabled = Enabled,
            Port = Port,
            Token = Token,
            ChangeMode = DirectMode ? AiChangeMode.Direct : AiChangeMode.Approval,
            ShareOpenDocument = ShareOpenDocument,
            EnableHttpMcp = EnableHttpMcp,
            WorkerIdleMinutes = WorkerIdleMinutes,
            AllowedFolders = AllowedFolders.ToList(),
            Assistant = new AssistantProviderSettings
            {
                Kind = ProviderKind,
                Model = Model?.Trim() ?? "",
                BaseUrl = BaseUrl?.Trim() ?? "",
                ProtectedApiKey = !string.IsNullOrWhiteSpace(NewApiKey) ? SecretProtector.Protect(NewApiKey.Trim())
                    : _clearKey ? "" : _original.Assistant.ProtectedApiKey,
                SendPreviewImages = SendPreviewImages,
            },
        };
        if (Consent && ProviderKind != AssistantProviderKind.None)
        {
            result.Assistant.DataSharingConsent = true;
            result.Assistant.ConsentScope = result.Assistant.CurrentScope;
            result.Assistant.ConsentGivenAt = _original.Assistant.HasValidConsent && _original.Assistant.ConsentScope == result.Assistant.CurrentScope
                ? _original.Assistant.ConsentGivenAt
                : DateTimeOffset.Now;
        }
        result.EnsureToken();
        result.Normalize();
        return result;
    }
}
