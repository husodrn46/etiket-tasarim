using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AkelEtiket.Editor.ViewModels;

namespace AkelEtiket.App.Views;

/// <summary>GS1 Firma Profili penceresi.</summary>
public partial class Gs1ProfileWindow : ThemedWindow
{
    private readonly Gs1ProfileViewModel _vm;

    public Gs1ProfileWindow(Gs1ProfileViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // Düzenlenmekte olan hücreyi ve odaktaki kutuyu uygula.
        GtinGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        (Keyboard.FocusedElement as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        if (_vm.Save()) DialogResult = true;
    }
}
