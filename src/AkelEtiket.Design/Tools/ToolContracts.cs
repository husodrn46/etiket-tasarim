using System.Text.Json;
using System.Text.Json.Nodes;

namespace AkelEtiket.Design.Tools;

/// <summary>
/// Bir tasarım aracının tanımı. Aynı tanım MCP sunucusunda (tools/list), yerel API'de (GET /api/tools) ve
/// uygulama içi asistanda (model araç listesi) kullanılır.
/// </summary>
public sealed class ToolDefinition
{
    /// <summary>Araç adı (snake_case, ör. <c>add_text</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Kısa Türkçe başlık.</summary>
    public required string Title { get; init; }

    /// <summary>Modelin aracı doğru kullanması için açıklama.</summary>
    public required string Description { get; init; }

    /// <summary>Girdi JSON şeması (type: object).</summary>
    public required JsonObject InputSchema { get; init; }

    /// <summary>Yalnızca okur, hiçbir şeyi değiştirmez.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>Var olan içeriği kaldırabilir (ör. öğe silme).</summary>
    public bool Destructive { get; init; }

    /// <summary>Aynı girdiyle tekrar çağrılması ek etki yaratmaz.</summary>
    public bool Idempotent { get; init; }
}

/// <summary>Araç sonucundaki görüntü (ör. baskı çözünürlüğünde önizleme).</summary>
public sealed record ToolImage(string MimeType, byte[] Data, string? Caption = null);

/// <summary>Araç sonucu. <see cref="Text"/> her zaman doludur (özet + JSON); makine için <see cref="Structured"/>.</summary>
public sealed class ToolResult
{
    public bool IsError { get; init; }

    /// <summary>Hata kodu (ör. VERSION_CONFLICT, NOT_FOUND, INVALID_ARGUMENT, PERMISSION_DENIED).</summary>
    public string? ErrorCode { get; init; }

    public required string Text { get; init; }

    public JsonNode? Structured { get; init; }

    public IReadOnlyList<ToolImage> Images { get; init; } = [];
}

/// <summary>Bir araç çağrısının bağlamı: kim çağırıyor, hangi kapsamda.</summary>
public sealed class ToolCallContext
{
    /// <summary>İstemcinin görünen adı (ör. "Claude Desktop", "Uygulama içi asistan"). Günlüğe ve önerilere yazılır.</summary>
    public required string ClientName { get; init; }

    /// <summary>Bağlantı/oturum kimliği; taslakların sahipliği için kullanılır.</summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// "Seçili nesneler" kapsamı: doluysa yalnızca bu öğeler (ve bu çağrılarda yeni eklenenler) değiştirilebilir.
    /// </summary>
    public IReadOnlySet<string>? AllowedElementIds { get; init; }

    /// <summary>Doluysa tasarım değiştiren bütün çağrılar bu taslağa yönlendirilir (uygulama içi asistan).</summary>
    public string? ForcedDraftId { get; init; }
}

/// <summary>Araçları adla çalıştıran yürütücü (yerel API, MCP ve uygulama içi asistan bunu kullanır).</summary>
public interface IToolExecutor
{
    IReadOnlyList<ToolDefinition> Tools { get; }

    Task<ToolResult> ExecuteAsync(string name, JsonElement arguments, ToolCallContext context, CancellationToken cancellationToken);
}
