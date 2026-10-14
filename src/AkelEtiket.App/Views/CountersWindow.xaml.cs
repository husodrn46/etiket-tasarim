using System.Windows;
using AkelEtiket.Editor.ViewModels;

namespace AkelEtiket.App.Views;

/// <summary>Kalıcı sayaçlar penceresi.</summary>
public partial class CountersWindow : ThemedWindow
{
    public CountersWindow(CountersViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
