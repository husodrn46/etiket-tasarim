using AkelEtiket.App.Mac.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AkelEtiket.App.Mac.Views;

/// <summary>
/// Kalıcı (modal) pencerelerin ortak tabanı. Sonuç <see cref="Finish"/> ile verilir; pencere başlık çubuğundan
/// ya da vazgeç düğmesiyle kapatılırsa sonuç null kalır.
/// </summary>
public class DialogWindow : Window
{
    private bool _closing;

    protected override Type StyleKeyOverride => typeof(Window);

    public DialogWindow()
    {
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // Vazgeç düğmesi (IsCancel; Esc de onu tetikler) pencereyi sonuçsuz kapatır.
        AddHandler(Button.ClickEvent, OnAnyButtonClick, RoutingStrategies.Bubble);
        Opened += (_, _) => FitToScreen();
        Closing += (_, _) => _closing = true;
    }

    /// <summary>Pencerenin sonucu (kapatılana kadar null).</summary>
    public object? Result { get; private set; }

    protected void Finish(object? result)
    {
        if (_closing) return;
        Result = result;
        Close(result);
    }

    /// <summary>Odaktaki kutunun değerini uygulayıp pencereyi onaylar.</summary>
    protected void Accept()
    {
        CommitFocusedInput();
        Finish(true);
    }

    /// <summary>Değerini odak kaybında yazan kutuların (sayı kutusu, bazı metin kutuları) bekleyen girdisini uygular.</summary>
    protected void CommitFocusedInput()
    {
        if (FocusManager?.GetFocusedElement() is NumberBox number) number.Commit();
        else if (FocusManager?.GetFocusedElement() is TextBox) FocusManager.TryMoveFocus(NavigationDirection.Next);
    }

    private void OnAnyButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { IsCancel: true } && !_closing) Close();
    }

    /// <summary>Pencere ekranın çalışma alanından uzunsa kısaltılır (küçük ekranlarda düğmeler görünür kalsın).</summary>
    private void FitToScreen()
    {
        if (Screens.ScreenFromWindow(this) is not { } screen) return;
        double available = screen.WorkingArea.Height / screen.Scaling - 40;
        if (available > 200 && Height > available) Height = available;
    }
}
