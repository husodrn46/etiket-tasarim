using System.Windows;
using System.Windows.Input;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.Editor.Services;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.App.Views;

public partial class FirstSetupWindow : ThemedWindow
{
    private readonly FirstSetupViewModel _vm;
    public FirstSetupWindow(FirstSetupViewModel vm)
    {
        _vm = vm; DataContext = vm; InitializeComponent();
        Loaded += (_, _) => vm.RefreshPrintersCommand.Execute(null);
    }
    private void OnSave(object sender, RoutedEventArgs e)
    {
        (Keyboard.FocusedElement as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        if (_vm.ValidationProblem is { } problem)
        { MessageWindow.Show(this, L10n.T("Etiket ölçüsü"), problem, MessageKind.Warning); return; }
        DialogResult = true;
    }
}
