using Avalonia;

namespace AkelEtiket.App.Mac;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>Tasarım zamanı önizleyicisi ve testler de bu yapılandırmayı kullanır.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
