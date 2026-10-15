using L10n = AkelEtiket.Core.Localization.UiText;
namespace AkelEtiket.Gs1;

/// <summary>
/// Tek bir baskı işinin SSCC muhasebesi. Her etiket üretilmeden hemen önce <see cref="Allocate"/> ile numara alınır;
/// üretilemeyen etiketin numarası hemen iptal edilir; iş bitince kuyruğa giden ilk N numara "gönderildi", kalanlar
/// "kullanılmadı" olarak kaydedilir. Hiçbir numara iki kez işaretlenmez ve tekrar verilmez.
/// İş parçacığı güvenlidir.
/// </summary>
public sealed class SsccPrintSession
{
    private readonly object _gate = new();
    private readonly SsccRegistry _registry;
    private readonly Gs1CompanyProfile _profile;
    private readonly List<string> _pending = [];
    private readonly List<string> _sent = [];
    private readonly List<string> _voided = [];
    private bool _finished;

    public SsccPrintSession(SsccRegistry registry, Gs1CompanyProfile profile, string jobDescription)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _profile = (profile ?? throw new ArgumentNullException(nameof(profile))).Clone();
        JobDescription = string.IsNullOrWhiteSpace(jobDescription) ? L10n.T("Baskı") : jobDescription.Trim();
    }

    public string JobDescription { get; }

    /// <summary>Ayrılan ve henüz sonucu yazılmamış numaralar (ayrılma sırasıyla).</summary>
    public IReadOnlyList<string> Pending
    {
        get { lock (_gate) return _pending.ToList(); }
    }

    public IReadOnlyList<string> Sent
    {
        get { lock (_gate) return _sent.ToList(); }
    }

    public IReadOnlyList<string> Voided
    {
        get { lock (_gate) return _voided.ToList(); }
    }

    /// <summary>Bir etiket için yeni SSCC ayırır (kayda yazıldıktan sonra döner).</summary>
    public string Allocate(string labelDescription)
    {
        lock (_gate)
        {
            if (_finished) throw new InvalidOperationException(L10n.T("Baskı oturumu kapandı; yeni SSCC ayrılamaz."));
            string note = string.IsNullOrWhiteSpace(labelDescription) ? JobDescription : $"{JobDescription} — {labelDescription.Trim()}";
            string sscc = _registry.Allocate(_profile, 1, note)[0];
            _pending.Add(sscc);
            return sscc;
        }
    }

    /// <summary>Numarası alınmış ama üretilemeyen/gönderilmeyecek etiketin numarasını hemen iptal eder.</summary>
    public void VoidOne(string sscc, string reason)
    {
        lock (_gate)
        {
            if (!_pending.Remove(sscc)) return;
            _registry.MarkVoided([sscc], Compose(reason));
            _voided.Add(sscc);
        }
    }

    /// <summary>
    /// İşi kapatır: bekleyen numaralardan ilk <paramref name="sentCount"/> tanesi kuyruğa gönderildi, kalanlar
    /// kullanılmadı olarak kaydedilir. İkinci çağrı etkisizdir.
    /// </summary>
    public void Finish(int sentCount, string sentNote, string voidReason)
    {
        lock (_gate)
        {
            if (_finished) return;
            _finished = true;
            int count = Math.Clamp(sentCount, 0, _pending.Count);
            var sent = _pending.Take(count).ToList();
            var unused = _pending.Skip(count).ToList();
            _pending.Clear();
            try
            {
                if (sent.Count > 0)
                {
                    _registry.MarkSent(sent, Compose(sentNote));
                    _sent.AddRange(sent);
                }
            }
            finally
            {
                if (unused.Count > 0)
                {
                    _registry.MarkVoided(unused, Compose(voidReason));
                    _voided.AddRange(unused);
                }
            }
        }
    }

    private string Compose(string text) =>
        string.IsNullOrWhiteSpace(text) ? JobDescription : $"{JobDescription}: {text.Trim()}";
}
