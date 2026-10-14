using System.Windows;
using System.Windows.Input;
using AkelEtiket.Editor.ViewModels;

namespace AkelEtiket.App.Views;

/// <summary>Etiket ölçüsü penceresi.</summary>
public partial class LabelSetupWindow : ThemedWindow
{
    public LabelSetupWindow(LabelSetupViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        (Keyboard.FocusedElement as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        if (DataContext is LabelSetupViewModel { IsValid: true }) DialogResult = true;
    }
}
