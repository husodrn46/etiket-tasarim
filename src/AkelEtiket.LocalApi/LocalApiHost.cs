using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using AkelEtiket.Core.Automation;
using AkelEtiket.Design.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.LocalApi;

/// <summary>Yerel API ayarları.</summary>
public sealed class LocalApiOptions
{
    public required IToolExecutor Tools { get; init; }
    public required Func<AiSettings> Settings { get; init; }
    public required ApiHostKind HostKind { get; init; }
    public string ProductVersion { get; init; } = "2.0.0";
    public string ServerInstructions { get; init; } = DesignToolset.Instructions;

    /// <summary>Worker'da: masaüstü devralmak istediğinde çağrılır (POST /api/host/handover).</summary>
    public Action? HandoverRequested { get; init; }

    /// <summary>Port meşgulse sonraki kaç port denensin.</summary>
    public int PortAttempts { get; init; } = 10;

    /// <summary>Testler için: 0 = rastgele boş port.</summary>
    public int? PortOverride { get; init; }
}

/// <summary>API'ye gelen bir araç çağrısı (arayüzdeki etkinlik göstergesi ve Worker'ın boşta kalma ölçümü için).</summary>
public sealed record ApiActivity(DateTimeOffset Time, string Client, string Tool, bool Ok);

/// <summary>
/// Yerel API: yalnızca 127.0.0.1'e bağlanır; her istekte Bearer anahtar, Host başlığı (DNS yeniden bağlama saldırısına
/// karşı) ve tarayıcı kaynağı (Origin) denetlenir. Uçlar: /api/health, /api/tools, /api/tools/{ad}, /api/host/handover
/// ve isteğe bağlı MCP (Streamable HTTP) /mcp.
/// </summary>
public sealed class LocalApiHost : IAsyncDisposable
{
    public const string ClientHeader = "X-Akel-Client";
    public const string SessionHeader = "X-Akel-Session";
    private const long MaxBodyBytes = 10L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly WebApplication _app;
    private readonly LocalApiOptions _options;

    private LocalApiHost(WebApplication app, LocalApiOptions options, int port)
    {
        _app = app;
        _options = options;
        Port = port;
    }

    public int Port { get; private set; }

    public string Url => $"http://127.0.0.1:{Port}";

    public ApiHostKind HostKind => _options.HostKind;

    /// <summary>Son araç çağrısının zamanı.</summary>
    public DateTimeOffset LastActivity { get; private set; } = DateTimeOffset.Now;

    public event EventHandler<ApiActivity>? Activity;

