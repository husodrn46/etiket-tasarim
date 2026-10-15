using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AkelEtiket.Core;
using AkelEtiket.Core.Serialization;
using AkelEtiket.PrintEngine;
using AkelEtiket.ProjectManager;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.Printing;

/// <summary>Plandaki bir etiketin (ya da adetli etiketin) baskı sonucu.</summary>
public enum PrintItemState
{
    /// <summary>Windows yazıcı kuyruğuna eksiksiz gönderildi (fiziksel baskı ayrıca kontrol edilmeli).</summary>
    Sent,

    /// <summary>Etiket üretilemedi (hatalı veri, okunamayan barkod...); gönderilmedi.</summary>
    Failed,

    /// <summary>İptal ya da hata nedeniyle gönderilmedi; "kaldığı yerden devam" ile basılabilir.</summary>
    NotSent,
}

/// <summary>Geçmişteki bir plan öğesi: sıra no, veri satırı, adet ve sonuç.</summary>
public sealed class PrintHistoryItem
{
    public long Sequence { get; set; }
    public int? Row { get; set; }
    public int Copies { get; set; } = 1;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PrintItemState State { get; set; }

    public string? Problem { get; set; }
}

/// <summary>Bir baskı işinin kalıcı kaydı (yalnızca yerel bilgisayarda saklanır).</summary>
public sealed class PrintHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public DateTimeOffset Time { get; set; } = DateTimeOffset.Now;

    /// <summary>Baskı, Test baskısı, Devam baskısı, Kalibrasyon etiketi.</summary>
    public string Title { get; set; } = L10n.T("Baskı");

    public string DocumentName { get; set; } = "";
    public string? TemplatePath { get; set; }

    /// <summary>Baskı anındaki şablon dosyasının özeti (sonradan değişti mi?).</summary>
    public string? TemplateHash { get; set; }

    /// <summary>Basılan tasarımın kopyası (geçmiş klasöründeki dosya adı).</summary>
    public string? SnapshotFile { get; set; }

    public bool UseData { get; set; }
    public string? DataFile { get; set; }
    public string? DataSheet { get; set; }
    public string? DataDelimiter { get; set; }
    public string? DataEncoding { get; set; }
    public long? DataFileLength { get; set; }
    public DateTime? DataFileWriteTimeUtc { get; set; }

    public int Copies { get; set; } = 1;
    public long StartNumber { get; set; } = 1;

    /// <summary>İstenen toplam fiziksel etiket.</summary>
    public int TotalLabels { get; set; }

    public string PrinterName { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PrintMethod Method { get; set; }

    public int Density { get; set; }
    public int Speed { get; set; }
    public bool Rotate180 { get; set; }
    public double OffsetXMm { get; set; }
    public double OffsetYMm { get; set; }
    public bool SingleJob { get; set; }
    public string? ProfileName { get; set; }

    // Rapor aşamaları
    public bool DataGenerated { get; set; }
    public int LabelsGenerated { get; set; }
    public int CopiesTotal { get; set; }
    public bool Spooled { get; set; }
    public int LabelsSpooled { get; set; }
    public List<int> JobIds { get; set; } = [];
    public string? QueueOutcome { get; set; }
    public string? QueueMessage { get; set; }
    public bool Canceled { get; set; }
    public bool Success { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> Skipped { get; set; } = [];
    public string? SsccSummary { get; set; }
    public string? CounterSummary { get; set; }

    /// <summary>Kuyruğa gönderilen ilk ve son SSCC (ayrılma sırasıyla).</summary>
    public string? SsccFirst { get; set; }
    public string? SsccLast { get; set; }
    public int SsccSentCount { get; set; }

    /// <summary>Sayaç adı → kuyruğa giden etiketlerin aldığı değer aralığı.</summary>
    public Dictionary<string, string>? CounterRanges { get; set; }

    public List<PrintHistoryItem> Items { get; set; } = [];

    /// <summary>Kuyruğa gönderilen fiziksel etiket sayısı.</summary>
    [JsonIgnore]
    public int LabelsSent => Items.Where(i => i.State == PrintItemState.Sent).Sum(i => i.Copies);

    /// <summary>Gönderilmeyen (devam ettirilebilecek) fiziksel etiket sayısı.</summary>
    [JsonIgnore]
    public int LabelsRemaining => Items.Where(i => i.State == PrintItemState.NotSent).Sum(i => i.Copies);

    [JsonIgnore]
    public int LabelsFailed => Items.Where(i => i.State == PrintItemState.Failed).Sum(i => i.Copies);

    [JsonIgnore]
    public bool CanResume => LabelsRemaining > 0;

    [JsonIgnore]
    public string StatusText => Success && LabelsRemaining == 0 && LabelsFailed == 0
        ? L10n.T("Kuyruğa gönderildi")
        : Canceled ? L10n.T($"İptal edildi — {LabelsRemaining} etiket gönderilmedi")
        : LabelsRemaining > 0 ? L10n.T($"Yarım kaldı — {LabelsRemaining} etiket gönderilmedi")
        : LabelsFailed > 0 ? L10n.T($"{LabelsFailed} etiket hatalı (atlandı)")
        : Errors.Count > 0 ? L10n.T("Hata") : L10n.T("Kuyruğa gönderildi (kuyruk sonucu belirsiz)");

    [JsonIgnore]
    public string TimeText => Time.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.GetCultureInfo("tr-TR"));

    [JsonIgnore]
    public string SummaryText => $"{Title}: {LabelsSent}/{Math.Max(TotalLabels, LabelsSent)} etiket · {PrinterName}";
}

