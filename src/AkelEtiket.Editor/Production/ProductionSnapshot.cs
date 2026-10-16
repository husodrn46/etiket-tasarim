using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;
using AkelEtiket.Editor.Printing;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;
using AkelEtiket.Gs1;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.Production;

/// <summary>One immutable job input shared by validation and export. No serial numbers are reserved.</summary>
public sealed record ProductionSnapshot(LabelProject Project, IReadOnlyList<PrintItem> Items,
    double DotsPerMm, IImageSourceResolver? Images, int SkippedRows)
{
    public const int MaximumLabels = 10_000;

    public static ProductionSnapshot Capture(MainViewModel main, bool useData, int copies, int startNumber)
    {
        if (copies is < 1 or > MaximumLabels || startNumber < 1 || startNumber > int.MaxValue - MaximumLabels)
            throw new InvalidOperationException(L10n.T("Adet 1–10.000 arasında, başlangıç numarası geçerli bir pozitif sayı olmalıdır."));
        var session = main.Session;
        if (CounterFields.DocumentUsesCounters(session.Document) || Gs1FieldSource.DocumentUsesSscc(session.Document))
            throw new InvalidOperationException(L10n.T("Sayaç veya SSCC içeren işler için mevcut Yazdır ekranını kullanın; üretim alanı numara ayırmaz."));
        if (useData && !main.Data.HasData)
            throw new InvalidOperationException(L10n.T("Önce bir Excel veya CSV dosyası yükleyin ya da veri kullanımını kapatın."));
        long count = useData ? main.Data.Rows.Where(r => r.IsSelected).Sum(r => (long)r.Copies) : copies;
        if (count is < 1 or > MaximumLabels)
            throw new InvalidOperationException(L10n.T("Bir üretim işi 1–10.000 etiket içermelidir."));
        if (main.Data.IsLoading) throw new InvalidOperationException(L10n.T("Veri yüklemesi tamamlandıktan sonra yeniden deneyin."));
        if (session.Document.Elements.Count > 1000 || session.Document.Elements.Sum(e => (long)(e switch { TextElement t => t.Text.Length, BarcodeElement b => b.Content.Length, _ => 0 })) > 1_000_000)
            throw new InvalidOperationException(L10n.T("Bu tasarımın alan belleği sınırı aşıldı; işi sadeleştirin."));
        var tokens = Tokens(session.Document).Distinct().Take(513).ToArray();
        long sourceWork = useData ? (long)main.Data.Rows.Count * (session.Document.Elements.Count + tokens.Length + (main.Data.Table?.Issues.Count ?? 0)) : 0;
        if (tokens.Length > 512 || sourceWork > 2_000_000)
            throw new InvalidOperationException(L10n.T("Bu işin alan belleği sınırı aşıldı; işi daha küçük parçalara ayırın."));
        if (useData) main.Data.ValidateAgainst(session.Document);
        var plan = PrintPlanner.Plan(session.Document, main.Data, useData, copies, startNumber, DateTime.Now);
        var assets = new AssetStore();
        long assetBytes = session.Assets.Assets.Sum(a => a.Size);
        if (assetBytes > 64L * 1024 * 1024)
            throw new InvalidOperationException(L10n.T("Bu işin görselleri 64 MiB sınırını aşıyor."));
        foreach (var asset in session.Assets.Assets) assets.AddExisting(new(asset.Id, asset.FileName, asset.Data.ToArray()));
        var project = new LabelProject(ModelJson.Clone(session.Document), assets);
        var budget = new FieldBudget();
        return new(project, plan.Items.Select(i => i with { Fields = Freeze(i.Fields, tokens, budget) }).ToArray(),
            session.DotsPerMm, session.ImageResolver is { } images ? new SnapshotImageResolver(images) : null, plan.Skipped.Count);
    }

    private static IFieldValueSource Freeze(IFieldValueSource source, IReadOnlyList<(string Name, string? Format)> tokens, FieldBudget budget)
    {
        if (source is BuiltInFieldSource builtIn) return new BuiltInFieldSource(builtIn.Sequence, builtIn.Now);
        if (source is CompositeFieldSource composite) return new CompositeFieldSource(composite.Sources.Select(s => Freeze(s, tokens, budget)).ToArray());
        return new FrozenFields(source, tokens, budget);
    }

    private static IEnumerable<(string Name, string? Format)> Tokens(LabelDocument document)
    {
        foreach (var element in document.Elements)
        {
            string text = element switch { TextElement t => t.Text, BarcodeElement b => b.Content,
                ImageElement i when !string.IsNullOrEmpty(i.SourceField) => "{" + i.SourceField.Trim('{', '}') + "}", _ => "" };
            foreach (var token in FieldTemplate.GetTokens(text))
            {
                yield return (token.Name, token.Format);
                yield return (token.Name, null);
                yield return (token.BaseName, token.Expression.ValueFormat);
                yield return (token.BaseName, null);
            }
            if (element.VisibleWhen is { } condition) yield return (condition.Field.Trim('{', '}'), null);
        }
    }

    private sealed class FieldBudget
    {
        private long _reads, _characters;
        public void Read() { if (++_reads > 200_000) throw new InvalidOperationException(L10n.T("Bu işin alan belleği sınırı aşıldı; işi daha küçük parçalara ayırın.")); }
        public void Add(string value) { _characters += value.Length; if (_characters > 8_000_000) throw new InvalidOperationException(L10n.T("Bu işin alan belleği sınırı aşıldı; işi daha küçük parçalara ayırın.")); }
    }

    private sealed class FrozenFields : IFieldValueSource, IDateFieldSource
    {
        private readonly Dictionary<(string, string?), (bool Exists, string Value)> _values = [];
        private readonly Dictionary<string, (bool Found, DateTime Date, bool Exists)> _dates = [];
        public FrozenFields(IFieldValueSource source, IReadOnlyList<(string Name, string? Format)> tokens, FieldBudget budget)
        {
            foreach (var (name, format) in tokens)
            {
                budget.Read();
                bool exists = source.TryGetValue(name, format, out var value);
                if (exists) { budget.Add(value); _values[(FieldNames.Normalize(name), format)] = (true, value); }
                bool found = FieldDates.TryGetDate(source, name, out var date, out var dateExists);
                if (dateExists) _dates[FieldNames.Normalize(name)] = (found, date, true);
            }
        }
        public bool TryGetValue(string name, string? format, out string value)
        {
            var entry = _values.GetValueOrDefault((FieldNames.Normalize(name), format));
            value = entry.Value ?? ""; return entry.Exists;
        }
        public bool TryGetDate(string name, out DateTime date, out bool exists)
        {
            var entry = _dates.GetValueOrDefault(FieldNames.Normalize(name));
            date = entry.Date; exists = entry.Exists; return entry.Found;
        }
    }
}

/// <summary>Freezes external image bytes on first access, with explicit bounded memory.</summary>
public sealed class SnapshotImageResolver(IImageSourceResolver source, long maxBytes = 64L * 1024 * 1024, int maxEntries = 512) : IImageSourceResolver
{
    private readonly Dictionary<string, (ImageAsset? Asset, string? Error)> _cache = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private long _bytes;
    public ImageAsset? Resolve(string value, out string? error)
    {
        lock (_sync)
        {
            if (_cache.TryGetValue(value, out var entry)) { error = entry.Error; return entry.Asset; }
            if (_cache.Count >= maxEntries) { error = L10n.T("Bu işin dış görselleri bellek sınırına ulaştı; işi daha küçük parçalara ayırın."); return null; }
            var asset = source.Resolve(value, out error);
            if (asset is not null)
            {
                if (asset.Size > maxBytes - _bytes) { error = L10n.T("Bu işin dış görselleri bellek sınırına ulaştı; işi daha küçük parçalara ayırın."); asset = null; }
                else { asset = new ImageAsset(asset.Id, asset.FileName, asset.Data.ToArray()); _bytes += asset.Size; }
            }
            _cache[value] = (asset, error); return asset;
        }
    }
}
