using System.IO;
using System.Windows;
using AkelEtiket.App.Views;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;
using Microsoft.Win32;

namespace AkelEtiket.App.Services;

/// <summary>Görünüm modellerinin istediği pencereleri açar.</summary>
public sealed class WpfDialogService : IDialogService
{
    public Window? Owner { get; set; }

    private Window? ActiveOwner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Owner;

    public string? ShowOpenFileDialog(string title, string filter, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        if (initialDirectory is not null && Directory.Exists(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog(ActiveOwner) == true ? dialog.FileName : null;
    }

    public string? ShowSaveFileDialog(string title, string filter, string defaultFileName, string? initialDirectory = null)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultFileName, OverwritePrompt = true, AddExtension = true };
        if (initialDirectory is not null && Directory.Exists(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog(ActiveOwner) == true ? dialog.FileName : null;
    }

    public void ShowMessage(string title, string message, MessageKind kind = MessageKind.Info) =>
        MessageWindow.Show(ActiveOwner, title, message, kind);

    public bool Confirm(string title, string message, string confirmText = "Evet", string cancelText = "Hayır") =>
        MessageWindow.Confirm(ActiveOwner, title, message, confirmText, cancelText);

    public SaveChangesChoice AskSaveChanges(string documentName) => MessageWindow.AskSaveChanges(ActiveOwner, documentName);

    public string? Prompt(string title, string message, string defaultValue = "")
    {
        var window = new PromptWindow(title, message, defaultValue) { Owner = ActiveOwner };
        return window.ShowDialog() == true ? window.Value : null;
    }

    public bool ShowDialog(object viewModel)
    {
        Window window = viewModel switch
        {
            PrintViewModel print => new PrintWindow(print),
            PrintHistoryViewModel history => new PrintHistoryWindow(history),
            CalibrationViewModel calibration => new CalibrationWindow(calibration),
            CountersViewModel counters => new CountersWindow(counters),
            PreviewViewModel preview => new PreviewWindow(preview),
            FirstSetupViewModel first => new FirstSetupWindow(first),
            ColumnMappingViewModel mapping => new ColumnMappingWindow(mapping),
            SettingsViewModel settings => new SettingsWindow(settings),
            LabelSetupViewModel setup => new LabelSetupWindow(setup),
            DataSourceViewModel data => new DataTableWindow(data),
            Gs1ProfileViewModel gs1 => new Gs1ProfileWindow(gs1),
            Gs1ContentBuilderViewModel builder => new Gs1ContentBuilderWindow(builder),
            AkelEtiket.Editor.Ai.AiSettingsViewModel ai => new AiSettingsWindow(ai),
            QuickPrintViewModel quick => new QuickPrintWindow(quick),
            ProductsViewModel products => new ProductsWindow(products),
            HelpViewModel help => new HelpWindow(help),
            BackupsViewModel backups => new BackupsWindow(backups),
            _ => throw new NotSupportedException($"Bu görünüm modeli için pencere yok: {viewModel.GetType().Name}"),
        };
        window.Owner = ActiveOwner;
        return window.ShowDialog() == true;
    }
}
