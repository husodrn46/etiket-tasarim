using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AkelEtiket.Core.Automation;

/// <summary>Yerel API'yi kimin barındırdığı.</summary>
public enum ApiHostKind
{
    /// <summary>Etiket Tasarım masaüstü uygulaması (açık tasarım canlı paylaşılır).</summary>
    Desktop,

    /// <summary>Penceresiz Worker (uygulama kapalıyken yalnızca dosyalar).</summary>
    Worker,

    /// <summary>Testler.</summary>
    Test,
}

/// <summary>Çalışan yerel API'nin adresi. Gizli bilgi içermez (anahtar ayar dosyasındadır).</summary>
public sealed record ApiEndpointInfo
{
    public required string Url { get; init; }
    public required int Port { get; init; }
    public required int ProcessId { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required ApiHostKind Host { get; init; }
    public string Version { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
}

/// <summary>
/// Yerel API keşfi: barındıran süreç adresini <c>baglanti.json</c> dosyasına yazar; MCP köprüsü buradan okur.
/// Aynı anda tek barındırıcı olur (<see cref="ApiHostLock"/>).
/// </summary>
public static class ApiDiscovery
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string FilePath => Path.Combine(AiSettingsStore.Folder, "baglanti.json");

    public static void Write(ApiEndpointInfo info)
    {
        Directory.CreateDirectory(AiSettingsStore.Folder);
        string temp = FilePath + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(info, Options));
        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>Dosyayı okur; süreç artık çalışmıyorsa null döner.</summary>
    public static ApiEndpointInfo? TryRead()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var info = JsonSerializer.Deserialize<ApiEndpointInfo>(File.ReadAllText(FilePath), Options);
            if (info is null || !IsProcessAlive(info.ProcessId)) return null;
            return info;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Dosyayı yalnızca bu sürece aitse siler.</summary>
    public static void Remove(int processId)
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var info = JsonSerializer.Deserialize<ApiEndpointInfo>(File.ReadAllText(FilePath), Options);
            if (info is null || info.ProcessId == processId) File.Delete(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
