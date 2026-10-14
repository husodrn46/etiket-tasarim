namespace AkelEtiket.DataImport;

/// <summary>Aktarım sorununun önem derecesi.</summary>
public enum ImportIssueSeverity
{
    /// <summary>Bilgi (ör. sütun başlığı düzenlendi).</summary>
    Info,
    /// <summary>Uyarı: değer büyük olasılıkla bozuk ama satır okunabildi.</summary>
    Warning,
    /// <summary>Hata: değer kesin olarak güvenilmez (ör. Excel'in 15 hane sınırı).</summary>
    Error,
}

/// <summary>
/// Aktarım sırasında bulunan sorun. <see cref="RowNumber"/> kullanıcının Excel'de / metin
/// düzenleyicide gördüğü gerçek satır numarasıdır; 0 ise dosyanın geneline ilişkindir.
/// Sorunlar hiçbir zaman diğer satırların okunmasını engellemez.
/// </summary>
public sealed record ImportIssue(int RowNumber, string? Column, ImportIssueSeverity Severity, string Message)
{
    /// <summary>"Satır 15, "Barkod"" gibi konum metni.</summary>
    public string Location => RowNumber > 0
        ? Column is null ? $"Satır {RowNumber}" : $"Satır {RowNumber}, \"{Column}\""
        : Column is null ? "Dosya" : $"\"{Column}\" sütunu";

    /// <summary>Önem derecesinin Türkçe adı.</summary>
    public string SeverityText => Severity switch
    {
        ImportIssueSeverity.Error => "Hata",
        ImportIssueSeverity.Warning => "Uyarı",
        _ => "Bilgi",
    };

    /// <summary>Konum + mesaj: "Satır 15, "Barkod": …".</summary>
    public string FullText => $"{Location}: {Message}";

    public override string ToString() => FullText;
}
