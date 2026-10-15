using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using AkelEtiket.Core;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.Rendering;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.Printing;

/// <summary>Kalıcı sayaç: sıradaki değer ve artış.</summary>
public sealed class CounterState
{
    public string Name { get; set; } = "";
    public long Next { get; set; } = 1;
    public long Step { get; set; } = 1;
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>Son kullanımda verilen ilk ve son değer (bilgi için).</summary>
    public string? LastRange { get; set; }

    public CounterState Clone() => (CounterState)MemberwiseClone();
}

/// <summary>
/// Kalıcı sayaçlar ve seri numaraları: {SAYAC} (belgenin kendi sayacı, adı belge adıdır) ve adlı sayaçlar
/// ({SAYAC.Koli}). Biçim verilebilir: {SAYAC:000000}. Değerler oturumlar arasında saklanır; önizleme sıradaki değeri
/// gösterir ama tüketmez; baskıda yalnızca yazıcı kuyruğuna gerçekten gönderilen etiketler sayacı ilerletir.
/// </summary>
public static class CounterFields
{
    public const string Prefix = "SAYAC";

    /// <summary>Alan adı bir sayaç mı? Öyleyse sayacın adını (belge sayacı için belge adı) döndürür.</summary>
    public static bool TryGetCounterName(string fieldName, string? documentName, out string counterName)
    {
        counterName = "";
        if (string.IsNullOrWhiteSpace(fieldName)) return false;
        string name = fieldName.Trim();
        int dot = name.IndexOf('.');
        string head = dot < 0 ? name : name[..dot];
        if (FieldNames.Normalize(head) != "sayac") return false;
        if (dot < 0)
        {
            counterName = DefaultName(documentName);
            return true;
        }
        string rest = name[(dot + 1)..].Trim();
        if (rest.Length == 0) return false;
        counterName = rest;
        return true;
    }

    /// <summary>Belge sayacının adı (belge adı; boşsa "Genel").</summary>
    public static string DefaultName(string? documentName) =>
        string.IsNullOrWhiteSpace(documentName) ? "Genel" : documentName.Trim();

    /// <summary>Belgede kullanılan sayaç adları.</summary>
    public static IReadOnlyList<string> CountersUsedBy(LabelDocument document)
    {
        var names = new List<string>();
        foreach (string field in FieldNamesOf(document))
        {
            if (TryGetCounterName(field, document.Name, out var counter) && !names.Contains(counter, StringComparer.OrdinalIgnoreCase))
                names.Add(counter);
        }
        return names;
    }

    public static bool DocumentUsesCounters(LabelDocument document) => CountersUsedBy(document).Count > 0;

    private static IEnumerable<string> FieldNamesOf(LabelDocument document) => document.Elements.SelectMany(e => e switch
    {
        TextElement t => FieldTemplate.GetFieldNames(t.Text),
        BarcodeElement b => FieldTemplate.GetFieldNames(b.Content),
        ImageElement { SourceField: { } f } => [f],
        _ => (IEnumerable<string>)[],
    });

    /// <summary>Değeri biçimlendirir ({SAYAC:000000} → 000123). Geçersiz biçimde düz sayı.</summary>
    public static string Format(long value, string? format)
    {
        try
        {
            return value.ToString(string.IsNullOrWhiteSpace(format) ? "0" : format, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>Sayaç kaydı bozuk/erişilemez ya da başka bir baskı kullanıyor.</summary>
public sealed class CounterException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Sayaçların kalıcı deposu (Belgeler\Etiket Tasarım\Sayaclar\sayaclar.json). Bütün yazmalar ve baskı süresince
/// kullanım kilit dosyasıyla korunur: iki baskı aynı anda aynı sayaçtan değer alamaz.
/// </summary>
public sealed class CounterStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public CounterStore(string? folder = null)
    {
        Folder = folder ?? Path.Combine(AppIdentity.DocumentsFolder, "Sayaclar");
    }

    public string Folder { get; }

    public string FilePath => Path.Combine(Folder, "sayaclar.json");

    private string LockPath => Path.Combine(Folder, "sayaclar.lock");

    /// <summary>Bütün sayaçlar (ada göre).</summary>
    public IReadOnlyList<CounterState> List() =>
        ReadFile().OrderBy(c => c.Name, StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), true)).ToList();

    /// <summary>Sıradaki değeri okur (tüketmez). Sayaç yoksa 1.</summary>
    public long Peek(string name) => Find(ReadFile(), name)?.Next ?? 1;

    public CounterState Get(string name) => Find(ReadFile(), name)?.Clone() ?? new CounterState { Name = name.Trim() };

    /// <summary>Sayacın sıradaki değerini ve artışını ayarlar (yoksa oluşturur).</summary>
    public void Set(string name, long next, long step = 1)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new CounterException(L10n.T("Sayaç adı boş olamaz."));
        if (next < 0) throw new CounterException(L10n.T("Sayacın değeri negatif olamaz."));
        if (step < 1) throw new CounterException(L10n.T("Artış en az 1 olmalı."));
        using var lease = Acquire(TimeSpan.FromSeconds(5));
        var counter = lease.GetOrCreate(name);
        counter.Next = next;
        counter.Step = step;
        lease.Save();
    }

