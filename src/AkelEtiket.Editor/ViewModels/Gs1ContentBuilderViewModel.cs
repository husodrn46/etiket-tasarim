using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.RegularExpressions;
using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.Gs1;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>GS1 içerik oluşturucudaki iletinin önemi.</summary>
public enum Gs1MessageKind
{
    /// <summary>İçerik geçerli.</summary>
    Ok,
    Info,
    Warning,
    /// <summary>Hata: "Uygula" kapalıdır.</summary>
    Error,
}

/// <summary>GS1 içerik oluşturucunun canlı doğrulama iletisi.</summary>
public sealed record Gs1BuilderMessage(Gs1MessageKind Kind, string Text)
{
    /// <summary>Görünüm dönüştürücülerinin anahtarı: "ok", "info", "warning", "error".</summary>
    public string Key => KeyOf(Kind);

    internal static string KeyOf(Gs1MessageKind kind) => kind switch
    {
        Gs1MessageKind.Ok => "ok",
        Gs1MessageKind.Warning => "warning",
        Gs1MessageKind.Error => "error",
        _ => "info",
    };
}

/// <summary>GS1 Firma Profili'nde kayıtlı, seçilebilir GTIN (14 haneye tamamlanmış).</summary>
public sealed record Gs1GtinChoice(string Gtin14, string Display);

/// <summary>GS1 içerik oluşturucuda bir satır: AI ve değeri (sabit değer ya da {Alan}).</summary>
public sealed partial class Gs1ContentRow : ObservableObject
{
    private static readonly HashSet<string> OwnGlnAis = ["412", "414", "415"];
    private readonly IReadOnlyList<string> _fieldNames;

