using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AkelEtiket.Core.Automation;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AkelEtiket.LocalApi.Tests;

/// <summary>
/// Uçtan uca: gerçek MCP istemcisi → AkelEtiket.Mcp.exe (stdio) → (uygulama kapalı) Worker'ı başlatır → yerel API →
/// tasarım dosyası. Bütün veriler geçici bir köke yönlendirilir (AKELETIKET2_VERI); kullanıcının klasörlerine dokunulmaz.
/// </summary>
[Collection("MCP uçtan uca")]
public sealed class McpEndToEndTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "akel-mcp-e2e", Guid.NewGuid().ToString("N"));

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AkelEtiket.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
        }
    }

    /// <summary>Yayınlanmış klasörü denemek için: AKELETIKET2_E2E_DIR (MCP ve Worker aynı klasörde olmalı).</summary>
    private static string? PublishedDir => Environment.GetEnvironmentVariable("AKELETIKET2_E2E_DIR") is { Length: > 0 } d ? d : null;

    private static string Exe(string project) => PublishedDir is { } dir
        ? Path.Combine(dir, project + ".exe")
        : Path.Combine(RepoRoot, "src", project, "bin", "Debug", "net10.0", project + ".exe");

    private string SettingsPath => Path.Combine(_root, "Yerel", "YapayZeka", "ayarlar.json");
    private string DiscoveryPath => Path.Combine(_root, "Yerel", "YapayZeka", "baglanti.json");

    private void WriteSettings(bool enabled)
    {
        int port;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var settings = new AiSettings { Enabled = enabled, Port = port, Token = AiSettings.NewToken(), WorkerIdleMinutes = 1 };
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
        }));
    }

    private StdioClientTransport Transport() => new(new StdioClientTransportOptions
    {
        Name = "akel-etiket-test",
        Command = Exe("AkelEtiket.Mcp"),
        EnvironmentVariables = new Dictionary<string, string?>
        {
            [AppIdentityVariable] = _root,
            // Yayınlanmış klasörde Worker, MCP sunucusunun yanında aranır (gerçek kurulumdaki gibi).
            ["AKELETIKET2_WORKER"] = PublishedDir is null ? Exe("AkelEtiket.Worker") : null,
        },
    });

    private const string AppIdentityVariable = Core.AppIdentity.DataRootVariable;

    [Fact]
    public async Task ClaudeLikeClient_CreatesAndSavesDesign_ThroughWorker()
    {
        Assert.True(File.Exists(Exe("AkelEtiket.Mcp")), "MCP exe derlenmemiş");
        Assert.True(File.Exists(Exe("AkelEtiket.Worker")), "Worker exe derlenmemiş");
        WriteSettings(enabled: true);
        try
        {
            await using var client = await McpClient.CreateAsync(Transport());
            Assert.Equal("akel-etiket", client.ServerInfo.Name);
            Assert.Contains("create_design", client.ServerInstructions ?? "");

            var tools = await client.ListToolsAsync();
            Assert.True(tools.Count >= 35);

            var created = await client.CallToolAsync("create_design", new Dictionary<string, object?> { ["name"] = "Uçtan Uca", ["width_mm"] = 100, ["height_mm"] = 50 });
            Assert.NotEqual(true, created.IsError);
            string id = created.StructuredContent!.Value.GetProperty("design_id").GetString()!;
            var text = await client.CallToolAsync("add_text", new Dictionary<string, object?>
            {
                ["design_id"] = id, ["text"] = "Örnek Firma", ["placement"] = "center", ["font_size_pt"] = 18, ["bold"] = true,
            });
            Assert.NotEqual(true, text.IsError);
            var preview = await client.CallToolAsync("render_preview", new Dictionary<string, object?> { ["design_id"] = id });
            Assert.Contains(preview.Content, c => c is ImageContentBlock);
            var saved = await client.CallToolAsync("save_design", new Dictionary<string, object?> { ["design_id"] = id });
            Assert.NotEqual(true, saved.IsError);

            Assert.True(File.Exists(Path.Combine(_root, "Belgeler", "Şablonlar", "Uçtan Uca.akelabel")));
            var discovery = JsonNode.Parse(File.ReadAllText(DiscoveryPath))!;
            Assert.Equal("Worker", discovery["host"]!.GetValue<string>());

            // Hatalı istek MCP hata sonucu olarak döner (sunucu çökmez).
            var bad = await client.CallToolAsync("add_barcode", new Dictionary<string, object?>
            {
                ["design_id"] = id, ["symbology"] = "ean13", ["content"] = "123", ["x"] = 1, ["y"] = 1,
            });
            Assert.True(bad.IsError);
        }
        finally
        {
            StopWorker();
        }
    }

    [Fact]
    public async Task DisabledConnection_IsReportedClearly()
    {
        WriteSettings(enabled: false);
        await using var client = await McpClient.CreateAsync(Transport());
        var ex = await Assert.ThrowsAnyAsync<Exception>(async () => await client.ListToolsAsync());
        Assert.Contains("kapalı", ex.Message);
        Assert.False(File.Exists(DiscoveryPath));
    }

    private void StopWorker()
    {
        try
        {
            if (!File.Exists(DiscoveryPath)) return;
            int pid = JsonNode.Parse(File.ReadAllText(DiscoveryPath))!["processId"]!.GetValue<int>();
            using var process = Process.GetProcessById(pid);
            if (process.ProcessName.Contains("AkelEtiket.Worker", StringComparison.OrdinalIgnoreCase))
            {
                process.Kill();
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
        }
    }

    public void Dispose()
    {
        StopWorker();
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
