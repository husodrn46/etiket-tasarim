using System.ComponentModel;
using System.Windows.Input;
using AkelEtiket.App.Mac.Controls;
using AkelEtiket.App.Mac.Services;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.App.Mac.Views;

/// <summary>Ana pencere: kısayollar, yakınlaştırma/sığdırma, pencere konumu, otomatik kurtarma zamanlayıcısı.</summary>
public partial class MainWindow : Window
{
    private const KeyModifiers Shift = KeyModifiers.Shift;

    private readonly MainViewModel _vm = null!;
    private readonly DispatcherTimer _recoveryTimer = new();
    private bool _closeConfirmed;

    /// <summary>Yalnızca tasarım zamanı önizleyicisi içindir.</summary>
    public MainWindow() => InitializeComponent();

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        RestorePlacement();

        vm.FitRequested += (_, _) =>
        {
            var owner = vm.ActiveDocument;
            Dispatcher.UIThread.Post(() => { if (vm.ActiveDocument == owner) FitToWindow(); }, DispatcherPriority.Loaded);
        };
        vm.ThemeChanged += (_, _) => ApplyTheme();
        vm.FocusContentEditorRequested += (_, _) => PropertiesHost.FocusContentEditor();
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.DocumentChanging += OnDocumentChanging;
        vm.DocumentChanged += OnDocumentChanged;
        Opened += (_, _) =>
        {
            ApplyTheme();
            UpdateRulerVisibility();
            UpdatePanels();
            // Sığdırma, pencerenin ilk yerleşimi bittikten sonra hesaplanır.
            Dispatcher.UIThread.Post(() =>
            {
                FitToWindow();
                Surface.Focus();
            }, DispatcherPriority.Loaded);
        };
        Closing += OnWindowClosing;

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        _recoveryTimer.Interval = RecoveryInterval();
        _recoveryTimer.Tick += (_, _) => vm.AutoRecoveryTick();
        _recoveryTimer.Start();
    }

    private TimeSpan RecoveryInterval() => TimeSpan.FromSeconds(Math.Clamp(_vm.Settings.AutoRecoverySeconds, 10, 600));

    private void OnDocumentChanging(object? sender, DocumentTabChangeEventArgs e)
    {
        CommitFocusedEditor();
        Surface.CancelDrag();
        if (e.Previous is { } previous)
        {
            previous.HorizontalOffset = CanvasScroll.Offset.X;
            previous.VerticalOffset = CanvasScroll.Offset.Y;
            previous.HasViewPosition = true;
        }
    }

    private void OnDocumentChanged(object? sender, DocumentTabChangeEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_vm.ActiveDocument != e.Current) return;
            CanvasScroll.UpdateLayout();
            if (e.Current.HasViewPosition) CanvasScroll.Offset = new Vector(e.Current.HorizontalOffset, e.Current.VerticalOffset);
            else FitToWindow();
            DocumentTabs.ScrollIntoView(e.Current);
        }, DispatcherPriority.Loaded);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsFocusMode) or nameof(MainViewModel.IsPanelOpen)) UpdatePanels();
        if (e.PropertyName == nameof(MainViewModel.Settings))
        {
            UpdateRulerVisibility();
            Surface.InvalidateVisual();
            _recoveryTimer.Interval = RecoveryInterval();
        }
    }

    /// <summary>Odak modu yan panelleri gizler; sol panel ayrıca kendi düğmesiyle kapanır.</summary>
    private void UpdatePanels()
    {
        bool focus = _vm.IsFocusMode;
        MainGrid.ColumnDefinitions[0].Width = new GridLength(focus ? 0 : 76);
        MainGrid.ColumnDefinitions[3].Width = new GridLength(focus ? 0 : 1);
        MainGrid.ColumnDefinitions[4].Width = new GridLength(focus ? 0 : 310);
        NavigationRail.IsVisible = !focus;
        LeftPanelHost.IsVisible = !focus && _vm.IsPanelOpen;
        PropertiesHost.IsVisible = !focus;
    }

    private void ApplyTheme()
    {
        ThemeManager.Configure(_vm.Settings);
        if (this.TryFindResource(ThemeManager.IsDark ? "Glyph.Sun" : "Glyph.Moon", out object? icon) && icon is Geometry geometry)
            ThemeIcon.Data = geometry;
        Surface.InvalidateVisual();
        TopRuler.InvalidateVisual();
        LeftRuler.InvalidateVisual();
    }

    private void UpdateRulerVisibility()
    {
        bool visible = _vm.Settings.ShowRulers;
        TopRuler.IsVisible = visible;
        LeftRuler.IsVisible = visible;
        RulerCorner.IsVisible = visible;
    }

    /// <summary>Etiketi görünür alana sığdırır ve ortalar.</summary>
    private void FitToWindow()
    {
        if (CanvasScroll.Viewport.Width <= 0) return;
        _vm.Zoom = Surface.ComputeFitZoom(CanvasScroll.Viewport.Width, CanvasScroll.Viewport.Height);
        CanvasScroll.UpdateLayout();
        CanvasScroll.Offset = new Vector(
            Math.Max(0, (CanvasScroll.Extent.Width - CanvasScroll.Viewport.Width) / 2),
            Math.Max(0, (CanvasScroll.Extent.Height - CanvasScroll.Viewport.Height) / 2));
    }

    private void OnCanvasScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        TopRuler.InvalidateVisual();
        LeftRuler.InvalidateVisual();
    }

    private void OnRailClick(object? sender, RoutedEventArgs e)
    {
        _vm.IsPanelOpen = true;
        if (_vm.ActivePanel == Editor.ViewModels.LeftPanel.Templates) _vm.Templates.Refresh();
    }

    private void OnToggleGrid(object? sender, RoutedEventArgs e)
    {
        _vm.ToggleGridCommand.Execute(null);
        Surface.InvalidateVisual();
    }

    // ───────────────────────── Belge sekmeleri ─────────────────────────

    private void OnCloseDocumentTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: DocumentTabViewModel tab } && _vm.CloseDocumentCommand.CanExecute(tab))
            _vm.CloseDocumentCommand.Execute(tab);
    }

    private void OnDocumentTabContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: DocumentTabViewModel tab } target) return;
        var menu = new ContextMenu();
        menu.Items.Add(ContextMenus.Item(tab.IsPinned ? "Sabitlemeyi kaldır" : "Sekmeyi sabitle", _vm.PinDocumentCommand, parameter: tab));
        menu.Items.Add(ContextMenus.Item("Diğer sekmeleri kapat", _vm.CloseOtherDocumentsCommand, parameter: tab));
        menu.Items.Add(new Separator());
        menu.Items.Add(ContextMenus.Item("Sekmeyi kapat", _vm.CloseDocumentCommand, PlatformKeys.Gesture(Key.W), parameter: tab));
        menu.Open(target);
        e.Handled = true;
    }

    // ───────────────────────── Açılır menüler ─────────────────────────

    private static void ShowMenu(Control anchor, IEnumerable<Control> items)
    {
        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var item in items) flyout.Items.Add(item);
        flyout.ShowAt(anchor);
    }

    private static MenuItem Disabled(string header) => new() { Header = L10n.T(header), IsEnabled = false };

    private void OnRecentClick(object? sender, RoutedEventArgs e)
    {
        var items = new List<Control>();
        if (_vm.RecentFiles.Count == 0) items.Add(Disabled("Son kullanılan dosya yok"));
        foreach (string path in _vm.RecentFiles)
        {
            var item = new MenuItem { Header = Path.GetFileNameWithoutExtension(path), Command = _vm.OpenRecentCommand, CommandParameter = path };
            ToolTip.SetTip(item, path);
            items.Add(item);
        }
        ShowMenu(RecentButton, items);
    }

    private void OnUndoHistoryClick(object? sender, RoutedEventArgs e)
    {
        if (Surface.IsTextEditing) Surface.CommitTextEdit();
        var items = new List<Control>();
        var history = _vm.GetUndoHistory();
        if (history.Count == 0)
        {
            items.Add(Disabled("Geri alınacak değişiklik yok"));
        }
        else
        {
            items.Add(Disabled("Seçilen değişikliğe kadar geri al (en yeni üstte):"));
            items.Add(new Separator());
        }
        foreach (var entry in history)
        {
            var item = new MenuItem
            {
                Header = $"{entry.Description}   {entry.Time:HH:mm:ss}",
                Command = _vm.UndoToCommand,
                CommandParameter = entry,
            };
            ToolTip.SetTip(item, entry.Steps == 1 ? L10n.T("Bu değişikliği geri al") : L10n.T($"Buraya kadar geri al ({entry.Steps} adım; Ctrl+Y ile ileri alınır)"));
            items.Add(item);
        }
        ShowMenu(UndoHistoryButton, items);
    }

    private void OnMoreFileClick(object? sender, RoutedEventArgs e) => ShowMenu(MoreFileButton,
    [
        ContextMenus.Item("Tüm etiketleri kaydet", _vm.SaveAllCommand, PlatformKeys.Gesture(Key.S, KeyModifiers.Alt)),
        ContextMenus.Item("Etiketi yeni sekmede kopyala", _vm.DuplicateDocumentCommand, PlatformKeys.Gesture(Key.D, Shift)),
        new Separator(),
        ContextMenus.Item("Farklı kaydet…", _vm.SaveDocumentAsCommand, PlatformKeys.Gesture(Key.S, Shift)),
        ContextMenus.Item("Şablon olarak kaydet…", _vm.SaveAsTemplateCommand),
        new Separator(),
        ContextMenus.Item("BarTender etiketi aç…", _vm.OpenBarTenderFileCommand),
        ContextMenus.Item("BarTender klasörünü dönüştür…", _vm.ConvertBarTenderFolderCommand),
        new Separator(),
        ContextMenus.Item("PNG olarak dışa aktar…", _vm.ExportPngCommand, PlatformKeys.Gesture(Key.E)),
        ContextMenus.Item("PDF olarak kaydet…", _vm.ExportPdfCommand),
        new Separator(),
        ContextMenus.Item("Üretim çalışma alanı…", _vm.OpenProductionCommand),
        ContextMenus.Item("Baskı geçmişi…", _vm.OpenPrintHistoryCommand),
        ContextMenus.Item("Sayaçlar…", _vm.OpenCountersCommand),
        new Separator(),
        ContextMenus.Item("Excel / CSV veri kaynağı seç…", _vm.LoadDataCommand),
        ContextMenus.Item("Veri sütunlarını eşleştir…", _vm.OpenColumnMappingCommand),
    ]);

    private void OnHelpClick(object? sender, RoutedEventArgs e) => ShowMenu(HelpButton,
    [
        ContextMenus.Item("İlk kurulum…", _vm.OpenFirstSetupCommand),
        ContextMenus.Item("Yardım ve kısayollar", _vm.OpenHelpCommand, new KeyGesture(Key.F1)),
        ContextMenus.Item("Firma, şablon ve ürün yedeği oluştur…", _vm.ExportWorkspaceBackupCommand),
        ContextMenus.Item("Firma yedeğini geri yükle…", _vm.ImportWorkspaceBackupCommand),
        ContextMenus.Item("Bu tasarımın yedekleri…", _vm.OpenBackupsCommand),
        ContextMenus.Item("Ürün listesi…", _vm.OpenProductsCommand),
        new Separator(),
        ContextMenus.Item("Destek paketi oluştur…", _vm.CreateSupportBundleCommand),
    ]);

    // ───────────────────────── Klavye kısayolları ─────────────────────────

    /// <summary>Odak bir metin girişinde mi? (Tuval kısayolları orada çalışmaz; yazı yazılıyordur.)</summary>
    private bool IsTextInputFocused() =>
        FocusManager?.GetFocusedElement() is TextBox or AutoCompleteBox or ComboBox { IsEditable: true };

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        var key = e.Key;
        // Komut tuşu: macOS'ta ⌘, diğer sistemlerde Ctrl. Sekme geçişi her yerde Ctrl+Tab'dır (⌘+Tab sistemindir).
        bool command = e.KeyModifiers.HasFlag(PlatformKeys.Command);
        bool shift = e.KeyModifiers.HasFlag(Shift);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        bool textInput = IsTextInputFocused();

        // Tuvalde yazı düzenlenirken geri al/ileri al düzenleme kutusunun kendisine aittir.
        if (command && Surface.IsTextEditing && key is Key.Z or Key.Y) return;

        if (key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (textInput) CommitFocusedEditor();
            Run(shift ? _vm.PreviousDocumentCommand : _vm.NextDocumentCommand);
            e.Handled = true;
            return;
        }

        // Her yerde geçerli kısayollar
        if (command)
        {
            ICommand? global = key switch
            {
                Key.N => _vm.NewCommand,
                Key.O => _vm.OpenCommand,
                Key.W => _vm.CloseDocumentCommand,
                Key.S when alt => _vm.SaveAllCommand,
                Key.D when shift => _vm.DuplicateDocumentCommand,
                Key.F when shift => _vm.ToggleFocusModeCommand,
                Key.S when shift => _vm.SaveDocumentAsCommand,
                Key.S => _vm.SaveDocumentCommand,
                Key.B when shift => _vm.OpenProductionCommand,
                Key.P when shift => _vm.OpenQuickPrintCommand,
                Key.P => _vm.PrintCommand,
                Key.E => _vm.ExportPngCommand,
                Key.Z when shift => _vm.RedoCommand,
                Key.Z => _vm.UndoCommand,
                Key.Y => _vm.RedoCommand,
                Key.D0 or Key.NumPad0 => _vm.ZoomFitCommand,
                Key.D1 or Key.NumPad1 => _vm.ZoomActualSizeCommand,
                Key.OemPlus or Key.Add => _vm.ZoomInCommand,
                Key.OemMinus or Key.Subtract => _vm.ZoomOutCommand,
                _ => null,
            };
            if (global is not null)
            {
                // Yakınlaştırma tuvaldeki yazı düzenlemesini kapatmaz (düzenleme kutusu yakınlaştırmayı izler).
                bool zoom = global == _vm.ZoomFitCommand || global == _vm.ZoomActualSizeCommand || global == _vm.ZoomInCommand || global == _vm.ZoomOutCommand;
                if (textInput && !(zoom && Surface.IsTextEditing)) CommitFocusedEditor();
                Run(global);
                e.Handled = true;
                return;
            }
        }
        if (key == Key.F5)
        {
            if (textInput) CommitFocusedEditor();
            Run(_vm.PreviewCommand);
            e.Handled = true;
            return;
        }
        if (key == Key.F1)
        {
            Run(_vm.OpenHelpCommand);
            e.Handled = true;
            return;
        }
        if (textInput) return;

        // Tuval kısayolları (metin kutusu dışında)
        ICommand? canvas = null;
        object? parameter = null;
        if (command)
        {
            canvas = key switch
            {
                Key.C when shift => _vm.CopyStyleCommand,
                Key.V when shift => _vm.PasteStyleCommand,
                Key.C => _vm.CopyCommand,
                Key.X => _vm.CutCommand,
                Key.V => _vm.PasteCommand,
                Key.D => _vm.DuplicateCommand,
                Key.A => _vm.SelectAllCommand,
                Key.L => _vm.ToggleLockCommand,
                Key.G when shift => _vm.UngroupSelectionCommand,
                Key.G => _vm.GroupSelectionCommand,
                // Türkçe klavyede [ ] ek tuşla yazıldığı için PageUp/PageDown da desteklenir.
                Key.OemCloseBrackets or Key.PageUp when shift => _vm.BringToFrontCommand,
                Key.OemCloseBrackets or Key.PageUp => _vm.BringForwardCommand,
                Key.OemOpenBrackets or Key.PageDown when shift => _vm.SendToBackCommand,
                Key.OemOpenBrackets or Key.PageDown => _vm.SendBackwardCommand,
                _ => null,
            };
        }
        else
        {
            switch (key)
            {
                case Key.Delete:
                case Key.Back:
                    canvas = _vm.DeleteCommand;
                    break;
                case Key.F2:
                    canvas = _vm.RenameElementCommand;
                    parameter = _vm.SelectedElement;
                    break;
                case Key.Enter:
                    // Seçili metni tuvalde düzenle (yalnız tuval odaktayken; düğmelerdeki Enter bozulmasın).
                    if (Surface.IsFocused && _vm.RequestInlineTextEdit(_vm.SelectedElement))
                    {
                        e.Handled = true;
                        return;
                    }
                    break;
                case Key.Escape:
                    if (Surface.IsDragging) Surface.CancelDrag();
                    else _vm.Session.ClearSelection();
                    e.Handled = true;
                    return;
            }
        }
        if (canvas is not null)
        {
            Run(canvas, parameter);
            e.Handled = true;
            return;
        }

        if (key is Key.Left or Key.Right or Key.Up or Key.Down && _vm.HasSelection && !Surface.IsDragging)
        {
            // Ok: 0,5 mm • Shift: 5 mm • Komut tuşu: 1 yazıcı noktası
            double step = command ? 1 / _vm.DotsPerMm : shift ? 5 : 0.5;
            double dx = key == Key.Left ? -step : key == Key.Right ? step : 0;
            double dy = key == Key.Up ? -step : key == Key.Down ? step : 0;
            _vm.Nudge(dx, dy);
            e.Handled = true;
        }
    }

    private static void Run(ICommand command, object? parameter = null)
    {
        if (command.CanExecute(parameter)) command.Execute(parameter);
    }

    /// <summary>Kısayoldan önce tuvaldeki yazı düzenlemesini (ya da odaktaki sayı kutusunun değerini) uygular.</summary>
    private void CommitFocusedEditor()
    {
        if (Surface.IsTextEditing)
        {
            Surface.CommitTextEdit();
            return;
        }
        // Değerini odak kaybında yazan kutular (sayı kutusu) için: odak tuvale alınır, kutu değerini uygular.
        if (IsTextInputFocused()) Surface.Focus();
    }

    // ───────────────────────── Dosya bırakma ─────────────────────────

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Handled) return;
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Handled) return;
        string[] files = DroppedFiles.LocalPaths(e);
        if (files.Length == 0) return;
        _vm.HandleFileDrop(files, null, null);
        e.Handled = true;
    }

    // ───────────────────────── Kapanış / pencere konumu ─────────────────────────

    /// <summary>
    /// Kapanış önce durdurulur; kaydedilmemiş belgeler için sorular pencere olayının dışında sorulur ve onaylanırsa
    /// pencere yeniden kapatılır. (Soru pencereleri iç içe ileti döngüsü çalıştırır; kapanış olayının içinde değil.)
    /// </summary>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed) return;
        e.Cancel = true;
        Dispatcher.UIThread.Post(() =>
        {
            CommitFocusedEditor();
            if (!_vm.ConfirmCloseAllDocuments()) return;
            SavePlacement();
            _recoveryTimer.Stop();
            _closeConfirmed = true;
            Close();
        });
    }

    private void RestorePlacement()
    {
        var p = _vm.Settings.Window;
        if (p.Width >= MinWidth && p.Height >= MinHeight)
        {
            Width = p.Width;
            Height = p.Height;
        }
        if (p.Left is { } left && p.Top is { } top && double.IsFinite(left) && double.IsFinite(top))
        {
            var position = new PixelPoint((int)left, (int)top);
            // Kayıtlı konum hâlâ bağlı bir ekranın içindeyse kullanılır (ekran çıkarılmış olabilir).
            if (Screens.All.Any(screen => screen.WorkingArea.Contains(position)))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = position;
            }
        }
        if (p.Maximized) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        var p = _vm.Settings.Window;
        if (WindowState == WindowState.Normal)
        {
            p.Left = Position.X;
            p.Top = Position.Y;
            p.Width = Width;
            p.Height = Height;
        }
        p.Maximized = WindowState == WindowState.Maximized;
        _vm.SaveSettings();
    }
}
