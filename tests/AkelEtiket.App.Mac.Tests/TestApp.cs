using System.Runtime.CompilerServices;
using AkelEtiket.App.Mac.Services;
using AkelEtiket.App.Mac.Views;
using AkelEtiket.Core;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.ProjectManager;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

[assembly: AvaloniaTestApplication(typeof(AkelEtiket.App.Mac.Tests.TestAppBuilder))]
// Avalonia uygulaması süreç genelinde tektir: bu derlemedeki testler sırayla çalışır.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AkelEtiket.App.Mac.Tests;

/// <summary>Bu test derlemesi bütün kullanıcı klasörlerini geçici bir köke yönlendirir (gerçek ayarlara dokunulmaz).</summary>
internal static class TestDataRoot
{
    public static readonly string Root = Path.Combine(Path.GetTempPath(), "labelharbor-mac-tests", Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Initialize() => Environment.SetEnvironmentVariable(AppIdentity.DataRootVariable, Root);
}

/// <summary>Gerçek uygulama sınıfı, gerçek Skia çizimi; yalnızca pencere sistemi penceresizdir.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        Avalonia.Logging.Logger.Sink = BindingErrors.Sink;
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .UseInterfaceFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}

/// <summary>
/// Çalışma anında çözülen bağlamaların hatalarını toplar. Paneller öğenin somut türüne göre bağlandığı için yanlış
/// yazılmış bir özellik adı derlemede değil burada ortaya çıkar; testler bu listeyi boş bekler.
/// </summary>
internal sealed class BindingErrors : Avalonia.Logging.ILogSink
{
    public static readonly BindingErrors Sink = new();
    private readonly List<string> _errors = [];

    public bool IsEnabled(Avalonia.Logging.LogEventLevel level, string area) =>
        area == Avalonia.Logging.LogArea.Binding && level >= Avalonia.Logging.LogEventLevel.Warning;

    public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string messageTemplate) =>
        Log(level, area, source, messageTemplate, []);

    public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (!IsEnabled(level, area)) return;
        string message = messageTemplate;
        foreach (object? value in propertyValues)
        {
            int open = message.IndexOf('{');
            int close = open < 0 ? -1 : message.IndexOf('}', open);
            if (close < 0) break;
            message = message[..open] + value + message[(close + 1)..];
        }
        lock (_errors) _errors.Add($"{source?.GetType().Name}: {message}");
    }

    /// <summary>O ana kadar toplanan hataları döndürür ve listeyi boşaltır.</summary>
    public static IReadOnlyList<string> Take()
    {
        lock (Sink._errors)
        {
            var taken = Sink._errors.ToList();
            Sink._errors.Clear();
            return taken;
        }
    }
}

/// <summary>Testi Avalonia arayüz iş parçacığında çalıştırır.</summary>
internal static class Ui
{
    private static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(Ui).Assembly);

    public static void Run(Action test) => Session.Dispatch(test, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Bekleyen arayüz işlerini çalıştırır ve bir kare çizdirir.</summary>
    public static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Arayüz döngüsünü kısa bir süre çalıştırır: zamanlayıcılar (gecikmeli bağlamalar) da tetiklenir.</summary>
    public static void Wait(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.UIThread.PushFrame(frame);
    }

    /// <summary>Koşul sağlanana kadar arayüz işlerini çalıştırır (arka planda render edilen katmanlar için).</summary>
    public static void WaitFor(Func<bool> condition, string what, int timeoutMs = 10_000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            Pump();
            if (condition()) return;
            if (clock.ElapsedMilliseconds > timeoutMs) throw new TimeoutException($"Beklenen durum oluşmadı: {what}");
            Wait(15);
        }
    }
}

/// <summary>Diyalog çağrılarını kaydeden sahte servis (testlerde pencere açılmaz).</summary>
internal sealed class RecordingDialogs : IDialogService
{
    public List<string> Messages { get; } = [];
    public List<object> RequestedDialogs { get; } = [];
    public string? NextSavePath { get; set; }
    public string? NextOpenPath { get; set; }

    public string? ShowOpenFileDialog(string title, string filter, string? initialDirectory = null) => NextOpenPath;
    public string? ShowSaveFileDialog(string title, string filter, string defaultFileName, string? initialDirectory = null) => NextSavePath;
    public void ShowMessage(string title, string message, MessageKind kind = MessageKind.Info) => Messages.Add($"{kind}: {title}: {message}");
    public bool Confirm(string title, string message, string confirmText = "Evet", string cancelText = "Hayır") => true;
    public SaveChangesChoice AskSaveChanges(string documentName) => SaveChangesChoice.Discard;
    public string? Prompt(string title, string message, string defaultValue = "") => defaultValue;

