using AkelEtiket.App.Mac.Views;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.App.Mac.Services;

/// <summary>Kalıcı (modal) pencereleri, çağıran kod sonucu bekleyecek biçimde açar.</summary>
internal static class DialogHost
{
    /// <summary>Etkin pencere; yoksa ana pencere.</summary>
    public static Window? ActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return null;
        return desktop.Windows.LastOrDefault(w => w.IsActive && w.IsVisible) ?? desktop.MainWindow;
    }

    /// <summary>Görünüm modelinin penceresini kalıcı açar. true = onaylandı.</summary>
    public static bool Show(object viewModel, Window? owner = null)
    {
        DialogWindow? window = DialogWindows.Create(viewModel);
        if (window is null)
        {
            // Bu pencere macOS sürümüne henüz taşınmadı: sessizce yutmak yerine açıkça söylenir.
            MessageWindow.Show(owner, L10n.T("macOS sürümü"), L10n.T("Bu pencere macOS sürümünde henüz yok."), MessageKind.Info);
            return false;
        }
        return ShowModal<bool>(window, owner);
    }

    public static T? ShowModal<T>(DialogWindow dialog, Window? owner)
    {
        owner = owner is { IsVisible: true } ? owner : ActiveWindow();
        if (owner is { IsVisible: true } && !ReferenceEquals(owner, dialog))
        {
            UiWait.Run(() => dialog.ShowDialog(owner));
        }
        else
        {
            // Sahip pencere yok (açılışta): bağımsız pencere olarak gösterilir ve kapanması beklenir.
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var closed = new TaskCompletionSource<bool>();
            dialog.Closed += (_, _) => closed.TrySetResult(true);
            dialog.Show();
            UiWait.Run(() => closed.Task);
        }
        return dialog.Result is T result ? result : default;
    }
}

/// <summary>Görünüm modellerinin istediği pencereleri ve dosya seçicilerini açar.</summary>
public sealed class AvaloniaDialogService : IDialogService
{
    /// <summary>Ana pencere (başka etkin pencere yoksa diyalogların sahibi).</summary>
    public Window? Owner { get; set; }

    private Window? ActiveOwner => DialogHost.ActiveWindow() ?? Owner;

    public string? ShowOpenFileDialog(string title, string filter, string? initialDirectory = null) =>
        PickFiles(title, filter, initialDirectory, multiple: false).FirstOrDefault();

    public IReadOnlyList<string> ShowOpenFilesDialog(string title, string filter, string? initialDirectory = null) =>
        PickFiles(title, filter, initialDirectory, multiple: true);

    private IReadOnlyList<string> PickFiles(string title, string filter, string? initialDirectory, bool multiple)
    {
        if (ActiveOwner is not { } owner) return [];
        return UiWait.Run(async () =>
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = multiple,
                FileTypeFilter = FileFilters.Parse(L10n.T(filter)),
                SuggestedStartLocation = await StartFolder(owner, initialDirectory).ConfigureAwait(true),
            }).ConfigureAwait(true);
            return (IReadOnlyList<string>)files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        });
    }

    public string? ShowSaveFileDialog(string title, string filter, string defaultFileName, string? initialDirectory = null)
    {
        if (ActiveOwner is not { } owner) return null;
        return UiWait.Run(async () =>
        {
            var types = FileFilters.Parse(L10n.T(filter));
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = defaultFileName,
                DefaultExtension = FileFilters.DefaultExtension(types),
                FileTypeChoices = types,
                ShowOverwritePrompt = true,
                SuggestedStartLocation = await StartFolder(owner, initialDirectory).ConfigureAwait(true),
            }).ConfigureAwait(true);
            return file?.TryGetLocalPath();
        });
    }

    public string? ShowFolderDialog(string title, string? initialDirectory = null)
    {
        if (ActiveOwner is not { } owner) return null;
        return UiWait.Run(async () =>
        {
            var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = await StartFolder(owner, initialDirectory).ConfigureAwait(true),
            }).ConfigureAwait(true);
            return folders.FirstOrDefault()?.TryGetLocalPath();
        });
    }

    private static async Task<IStorageFolder?> StartFolder(TopLevel owner, string? directory) =>
        directory is not null && Directory.Exists(directory)
            ? await owner.StorageProvider.TryGetFolderFromPathAsync(directory).ConfigureAwait(true)
            : null;

    public void ShowMessage(string title, string message, MessageKind kind = MessageKind.Info) =>
        MessageWindow.Show(ActiveOwner, title, message, kind);

    public bool Confirm(string title, string message, string confirmText = "Evet", string cancelText = "Hayır") =>
        MessageWindow.Confirm(ActiveOwner, title, message, L10n.T(confirmText), L10n.T(cancelText));

    public SaveChangesChoice AskSaveChanges(string documentName) => MessageWindow.AskSaveChanges(ActiveOwner, documentName);

    public string? Prompt(string title, string message, string defaultValue = "") =>
        DialogHost.ShowModal<string?>(new PromptWindow(title, message, defaultValue), ActiveOwner);

    public bool ShowDialog(object viewModel) => DialogHost.Show(viewModel, ActiveOwner);
}

/// <summary>Görünüm modeline karşılık gelen pencere. Taşınan her pencere buraya eklenir.</summary>
internal static class DialogWindows
{
    public static DialogWindow? Create(object viewModel) => viewModel switch
    {
        LabelSetupViewModel setup => new LabelSetupWindow(setup),
        PreviewViewModel preview => new PreviewWindow(preview),
        SettingsViewModel settings => new SettingsWindow(settings),
        FirstSetupViewModel first => new FirstSetupWindow(first),
        HelpViewModel help => new HelpWindow(help),
        StarterPacksViewModel packs => new StarterPacksWindow(packs),
        BackupsViewModel backups => new BackupsWindow(backups),
        ColumnMappingViewModel mapping => new ColumnMappingWindow(mapping),
        ProductsViewModel products => new ProductsWindow(products),
        CountersViewModel counters => new CountersWindow(counters),
        DataSourceViewModel data => new DataTableWindow(data),
        ProductionViewModel production => new ProductionWindow(production),
        PrintViewModel print => new PrintWindow(print),
        QuickPrintViewModel quick => new QuickPrintWindow(quick),
        CalibrationViewModel calibration => new CalibrationWindow(calibration),
        PrintHistoryViewModel history => new PrintHistoryWindow(history),
        _ => null,
    };
}

/// <summary>Windows biçimindeki dosya süzgeci metnini ("Ad|*.a;*.b|Ad 2|*.*") seçici türlerine çevirir.</summary>
internal static class FileFilters
{
    public static IReadOnlyList<FilePickerFileType> Parse(string filter)
    {
        var parts = filter.Split('|');
        var types = new List<FilePickerFileType>();
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            string[] patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (patterns.Length == 0) continue;
            types.Add(new FilePickerFileType(parts[i].Trim()) { Patterns = patterns });
        }
        return types;
    }

    /// <summary>İlk türün ilk uzantısı (nokta olmadan); "*.*" ise null.</summary>
    public static string? DefaultExtension(IReadOnlyList<FilePickerFileType> types)
    {
        string? pattern = types.FirstOrDefault()?.Patterns?.FirstOrDefault();
        if (pattern is null || !pattern.StartsWith("*.", StringComparison.Ordinal)) return null;
        string extension = pattern[2..];
        return extension.Length == 0 || extension.Contains('*') ? null : extension;
    }
}
