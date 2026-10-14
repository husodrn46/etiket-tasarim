using System.Windows;
using AkelEtiket.Editor.ViewModels;

namespace AkelEtiket.App.Views;

/// <summary>GS1 içerik oluşturucu penceresi (GS1-128, GS1 DataMatrix, GS1 QR).</summary>
public partial class Gs1ContentBuilderWindow : ThemedWindow
{
    private readonly Gs1ContentBuilderViewModel _vm;

    public Gs1ContentBuilderWindow(Gs1ContentBuilderViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (_vm.CanAccept) DialogResult = true;
    }
}
