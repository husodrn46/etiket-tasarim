using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AkelEtiket.App.Mac.Views;

/// <summary>Tek satırlık metin sorar. Sonuç girilen metindir; vazgeçilirse null.</summary>
public partial class PromptWindow : DialogWindow
{
    public PromptWindow() : this("", "", "")
    {
    }

    public PromptWindow(string title, string message, string defaultValue)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        MessageText.IsVisible = !string.IsNullOrWhiteSpace(message);
        Input.Text = defaultValue;
        Opened += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Finish(Input.Text ?? "");

    private void OnCancel(object? sender, RoutedEventArgs e) => Finish(null);
}
