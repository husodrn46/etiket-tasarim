using AkelEtiket.Core.Automation;
using AkelEtiket.Design.Tools;
using AkelEtiket.LocalApi;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.Ai;

/// <summary>Yerel API bağlantısının durumu.</summary>
public enum AiConnectionState
{
    /// <summary>Ayarlardan kapalı.</summary>
    Off,

    /// <summary>Bu pencere yerel API'yi barındırıyor.</summary>
    Hosting,

    /// <summary>Başka bir Etiket Tasarım penceresi barındırıyor.</summary>
    OtherWindow,

    /// <summary>Başlatılamadı.</summary>
    Error,
}

/// <summary>
/// Masaüstü açıkken yerel API'yi (ve /mcp uç noktasını) bu süreçte barındırır: açılışta Worker'dan devralır,
/// kapanışta bırakır; ayarlar değişince yeniden başlatır.
/// </summary>
public sealed class AiConnectionService : IAsyncDisposable
{
    private readonly IToolExecutor _tools;
    private readonly AiSettingsMonitor _settings;
    private readonly string _version;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LocalApiHost? _host;
    private ApiHostLock? _lock;

    public AiConnectionService(IToolExecutor tools, AiSettingsMonitor settings, string version)
    {
        _tools = tools;
        _settings = settings;
        _version = version;
    }

    public AiConnectionState State { get; private set; } = AiConnectionState.Off;

    public string StatusText { get; private set; } = L10n.T("Yapay zekâ bağlantısı kapalı.");

    public string? Url => _host?.Url;

    public event EventHandler? StatusChanged;

    /// <summary>Yerel API'ye araç çağrısı geldi (herhangi bir iş parçacığında).</summary>
    public event EventHandler<ApiActivity>? Activity;

    /// <summary>Ayarlara göre başlatır/durdurur (açılışta ve ayarlar kaydedilince çağrılır).</summary>
    public async Task ApplySettingsAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _settings.Invalidate();
            var settings = _settings.Current;
            await StopCoreAsync().ConfigureAwait(false);
            if (!settings.Enabled)
            {
                SetState(AiConnectionState.Off, L10n.T("Yapay zekâ bağlantısı kapalı (Ayarlar → Yapay Zekâ)."));
                return;
            }
            _lock = await ApiHostCoordinator.AcquireForDesktopAsync(settings).ConfigureAwait(false);
            if (_lock is null)
            {
                SetState(AiConnectionState.OtherWindow, L10n.T("Yapay zekâ bağlantısı başka bir Etiket Tasarım penceresinde çalışıyor."));
                return;
            }
            try
            {
                _host = await LocalApiHost.StartAsync(new LocalApiOptions
                {
                    Tools = _tools,
                    Settings = () => _settings.Current,
                    HostKind = ApiHostKind.Desktop,
                    ProductVersion = _version,
                }).ConfigureAwait(false);
            }
            catch (LocalApiException ex)
            {
                _lock.Dispose();
                _lock = null;
                SetState(AiConnectionState.Error, ex.Message);
                return;
            }
            _host.Activity += OnActivity;
            ApiHostCoordinator.Publish(_host, _version);
            string mcp = settings.EnableHttpMcp ? $" · MCP: {_host.Url}/mcp" : "";
            SetState(AiConnectionState.Hosting, L10n.T($"Bağlantı açık: {_host.Url}{mcp}"));
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnActivity(object? sender, ApiActivity activity) => Activity?.Invoke(this, activity);

    private async Task StopCoreAsync()
    {
        if (_host is not null)
        {
            _host.Activity -= OnActivity;
            ApiHostCoordinator.Unpublish();
            await _host.DisposeAsync().ConfigureAwait(false);
            _host = null;
        }
        _lock?.Dispose();
        _lock = null;
    }

    private void SetState(AiConnectionState state, string text)
    {
        State = state;
        StatusText = text;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
