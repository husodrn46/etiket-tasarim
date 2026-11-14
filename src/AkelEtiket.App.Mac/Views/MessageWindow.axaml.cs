using AkelEtiket.App.Mac.Services;
using AkelEtiket.Editor.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.App.Mac.Views;

/// <summary>Temaya uygun mesaj / onay penceresi. Sonuç, basılan düğmenin sırasıdır (pencere kapatılırsa -1).</summary>
public partial class MessageWindow : DialogWindow
{
    private readonly int _cancelIndex;

    public MessageWindow() : this("", "", MessageKind.Info, [(L10n.T("Tamam"), true)])
    {
    }

    internal MessageWindow(string title, string message, MessageKind kind, IReadOnlyList<(string Text, bool Accent)> buttons)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        (string icon, string brush) = kind switch
        {
            MessageKind.Error => ("Glyph.Error", "Brush.Error"),
            MessageKind.Warning => ("Glyph.Warning", "Brush.Warning"),
            _ => ("Glyph.Info", "Brush.Info"),
        };
        if (this.TryFindResource(icon, out object? data) && data is Geometry geometry) KindIcon.Data = geometry;
        KindIcon[!ForegroundProperty] = this.GetResourceObservable(brush).ToBinding();

        // Esc: birden çok düğme varsa sonuncusu (vazgeç); tek düğmede o düğme.
        _cancelIndex = buttons.Count > 1 ? buttons.Count - 1 : 0;
        for (int i = 0; i < buttons.Count; i++)
        {
            int index = i;
            var button = new Button { Content = buttons[i].Text, IsDefault = buttons[i].Accent };
            button.Classes.Add("dialog");
            if (buttons[i].Accent) button.Classes.Add("accent");
            button.Click += (_, _) => Finish(index);
            Buttons.Children.Add(button);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape) return;
        Finish(_cancelIndex);
        e.Handled = true;
    }

    private int ShowAndWait(Window? owner)
    {
        DialogHost.ShowModal<object>(this, owner);
        return Result is int index ? index : -1;
    }

    private static int Run(Window? owner, string title, string message, MessageKind kind, params (string, bool)[] buttons)
    {
        var window = new MessageWindow(title, message, kind, buttons);
        return window.ShowAndWait(owner);
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
