using System.Windows;
using AkelEtiket.Editor.ViewModels;

namespace AkelEtiket.App.Views;

/// <summary>Baskı geçmişi penceresi.</summary>
public partial class PrintHistoryWindow : ThemedWindow
{
    private readonly PrintHistoryViewModel _vm;

    public PrintHistoryWindow(PrintHistoryViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        vm.CloseRequested += OnCloseRequested;
        Closed += (_, _) => vm.CloseRequested -= OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => DialogResult = true;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnToday(object sender, RoutedEventArgs e)
    {
        _vm.FromDate = DateTime.Today;
        _vm.ToDate = DateTime.Today;
    }

    private void OnLastWeek(object sender, RoutedEventArgs e)
    {
        _vm.FromDate = DateTime.Today.AddDays(-6);
        _vm.ToDate = DateTime.Today;
    }
}
