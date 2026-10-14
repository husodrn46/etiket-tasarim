using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AkelEtiket.Gs1;

/// <summary>Bir SSCC numarasının kayıttaki durumu.</summary>
public enum SsccStatus
{
    /// <summary>Numara ayrıldı (bir daha hiçbir etikete verilmez).</summary>
    [JsonStringEnumMemberName("Ayrildi")]
    Allocated,

    /// <summary>Numaralı etiket yazıcı kuyruğuna gönderildi (fiziksel baskı ayrıca kontrol edilmeli).</summary>
    [JsonStringEnumMemberName("Gonderildi")]
    Sent,

    /// <summary>Numara kullanılmadı (iptal, hata). Yine de tekrar verilmez.</summary>
    [JsonStringEnumMemberName("Iptal")]
    Voided,
}

/// <summary>SSCC kayıt dosyasındaki tek satır (yalnızca eklenir, asla değiştirilmez).</summary>
public sealed record SsccLogRecord
{
    [JsonPropertyName("zaman")] public DateTimeOffset Time { get; init; }
    [JsonPropertyName("sscc")] public string Sscc { get; init; } = "";
    [JsonPropertyName("durum")] public SsccStatus Status { get; init; }
    [JsonPropertyName("uzanti")] public int ExtensionDigit { get; init; }
    [JsonPropertyName("onek")] public string Gcp { get; init; } = "";
    [JsonPropertyName("seri")] public long Serial { get; init; }
    [JsonPropertyName("aciklama")] public string Note { get; init; } = "";
    [JsonPropertyName("bilgisayar")] public string Machine { get; init; } = "";
}

/// <summary>Bir SSCC'nin kayıttaki son durumu (arayüz listesi için).</summary>
public sealed record SsccEntry(
    string Sscc,
    long Serial,
    DateTimeOffset AllocatedAt,
    SsccStatus Status,
    DateTimeOffset StatusAt,
    string Note,
    string Machine)
{
    public string StatusText => Status switch
    {
        SsccStatus.Allocated => "Ayrıldı",
        SsccStatus.Sent => "Yazıcıya gönderildi",
        _ => "Kullanılmadı (iptal)",
    };

    /// <summary>İnsan okunur biçim: (00) uygulama tanımlayıcısından sonra düz 18 hane.</summary>
    public string HumanReadable => "(00) " + Sscc;
}

/// <summary>Kayıt özeti (arayüz için).</summary>
public sealed record SsccRegistrySummary(int Allocated, int Sent, int Voided, long? LastSerial, long? NextSerial, long? Remaining);

/// <summary>
/// Kalıcı SSCC kaydı. Numaralar <c>sscc-kayitlari.jsonl</c> dosyasına <b>yalnızca eklenerek</b> yazılır; her numara
/// döndürülmeden ÖNCE diske yazılır. Aynı numaranın ikinci kez verilmesi şu şekilde engellenir:
/// <list type="bullet">
/// <item>Ayırma ve durum yazımı, özel kilit dosyası (FileShare.None) altında yapılır; aynı anda çalışan süreçler sıraya girer.</item>
/// <item>Sayaç dosyası (<c>sscc-sayac.json</c>) yalnızca hızlandırıcıdır; kayıt dosyasının boyutu sayaçtakiyle uyuşmazsa
/// (sayaç silinmiş/bozuk/geride) kayıt dosyası taranır ve en büyük seri + 1'den devam edilir.</item>
/// <item>İptal edilen veya basılamayan numaralar da tekrar verilmez.</item>
/// </list>
/// Not: Kayıt dosyası VE sayaç birlikte silinirse geçmiş bilinemez; bu klasör yedeklenmelidir.
/// </summary>
public sealed class SsccRegistry
{
    public const string LogFileName = "sscc-kayitlari.jsonl";
    public const string CounterFileName = "sscc-sayac.json";
    public const string LockFileName = "sscc.lock";
    public const int MaxAllocationPerCall = 10000;

    private static readonly JsonSerializerOptions LineOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions CounterOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Regex SsccInLine = new("\"sscc\"\\s*:\\s*\"(\\d{18})\"", RegexOptions.CultureInvariant);

    public SsccRegistry(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = Path.GetFullPath(folder);
    }

    public string Folder { get; }

    public string LogPath => Path.Combine(Folder, LogFileName);

    public string CounterPath => Path.Combine(Folder, CounterFileName);

    public string LockPath => Path.Combine(Folder, LockFileName);

