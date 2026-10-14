using AkelEtiket.Core.Data;

namespace AkelEtiket.Gs1;

/// <summary>
/// GS1 profil deposu, SSCC kaydı ve önbelleğe alınmış profilin bir arada tutulduğu servis.
/// Uygulama <see cref="Default"/> örneğini kullanır (Belgeler\Etiket Tasarım\GS1); testler kendi klasörüyle oluşturur.
/// Profil yalnızca ilk kez gerektiğinde diskten okunur.
/// </summary>
public sealed class Gs1Context
{
    private static readonly Lazy<Gs1Context> LazyDefault = new(() => new Gs1Context(Gs1ProfileStore.DefaultFolder));
    private readonly object _gate = new();
    private Gs1CompanyProfile? _profile;

    public Gs1Context(string folder)
    {
        Store = new Gs1ProfileStore(folder);
        Registry = new SsccRegistry(folder);
    }

    /// <summary>Uygulamanın varsayılan GS1 bağlamı (ilk erişimde oluşturulur, disk okuması profil istenince yapılır).</summary>
    public static Gs1Context Default => LazyDefault.Value;

    public Gs1ProfileStore Store { get; }

    public SsccRegistry Registry { get; }

    /// <summary>Son profil okumasında oluşan sorun (bozuk/okunamayan dosya); yoksa null.</summary>
    public string? LastLoadProblem { get; private set; }

    /// <summary>Profil kaydedildi veya yeniden okundu.</summary>
    public event EventHandler? ProfileChanged;

    /// <summary>
    /// Önbellekteki profil. <b>Salt okunur kullanın</b>; düzenlemek için <see cref="Gs1CompanyProfile.Clone"/> alıp
    /// <see cref="Save"/> ile kaydedin.
    /// </summary>
    public Gs1CompanyProfile Profile
    {
        get
        {
            lock (_gate)
            {
                return _profile ??= LoadSafe();
            }
        }
    }

    /// <summary>Profili kaydeder ve önbelleği günceller.</summary>
    public void Save(Gs1CompanyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Store.Save(profile);
        lock (_gate)
        {
            _profile = profile.Clone();
            _profile.Normalize();
        }
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Profili diskten yeniden okur.</summary>
    public void Reload()
    {
        lock (_gate)
        {
            _profile = LoadSafe();
        }
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Önizleme alanları ({SSCC} için numara ayırmaz).</summary>
    public IFieldValueSource PreviewFields => Gs1FieldSource.Preview(() => Profile);

    /// <summary>Bir baskı işi için SSCC oturumu başlatır (profil SSCC'ye hazır değilse <see cref="Gs1Exception"/>).</summary>
    public SsccPrintSession BeginPrint(string jobDescription)
    {
        var profile = Profile;
        var blockers = profile.GetSsccBlockers();
        if (blockers.Count > 0) throw new Gs1Exception(blockers[0]);
        return new SsccPrintSession(Registry, profile, jobDescription);
    }

    /// <summary>Ayrılmış bir SSCC için baskı alanları.</summary>
    public IFieldValueSource FieldsFor(string sscc) => Gs1FieldSource.ForAllocated(sscc, () => Profile);

    private Gs1CompanyProfile LoadSafe()
    {
        try
        {
            var profile = Store.Load();
            LastLoadProblem = Store.LastLoadProblem;
            return profile;
        }
        catch (Gs1Exception ex)
        {
            LastLoadProblem = ex.Message;
            return Gs1ProfileStore.CreateDefault();
        }
    }
}
