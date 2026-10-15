using System.Collections.ObjectModel;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.Templates;
using AkelEtiket.ProjectManager;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>Şablon galerisi öğesi.</summary>
public sealed class TemplateItemViewModel
{
    public TemplateItemViewModel(TemplateInfo info, bool favorite = false) { Info = info; IsFavorite = favorite; }
    public bool IsFavorite { get; }
    public string DisplayName => (IsFavorite ? "★ " : "") + Name;

    public TemplateInfo Info { get; }
    public string Name => Info.Name;
    public string Details => Info.Problem is null
        ? L10n.T($"{Info.SizeText} • {Info.ElementCount} öğe{(Info.HasFieldPlaceholders ? L10n.T(" • değişkenli") : "")}")
        : L10n.T("Okunamadı: ") + Info.Problem;
    public byte[]? PreviewPng => Info.PreviewPng;
    public bool HasProblem => Info.Problem is not null;
    public string Category
    {
        get
        {
            string name = Design.Data.ProductCatalog.Fold(Name);
            if (name.Contains("demirbas") || name.Contains("asset")) return "Demirbaş";
            if (name.Contains("kargo") || name.Contains("shipping")) return "Kargo";
            if (name.Contains("koli") || name.Contains("carton")) return "Koli";
            if (name.Contains("raf") || name.Contains("shelf")) return "Raf";
            return name.Contains("urun") || name.Contains("product") ? "Ürün" : "Diğer";
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
    public IReadOnlyList<KeyValuePair<string,string>> LocalizedCategories => Categories.Select(c => new KeyValuePair<string,string>(c, L10n.T(c))).ToArray();
    [ObservableProperty] public partial bool OnlyFavorites { get; set; }
    partial void OnOnlyFavoritesChanged(bool value) => FilterItems();
    public string FavoriteAction => SelectedItem?.IsFavorite == true ? L10n.T("Favoriden çıkar") : L10n.T("Favoriye ekle");
    [RelayCommand] private void ToggleFavorite()
    {
        if (SelectedItem is not { } item) return;
        if (_main.UpdatePreferences(settings =>
        {
            if (item.IsFavorite) settings.FavoriteTemplatePaths.RemoveAll(p => string.Equals(p,item.Info.FilePath,StringComparison.OrdinalIgnoreCase));
            else settings.FavoriteTemplatePaths.Add(item.Info.FilePath);
        })) Refresh();
    }
    [ObservableProperty] public partial string Query { get; set; } = "";
    [ObservableProperty] public partial string Category { get; set; } = "Tümü";
    public string ResultSummary => L10n.T($"{Items.Count} / {_allItems.Count} şablon");
    partial void OnQueryChanged(string value) => FilterItems();
    partial void OnCategoryChanged(string value) => FilterItems();
    private void FilterItems()
    {
        string? selectedPath = SelectedItem?.Info.FilePath;
        string query = Design.Data.ProductCatalog.Fold(Query ?? "");
        Items.Clear();
        foreach (var item in _allItems.Where(i => (!OnlyFavorites || i.IsFavorite) && (Category == "Tümü" || i.Category == Category) &&
            Design.Data.ProductCatalog.Fold(i.Name + " " + i.Info.SizeText).Contains(query))) Items.Add(item);
        SelectedItem = Items.FirstOrDefault(i => i.Info.FilePath == selectedPath);
        OnPropertyChanged(nameof(ResultSummary));
    }

    [ObservableProperty]
    public partial TemplateItemViewModel? SelectedItem { get; set; }
    partial void OnSelectedItemChanged(TemplateItemViewModel? value) => OnPropertyChanged(nameof(FavoriteAction));

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
            foreach (var info in Library.List()) _allItems.Add(new TemplateItemViewModel(info, _main.Settings.FavoriteTemplatePaths.Contains(info.FilePath, StringComparer.OrdinalIgnoreCase)));
            FilterItems();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Items.Clear();
            SelectedItem = null;
            OnPropertyChanged(nameof(ResultSummary));
            _main.Dialogs.ShowMessage(L10n.T("Şablonlar"), L10n.T("Şablon klasörü okunamadı: ") + ex.Message, MessageKind.Warning);
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
            foreach (var (name, project) in InternationalSamples.CreateAll(logoPng, _main.Settings.Company, L10n.Language))
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
            _main.Dialogs.ShowMessage(L10n.T("Örnek şablonlar"), L10n.T("Örnek şablonlar oluşturulamadı: ") + ex.Message, MessageKind.Warning);
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
            _main.OpenProjectAsNew(result.Project, L10n.T($"\"{item.Name}\" şablonundan yeni etiket oluşturuldu."));
            if (result.Warnings.Count > 0)
                _main.Dialogs.ShowMessage(L10n.T("Şablon (uyarılar)"), string.Join("\n", result.Warnings), MessageKind.Warning);
        }
        catch (ProjectFileException ex)
        {
            _main.Dialogs.ShowMessage(L10n.T("Şablon açılamadı"), ex.Message, MessageKind.Error);
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
            _main.Dialogs.ShowMessage(L10n.T("Şablon çoğaltılamadı"), ex.Message, MessageKind.Error);
        }
    }

