using System.Globalization;
using System.Reflection;
using AkelEtiket.Core.Automation;
using AkelEtiket.Design;
using AkelEtiket.Design.Tools;
using AkelEtiket.LocalApi;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Worker;

/// <summary>
/// Etiket Tasarım Worker: masaüstü uygulaması kapalıyken yapay zekâ ajanlarının (MCP köprüsü veya yerel API
/// istemcileri) tasarım dosyalarıyla çalışabilmesi için yerel API'yi penceresiz barındırır. Yalnızca yapay zekâ
/// bağlantısı ayarlardan açıksa çalışır; boşta kalınca, bağlantı kapatılınca ya da masaüstü devralınca kapanır.
/// Çıkış kodları: 0 normal, 2 bağlantı kapalı, 3 başlatılamadı.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var turkish = CultureInfo.GetCultureInfo("tr-TR");
        CultureInfo.DefaultThreadCurrentCulture = turkish;
        CultureInfo.DefaultThreadCurrentUICulture = turkish;
        string version = Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "2.0.0";
        ProjectFile.AppVersion = version;

        var monitor = new AiSettingsMonitor();
        if (!monitor.Current.Enabled)
        {
            Log("Yapay zekâ bağlantısı kapalı; Worker başlatılmadı.");
            return 2;
        }

        using var hostLock = ApiHostLock.TryAcquire();
        if (hostLock is null)
        {
            Log("Yerel API zaten başka bir süreçte (masaüstü veya başka Worker) çalışıyor.");
            return 0;
        }

        using var stop = new CancellationTokenSource();
        LocalApiHost host;
        var service = new DesignService(new DesignServiceOptions { SettingsProvider = () => monitor.Current });
        service.Working.Cleanup(TimeSpan.FromDays(14));
        service.Proposals.Cleanup(TimeSpan.FromDays(30));
        var tools = new DesignToolset(service);
        try
        {
            host = await LocalApiHost.StartAsync(new LocalApiOptions
            {
                Tools = tools.Registry,
                Settings = () => monitor.Current,
                HostKind = ApiHostKind.Worker,
                ProductVersion = version,
                HandoverRequested = () =>
                {
                    Log("Masaüstü uygulaması görevi devralıyor.");
                    stop.Cancel();
                },
            }).ConfigureAwait(false);
        }
        catch (LocalApiException ex)
        {
            Log("Başlatılamadı: " + ex.Message);
            return 3;
        }

        await using (host)
        {
            ApiHostCoordinator.Publish(host, version);
            Log($"Yerel API çalışıyor: {host.Url} (PID {Environment.ProcessId}).");
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stop.Token).ConfigureAwait(false);
                    var settings = monitor.Current;
                    if (!settings.Enabled)
                    {
                        Log("Yapay zekâ bağlantısı kapatıldı; Worker kapanıyor.");
                        break;
                    }
                    if (DateTimeOffset.Now - host.LastActivity > TimeSpan.FromMinutes(settings.WorkerIdleMinutes))
                    {
                        Log($"{settings.WorkerIdleMinutes} dakikadır istek yok; Worker kapanıyor.");
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            ApiHostCoordinator.Unpublish();
            await host.StopAsync().ConfigureAwait(false);
        }
        return 0;
    }

    private static void Log(string message)
    {
        try
        {
            string folder = Path.Combine(AiSettingsStore.Folder, "Gunluk");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, $"worker-{DateTime.Now:yyyyMMdd}.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