    public void Delete(string name)
    {
        using var lease = Acquire(TimeSpan.FromSeconds(5));
        lease.Remove(name);
        lease.Save();
    }

    /// <summary>
    /// Sayaçları baskı süresince kilitler. Başka bir baskı ya da pencere kilidi tutuyorsa <paramref name="timeout"/>
    /// kadar bekler, sonra <see cref="CounterException"/> fırlatır.
    /// </summary>
    public CounterLease Acquire(TimeSpan timeout)
    {
        Directory.CreateDirectory(Folder);
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            FileStream? handle = null;
            try
            {
                handle = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                // Kilit altında her zaman diskten okunur (önbellek yalnızca önizleme içindir).
                var lease = new CounterLease(this, handle, ReadFile(useCache: false));
                handle = null;
                return lease;
            }
            catch (IOException) when (handle is null && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
            catch (IOException ex) when (handle is null)
            {
                throw new CounterException(L10n.T("Sayaçlar şu anda başka bir baskıda kullanılıyor; o baskı bitince tekrar deneyin."), ex);
            }
            catch (UnauthorizedAccessException ex) when (handle is null)
            {
                throw new CounterException(L10n.T("Sayaç klasörüne erişilemiyor: ") + ex.Message, ex);
            }
            finally
            {
                // Kilit alındı ama kayıt okunamadıysa kilit hemen bırakılır.
                handle?.Dispose();
            }
        }
    }

    internal static CounterState? Find(IEnumerable<CounterState> counters, string name) =>
        counters.FirstOrDefault(c => string.Equals(c.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    private (DateTime Stamp, long Length, List<CounterState> Counters)? _cache;
    private readonly object _cacheLock = new();

    /// <summary>Dosyayı okur (önizlemede değişmediyse önbellekten; her çağrı bağımsız kopya döndürür).</summary>
    internal List<CounterState> ReadFile(bool useCache = true)
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            var info = new FileInfo(FilePath);
            if (useCache)
            {
                lock (_cacheLock)
                {
                    if (_cache is { } cached && cached.Stamp == info.LastWriteTimeUtc && cached.Length == info.Length)
                        return cached.Counters.Select(c => c.Clone()).ToList();
                }
            }
            string json;
            // Yazan taraf dosyayı değiştirirken (taşıma ile) okuyucu engel olmasın.
            using (var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                json = reader.ReadToEnd();
            }
            var file = JsonSerializer.Deserialize<CounterFile>(json, Options);
            var counters = (file?.Counters ?? []).Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Name)).ToList();
            lock (_cacheLock) _cache = (info.LastWriteTimeUtc, info.Length, counters.Select(c => c.Clone()).ToList());
            return counters;
        }
        catch (JsonException ex)
        {
            throw new CounterException(L10n.T($"Sayaç dosyası bozuk ({FilePath}): {ex.Message}. Dosyayı düzeltin ya da yedekten geri yükleyin."), ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CounterException(L10n.T("Sayaç dosyası okunamadı: ") + ex.Message, ex);
        }
    }

    /// <summary>Kaydı atomik olarak yazar (geçici dosya + taşıma); dosya o an okunuyorsa kısa süre yeniden dener.</summary>
    internal void WriteFile(List<CounterState> counters)
    {
        Directory.CreateDirectory(Folder);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new CounterFile { Counters = counters }, Options));
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (true)
        {
            try
            {
                File.Move(temp, FilePath, overwrite: true);
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(25);
            }
        }
        lock (_cacheLock) _cache = null;
    }

    private sealed class CounterFile
    {
        public int Version { get; set; } = 1;
        public List<CounterState> Counters { get; set; } = [];
    }
}

