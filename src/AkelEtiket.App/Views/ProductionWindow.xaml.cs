using System.Windows;
using System.Windows.Controls;
using AkelEtiket.Editor.ViewModels;
namespace AkelEtiket.App.Views;
public partial class ProductionWindow : ThemedWindow
{
    private readonly ProductionViewModel _vm;
    private readonly HashSet<ValidationError> _inputErrors = [];
    public ProductionWindow(ProductionViewModel vm)
    {
        _vm = vm; DataContext = vm; InitializeComponent();
        // Override the shared fixed row height without the XAML Double converter's "Auto" parsing.
        FindingsGrid.RowHeight = double.NaN;
        AddHandler(Validation.ErrorEvent, new EventHandler<ValidationErrorEventArgs>((_, e) =>
        {
            if (e.Action == ValidationErrorEventAction.Added) _inputErrors.Add(e.Error); else _inputErrors.Remove(e.Error);
            vm.HasInputErrors = _inputErrors.Count > 0;
        }));
        Closed += (_, _) => vm.Dispose();
    }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
