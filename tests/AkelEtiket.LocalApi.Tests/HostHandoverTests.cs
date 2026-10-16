using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using AkelEtiket.Core;
using AkelEtiket.Core.Automation;

namespace AkelEtiket.LocalApi.Tests;

/// <summary>
/// Barındırıcı devri: Worker yerel API'yi barındırırken masaüstü açılınca görevi devralır (Worker kapanır, kilit
/// masaüstüne geçer). Süreçler arası kilit ve keşif dosyası gerçek Worker süreciyle denenir.
/// </summary>
[Collection("MCP uçtan uca")]
public sealed class HostHandoverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "akel-handover", Guid.NewGuid().ToString("N"));
    private readonly string? _previous = Environment.GetEnvironmentVariable(AppIdentity.DataRootVariable);

    public HostHandoverTests()
    {
        Environment.SetEnvironmentVariable(AppIdentity.DataRootVariable, _root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AppIdentity.DataRootVariable, _previous);
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    private static string BuildConfiguration => typeof(HostHandoverTests).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
        .OfType<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration;

    private static string WorkerExe
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AkelEtiket.sln"))) dir = dir.Parent;
            return Path.Combine(dir!.FullName, "src", "AkelEtiket.Worker", "bin", BuildConfiguration, "net10.0", "AkelEtiket.Worker.exe");
        }
    }

    [Fact]
    public async Task DesktopTakesOverFromRunningWorker()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var settings = new AiSettings { Enabled = true, Port = port, Token = AiSettings.NewToken(), WorkerIdleMinutes = 5 };
        AiSettingsStore.Save(settings);

        var start = new ProcessStartInfo(WorkerExe) { UseShellExecute = false, CreateNoWindow = true };
        start.Environment[AppIdentity.DataRootVariable] = _root;
        using var worker = Process.Start(start)!;
        try
        {
            ApiEndpointInfo? info = null;
            for (int i = 0; i < 100 && info is null; i++)
            {
                await Task.Delay(200);
                info = ApiDiscovery.TryRead();
            }
            Assert.NotNull(info);
            Assert.Equal(ApiHostKind.Worker, info!.Host);
            Assert.Equal(worker.Id, info.ProcessId);
            Assert.Null(ApiHostLock.TryAcquire());   // kilit Worker'da

            using var hostLock = await ApiHostCoordinator.AcquireForDesktopAsync(settings);
            Assert.NotNull(hostLock);
            Assert.True(worker.WaitForExit(10_000), "Worker devirden sonra kapanmadı");
            Assert.Null(ApiDiscovery.TryRead());     // Worker keşif kaydını kaldırdı
        }
        finally
        {
            if (!worker.HasExited) worker.Kill();
        }
    }

    [Fact]
    public void WorkerRefusesToStartWhenConnectionDisabled()
    {
        AiSettingsStore.Save(new AiSettings { Enabled = false });
        var start = new ProcessStartInfo(WorkerExe) { UseShellExecute = false, CreateNoWindow = true };
        start.Environment[AppIdentity.DataRootVariable] = _root;
        using var worker = Process.Start(start)!;
        Assert.True(worker.WaitForExit(15_000));
        Assert.Equal(2, worker.ExitCode);
        Assert.Null(ApiDiscovery.TryRead());
    }
}
