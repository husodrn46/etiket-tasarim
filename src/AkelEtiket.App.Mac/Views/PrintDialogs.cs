using AkelEtiket.App.Mac.Services;
using AkelEtiket.Editor.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.App.Mac.Views;

// Baskı pencereleri: yazdır, hızlı baskı, kalibrasyon ve baskı geçmişi.

/// <summary>Baskı penceresi.</summary>
public partial class PrintWindow : DialogWindow
{
    private readonly PrintViewModel? _vm;
    private bool _closeConfirmed;

    public PrintWindow() => InitializeComponent();

    public PrintWindow(PrintViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
        Closing += OnWindowClosing;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Baskı sürerken pencere kapatılmaz; önce sorulur. Soru penceresi kapanış olayının dışında açılır
    /// (iç içe ileti döngüsü kapanış olayının içinde çalıştırılmaz).
    /// </summary>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_vm is not { IsBusy: true } || _closeConfirmed) return;
        e.Cancel = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_vm.IsBusy)
            {
                Close();
                return;
            }
            bool stop = MessageWindow.Confirm(this, L10n.T("Baskı sürüyor"),
                L10n.T("Baskı devam ediyor. Durdurup pencereyi kapatmak ister misiniz? (Kuyruğa gönderilmiş etiketler basılmaya devam edebilir.)"),
                L10n.T("Durdur ve kapat"), L10n.T("Bekle"));
            if (!stop) return;
            _vm.CancelCommand.Execute(null);
            _closeConfirmed = true;
            Close();
        });
    }
}

/// <summary>Hızlı Baskı penceresi: açılınca okutma kutusuna odaklanır (barkod okuyucu klavye gibi yazar).</summary>
public partial class QuickPrintWindow : DialogWindow
{
    private readonly QuickPrintViewModel? _vm;

    public QuickPrintWindow() => InitializeComponent();

    public QuickPrintWindow(QuickPrintViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
        Opened += (_, _) => ScanBox.Focus();
        vm.CloseRequested += (_, _) => Finish(true);
        vm.FocusCopiesRequested += (_, _) => CopiesBox.Focus();
    }

    private void OnManageProducts(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        DialogHost.ShowModal<bool>(new ProductsWindow(_vm.Products), this);
        _vm.Products.Refresh();
    }
}

/// <summary>Kalibrasyon yardımcısı penceresi.</summary>
public partial class CalibrationWindow : DialogWindow
{
    public CalibrationWindow() => InitializeComponent();

    public CalibrationWindow(CalibrationViewModel vm) : this() => DataContext = vm;

    private void OnClose(object? sender, RoutedEventArgs e) => Finish(null);
}

/// <summary>Baskı geçmişi penceresi.</summary>
public partial class PrintHistoryWindow : DialogWindow
{
    private readonly PrintHistoryViewModel? _vm;

    public PrintHistoryWindow() => InitializeComponent();

    public PrintHistoryWindow(PrintHistoryViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
        vm.CloseRequested += OnCloseRequested;
        Closed += (_, _) => vm.CloseRequested -= OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Finish(true);

    private void OnClose(object? sender, RoutedEventArgs e) => Finish(null);

    private void OnToday(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.FromDate = DateTime.Today;
        _vm.ToDate = DateTime.Today;
    }

    private void OnLastWeek(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.FromDate = DateTime.Today.AddDays(-6);
        _vm.ToDate = DateTime.Today;
    }
}
