using System.Globalization;
using AkelEtiket.Core.Localization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using AkelEtiket.App.Services;
using AkelEtiket.App.Views;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.App;

/// <summary>Uygulama girişi: Arayüz dili, tema, servisler, ana pencere ve genel hata yakalama.</summary>
public partial class App : Application
{
    private MainViewModel? _main;

    public static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "2.1.0";

    protected override void OnStartup(StartupEventArgs e)
    {
        var systemUiCulture = CultureInfo.CurrentUICulture;
        var settingsStore = new JsonSettingsStore<AppSettings>(Path.Combine(JsonSettingsStore<AppSettings>.DefaultFolder, "ayarlar.json"));
        bool importedLegacy = LegacySettingsImport.TryImport(settingsStore, Core.AppIdentity.LegacySettingsFile, Core.AppIdentity.LegacyTemplatesFolder);
        var startupSettings = settingsStore.Load();
        startupSettings.Normalize();
        UiText.ApplyLanguage(startupSettings.UiLanguage, systemUiCulture);
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => CrashLog.Write(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Write(args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
        ProjectFile.AppVersion = Version;

        var dialogs = new WpfDialogService();
        // v1'den geçiş: v2'nin ayar dosyası yoksa v1'in ayarları devralınır (v1'in dosyası değişmez).
        var services = new AppServices
        {
            Dialogs = dialogs,
            Clipboard = new WpfClipboardService(),
            Dispatcher = new WpfDispatcher(Dispatcher),
            ImageFactory = new WpfLayerImageFactory(),
            SettingsStore = settingsStore,
            InitialSettings = startupSettings,
            RecoveryFolder = AutoRecoveryService.DefaultFolder,
            RecycleFile = ShellHelper.SendToRecycleBin,
            OpenFolder = ShellHelper.OpenFolder,
            AppVersion = Version,
            ImportedLegacySettings = importedLegacy,
        };

        _main = new MainViewModel(services);
        ThemeManager.Configure(_main.Settings);
        var window = new MainWindow(_main);
        dialogs.Owner = window;
        MainWindow = window;
        window.Show();
        _main.Initialize(e.Args);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string logPath = CrashLog.Write(e.Exception);
        MessageWindow.Show(MainWindow, UiText.T("Beklenmeyen hata"),
            UiText.T("Bir hata oluştu, ancak uygulama çalışmaya devam ediyor. Çalışmanızı kaydetmeniz önerilir.\n\n") +
            e.Exception.Message + UiText.T($"\n\nAyrıntılar: {logPath}"), MessageKind.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _main?.Dispose();
        base.OnExit(e);
    }
}

/// <summary>Hata günlüğü (%LOCALAPPDATA%\AKEL Etiket\Günlük).</summary>
internal static class CrashLog
{
    public static string Write(Exception? ex)
    {
        try
        {
            string folder = AkelEtiket.Core.AppIdentity.LogFolder;
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"hata-{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
            return path;
        }
        catch (Exception)
        {
            return UiText.T("(günlük yazılamadı)");
        }
    }
}
