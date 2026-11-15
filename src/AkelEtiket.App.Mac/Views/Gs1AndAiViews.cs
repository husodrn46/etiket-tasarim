using AkelEtiket.App.Mac.Services;
using AkelEtiket.Editor.Ai;
using AkelEtiket.Editor.ViewModels;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.App.Mac.Views;

// GS1 pencereleri ile yapay zekâ ayarları ve paneli.

/// <summary>GS1 Firma Profili penceresi.</summary>
public partial class Gs1ProfileWindow : DialogWindow
{
    private readonly Gs1ProfileViewModel? _vm;

    public Gs1ProfileWindow() => InitializeComponent();

    public Gs1ProfileWindow(Gs1ProfileViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        // Düzenlenmekte olan hücreyi ve odaktaki kutuyu uygula.
        GtinGrid.CommitEdit(DataGridEditingUnit.Row, true);
        CommitFocusedInput();
        if (_vm?.Save() == true) Finish(true);
    }
}

/// <summary>GS1 içerik oluşturucu penceresi (GS1-128, GS1 DataMatrix, GS1 QR).</summary>
public partial class Gs1ContentBuilderWindow : DialogWindow
{
    private readonly Gs1ContentBuilderViewModel? _vm;

    public Gs1ContentBuilderWindow() => InitializeComponent();

    public Gs1ContentBuilderWindow(Gs1ContentBuilderViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        if (_vm?.CanAccept == true) Finish(true);
    }
}

/// <summary>Yapay Zekâ ayarları penceresi.</summary>
public partial class AiSettingsWindow : DialogWindow
{
    private readonly AiSettingsViewModel? _vm;

    public AiSettingsWindow() => InitializeComponent();

    public AiSettingsWindow(AiSettingsViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
    }

    private void OnKeyChanged(object? sender, TextChangedEventArgs e)
    {
        if (_vm is not null) _vm.NewApiKey = KeyBox.Text ?? "";
    }

    private void OnClearKey(object? sender, RoutedEventArgs e) => KeyBox.Clear();

    private void OnCopyToken(object? sender, RoutedEventArgs e) => Copy(_vm?.Token);

    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text }) Copy(text);
    }

    private void Copy(string? text)
    {
        if (string.IsNullOrEmpty(text) || Clipboard is not { } clipboard) return;
        try
        {
            UiWait.Run(() => clipboard.SetTextAsync(text));
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or TimeoutException)
        {
            // Pano başka bir uygulamada kilitliyse sessizce geç.
        }
    }

    private void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        string? folder = UiWait.Run(async () =>
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = L10n.T("Yapay zekânın okuyabileceği klasörü seçin"),
            }).ConfigureAwait(true);
            return picked.FirstOrDefault()?.TryGetLocalPath();
        });
        if (folder is not null) _vm.AddFolder(folder);
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        CommitFocusedInput();
        if (_vm?.Validate() is { } problem)
        {
            ErrorText.Text = problem;
            return;
        }
        Finish(true);
    }
}

/// <summary>Yapay Zekâ paneli (öneriler, uygulama içi asistan, bağlantı etkinliği).</summary>
public partial class AiPanel : UserControl
{
    public AiPanel() => InitializeComponent();
}
