using System.Collections.ObjectModel;
using System.Globalization;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Printing;
using AkelEtiket.Editor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>Sayaç listesindeki bir satır.</summary>
public sealed record CounterRow(string Name, long Next, long Step, string LastUsedText, string LastRange, bool UsedByDocument, bool Exists)
{
    public string UsageText => UsedByDocument ? "Bu etikette" : "";
}

/// <summary>
/// Kalıcı sayaçlar penceresi: sayaçları listeler, sıradaki değeri/artışı ayarlar, sıfırlar, siler. Değişiklikler sayaç
/// kilidiyle yapılır; baskı sürerken bekler ya da açıklamalı uyarı verir.
/// </summary>
public sealed partial class CountersViewModel : ObservableObject
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly CounterStore _store;
    private readonly IDialogService _dialogs;
    private readonly LabelDocument? _document;

    public CountersViewModel(CounterStore store, IDialogService dialogs, LabelDocument? document)
    {
        _store = store;
        _dialogs = dialogs;
        _document = document;
        Refresh();
    }

    public ObservableCollection<CounterRow> Counters { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetNextCommand), nameof(SetStepCommand), nameof(ResetCommand), nameof(DeleteCommand))]
    public partial CounterRow? SelectedCounter { get; set; }

    [ObservableProperty]
    public partial string? Problem { get; private set; }

    public string FilePath => _store.FilePath;

    /// <summary>Açık etiketin sayaç kullanımı ve yazım kılavuzu.</summary>
    public string UsageText
    {
        get
        {
            var used = _document is null ? [] : CounterFields.CountersUsedBy(_document);
            string help = "Metin ya da barkoda {SAYAC} (bu etiketin kendi sayacı) veya {SAYAC.Koli} gibi adlı sayaç yazın; " +
                          "biçim için {SAYAC:000000}. Önizleme sıradaki değeri gösterir ama sayacı ilerletmez; sayaç yalnızca " +
                          "yazıcı kuyruğuna gönderilen etiket sayısı kadar ilerler.";
            return used.Count == 0
                ? "Bu etikette sayaç alanı yok. " + help
                : "Bu etiketin kullandığı sayaçlar: " + string.Join(", ", used.Select(u => $"\"{u}\"")) + ". " + help;
        }
    }

    [RelayCommand]
    public void Refresh()
    {
        string? selected = SelectedCounter?.Name;
        Counters.Clear();
        Problem = null;
        IReadOnlyList<CounterState> stored;
        try
        {
            stored = _store.List();
        }
        catch (CounterException ex)
        {
            Problem = ex.Message;
            stored = [];
        }
        var used = _document is null ? [] : CounterFields.CountersUsedBy(_document);
        foreach (var counter in stored)
        {
            bool inDocument = used.Contains(counter.Name, StringComparer.OrdinalIgnoreCase);
            Counters.Add(new CounterRow(counter.Name, counter.Next, Math.Max(1, counter.Step),
                counter.LastUsedAt is { } at ? at.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Turkish) : "henüz kullanılmadı",
                counter.LastRange ?? "", inDocument, Exists: true));
        }
        // Etikette kullanılan ama henüz hiç basılmamış sayaçlar da görünsün (1'den başlar).
        foreach (string name in used.Where(u => !stored.Any(c => string.Equals(c.Name, u, StringComparison.OrdinalIgnoreCase))))
            Counters.Add(new CounterRow(name, 1, 1, "henüz kullanılmadı", "", UsedByDocument: true, Exists: false));
        SelectedCounter = Counters.FirstOrDefault(c => string.Equals(c.Name, selected, StringComparison.OrdinalIgnoreCase)) ?? Counters.FirstOrDefault();
    }

    private bool HasSelection() => SelectedCounter is not null;

    [RelayCommand]
    private void Add()
    {
        string? name = _dialogs.Prompt("Yeni sayaç", "Sayaç adı (etikette {SAYAC.Ad} olarak kullanılır):", "Koli");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (name.Contains('}') || name.Contains('{') || name.Contains(':'))
        {
            _dialogs.ShowMessage("Sayaç", "Sayaç adında { } : karakterleri kullanılamaz.", MessageKind.Warning);
            return;
        }
        if (Counters.Any(c => c.Exists && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            _dialogs.ShowMessage("Sayaç", $"\"{name}\" sayacı zaten var.", MessageKind.Warning);
            return;
        }
        long? start = AskNumber("Yeni sayaç", $"\"{name}\" sayacının ilk değeri:", 1, min: 0);
        if (start is null) return;
        if (Run(() => _store.Set(name, start.Value, 1))) Select(name);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SetNext()
    {
        if (SelectedCounter is not { } row) return;
        long? next = AskNumber("Sıradaki değer", $"\"{row.Name}\" sayacının sıradaki değeri (bir sonraki etiket bu numarayı alır):", row.Next, min: 0);
        if (next is null || next == row.Next) return;
        if (next < row.Next && row.Exists && !_dialogs.Confirm("Sayaç",
                $"Değer {row.Next} → {next} olarak geri alınacak. Daha önce basılmış numaralar tekrar basılabilir. Devam edilsin mi?",
                "Geri al", "Vazgeç"))
            return;
        if (Run(() => _store.Set(row.Name, next.Value, row.Step))) Select(row.Name);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SetStep()
    {
        if (SelectedCounter is not { } row) return;
        long? step = AskNumber("Artış", $"\"{row.Name}\" sayacı her etikette kaç artsın?", row.Step, min: 1);
        if (step is null || step == row.Step) return;
        if (Run(() => _store.Set(row.Name, row.Next, step.Value))) Select(row.Name);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Reset()
    {
        if (SelectedCounter is not { } row) return;
        if (!_dialogs.Confirm("Sayacı sıfırla", $"\"{row.Name}\" sayacı 1'den yeniden başlayacak. Daha önce basılmış numaralar tekrar basılabilir. Devam edilsin mi?", "Sıfırla", "Vazgeç"))
            return;
        if (Run(() => _store.Set(row.Name, 1, row.Step))) Select(row.Name);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (SelectedCounter is not { Exists: true } row) return;
        string warning = row.UsedByDocument ? " Bu etiket sayacı kullanıyor; bir sonraki baskıda sayaç 1'den başlar." : "";
        if (!_dialogs.Confirm("Sayacı sil", $"\"{row.Name}\" sayacı silinsin mi?{warning}", "Sil", "Vazgeç")) return;
        Run(() => _store.Delete(row.Name));
    }

    private long? AskNumber(string title, string message, long current, long min)
    {
        string? text = _dialogs.Prompt(title, message, current.ToString(CultureInfo.InvariantCulture));
        if (text is null) return null;
        text = text.Trim().Replace(".", "").Replace(" ", "");
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) || value < min || value > 999_999_999_999)
        {
            _dialogs.ShowMessage(title, $"Geçerli bir tam sayı girin ({min} veya büyük).", MessageKind.Warning);
            return null;
        }
        return value;
    }

    private bool Run(Action action)
    {
        try
        {
            action();
            Refresh();
            return true;
        }
        catch (CounterException ex)
        {
            _dialogs.ShowMessage("Sayaç", ex.Message, MessageKind.Warning);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowMessage("Sayaç kaydedilemedi", ex.Message, MessageKind.Error);
            return false;
        }
    }

    private void Select(string name) =>
        SelectedCounter = Counters.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) ?? SelectedCounter;
}
