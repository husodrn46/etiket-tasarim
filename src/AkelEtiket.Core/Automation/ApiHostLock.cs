namespace AkelEtiket.Core.Automation;

/// <summary>
/// Yerel API barındırıcı rolü için süreçler arası kilit: kilit dosyası paylaşımsız açık tutulur. Süreç kapanınca
/// (çökse bile) Windows dosyayı bırakır; iş parçacığı bağımlılığı yoktur.
/// </summary>
public sealed class ApiHostLock : IDisposable
{
    private FileStream? _stream;

    private ApiHostLock(FileStream stream)
    {
        _stream = stream;
    }

    public static string FilePath => Path.Combine(AiSettingsStore.Folder, "api.lock");

    /// <summary>Kilidi almayı dener; başka süreç tutuyorsa null.</summary>
    public static ApiHostLock? TryAcquire()
    {
        try
        {
            Directory.CreateDirectory(AiSettingsStore.Folder);
            var stream = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            return new ApiHostLock(stream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Kilidi belirtilen süre boyunca almaya çalışır.</summary>
    public static async Task<ApiHostLock?> AcquireAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var hostLock = TryAcquire();
            if (hostLock is not null || DateTime.UtcNow >= deadline) return hostLock;
            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
