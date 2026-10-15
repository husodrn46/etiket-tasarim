using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AkelEtiket.Core;
using AkelEtiket.Core.Model;
using AkelEtiket.PrintEngine;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.Printing;

/// <summary>
/// Adlandırılmış yazıcı ve rulo profili: yazıcı, yöntem, yoğunluk, hız, yön, ofsetler, medya türü ve boşluk.
/// Baskı penceresinde tek tıkla uygulanır.
/// </summary>
public sealed class PrinterProfile
{
    public string Name { get; set; } = "";

    /// <summary>Uygulamayla gelen, değiştirilemeyen profil.</summary>
    [JsonIgnore]
    public bool IsBuiltIn { get; init; }

    /// <summary>Windows yazıcı adı (boşsa uygulanırken mevcut seçim korunur; bulunamazsa uyarılır).</summary>
    public string PrinterName { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PrintMethod Method { get; set; } = PrintMethod.TsplRaw;

    public int Density { get; set; } = 8;
    public int Speed { get; set; } = 4;
    public bool Rotate180 { get; set; }
    public double OffsetXMm { get; set; }
    public double OffsetYMm { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TearMode TearMode { get; set; } = TearMode.Unchanged;

    public bool AllowPrintWhenOffline { get; set; }

    /// <summary>Toplu baskıyı tek Windows işi olarak gönder.</summary>
    public bool SingleJob { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MediaType Media { get; set; } = MediaType.Gap;

    public double GapMm { get; set; } = 3;
    public double GapOffsetMm { get; set; }

    /// <summary>Rulodaki etiket ölçüsü (bilgi ve uyarı için; tasarımın ölçüsü profil uygulanırken değiştirilmez).</summary>
    public double? LabelWidthMm { get; set; }

    public double? LabelHeightMm { get; set; }

    public string Notes { get; set; } = "";

    public PrinterProfile Clone() => (PrinterProfile)MemberwiseClone();

    /// <summary>Profilin kısa açıklaması (liste ve araç ipucu için).</summary>
    [JsonIgnore]
    public string Summary
    {
        get
        {
            string method = Method == PrintMethod.TsplRaw ? "TSPL" : L10n.T("Windows sürücüsü");
            string media = Media switch
            {
                MediaType.BlackMark => L10n.T($"siyah işaret {GapMm:0.##} mm"),
                MediaType.Continuous => L10n.T("sürekli rulo"),
                _ => L10n.T($"boşluk {GapMm:0.##} mm"),
            };
            string size = LabelWidthMm is { } w && LabelHeightMm is { } h ? $" · rulo {w:0.##} × {h:0.##} mm" : "";
            return L10n.T($"{method} · yoğunluk {Density} · hız {Speed} · {media}{(Rotate180 ? " · 180°" : "")}{size}");
        }
    }

    public override string ToString() => IsBuiltIn ? Name + L10n.T(" (hazır)") : Name;
}

/// <summary>
/// Yazıcı/rulo profilleri (%APPDATA%\Etiket Tasarım\yazici-profilleri.json). Hazır profiller koddan gelir ve
/// değiştirilemez; kullanıcı profilleri kaydedilir, yeniden adlandırılır, silinir.
/// </summary>
public sealed class PrinterProfileStore
{
    public const string AkelProfileName = "XP-470B başlangıç profili";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _lock = new();
    private ProfileFile? _file;
    private bool _readFailed;

    public PrinterProfileStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(AppIdentity.RoamingFolder, "yazici-profilleri.json");
    }

    public string FilePath { get; }

    /// <summary>Profil dosyası okunamadıysa açıklama (bozuk dosya yedeklenir, üzerine yazılmaz).</summary>
    public string? LoadProblem { get; private set; }

    /// <summary>
    /// XP-470B için düzenlenebilir başlangıç noktası. Kullanıcının gerçek yazıcısında kalibre edilmelidir.
    /// </summary>
    public static PrinterProfile AkelXp470B { get; } = new()
    {
        Name = AkelProfileName,
        IsBuiltIn = true,
        PrinterName = "",
        Method = PrintMethod.TsplRaw,
        Density = 8,
        Speed = 4,
        Rotate180 = false,
        Media = MediaType.Gap,
        GapMm = 3,
        GapOffsetMm = 0,
        TearMode = TearMode.Unchanged,
        Notes = L10n.T("Başlangıç değerleridir. Yazıcıyı seçin, rulo boşluğunu girin ve kalibrasyon etiketiyle ölçün; ") +
                L10n.T("yoğunluk, hız ve yönü kendi yazıcınızda doğrulayın."),
    };

    public static IReadOnlyList<PrinterProfile> BuiltIns { get; } = [AkelXp470B];

    /// <summary>Hazır profiller önce, sonra kullanıcı profilleri (ada göre).</summary>
    public IReadOnlyList<PrinterProfile> All
    {
        get
        {
            var user = Load().Profiles.Select(p => p.Clone()).OrderBy(p => p.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), true));
            // Clone, init alanlarını da kopyalar: hazır profilin kopyası da hazır olarak işaretli kalır.
            return [.. BuiltIns.Select(p => p.Clone()), .. user];
        }
    }

