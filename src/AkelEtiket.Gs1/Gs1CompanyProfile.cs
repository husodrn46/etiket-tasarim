using System.Text.Json.Serialization;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Gs1;

/// <summary>Kayıtlı GTIN'in ambalaj düzeyi.</summary>
public enum PackagingLevel
{
    /// <summary>Tüketici birimi (tekli ürün, kasada okutulan).</summary>
    ConsumerUnit,

    /// <summary>Koli (dış ambalaj).</summary>
    Carton,

    /// <summary>Palet.</summary>
    Pallet,

    /// <summary>Diğer.</summary>
    Other,
}

/// <summary>Firmanın GS1'e kayıtlı bir ürün numarası (GTIN).</summary>
public sealed class RegisteredGtin
{
    public string Gtin { get; set; } = "";

    /// <summary>Firma içi ürün/stok kodu (isteğe bağlı).</summary>
    public string ProductCode { get; set; } = "";

    public string Description { get; set; } = "";

    public PackagingLevel PackagingLevel { get; set; } = PackagingLevel.ConsumerUnit;

    public string Notes { get; set; } = "";

    public RegisteredGtin Clone() => new()
    {
        Gtin = Gtin,
        ProductCode = ProductCode,
        Description = Description,
        PackagingLevel = PackagingLevel,
        Notes = Notes,
    };
}

/// <summary>Profil denetimi bulgusunun önemi.</summary>
public enum Gs1IssueSeverity
{
    /// <summary>Bilgi/tutarlılık ipucu; hiçbir şeyi engellemez.</summary>
    Hint,

    /// <summary>Uyarı.</summary>
    Warning,

    /// <summary>Hata (ör. kontrol basamağı yanlış).</summary>
    Error,
}

/// <summary>Profil denetimi bulgusu.</summary>
public sealed record Gs1ProfileIssue(Gs1IssueSeverity Severity, string Field, string Message)
{
    public override string ToString() => Severity switch
    {
        Gs1IssueSeverity.Error => "HATA: " + Message,
        Gs1IssueSeverity.Warning => L10n.T("Uyarı: ") + Message,
        _ => "Bilgi: " + Message,
    };
}

/// <summary>
/// GS1 firma profili: GLN, firma öneki (GCP), SSCC ayarları ve kayıtlı GTIN'ler.
/// <para>
/// Kurallar: GLN bir yer/tüzel kişi numarasıdır; ürün barkodu (GTIN) ya da firma öneki <b>değildir</b>.
/// Firma öneki ve uzunluğu GLN'den <b>türetilmez</b>; yalnızca kullanıcının üyelik belgesinden girdiği değer kullanılır
/// ve "doğrulandı" işaretlenmeden SSCC üretilmez.
/// </para>
/// </summary>
public sealed class Gs1CompanyProfile
{
    /// <summary>Yeni kurulumda kimlik atanmaz; kullanıcı kendi GS1 belgesinden girer.</summary>
    public const string DefaultGln = "";

    public const string DefaultCompanyName = "";

    public int SchemaVersion { get; set; } = 1;

    public string CompanyName { get; set; } = DefaultCompanyName;

    /// <summary>Küresel Lokasyon Numarası (13 hane).</summary>
    public string Gln { get; set; } = DefaultGln;

    /// <summary>GS1 firma öneki (GCP), 6–12 hane. Boş = girilmedi.</summary>
    public string GcpPrefix { get; set; } = "";

    /// <summary>Kullanıcı, firma önekini GS1 üyelik belgesinden doğruladığını işaretledi mi?</summary>
    public bool GcpVerified { get; set; }

    public DateTimeOffset? GcpVerifiedAt { get; set; }

    /// <summary>Doğrulama notu (ör. "GS1 Türkiye üyelik belgesi, 2026").</summary>
    public string GcpVerificationNote { get; set; } = "";

    /// <summary>SSCC uzantı basamağı (0–9). Firma içinde serileri ayırmak için kullanılabilir.</summary>
    public int SsccExtensionDigit { get; set; }

    /// <summary>
    /// Bu bilgisayarın kullanacağı ilk seri referansı (isteğe bağlı). Birden fazla bilgisayar SSCC basacaksa
    /// her birine çakışmayan bir aralık verilir.
    /// </summary>
    public long? SerialStart { get; set; }

    /// <summary>Bu bilgisayarın kullanabileceği son seri referansı (isteğe bağlı).</summary>
    public long? SerialEnd { get; set; }

    public List<RegisteredGtin> RegisteredGtins { get; set; } = [];

    public string Notes { get; set; } = "";

