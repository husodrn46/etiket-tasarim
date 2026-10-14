using AkelEtiket.Core.Model;
using AkelEtiket.DataImport;
using AkelEtiket.Editor.Services;
using AkelEtiket.ImageProcessor;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;
using CommunityToolkit.Mvvm.Input;

namespace AkelEtiket.Editor.ViewModels;

public sealed partial class MainViewModel
{
    // ───────────────────────── Yeni / Aç / Kaydet ─────────────────────────

    [RelayCommand]
    private void New()
    {
        if (!ConfirmCloseDocument()) return;
        var setup = new LabelSetupViewModel("Yeni etiket", new LabelSetup
        {
            WidthMm = Settings.DefaultLabelWidthMm,
            HeightMm = Settings.DefaultLabelHeightMm,
        }, Settings.Printer.MaxPrintWidthMm);
        if (!Dialogs.ShowDialog(setup)) return;
        var project = LabelProject.CreateBlank(setup.WidthMm, setup.HeightMm);
        setup.ApplyTo(project.Document.Label);
        Session = CreateSession(project);
        _recoveryDirty = true;
        RequestFit();
        SetStatus($"Yeni {setup.WidthMm:0.#} × {setup.HeightMm:0.#} mm etiket oluşturuldu.");
    }

    /// <summary>Etiket ölçüsünü/medya ayarını değiştirir (geri alınabilir).</summary>
    [RelayCommand]
    private void LabelSetup()
    {
        var setup = new LabelSetupViewModel("Etiket ölçüsü", Session.Document.Label, Settings.Printer.MaxPrintWidthMm);
        if (!Dialogs.ShowDialog(setup)) return;
        using (Session.BeginTransaction()) setup.ApplyTo(Session.Document.Label);
        RequestFit();
    }

    [RelayCommand]
    private void Open()
    {
        if (!ConfirmCloseDocument()) return;
        string? path = Dialogs.ShowOpenFileDialog("Proje aç", ProjectFile.FileFilter, Settings.LastProjectFolder);
        if (path is not null) OpenProject(path, askToSave: false);
    }

    [RelayCommand]
    private void OpenRecent(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!File.Exists(path))
        {
            Dialogs.ShowMessage("Son kullanılanlar", $"Dosya bulunamadı:\n{path}", MessageKind.Warning);
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
                Dialogs.ShowMessage("Proje açıldı (uyarılar)", string.Join("\n", result.Warnings), MessageKind.Warning);
            SetStatus($"Açıldı: {Path.GetFileName(path)}");
            OfferLinkedDataSource(result.Project.Document);
            return true;
        }
        catch (ProjectFileException ex)
        {
            Dialogs.ShowMessage("Proje açılamadı", ex.Message, MessageKind.Error);
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
        if (Dialogs.Confirm("Veri kaynağı", $"Bu şablon en son şu veri dosyasıyla kullanıldı:\n{link.FilePath}\n\nYüklensin mi?", "Yükle", "Hayır"))
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
        string? path = Dialogs.ShowSaveFileDialog("Farklı kaydet", ProjectFile.FileFilter, defaultName, Settings.LastProjectFolder);
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
            SetStatus("Uyarı: önceki sürümün yedeği alınamadı (" + ex.Message + ").");
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
            SetStatus($"Kaydedildi: {path}");
            return true;
        }
        catch (ProjectFileException ex)
        {
            Dialogs.ShowMessage("Kaydedilemedi", ex.Message, MessageKind.Error);
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
        string? name = Dialogs.Prompt("Şablon olarak kaydet", "Şablon adı:", Session.Document.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        var library = Templates.Library;
        bool exists = File.Exists(library.GetPathForName(name));
        if (exists && !Dialogs.Confirm("Şablon", $"\"{name}\" adında bir şablon var. Üzerine yazılsın mı?", "Üzerine yaz", "Vazgeç")) return;
        try
        {
            UpdateDataSourceLink();
            library.SaveAsTemplate(Session.Project, name, CreateThumbnailPng(), overwrite: true);
            Templates.Refresh();
            SetStatus($"Şablon kaydedildi: {name}");
        }
        catch (Exception ex) when (ex is ProjectFileException or IOException or UnauthorizedAccessException)
        {
            Dialogs.ShowMessage("Şablon kaydedilemedi", ex.Message, MessageKind.Error);
        }
    }

    /// <summary>Etiketi yazıcı çözünürlüğünde, gerçek ölçü bilgisiyle PNG olarak dışa aktarır.</summary>
    [RelayCommand]
    private void ExportPng()
    {
        string? path = Dialogs.ShowSaveFileDialog("PNG olarak dışa aktar", "PNG resmi (*.png)|*.png",
            TemplateLibrary.SanitizeName(Session.Document.Name) + ".png", Settings.LastProjectFolder);
        if (path is null) return;
        try
        {
            var result = Session.RenderLabel(RenderMode.Design, Data.GetPreviewFields(DateTime.Now));
            File.WriteAllBytes(path, MonoImageExport.EncodePng(result.Bitmap, Settings.Printer.DotsPerMm * 25.4));
            SetStatus($"PNG kaydedildi ({result.Bitmap.Width} × {result.Bitmap.Height} nokta): {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.ShowMessage("Dışa aktarılamadı", ex.Message, MessageKind.Error);
        }
    }

    // ───────────────────────── Veri kaynağı ─────────────────────────

    [RelayCommand]
    private async Task LoadData()
    {
        string? path = Dialogs.ShowOpenFileDialog("Veri kaynağı seç (Excel/CSV)", DataImporter.OpenFileFilter, Settings.LastDataFolder);
        if (path is not null) await LoadDataFileAsync(path, null);
    }

    public async Task LoadDataFileAsync(string path, ImportOptions? options)
    {
        try
        {
            SetStatus($"Veri okunuyor: {Path.GetFileName(path)}…");
            await Data.LoadAsync(path, options);
            Settings.LastDataFolder = Path.GetDirectoryName(path);
            ActivePanel = LeftPanel.Data;
            IsPanelOpen = true;
            SetStatus(Data.Summary);
            if (Data.Table is { ErrorCount: > 0 } table)
                Dialogs.ShowMessage("Veri kaynağında sorunlar var",
                    $"{table.ErrorCount} satırda hata bulundu. Hatalı satırlar yazdırılmaz; diğer satırlar etkilenmez.\n" +
                    "Ayrıntılar Veri panelindeki listede satır numarasıyla gösterilir.", MessageKind.Warning);
        }
        catch (DataImportException ex)
        {
            SetStatus("Veri okunamadı.");
            Dialogs.ShowMessage("Veri kaynağı okunamadı", ex.Message, MessageKind.Error);
        }
    }

    [RelayCommand]
    private void ClearData()
    {
        Data.Clear();
        SetStatus("Veri kaynağı kaldırıldı.");
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
            Dialogs.ShowMessage("Desteklenmeyen dosya", $"Bu dosya türü eklenemez: {Path.GetFileName(path)}", MessageKind.Warning);
        }
    }
}