/// <summary>
/// Baskı geçmişi: her iş tek satırlık JSON olarak eklenir (%LOCALAPPDATA%\Etiket Tasarım\BaskiGecmisi\baski-gecmisi.jsonl);
/// basılan tasarımın kopyası "tasarimlar" alt klasöründe saklanır (tekrar basmak için; aynı tasarım bir kez saklanır,
/// <see cref="SnapshotRetention"/> süresince kullanılmayan kopyalar silinir). Bozuk satırlar atlanır.
/// </summary>
public sealed class PrintHistoryStore
{
    /// <summary>Tasarım kopyalarının saklanma süresi (son kullanımdan itibaren).</summary>
    public static readonly TimeSpan SnapshotRetention = TimeSpan.FromDays(365);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private bool _pruned;

    public PrintHistoryStore(string? folder = null)
    {
        Folder = folder ?? Path.Combine(AppIdentity.LocalFolder, "BaskiGecmisi");
    }

    public string Folder { get; }

    public string LogPath => Path.Combine(Folder, "baski-gecmisi.jsonl");

    public string SnapshotFolder => Path.Combine(Folder, "tasarimlar");

    /// <summary>Kaydı ekler (süreçler arası güvenli: dosya özel açılır, gerekirse kısa süre beklenir).</summary>
    public void Append(PrintHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Directory.CreateDirectory(Folder);
        byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, Options) + "\n");
        // FileShare tek başına Unix'te aynı süreçteki yazıcıları sıralamaz. Aynı dosyayı kullanan
        // bütün örnekler ve süreçler, dosya sonuna gitme + yazma işlemini tek kilitte yapar.
        string identity = Path.GetFullPath(LogPath);
        if (OperatingSystem.IsWindows()) identity = identity.ToUpperInvariant();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        using var mutex = new Mutex(false, "EtiketTasarim-PrintHistory-" + key);
        bool acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException(L10n.T("Baskı geçmişi başka bir işlem tarafından kullanılıyor; kayıt eklenemedi."));
            using var stream = new FileStream(LogPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            if (stream.Length > 0)
            {
                stream.Seek(-1, SeekOrigin.End);
                if (stream.ReadByte() != '\n') stream.WriteByte((byte)'\n');
            }
            stream.Seek(0, SeekOrigin.End);
            stream.Write(line);
            stream.Flush(flushToDisk: true);
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    /// <summary>Bütün kayıtlar, en yenisi önce.</summary>
    public IReadOnlyList<PrintHistoryEntry> ReadAll()
    {
        if (!File.Exists(LogPath)) return [];
        string text;
        using (var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            text = reader.ReadToEnd();
        }
        var list = new List<PrintHistoryEntry>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            try
            {
                if (JsonSerializer.Deserialize<PrintHistoryEntry>(line, Options) is { } entry) list.Add(entry);
            }
            catch (JsonException)
            {
                // Yarım yazılmış/bozuk satır atlanır; diğer kayıtlar etkilenmez.
            }
        }
        return list.OrderByDescending(e => e.Time).ToList();
    }

    public PrintHistoryEntry? Find(string id) => ReadAll().FirstOrDefault(e => e.Id == id);

    /// <summary>
    /// Basılan tasarımın kopyasını saklar; dosya adını döndürür (saklanamazsa null — baskı engellenmez). Ad tasarımın
    /// içeriğinden türetilir: aynı tasarım tekrar basıldığında yeni kopya yazılmaz, mevcut kopyanın tarihi yenilenir.
    /// </summary>
    public string? SaveSnapshot(LabelProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        try
        {
            Directory.CreateDirectory(SnapshotFolder);
            if (!_pruned)
            {
                _pruned = true;
                PruneSnapshots(SnapshotRetention);
            }
            var key = ModelJson.Clone(project.Document);
            key.ModifiedAt = default;
            // Görsellerin kimliği içerik özetidir; belge metni görselleri de kapsar.
            string name = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ModelJson.Serialize(key, indented: false))))[..20] +
                          ProjectFile.Extension;
            string path = Path.Combine(SnapshotFolder, name);
            if (File.Exists(path))
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                return name;
            }
            var copy = new LabelProject(ModelJson.Clone(project.Document), project.Assets);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    ProjectFile.Write(copy, stream);
                File.Move(temp, path, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectFileException)
        {
            return null;
        }
    }

    public string? SnapshotPath(PrintHistoryEntry entry) =>
        entry.SnapshotFile is { Length: > 0 } file && File.Exists(Path.Combine(SnapshotFolder, file)) ? Path.Combine(SnapshotFolder, file) : null;

    private string PinsPath => Path.Combine(Folder, "favorite-snapshots.json");
    public void SetPinnedSnapshots(IEnumerable<string?> names)
    {
        var safe = names.Where(n => n is not null && n == Path.GetFileName(n) && n.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
        Directory.CreateDirectory(Folder);
        string temp = PinsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(safe)); File.Move(temp, PinsPath, overwrite: true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    /// <summary>Eski tasarım kopyalarını siler (kayıt satırları kalır).</summary>
    public int PruneSnapshots(TimeSpan maxAge)
    {
        if (!Directory.Exists(SnapshotFolder)) return 0;
        HashSet<string> pinned;
        try { pinned = File.Exists(PinsPath) ? new(JsonSerializer.Deserialize<string[]>(File.ReadAllText(PinsPath)) ?? [], StringComparer.OrdinalIgnoreCase) : []; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return 0; }
        int removed = 0;
        var limit = DateTime.UtcNow - maxAge;
        foreach (var file in new DirectoryInfo(SnapshotFolder).GetFiles("*" + ProjectFile.Extension))
        {
            if (file.LastWriteTimeUtc > limit || pinned.Contains(file.Name)) continue;
            try
            {
                file.Delete();
                removed++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return removed;
    }

    /// <summary>Dosyanın kısa SHA-256 özeti (şablon baskıdan sonra değişti mi?).</summary>
    public static string? HashFile(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexStringLower(SHA256.HashData(stream))[..16];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>"Kaldığı yerden devam" için kalan etiketlerin hesabı.</summary>
public static class ResumePlanner
{
    /// <summary>Veri satırı → kalan adet (yalnızca gönderilmemiş öğeler; sırayı korur).</summary>
    public static IReadOnlyList<(int Row, int Copies)> RemainingRows(PrintHistoryEntry entry) =>
        GroupRows(entry.Items.Where(i => i.State == PrintItemState.NotSent));

    /// <summary>Veri satırı → baskıdaki adet ("tekrar bas": bütün öğeler, sırayı korur).</summary>
    public static IReadOnlyList<(int Row, int Copies)> AllRows(PrintHistoryEntry entry) => GroupRows(entry.Items);

    /// <summary>Verisiz baskı: ilk gönderilmemiş etiketin sıra numarası ve kalan adet.</summary>
    public static (long StartNumber, int Copies)? RemainingSequence(PrintHistoryEntry entry)
    {
        var remaining = entry.Items.Where(i => i.State == PrintItemState.NotSent && i.Row is null).ToList();
        if (remaining.Count == 0) return null;
        return (remaining.Min(i => i.Sequence), remaining.Sum(i => i.Copies));
    }

    /// <summary>İlk gönderilmemiş etiketin sıra numarası ({SIRA} kaldığı yerden sürsün diye); yoksa null.</summary>
    public static long? FirstRemainingSequence(PrintHistoryEntry entry)
    {
        var remaining = entry.Items.Where(i => i.State == PrintItemState.NotSent).ToList();
        return remaining.Count == 0 ? null : remaining.Min(i => i.Sequence);
    }

    private static List<(int Row, int Copies)> GroupRows(IEnumerable<PrintHistoryItem> items)
    {
        var result = new List<(int Row, int Copies)>();
        foreach (var item in items)
        {
            if (item.Row is not { } row) continue;
            int index = result.FindIndex(r => r.Row == row);
            if (index >= 0) result[index] = (row, result[index].Copies + item.Copies);
            else result.Add((row, item.Copies));
        }
        return result;
    }
}
