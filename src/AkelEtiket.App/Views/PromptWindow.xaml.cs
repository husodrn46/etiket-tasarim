using System.Windows;

namespace AkelEtiket.App.Views;

/// <summary>Tek satırlık metin girişi penceresi.</summary>
public partial class PromptWindow : ThemedWindow
{
    public PromptWindow(string title, string message, string defaultValue)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        Input.Text = defaultValue;
        Loaded += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    public string Value => Input.Text;

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
