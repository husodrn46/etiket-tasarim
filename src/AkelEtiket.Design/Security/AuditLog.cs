using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using AkelEtiket.Core.Automation;

namespace AkelEtiket.Design.Security;

/// <summary>Yapay zekâ işlem günlüğünün bir satırı.</summary>
public sealed record AuditEntry(
    DateTimeOffset Time,
    string Client,
    string Tool,
    string? DesignId,
    bool Ok,
    string? ErrorCode,
    string? Version,
    long DurationMs,
    string? Note);

/// <summary>
/// Her araç çağrısını günlüğe yazar (%LOCALAPPDATA%\Etiket Tasarım\YapayZeka\Gunluk\islemler-YYYY-MM-DD.jsonl).
/// Girdilerin tamamı yazılmaz (kişisel/dış veri barındırabilir); yalnızca araç, tasarım ve sonuç.
/// Son kayıtlar bellekte de tutulur (uygulamadaki "Yapay zekâ işlem geçmişi" için).
/// </summary>
public sealed class AuditLog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _fileLock = new();
    private readonly ConcurrentQueue<AuditEntry> _recent = new();

    public AuditLog(string? folder = null)
    {
        Folder = folder ?? Path.Combine(AiSettingsStore.Folder, "Gunluk");
    }

    public string Folder { get; }

    public event EventHandler<AuditEntry>? Written;

    public IReadOnlyList<AuditEntry> Recent => _recent.ToArray().Reverse().ToList();

    public void Write(AuditEntry entry)
    {
        _recent.Enqueue(entry);
        while (_recent.Count > 300 && _recent.TryDequeue(out _))
        {
        }
        try
        {
            lock (_fileLock)
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, $"islemler-{entry.Time:yyyy-MM-dd}.jsonl");
                File.AppendAllText(path, JsonSerializer.Serialize(entry, Options) + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Günlük yazılamazsa işlem engellenmez.
        }
        Written?.Invoke(this, entry);
    }
}