    /// <summary>En son uygulanan profilin adı (kaydedilemezse sessizce yok sayılır; yalnızca kolaylık bilgisidir).</summary>
    public string? LastUsed
    {
        get => Load().LastUsed;
        set
        {
            lock (_lock)
            {
                var file = Load();
                if (string.Equals(file.LastUsed, value, StringComparison.Ordinal)) return;
                file.LastUsed = value;
                try
                {
                    Persist(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    public PrinterProfile? Find(string name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Kullanıcı profilini ekler ya da aynı adlı kullanıcı profilini günceller. Hazır profillerin üzerine yazılamaz.</summary>
    public void Save(PrinterProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        string name = (profile.Name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException(L10n.T("Profil adı boş olamaz."));
        if (IsBuiltInName(name)) throw new InvalidOperationException(L10n.T("Hazır profil değiştirilemez; farklı bir adla kaydedin."));
        lock (_lock)
        {
            var file = Load();
            file.Profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            var copy = profile.Clone();
            copy.Name = name;
            file.Profiles.Add(copy);
            Persist(file);
        }
    }

    public void Rename(string oldName, string newName)
    {
        string target = (newName ?? "").Trim();
        if (target.Length == 0) throw new ArgumentException(L10n.T("Profil adı boş olamaz."));
        if (IsBuiltInName(oldName) || IsBuiltInName(target)) throw new InvalidOperationException(L10n.T("Hazır profil yeniden adlandırılamaz."));
        lock (_lock)
        {
            var file = Load();
            var profile = file.Profiles.FirstOrDefault(p => string.Equals(p.Name, oldName, StringComparison.OrdinalIgnoreCase))
                          ?? throw new InvalidOperationException(L10n.T($"\"{oldName}\" profili bulunamadı."));
            if (!string.Equals(oldName, target, StringComparison.OrdinalIgnoreCase) &&
                file.Profiles.Any(p => string.Equals(p.Name, target, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(L10n.T($"\"{target}\" adında bir profil zaten var."));
            profile.Name = target;
            if (string.Equals(file.LastUsed, oldName, StringComparison.OrdinalIgnoreCase)) file.LastUsed = target;
            Persist(file);
        }
    }

    public void Delete(string name)
    {
        if (IsBuiltInName(name)) throw new InvalidOperationException(L10n.T("Hazır profil silinemez."));
        lock (_lock)
        {
            var file = Load();
            file.Profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(file.LastUsed, name, StringComparison.OrdinalIgnoreCase)) file.LastUsed = null;
            Persist(file);
        }
    }

    public static bool IsBuiltInName(string? name) =>
        BuiltIns.Any(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    private ProfileFile Load()
    {
        lock (_lock)
        {
            if (_file is not null) return _file;
            _readFailed = false;
            try
            {
                if (File.Exists(FilePath))
                    _file = JsonSerializer.Deserialize<ProfileFile>(File.ReadAllText(FilePath), Options);
            }
            catch (JsonException ex)
            {
                // Bozuk dosya kaybolmasın: yanına kopyalanır; yeni kayıt boş listeyle başlar.
                string backup = FilePath + ".bozuk";
                try
                {
                    File.Copy(FilePath, backup, overwrite: true);
                    LoadProblem = L10n.T($"Yazıcı profilleri okunamadı ({ex.Message}). Bozuk dosya \"{Path.GetFileName(backup)}\" olarak saklandı.");
                }
                catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException)
                {
                    _readFailed = true;
                    LoadProblem = L10n.T($"Yazıcı profilleri okunamadı ({ex.Message}); dosya korunuyor, değişiklik kaydedilmeyecek.");
                }
                _file = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Dosya okunamadı (kilitli/izin): üzerine yazılmasın, bir sonraki erişimde yeniden denenir.
                _readFailed = true;
                LoadProblem = L10n.T("Yazıcı profilleri okunamadı: ") + ex.Message;
                var empty = new ProfileFile();
                return empty;
            }
            _file ??= new ProfileFile();
            _file.Profiles ??= [];
            _file.Profiles.RemoveAll(p => p is null || string.IsNullOrWhiteSpace(p.Name) || IsBuiltInName(p.Name));
            return _file;
        }
    }

    private void Persist(ProfileFile file)
    {
        if (_readFailed) throw new IOException(LoadProblem ?? L10n.T("Yazıcı profilleri okunamadı; değişiklik kaydedilmedi."));
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file, Options));
        File.Move(temp, FilePath, overwrite: true);
        _file = file;
    }

    private sealed class ProfileFile
    {
        public int Version { get; set; } = 1;
        public string? LastUsed { get; set; }
        public List<PrinterProfile> Profiles { get; set; } = [];
    }
}
