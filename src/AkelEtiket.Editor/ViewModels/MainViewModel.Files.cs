using AkelEtiket.Core.Model;
using AkelEtiket.DataImport;
using AkelEtiket.Editor.Services;
using AkelEtiket.ImageProcessor;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

public sealed partial class MainViewModel
{
    // ───────────────────────── Yeni / Aç / Kaydet ─────────────────────────

    [RelayCommand]
    private void New()
    {
        if (!ConfirmCloseDocument()) return;
        var setup = new LabelSetupViewModel(L10n.T("Yeni etiket"), new LabelSetup
        {
            WidthMm = Settings.DefaultLabelWidthMm,
            HeightMm = Settings.DefaultLabelHeightMm,
        }, Settings.Printer.MaxPrintWidthMm);
        if (!Dialogs.ShowDialog(setup)) return;
        var project = LabelProject.CreateBlank(setup.WidthMm, setup.HeightMm);
        setup.ApplyTo(project.Document.Label);
        project.Document.Formatting = setup.Formatting.ToModel();
        Session = CreateSession(project);
        _recoveryDirty = true;
        RequestFit();
        SetStatus(L10n.T($"Yeni {setup.WidthMm:0.#} × {setup.HeightMm:0.#} mm etiket oluşturuldu."));
    }

    /// <summary>Etiket ölçüsünü/medya ayarını değiştirir (geri alınabilir).</summary>
    [RelayCommand]
    private void LabelSetup()
    {
        var setup = new LabelSetupViewModel(L10n.T("Etiket ölçüsü"), Session.Document.Label, Settings.Printer.MaxPrintWidthMm, Session.Document.Formatting);
        if (!Dialogs.ShowDialog(setup)) return;
        using (Session.BeginTransaction()) { setup.ApplyTo(Session.Document.Label); Session.Document.Formatting = setup.Formatting.ToModel(); }
        RequestFit();
    }

    [RelayCommand]
    private void Open()
    {
        if (!ConfirmCloseDocument()) return;
        string? path = Dialogs.ShowOpenFileDialog(L10n.T("Proje aç"), ProjectFile.FileFilter, Settings.LastProjectFolder);
        if (path is not null) OpenProject(path, askToSave: false);
    }

