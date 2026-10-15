using AkelEtiket.Editor.ViewModels;
namespace AkelEtiket.App.Views;
public partial class StarterPacksWindow : ThemedWindow
{
    public StarterPacksWindow(StarterPacksViewModel vm) { DataContext=vm;InitializeComponent(); }
}