/// <summary>Kilitli sayaç erişimi (bir baskı ya da bir ayar değişikliği süresince).</summary>
public sealed class CounterLease : IDisposable
{
    private readonly CounterStore _store;
    private FileStream? _handle;

    internal CounterLease(CounterStore store, FileStream handle, List<CounterState> counters)
    {
        _store = store;
        _handle = handle;
        Counters = counters;
    }

    internal List<CounterState> Counters { get; }

    public long Peek(string name) => CounterStore.Find(Counters, name)?.Next ?? 1;

    public long StepOf(string name) => Math.Max(1, CounterStore.Find(Counters, name)?.Step ?? 1);

    public CounterState GetOrCreate(string name)
    {
        var counter = CounterStore.Find(Counters, name);
        if (counter is null)
        {
            counter = new CounterState { Name = name.Trim() };
            Counters.Add(counter);
        }
        return counter;
    }

    public void Remove(string name) => Counters.RemoveAll(c => string.Equals(c.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    public void Save() => _store.WriteFile(Counters);

    public void Dispose()
    {
        _handle?.Dispose();
        _handle = null;
    }
}

/// <summary>
/// Sayaç alanları. Önizleme kipinde sıradaki değeri gösterir (tüketmez); baskıda <see cref="CounterPrintTracker"/>
/// her etikete ayrılan değeri verir.
/// </summary>
public sealed class CounterFieldSource : IFieldValueSource
{
    private readonly Func<string?> _documentName;
    private readonly Func<string, long> _value;

    private CounterFieldSource(Func<string?> documentName, Func<string, long> value)
    {
        _documentName = documentName;
        _value = value;
    }

    /// <summary>Önizleme: depodaki sıradaki değer.</summary>
    public static CounterFieldSource Preview(CounterStore store, Func<string?> documentName) =>
        new(documentName, name =>
        {
            try
            {
                return store.Peek(name);
            }
            catch (CounterException)
            {
                return 1;
            }
        });

    /// <summary>Baskıda bir etikete ayrılmış değerler.</summary>
    public static CounterFieldSource Assigned(IReadOnlyDictionary<string, long> values, string? documentName) =>
        new(() => documentName, name => values.TryGetValue(name, out long v) ? v : -1);

    public bool TryGetValue(string name, string? format, out string value)
    {
        value = "";
        if (!CounterFields.TryGetCounterName(name, _documentName(), out var counter)) return false;
        long v = _value(counter);
        if (v < 0) return false;
        value = CounterFields.Format(v, format);
        return true;
    }
}

/// <summary>
/// Baskı sırasında sayaç muhasebesi: tasarımda sayaç varsa baskı boyunca sayaçlar kilitlenir, her etikete üretilmeden
/// hemen önce sıradaki değer verilir, üretilemeyen etiketin değeri geri alınır. İş bitince yalnızca yazıcı kuyruğuna
/// gönderilen etiket sayısı kadar sayaç ilerler; gönderilmeyenlerin değerleri bir sonraki baskıda yeniden kullanılır.
/// </summary>
public sealed class CounterPrintTracker : IDisposable
{
    private readonly CounterLease? _lease;
    private readonly string? _documentName;
    private readonly IReadOnlyList<string> _counters;
    private readonly Dictionary<string, long> _start = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _step = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _ranges = new(StringComparer.OrdinalIgnoreCase);
    private int _assigned;
    private bool _completed;

    private CounterPrintTracker(CounterLease? lease, string? documentName, IReadOnlyList<string> counters)
    {
        _lease = lease;
        _documentName = documentName;
        _counters = counters;
        if (lease is null) return;
        foreach (string name in counters)
        {
            _start[name] = lease.Peek(name);
            _step[name] = lease.StepOf(name);
        }
    }

    /// <summary>Etkin değil (tasarımda sayaç yok).</summary>
    public static CounterPrintTracker Inactive() => new(null, null, []);

    /// <summary>
    /// Tasarımda sayaç varsa sayaçları kilitler (başka baskı kullanıyorsa <paramref name="timeout"/> bekler, sonra
    /// <see cref="CounterException"/>); yoksa etkin olmayan bir izleyici döndürür.
    /// </summary>
    public static CounterPrintTracker Begin(CounterStore store, LabelDocument document, TimeSpan timeout)
    {
        var counters = CounterFields.CountersUsedBy(document);
        if (counters.Count == 0) return Inactive();
        return new CounterPrintTracker(store.Acquire(timeout), document.Name, counters);
    }

    public bool IsActive => _lease is not null;

    /// <summary>Şu ana kadar değer verilmiş (ve geri alınmamış) etiket sayısı.</summary>
    public int AssignedCount => _assigned;

    /// <summary>Sayacın bu baskıdaki ilk değeri.</summary>
    public long StartOf(string name) => _start.TryGetValue(name, out long v) ? v : 1;

    /// <summary>İş kapandıktan sonra: sayaç adı → kuyruğa giden etiketlerin aldığı aralık ("41–60").</summary>
    public IReadOnlyDictionary<string, string> UsedRanges => _ranges;

    /// <summary>Etikete sıradaki sayaç değerlerini verir (değerler etiketin alanlarının önüne eklenir).</summary>
    public PrintItem Prepare(PrintItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_lease is null) return item;
        var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in _counters) values[name] = _start[name] + (long)_assigned * _step[name];
        _assigned++;
        return item with { Fields = new CompositeFieldSource(CounterFieldSource.Assigned(values, _documentName), item.Fields) };
    }

