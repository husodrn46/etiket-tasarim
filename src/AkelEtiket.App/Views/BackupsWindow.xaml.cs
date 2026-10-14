using AkelEtiket.Editor.ViewModels;

namespace AkelEtiket.App.Views;

/// <summary>Tasarım yedekleri penceresi.</summary>
public partial class BackupsWindow : ThemedWindow
{
    public BackupsWindow(BackupsViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
        vm.CloseRequested += (_, _) => DialogResult = true;
    }
}