    /// <summary>Temizlenmiş firma öneki.</summary>
    [JsonIgnore]
    public string NormalizedGcp => Gs1Identifiers.Normalize(GcpPrefix);

    /// <summary>SSCC üretimine hazır mı? (Geçerli önek + doğrulama işareti + geçerli uzantı ve aralık.)</summary>
    [JsonIgnore]
    public bool IsReadyForSscc => GetSsccBlockers().Count == 0;

    /// <summary>SSCC üretimini engelleyen nedenler (Türkçe). Boşsa üretim yapılabilir.</summary>
    public IReadOnlyList<string> GetSsccBlockers()
    {
        var blockers = new List<string>();
        if (!Gs1Identifiers.IsValidGcpFormat(GcpPrefix, out string? gcpError))
            blockers.Add(gcpError!);
        else if (!GcpVerified)
            blockers.Add(L10n.T("Firma öneki GS1 üyelik belgesinden doğrulanmadı. GS1 Firma Profili'nde \"Üyelik belgesinden doğrulandı\" işaretlenmeden SSCC üretilemez."));
        if (SsccExtensionDigit is < 0 or > 9)
            blockers.Add(L10n.T("SSCC uzantı basamağı 0–9 olmalı."));
        if (blockers.Count == 0 && GetRangeError() is { } rangeError)
            blockers.Add(rangeError);
        return blockers;
    }

    /// <summary>Seri referansı için geçerli aralık (önek uzunluğuna göre); önek geçersizse null.</summary>
    public (long Min, long Max)? GetSerialRange()
    {
        if (!Gs1Identifiers.IsValidGcpFormat(GcpPrefix, out _)) return null;
        long max = Gs1Identifiers.MaxSerial(Gs1Identifiers.SsccSerialLength(NormalizedGcp.Length));
        // Seri 0 hiç verilmez: önizleme değerleri için ayrılmıştır (gerçek numarayla çakışmasın).
        long start = SerialStart ?? 1;
        long end = SerialEnd ?? max;
        return (start, end);
    }

    private string? GetRangeError()
    {
        if (!Gs1Identifiers.IsValidGcpFormat(GcpPrefix, out _)) return null;
        long max = Gs1Identifiers.MaxSerial(Gs1Identifiers.SsccSerialLength(NormalizedGcp.Length));
        if (SerialStart is { } s && (s < 1 || s > max))
            return L10n.T($"Seri başlangıcı 1–{max:N0} aralığında olmalı.");
        if (SerialEnd is { } e && (e < 1 || e > max))
            return L10n.T($"Seri sonu 1–{max:N0} aralığında olmalı.");
        if (SerialStart is { } a && SerialEnd is { } b && a > b)
            return L10n.T("Seri başlangıcı, seri sonundan büyük olamaz.");
        return null;
    }

