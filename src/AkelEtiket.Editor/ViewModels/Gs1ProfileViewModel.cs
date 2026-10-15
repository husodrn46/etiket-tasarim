using System.Collections.ObjectModel;
using System.Globalization;
using AkelEtiket.DataImport;
using AkelEtiket.Editor.Services;
using AkelEtiket.Gs1;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>Kayıtlı GTIN listesindeki düzenlenebilir satır.</summary>
public sealed partial class RegisteredGtinRow : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsValid))]
    public partial string Gtin { get; set; } = "";

    [ObservableProperty] public partial string ProductCode { get; set; } = "";
    [ObservableProperty] public partial string Description { get; set; } = "";
    [ObservableProperty] public partial PackagingLevel PackagingLevel { get; set; } = PackagingLevel.ConsumerUnit;
    [ObservableProperty] public partial string Notes { get; set; } = "";

    public bool IsValid => Gs1Identifiers.ValidateGtin(Gtin).IsValid;

    public string StatusText
    {
        get
        {
            var result = Gs1Identifiers.ValidateGtin(Gtin);
            return result.IsValid ? L10n.T($"Geçerli ({TypeName(result.Type)})") : result.Error ?? L10n.T("Geçersiz");
        }
    }

    private static string TypeName(Gs1KeyType? type) => type switch
    {
        Gs1KeyType.Gtin8 => "GTIN-8",
        Gs1KeyType.Gtin12 => "GTIN-12",
        Gs1KeyType.Gtin13 => "GTIN-13",
        Gs1KeyType.Gtin14 => "GTIN-14",
        _ => "",
    };

    public static RegisteredGtinRow From(RegisteredGtin model) => new()
    {
        Gtin = model.Gtin,
        ProductCode = model.ProductCode,
        Description = model.Description,
        PackagingLevel = model.PackagingLevel,
        Notes = model.Notes,
    };

    public RegisteredGtin ToModel() => new()
    {
        Gtin = Gs1Identifiers.Normalize(Gtin),
        ProductCode = (ProductCode ?? "").Trim(),
        Description = (Description ?? "").Trim(),
        PackagingLevel = PackagingLevel,
        Notes = (Notes ?? "").Trim(),
    };
}

