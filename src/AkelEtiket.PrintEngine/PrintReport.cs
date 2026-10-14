using System.Globalization;
using System.Text;

namespace AkelEtiket.PrintEngine;

/// <summary>Baskı ilerlemesi.</summary>
public sealed class PrintProgress
{
    public int Done { get; init; }
    public int Total { get; init; }

    /// <summary>Aşamanın Türkçe adı (ör. "Yazıcı kuyruğuna gönderiliyor").</summary>
    public string Stage { get; init; } = "";

    public double Fraction => Total <= 0 ? 0 : Math.Clamp((double)Done / Total, 0, 1);

    public override string ToString() => Total > 0 ? $"{Stage} ({Done}/{Total})" : Stage;
}

/// <summary>
/// Baskı raporu. Aşamalar AYRI raporlanır ve hiçbiri diğerinin yerine geçmez:
/// 1) baskı verisi üretildi, 2) Windows kuyruğuna gönderildi, 3) kuyruk sonucu,
/// 4) fiziksel baskı — uygulama bunu doğrulayamaz, kullanıcı yazıcıdan kontrol etmelidir.
/// </summary>
public sealed class PrintReport
{
    public const string PhysicalVerificationText =
        "Fiziksel baskı uygulama tarafından doğrulanamaz: etiketin çıktığını, ölçünün ve barkod okunurluğunun doğru olduğunu yazıcıdan kontrol edin.";

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public PrintMethod Method { get; init; }
    public string PrinterName { get; init; } = "";

    // 1) Veri üretimi
    public bool DataGenerated { get; set; }

    /// <summary>Üretilen farklı etiket görüntüsü sayısı.</summary>
    public int LabelsGenerated { get; set; }

    /// <summary>İstenen toplam fiziksel etiket (kopyalar dahil).</summary>
    public int CopiesTotal { get; set; }

    public long BytesGenerated { get; set; }

    // 2) Kuyruğa gönderim
    public bool Spooled { get; set; }

    /// <summary>Kuyruğa eksiksiz gönderilen etiket görüntüsü sayısı.</summary>
    public int LabelsSpooled { get; set; }

    public List<int> JobIds { get; } = [];

    // 3) Kuyruk sonucu
    public SpoolOutcome? QueueOutcome { get; set; }
    public string QueueMessage { get; set; } = "";

    // 4) Fiziksel doğrulama
    public string PhysicalVerification => PhysicalVerificationText;

    public bool Canceled { get; set; }

    /// <summary>Dosyaya yazdırıldıysa çıktı dosyası.</summary>
    public string? OutputFile { get; set; }

    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];

    /// <summary>Veri üretildi, kuyruğa gitti ve kuyruk iletimi bildirdi (fiziksel baskı yine kontrol edilmeli).</summary>
    public bool Success =>
        Errors.Count == 0 && !Canceled && DataGenerated && Spooled &&
        QueueOutcome is SpoolOutcome.LeftQueue or SpoolOutcome.Printed;

    /// <summary>Türkçe çok satırlı özet.</summary>
    public string Summary
    {
        get
        {
            var sb = new StringBuilder();
            string method = Method == PrintMethod.TsplRaw ? "TSPL (RAW)" : "Windows sürücüsü";
            sb.AppendLine($"Yazıcı: {(string.IsNullOrWhiteSpace(PrinterName) ? "-" : PrinterName)} — yöntem: {method}");

            sb.AppendLine(DataGenerated
                ? $"1) Baskı verisi üretildi: {LabelsGenerated} etiket görüntüsü, toplam {CopiesTotal} adet, {BytesGenerated.ToString("N0", Turkish)} bayt."
                : "1) Baskı verisi üretilmedi.");

            if (Spooled)
            {
                string jobs = JobIds.Count > 0 ? $" (iş no: {string.Join(", ", JobIds)})" : "";
                sb.AppendLine($"2) Windows yazdırma kuyruğuna gönderildi: {LabelsSpooled} etiket görüntüsü{jobs}.");
            }
            else
            {
                sb.AppendLine("2) Windows yazdırma kuyruğuna gönderilmedi.");
            }

            sb.AppendLine(QueueOutcome is null
                ? "3) Kuyruk sonucu: bilinmiyor."
                : $"3) Kuyruk sonucu: {QueueMessage}");
            if (OutputFile is not null) sb.AppendLine($"   Çıktı dosyası: {OutputFile}");
            sb.AppendLine("4) " + PhysicalVerificationText);

            if (Canceled) sb.AppendLine("İşlem kullanıcı tarafından iptal edildi.");
            foreach (var error in Errors) sb.AppendLine("HATA: " + error);
            foreach (var warning in Warnings.Distinct()) sb.AppendLine("Uyarı: " + warning);
            return sb.ToString().TrimEnd();
        }
    }

    public override string ToString() => Summary;
}