    public bool ShowDialog(object viewModel)
    {
        RequestedDialogs.Add(viewModel);
        return false;
    }
}

internal sealed class MemoryClipboard : IClipboardService
{
    private string? _elements;
    private string? _text;

    public void SetElements(string payloadJson, string plainText) => (_elements, _text) = (payloadJson, plainText);
    public string? GetElements() => _elements;
    public bool ContainsElements() => _elements is not null;
    public byte[]? GetImagePng() => null;
    public string? GetText() => _text;
}

/// <summary>Gerçek ana pencere ve görünüm modeli; depodaki örnek etiketlerden biri açılır.</summary>
internal sealed class AppHarness : IDisposable
{
    public RecordingDialogs Dialogs { get; } = new();
    public MainViewModel Vm { get; }
    public MainWindow Window { get; }

    public AppHarness(string? sample = "Ornek-100x150-Koli-Etiketi-GS1.akelabel", double width = 1440, double height = 900)
    {
        string dataRoot = Path.Combine(TestDataRoot.Root, Guid.NewGuid().ToString("N"));
        var store = new JsonSettingsStore<AppSettings>(Path.Combine(dataRoot, "ayarlar.json"));
        var settings = store.Load();
        settings.Normalize();
        settings.Window.Maximized = false;
        settings.Window.Width = width;
        settings.Window.Height = height;
        Vm = new MainViewModel(new AppServices
        {
            Dialogs = Dialogs,
            Clipboard = new MemoryClipboard(),
            Dispatcher = new AvaloniaDispatcher(),
            ImageFactory = new AvaloniaLayerImageFactory(),
            SettingsStore = store,
            InitialSettings = settings,
            RecoveryFolder = Path.Combine(dataRoot, "Kurtarma"),
        });
        ThemeManager.Configure(Vm.Settings);
        Window = new MainWindow(Vm) { Width = width, Height = height };
        Window.Show();
        Vm.Initialize(sample is null ? [] : [SamplePath(sample)]);
        if (sample is not null)
            Ui.WaitFor(() => Vm.Session.Project.FilePath is not null, "örnek etiket açıldı");
        WaitForLayers();
    }

    public Controls.DesignSurface Surface => Window.FindControl<Controls.DesignSurface>("Surface")!;

    public static string SamplePath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AkelEtiket.sln"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı."), "samples", name);
    }

    /// <summary>Görünür her öğenin katmanı render edilene ve pencere yerleşimi oturana kadar bekler.</summary>
    public void WaitForLayers() =>
        Ui.WaitFor(() => Vm.Session.Elements.All(e => !e.Model.Visible || e.LayerImage is not null), "öğe katmanları render edildi");

    /// <summary>Etiket üzerindeki bir noktanın (mm) pencere koordinatı.</summary>
    public Point At(double xMm, double yMm) =>
        Surface.TranslatePoint(Surface.MmToDip(xMm, yMm), Window) ?? throw new InvalidOperationException("Tuval pencereye bağlı değil.");

    public void Click(Point p, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.MouseDown(p, MouseButton.Left, modifiers);
        Window.MouseUp(p, MouseButton.Left, modifiers);
        Ui.Pump();
    }

    /// <summary>Basılı tutarak sürükler; ara adımlar gerçek bir sürüklemedeki gibi tek tek gönderilir.</summary>
    public void Drag(Point from, Point to, RawInputModifiers modifiers = RawInputModifiers.None, bool release = true)
    {
        Window.MouseDown(from, MouseButton.Left, modifiers);
        const int steps = 8;
        for (int i = 1; i <= steps; i++)
        {
            var p = new Point(from.X + (to.X - from.X) * i / steps, from.Y + (to.Y - from.Y) * i / steps);
            Window.MouseMove(p, modifiers | RawInputModifiers.LeftMouseButton);
        }
        if (release) Window.MouseUp(to, MouseButton.Left, modifiers);
        Ui.Pump();
    }

    public void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPress(key, modifiers, PhysicalKey.None, null);
        Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Ui.Pump();
    }

    /// <summary>Pencerenin o anki görüntüsünü PNG olarak kaydeder (inceleme ve CI kanıtı için).</summary>
    public string Screenshot(string name)
    {
        Ui.Pump();
        using var frame = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Pencere çizilemedi.");
        string folder = Environment.GetEnvironmentVariable("LABELHARBOR_SCREENSHOTS") is { Length: > 0 } custom
            ? custom
            : Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name + ".png");
        frame.Save(path, PngBitmapEncoderOptions.Default);
        return path;
    }

    public void Dispose()
    {
        // Kaydedilmemiş değişiklik sorusu sahte servisle "kaydetme" olarak yanıtlanır.
        Window.Close();
        Ui.Pump();
        Ui.Pump();
        Vm.Dispose();
    }
}
