namespace AkelEtiket.Core.Automation;

/// <summary>
/// Yapay zekâ ayarlarını dosya değiştikçe yeniden okuyan önbellek (en çok saniyede bir dosya zamanına bakar).
/// Böylece masaüstündeki ayar değişikliği (ör. bağlantıyı kapatma) Worker ve MCP köprüsünde de hemen geçerli olur.
/// </summary>
public sealed class AiSettingsMonitor
{
    private readonly object _lock = new();
    private AiSettings _current;
    private DateTime _stamp;
    private DateTime _checkedAt;

    public AiSettingsMonitor()
    {
        _current = AiSettingsStore.LoadWithToken();
        _stamp = Stamp();
        _checkedAt = DateTime.UtcNow;
    }

    public AiSettings Current
    {
        get
        {
            lock (_lock)
            {
                if (DateTime.UtcNow - _checkedAt < TimeSpan.FromSeconds(1)) return _current;
                _checkedAt = DateTime.UtcNow;
                var stamp = Stamp();
                if (stamp != _stamp)
                {
                    _stamp = stamp;
                    _current = AiSettingsStore.Load();
                }
                return _current;
            }
        }
    }

    /// <summary>Bir sonraki erişimde dosyayı yeniden okur.</summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _stamp = DateTime.MinValue;
            _checkedAt = DateTime.MinValue;
        }
    }

    private static DateTime Stamp()
    {
        try
        {
            return File.Exists(AiSettingsStore.FilePath) ? File.GetLastWriteTimeUtc(AiSettingsStore.FilePath) : DateTime.MinValue;
        }
        catch (IOException)
        {
            return DateTime.MinValue;
        }
    }
}
