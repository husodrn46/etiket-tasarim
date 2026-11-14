using Avalonia;
using Avalonia.Media;

namespace AkelEtiket.App.Mac;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>Tasarım zamanı önizleyicisi de bu yapılandırmayı kullanır.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().UseInterfaceFont().LogToTrace();
}

/// <summary>Uygulamanın ve arayüz testlerinin ortak yapılandırması.</summary>
public static class AppBuilderExtensions
{
    /// <summary>
    /// Arayüz yazı tipi. macOS'ta Avalonia'nın kendi seçtiği varsayılan (klasik "Helvetica") büyük harflerin üstündeki
    /// işaretlere yer bırakmaz: Ü, Ö, İ, Ğ kesilir ("Üretim" → "Uretim"). Satır ölçüleri doğru olan Helvetica Neue
    /// seçilir; bulunamazsa sistem yazı tipine düşülür. Diğer sistemlerde platformun varsayılanı kalır.
    /// </summary>
    public static AppBuilder UseInterfaceFont(this AppBuilder builder) => OperatingSystem.IsMacOS()
        ? builder.With(new FontManagerOptions
        {
            DefaultFamilyName = "Helvetica Neue",
            FontFallbacks = [new FontFallback { FontFamily = new FontFamily(".AppleSystemUIFont") }],
        })
        : builder;
}