    internal Gs1ContentRow(Gs1AiInfo? ai, string value, IReadOnlyList<string> fieldNames)
    {
        _fieldNames = fieldNames;
        Ai = ai;
        Value = value;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Hint), nameof(Suggestions))]
    public partial Gs1AiInfo? Ai { get; set; }

    [ObservableProperty] public partial string Value { get; set; } = "";

    /// <summary>Satırın sorunu ya da notu (yoksa null).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProblemKey))]
    public partial string? Problem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProblemKey))]
    public partial Gs1MessageKind ProblemKind { get; set; }

    /// <summary>Görünüm dönüştürücülerinin anahtarı ("error", "warning", "info").</summary>
    public string ProblemKey => Gs1BuilderMessage.KeyOf(ProblemKind);

    /// <summary>Seçili AI'nın biçimi ve Türkçe açıklaması.</summary>
    public string Hint => Ai is null ? "Listeden bir AI seçin (rakamını yazarak da arayabilirsiniz)." : $"{Ai.Format} · {Ai.Description}";

    /// <summary>Değer yerine yazılabilecek alanlar: AI'ya uygun yerleşik alanlar ve veri kaynağının sütunları.</summary>
    public IReadOnlyList<string> Suggestions
    {
        get
        {
            var list = new List<string>();
            if (Ai is { } ai)
            {
                if (ai.Ai == "00") list.Add("{SSCC}");
                if (ai.IsDate) list.Add("{TARIH:yyMMdd}");
                if (OwnGlnAis.Contains(ai.Ai)) list.Add("{GLN}");
            }
            list.AddRange(_fieldNames.Select(FieldTemplate.MakePlaceholder));
            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>Değer bir veri alanı içeriyor mu ({Parti}, {SSCC}...)?</summary>
    public bool HasFields => FieldTemplate.ContainsFields(Value);

    internal string TrimmedValue => (Value ?? "").Trim();
}

/// <summary>
/// "GS1 içerik oluşturucu": GS1-128, GS1 DataMatrix ve GS1 QR içeriğini AI satırlarından kurar. Her satırda AI listeden
/// seçilir (Türkçe açıklamalı), değer sabit yazılır ya da {Alan} olarak veri kaynağına bağlanır. Kayıtlı GTIN'ler ve
/// (00){SSCC} kısayolu sunulur; doğrulama barkod motorunun GS1 kurallarıyla canlı yapılır. Sonuç
/// <see cref="Content"/>'tir ("(01)…(17)…(10)…"); FNC1 ve ayırıcıları barkod motoru ekler.
/// </summary>
public sealed partial class Gs1ContentBuilderViewModel : ObservableObject
{
    /// <summary>Var olan içeriği satırlara ayırır; değerlerde {Alan} bulunabilir.</summary>
    [GeneratedRegex(@"\((\d{2,4})\)([^()]*)", RegexOptions.CultureInvariant)]
    private static partial Regex RowPattern();

    /// <summary>Bir GTIN'e ((01) ya da (02)) bağlı olması gereken nitelik AI'ları.</summary>
    private static readonly HashSet<string> TradeItemAttributes = ["10", "11", "13", "15", "16", "17", "20", "30"];

    private readonly IReadOnlyList<string> _fieldNames;
    private readonly HashSet<string> _knownFields;

    /// <param name="symbology">GS1 türü: GS1-128, GS1 DataMatrix ya da GS1 QR.</param>
    /// <param name="content">Barkodun şimdiki içeriği (satırlara ayrılır).</param>
    /// <param name="profile">GS1 Firma Profili (kayıtlı GTIN'ler için; null olabilir).</param>
    /// <param name="dataFields">Yüklü veri kaynağının sütunları ({Alan} önerileri için).</param>
    public Gs1ContentBuilderViewModel(BarcodeType symbology, string? content, Gs1CompanyProfile? profile, IEnumerable<string>? dataFields = null)
    {
        if (!BarcodeElement.IsGs1(symbology))
            throw new ArgumentException("GS1 içerik oluşturucu yalnızca GS1-128, GS1 DataMatrix ve GS1 QR içindir.", nameof(symbology));
        Symbology = symbology;
        _fieldNames = (dataFields ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _knownFields = _fieldNames
            .Concat(BuiltInFieldSource.FieldNamesList)
            .Concat(Gs1FieldSource.FieldNamesList)
            .Select(FieldNames.Normalize)
            .ToHashSet();
        Gtins = BuildGtinChoices(profile);
        Rows.CollectionChanged += OnRowsChanged;
        LoadContent(content);
        Refresh();
        // İçerikteki GTIN kayıtlıysa listede seçili gelsin.
        SelectedGtin = Gtins.FirstOrDefault(g => Rows.Any(r => r.Ai?.Ai is "01" or "02" && r.TrimmedValue == g.Gtin14));
    }

    public BarcodeType Symbology { get; }

    public string SymbologyName => BarcodeCatalog.GetDisplayName(Symbology);

    public string Title => $"GS1 içerik oluşturucu — {SymbologyName}";

    /// <summary>Seçilebilir AI'lar (barkod motorunun bildiği tümü, Türkçe açıklamalı).</summary>
    public IReadOnlyList<Gs1AiInfo> AiChoices => Gs1AiCatalog.All;

    public ObservableCollection<Gs1ContentRow> Rows { get; } = [];

    /// <summary>GS1 Firma Profili'ndeki geçerli GTIN'ler.</summary>
    public IReadOnlyList<Gs1GtinChoice> Gtins { get; }

    public bool HasGtins => Gtins.Count > 0;

    public string GtinNote => HasGtins
        ? "Seçtiğiniz GTIN (01) ya da (02) satırına yazılır; satır yoksa eklenir."
        : "GS1 Firma Profili'nde kayıtlı GTIN yok. Ayarlar → GS1 Firma Profili… bölümünden ekleyebilirsiniz.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseGtinAs01Command), nameof(UseGtinAs02Command))]
    public partial Gs1GtinChoice? SelectedGtin { get; set; }

    /// <summary>Var olan içerik satırlara ayrılamadıysa açıklama (yoksa null).</summary>
    public string? LoadNote { get; private set; }

    /// <summary>Oluşan içerik: "(01)08691234567890(10){Parti}".</summary>
    [ObservableProperty] public partial string Content { get; set; } = "";

    /// <summary>Canlı doğrulama iletileri.</summary>
    public ObservableCollection<Gs1BuilderMessage> Messages { get; } = [];

    /// <summary>İçerikte hata yok: "Uygula" açık.</summary>
    [ObservableProperty] public partial bool CanAccept { get; set; }

    [RelayCommand]
    private void AddRow() => Rows.Add(CreateRow(null, ""));

    [RelayCommand]
    private void RemoveRow(Gs1ContentRow? row)
    {
        if (row is not null) Rows.Remove(row);
    }

    [RelayCommand]
    private void MoveRowUp(Gs1ContentRow? row)
    {
        int index = row is null ? -1 : Rows.IndexOf(row);
        if (index > 0) Rows.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveRowDown(Gs1ContentRow? row)
    {
        int index = row is null ? -1 : Rows.IndexOf(row);
        if (index >= 0 && index < Rows.Count - 1) Rows.Move(index, index + 1);
    }

    /// <summary>(00){SSCC} satırı: numara baskı anında GS1 Firma Profili'nden ayrılır.</summary>
    [RelayCommand]
    private void AddSscc() => SetAi("00", FieldTemplate.MakePlaceholder("SSCC"), 0);

    [RelayCommand(CanExecute = nameof(HasSelectedGtin))]
    private void UseGtinAs01() => SetAi("01", SelectedGtin!.Gtin14, IndexAfterSscc());

    [RelayCommand(CanExecute = nameof(HasSelectedGtin))]
    private void UseGtinAs02() => SetAi("02", SelectedGtin!.Gtin14, IndexAfterSscc());

    private bool HasSelectedGtin() => SelectedGtin is not null;

    /// <summary>Satırlardan içeriği kurar: her satır "(AI)değer", sırası korunur. AI'sı seçilmemiş satır atlanır.</summary>
    public static string Compose(IEnumerable<(string Ai, string Value)> rows) =>
        string.Concat(rows.Select(r => $"({r.Ai}){(r.Value ?? "").Trim()}"));

    /// <summary>
    /// AI birlikteliği uyarıları (GS1 Genel Spesifikasyonları): (01) ile (02) birlikte kullanılmaz; (02) ile (37)
    /// birbirini gerektirir; parti, tarih, varyant ve adet bir GTIN'e, (21) seri numarası (01)'e bağlıdır.
    /// </summary>
    public static IReadOnlyList<string> PairingWarnings(IReadOnlyCollection<string> ais)
    {
        var set = ais.ToHashSet(StringComparer.Ordinal);
        var warnings = new List<string>();
        bool hasGtin = set.Contains("01") || set.Contains("02");
        if (set.Contains("01") && set.Contains("02"))
            warnings.Add("(01) ve (02) birlikte kullanılmaz: koli bir ticari ürünse (01), içindeki ürünleri gösteriyorsa (02) kullanılır.");
        if (set.Contains("02") && !set.Contains("37"))
            warnings.Add("(02) içerik GTIN'i, (37) adet ile birlikte kullanılır.");
        if (set.Contains("37") && !set.Contains("02"))
            warnings.Add("(37) adet, (02) içerik GTIN'i ile birlikte kullanılır.");
        foreach (string ai in ais.Distinct(StringComparer.Ordinal))
        {
            bool attribute = TradeItemAttributes.Contains(ai) || IsMeasure(ai);
            if (attribute && !hasGtin)
                warnings.Add($"({ai}) bir ürün numarasına bağlıdır: (01) GTIN ya da (02) içerik GTIN'i de ekleyin.");
        }
        if (set.Contains("21") && !set.Contains("01"))
            warnings.Add("(21) seri numarası (01) GTIN ile birlikte kullanılır.");
        return warnings;
    }

    private static bool IsMeasure(string ai) =>
        ai.Length == 4 && int.TryParse(ai.AsSpan(0, 3), out int stem) && stem is >= 310 and <= 369;

    private static IReadOnlyList<Gs1GtinChoice> BuildGtinChoices(Gs1CompanyProfile? profile)
    {
        var list = new List<Gs1GtinChoice>();
        foreach (var item in profile?.RegisteredGtins ?? [])
        {
            string? gtin14 = Gs1Identifiers.ToGtin14(item.Gtin);
            if (gtin14 is null || list.Any(c => c.Gtin14 == gtin14)) continue;
            string extra = string.Join(" · ", new[] { item.ProductCode, item.Description }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
            list.Add(new Gs1GtinChoice(gtin14, extra.Length > 0 ? $"{gtin14} — {extra}" : gtin14));
        }
        return list;
    }

    private void LoadContent(string? content)
    {
        string text = (content ?? "").Trim();
        if (text.Length == 0)
        {
            Rows.Add(CreateRow(Gs1AiCatalog.Find("01"), ""));
            return;
        }
        var rows = new List<Gs1ContentRow>();
        var unknown = new List<string>();
        int consumed = 0;
        foreach (Match match in RowPattern().Matches(text))
        {
            if (match.Index != consumed) break;
            consumed = match.Index + match.Length;
            string ai = match.Groups[1].Value;
            var info = Gs1AiCatalog.Find(ai);
            if (info is null) unknown.Add($"({ai})");
            rows.Add(CreateRow(info, match.Groups[2].Value.Trim()));
        }
        if (consumed != text.Length || rows.Count == 0)
        {
            LoadNote = $"Şimdiki içerik \"{Shorten(text)}\" AI satırlarına ayrılamadı. Uygula'ya basarsanız yerine buradaki içerik yazılır.";
            Rows.Add(CreateRow(Gs1AiCatalog.Find("01"), ""));
            return;
        }
        if (unknown.Count > 0)
            LoadNote = $"{string.Join(", ", unknown)} bilinen bir GS1 AI değil; bu satırlar için listeden AI seçin ya da satırı silin.";
        foreach (var row in rows) Rows.Add(row);
    }

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..57] + "…";

    private Gs1ContentRow CreateRow(Gs1AiInfo? ai, string value) => new(ai, value, _fieldNames);

    private int IndexAfterSscc()
    {
        int index = Rows.ToList().FindIndex(r => r.Ai?.Ai == "00");
        return index + 1;
    }

    /// <summary>AI'nın satırı varsa değerini değiştirir, yoksa verilen sıraya yeni satır ekler (boş, AI'sız satır kullanılır).</summary>
    private void SetAi(string ai, string value, int insertAt)
    {
        var existing = Rows.FirstOrDefault(r => r.Ai?.Ai == ai);
        if (existing is not null)
        {
            existing.Value = value;
            return;
        }
        var info = Gs1AiCatalog.Find(ai) ?? throw new InvalidOperationException($"({ai}) AI listesinde yok.");
        // Boş satır yeniden kullanılır: AI'sız boş satır ya da (01)/(02) için öbürünün boş satırı (başlangıçtaki boş (01) gibi).
        string? sibling = ai switch { "01" => "02", "02" => "01", _ => null };
        var reusable = Rows.FirstOrDefault(r => r.TrimmedValue.Length == 0 && (r.Ai is null || (sibling is not null && r.Ai.Ai == sibling)));
        if (reusable is not null)
        {
            reusable.Ai = info;
            reusable.Value = value;
            return;
        }
        Rows.Insert(Math.Clamp(insertAt, 0, Rows.Count), CreateRow(info, value));
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (Gs1ContentRow row in e.OldItems) row.PropertyChanged -= OnRowPropertyChanged;
        if (e.NewItems is not null)
            foreach (Gs1ContentRow row in e.NewItems) row.PropertyChanged += OnRowPropertyChanged;
        Refresh();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Gs1ContentRow.Ai) or nameof(Gs1ContentRow.Value)) Refresh();
    }

    /// <summary>Satırları ve bütün içeriği yeniden doğrular, <see cref="Content"/>'i kurar.</summary>
    private void Refresh()
    {
        var messages = new List<Gs1BuilderMessage>();
        foreach (var row in Rows) EvaluateRow(row);
        var complete = Rows.Where(r => r.Ai is not null).Select(r => (r.Ai!.Ai, r.TrimmedValue)).ToList();
        Content = Compose(complete);

        if (Rows.Count == 0)
            messages.Add(new(Gs1MessageKind.Error, "En az bir AI ekleyin."));
        int errorRows = Rows.Count(r => r.Problem is not null && r.ProblemKind == Gs1MessageKind.Error);
        if (errorRows > 0)
            messages.Add(new(Gs1MessageKind.Error, $"{errorRows} satırda hata var; içerik ancak hatalar düzeltilince uygulanabilir."));
        foreach (var dup in complete.GroupBy(r => r.Ai).Where(g => g.Count() > 1))
            messages.Add(new(Gs1MessageKind.Error, $"({dup.Key}) birden fazla kez kullanılmış."));

        // Bütün içerik ancak satırlar tek tek geçerliyse denetlenir; alan varsa değerler baskıda belli olur.
        if (messages.Count == 0)
        {
            bool hasFields = Rows.Any(r => r.HasFields);
            if (hasFields)
            {
                messages.Add(new(Gs1MessageKind.Info,
                    "İçerikte veri alanı var: alanların değerleri önizlemede ve baskıda, gerçek verilerle GS1 kurallarına göre yeniden denetlenir."));
            }
            else
            {
                var result = BarcodeValidator.Validate(Symbology, Content);
                foreach (string error in result.Errors) messages.Add(new(Gs1MessageKind.Error, error));
                if (result.IsValid)
                    messages.Add(new(Gs1MessageKind.Ok, $"Geçerli {SymbologyName} içeriği. Okunur metin: {result.HumanReadableText}"));
            }
        }

        var ais = complete.Select(r => r.Ai).ToList();
        foreach (string warning in PairingWarnings(ais)) messages.Add(new(Gs1MessageKind.Warning, warning));
        if (HasVariableBeforeFixed(ais))
            messages.Add(new(Gs1MessageKind.Info,
                "İpucu: sabit uzunluklu AI'ları ((01), (17) gibi) başa, değişken uzunlukluları ((10), (21) gibi) sona almak ayırıcı sayısını azaltır ve sembolü küçültür."));
        if (complete.Any(r => r.Ai == "00" && FieldTemplate.GetFieldNames(r.TrimmedValue).Any(n => FieldNames.Normalize(n) == "sscc")))
            messages.Add(new(Gs1MessageKind.Info,
                "{SSCC}: numara her etiket için baskı anında GS1 Firma Profili'nden ayrılır ve kaydedilir (önizlemede 18 sıfır görünür). Firma öneki doğrulanmış olmalıdır."));
        if (ais.Contains("00") && Symbology != BarcodeType.Gs1_128)
            messages.Add(new(Gs1MessageKind.Info,
                "Lojistik etiket standardı SSCC için GS1-128 barkod ister; GS1 DataMatrix / GS1 QR'ı ona ek olarak kullanın."));

        Messages.Clear();
        foreach (var message in messages) Messages.Add(message);
        CanAccept = messages.All(m => m.Kind != Gs1MessageKind.Error);
    }

    private static bool HasVariableBeforeFixed(IReadOnlyList<string> ais)
    {
        bool seenVariable = false;
        foreach (string ai in ais)
        {
            bool variable = BarcodeEngine.Gs1.RequiresSeparator(ai);
            if (!variable && seenVariable) return true;
            seenVariable |= variable;
        }
        return false;
    }

    private void EvaluateRow(Gs1ContentRow row)
    {
        string value = row.TrimmedValue;
        (Gs1MessageKind kind, string? problem) = row.Ai switch
        {
            null => (Gs1MessageKind.Error, "AI seçin."),
            _ when value.Length == 0 => (Gs1MessageKind.Error, "Değer boş."),
            _ when value.IndexOfAny(['(', ')']) >= 0 => (Gs1MessageKind.Error, "Değerde parantez kullanılamaz: parantez AI'ları ayırır."),
            { } ai when row.HasFields => EvaluateFields(ai, value),
            { } ai => EvaluateFixed(ai, value),
        };
        row.ProblemKind = kind;
        row.Problem = problem;
    }

    private (Gs1MessageKind, string?) EvaluateFields(Gs1AiInfo ai, string value)
    {
        var names = FieldTemplate.GetFieldNames(value);
        if (names.Any(n => FieldNames.Normalize(n) == "sscc") && ai.Ai != "00")
            return (Gs1MessageKind.Error, "{SSCC} yalnızca (00) AI'sıyla kullanılır.");
        if (_fieldNames.Count > 0)
        {
            var missing = names.Where(n => !_knownFields.Contains(FieldNames.Normalize(n))).ToList();
            if (missing.Count > 0)
                return (Gs1MessageKind.Warning, $"Veri kaynağında {string.Join(", ", missing.Select(FieldTemplate.MakePlaceholder))} sütunu yok.");
        }
        return (Gs1MessageKind.Info, $"Değer baskıda {string.Join(", ", names.Select(FieldTemplate.MakePlaceholder))} alanından gelir.");
    }

    private (Gs1MessageKind, string?) EvaluateFixed(Gs1AiInfo ai, string value)
    {
        var parsed = BarcodeEngine.Gs1.Parse($"({ai.Ai}){value}", SymbologyName, int.MaxValue);
        return parsed.Errors.Count > 0 ? (Gs1MessageKind.Error, parsed.Errors[0]) : (Gs1MessageKind.Info, null);
    }
}
