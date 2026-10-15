using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;
using AkelEtiket.DataImport;
using AkelEtiket.Editor.Printing;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.Templates;
using AkelEtiket.Gs1;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>Baskı araçları: baskı geçmişi (tekrar bas / kaldığı yerden devam), PDF olarak kaydet, sayaçlar.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Önizleme alanlarını yeniler (ör. sayaç değeri değiştiğinde).</summary>
    internal void RefreshPreviewFields() => ApplyPreviewFields();

    /// <summary>Kalıcı sayaçlar penceresi.</summary>
    [RelayCommand]
    private void OpenCounters()
    {
        Dialogs.ShowDialog(new CountersViewModel(CounterIntegration.Store, Dialogs, Session.Document));
        ApplyPreviewFields();
    }

    // ───────────────────────── Baskı geçmişi ─────────────────────────

    [RelayCommand]
    private async Task OpenPrintHistory()
    {
        var history = new PrintHistoryViewModel(PrintingServices.History, Settings.FavoritePrintJobIds, ids => UpdatePreferences(s => s.FavoritePrintJobIds = ids));
        Dialogs.ShowDialog(history);
        if (history.Request is { } request) await ReprintFromHistoryAsync(request.Entry, request.ResumeOnly);
    }

    /// <summary>
    /// Geçmişteki işi yeniden hazırlar: şablonu (değiştiyse sorarak; dosya yoksa baskıdaki kopyasını) ve veri dosyasını
    /// açar, satır seçimini ve adetleri geri yükler, baskı penceresini aynı seçimle açar. Kaldığı yerden devamda yalnızca
    /// gönderilmemiş etiketler seçilir. Baskı ancak kullanıcı baskı penceresinde "Yazdır" derse yapılır.
    /// </summary>
    public async Task<bool> ReprintFromHistoryAsync(PrintHistoryEntry entry, bool resumeOnly)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string action = resumeOnly ? L10n.T("Kaldığı yerden devam") : L10n.T("Tekrar bas");
        if (resumeOnly && !entry.CanResume)
        {
            Dialogs.ShowMessage(action, L10n.T("Bu işte gönderilmemiş etiket yok."), MessageKind.Info);
            return false;
        }

        // 1) Şablon
        var notes = new List<string>();
        string? snapshot = PrintingServices.History.SnapshotPath(entry);
        string? template = entry.TemplatePath is { Length: > 0 } path && File.Exists(path) ? path : null;
        if (template is null && snapshot is null)
        {
            Dialogs.ShowMessage(action, L10n.T("Bu baskının şablonu bulunamadı") + (entry.TemplatePath is { } missing ? $":\n{missing}" : ".") +
                                        L10n.T("\n\nBaskıdaki tasarımın kopyası da yok (eski kayıt ya da test baskısı)."), MessageKind.Warning);
            return false;
        }
        bool templateChanged = template is not null && entry.TemplateHash is not null && PrintHistoryStore.HashFile(template) != entry.TemplateHash;
        bool useSnapshot;
        if (template is null)
        {
            useSnapshot = true;
            notes.Add(L10n.T("Şablon dosyası bulunamadı; baskıdaki tasarımın kopyası açıldı (kaydedilmemiş belge)."));
        }
        else if (templateChanged && snapshot is not null)
        {
            useSnapshot = Dialogs.Confirm(L10n.T("Şablon değişmiş"),
                L10n.T($"\"{Path.GetFileName(template)}\" bu baskıdan sonra değiştirilmiş.\n\n") +
                L10n.T("Baskıdaki hâli: o gün basılan tasarımın kopyası açılır (kaydedilmemiş belge olarak).\n") +
                L10n.T("Güncel şablon: dosyanın şimdiki hâli açılır."), L10n.T("Baskıdaki hâli"), L10n.T("Güncel şablon"));
            notes.Add(useSnapshot ? L10n.T("Baskıdaki tasarımın kopyası açıldı (kaydedilmemiş belge).") : L10n.T("Şablon baskıdan sonra değişmiş; güncel hâli kullanılıyor."));
        }
        else
        {
            useSnapshot = false;
            if (templateChanged) notes.Add(L10n.T("Şablon baskıdan sonra değişmiş; güncel hâli kullanılıyor (baskıdaki kopya yok)."));
        }

        bool alreadyOpen = !useSnapshot && !Session.IsDirty &&
                           string.Equals(Session.Project.FilePath, template, StringComparison.OrdinalIgnoreCase);
        if (!alreadyOpen)
        {
            if (!ConfirmCloseDocument()) return false;
            if (!OpenForReprint(useSnapshot ? snapshot! : template!, asCopy: useSnapshot)) return false;
        }

        // 2) Veri ve satır seçimi
        long startNumber = entry.StartNumber;
        int copies = Math.Max(1, entry.Copies);
        if (entry.UseData)
        {
            if (string.IsNullOrWhiteSpace(entry.DataFile) || !File.Exists(entry.DataFile))
            {
                Dialogs.ShowMessage(action, L10n.T("Bu baskının veri dosyası bulunamadı") + (entry.DataFile is { } file ? $":\n{file}" : ".") +
                                            L10n.T("\n\nŞablon açıldı; veri dosyasını seçip satırları elle seçebilirsiniz."), MessageKind.Warning);
                return false;
            }
            var info = new FileInfo(entry.DataFile);
            bool dataChanged = (entry.DataFileLength is { } length && info.Length != length) ||
                               (entry.DataFileWriteTimeUtc is { } written && info.LastWriteTimeUtc != written);
            if (dataChanged && !Dialogs.Confirm(L10n.T("Veri dosyası değişmiş"),
                    L10n.T($"\"{Path.GetFileName(entry.DataFile)}\" bu baskıdan sonra değiştirilmiş. Satırlar baskıdaki satır numaralarıyla seçilecek; ") +
                    L10n.T("satırlar eklendi ya da silindiyse farklı kayıtlar seçilebilir.\n\nBaskı penceresinden önce Veri panelinden seçimi kontrol edin. Devam edilsin mi?"),
                    L10n.T("Devam"), L10n.T("Vazgeç")))
                return false;
            if (dataChanged) notes.Add(L10n.T("Veri dosyası baskıdan sonra değişmiş: seçili satırları kontrol edin."));
            var options = new ImportOptions
            {
                SheetName = entry.DataSheet,
                Delimiter = entry.DataDelimiter is { Length: 1 } delimiter ? delimiter[0] : null,
                Encoding = ImportChoices.ResolveEncoding(entry.DataEncoding),
            };
            try
            {
                SetStatus(L10n.T($"Veri okunuyor: {Path.GetFileName(entry.DataFile)}…"));
                await Data.LoadAsync(entry.DataFile, options);
            }
            catch (DataImportException ex)
            {
                SetStatus(L10n.T("Veri okunamadı."));
                Dialogs.ShowMessage(L10n.T("Veri kaynağı okunamadı"), ex.Message, MessageKind.Error);
                return false;
            }
            Settings.LastDataFolder = Path.GetDirectoryName(entry.DataFile);
            ActivePanel = LeftPanel.Data;
            IsPanelOpen = true;

            var wanted = resumeOnly ? ResumePlanner.RemainingRows(entry) : ResumePlanner.AllRows(entry);
            int missing = ApplyRowSelection(wanted);
            if (missing > 0) notes.Add(L10n.T($"{missing} satır veri dosyasında bulunamadı (dosya kısalmış olabilir)."));
            if (resumeOnly && ResumePlanner.FirstRemainingSequence(entry) is { } first) startNumber = first;
            Data.ValidateAgainst(Session.Document);
        }
        else if (resumeOnly && ResumePlanner.RemainingSequence(entry) is { } remaining)
        {
            startNumber = remaining.StartNumber;
            copies = remaining.Copies;
        }

        // 3) Baskı penceresi (aynı seçim ve adetle; yazdırmak kullanıcının onayına bağlı)
        string what = resumeOnly
            ? L10n.T($"Kaldığı yerden devam: {entry.TimeText} tarihli işten gönderilmemiş {entry.LabelsRemaining} etiket.")
            : L10n.T($"Tekrar baskı: {entry.TimeText} tarihli iş ({entry.TotalLabels} etiket).");
        string banner = what + L10n.T($" O baskının yazıcısı: {entry.PrinterName}.") + (notes.Count > 0 ? "\n" + string.Join("\n", notes) : "") +
                        L10n.T("\nAyarları kontrol edip \"Yazdır\"a basın.");
        SetStatus(what);
        Dialogs.ShowDialog(new PrintViewModel(this, new PrintPreset(
            UseData: entry.UseData,
            Copies: copies,
            StartNumber: startNumber,
            Banner: banner,
            Title: resumeOnly ? L10n.T("Devam baskısı") : L10n.T("Tekrar baskı"),
            PrinterName: entry.PrinterName,
            ProfileName: entry.ProfileName)));
        return true;
    }

    /// <summary>Veri satırlarını seçer ve adetlerini ayarlar; bulunamayan satır sayısını döndürür.</summary>
    internal int ApplyRowSelection(IReadOnlyList<(int Row, int Copies)> wanted)
    {
        var map = new Dictionary<int, int>();
        foreach (var (row, copies) in wanted) map[row] = copies;
        int found = 0;
        foreach (var row in Data.Rows)
        {
            if (map.TryGetValue(row.RowNumber, out int copies))
            {
                row.IsSelected = true;
                row.Copies = copies;
                found++;
            }
            else
            {
                row.IsSelected = false;
            }
        }
        return map.Count - found;
    }

    /// <summary>Şablonu tekrar baskı için açar (bağlı veri kaynağı sorusu sorulmaz; veri ayrıca yüklenir).</summary>
    private bool OpenForReprint(string path, bool asCopy)
    {
        try
        {
            var result = ProjectFile.Load(path);
            if (asCopy) result.Project.FilePath = null;
            Session = CreateSession(result.Project);
            if (asCopy)
            {
                _recoveryDirty = true;
            }
            else
            {
                AddRecent(path);
                Settings.LastProjectFolder = Path.GetDirectoryName(path);
            }
            RequestFit();
            if (result.Warnings.Count > 0)
                Dialogs.ShowMessage(L10n.T("Şablon açıldı (uyarılar)"), string.Join("\n", result.Warnings), MessageKind.Warning);
            SetStatus(L10n.T($"Açıldı: {(asCopy ? Session.Document.Name + L10n.T(" (baskıdaki kopya)") : Path.GetFileName(path))}"));
            return true;
        }
        catch (ProjectFileException ex)
        {
            Dialogs.ShowMessage(L10n.T("Şablon açılamadı"), ex.Message, MessageKind.Error);
            return false;
        }
    }

    // ───────────────────────── PDF ─────────────────────────

    /// <summary>
    /// PDF olarak kaydet: ekrandaki etiket ya da veri kaynağındaki seçili satırlar (her etiket bir sayfa, sayfa ölçüsü =
    /// etiket ölçüsü). Sayfalar yazıcıya giden aynı 1-bit görüntüyle, yazıcı çözünürlüğünde çizilir. PDF kaydı SSCC
    /// ayırmaz, sayaç ilerletmez.
    /// </summary>
    [RelayCommand]
    private async Task ExportPdf()
    {
        var document = ModelJson.Clone(Session.Document);
        bool batch = false;
        if (Data.HasData && document.HasFieldPlaceholders && Data.SelectedLabelCount > 0)
        {
            int labels = Data.SelectedLabelCount;
            batch = Dialogs.Confirm(L10n.T("PDF olarak kaydet"),
                L10n.T($"Veri kaynağındaki seçili satırlar mı ({labels} etiket, her etiket ayrı sayfa) yoksa yalnızca ekrandaki etiket mi kaydedilsin?"),
                L10n.T($"Seçili satırlar ({labels})"), L10n.T("Yalnızca bu etiket"));
        }
        if ((Gs1FieldSource.DocumentUsesSscc(document) || CounterFields.DocumentUsesCounters(document)) &&
            !Dialogs.Confirm(L10n.T("PDF olarak kaydet"),
                L10n.T("Bu etikette {SSCC} ya da sayaç alanı var. PDF'te bu alanlar önizleme değeriyle yer alır: SSCC numarası ayrılmaz, ") +
                L10n.T("sayaç ilerlemez. Numaralı etiketleri basmak için Yazdır'ı kullanın.\n\nDevam edilsin mi?"), L10n.T("Devam"), L10n.T("Vazgeç")))
            return;
        string? path = Dialogs.ShowSaveFileDialog(L10n.T("PDF olarak kaydet"), "PDF belgesi (*.pdf)|*.pdf",
            TemplateLibrary.SanitizeName(document.Name) + ".pdf", Settings.LastProjectFolder);
        if (path is null) return;
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) path += ".pdf";

        double dpmm = Settings.Printer.DotsPerMm;
        double widthMm = document.Label.WidthMm, heightMm = document.Label.HeightMm;
        var now = DateTime.Now;
        var assets = Session.Assets;
        var images = Session.ImageResolver;
        var skipped = new List<string>();
        try
        {
            int pages;
            if (batch)
            {
                var plan = PrintPlanner.Plan(document, Data, useData: true, copies: 1, startNumber: 1, now);
                skipped.AddRange(plan.Skipped);
                SetStatus(L10n.T($"PDF hazırlanıyor ({plan.TotalLabels} etiket)…"));
                pages = await Task.Run(() =>
                {
                    using var producer = new LabelProducer(document, assets, verifyBarcodes: false, images);
                    IEnumerable<PdfLabelPage> Pages()
                    {
                        foreach (var item in plan.Items)
                        {
                            var (bitmap, problems, _) = producer.Produce(item, dpmm);
                            if (bitmap is null)
                            {
                                skipped.Add($"{item.Describe()}: {string.Join("; ", problems)}");
                                continue;
                            }
                            for (int copy = 0; copy < item.Copies; copy++) yield return new PdfLabelPage(bitmap, widthMm, heightMm);
                        }
                    }
                    return PdfExport.WriteFile(path, Pages(), document.Name);
                });
            }
            else
            {
                var result = Session.RenderLabel(RenderMode.Design, Data.GetPreviewFields(now), dpmm);
                pages = await Task.Run(() => PdfExport.WriteFile(path, [new PdfLabelPage(result.Bitmap, widthMm, heightMm)], document.Name));
            }
            Settings.LastProjectFolder = Path.GetDirectoryName(path);
            SetStatus(L10n.T($"PDF kaydedildi ({pages} sayfa, {widthMm:0.##} × {heightMm:0.##} mm): {path}"));
            if (skipped.Count > 0)
                Dialogs.ShowMessage(L10n.T("PDF kaydedildi (atlananlar var)"),
                    L10n.T($"{pages} sayfa kaydedildi. Şu etiketler üretilemediği için PDF'e eklenmedi:\n") + string.Join("\n", skipped.Take(15)) +
                    (skipped.Count > 15 ? $"\n… ve {skipped.Count - 15} tane daha" : ""), MessageKind.Warning);
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(L10n.T("PDF kaydedilemedi."));
            Dialogs.ShowMessage(L10n.T("PDF kaydedilemedi"), ex.Message + (skipped.Count > 0 ? "\n\n" + string.Join("\n", skipped.Take(15)) : ""), MessageKind.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(L10n.T("PDF kaydedilemedi."));
            Dialogs.ShowMessage(L10n.T("PDF kaydedilemedi"), ex.Message, MessageKind.Error);
        }
    }
}
