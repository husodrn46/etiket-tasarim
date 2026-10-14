using System.Net.Http.Headers;
using AkelEtiket.Core.Automation;

namespace AkelEtiket.LocalApi;

/// <summary>
/// Yerel API'yi kimin barındıracağını düzenler: aynı anda tek barındırıcı olur (kilit dosyası). Masaüstü açılınca
/// Worker'dan görevi devralır; Worker yalnızca masaüstü kapalıyken çalışır.
/// </summary>
public static class ApiHostCoordinator
{
    /// <summary>
    /// Masaüstü için barındırıcı kilidini alır. Kilidi bir Worker tutuyorsa ondan devretmesini ister ve bekler.
    /// Başka bir masaüstü penceresi tutuyorsa null döner.
    /// </summary>
    public static async Task<ApiHostLock?> AcquireForDesktopAsync(AiSettings settings, CancellationToken cancellationToken = default)
    {
        var hostLock = ApiHostLock.TryAcquire();
        if (hostLock is not null) return hostLock;
        var info = ApiDiscovery.TryRead();
        if (info is { Host: ApiHostKind.Worker })
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var request = new HttpRequestMessage(HttpMethod.Post, info.Url + "/api/host/handover");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
                using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Worker yanıt vermiyorsa kilidin bırakılmasını yine de bekle (çökmüş olabilir).
            }
            return await ApiHostLock.AcquireAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        }
        return await ApiHostLock.AcquireAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Çalışan API'nin adresini keşif dosyasına yazar.</summary>
    public static ApiEndpointInfo Publish(LocalApiHost host, string version)
    {
        var info = new ApiEndpointInfo
        {
            Url = host.Url,
            Port = host.Port,
            ProcessId = Environment.ProcessId,
            Host = host.HostKind,
            Version = version,
            StartedAt = DateTimeOffset.Now,
        };
        ApiDiscovery.Write(info);
        return info;
    }

    public static void Unpublish() => ApiDiscovery.Remove(Environment.ProcessId);
}