/// <summary>
/// GS1 Firma Profili penceresi: GLN, firma öneki (GCP) ve doğrulama işareti, SSCC ayarları, kayıtlı GTIN'ler,
/// son SSCC kayıtları ve AI listesi. Değişiklikler "Kaydet" ile diske yazılır.
/// </summary>
public sealed partial class Gs1ProfileViewModel : ObservableObject
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly Gs1Context _context;
    private readonly IDialogService _dialogs;
    private readonly string _initialGcp;
    private readonly bool _initialVerified;
    private bool _loading;

    public Gs1ProfileViewModel(Gs1Context context, IDialogService dialogs)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        var profile = context.Profile.Clone();
        _loading = true;
        CompanyName = profile.CompanyName;
        Gln = profile.Gln;
        GcpPrefix = profile.GcpPrefix;
        GcpVerified = profile.GcpVerified;
        GcpVerifiedAt = profile.GcpVerifiedAt;
        GcpVerificationNote = profile.GcpVerificationNote;
        SsccExtensionDigit = profile.SsccExtensionDigit;
        SerialStartText = profile.SerialStart?.ToString(CultureInfo.InvariantCulture) ?? "";
        SerialEndText = profile.SerialEnd?.ToString(CultureInfo.InvariantCulture) ?? "";
        Notes = profile.Notes;
        foreach (var gtin in profile.RegisteredGtins) AddRow(RegisteredGtinRow.From(gtin));
        _loading = false;
        _initialGcp = profile.NormalizedGcp;
        _initialVerified = profile.GcpVerified;
        LoadProblem = context.LastLoadProblem;
        RefreshIssues();
        RefreshRegistry();
    }

    public IReadOnlyList<int> ExtensionDigits { get; } = Enumerable.Range(0, 10).ToList();

    /// <summary>Profil ve SSCC kayıtlarının klasörü.</summary>
    public string DataFolder => _context.Store.Folder;

    public IReadOnlyList<KeyValuePair<PackagingLevel, string>> PackagingChoices { get; } =
    [
        new(PackagingLevel.ConsumerUnit, L10n.T("Tüketici birimi")),
        new(PackagingLevel.Carton, L10n.T("Koli")),
        new(PackagingLevel.Pallet, L10n.T("Palet")),
        new(PackagingLevel.Other, L10n.T("Diğer")),
    ];

    public ObservableCollection<RegisteredGtinRow> Gtins { get; } = [];

    /// <summary>Profil denetimi bulguları ("HATA: …", "Uyarı: …", "Bilgi: …").</summary>
    public ObservableCollection<string> Issues { get; } = [];

    public ObservableCollection<SsccEntry> RecentSscc { get; } = [];

    public IReadOnlyList<Gs1AiInfo> AllAis { get; } = Gs1AiCatalog.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AiList))]
    public partial bool ShowAllAis { get; set; }

    public IReadOnlyList<Gs1AiInfo> AiList => ShowAllAis ? AllAis : Gs1AiCatalog.Common;

    [ObservableProperty] public partial string CompanyName { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GlnStatus), nameof(GlnIsValid))]
    public partial string Gln { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GcpStatus), nameof(SerialLengthText))]
    public partial string GcpPrefix { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GcpStatus), nameof(VerifiedText))]
    public partial bool GcpVerified { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VerifiedText))]
    public partial DateTimeOffset? GcpVerifiedAt { get; set; }

    [ObservableProperty] public partial string GcpVerificationNote { get; set; } = "";
    [ObservableProperty] public partial int SsccExtensionDigit { get; set; }
    [ObservableProperty] public partial string SerialStartText { get; set; } = "";
    [ObservableProperty] public partial string SerialEndText { get; set; } = "";
    [ObservableProperty] public partial string Notes { get; set; } = "";
    [ObservableProperty] public partial RegisteredGtinRow? SelectedGtin { get; set; }
    [ObservableProperty] public partial string RegistrySummary { get; private set; } = "";
    [ObservableProperty] public partial string? LoadProblem { get; private set; }

    /// <summary>Önek değiştiği için doğrulama işaretinin kaldırıldığını bildiren not.</summary>
    [ObservableProperty] public partial string? VerificationResetNote { get; private set; }

    public bool GlnIsValid => Gs1Identifiers.ValidateGln(Gln).IsValid;

    public string GlnStatus
    {
        get
        {
            var result = Gs1Identifiers.ValidateGln(Gln);
            return result.IsValid
                ? L10n.T("Geçerli GLN (kontrol basamağı doğru). GLN bir yer/firma numarasıdır; ürün barkodu ve firma öneki değildir.")
                : result.Error ?? L10n.T("Geçersiz GLN");
        }
    }

    public string GcpStatus
    {
        get
        {
            string gcp = Gs1Identifiers.Normalize(GcpPrefix);
            if (gcp.Length == 0)
                return L10n.T("Firma öneki girilmedi. GS1 Türkiye üyelik belgenizdeki öneki yazın; GLN'den tahmin edilmez.");
            if (!Gs1Identifiers.IsValidGcpFormat(gcp, out string? error)) return error!;
            return GcpVerified
                ? L10n.T($"{gcp.Length} haneli önek, üyelik belgesinden doğrulandı. SSCC üretilebilir.")
                : L10n.T($"{gcp.Length} haneli önek. SSCC üretimi için \"Üyelik belgesinden doğrulandı\" kutusunu işaretleyin.");
        }
    }

    public string SerialLengthText
    {
        get
        {
            string gcp = Gs1Identifiers.Normalize(GcpPrefix);
            if (!Gs1Identifiers.IsValidGcpFormat(gcp, out _)) return "";
            int length = Gs1Identifiers.SsccSerialLength(gcp.Length);
            return L10n.T($"Seri referansı {length} hane: 1 – {Gs1Identifiers.MaxSerial(length).ToString("N0", Turkish)}");
        }
    }

    public string VerifiedText => GcpVerified && GcpVerifiedAt is { } at
        ? L10n.T($"Doğrulama: {at.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Turkish)}")
        : "";

    partial void OnGcpPrefixChanged(string value)
    {
        if (_loading) return;
        // Doğrulanan önek değiştirildiyse doğrulama artık geçerli değildir.
        if (GcpVerified && Gs1Identifiers.Normalize(value) != _initialGcp)
        {
            GcpVerified = false;
            VerificationResetNote = L10n.T("Firma öneki değiştiği için \"doğrulandı\" işareti kaldırıldı. Yeni öneki üyelik belgenizle kontrol edip yeniden işaretleyin.");
        }
        RefreshIssues();
    }

    partial void OnGcpVerifiedChanged(bool value)
    {
        if (_loading) return;
        if (value)
        {
            if (!Gs1Identifiers.IsValidGcpFormat(GcpPrefix, out _))
            {
                // Geçersiz önek doğrulanmış sayılamaz.
                GcpVerified = false;
                return;
            }
            GcpVerifiedAt = DateTimeOffset.Now;
            VerificationResetNote = null;
        }
        else
        {
            GcpVerifiedAt = null;
        }
        RefreshIssues();
    }

    partial void OnGlnChanged(string value)
    {
        if (!_loading) RefreshIssues();
    }

    partial void OnSsccExtensionDigitChanged(int value)
    {
        if (!_loading) RefreshIssues();
    }

    partial void OnSerialStartTextChanged(string value)
    {
        if (!_loading) RefreshIssues();
    }

    partial void OnSerialEndTextChanged(string value)
    {
        if (!_loading) RefreshIssues();
    }

    private void AddRow(RegisteredGtinRow row)
    {
        row.PropertyChanged += (_, _) =>
        {
            if (!_loading) RefreshIssues();
        };
        Gtins.Add(row);
    }

    [RelayCommand]
    private void AddGtin()
    {
        var row = new RegisteredGtinRow();
        AddRow(row);
        SelectedGtin = row;
        RefreshIssues();
    }

    [RelayCommand]
    private void RemoveGtin()
    {
        if (SelectedGtin is null) return;
        Gtins.Remove(SelectedGtin);
        SelectedGtin = Gtins.LastOrDefault();
        RefreshIssues();
    }

    /// <summary>Excel/CSV'den GTIN aktarır (GTIN/Barkod/EAN sütunu; ürün kodu ve açıklama varsa onlar da).</summary>
    [RelayCommand]
    private void ImportGtins()
    {
        string? path = _dialogs.ShowOpenFileDialog(L10n.T("GTIN listesi seç (Excel/CSV)"), DataImporter.OpenFileFilter);
        if (path is null) return;
        DataSourceTable table;
        try
        {
            table = DataImporter.Import(path);
        }
        catch (DataImportException ex)
        {
            _dialogs.ShowMessage(L10n.T("GTIN aktarma"), ex.Message, MessageKind.Error);
            return;
        }
        var result = GtinImport.FromRows(table.Columns, table.Rows.Select(r => (r.RowNumber, r.Values)), Gtins.Select(g => g.ToModel()));
        foreach (var gtin in result.Added) AddRow(RegisteredGtinRow.From(gtin));
        RefreshIssues();
        string message = L10n.T($"{result.Added.Count} GTIN eklendi.") + (result.ColumnSummary.Length > 0 ? $"\n({result.ColumnSummary})" : "");
        if (result.Skipped.Count > 0)
            message += L10n.T($"\n\n{result.Skipped.Count} satır eklenmedi:\n") + string.Join("\n", result.Skipped.Take(15)) +
                       (result.Skipped.Count > 15 ? "\n…" : "");
        _dialogs.ShowMessage(L10n.T("GTIN aktarma"), message, result.Added.Count > 0 ? MessageKind.Info : MessageKind.Warning);
    }

    [RelayCommand]
    private void RefreshRegistry()
    {
        RecentSscc.Clear();
        try
        {
            foreach (var entry in _context.Registry.GetRecent(100)) RecentSscc.Add(entry);
            var summary = _context.Registry.GetSummary(BuildProfile());
            var parts = new List<string>
            {
                L10n.T($"Gönderilen: {summary.Sent.ToString("N0", Turkish)}"),
                L10n.T($"Kullanılmayan: {summary.Voided.ToString("N0", Turkish)}"),
            };
            if (summary.Allocated > 0) parts.Add(L10n.T($"Sonucu bekleyen: {summary.Allocated.ToString("N0", Turkish)}"));
            if (summary.NextSerial is { } next) parts.Add(L10n.T($"Sıradaki seri: {next.ToString("N0", Turkish)}"));
            if (summary.Remaining is { } remaining) parts.Add($"Kalan: {remaining.ToString("N0", Turkish)}");
            RegistrySummary = string.Join(" • ", parts);
        }
        catch (Gs1Exception ex)
        {
            RegistrySummary = ex.Message;
        }
    }

    /// <summary>Penceredeki değerlerden profil oluşturur (kaydetmeden).</summary>
    public Gs1CompanyProfile BuildProfile()
    {
        var profile = _context.Profile.Clone();
        profile.CompanyName = (CompanyName ?? "").Trim();
        profile.Gln = Gs1Identifiers.Normalize(Gln);
        profile.GcpPrefix = Gs1Identifiers.Normalize(GcpPrefix);
        profile.GcpVerified = GcpVerified && Gs1Identifiers.IsValidGcpFormat(profile.GcpPrefix, out _);
        profile.GcpVerifiedAt = profile.GcpVerified ? GcpVerifiedAt ?? DateTimeOffset.Now : null;
        profile.GcpVerificationNote = (GcpVerificationNote ?? "").Trim();
        profile.SsccExtensionDigit = SsccExtensionDigit;
        profile.SerialStart = ParseSerial(SerialStartText);
        profile.SerialEnd = ParseSerial(SerialEndText);
        profile.RegisteredGtins = Gtins.Where(g => !string.IsNullOrWhiteSpace(g.Gtin) || !string.IsNullOrWhiteSpace(g.Description))
            .Select(g => g.ToModel()).ToList();
        profile.Notes = (Notes ?? "").Trim();
        return profile;
    }

    /// <summary>Seri alanı: boş = sınırsız; sayı değilse hata için -1 döner (denetim yakalar).</summary>
    private static long? ParseSerial(string? text)
    {
        string value = (text ?? "").Replace(".", "").Replace(" ", "").Trim();
        if (value.Length == 0) return null;
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long n) ? n : -1;
    }

    private void RefreshIssues()
    {
        Issues.Clear();
        foreach (var issue in BuildProfile().Validate()) Issues.Add(issue.ToString());
        if (Issues.Count == 0) Issues.Add(L10n.T("Sorun bulunmadı."));
    }

    /// <summary>Profili doğrular ve kaydeder. Hata varsa kaydetmez, nedenini gösterir; başarılıysa true.</summary>
    public bool Save()
    {
        var profile = BuildProfile();
        var errors = profile.Validate().Where(i => i.Severity == Gs1IssueSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            _dialogs.ShowMessage(L10n.T("GS1 Firma Profili"), L10n.T("Kaydedilmedi; önce şu hataları düzeltin:\n\n") + string.Join("\n", errors.Select(e => "• " + e.Message)),
                MessageKind.Warning);
            return false;
        }
        if (profile.GcpVerified && !_initialVerified &&
            !_dialogs.Confirm(L10n.T("Firma öneki doğrulaması"),
                L10n.T($"Firma öneki {profile.NormalizedGcp} olarak kaydedilecek ve SSCC üretimi açılacak.\n\n") +
                L10n.T("Bu öneki GS1 Türkiye üyelik belgenizden kontrol ettiniz mi? Yanlış önekle basılan SSCC'ler geri alınamaz."),
                L10n.T("Evet, kontrol ettim"), L10n.T("Vazgeç")))
            return false;
        try
        {
            _context.Save(profile);
        }
        catch (Gs1Exception ex)
        {
            _dialogs.ShowMessage(L10n.T("GS1 Firma Profili"), ex.Message, MessageKind.Error);
            return false;
        }
        return true;
    }
}
