using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using AkelEtiket.Core.Automation;
using AkelEtiket.Design;
using AkelEtiket.Design.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AkelEtiket.LocalApi.Tests;

/// <summary>Yerel API: güvenlik denetimleri, araç uçları ve HTTP üzerinden MCP.</summary>
public sealed class LocalApiTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "akel-api-tests", Guid.NewGuid().ToString("N"));
    private readonly AiSettings _settings = new() { Enabled = true, Token = AiSettings.NewToken(), EnableHttpMcp = true };
    private LocalApiHost _host = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        var service = new DesignService(new DesignServiceOptions
        {
            SettingsProvider = () => _settings,
            TemplatesFolder = Path.Combine(_root, "Şablonlar"),
            SamplesFolder = Path.Combine(_root, "yok"),
            WorkingFolder = Path.Combine(_root, "Calisma"),
            ProposalsFolder = Path.Combine(_root, "Oneriler"),
            BackupFolder = Path.Combine(_root, "Yedekler"),
            ProductsFile = Path.Combine(_root, "urunler.json"),
            AuditFolder = Path.Combine(_root, "Gunluk"),
            Gs1 = new AkelEtiket.Gs1.Gs1Context(Path.Combine(_root, "GS1")),
        });
        var tools = new DesignToolset(service);
        _host = await LocalApiHost.StartAsync(new LocalApiOptions
        {
            Tools = tools.Registry,
            Settings = () => _settings,
            HostKind = ApiHostKind.Test,
            PortOverride = 0,
        });
        _http = new HttpClient { BaseAddress = new Uri(_host.Url) };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _host.DisposeAsync();
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string? body = null, bool auth = true)
    {
        var request = new HttpRequestMessage(method, path);
        if (auth) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Token);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }

    [Fact]
    public async Task MissingOrWrongToken_IsRejected()
    {
        using var noToken = await _http.SendAsync(Request(HttpMethod.Get, "/api/health", auth: false));
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        var wrong = Request(HttpMethod.Get, "/api/health", auth: false);
        wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AiSettings.NewToken());
        using var wrongToken = await _http.SendAsync(wrong);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongToken.StatusCode);
    }

    [Fact]
    public async Task ForeignHostHeader_IsRejected_DnsRebindingProtection()
    {
        var request = Request(HttpMethod.Get, "/api/health");
        request.Headers.Host = "kotu-site.example.com";
        using var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BrowserOrigin_IsRejected()
    {
        var request = Request(HttpMethod.Post, "/api/tools/list_designs", "{}");
        request.Headers.Add("Origin", "https://kotu-site.example.com");
        using var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DisabledConnection_Returns503()
    {
        _settings.Enabled = false;
        try
        {
            using var response = await _http.SendAsync(Request(HttpMethod.Get, "/api/health"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally
        {
            _settings.Enabled = true;
        }
    }

    [Fact]
    public async Task HealthAndToolList()
    {
        using var health = await _http.SendAsync(Request(HttpMethod.Get, "/api/health"));
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        var h = JsonNode.Parse(await health.Content.ReadAsStringAsync())!;
        Assert.Equal("LabelHarbor", h["product"]!.GetValue<string>());

        using var tools = await _http.SendAsync(Request(HttpMethod.Get, "/api/tools"));
        var list = JsonNode.Parse(await tools.Content.ReadAsStringAsync())!.AsArray();
        Assert.Contains(list, t => t!["name"]!.GetValue<string>() == "add_text");
        Assert.All(list, t => Assert.Equal("object", t!["input_schema"]!["type"]!.GetValue<string>()));
    }

    [Fact]
    public async Task ToolCalls_CreateEditAndPreview()
    {
        var created = await Call("create_design", """{"name":"API deneme"}""");
        string id = created["structured"]!["design_id"]!.GetValue<string>();
        var text = await Call("add_text", $$"""{"design_id":"{{id}}","text":"Merhaba","placement":"center","font_size_pt":16}""");
        Assert.False(text["is_error"]!.GetValue<bool>(), text.ToJsonString());
        var preview = await Call("render_preview", $$"""{"design_id":"{{id}}"}""");
        var image = preview["images"]!.AsArray().Single()!;
        Assert.Equal("image/png", image["mime_type"]!.GetValue<string>());
        Assert.True(Convert.FromBase64String(image["data"]!.GetValue<string>()).Length > 500);

        var error = await Call("add_text", $$"""{"design_id":"{{id}}","text":"x","x":1,"y":1,"expected_version":"r0"}""");
        Assert.True(error["is_error"]!.GetValue<bool>());
        Assert.Equal("VERSION_CONFLICT", error["error_code"]!.GetValue<string>());
    }

    [Fact]
    public async Task McpOverHttp_ListsAndCallsTools()
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(_host.Url + "/mcp"),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + _settings.Token },
            Name = "test",
        });
        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, t => t.Name == "create_design");
        Assert.Contains(tools, t => t.Name == "render_preview");

        var created = await client.CallToolAsync("create_design", new Dictionary<string, object?> { ["name"] = "MCP HTTP" });
        Assert.NotEqual(true, created.IsError);
        string id = created.StructuredContent!.Value.GetProperty("design_id").GetString()!;
        await client.CallToolAsync("add_barcode", new Dictionary<string, object?>
        {
            ["design_id"] = id, ["symbology"] = "qr_code", ["content"] = "https://example.com", ["placement"] = "center",
        });
        var preview = await client.CallToolAsync("render_preview", new Dictionary<string, object?> { ["design_id"] = id, ["annotate"] = true });
        Assert.Contains(preview.Content, c => c is ImageContentBlock);
        Assert.Contains(preview.Content, c => c is TextContentBlock);
    }

    [Fact]
    public async Task McpOverHttp_WithoutToken_IsRejected()
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(_host.Url + "/mcp"), Name = "test" });
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var client = await McpClient.CreateAsync(transport);
            await client.ListToolsAsync();
        });
    }

    private async Task<JsonNode> Call(string tool, string json)
    {
        using var response = await _http.SendAsync(Request(HttpMethod.Post, "/api/tools/" + tool, json));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }
}
