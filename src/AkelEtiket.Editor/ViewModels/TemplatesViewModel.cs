using System.Collections.ObjectModel;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.Templates;
using AkelEtiket.ProjectManager;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>Şablon galerisi öğesi.</summary>
public sealed class TemplateItemViewModel
{
    public TemplateItemViewModel(TemplateInfo info) => Info = info;

    public TemplateInfo Info { get; }
    public string Name => Info.Name;
    public string Details => Info.Problem is null
        ? $"{Info.SizeText} • {Info.ElementCount} öğe{(Info.HasFieldPlaceholders ? " • değişkenli" : "")}"
        : "Okunamadı: " + Info.Problem;
    public byte[]? PreviewPng => Info.PreviewPng;
    public bool HasProblem => Info.Problem is not null;
    public string Category
    {
        get
        {
            string name = Design.Data.ProductCatalog.Fold(Name);
            if (name.Contains("demirbas")) return "Demirbaş";
            if (name.Contains("kargo")) return "Kargo";
            if (name.Contains("koli")) return "Koli";
            if (name.Contains("raf")) return "Raf";
            return name.Contains("urun") ? "Ürün" : "Diğer";
        }
    }
}

/// <summary>Şablonlar paneli: aç, kaydet, çoğalt, yeniden adlandır, sil.</summary>
public sealed partial class TemplatesViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public TemplatesViewModel(MainViewModel main)
    {
        _main = main;
        Library = new TemplateLibrary(main.Settings.EffectiveTemplatesFolder);
    }

    public TemplateLibrary Library { get; private set; }

    private readonly List<TemplateItemViewModel> _allItems = [];
    public ObservableCollection<TemplateItemViewModel> Items { get; } = [];
    public IReadOnlyList<string> Categories { get; } = ["Tümü", "Ürün", "Raf", "Koli", "Kargo", "Demirbaş", "Diğer"];
    [ObservableProperty] public partial string Query { get; set; } = "";
    [ObservableProperty] public partial string Category { get; set; } = "Tümü";
    public string ResultSummary => $"{Items.Count} / {_allItems.Count} şablon";
    partial void OnQueryChanged(string value) => FilterItems();
    partial void OnCategoryChanged(string value) => FilterItems();
    private void FilterItems()
    {
        string? selectedPath = SelectedItem?.Info.FilePath;
        string query = Design.Data.ProductCatalog.Fold(Query ?? "");
        Items.Clear();
        foreach (var item in _allItems.Where(i => (Category == "Tümü" || i.Category == Category) &&
            Design.Data.ProductCatalog.Fold(i.Name + " " + i.Info.SizeText).Contains(query))) Items.Add(item);
        SelectedItem = Items.FirstOrDefault(i => i.Info.FilePath == selectedPath);
        OnPropertyChanged(nameof(ResultSummary));
    }

    [ObservableProperty]
    public partial TemplateItemViewModel? SelectedItem { get; set; }

    public string FolderText => Library.Folder;

    public void ChangeFolder(string folder)
    {
        Library = new TemplateLibrary(folder);
        OnPropertyChanged(nameof(FolderText));
        Refresh();
    }

    [RelayCommand]
    public void Refresh()
    {
        _allItems.Clear();
        try
        {
            foreach (var info in Library.List()) _allItems.Add(new TemplateItemViewModel(info));
            FilterItems();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Items.Clear();
            SelectedItem = null;
            OnPropertyChanged(nameof(ResultSummary));
            _main.Dialogs.ShowMessage("Şablonlar", "Şablon klasörü okunamadı: " + ex.Message, MessageKind.Warning);
        }
    }

    /// <summary>v1'den kopyalanan şablon sayısı (ilk çalıştırmada).</summary>
    public int ImportedLegacyCount { get; private set; }

    /// <summary>
    /// İlk çalıştırma: v1'in şablonlarını (varsa) kopyalar.
    /// </summary>
    public void EnsureSamples(byte[]? logoPng) => EnsureSamples(logoPng, Core.AppIdentity.LegacyTemplatesFolder);

    /// <summary>
    /// İlk çalıştırma (şablon klasörü boşken): <paramref name="legacyFolder"/>'daki v1 şablonlarını kopyalar, sonra
    /// eksik örnek şablonları ekler (SSCC koli, GS1 DataMatrix gibi v1'de olmayanlar). v1'den gelen aynı adlı şablon
    /// korunur. Klasörde zaten şablon varsa hiçbir şey yapılmaz; kullanıcının sildiği örnekler geri gelmez.
    /// v1'in dosyalarına yazılmaz.
    /// </summary>
    public void EnsureSamples(byte[]? logoPng, string? legacyFolder)
    {
        try
        {
            Library.EnsureFolder();
            if (Library.List().Count > 0) return;
            if (legacyFolder is not null) ImportedLegacyCount = Library.ImportMissingFrom(legacyFolder);
            foreach (var (name, project) in SampleTemplates.CreateAll(logoPng, _main.Settings.Company))
            {
                if (File.Exists(Library.GetPathForName(name))) continue;
                byte[]? preview = null;
                try
                {
                    using var renderer = new Rendering.LabelRenderer(project.Assets);
                    var result = renderer.RenderLabel(project.Document, new Rendering.RenderOptions { DotsPerMm = 4 });
                    preview = Rendering.MonoImageExport.EncodePng(result.Bitmap, 4 * 25.4);
                }
                catch (Exception)
                {
                    // Önizleme oluşturulamazsa şablon yine kaydedilir.
                }
                Library.SaveAsTemplate(project, name, preview, overwrite: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectFileException)
        {
            _main.Dialogs.ShowMessage("Örnek şablonlar", "Örnek şablonlar oluşturulamadı: " + ex.Message, MessageKind.Warning);
        }
    }

    /// <summary>Şablondan yeni belge açar (şablon dosyası değişmez).</summary>
    [RelayCommand]
    private void Use(TemplateItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item is null || item.HasProblem) return;
        if (!_main.ConfirmCloseDocument()) return;
        try
        {
            var result = ProjectFile.Load(item.Info.FilePath);
            _main.OpenProjectAsNew(result.Project, $"\"{item.Name}\" şablonundan yeni etiket oluşturuldu.");
            if (result.Warnings.Count > 0)
                _main.Dialogs.ShowMessage("Şablon (uyarılar)", string.Join("\n", result.Warnings), MessageKind.Warning);
        }
        catch (ProjectFileException ex)
        {
            _main.Dialogs.ShowMessage("Şablon açılamadı", ex.Message, MessageKind.Error);
        }
    }

    /// <summary>Şablonun kendisini düzenlemek için açar (kaydet şablonun üzerine yazar).</summary>
    [RelayCommand]
    private void Edit(TemplateItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item is null || item.HasProblem) return;
        _main.OpenProject(item.Info.FilePath, askToSave: true);
    }

    [RelayCommand]
    private void Duplicate(TemplateItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item is null || item.HasProblem) return;
        try
        {
            Library.Duplicate(item.Info);
            Refresh();
        }
        catch (Exception ex) when (ex is ProjectFileException or IOException or UnauthorizedAccessException)
        {
            _main.Dialogs.ShowMessage("Şablon çoğaltılamadı", ex.Message, MessageKind.Error);
        }
    }

    [RelayCommand]
    private void Rename(TemplateItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item is null) return;
        string? name = _main.Dialogs.Prompt("Şablonu yeniden adlandır", "Yeni ad:", item.Name);
        if (string.IsNullOrWhiteSpace(name) || name == item.Name) return;
        try
        {
            Library.Rename(item.Info, name);
            Refresh();
        }
        catch (Exception ex) when (ex is ProjectFileException or IOException or UnauthorizedAccessException)
        {
            _main.Dialogs.ShowMessage("Yeniden adlandırılamadı", ex.Message, MessageKind.Error);
        }
    }

    [RelayCommand]
    private void Delete(TemplateItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item is null) return;
        if (!_main.Dialogs.Confirm("Şablonu sil", $"\"{item.Name}\" şablonu silinsin mi?\n(Geri Dönüşüm Kutusu'na gönderilir.)", "Sil", "Vazgeç")) return;
        try
        {
            Library.Delete(item.Info, _main.Services.RecycleFile);
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _main.Dialogs.ShowMessage("Silinemedi", ex.Message, MessageKind.Error);
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        Library.EnsureFolder();
        _main.Services.OpenFolder?.Invoke(Library.Folder);
    }

    [RelayCommand]
    private void RestoreSamples()
    {
        var samples = SampleTemplates.CreateAll(_main.Settings.Company.LogoPng ?? _main.Services.LogoProvider(), _main.Settings.Company);
        int created = 0;
        foreach (var (name, project) in samples)
        {
            if (File.Exists(Library.GetPathForName(name))) continue;
            try
            {
                using var renderer = new Rendering.LabelRenderer(project.Assets);
                var result = renderer.RenderLabel(project.Document, new Rendering.RenderOptions { DotsPerMm = 4 });
                Library.SaveAsTemplate(project, name, Rendering.MonoImageExport.EncodePng(result.Bitmap, 4 * 25.4), overwrite: false);
                created++;
            }
            catch (Exception ex) when (ex is ProjectFileException or IOException or UnauthorizedAccessException)
            {
                _main.Dialogs.ShowMessage("Örnek şablonlar", ex.Message, MessageKind.Warning);
                break;
            }
        }
        Refresh();
        _main.StatusMessage = created > 0 ? $"{created} örnek şablon geri yüklendi." : "Örnek şablonlar zaten mevcut.";
    }

    /// <summary>Firma bilgileriyle yeni kopyalar ekler; mevcut şablonları asla değiştirmez.</summary>
    public int AddCompanyTemplates()
    {
        var company = _main.Settings.Company;
        int created = 0;
        foreach (var (name, project) in SampleTemplates.CreateAll(company.LogoPng, company))
        {
            string prefix = TemplateLibrary.SanitizeName(company.DisplayName);
            if (prefix.Length > 30) prefix = prefix[..30];
            string templateName = $"{prefix} - {name}";
            string candidate = templateName;
            for (int n = 2; File.Exists(Library.GetPathForName(candidate)); n++) candidate = $"{templateName} ({n})";
            using var renderer = new Rendering.LabelRenderer(project.Assets);
            var result = renderer.RenderLabel(project.Document, new Rendering.RenderOptions { DotsPerMm = 4 });
            Library.SaveAsTemplate(project, candidate, Rendering.MonoImageExport.EncodePng(result.Bitmap, 4 * 25.4), overwrite: false);
            created++;
        }
        Refresh();
        return created;
    }
}
