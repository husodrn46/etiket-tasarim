using System.Collections.ObjectModel;
using AkelEtiket.Core.Data;
using AkelEtiket.Design.Data;
using AkelEtiket.Design.Preview;
using AkelEtiket.ProjectManager;
using AkelEtiket.Editor.Printing;
using AkelEtiket.Rendering;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>Hızlı Baskı'daki şablon seçeneği.</summary>
public sealed class QuickTemplateChoice(TemplateInfo info)
{
    public TemplateInfo Info { get; } = info;
    public string Name => Info.Name;
    public string SizeText => Info.SizeText;
    public byte[]? PreviewPng => Info.PreviewPng;
    public bool UsesData => Info.HasFieldPlaceholders;
}

/// <summary>
/// Hızlı Baskı: şablonu seç → ürünü ara ya da barkod okuyucuyla okut → adet → Yazdır. Tasarım ekranına girmeden
/// günlük etiket basmak içindir. Yazdır, mevcut baskı penceresini (yazıcı ayarları, onay, rapor) açar; doğrudan
/// yazıcıya iş göndermez.
/// </summary>
public sealed partial class QuickPrintViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly Dictionary<string, LabelProject> _projects = new(StringComparer.OrdinalIgnoreCase);

    public QuickPrintViewModel(MainViewModel main, ProductsViewModel products)
    {
        _main = main;
        Products = products;
        foreach (var template in main.Templates.Library.List().Where(t => t.Problem is null))
            Templates.Add(new QuickTemplateChoice(template));
        SelectedTemplate = Templates.FirstOrDefault(t => t.Info.FilePath == main.Settings.LastQuickTemplatePath)
            ?? Templates.FirstOrDefault(t => t.UsesData) ?? Templates.FirstOrDefault();
        Products.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ProductsViewModel.SelectedProduct)) return;
            if (Products.SelectedProduct is not null) { ScanInput = ""; ScanMessage = null; }
            UpdatePreview();
            PrintCommand.NotifyCanExecuteChanged();
        };
    }

    public ProductsViewModel Products { get; }

    public ObservableCollection<QuickTemplateChoice> Templates { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    public partial QuickTemplateChoice? SelectedTemplate { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    public partial int Copies { get; set; } = 1;

    [ObservableProperty]
    public partial byte[]? PreviewPng { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    public partial string? Problem { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    public partial bool RequiresProduct { get; private set; } = true;

    [ObservableProperty]
    public partial string ScanInput { get; set; } = "";

    [ObservableProperty]
    public partial string? ScanMessage { get; set; }

    public string EmptyHint => Templates.Count == 0 ? "Şablon bulunamadı. Şablonlar panelinden örnekleri ekleyin veya tasarımınızı şablon olarak kaydedin."
        : Products.Count == 0 ? "Ürün listesi boş. Ürün listesi düğmesinden Excel/CSV dosyanızı aktarabilirsiniz." : "";

    partial void OnScanInputChanged(string value) => PrintCommand.NotifyCanExecuteChanged();

    /// <summary>Pencere kapanmalı (baskı penceresine geçilirken).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Okutulan kod sonrası adet kutusuna odaklanılmalı.</summary>
    public event EventHandler? FocusCopiesRequested;

    partial void OnSelectedTemplateChanged(QuickTemplateChoice? value) => UpdatePreview();

    /// <summary>
    /// Barkod okuyucu (klavye gibi yazar + Enter) ya da elle yazılan kod: tek eşleşme varsa ürün seçilir ve adet
    /// kutusuna geçilir; birden fazla varsa liste süzülür.
    /// </summary>
    [RelayCommand]
    private void Scan()
    {
        string text = (ScanInput ?? "").Trim();
        if (text.Length == 0) return;
        // Başarısız veya belirsiz bir okutma önceki ürünü baskıya hazır bırakmamalı.
        Products.SelectedProduct = null;
        ScanMessage = null;
        Products.Query = text;
        var exact = Products.FindScanMatches(text);
        var pick = exact.Count == 1 ? exact[0] : null;
        if (pick is not null)
        {
            // WPF seçimi yalnız listede bulunan öğelerde tutar. Tam eşleşme 300'lük görünümün dışında kalabilir.
            if (!Products.Results.Contains(pick))
            {
                if (Products.Results.Count >= ProductsViewModel.MaxShown) Products.Results.RemoveAt(Products.Results.Count - 1);
                Products.Results.Insert(0, pick);
            }
            Products.SelectedProduct = pick;
            ScanInput = "";
            FocusCopiesRequested?.Invoke(this, EventArgs.Empty);
        }
        else ScanMessage = Products.Results.Count == 0
            ? "Bu kodla ürün bulunamadı. Kodu kontrol edin veya ürün listesini güncelleyin."
            : exact.Count > 1 ? "Birden fazla ürün aynı koda sahip. Listeden doğru ürünü seçin."
            : "Tam kod eşleşmesi yok. Listeden doğru ürünü seçin.";
    }

    private void UpdatePreview()
    {
        Problem = null;
        PreviewPng = null;
        if (SelectedTemplate is null) return;
        try
        {
            var project = Load(SelectedTemplate.Info.FilePath);
            var builtIns = EditorFields.BuiltIns(1, DateTime.Now);
            RequiresProduct = project.Document.Elements.SelectMany(DataSourceViewModel.FieldReferencesOf)
                .Any(f => !builtIns.TryGetValue(f.Raw, null, out _) && !builtIns.TryGetValue(f.Base, null, out _));
            IFieldValueSource fields = Products.SelectedProduct is { } product
                ? new CompositeFieldSource(Products.FieldsFor(product.Record), builtIns)
                : builtIns;
            var render = PreviewRenderer.Render(project.Document, project.Assets, fields, _main.Session.ImageResolver,
                Products.SelectedProduct is null ? RenderMode.Design : RenderMode.Print, _main.DotsPerMm);
            PreviewPng = PreviewRenderer.EncodeMono(render.Bitmap, render.DotsPerMm);
            var errors = render.Issues.Where(i => i.Severity == RenderIssueSeverity.Error).Select(i => i.ToString()).Distinct().ToList();
            if ((!RequiresProduct || Products.SelectedProduct is not null) && errors.Count > 0) Problem = string.Join("\n", errors.Take(4));
            else if (Products.SelectedProduct is null && RequiresProduct) Problem = "Ürün seçin (arayın ya da barkod okutun).";
        }
        catch (ProjectFileException ex)
        {
            Problem = "Şablon açılamadı: " + ex.Message;
        }
    }

    private LabelProject Load(string path)
    {
        if (!_projects.TryGetValue(path, out var project))
        {
            project = ProjectFile.Load(path).Project;
            _projects[path] = project;
        }
        return project;
    }

    private bool CanPrint() =>
        SelectedTemplate is not null && Copies is > 0 and <= 9999 && Problem is null && string.IsNullOrWhiteSpace(ScanInput) &&
        (Products.SelectedProduct is not null || !RequiresProduct);

    /// <summary>Şablonu ve ürünü baskı penceresine aktarır (yazıcı seçimi, onay ve rapor orada).</summary>
    [RelayCommand(CanExecute = nameof(CanPrint))]
    private void Print()
    {
        if (SelectedTemplate is null) return;
        var product = Products.SelectedProduct?.Record;
        if (!_main.StartQuickPrint(SelectedTemplate.Info.FilePath, product, Products.Columns, Copies)) return;
        _main.Settings.LastQuickTemplatePath = SelectedTemplate.Info.FilePath;
        _main.SaveSettings();
        CloseRequested?.Invoke(this, EventArgs.Empty);
        _main.OpenPrintDialogAfterQuickPrint();
    }
}