    [RelayCommand]
    private void Rename(TemplateItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item is null) return;
        string? name = _main.Dialogs.Prompt(L10n.T("Şablonu yeniden adlandır"), L10n.T("Yeni ad:"), item.Name);
        if (string.IsNullOrWhiteSpace(name) || name == item.Name) return;
        try
        {
            string oldPath = item.Info.FilePath;
            Library.Rename(item.Info, name);
            string newPath = Library.GetPathForName(name);
            _main.UpdatePreferences(settings =>
            {
                if (settings.FavoriteTemplatePaths.RemoveAll(p => string.Equals(p,oldPath,StringComparison.OrdinalIgnoreCase)) > 0) settings.FavoriteTemplatePaths.Add(newPath);
                if (string.Equals(settings.Personalization.StartupTemplatePath,oldPath,StringComparison.OrdinalIgnoreCase)) settings.Personalization.StartupTemplatePath = newPath;
            });
            Refresh();
        }
        catch (Exception ex) when (ex is ProjectFileException or IOException or UnauthorizedAccessException)
        {
            _main.Dialogs.ShowMessage(L10n.T("Yeniden adlandırılamadı"), ex.Message, MessageKind.Error);
        }
    }

    [RelayCommand]
    private void Delete(TemplateItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item is null) return;
        if (!_main.Dialogs.Confirm(L10n.T("Şablonu sil"), L10n.T($"\"{item.Name}\" şablonu silinsin mi?\n(Geri Dönüşüm Kutusu'na gönderilir.)"), L10n.T("Sil"), L10n.T("Vazgeç"))) return;
        try
        {
            Library.Delete(item.Info, _main.Services.RecycleFile);
            _main.UpdatePreferences(settings =>
            {
                settings.FavoriteTemplatePaths.RemoveAll(p => string.Equals(p,item.Info.FilePath,StringComparison.OrdinalIgnoreCase));
                if (string.Equals(settings.Personalization.StartupTemplatePath,item.Info.FilePath,StringComparison.OrdinalIgnoreCase)) settings.Personalization.StartupTemplatePath = null;
            });
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _main.Dialogs.ShowMessage(L10n.T("Silinemedi"), ex.Message, MessageKind.Error);
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
        var samples = InternationalSamples.CreateAll(_main.Settings.Company.LogoPng ?? _main.Services.LogoProvider(), _main.Settings.Company, L10n.Language);
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
                _main.Dialogs.ShowMessage(L10n.T("Örnek şablonlar"), ex.Message, MessageKind.Warning);
                break;
            }
        }
        Refresh();
        _main.StatusMessage = created > 0 ? L10n.T($"{created} örnek şablon geri yüklendi.") : L10n.T("Örnek şablonlar zaten mevcut.");
    }

    /// <summary>Firma bilgileriyle yeni kopyalar ekler; mevcut şablonları asla değiştirmez.</summary>
    public int AddCompanyTemplates()
    {
        var company = _main.Settings.Company;
        int created = 0;
        foreach (var (name, project) in InternationalSamples.CreateAll(company.LogoPng, company, L10n.Language))
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
