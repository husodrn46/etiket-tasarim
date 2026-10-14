using AkelEtiket.Core.Data;
using AkelEtiket.Gs1;
using AkelEtiket.Rendering;

namespace AkelEtiket.Editor.Printing;

/// <summary>Editörün kullandığı GS1 bağlamı (uygulamada varsayılan klasör; testler kendi bağlamını verebilir).</summary>
public static class Gs1Integration
{
    public static Gs1Context Context { get; set; } = Gs1Context.Default;
}

/// <summary>
/// Editörde kullanılan yerleşik alanlar: {SIRA}, {TARIH}, {SAAT}, GS1 alanları ({SSCC} önizleme değeriyle,
/// {GLN}, {GCP}) ve kalıcı sayaçlar ({SAYAC}, {SAYAC.Ad}). Önizleme kaynağı hiçbir zaman SSCC ayırmaz, sayaç tüketmez.
/// </summary>
public static class EditorFields
{
    public static IFieldValueSource BuiltIns(long sequence, DateTime now) =>
        new CompositeFieldSource(new BuiltInFieldSource(sequence, now), Gs1Integration.Context.PreviewFields, CounterIntegration.PreviewFields);
}

/// <summary>
/// Baskı sırasında SSCC muhasebesi: tasarımda {SSCC} varsa her etiket üretilmeden hemen önce numara ayrılır,
/// üretilemeyen etiketin numarası hemen iptal edilir, iş bitince kuyruğa giden etiketler "gönderildi",
/// kalanlar "kullanılmadı" olarak kaydedilir. Tasarımda {SSCC} yoksa hiçbir şey yapmaz.
/// </summary>
public sealed class SsccPrintTracker
{
    private readonly Gs1Context _context;
    private readonly SsccPrintSession? _session;
    private bool _completed;

    private SsccPrintTracker(Gs1Context context, SsccPrintSession? session)
    {
        _context = context;
        _session = session;
    }

    /// <summary>Etkin değil: numara ayırmaz.</summary>
    public static SsccPrintTracker Inactive(Gs1Context context) => new(context, null);

    /// <summary>
    /// Baskı için başlatır. Profil SSCC'ye hazır değilse <see cref="Gs1Exception"/> fırlatır (önek girilmemiş,
    /// doğrulanmamış, aralık hatalı...).
    /// </summary>
    public static SsccPrintTracker Begin(Gs1Context context, string jobDescription) => new(context, context.BeginPrint(jobDescription));

    public bool IsActive => _session is not null;

    /// <summary>Gönderildi olarak kaydedilen numara sayısı.</summary>
    public int SentCount => _session?.Sent.Count ?? 0;

    /// <summary>Kullanılmadı (iptal) olarak kaydedilen numara sayısı.</summary>
    public int VoidedCount => _session?.Voided.Count ?? 0;

    /// <summary>Gönderildi olarak kaydedilen numaralar (ayrılma sırasıyla; baskı geçmişi için).</summary>
    public IReadOnlyList<string> SentNumbers => _session?.Sent ?? [];

    /// <summary>Etiketi baskıya hazırlar: etkinse yeni SSCC ayırır ve etiketin alanlarının önüne ekler.</summary>
    public (PrintItem Item, string? Sscc) Prepare(PrintItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_session is null) return (item, null);
        string sscc = _session.Allocate(item.Describe());
        var fields = new CompositeFieldSource(_context.FieldsFor(sscc), item.Fields);
        return (item with { Fields = fields }, sscc);
    }

    /// <summary>Numara alınmış etiket üretilemedi: numara hemen "kullanılmadı" olur.</summary>
    public void ProductionFailed(string? sscc, string reason)
    {
        if (_session is null || sscc is null) return;
        _session.VoidOne(sscc, "Etiket üretilemedi: " + reason);
    }

    /// <summary>
    /// İşi kapatır: ilk <paramref name="spooledLabels"/> numara yazıcı kuyruğuna gönderildi, kalanlar kullanılmadı.
    /// Baskı raporuna eklenecek Türkçe özeti döndürür (etkin değilse null). Kayıt yazılamazsa <c>Failed</c> true olur.
    /// </summary>
    public (string Text, bool Failed)? Complete(int spooledLabels, bool canceled)
    {
        if (_session is null || _completed) return null;
        _completed = true;
        try
        {
            _session.Finish(spooledLabels, "Yazıcı kuyruğuna gönderildi",
                canceled ? "Baskı iptal edildi, etiket gönderilmedi" : "Etiket yazıcıya gönderilmedi");
        }
        catch (Exception ex) when (ex is Gs1Exception or IOException or UnauthorizedAccessException)
        {
            return ("SSCC kaydı güncellenemedi: " + ex.Message + " Numaralar ayrılmış durumda kaldı ve tekrar verilmeyecek.", true);
        }
        return (BuildSummary(SentCount, VoidedCount), false);
    }

    public static string BuildSummary(int sent, int voided)
    {
        string text = $"SSCC: {sent} numara yazıcı kuyruğuna gönderildi olarak kaydedildi";
        if (voided > 0) text += $", {voided} numara kullanılmadı (tekrar verilmeyecek)";
        return text + ". Fiziksel baskıyı etiketten kontrol edin.";
    }
}