    /// <summary>Son verilen etiket üretilemedi: değeri geri alınır (sonraki etiket aynı değeri alır).</summary>
    public void ProductionFailed()
    {
        if (_lease is not null && _assigned > 0) _assigned--;
    }

    /// <summary>
    /// İşi kapatır: sayaçlar <paramref name="spooledLabels"/> etiket kadar ilerler. Rapor için Türkçe özet döndürür
    /// (etkin değilse null). Kayıt yazılamazsa <c>Failed</c> true olur.
    /// </summary>
    public (string Text, bool Failed)? Complete(int spooledLabels)
    {
        if (_lease is null || _completed) return null;
        _completed = true;
        int used = Math.Clamp(spooledLabels, 0, _assigned);
        var parts = new List<string>();
        try
        {
            foreach (string name in _counters)
            {
                var counter = _lease.GetOrCreate(name);
                long first = _start[name];
                long step = _step[name];
                counter.Step = step;
                if (used > 0)
                {
                    long last = first + (long)(used - 1) * step;
                    counter.Next = first + (long)used * step;
                    counter.LastUsedAt = DateTimeOffset.Now;
                    counter.LastRange = $"{first}–{last}";
                    _ranges[name] = counter.LastRange;
                    parts.Add(L10n.T($"\"{name}\" {first}–{last} kullanıldı, sıradaki {counter.Next}"));
                }
                else
                {
                    counter.Next = first;
                    parts.Add(L10n.T($"\"{name}\" ilerlemedi (sıradaki {first})"));
                }
            }
            _lease.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (L10n.T("Sayaçlar güncellenemedi: ") + ex.Message + L10n.T(" Basılan etiketlerin numaralarını kontrol edin."), true);
        }
        finally
        {
            _lease.Dispose();
        }
        string unsent = _assigned > used ? L10n.T($" Gönderilmeyen {_assigned - used} etiketin numaraları bir sonraki baskıda yeniden kullanılacak.") : "";
        return (L10n.T("Sayaç: ") + string.Join("; ", parts) + "." + unsent, false);
    }

    public void Dispose()
    {
        if (!_completed) _lease?.Dispose();
    }
}

/// <summary>Editörün kullandığı sayaç deposu ve önizleme kaynağı (testler kendi deposunu verebilir).</summary>
public static class CounterIntegration
{
    public static CounterStore Store { get; set; } = new();

    /// <summary>Açık belgenin adı ({SAYAC} belge sayacının adı için).</summary>
    public static Func<string?> DocumentName { get; set; } = () => null;

    /// <summary>Önizleme alanları: sıradaki değerleri gösterir, tüketmez.</summary>
    public static IFieldValueSource PreviewFields => CounterFieldSource.Preview(Store, () => DocumentName());
}
