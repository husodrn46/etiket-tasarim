using Avalonia.Controls;

namespace AkelEtiket.App.Mac.Views;

/// <summary>
/// Kalıcı (modal) pencerelerin ortak tabanı. Sonuç <see cref="Finish"/> ile verilir; pencere başlık çubuğundan
/// kapatılırsa sonuç null kalır.
/// </summary>
public class DialogWindow : Window
{
    protected override Type StyleKeyOverride => typeof(Window);

    public DialogWindow()
    {
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    /// <summary>Pencerenin sonucu (kapatılana kadar null).</summary>
    public object? Result { get; private set; }

    protected void Finish(object? result)
    {
        Result = result;
        Close(result);
    }
}
