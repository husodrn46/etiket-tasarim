using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using AkelEtiket.Editor.Printing;
using AkelEtiket.PrintEngine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>Geçmiş penceresinde seçilen işlem: aynı işi tekrar basmak ya da yalnızca gönderilmemiş etiketleri basmak.</summary>
public sealed record PrintHistoryRequest(PrintHistoryEntry Entry, bool ResumeOnly);

/// <summary>
/// Baskı geçmişi penceresi: en yeni iş önce; tarih aralığı, şablon adı ve "yalnızca yarım kalanlar" süzgeçleri; seçili işin
/// ayrıntıları. "Tekrar bas" ve "Kaldığı yerden devam" pencereyi kapatır; ana pencere şablonu ve veriyi açıp baskı
/// penceresini aynı seçimle gösterir (baskı yine kullanıcı onayıyla yapılır).
/// </summary>
public sealed partial class PrintHistoryViewModel : ObservableObject
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly PrintHistoryStore _store;
    private List<PrintHistoryEntry> _all = [];

    public PrintHistoryViewModel(PrintHistoryStore store)
    {
        _store = store;
        Reload();
    }

    public ObservableCollection<PrintHistoryEntry> Entries { get; } = [];

    /// <summary>Şablon süzgeci için geçmişteki şablon adları.</summary>
    public ObservableCollection<string> TemplateNames { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details), nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(ReprintCommand), nameof(ResumeCommand))]
    public partial PrintHistoryEntry? SelectedEntry { get; set; }

    [ObservableProperty]
    public partial DateTime? FromDate { get; set; }

    [ObservableProperty]
    public partial DateTime? ToDate { get; set; }

    [ObservableProperty]
    public partial string TemplateFilter { get; set; } = "";

    [ObservableProperty]
    public partial bool OnlyIncomplete { get; set; }

    [ObservableProperty]
    public partial string CountText { get; private set; } = "";

    [ObservableProperty]
    public partial string? Problem { get; private set; }

    public bool HasSelection => SelectedEntry is not null;

    public string Folder => _store.Folder;

    /// <summary>Kullanıcının seçtiği işlem (pencere kapanınca ana pencere uygular).</summary>
    public PrintHistoryRequest? Request { get; private set; }

    /// <summary>Pencere kapanmalı (işlem seçildi).</summary>
    public event EventHandler? CloseRequested;

    partial void OnFromDateChanged(DateTime? value) => ApplyFilter();
    partial void OnToDateChanged(DateTime? value) => ApplyFilter();
    partial void OnTemplateFilterChanged(string value) => ApplyFilter();
    partial void OnOnlyIncompleteChanged(bool value) => ApplyFilter();

    [RelayCommand]
    public void Reload()
    {
        Problem = null;
        try
        {
            _all = [.. _store.ReadAll()];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Problem = "Baskı geçmişi okunamadı: " + ex.Message;
            _all = [];
        }
        TemplateNames.Clear();
        foreach (string name in _all.Select(e => e.DocumentName).Where(n => n.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(n => n, StringComparer.Create(Turkish, true)))
            TemplateNames.Add(name);
        ApplyFilter();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        FromDate = null;
        ToDate = null;
        TemplateFilter = "";
        OnlyIncomplete = false;
    }

    /// <summary>Süzgeçlere uyan kayıtlar (en yeni önce).</summary>
    public static IEnumerable<PrintHistoryEntry> Filter(IEnumerable<PrintHistoryEntry> entries, DateTime? from, DateTime? to, string? template, bool onlyIncomplete)
    {
        string needle = (template ?? "").Trim();
        foreach (var entry in entries)
        {
            var day = entry.Time.ToLocalTime().Date;
            if (from is { } f && day < f.Date) continue;
            if (to is { } t && day > t.Date) continue;
            if (onlyIncomplete && !entry.CanResume) continue;
            if (needle.Length > 0 && !Contains(entry.DocumentName, needle) && !Contains(Path.GetFileName(entry.TemplatePath ?? ""), needle)) continue;
            yield return entry;
        }
    }

    private static bool Contains(string text, string needle) => Turkish.CompareInfo.IndexOf(text, needle, CompareOptions.IgnoreCase) >= 0;

    private void ApplyFilter()
    {
        string? selected = SelectedEntry?.Id;
        Entries.Clear();
        foreach (var entry in Filter(_all, FromDate, ToDate, TemplateFilter, OnlyIncomplete)) Entries.Add(entry);
        SelectedEntry = Entries.FirstOrDefault(e => e.Id == selected) ?? Entries.FirstOrDefault();
        CountText = Entries.Count == _all.Count ? $"{_all.Count} kayıt" : $"{Entries.Count} / {_all.Count} kayıt";
    }

    private bool CanReprint() => SelectedEntry is { } e && (e.TemplatePath is not null || e.SnapshotFile is not null);

    private bool CanResume() => CanReprint() && SelectedEntry!.CanResume;

    [RelayCommand(CanExecute = nameof(CanReprint))]
    private void Reprint()
    {
        Request = new PrintHistoryRequest(SelectedEntry!, ResumeOnly: false);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume()
    {
        Request = new PrintHistoryRequest(SelectedEntry!, ResumeOnly: true);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Seçili işin ayrıntıları (rapor aşamaları, satırlar, hatalar).</summary>
    public string Details => SelectedEntry is { } e ? Describe(e) : "Soldan bir baskı işi seçin.";

    public static string Describe(PrintHistoryEntry e)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{e.TimeText} · {e.Title}");
        sb.AppendLine("Durum: " + e.StatusText);
        sb.AppendLine();
        sb.AppendLine("Şablon: " + (e.DocumentName.Length > 0 ? e.DocumentName : "(adsız)") +
                      (e.TemplatePath is { } path ? $" — {path}" : " — dosyaya kaydedilmemiş tasarım") +
                      (e.SnapshotFile is not null ? " (baskıdaki hâlinin kopyası saklı)" : ""));
        if (e.UseData)
        {
            sb.Append("Veri: " + (e.DataFile ?? "(bilinmiyor)"));
            if (!string.IsNullOrEmpty(e.DataSheet)) sb.Append($" · sayfa \"{e.DataSheet}\"");
            if (!string.IsNullOrEmpty(e.DataEncoding)) sb.Append(" · " + e.DataEncoding);
            sb.AppendLine();
            var rows = ResumePlanner.AllRows(e);
            sb.AppendLine($"Seçili satırlar: {rows.Count} satır, {rows.Sum(r => r.Copies)} etiket");
        }
        else
        {
            sb.AppendLine($"Veri kaynağı kullanılmadı · başlangıç no {e.StartNumber}");
        }
        sb.AppendLine($"Etiket: {e.TotalLabels} istendi · {e.LabelsSent} kuyruğa gönderildi · {e.LabelsRemaining} gönderilmedi · {e.LabelsFailed} hatalı");
        sb.AppendLine();
        string method = e.Method == PrintMethod.TsplRaw ? "TSPL doğrudan" : "Windows sürücüsü";
        sb.Append($"Yazıcı: {e.PrinterName} · {method}");
        if (e.Method == PrintMethod.TsplRaw)
            sb.Append(string.Create(Turkish, $" · yoğunluk {e.Density} · hız {e.Speed} · {(e.Rotate180 ? "180° (DIRECTION 0)" : "normal yön")} · ofset {e.OffsetXMm:0.###} / {e.OffsetYMm:0.###} mm"));
        if (e.SingleJob) sb.Append(" · tek parça");
        sb.AppendLine();
        if (e.ProfileName is not null) sb.AppendLine("Profil: " + e.ProfileName);
        sb.AppendLine();
        sb.AppendLine("Aşamalar:");
        sb.AppendLine(e.DataGenerated
            ? $"  ✓ Baskı verisi üretildi: {e.LabelsGenerated} etiket görüntüsü, toplam {e.CopiesTotal} adet"
            : "  ✗ Baskı verisi üretilemedi");
        sb.AppendLine(e.Spooled
            ? $"  ✓ Windows kuyruğuna gönderildi: {e.LabelsSpooled} etiket" + (e.JobIds.Count > 0 ? $" (iş no {string.Join(", ", e.JobIds)})" : "")
            : "  ✗ Windows kuyruğuna gönderilmedi");
        if (e.QueueMessage is not null) sb.AppendLine("  Kuyruk durumu: " + e.QueueMessage);
        if (e.Canceled) sb.AppendLine("  ! Kullanıcı tarafından iptal edildi");
        sb.AppendLine("  Fiziksel baskı bu kayıtla doğrulanamaz; etiketleri yazıcıdan kontrol edin.");
        if (e.SsccSummary is not null) sb.AppendLine(e.SsccSummary);
        if (e.SsccFirst is not null)
            sb.AppendLine(e.SsccSentCount > 1 ? $"SSCC aralığı: {e.SsccFirst} – {e.SsccLast} ({e.SsccSentCount} numara)" : $"SSCC: {e.SsccFirst}");
        if (e.CounterSummary is not null) sb.AppendLine(e.CounterSummary);
        if (e.CounterRanges is { Count: > 0 } ranges)
            sb.AppendLine("Sayaç aralıkları: " + string.Join("; ", ranges.Select(r => $"{r.Key} {r.Value}")));
        if (e.Errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Hatalar:");
            foreach (string error in e.Errors) sb.AppendLine("  " + error);
        }
        if (e.Skipped.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Atlanan satırlar / etiketler:");
            foreach (string skipped in e.Skipped.Take(30)) sb.AppendLine("  " + skipped);
            if (e.Skipped.Count > 30) sb.AppendLine($"  … ve {e.Skipped.Count - 30} tane daha");
        }
        if (e.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Uyarılar:");
            foreach (string warning in e.Warnings.Take(10)) sb.AppendLine("  " + warning);
        }
        var notSent = e.Items.Where(i => i.State != PrintItemState.Sent).ToList();
        if (notSent.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Gönderilmeyenler:");
            foreach (var item in notSent.Take(40))
            {
                string what = item.Row is { } row ? $"Satır {row}" : $"Etiket {item.Sequence}";
                string state = item.State == PrintItemState.Failed ? "hatalı" + (item.Problem is { } p ? ": " + p : "") : "gönderilmedi";
                sb.AppendLine($"  {what} × {item.Copies} — {state}");
            }
            if (notSent.Count > 40) sb.AppendLine($"  … ve {notSent.Count - 40} tane daha");
        }
        return sb.ToString().TrimEnd();
    }
}
