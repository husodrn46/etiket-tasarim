using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AkelEtiket.Editor.ViewModels;

namespace AkelEtiket.App.Views;

/// <summary>Baskı önizlemesi: gerçek baskı çözünürlüğündeki 1-bit görüntü.</summary>
public partial class PreviewWindow : ThemedWindow
{
    private readonly PreviewViewModel _vm;

    public PreviewWindow(PreviewViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        ZoomSlider.ValueChanged += (_, _) => UpdateImageSize();
        vm.PropertyChanged += OnVmPropertyChanged;
        Loaded += (_, _) =>
        {
            // Pencereye sığacak başlangıç yakınlaştırması
            double perMm = 96 / 25.4;
            double fit = Math.Min((ImageScroll.ActualWidth - 100) / (vm.WidthMm * perMm), (ImageScroll.ActualHeight - 100) / (vm.HeightMm * perMm));
            ZoomSlider.Value = Math.Clamp(fit, ZoomSlider.Minimum, ZoomSlider.Maximum);
            UpdateImageSize();
        };
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PreviewViewModel.Image) or nameof(PreviewViewModel.PixelWidth)) UpdateImageSize();
    }

    /// <summary>Görüntü fiziksel ölçüye göre (mm) boyutlanır; yakınlaştırılınca noktalar keskin görünür.</summary>
    private void UpdateImageSize()
    {
        double perMm = 96 / 25.4;
        double zoom = ActualSize.IsChecked == true ? 1 : ZoomSlider.Value;
        LabelImage.Width = _vm.WidthMm * perMm * zoom;
        LabelImage.Height = _vm.HeightMm * perMm * zoom;
        double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double pixelsPerDot = LabelImage.Width * dpiScale / Math.Max(1, _vm.PixelWidth);
        RenderOptions.SetBitmapScalingMode(LabelImage, pixelsPerDot >= 0.99 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
    }

    private void OnActualSize(object sender, RoutedEventArgs e)
    {
        ZoomSlider.IsEnabled = ActualSize.IsChecked != true;
        UpdateImageSize();
    }

    private void OnThumbnailSelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: PreviewThumbnail thumbnail }) _vm.ShowCommand.Execute(thumbnail);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.Close();
        base.OnClosed(e);
    }
}
