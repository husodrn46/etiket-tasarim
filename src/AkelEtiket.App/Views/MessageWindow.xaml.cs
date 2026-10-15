using System.Windows;
using System.Windows.Controls;
using AkelEtiket.Editor.Services;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.App.Views;

/// <summary>Temaya uygun mesaj / onay penceresi.</summary>
public partial class MessageWindow : ThemedWindow
{
    private int _result = -1;

    private MessageWindow(string title, string message, MessageKind kind, IReadOnlyList<(string Text, bool Accent)> buttons)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        (IconText.Text, string brush) = kind switch
        {
            MessageKind.Error => ("\uE783", "Brush.Error"),
            MessageKind.Warning => ("\uE7BA", "Brush.Warning"),
            _ => ("\uE946", "Brush.Info"),
        };
        IconText.SetResourceReference(TextBlock.ForegroundProperty, brush);
        for (int i = 0; i < buttons.Count; i++)
        {
            int index = i;
            var button = new Button
            {
                Content = buttons[i].Text,
                MinWidth = 96,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = buttons[i].Accent,
                IsCancel = i == buttons.Count - 1 && buttons.Count > 1,
            };
            if (buttons[i].Accent) button.Style = (Style)FindResource("Button.Accent");
            button.Click += (_, _) =>
            {
                _result = index;
                DialogResult = true;
            };
            Buttons.Children.Add(button);
        }
    }

    private static Window? ResolveOwner(Window? owner) =>
        owner is { IsLoaded: true } ? owner : Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    private static int Run(Window? owner, string title, string message, MessageKind kind, params (string, bool)[] buttons)
    {
        var window = new MessageWindow(title, message, kind, buttons);
        var resolved = ResolveOwner(owner);
        if (resolved is not null && !ReferenceEquals(resolved, window) && resolved.IsVisible) window.Owner = resolved;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.ShowDialog();
        return window._result;
    }

    public static void Show(Window? owner, string title, string message, MessageKind kind) =>
        Run(owner, title, message, kind, (L10n.T("Tamam"), true));

    public static bool Confirm(Window? owner, string title, string message, string confirmText, string cancelText) =>
        Run(owner, title, message, MessageKind.Warning, (confirmText, true), (cancelText, false)) == 0;

    public static SaveChangesChoice AskSaveChanges(Window? owner, string documentName) =>
        Run(owner, L10n.T("Kaydedilmemiş değişiklikler"), L10n.T($"\"{documentName}\" belgesindeki değişiklikler kaydedilsin mi?"),
                MessageKind.Warning, (L10n.T("Kaydet"), true), (L10n.T("Kaydetme"), false), (L10n.T("Vazgeç"), false)) switch
            {
                0 => SaveChangesChoice.Save,
                1 => SaveChangesChoice.Discard,
                _ => SaveChangesChoice.Cancel,
            };
}