    /// <summary>Profilin tamamını denetler (hatalar, uyarılar ve tutarlılık ipuçları).</summary>
    public IReadOnlyList<Gs1ProfileIssue> Validate()
    {
        var issues = new List<Gs1ProfileIssue>();
        if (string.IsNullOrWhiteSpace(CompanyName))
            issues.Add(new(Gs1IssueSeverity.Warning, nameof(CompanyName), L10n.T("Firma adı boş.")));

        var gln = Gs1Identifiers.ValidateGln(Gln);
        if (!gln.IsValid)
            issues.Add(new(string.IsNullOrWhiteSpace(Gln) ? Gs1IssueSeverity.Warning : Gs1IssueSeverity.Error,
                nameof(Gln), string.IsNullOrWhiteSpace(Gln) ? L10n.T("GLN girilmedi; lokasyon numarası atanmaz.") : "GLN: " + gln.Error));

        bool gcpOk = false;
        if (Gs1Identifiers.Normalize(GcpPrefix).Length == 0)
        {
            issues.Add(new(Gs1IssueSeverity.Warning, nameof(GcpPrefix),
                L10n.T("Firma öneki (GCP) girilmedi. SSCC üretmek için üyelik belgenizdeki öneki yazın; GLN'den tahmin edilmez.")));
        }
        else if (!Gs1Identifiers.IsValidGcpFormat(GcpPrefix, out string? gcpError))
        {
            issues.Add(new(Gs1IssueSeverity.Error, nameof(GcpPrefix), gcpError!));
        }
        else
        {
            gcpOk = true;
            if (!GcpVerified)
                issues.Add(new(Gs1IssueSeverity.Warning, nameof(GcpVerified),
                    L10n.T("Firma öneki henüz \"üyelik belgesinden doğrulandı\" olarak işaretlenmedi; SSCC üretimi kapalı.")));
            if (gln.IsValid && !Gs1Identifiers.StartsWithPrefix(gln.Normalized, NormalizedGcp))
                issues.Add(new(Gs1IssueSeverity.Hint, nameof(Gln),
                    L10n.T("GLN bu firma önekiyle başlamıyor. GLN farklı bir önekten verilmiş olabilir; bu bir hata değildir, yalnızca kontrol edin.")));
        }

        if (SsccExtensionDigit is < 0 or > 9)
            issues.Add(new(Gs1IssueSeverity.Error, nameof(SsccExtensionDigit), L10n.T("SSCC uzantı basamağı 0–9 olmalı.")));
        if (gcpOk && GetRangeError() is { } rangeError)
            issues.Add(new(Gs1IssueSeverity.Error, nameof(SerialStart), rangeError));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < RegisteredGtins.Count; i++)
        {
            var item = RegisteredGtins[i];
            string label = string.IsNullOrWhiteSpace(item.Description) ? $"{i + 1}. GTIN" : $"{i + 1}. GTIN ({item.Description})";
            var result = Gs1Identifiers.ValidateGtin(item.Gtin);
            if (!result.IsValid)
            {
                issues.Add(new(Gs1IssueSeverity.Error, nameof(RegisteredGtins), $"{label}: {result.Error}"));
                continue;
            }
            string key = result.Normalized.PadLeft(14, '0');
            if (!seen.Add(key))
                issues.Add(new(Gs1IssueSeverity.Error, nameof(RegisteredGtins), $"{label}: {result.Normalized} listede birden fazla kez var."));
            if (gln.IsValid && key.EndsWith(gln.Normalized, StringComparison.Ordinal) && result.Normalized.Length >= 13)
                issues.Add(new(Gs1IssueSeverity.Warning, nameof(RegisteredGtins),
                    L10n.T($"{label}: GLN ile aynı numara. GLN bir yer numarasıdır, ürün numarası (GTIN) olarak kullanılmamalı.")));
            if (gcpOk && result.Type is Gs1KeyType.Gtin13 or Gs1KeyType.Gtin14 && !GtinUsesPrefix(result.Normalized, NormalizedGcp))
                issues.Add(new(Gs1IssueSeverity.Hint, nameof(RegisteredGtins),
                    L10n.T($"{label}: firma önekinizle başlamıyor (başka bir firmanın numarası ya da farklı önek olabilir).")));
        }
        return issues;
    }

    /// <summary>GTIN-13 önekle, GTIN-14 ise gösterge basamağından sonra önekle başlar.</summary>
    public static bool GtinUsesPrefix(string gtin, string gcp)
    {
        string value = Gs1Identifiers.Normalize(gtin);
        string prefix = Gs1Identifiers.Normalize(gcp);
        if (prefix.Length == 0) return false;
        return value.Length switch
        {
            13 => value.StartsWith(prefix, StringComparison.Ordinal),
            14 => value.AsSpan(1).StartsWith(prefix, StringComparison.Ordinal),
            _ => false,
        };
    }

    /// <summary>Derin kopya (pencere düzenlemesi için).</summary>
    public Gs1CompanyProfile Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        CompanyName = CompanyName,
        Gln = Gln,
        GcpPrefix = GcpPrefix,
        GcpVerified = GcpVerified,
        GcpVerifiedAt = GcpVerifiedAt,
        GcpVerificationNote = GcpVerificationNote,
        SsccExtensionDigit = SsccExtensionDigit,
        SerialStart = SerialStart,
        SerialEnd = SerialEnd,
        RegisteredGtins = RegisteredGtins.Select(g => g.Clone()).ToList(),
        Notes = Notes,
    };

    /// <summary>Okunan dosyadaki eksik/null alanları güvenli değerlere çeker.</summary>
    public void Normalize()
    {
        CompanyName ??= "";
        Gln ??= "";
        GcpPrefix = Gs1Identifiers.Normalize(GcpPrefix);
        GcpVerificationNote ??= "";
        RegisteredGtins ??= [];
        RegisteredGtins.RemoveAll(g => g is null);
        foreach (var g in RegisteredGtins)
        {
            g.Gtin = Gs1Identifiers.Normalize(g.Gtin);
            g.ProductCode ??= "";
            g.Description ??= "";
            g.Notes ??= "";
        }
        Notes ??= "";
        // Doğrulanmış işaret yalnızca geçerli bir önekle anlamlıdır.
        if (GcpVerified && !Gs1Identifiers.IsValidGcpFormat(GcpPrefix, out _)) GcpVerified = false;
    }
}
