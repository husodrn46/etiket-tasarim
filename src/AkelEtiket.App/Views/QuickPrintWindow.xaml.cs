using System.Windows;
using AkelEtiket.Editor.ViewModels;

namespace AkelEtiket.App.Views;

/// <summary>Hızlı Baskı penceresi: açılınca okutma kutusuna odaklanır (barkod okuyucu klavye gibi yazar).</summary>
public partial class QuickPrintWindow : ThemedWindow
{
    private readonly QuickPrintViewModel _vm;

    public QuickPrintWindow(QuickPrintViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        Loaded += (_, _) => ScanBox.Focus();
        vm.CloseRequested += (_, _) => DialogResult = true;
        vm.FocusCopiesRequested += (_, _) => CopiesBox.Focus();
    }

    private void OnManageProducts(object sender, RoutedEventArgs e)
    {
        var products = new ProductsWindow(_vm.Products) { Owner = this };
        products.ShowDialog();
        _vm.Products.Refresh();
    }
}
