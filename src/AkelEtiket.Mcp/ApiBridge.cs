using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AkelEtiket.Core.Automation;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace AkelEtiket.Mcp;

/// <summary>
/// MCP (stdio) ↔ Etiket Tasarım yerel API köprüsü. Araçların kendisi yerel API'dedir (masaüstü açıksa masaüstünde,
/// değilse Worker'da); köprü yalnızca istekleri iletir. Barındırıcı yoksa Worker'ı penceresiz başlatır.
/// </summary>
public sealed class ApiBridge : IDisposable
{
    /// <summary>Geliştirme/testte Worker yolunu değiştirmek için ortam değişkeni.</summary>
    public const string WorkerPathVariable = "AKELETIKET2_WORKER";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly string _session = Guid.NewGuid().ToString("N")[..12];
    private readonly SemaphoreSlim _hostGate = new(1, 1);
    private ApiEndpointInfo? _endpoint;
    private (DateTime At, ListToolsResult Result)? _tools;

    public async Task<ListToolsResult> ListToolsAsync(CancellationToken cancellationToken)
    {
        if (_tools is { } cached && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(30)) return cached.Result;
        var json = await SendAsync(HttpMethod.Get, "/api/tools", null, null, cancellationToken).ConfigureAwait(false);
        var tools = new List<Tool>();
        foreach (var node in json.AsArray())
        {
            if (node is not JsonObject t) continue;
            tools.Add(new Tool
            {
                Name = t["name"]!.GetValue<string>(),
                Title = t["title"]?.GetValue<string>(),
                Description = t["description"]?.GetValue<string>(),
                InputSchema = JsonSerializer.SerializeToElement(t["input_schema"]),
                Annotations = new ToolAnnotations
                {
                    Title = t["title"]?.GetValue<string>(),
                    ReadOnlyHint = t["read_only"]?.GetValue<bool>() ?? false,
                    DestructiveHint = t["destructive"]?.GetValue<bool>() ?? false,
                    IdempotentHint = t["idempotent"]?.GetValue<bool>() ?? false,
                    OpenWorldHint = false,
                },
            });
        }
        var result = new ListToolsResult { Tools = tools };
        _tools = (DateTime.UtcNow, result);
        return result;
    }

    public async Task<CallToolResult> CallToolAsync(string name, IDictionary<string, JsonElement>? arguments, string? clientName,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject();
        if (arguments is not null)
        {
            foreach (var (key, value) in arguments) body[key] = JsonNode.Parse(value.GetRawText());
        }
        string client = string.IsNullOrWhiteSpace(clientName) ? "MCP istemcisi" : clientName.Trim() + " (MCP)";
        var json = await SendAsync(HttpMethod.Post, "/api/tools/" + Uri.EscapeDataString(name), body, client, cancellationToken).ConfigureAwait(false);
        var content = new List<ContentBlock> { new TextContentBlock { Text = json["text"]?.GetValue<string>() ?? "" } };
        if (json["images"] is JsonArray images)
        {
            foreach (var image in images.OfType<JsonObject>())
            {
                string? data = image["data"]?.GetValue<string>();
                if (data is null) continue;
                content.Add(ImageContentBlock.FromBytes(Convert.FromBase64String(data), image["mime_type"]?.GetValue<string>() ?? "image/png"));
            }
        }
        return new CallToolResult
        {
            Content = content,
            StructuredContent = json["structured"] is JsonObject structured ? JsonSerializer.SerializeToElement(structured) : null,
            IsError = json["is_error"]?.GetValue<bool>() ?? false,
        };
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, JsonObject? body, string? client, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            var endpoint = await EnsureHostAsync(cancellationToken).ConfigureAwait(false);
            var settings = AiSettingsStore.Load();
            using var request = new HttpRequestMessage(method, endpoint.Url + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
            request.Headers.Add("X-Akel-Session", _session);
            if (client is not null) request.Headers.Add("X-Akel-Client", Ascii(client));
            if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            try
            {
                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    string message = TryError(text) ?? $"HTTP {(int)response.StatusCode}";
                    throw new McpException($"LabelHarbor yerel API isteği reddetti: {message}");
                }
                return JsonNode.Parse(text) ?? throw new McpException("Yerel API boş yanıt verdi.");
            }
            catch (HttpRequestException) when (attempt == 0)
            {
                // Barındırıcı kapanmış olabilir (ör. masaüstü kapatıldı): yeniden bul/başlat ve bir kez daha dene.
                _endpoint = null;
                _tools = null;
            }
            catch (HttpRequestException ex)
            {
                throw new McpException("LabelHarbor yerel API'sine ulaşılamadı: " + ex.Message);
            }
        }
    }

    private async Task<ApiEndpointInfo> EnsureHostAsync(CancellationToken cancellationToken)
    {
        await _hostGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = AiSettingsStore.Load();
            if (!settings.Enabled)
                throw new McpException("LabelHarbor'de yapay zekâ bağlantısı kapalı. Uygulamada Ayarlar → Yapay Zekâ → " +
                                       "\"Yapay zekâ bağlantısına izin ver\" seçeneğini açın.");
            if (_endpoint is not null && ApiDiscovery.TryRead() is { } current && current.ProcessId == _endpoint.ProcessId) return _endpoint;

            var info = ApiDiscovery.TryRead();
            if (info is not null && await IsHealthyAsync(info, settings.Token, cancellationToken).ConfigureAwait(false))
                return _endpoint = info;

            StartWorker();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                info = ApiDiscovery.TryRead();
                if (info is not null && await IsHealthyAsync(info, settings.Token, cancellationToken).ConfigureAwait(false))
                    return _endpoint = info;
            }
            throw new McpException("LabelHarbor arka plan hizmeti (Worker) başlatılamadı. Uygulamayı açıp yeniden deneyin.");
        }
        finally
        {
            _hostGate.Release();
        }
    }

    private async Task<bool> IsHealthyAsync(ApiEndpointInfo info, string token, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, info.Url + "/api/health");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private static void StartWorker()
    {
        string path = Environment.GetEnvironmentVariable(WorkerPathVariable) is { Length: > 0 } custom
            ? custom
            : Path.Combine(AppContext.BaseDirectory, "AkelEtiket.Worker.exe");
        if (!File.Exists(path)) throw new McpException($"Worker bulunamadı: {path}");
        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path)!,
        };
        Process.Start(start)?.Dispose();
    }

    private static string? TryError(string text)
    {
        try
        {
            return JsonNode.Parse(text)?["error"]?.GetValue<string>();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>HTTP başlığı yalnızca ASCII taşır: Türkçe karakterleri sadeleştir.</summary>
    private static string Ascii(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            sb.Append(ch switch
            {
                'ı' => 'i', 'İ' => 'I', 'ğ' => 'g', 'Ğ' => 'G', 'ü' => 'u', 'Ü' => 'U', 'ş' => 's', 'Ş' => 'S', 'ö' => 'o', 'Ö' => 'O',
                'ç' => 'c', 'Ç' => 'C', _ when ch < 128 && !char.IsControl(ch) => ch, _ => '?',
            });
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        _http.Dispose();
        _hostGate.Dispose();
    }
}
