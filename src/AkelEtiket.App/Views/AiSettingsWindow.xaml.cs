using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AkelEtiket.Editor.Ai;
using Microsoft.Win32;

namespace AkelEtiket.App.Views;

/// <summary>Yapay Zekâ ayarları penceresi.</summary>
public partial class AiSettingsWindow : ThemedWindow
{
    private readonly AiSettingsViewModel _vm;

    public AiSettingsWindow(AiSettingsViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    private void OnKeyChanged(object sender, RoutedEventArgs e) => _vm.NewApiKey = KeyBox.Password;

    private void OnClearKey(object sender, RoutedEventArgs e) => KeyBox.Clear();

    private void OnCopyToken(object sender, RoutedEventArgs e) => TryCopy(_vm.Token);

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text }) TryCopy(text);
    }

    private static void TryCopy(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Pano başka bir uygulamada kilitliyse sessizce geç.
        }
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Yapay zekânın okuyabileceği klasörü seçin" };
        if (dialog.ShowDialog(this) == true) _vm.AddFolder(dialog.FolderName);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        (Keyboard.FocusedElement as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        if (_vm.Validate() is { } problem)
        {
            ErrorText.Text = problem;
            return;
        }
        DialogResult = true;
    }
}