    [RelayCommand]
    private void OpenRecent(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!File.Exists(path))
        {
            Dialogs.ShowMessage(L10n.T("Son kullanılanlar"), L10n.T($"Dosya bulunamadı:\n{path}"), MessageKind.Warning);
            RecentFiles.Remove(path);
            SaveSettings();
            return;
        }
        OpenProject(path, askToSave: true);
    }

    /// <summary>Projeyi açar; uyarıları gösterir, son kullanılanlara ekler.</summary>
    public bool OpenProject(string path, bool askToSave)
    {
        if (askToSave && !ConfirmCloseDocument()) return false;
        try
        {
            var result = ProjectFile.Load(path);
            Session = CreateSession(result.Project);
            AddRecent(path);
            Settings.LastProjectFolder = Path.GetDirectoryName(path);
            RequestFit();
            if (result.Warnings.Count > 0)
                Dialogs.ShowMessage(L10n.T("Proje açıldı (uyarılar)"), string.Join("\n", result.Warnings), MessageKind.Warning);
            SetStatus(L10n.T($"Açıldı: {Path.GetFileName(path)}"));
            OfferLinkedDataSource(result.Project.Document);
            return true;
        }
        catch (ProjectFileException ex)
        {
            Dialogs.ShowMessage(L10n.T("Proje açılamadı"), ex.Message, MessageKind.Error);
            return false;
        }
    }

    /// <summary>Açılan şablon/proje (dosya yolu olmadan) — ör. şablon galerisinden.</summary>
    public void OpenProjectAsNew(LabelProject project, string statusText)
    {
        project.FilePath = null;
        Session = CreateSession(project);
        _recoveryDirty = true;
        RequestFit();
        SetStatus(statusText);
        OfferLinkedDataSource(project.Document);
    }

    private void OfferLinkedDataSource(LabelDocument document)
    {
        var link = document.DataSource;
        if (link is null || string.IsNullOrWhiteSpace(link.FilePath) || !File.Exists(link.FilePath)) return;
        if (Data.Table is not null && string.Equals(Data.Table.SourcePath, link.FilePath, StringComparison.OrdinalIgnoreCase)) return;
        if (Dialogs.Confirm(L10n.T("Veri kaynağı"), L10n.T($"Bu şablon en son şu veri dosyasıyla kullanıldı:\n{link.FilePath}\n\nYüklensin mi?"), L10n.T("Yükle"), L10n.T("Hayır")))
            _ = LoadDataFileAsync(link.FilePath, new ImportOptions { SheetName = link.SheetName });
    }

    [RelayCommand]
    private void SaveDocument() => Save();

    /// <summary>Kaydeder; yolu yoksa "Farklı kaydet" açar. Başarılıysa true.</summary>
    public bool Save()
    {
        if (Session.Project.FilePath is null) return SaveAs();
        return SaveTo(Session.Project.FilePath);
    }

    [RelayCommand]
    private void SaveDocumentAs() => SaveAs();

    public bool SaveAs()
    {
        string defaultName = TemplateLibrary.SanitizeName(Session.Document.Name) + ProjectFile.Extension;
        string? path = Dialogs.ShowSaveFileDialog(L10n.T("Farklı kaydet"), ProjectFile.FileFilter, defaultName, Settings.LastProjectFolder);
        if (path is null) return false;
        if (!path.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase)) path += ProjectFile.Extension;
        return SaveTo(path);
    }

    /// <summary>Var olan dosyanın üzerine yazmadan önce önceki hâlini yedekler (son 20; ? → Bu tasarımın yedekleri).</summary>
    private void BackupBeforeOverwrite(string path)
    {
        try
        {
            if (File.Exists(path)) Backups.Backup(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Yedek alınamaması kaydı engellemez.
            SetStatus(L10n.T("Uyarı: önceki sürümün yedeği alınamadı (") + ex.Message + ").");
        }
    }

    private bool SaveTo(string path)
    {
        try
        {
            UpdateDataSourceLink();
            BackupBeforeOverwrite(path);
            ProjectFile.Save(Session.Project, path, CreateThumbnailPng());
            Session.MarkSaved();
            AddRecent(path);
            Settings.LastProjectFolder = Path.GetDirectoryName(path);
            _recovery.ClearSession();
            _recoveryDirty = false;
            OnPropertyChanged(nameof(WindowTitle));
            SetStatus(L10n.T($"Kaydedildi: {path}"));
            return true;
        }
        catch (ProjectFileException ex)
        {
            Dialogs.ShowMessage(L10n.T("Kaydedilemedi"), ex.Message, MessageKind.Error);
            return false;
        }
    }

    private void UpdateDataSourceLink()
    {
        Session.Document.DataSource = Data.Table is null ? Session.Document.DataSource : new DataSourceLink
        {
            FilePath = Data.Table.SourcePath,
            SheetName = Data.Table.SheetName,
            Delimiter = Data.Table.Delimiter?.ToString(),
            EncodingName = Data.Table.EncodingName,
        };
    }

    private void AddRecent(string path)
    {
        Settings.Recent.Add(path);
        RecentFiles.Clear();
        foreach (var item in Settings.Recent.Items) RecentFiles.Add(item);
        SaveSettings();
    }

    /// <summary>Şablon galerisi ve paket için küçük önizleme (4 nokta/mm).</summary>
    public byte[]? CreateThumbnailPng()
    {
        try
        {
            var result = Session.RenderLabel(RenderMode.Design, Data.GetPreviewFields(DateTime.Now), dotsPerMm: 4);
            return MonoImageExport.EncodePng(result.Bitmap, 4 * 25.4);
        }
        catch (Exception)
        {
            return null;
        }
    }

    [RelayCommand]
    private void SaveAsTemplate()
    {
        string? name = Dialogs.Prompt(L10n.T("Şablon olarak kaydet"), L10n.T("Şablon adı:"), Session.Document.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        var library = Templates.Library;
        bool exists = File.Exists(library.GetPathForName(name));
        if (exists && !Dialogs.Confirm(L10n.T("Şablon"), L10n.T($"\"{name}\" adında bir şablon var. Üzerine yazılsın mı?"), L10n.T("Üzerine yaz"), L10n.T("Vazgeç"))) return;
        try
        {
            UpdateDataSourceLink();
            library.SaveAsTemplate(Session.Project, name, CreateThumbnailPng(), overwrite: true);
            Templates.Refresh();
            SetStatus(L10n.T($"Şablon kaydedildi: {name}"));
        }
        catch (Exception ex) when (ex is ProjectFileException or IOException or UnauthorizedAccessException)
        {
            Dialogs.ShowMessage(L10n.T("Şablon kaydedilemedi"), ex.Message, MessageKind.Error);
        }
    }

    /// <summary>Etiketi yazıcı çözünürlüğünde, gerçek ölçü bilgisiyle PNG olarak dışa aktarır.</summary>
    [RelayCommand]
    private void ExportPng()
    {
        string? path = Dialogs.ShowSaveFileDialog(L10n.T("PNG olarak dışa aktar"), "PNG resmi (*.png)|*.png",
            TemplateLibrary.SanitizeName(Session.Document.Name) + ".png", Settings.LastProjectFolder);
        if (path is null) return;
        try
        {
            var result = Session.RenderLabel(RenderMode.Design, Data.GetPreviewFields(DateTime.Now));
            File.WriteAllBytes(path, MonoImageExport.EncodePng(result.Bitmap, Settings.Printer.DotsPerMm * 25.4));
            SetStatus(L10n.T($"PNG kaydedildi ({result.Bitmap.Width} × {result.Bitmap.Height} nokta): {path}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.ShowMessage(L10n.T("Dışa aktarılamadı"), ex.Message, MessageKind.Error);
        }
    }

    // ───────────────────────── Veri kaynağı ─────────────────────────

    [RelayCommand]
    private async Task LoadData()
    {
        string? path = Dialogs.ShowOpenFileDialog(L10n.T("Veri kaynağı seç (Excel/CSV)"), DataImporter.OpenFileFilter, Settings.LastDataFolder);
        if (path is not null)
        {
            await LoadDataFileAsync(path, SelectedImportProfile?.ToImportOptions());
            if (SelectedImportProfile is not null && Data.Table?.SourcePath == Path.GetFullPath(path)) OpenColumnMappingCommand.Execute(null);
        }
    }

    public async Task LoadDataFileAsync(string path, ImportOptions? options)
    {
        try
        {
            SetStatus(L10n.T($"Veri okunuyor: {Path.GetFileName(path)}…"));
            await Data.LoadAsync(path, options);
            Settings.LastDataFolder = Path.GetDirectoryName(path);
            ActivePanel = LeftPanel.Data;
            IsPanelOpen = true;
            SetStatus(Data.Summary);
            if (Data.Table is { ErrorCount: > 0 } table)
                Dialogs.ShowMessage(L10n.T("Veri kaynağında sorunlar var"),
                    L10n.T($"{table.ErrorCount} satırda hata bulundu. Hatalı satırlar yazdırılmaz; diğer satırlar etkilenmez.\n") +
                    L10n.T("Ayrıntılar Veri panelindeki listede satır numarasıyla gösterilir."), MessageKind.Warning);
        }
        catch (DataImportException ex)
        {
            SetStatus(L10n.T("Veri okunamadı."));
            Dialogs.ShowMessage(L10n.T("Veri kaynağı okunamadı"), ex.Message, MessageKind.Error);
        }
    }

    [RelayCommand]
    private void ClearData()
    {
        Data.Clear();
        SetStatus(L10n.T("Veri kaynağı kaldırıldı."));
    }

    // ───────────────────────── Sürükle-bırak ─────────────────────────

    /// <summary>Tuvale veya pencereye bırakılan dosyalar: görsel → öğe, .akelabel → aç, xlsx/csv → veri.</summary>
    public void HandleFileDrop(IReadOnlyList<string> paths, double? xMm, double? yMm)
    {
        // Önce proje, sonra veri, en son görseller: "şablonu aç + veriyi yükle" tek seferde yapılabilir.
        var ordered = paths
            .OrderBy(p => Path.GetExtension(p).Equals(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase) ? 0
                : DataImporter.IsSupportedFile(p) && !ImageLoader.IsSupportedFile(p) ? 1 : 2)
            .ToList();
        int imageIndex = 0;
        foreach (var path in ordered)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ProjectFile.Extension)
            {
                if (!OpenProject(path, askToSave: true)) return;
                continue;
            }
            if (DataImporter.IsSupportedFile(path) && !ImageLoader.IsSupportedFile(path))
            {
                _ = LoadDataFileAsync(path, null);
                continue;
            }
            if (ImageLoader.IsSupportedFile(path))
            {
                double offset = imageIndex++ * 4;
                AddImageFromFile(path, xMm + offset, yMm + offset);
                continue;
            }
            Dialogs.ShowMessage(L10n.T("Desteklenmeyen dosya"), L10n.T($"Bu dosya türü eklenemez: {Path.GetFileName(path)}"), MessageKind.Warning);
        }
    }
}
