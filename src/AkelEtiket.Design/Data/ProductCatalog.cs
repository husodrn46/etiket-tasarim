using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Encodings.Web;
using AkelEtiket.Core;
using AkelEtiket.Core.Data;
using AkelEtiket.DataImport;

namespace AkelEtiket.Design.Data;

/// <summary>Ürün kaydı: anahtar + sütun değerleri.</summary>
public sealed class ProductRecord
{
    public string Key { get; set; } = "";
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public IFieldValueSource ToFieldSource(IReadOnlyList<string> columns) =>
        new RecordFieldSource(columns, columns.Select(c => Fields.TryGetValue(c, out var v) ? v : null).ToList());
}

/// <summary>Ürün listesinin kalıcı hâli.</summary>
public sealed class ProductCatalogData
{
    public List<string> Columns { get; set; } = [];
    public string KeyColumn { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public DateTimeOffset ImportedAt { get; set; }
    public List<ProductRecord> Products { get; set; } = [];
}

/// <summary>
/// Yerel ürün listesi (Belgeler\Etiket Tasarım\Ürünler\urunler.json). Excel/CSV'den bir kez aktarılır; ad, kod
/// veya barkodla aranır. Hızlı Baskı ekranı ve yapay zekâ veri araçları aynı listeyi kullanır.
/// </summary>
public sealed class ProductCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string[] KeyCandidates =
        ["StokKodu", "Stok Kodu", "UrunKodu", "Ürün Kodu", "Kod", "Kodu", "SKU", "Barkod", "EAN", "GTIN"];

    private static readonly string[] BarcodeCandidates = ["Barkod", "Barcode", "EAN", "EAN13", "GTIN"];

    private static readonly string[] NameCandidates = ["UrunAdi", "Ürün Adı", "Urun", "Ad", "Adı", "Aciklama", "Açıklama", "StokAdi"];

    private readonly object _lock = new();
    private ProductCatalogData? _data;

    public ProductCatalog(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(AppIdentity.DocumentsFolder, "Ürünler", "urunler.json");
    }

    public string FilePath { get; }

    public ProductCatalogData Data
    {
        get
        {
            lock (_lock)
            {
                return _data ??= Load();
            }
        }
    }

    public bool IsEmpty => Data.Products.Count == 0;

