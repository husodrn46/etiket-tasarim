using System.Windows;
using System.Windows.Input;
using AkelEtiket.Editor.ViewModels;
using Microsoft.Win32;

namespace AkelEtiket.App.Views;

/// <summary>Ayarlar penceresi.</summary>
public partial class SettingsWindow : ThemedWindow
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    private void OnBrowseTemplates(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Şablon klasörünü seçin", InitialDirectory = _vm.TemplatesFolder };
        if (dialog.ShowDialog(this) == true) _vm.TemplatesFolder = dialog.FolderName;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // Odaktaki sayı kutusunun değerini uygula.
        (Keyboard.FocusedElement as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        DialogResult = true;
    }
}
