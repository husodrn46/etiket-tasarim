using System.Text.Json;
using System.Text.Json.Nodes;
using AkelEtiket.Core.Automation;
using AkelEtiket.Design;
using AkelEtiket.Design.Tools;
using SkiaSharp;

namespace AkelEtiket.Design.Tests;

/// <summary>Geçici klasörlerle çalışan tasarım servisi + araç kaydı (gerçek kullanıcı klasörlerine dokunmaz).</summary>
public sealed class DesignTestHost : IDisposable
{
    public DesignTestHost(AiChangeMode mode = AiChangeMode.Approval)
    {
        Root = Path.Combine(Path.GetTempPath(), "akel-design-tests", Guid.NewGuid().ToString("N"));
        Templates = Path.Combine(Root, "Belgeler", "Şablonlar");
        Extra = Path.Combine(Root, "Gorseller");
        Outside = Path.Combine(Root, "Disari");
        Directory.CreateDirectory(Templates);
        Directory.CreateDirectory(Extra);
        Directory.CreateDirectory(Outside);
        Settings = new AiSettings { Enabled = true, ChangeMode = mode, AllowedFolders = [Extra] };
        Service = new DesignService(new DesignServiceOptions
        {
            SettingsProvider = () => Settings,
            LogoProvider = () => MakePng(64, 32),
            TemplatesFolder = Templates,
            SamplesFolder = Path.Combine(Root, "yok"),
            WorkingFolder = Path.Combine(Root, "Calisma"),
            ProposalsFolder = Path.Combine(Root, "Oneriler"),
            BackupFolder = Path.Combine(Root, "Yedekler"),
            ProductsFile = Path.Combine(Root, "Urunler", "urunler.json"),
            AuditFolder = Path.Combine(Root, "Gunluk"),
            Gs1 = new AkelEtiket.Gs1.Gs1Context(Path.Combine(Root, "GS1")),
        });
        Tools = new DesignToolset(Service);
    }

    public string Root { get; }
    public string Templates { get; }

    /// <summary>Ayarlarda izin verilen ek klasör ("Gorseller" takma adıyla).</summary>
    public string Extra { get; }

    /// <summary>İzinli olmayan klasör.</summary>
    public string Outside { get; }

    public AiSettings Settings { get; }
    public DesignService Service { get; }
    public DesignToolset Tools { get; }

    public ToolCallContext Agent { get; } = new() { ClientName = "Test ajanı" };

    /// <summary>Aracı çalıştırır; hata dönerse testi düşürür.</summary>
    public async Task<JsonObject> Call(string tool, object args, ToolCallContext? context = null)
    {
        var result = await CallRaw(tool, args, context);
        Assert.False(result.IsError, $"{tool} başarısız: {result.Text}");
        return (JsonObject)result.Structured!;
    }

    public async Task<ToolResult> CallRaw(string tool, object args, ToolCallContext? context = null)
    {
        var json = args is JsonNode node ? node.ToJsonString() : JsonSerializer.Serialize(args);
        using var doc = JsonDocument.Parse(json);
        return await Tools.Registry.ExecuteAsync(tool, doc.RootElement.Clone(), context ?? Agent, CancellationToken.None);
    }

    public async Task<string> ExpectError(string tool, object args, string code, ToolCallContext? context = null)
    {
        var result = await CallRaw(tool, args, context);
        Assert.True(result.IsError, $"{tool} hata vermeliydi: {result.Text}");
        Assert.Equal(code, result.ErrorCode);
        return result.Text;
    }

    /// <summary>Basit bir PNG oluşturur (logo benzeri koyu daire veya fotoğraf benzeri degrade).</summary>
    public static byte[] MakePng(int width, int height, bool photo = false)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        if (photo)
        {
            using var paint = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height),
                    [SKColors.Black, SKColors.LightGray, SKColors.DarkGray], SKShaderTileMode.Clamp),
            };
            canvas.DrawRect(0, 0, width, height, paint);
        }
        else
        {
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