    /// <summary>Kilit için en uzun bekleme.</summary>
    public TimeSpan LockTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// <paramref name="count"/> adet yeni SSCC ayırır. Profil SSCC'ye hazır değilse (önek doğrulanmamış vb.) veya aralık
    /// yetmiyorsa <see cref="Gs1Exception"/> fırlatır. Döndürülen her numara kayda yazılmıştır.
    /// </summary>
    public IReadOnlyList<string> Allocate(Gs1CompanyProfile profile, int count, string note)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (count is < 1 or > MaxAllocationPerCall)
            throw new ArgumentOutOfRangeException(nameof(count), $"Tek seferde 1–{MaxAllocationPerCall} SSCC ayrılabilir.");
        var blockers = profile.GetSsccBlockers();
        if (blockers.Count > 0) throw new Gs1Exception(blockers[0]);

        string gcp = profile.NormalizedGcp;
        int ext = profile.SsccExtensionDigit;
        var (min, max) = profile.GetSerialRange()!.Value;

        return WithLock(() =>
        {
            var state = ReadCounterState();
            long logLength = GetLogLength();
            string key = Key(ext, gcp);
            long last = state.Counters.GetValueOrDefault(key, 0);
            if (state.LogSize != logLength || TailMaxSerial(ext, gcp) > last)
            {
                // Sayaç güvenilmez (silinmiş, bozuk, geride ya da yarıda kalmış yazım): kayıt dosyası esas alınır.
                last = Math.Max(last, ScanMaxSerial(ext, gcp));
            }
            long first = Math.Max(last + 1, min);
            long lastNew = first + count - 1;
            if (lastNew > max)
            {
                long remaining = Math.Max(0, max - first + 1);
                throw new Gs1Exception(remaining == 0
                    ? $"SSCC aralığı doldu (son seri {last:N0}, aralık sonu {max:N0}). GS1 Firma Profili'nde yeni bir aralık tanımlayın."
                    : $"SSCC aralığında yalnızca {remaining:N0} numara kaldı, {count:N0} isteniyor. GS1 Firma Profili'nde aralığı genişletin.");
            }

            var now = DateTimeOffset.Now;
            string machine = Environment.MachineName;
            var numbers = new List<string>(count);
            var records = new List<SsccLogRecord>(count);
            for (long serial = first; serial <= lastNew; serial++)
            {
                string sscc = Gs1Identifiers.BuildSscc(ext, gcp, serial);
                numbers.Add(sscc);
                records.Add(new SsccLogRecord
                {
                    Time = now, Sscc = sscc, Status = SsccStatus.Allocated, ExtensionDigit = ext, Gcp = gcp,
                    Serial = serial, Note = note ?? "", Machine = machine,
                });
            }
            AppendRecords(records);
            state.Counters[key] = lastNew;
            state.LogSize = GetLogLength();
            WriteCounterState(state);
            return (IReadOnlyList<string>)numbers;
        });
    }

    /// <summary>Numaraların yazıcı kuyruğuna gönderildiğini kaydeder.</summary>
    public void MarkSent(IEnumerable<string> ssccs, string note) => AppendStatus(ssccs, SsccStatus.Sent, note);

    /// <summary>Numaraların kullanılmadığını kaydeder (tekrar verilmezler).</summary>
    public void MarkVoided(IEnumerable<string> ssccs, string reason) => AppendStatus(ssccs, SsccStatus.Voided, reason);

    /// <summary>Profilin önek/uzantısı için sıradaki seri numarası (ayırmaz). Profil hazır değilse null.</summary>
    public long? PeekNextSerial(Gs1CompanyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.GetSerialRange() is not { } range || !Gs1Identifiers.IsValidGcpFormat(profile.GcpPrefix, out _)) return null;
        string gcp = profile.NormalizedGcp;
        int ext = profile.SsccExtensionDigit;
        if (!File.Exists(LogPath) && !File.Exists(CounterPath)) return range.Min;
        return WithLock(() =>
        {
            var state = ReadCounterState();
            long last = state.Counters.GetValueOrDefault(Key(ext, gcp), 0);
            if (state.LogSize != GetLogLength() || TailMaxSerial(ext, gcp) > last) last = Math.Max(last, ScanMaxSerial(ext, gcp));
            return (long?)Math.Max(last + 1, range.Min);
        });
    }

    /// <summary>Kayıttaki numaraların son durumları; en yeni ayrılan önce.</summary>
    public IReadOnlyList<SsccEntry> GetRecent(int maxCount = 200)
    {
        var entries = ReadEntries();
        return entries.Values
            .OrderByDescending(e => e.AllocatedAt)
            .ThenByDescending(e => e.Serial)
            .Take(Math.Max(0, maxCount))
            .ToList();
    }

    /// <summary>Tek numaranın son durumu; kayıtta yoksa null.</summary>
    public SsccEntry? GetEntry(string sscc)
    {
        string value = Gs1Identifiers.Normalize(sscc);
        return ReadEntries().GetValueOrDefault(value);
    }

    /// <summary>Özet: durum sayıları ve profile göre sıradaki/kalan numaralar.</summary>
    public SsccRegistrySummary GetSummary(Gs1CompanyProfile? profile)
    {
        var entries = ReadEntries().Values.ToList();
        long? last = null, next = null, remaining = null;
        if (profile is not null && profile.GetSerialRange() is { } range)
        {
            string gcp = profile.NormalizedGcp;
            var own = entries.Where(e => e.Sscc.Length == Gs1Identifiers.SsccLength && e.Sscc[0] - '0' == profile.SsccExtensionDigit &&
                                         e.Sscc.AsSpan(1).StartsWith(gcp, StringComparison.Ordinal)).ToList();
            if (own.Count > 0) last = own.Max(e => e.Serial);
            next = PeekNextSerial(profile);
            if (next is { } n) remaining = Math.Max(0, range.Max - n + 1);
        }
        return new SsccRegistrySummary(
            entries.Count(e => e.Status == SsccStatus.Allocated),
            entries.Count(e => e.Status == SsccStatus.Sent),
            entries.Count(e => e.Status == SsccStatus.Voided),
            last, next, remaining);
    }

    // ───────────────────────── iç işler ─────────────────────────

    private static string Key(int ext, string gcp) => ext.ToString(CultureInfo.InvariantCulture) + "|" + gcp;

    private void AppendStatus(IEnumerable<string> ssccs, SsccStatus status, string note)
    {
        ArgumentNullException.ThrowIfNull(ssccs);
        var list = new List<SsccLogRecord>();
        var now = DateTimeOffset.Now;
        string machine = Environment.MachineName;
        foreach (var raw in ssccs)
        {
            var check = Gs1Identifiers.ValidateSscc(raw);
            if (!check.IsValid) throw new ArgumentException($"Geçersiz SSCC: {raw} ({check.Error})", nameof(ssccs));
            list.Add(new SsccLogRecord
            {
                Time = now, Sscc = check.Normalized, Status = status, ExtensionDigit = check.Normalized[0] - '0',
                Note = note ?? "", Machine = machine,
            });
        }
        if (list.Count == 0) return;
        WithLock(() =>
        {
            var state = ReadCounterState();
            bool wasConsistent = state.LogSize == GetLogLength();
            AppendRecords(list);
            // Sayaç değişmez; tutarlıysa yeni boyutu yaz ki sonraki ayırmada boşuna tarama yapılmasın.
            if (wasConsistent)
            {
                state.LogSize = GetLogLength();
                WriteCounterState(state);
            }
            return 0;
        });
    }

    private T WithLock<T>(Func<T> action)
    {
        try
        {
            Directory.CreateDirectory(Folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new Gs1Exception("SSCC kayıt klasörü oluşturulamadı: " + ex.Message, ex);
        }

        var sw = Stopwatch.StartNew();
        FileStream? lockStream = null;
        while (lockStream is null)
        {
            try
            {
                lockStream = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
            }
            catch (IOException) when (sw.Elapsed < LockTimeout)
            {
                Thread.Sleep(Random.Shared.Next(3, 20));
            }
            catch (IOException ex)
            {
                throw new Gs1Exception("SSCC kaydı kilitli: başka bir baskı işlemi numara alıyor olabilir. Biraz sonra tekrar deneyin. (" + ex.Message + ")", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new Gs1Exception("SSCC kayıt klasörüne yazılamıyor: " + ex.Message, ex);
            }
        }
        using (lockStream)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new Gs1Exception("SSCC kaydı yazılamadı: " + ex.Message, ex);
            }
        }
    }

    private long GetLogLength()
    {
        var info = new FileInfo(LogPath);
        return info.Exists ? info.Length : 0;
    }

    private void AppendRecords(IReadOnlyList<SsccLogRecord> records)
    {
        var sb = new StringBuilder();
        foreach (var record in records)
            sb.Append(JsonSerializer.Serialize(record, LineOptions)).Append('\n');
        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        using var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        // Önceki yazım yarıda kalmışsa (satır sonu yoksa) yeni kayıt ayrı satırda başlasın.
        if (stream.Position > 0 && !EndsWithNewline()) stream.WriteByte((byte)'\n');
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private bool EndsWithNewline()
    {
        using var reader = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (reader.Length == 0) return true;
        reader.Seek(-1, SeekOrigin.End);
        return reader.ReadByte() == '\n';
    }

    /// <summary>
    /// Kayıt dosyasının son 8 KB'ındaki en büyük seri (hızlı tutarlılık denetimi: sayaç kayıttan geride kalmışsa
    /// tam tarama yapılır).
    /// </summary>
    private long TailMaxSerial(int ext, string gcp)
    {
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length == 0) return 0;
        using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        int take = (int)Math.Min(8192, stream.Length);
        stream.Seek(-take, SeekOrigin.End);
        byte[] buffer = new byte[take];
        stream.ReadExactly(buffer);
        return MaxSerialIn(Encoding.UTF8.GetString(buffer), ext, gcp);
    }

    private static long MaxSerialIn(string text, int ext, string gcp)
    {
        long max = 0;
        int serialLength = Gs1Identifiers.SsccSerialLength(gcp.Length);
        foreach (Match match in SsccInLine.Matches(text))
        {
            string sscc = match.Groups[1].Value;
            if (sscc[0] - '0' != ext || !sscc.AsSpan(1).StartsWith(gcp, StringComparison.Ordinal)) continue;
            if (long.TryParse(sscc.AsSpan(1 + gcp.Length, serialLength), NumberStyles.None, CultureInfo.InvariantCulture, out long serial))
                max = Math.Max(max, serial);
        }
        return max;
    }

    /// <summary>Kayıt dosyasındaki (bozuk satırlar dahil) bu uzantı + önek için en büyük seri.</summary>
    private long ScanMaxSerial(int ext, string gcp)
    {
        if (!File.Exists(LogPath)) return 0;
        long max = 0;
        // Bozuk/yarım satırlarda bile numara okunabiliyorsa hesaba katılır (yanlışlıkla tekrar verilmesin).
        foreach (var line in ReadLinesShared())
            max = Math.Max(max, MaxSerialIn(line, ext, gcp));
        return max;
    }

    private IEnumerable<string> ReadLinesShared()
    {
        using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0) yield return line;
        }
    }

    private Dictionary<string, SsccEntry> ReadEntries()
    {
        var result = new Dictionary<string, SsccEntry>(StringComparer.Ordinal);
        if (!File.Exists(LogPath)) return result;
        IEnumerable<string> lines;
        try
        {
            lines = ReadLinesShared().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new Gs1Exception("SSCC kayıtları okunamadı: " + ex.Message, ex);
        }
        foreach (var line in lines)
        {
            SsccLogRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<SsccLogRecord>(line, LineOptions);
            }
            catch (JsonException)
            {
                continue;
            }
            if (record is null || record.Sscc.Length != Gs1Identifiers.SsccLength) continue;
            if (result.TryGetValue(record.Sscc, out var existing))
            {
                // Durum kayıtları sonradan eklenir; ayrılma zamanı ve seri ilk kayıttan gelir.
                result[record.Sscc] = existing with
                {
                    Status = record.Status,
                    StatusAt = record.Time,
                    Note = string.IsNullOrWhiteSpace(record.Note) ? existing.Note : record.Note,
                };
            }
            else
            {
                result[record.Sscc] = new SsccEntry(record.Sscc, record.Serial, record.Time, record.Status, record.Time, record.Note, record.Machine);
            }
        }
        return result;
    }

    private sealed class CounterState
    {
        [JsonPropertyName("surum")] public int Version { get; set; } = 1;
        [JsonPropertyName("sayaclar")] public Dictionary<string, long> Counters { get; set; } = new(StringComparer.Ordinal);
        [JsonPropertyName("kayitBoyutu")] public long LogSize { get; set; } = -1;
        [JsonPropertyName("guncelleme")] public DateTimeOffset UpdatedAt { get; set; }
    }

    private CounterState ReadCounterState()
    {
        try
        {
            if (File.Exists(CounterPath))
            {
                var state = JsonSerializer.Deserialize<CounterState>(File.ReadAllText(CounterPath), CounterOptions);
                if (state is not null)
                {
                    state.Counters = new Dictionary<string, long>(state.Counters ?? [], StringComparer.Ordinal);
                    return state;
                }
            }
        }
        catch (JsonException)
        {
            // Bozuk sayaç: kayıt dosyası taranacak (LogSize = -1 uyuşmaz).
        }
        return new CounterState();
    }

    private void WriteCounterState(CounterState state)
    {
        state.UpdatedAt = DateTimeOffset.Now;
        string temp = CounterPath + "." + Environment.ProcessId + "." + Environment.CurrentManagedThreadId + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, CounterOptions));
        File.Move(temp, CounterPath, overwrite: true);
    }
}
