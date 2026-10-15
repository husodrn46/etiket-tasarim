using System.Windows;
using AkelEtiket.Editor.ViewModels;
namespace AkelEtiket.App.Views;
public partial class ColumnMappingWindow : ThemedWindow
{
    public ColumnMappingWindow(ColumnMappingViewModel vm) { DataContext = vm; InitializeComponent(); }
    private void OnApply(object sender, RoutedEventArgs e) => DialogResult = true;
}
