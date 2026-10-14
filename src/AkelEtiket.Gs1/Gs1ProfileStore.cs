using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AkelEtiket.Core;

namespace AkelEtiket.Gs1;

/// <summary>GS1 kayıtlarında (profil, SSCC kaydı) oluşan hata. Mesajlar Türkçedir ve kullanıcıya gösterilebilir.</summary>
public sealed class Gs1Exception : Exception
{
    public Gs1Exception(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// GS1 firma profilini okur/yazar: <c>Belgeler\Etiket Tasarım\GS1\firma-profili.json</c>.
/// Yazım atomiktir (geçici dosya + değiştirme). Bozuk dosya sessizce ezilmez: yedeği alınıp varsayılanla devam edilir.
/// </summary>
public sealed class Gs1ProfileStore
{
    public const string FileName = "firma-profili.json";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public Gs1ProfileStore(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = Path.GetFullPath(folder);
    }

    /// <summary>Varsayılan GS1 klasörü (Belgeler\Etiket Tasarım\GS1; ETIKET_VERI ile yönlendirilebilir).</summary>
    public static string DefaultFolder => Path.Combine(AppIdentity.DocumentsFolder, "GS1");

    public string Folder { get; }

    public string FilePath => Path.Combine(Folder, FileName);

    /// <summary>Son okumada karşılaşılan sorun (bozuk dosya vb.); yoksa null.</summary>
    public string? LastLoadProblem { get; private set; }

    /// <summary>Profili okur. Dosya yoksa kimlikleri boş ve doğrulanmamış profil döner.</summary>
    public Gs1CompanyProfile Load()
    {
        LastLoadProblem = null;
        if (!File.Exists(FilePath)) return CreateDefault();
        try
        {
            var profile = JsonSerializer.Deserialize<Gs1CompanyProfile>(File.ReadAllText(FilePath), JsonOptions)
                          ?? throw new JsonException("Dosya boş.");
            profile.Normalize();
            return profile;
        }
        catch (JsonException ex)
        {
            string backup = BackupCorruptFile();
            LastLoadProblem = $"GS1 firma profili okunamadı ({ex.Message}). Dosyanın kopyası saklandı: {backup}. Varsayılan profil kullanılıyor.";
            return CreateDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new Gs1Exception("GS1 firma profili okunamadı: " + ex.Message, ex);
        }
    }

    /// <summary>Profili atomik olarak yazar.</summary>
    public void Save(Gs1CompanyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var copy = profile.Clone();
        copy.Normalize();
        try
        {
            Directory.CreateDirectory(Folder);
            string temp = FilePath + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(copy, JsonOptions));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new Gs1Exception("GS1 firma profili kaydedilemedi: " + ex.Message, ex);
        }
    }

    public static Gs1CompanyProfile CreateDefault() => new();

    private string BackupCorruptFile()
    {
        try
        {
            string backup = Path.Combine(Folder, $"firma-profili.bozuk-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(FilePath, backup, overwrite: false);
            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "(kopyalanamadı)";
        }
    }
}