    /// <summary>API'yi başlatır. Port meşgulse sonrakini dener; hiçbiri boş değilse <see cref="LocalApiException"/>.</summary>
    public static async Task<LocalApiHost> StartAsync(LocalApiOptions options, CancellationToken cancellationToken = default)
    {
        var settings = options.Settings();
        if (!settings.Enabled) throw new LocalApiException(L10n.T("Yapay zekâ bağlantısı kapalı (Ayarlar → Yapay Zekâ)."));
        if (settings.Token.Length < 32) throw new LocalApiException(L10n.T("Bağlantı anahtarı oluşturulmamış."));

        int firstPort = options.PortOverride ?? settings.Port;
        int attempts = options.PortOverride == 0 ? 1 : Math.Max(1, options.PortAttempts);
        Exception? last = null;
        for (int i = 0; i < attempts; i++)
        {
            int port = firstPort == 0 ? 0 : firstPort + i;
            try
            {
                return await StartOnPortAsync(options, port, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException
                                       || ex.InnerException is SocketException)
            {
                last = ex;
            }
        }
        throw new LocalApiException(L10n.T($"Yerel API başlatılamadı: {firstPort}–{firstPort + attempts - 1} portları kullanımda. ({last?.Message})"), last);
    }

    private static async Task<LocalApiHost> StartOnPortAsync(LocalApiOptions options, int port, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ApplicationName = "AkelEtiket.LocalApi" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(k =>
        {
            k.Listen(IPAddress.Loopback, port);
            k.Limits.MaxRequestBodySize = MaxBodyBytes;
            k.AddServerHeader = false;
        });

        LocalApiHost? self = null;
        var settingsAtStart = options.Settings();
        if (settingsAtStart.EnableHttpMcp)
        {
            builder.Services
                .AddMcpServer(o =>
                {
                    o.ServerInfo = new Implementation { Name = "akel-etiket", Title = L10n.T("Etiket Tasarım"), Version = options.ProductVersion };
                    o.ServerInstructions = options.ServerInstructions;
                })
                .WithHttpTransport()
                .WithListToolsHandler((request, ct) => ValueTask.FromResult(McpMapping.ListTools(options.Tools)))
                .WithCallToolHandler(async (request, ct) =>
                {
                    string client = (request.Server.ClientInfo?.Name ?? "MCP istemcisi") + " (HTTP MCP)";
                    var context = new ToolCallContext { ClientName = client, ClientId = request.Server.SessionId };
                    var args = McpMapping.ArgumentsToJson(request.Params?.Arguments);
                    var result = await options.Tools.ExecuteAsync(request.Params?.Name ?? "", args, context, ct).ConfigureAwait(false);
                    self?.Record(client, request.Params?.Name ?? "?", !result.IsError);
                    return McpMapping.ToCallToolResult(result);
                });
        }

        var app = builder.Build();
        self = new LocalApiHost(app, options, port);
        app.Use(self.GuardAsync);

        app.MapGet("/api/health", () => Results.Json(self.Health(), JsonOptions));
        app.MapGet("/api/tools", () => Results.Json(ToolsJson(options.Tools), JsonOptions));
        app.MapPost("/api/tools/{name}", self.CallToolAsync);
        app.MapPost("/api/host/handover", () =>
        {
            if (options.HostKind != ApiHostKind.Worker || options.HandoverRequested is null)
                return Results.Json(new { ok = false, message = L10n.T("Bu süreç görevi devredemez.") }, JsonOptions, statusCode: 409);
            _ = Task.Run(() =>
            {
                Thread.Sleep(200);
                options.HandoverRequested();
            });
            return Results.Json(new { ok = true }, JsonOptions);
        });
        if (settingsAtStart.EnableHttpMcp) app.MapMcp("/mcp");

        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        if (port == 0)
        {
            var address = app.Urls.FirstOrDefault() ?? throw new LocalApiException(L10n.T("Port alınamadı."));
            self.Port = new Uri(address).Port;
        }
        return self;
    }

    private void Record(string client, string tool, bool ok)
    {
        LastActivity = DateTimeOffset.Now;
        Activity?.Invoke(this, new ApiActivity(DateTimeOffset.Now, client, tool, ok));
    }

    private object Health()
    {
        var settings = _options.Settings();
        return new
        {
            product = Core.AppIdentity.ProductName,
            version = _options.ProductVersion,
            host = _options.HostKind.ToString(),
            ai_enabled = settings.Enabled,
            change_mode = settings.ChangeMode.ToString(),
            share_open_document = settings.ShareOpenDocument,
            tool_count = _options.Tools.Tools.Count,
            mcp_http = settings.EnableHttpMcp ? "/mcp" : null,
        };
    }

    /// <summary>Kimlik, Host ve Origin denetimi.</summary>
    private async Task GuardAsync(HttpContext context, RequestDelegate next)
    {
        var request = context.Request;
        string host = request.Host.Value ?? "";
        if (!IsAllowedHost(host, Port))
        {
            await Reject(context, 400, L10n.T("Geçersiz Host başlığı."));
            return;
        }
        if (request.Headers.Origin.Count > 0 && !string.IsNullOrEmpty(request.Headers.Origin.ToString()))
        {
            await Reject(context, 403, L10n.T("Tarayıcı kaynaklı isteklere izin verilmez."));
            return;
        }
        var settings = _options.Settings();
        if (!settings.Enabled)
        {
            await Reject(context, 503, L10n.T("Yapay zekâ bağlantısı kapalı (Etiket Tasarım → Ayarlar → Yapay Zekâ)."));
            return;
        }
        string auth = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !TokenEquals(auth[prefix.Length..].Trim(), settings.Token))
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await Reject(context, 401, L10n.T("Bağlantı anahtarı eksik ya da hatalı."));
            return;
        }
        await next(context);
    }

    private static bool IsAllowedHost(string host, int port) =>
        host == $"127.0.0.1:{port}" || host == $"localhost:{port}" || host == $"[::1]:{port}";

    private static bool TokenEquals(string given, string expected)
    {
        var a = Encoding.UTF8.GetBytes(given);
        var b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static Task Reject(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new { error = message }, JsonOptions));
    }

    private async Task<IResult> CallToolAsync(string name, HttpContext context)
    {
        JsonElement args;
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted).ConfigureAwait(false);
            args = document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : JsonDocument.Parse("{}").RootElement.Clone();
        }
        catch (JsonException)
        {
            args = JsonDocument.Parse("{}").RootElement.Clone();
            if (context.Request.ContentLength is > 0)
                return Results.Json(new { is_error = true, error_code = ToolErrorCodes.InvalidArgument, text = L10n.T("İstek gövdesi geçerli JSON değil.") }, JsonOptions, statusCode: 400);
        }
        string client = context.Request.Headers[ClientHeader].ToString();
        if (string.IsNullOrWhiteSpace(client)) client = "Yerel API istemcisi";
        client = client.Length > 80 ? client[..80] : client;
        string? session = context.Request.Headers[SessionHeader].ToString() is { Length: > 0 } s ? s : null;
        var toolContext = new ToolCallContext { ClientName = client, ClientId = session };
        var result = await _options.Tools.ExecuteAsync(name, args, toolContext, context.RequestAborted).ConfigureAwait(false);
        Record(client, name, !result.IsError);
        return Results.Json(new
        {
            is_error = result.IsError,
            error_code = result.ErrorCode,
            text = result.Text,
            structured = result.Structured,
            images = result.Images.Select(i => new { mime_type = i.MimeType, data = Convert.ToBase64String(i.Data), caption = i.Caption }),
        }, JsonOptions);
    }

    private static JsonArray ToolsJson(IToolExecutor tools) =>
        new(tools.Tools.Select(t => (JsonNode)new JsonObject
        {
            ["name"] = t.Name,
            ["title"] = t.Title,
            ["description"] = t.Description,
            ["input_schema"] = t.InputSchema.DeepClone(),
            ["read_only"] = t.ReadOnly,
            ["destructive"] = t.Destructive,
            ["idempotent"] = t.Idempotent,
        }).ToArray());

    public async Task StopAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _app.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}

public sealed class LocalApiException : Exception
{
    public LocalApiException(string message, Exception? inner = null) : base(message, inner) { }
}
