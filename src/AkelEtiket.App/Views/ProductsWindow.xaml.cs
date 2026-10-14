using AkelEtiket.Editor.ViewModels;

namespace AkelEtiket.App.Views;

/// <summary>Ürün listesi penceresi.</summary>
public partial class ProductsWindow : ThemedWindow
{
    public ProductsWindow(ProductsViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}