    /// <summary>Excel/CSV tablosundan ürün listesini oluşturur (önceki liste değiştirilir).</summary>
    public ProductCatalogData Import(DataSourceTable table, string? keyColumn)
    {
        if (table.Columns.Count == 0) throw new ToolException(ToolErrorCodes.InvalidArgument, "Dosyada sütun bulunamadı.");
        string key = keyColumn is { Length: > 0 }
            ? table.Columns[FieldNames.FindIndex(table.Columns, keyColumn) is var i and >= 0 ? i
                : throw new ToolException(ToolErrorCodes.InvalidArgument, $"\"{keyColumn}\" sütunu yok. Sütunlar: {string.Join(", ", table.Columns)}")]
            : GuessColumn(table.Columns, KeyCandidates) ?? table.Columns[0];
        int keyIndex = table.Columns.ToList().IndexOf(key);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var data = new ProductCatalogData
        {
            Columns = table.Columns.ToList(),
            KeyColumn = key,
            SourceFile = table.SourcePath,
            ImportedAt = DateTimeOffset.Now,
        };
        foreach (var row in table.Rows)
        {
            string k = row.GetValue(keyIndex).Trim();
            if (k.Length == 0) k = $"satir-{row.RowNumber}";
            if (!seen.Add(k)) k = $"{k}#{row.RowNumber}";
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < table.Columns.Count; c++) fields[table.Columns[c]] = row.GetValue(c);
            data.Products.Add(new ProductRecord { Key = k, Fields = fields });
        }
        Save(data);
        lock (_lock) _data = data;
        return data;
    }

    /// <summary>Geri yüklemeden önce JSON verisini doğrular ve bağımsız kopyasını verir.</summary>
    public static ProductCatalogData ValidateBackup(ProductCatalogData data)
    {
        if (data.Columns is null || data.Products is null || data.Columns.Any(string.IsNullOrWhiteSpace) ||
            data.Columns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Columns.Count ||
            data.Products.Any(p => p is null || string.IsNullOrWhiteSpace(p.Key) || p.Fields is null || p.Fields.Values.Any(v => v is null)) ||
            data.Products.Select(p => p.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Products.Count ||
            data.Products.Count > 0 && FieldNames.FindIndex(data.Columns, data.KeyColumn ?? "") < 0)
            throw new InvalidDataException("Yedekteki ürün listesi geçersiz.");
        return JsonSerializer.Deserialize<ProductCatalogData>(JsonSerializer.Serialize(data, Options), Options)!;
    }

    /// <summary>Ürünleri değiştirir; önceki listenin ayrı kopyasını korur.</summary>
    public void RestoreBackup(ProductCatalogData data, Action? commit = null)
    {
        var copy = ValidateBackup(data);
        lock (_lock)
        {
            byte[]? previous = File.Exists(FilePath) ? File.ReadAllBytes(FilePath) : null;
            if (previous is not null) File.WriteAllBytes(FilePath + ".onceki-" + Guid.NewGuid().ToString("N") + ".json", previous);
            try
            {
                Save(copy);
                commit?.Invoke();
                _data = copy;
            }
            catch (Exception error)
            {
                try
                {
                    if (previous is null) File.Delete(FilePath);
                    else
                    {
                        string temporary = FilePath + ".rollback-" + Guid.NewGuid().ToString("N");
                        try { File.WriteAllBytes(temporary, previous); File.Move(temporary, FilePath, overwrite: true); }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                }
                catch (Exception rollbackError)
                {
                    throw new IOException("Geri yükleme tamamlanamadı ve ürün listesi geri alınamadı. Önceki liste .onceki dosyasında korunuyor.",
                        new AggregateException(error, rollbackError));
                }
                throw;
            }
        }
    }

    /// <summary>Okutma için yalnız kod/barkod alanlarında tam eşleşme; ekranın sonuç sınırına bağlı değildir.</summary>
    public IReadOnlyList<ProductRecord> FindScanMatches(string code)
    {
        string wanted = code.Trim();
        if (wanted.Length == 0) return [];
        var names = KeyCandidates.Concat(BarcodeCandidates).Select(FieldNames.Normalize).ToHashSet();
        return Data.Products.Where(p => string.Equals(p.Key, wanted, StringComparison.OrdinalIgnoreCase) ||
            p.Fields.Any(f => names.Contains(FieldNames.Normalize(f.Key)) &&
                string.Equals(f.Value.Trim(), wanted, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public ProductRecord? Find(string key)
    {
        string wanted = key.Trim();
        return Data.Products.FirstOrDefault(p => string.Equals(p.Key, wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Ad, kod veya barkodda arar (Türkçe karakter ve büyük/küçük harf duyarsız). Barkod tam eşleşmesi öne alınır.</summary>
    public IReadOnlyList<ProductRecord> Search(string query, int limit)
    {
        string q = Fold(query);
        if (q.Length == 0) return Data.Products.Take(limit).ToList();
        string digits = new string(query.Where(char.IsDigit).ToArray());
        var scored = new List<(ProductRecord P, int Score)>();
        foreach (var p in Data.Products)
        {
            int score = 0;
            if (Fold(p.Key) == q) score = 100;
            foreach (var (column, value) in p.Fields)
            {
                if (value.Length == 0) continue;
                string folded = Fold(value);
                if (digits.Length >= 8 && BarcodeCandidates.Any(c => FieldNames.Normalize(c) == FieldNames.Normalize(column)) && value.Contains(digits, StringComparison.Ordinal))
                    score = Math.Max(score, 95);
                else if (folded == q) score = Math.Max(score, 90);
                else if (folded.StartsWith(q, StringComparison.Ordinal)) score = Math.Max(score, 70);
                else if (folded.Contains(q, StringComparison.Ordinal)) score = Math.Max(score, 50);
            }
            if (score > 0) scored.Add((p, score));
        }
        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.P.Key, StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), true))
            .Take(limit).Select(s => s.P).ToList();
    }

    /// <summary>Ürünün ekranda gösterilecek adı (ad sütunu tahmini).</summary>
    public string DisplayName(ProductRecord product)
    {
        string? nameColumn = GuessColumn(Data.Columns, NameCandidates);
        return nameColumn is not null && product.Fields.TryGetValue(nameColumn, out var name) && name.Length > 0 ? name : product.Key;
    }

    private ProductCatalogData Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<ProductCatalogData>(File.ReadAllText(FilePath), Options) ?? new ProductCatalogData();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return new ProductCatalogData();
    }

    private void Save(ProductCatalogData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data, Options));
        File.Move(temp, FilePath, overwrite: true);
    }

    private static string? GuessColumn(IReadOnlyList<string> columns, string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            int index = FieldNames.FindIndex(columns, candidate);
            if (index >= 0) return columns[index];
        }
        return null;
    }

    /// <summary>Arama için katlama: Türkçe karakterler sadeleşir, harf/rakam dışı atılır.</summary>
    public static string Fold(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char ch in text.ToLower(CultureInfo.GetCultureInfo("tr-TR")))
        {
            char c = ch switch { 'ı' => 'i', 'ğ' => 'g', 'ü' => 'u', 'ş' => 's', 'ö' => 'o', 'ç' => 'c', 'â' => 'a', 'î' => 'i', 'û' => 'u', _ => ch };
            if (char.IsLetterOrDigit(c) || c == ' ') sb.Append(c);
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>Excel/CSV tablolarını dosya + değişiklik zamanına göre önbelleğe alır.</summary>
public sealed class DataSourceCache
{
    private readonly ConcurrentDictionary<string, (DateTime Stamp, DataSourceTable Table)> _tables = new(StringComparer.OrdinalIgnoreCase);

    public DataSourceTable Load(string fullPath, string? sheet)
    {
        var info = new FileInfo(fullPath);
        string key = fullPath + "|" + (sheet ?? "");
        if (_tables.TryGetValue(key, out var cached) && cached.Stamp == info.LastWriteTimeUtc) return cached.Table;
        try
        {
            var table = DataImporter.Import(fullPath, new ImportOptions { SheetName = sheet });
            _tables[key] = (info.LastWriteTimeUtc, table);
            return table;
        }
        catch (DataImportException ex)
        {
            throw new ToolException(ToolErrorCodes.InvalidArgument, $"Veri dosyası okunamadı: {ex.Message}");
        }
    }
}
