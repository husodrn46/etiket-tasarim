using CommunityToolkit.Mvvm.Input;

namespace AkelEtiket.Editor.ViewModels;

public sealed partial class MainViewModel
{
    [RelayCommand]
    private void OpenProduction()
    {
        using var production = new ProductionViewModel(this);
        Dialogs.ShowDialog(production);
    }
}
