using System.Globalization;
using System.Reflection;
using AkelEtiket.App.Mac.Services;
using AkelEtiket.App.Mac.Views;
using AkelEtiket.Core;
using AkelEtiket.Core.Localization;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.ProjectManager;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace AkelEtiket.App.Mac;

/// <summary>Uygulama girişi: arayüz dili, tema, servisler, ana pencere ve genel hata yakalama.</summary>
public partial class App : Application
{
    private MainViewModel? _main;

    public static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "2.1.0";

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) Start(desktop);
        base.OnFrameworkInitializationCompleted();
    }

    private void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var systemUiCulture = CultureInfo.CurrentUICulture;
        var settingsStore = new JsonSettingsStore<AppSettings>(Path.Combine(JsonSettingsStore<AppSettings>.DefaultFolder, "ayarlar.json"));
        var startupSettings = settingsStore.Load();
        startupSettings.Normalize();
        UiText.ApplyLanguage(startupSettings.UiLanguage, systemUiCulture);

        Dispatcher.UIThread.UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => CrashLog.Write(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Write(args.Exception);
            args.SetObserved();
        };

        ProjectFile.AppVersion = Version;
        var dialogs = new AvaloniaDialogService();
        var services = new AppServices
        {
            Dialogs = dialogs,
            Clipboard = new AvaloniaClipboardService(() => desktop.MainWindow),
            Dispatcher = new AvaloniaDispatcher(),
            ImageFactory = new AvaloniaLayerImageFactory(),
            SettingsStore = settingsStore,
            InitialSettings = startupSettings,
            RecoveryFolder = AutoRecoveryService.DefaultFolder,
            RecycleFile = ShellHelper.SendToTrash,
            OpenFolder = ShellHelper.OpenFolder,
            AppVersion = Version,
        };

        _main = new MainViewModel(services);
        ThemeManager.Configure(_main.Settings);
        var window = new MainWindow(_main);
        dialogs.Owner = window;
        desktop.MainWindow = window;
        desktop.Exit += (_, _) => _main?.Dispose();
        // Açılış soruları (kurtarma, ilk kurulum) ana pencere göründükten sonra sorulur.
        string[] args = desktop.Args ?? [];
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => _main.Initialize(args), DispatcherPriority.Background);
    }

    private void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string logPath = CrashLog.Write(e.Exception);
        e.Handled = true;
        try
        {
            MessageWindow.Show(null, UiText.T("Beklenmeyen hata"),
                UiText.T("Bir hata oluştu, ancak uygulama çalışmaya devam ediyor. Çalışmanızı kaydetmeniz önerilir.\n\n") +
                e.Exception.Message + UiText.T($"\n\nAyrıntılar: {logPath}"), MessageKind.Error);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            CrashLog.Write(ex);
        }
    }
}

/// <summary>Hata günlüğü (yerel veri klasörünün Günlük alt klasörü).</summary>
internal static class CrashLog
{
    public static string Write(Exception? ex)
    {
        try
        {
            string folder = AppIdentity.LogFolder;
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"hata-{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
            return path;
        }
        catch (Exception)
        {
            return UiText.T("(günlük yazılamadı)");
        }
    }
}
