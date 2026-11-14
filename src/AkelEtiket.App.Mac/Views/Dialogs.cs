using AkelEtiket.App.Mac.Services;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.App.Mac.Views;

// İletişim pencerelerinin kod tarafı. Pencerelerin tamamı görünüm modeline bağlıdır; burada yalnızca onaylama,
// klasör seçme ve görüntü ölçekleme gibi görünüme özgü işler bulunur.

/// <summary>Etiket ölçüsü penceresi.</summary>
public partial class LabelSetupWindow : DialogWindow
{
    public LabelSetupWindow() => InitializeComponent();

    public LabelSetupWindow(LabelSetupViewModel vm) : this() => DataContext = vm;

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        CommitFocusedInput();
        if (DataContext is LabelSetupViewModel { IsValid: true }) Finish(true);
    }
}

/// <summary>İlk kurulum: firma, yazıcı ve etiket ölçüsü.</summary>
public partial class FirstSetupWindow : DialogWindow
{
    private readonly FirstSetupViewModel? _vm;

    public FirstSetupWindow() => InitializeComponent();

    public FirstSetupWindow(FirstSetupViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
        Opened += (_, _) => vm.RefreshPrintersCommand.Execute(null);
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        CommitFocusedInput();
        if (_vm?.ValidationProblem is { } problem)
        {
            MessageWindow.Show(this, L10n.T("Etiket ölçüsü"), problem, MessageKind.Warning);
            return;
        }
        Finish(true);
    }
}

/// <summary>Yardım penceresi.</summary>
public partial class HelpWindow : DialogWindow
{
    private readonly HelpViewModel? _vm;

    public HelpWindow() => InitializeComponent();

    public HelpWindow(HelpViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
    }

    private void OnOpenDocs(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null && Directory.Exists(_vm.DocumentsFolder)) ShellHelper.OpenFolder(_vm.DocumentsFolder);
        else MessageWindow.Show(this, L10n.T("Yardım"),
            L10n.T("Belgeler klasörü bulunamadı (geliştirme sürümünde belgeler depodaki docs klasöründedir)."), MessageKind.Info);
    }
}

/// <summary>Hazır kullanım paketleri.</summary>
public partial class StarterPacksWindow : DialogWindow
{
    public StarterPacksWindow() => InitializeComponent();

    public StarterPacksWindow(StarterPacksViewModel vm) : this() => DataContext = vm;
}

/// <summary>Tasarım yedekleri penceresi.</summary>
public partial class BackupsWindow : DialogWindow
{
    public BackupsWindow() => InitializeComponent();

    public BackupsWindow(BackupsViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += (_, _) => Finish(true);
    }
}

/// <summary>Veri sütunlarını tasarım alanlarına eşleştirme.</summary>
public partial class ColumnMappingWindow : DialogWindow
{
    public ColumnMappingWindow() => InitializeComponent();

    public ColumnMappingWindow(ColumnMappingViewModel vm) : this() => DataContext = vm;

    private void OnApply(object? sender, RoutedEventArgs e) => Finish(true);
}

/// <summary>Ürün listesi penceresi.</summary>
public partial class ProductsWindow : DialogWindow
{
    public ProductsWindow() => InitializeComponent();

    public ProductsWindow(ProductsViewModel vm) : this() => DataContext = vm;
}

/// <summary>Ayarlar penceresi.</summary>
public partial class SettingsWindow : DialogWindow
{
    private readonly SettingsViewModel? _vm;

    public SettingsWindow() => InitializeComponent();

    public SettingsWindow(SettingsViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
    }

    private void OnBrowseTemplates(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        string? folder = UiWait.Run(async () =>
        {
            var start = Directory.Exists(_vm.TemplatesFolder) ? await StorageProvider.TryGetFolderFromPathAsync(_vm.TemplatesFolder).ConfigureAwait(true) : null;
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = L10n.T("Şablon klasörünü seçin"),
                SuggestedStartLocation = start,
            }).ConfigureAwait(true);
            return picked.FirstOrDefault()?.TryGetLocalPath();
        });
        if (folder is not null) _vm.TemplatesFolder = folder;
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Accept();
}

/// <summary>Baskı önizlemesi: gerçek baskı çözünürlüğündeki 1-bit görüntü.</summary>
public partial class PreviewWindow : DialogWindow
{
    private const double PerMm = 96 / 25.4;
    private readonly PreviewViewModel? _vm;

    public PreviewWindow() => InitializeComponent();

    public PreviewWindow(PreviewViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
        ZoomSlider.ValueChanged += (_, _) => UpdateImageSize();
        vm.PropertyChanged += OnVmPropertyChanged;
        // Pencereye sığacak başlangıç yakınlaştırması, ilk yerleşimden sonra hesaplanır.
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            double fit = Math.Min((ImageScroll.Bounds.Width - 100) / (vm.WidthMm * PerMm), (ImageScroll.Bounds.Height - 100) / (vm.HeightMm * PerMm));
            if (double.IsFinite(fit)) ZoomSlider.Value = Math.Clamp(fit, ZoomSlider.Minimum, ZoomSlider.Maximum);
            UpdateImageSize();
        }, DispatcherPriority.Loaded);
        Closed += (_, _) =>
        {
            vm.PropertyChanged -= OnVmPropertyChanged;
            vm.Close();
        };
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PreviewViewModel.Image) or nameof(PreviewViewModel.PixelWidth)) UpdateImageSize();
    }

    /// <summary>Görüntü fiziksel ölçüye göre (mm) boyutlanır; yakınlaştırılınca noktalar keskin görünür.</summary>
    private void UpdateImageSize()
    {
        if (_vm is null) return;
        double zoom = ActualSize.IsChecked == true ? 1 : ZoomSlider.Value;
        LabelImage.Width = _vm.WidthMm * PerMm * zoom;
        LabelImage.Height = _vm.HeightMm * PerMm * zoom;
        double pixelsPerDot = LabelImage.Width * RenderScaling / Math.Max(1, _vm.PixelWidth);
        RenderOptions.SetBitmapInterpolationMode(LabelImage, pixelsPerDot >= 0.99 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
    }

    private void OnActualSize(object? sender, RoutedEventArgs e)
    {
        ZoomSlider.IsEnabled = ActualSize.IsChecked != true;
        UpdateImageSize();
    }

    private void OnThumbnailSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: PreviewThumbnail thumbnail }) _vm?.ShowCommand.Execute(thumbnail);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Finish(null);

    private void OnPrint(object? sender, RoutedEventArgs e) => Finish(true);
}
