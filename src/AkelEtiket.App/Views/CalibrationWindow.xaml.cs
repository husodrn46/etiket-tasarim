using System.Windows;
using AkelEtiket.Editor.ViewModels;

namespace AkelEtiket.App.Views;

/// <summary>Kalibrasyon yardımcısı penceresi.</summary>
public partial class CalibrationWindow : ThemedWindow
{
    public CalibrationWindow(CalibrationViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
