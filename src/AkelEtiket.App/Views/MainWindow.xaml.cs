using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using AkelEtiket.App.Services;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.App.Views;

/// <summary>Ana pencere: kısayollar, yakınlaştırma/sığdırma, pencere konumu, otomatik kurtarma zamanlayıcısı.</summary>
public partial class MainWindow : ThemedWindow
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _recoveryTimer;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RestorePlacement();

        vm.FitRequested += (_, _) =>
        {
            var owner=vm.ActiveDocument;
            Dispatcher.BeginInvoke(()=>{ if(vm.ActiveDocument==owner) FitToWindow(); },DispatcherPriority.Loaded);
        };
        vm.ThemeChanged += (_, _) => ApplyTheme();
        vm.FocusContentEditorRequested += (_, _) => Properties.FocusContentEditor();
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.DocumentChanging += OnDocumentChanging;
        vm.DocumentChanged += OnDocumentChanged;
        Loaded += (_, _) =>
        {
            ApplyTheme();
            UpdateRulerVisibility();
            FitToWindow();
            Surface.Focus();
        };

        _recoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(vm.Settings.AutoRecoverySeconds, 10, 600)) };
        _recoveryTimer.Tick += (_, _) => vm.AutoRecoveryTick();
        _recoveryTimer.Start();
    }

    private void OnDocumentChanging(object? sender, DocumentTabChangeEventArgs e)
    {
        CommitFocusedEditor();
        Surface.CancelDrag();
        if(e.Previous is { } previous)
        {
            previous.HorizontalOffset=CanvasScroll.HorizontalOffset;
            previous.VerticalOffset=CanvasScroll.VerticalOffset;
            previous.HasViewPosition=true;
        }
    }

    private void OnDocumentChanged(object? sender, DocumentTabChangeEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if(_vm.ActiveDocument != e.Current) return;
            CanvasScroll.UpdateLayout();
            if(e.Current.HasViewPosition)
            {
                CanvasScroll.ScrollToHorizontalOffset(e.Current.HorizontalOffset);
                CanvasScroll.ScrollToVerticalOffset(e.Current.VerticalOffset);
            }
            else FitToWindow();
            DocumentTabs.ScrollIntoView(e.Current);
            Motion.Pulse(WorkspaceHost);
        },DispatcherPriority.Loaded);
    }

    private void OnDocumentMenuOpened(object sender, RoutedEventArgs e)
    {
        if(sender is not ContextMenu menu || menu.PlacementTarget is not FrameworkElement { DataContext: DocumentTabViewModel tab }) return;
        menu.Items.Clear();
        menu.Items.Add(new MenuItem { Header=tab.IsPinned?L10n.T("Sabitlemeyi kaldır"):L10n.T("Sekmeyi sabitle"), Command=_vm.PinDocumentCommand, CommandParameter=tab });
        menu.Items.Add(new MenuItem { Header=L10n.T("Diğer sekmeleri kapat"), Command=_vm.CloseOtherDocumentsCommand, CommandParameter=tab });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header=L10n.T("Sekmeyi kapat"),Command=_vm.CloseDocumentCommand,CommandParameter=tab,InputGestureText="Ctrl+W" });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if(e.PropertyName == nameof(MainViewModel.StatusMessage)) Motion.Pulse(StatusText);
        if(e.PropertyName == nameof(MainViewModel.IsFocusMode))
        {
            bool focus=_vm.IsFocusMode;
            MainGrid.ColumnDefinitions[0].Width=new GridLength(focus?0:76);
            MainGrid.ColumnDefinitions[3].Width=new GridLength(focus?0:1);
            MainGrid.ColumnDefinitions[4].Width=new GridLength(focus?0:310);
            NavigationRail.Visibility=focus?Visibility.Collapsed:Visibility.Visible;
            Motion.Pulse(WorkspaceHost);
        }
        if (e.PropertyName == nameof(MainViewModel.Settings))
        {
            UpdateRulerVisibility();
            Surface.InvalidateVisual();
            _recoveryTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_vm.Settings.AutoRecoverySeconds, 10, 600));
        }
    }

    private void ApplyTheme()
    {
        ThemeManager.Configure(_vm.Settings);
        ThemeGlyph.Text = (string)FindResource(ThemeManager.Current == AppTheme.Dark ? "Glyph.Sun" : "Glyph.Moon");
        Surface.InvalidateVisual();
        TopRuler.InvalidateVisual();
        LeftRuler.InvalidateVisual();
    }

    private void UpdateRulerVisibility()
    {
        var visibility = _vm.Settings.ShowRulers ? Visibility.Visible : Visibility.Collapsed;
        TopRuler.Visibility = visibility;
        LeftRuler.Visibility = visibility;
        RulerCorner.Visibility = visibility;
    }

    /// <summary>Etiketi görünür alana sığdırır ve ortalar.</summary>
    private void FitToWindow()
    {
        if (CanvasScroll.ViewportWidth <= 0) return;
        _vm.Zoom = Surface.ComputeFitZoom(CanvasScroll.ViewportWidth, CanvasScroll.ViewportHeight);
        CanvasScroll.UpdateLayout();
        CanvasScroll.ScrollToHorizontalOffset((CanvasScroll.ExtentWidth - CanvasScroll.ViewportWidth) / 2);
        CanvasScroll.ScrollToVerticalOffset((CanvasScroll.ExtentHeight - CanvasScroll.ViewportHeight) / 2);
    }

    private void OnCanvasScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        TopRuler.InvalidateVisual();
        LeftRuler.InvalidateVisual();
    }

    private void OnRailClick(object sender, RoutedEventArgs e)
    {
        _vm.IsPanelOpen = true;
        if (_vm.ActivePanel == global::AkelEtiket.Editor.ViewModels.LeftPanel.Templates) _vm.Templates.Refresh();
    }

    private void OnToggleGrid(object sender, RoutedEventArgs e)
    {
        _vm.ToggleGridCommand.Execute(null);
        Surface.InvalidateVisual();
    }

    private void OnToggleSnap(object sender, RoutedEventArgs e) => _vm.ToggleSnapCommand.Execute(null);

    private void OnRecentClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = RecentButton, Placement = PlacementMode.Bottom };
        if (_vm.RecentFiles.Count == 0) menu.Items.Add(new MenuItem { Header = L10n.T("Son kullanılan dosya yok"), IsEnabled = false });
        foreach (var path in _vm.RecentFiles)
        {
            menu.Items.Add(new MenuItem
            {
                Header = Path.GetFileNameWithoutExtension(path),
                ToolTip = path,
                Command = _vm.OpenRecentCommand,
                CommandParameter = path,
            });
        }
        menu.IsOpen = true;
    }

    private void OnUndoHistoryClick(object sender, RoutedEventArgs e)
    {
        if (Surface.IsTextEditing) Surface.CommitTextEdit();
        var menu = new ContextMenu { PlacementTarget = UndoHistoryButton, Placement = PlacementMode.Bottom };
        var history = _vm.GetUndoHistory();
        if (history.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = L10n.T("Geri alınacak değişiklik yok"), IsEnabled = false });
        }
        else
        {
            menu.Items.Add(new MenuItem { Header = L10n.T("Seçilen değişikliğe kadar geri al (en yeni üstte):"), IsEnabled = false });
            menu.Items.Add(new Separator());
        }
        foreach (var item in history)
        {
            menu.Items.Add(new MenuItem
            {
                Header = new TextBlock { Text = item.Description },
                InputGestureText = item.Time.ToString("HH:mm:ss"),
                ToolTip = item.Steps == 1 ? L10n.T("Bu değişikliği geri al") : L10n.T($"Buraya kadar geri al ({item.Steps} adım; Ctrl+Y ile ileri alınır)"),
                Command = _vm.UndoToCommand,
                CommandParameter = item,
            });
        }
        menu.IsOpen = true;
    }

    private void OnMoreFileClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = MoreFileButton, Placement = PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header=L10n.T("Tüm etiketleri kaydet"),Command=_vm.SaveAllCommand,InputGestureText="Ctrl+Alt+S" });
        menu.Items.Add(new MenuItem { Header=L10n.T("Etiketi yeni sekmede kopyala"),Command=_vm.DuplicateDocumentCommand,InputGestureText="Ctrl+Shift+D" });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = L10n.T("Farklı kaydet…"), Command = _vm.SaveDocumentAsCommand, InputGestureText = "Ctrl+Shift+S" });
        menu.Items.Add(new MenuItem { Header = L10n.T("Şablon olarak kaydet…"), Command = _vm.SaveAsTemplateCommand });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = L10n.T("PNG olarak dışa aktar…"), Command = _vm.ExportPngCommand, InputGestureText = "Ctrl+E" });
        menu.Items.Add(new MenuItem { Header = L10n.T("PDF olarak kaydet…"), Command = _vm.ExportPdfCommand });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = L10n.T("Üretim çalışma alanı…"), Command = _vm.OpenProductionCommand });
        menu.Items.Add(new MenuItem { Header = L10n.T("Baskı geçmişi…"), Command = _vm.OpenPrintHistoryCommand });
        menu.Items.Add(new MenuItem { Header = L10n.T("Sayaçlar…"), Command = _vm.OpenCountersCommand });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = L10n.T("Excel / CSV veri kaynağı seç…"), Command = _vm.LoadDataCommand });
        menu.Items.Add(new MenuItem { Header = L10n.T("Veri sütunlarını eşleştir…"), Command = _vm.OpenColumnMappingCommand });
        menu.IsOpen = true;
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = HelpButton, Placement = PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = L10n.T("İlk kurulum…"), Command = _vm.OpenFirstSetupCommand });
        menu.Items.Add(new MenuItem { Header = L10n.T("Yardım ve kısayollar"), Command = _vm.OpenHelpCommand, InputGestureText = "F1" });
        menu.Items.Add(new MenuItem { Header = L10n.T("Firma, şablon ve ürün yedeği oluştur…"), Command = _vm.ExportWorkspaceBackupCommand });
        menu.Items.Add(new MenuItem { Header = L10n.T("Firma yedeğini geri yükle…"), Command = _vm.ImportWorkspaceBackupCommand });
        menu.Items.Add(new MenuItem { Header = L10n.T("Bu tasarımın yedekleri…"), Command = _vm.OpenBackupsCommand });
        menu.Items.Add(new MenuItem { Header = L10n.T("Ürün listesi…"), Command = _vm.OpenProductsCommand });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = L10n.T("Destek paketi oluştur…"), Command = _vm.CreateSupportBundleCommand });
        menu.IsOpen = true;
    }

    // ───────────────────────── Klavye kısayolları ─────────────────────────

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool textInput = Keyboard.FocusedElement is TextBox or PasswordBox || (Keyboard.FocusedElement is ComboBox { IsEditable: true });

        // Tuvalde yazı düzenlenirken Ctrl+Z/Y düzenleme kutusunun kendi geri al/ileri al'ıdır.
        if (ctrl && Surface.IsTextEditing && key is Key.Z or Key.Y) return;

        // Her yerde geçerli kısayollar
        if (ctrl)
        {
            ICommand? global = key switch
            {
                Key.N => _vm.NewCommand,
                Key.O => _vm.OpenCommand,
                Key.Tab when shift => _vm.PreviousDocumentCommand,
                Key.Tab => _vm.NextDocumentCommand,
                Key.W => _vm.CloseDocumentCommand,
                Key.S when Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) => _vm.SaveAllCommand,
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
                if (global.CanExecute(null)) global.Execute(null);
                e.Handled = true;
                return;
            }
        }
        if (key == Key.F5)
        {
            if (textInput) CommitFocusedEditor();
            _vm.PreviewCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (key == Key.F1)
        {
            _vm.OpenHelpCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (textInput) return;

        // Tuval kısayolları (metin kutusu dışında)
        ICommand? command = null;
        object? parameter = null;
        if (ctrl)
        {
            command = key switch
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
                // Türkçe Q klavyede [ ] AltGr ile yazıldığı için Ctrl+PageUp/PageDown da desteklenir.
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
                    command = _vm.DeleteCommand;
                    break;
                case Key.F2:
                    command = _vm.RenameElementCommand;
                    parameter = _vm.SelectedElement;
                    break;
                case Key.Enter:
                    // Seçili metni tuvalde düzenle (yalnız tuval odaktayken; düğmelerdeki Enter bozulmasın).
                    if (Surface.IsKeyboardFocused && _vm.RequestInlineTextEdit(_vm.SelectedElement))
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
        if (command is not null)
        {
            if (command.CanExecute(parameter)) command.Execute(parameter);
            e.Handled = true;
            return;
        }

        if (key is Key.Left or Key.Right or Key.Up or Key.Down && _vm.HasSelection && !Surface.IsDragging)
        {
            // Ok: 0,5 mm • Shift: 5 mm • Ctrl: 1 yazıcı noktası
            double step = ctrl ? 1 / _vm.DotsPerMm : shift ? 5 : 0.5;
            double dx = key == Key.Left ? -step : key == Key.Right ? step : 0;
            double dy = key == Key.Up ? -step : key == Key.Down ? step : 0;
            _vm.Nudge(dx, dy);
            e.Handled = true;
        }
    }

    /// <summary>Kısayoldan önce odaktaki sayı/metin kutusunun değerini (ya da tuvaldeki yazı düzenlemesini) uygular.</summary>
    private void CommitFocusedEditor()
    {
        if (Surface.IsTextEditing)
        {
            Surface.CommitTextEdit();
            return;
        }
        if (Keyboard.FocusedElement is TextBox box)
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (Keyboard.FocusedElement is Controls.NumberBox number)
            number.GetBindingExpression(Controls.NumberBox.ValueProperty)?.UpdateSource();
    }

    // ───────────────────────── Dosya bırakma ─────────────────────────

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (e.Handled || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        _vm.HandleFileDrop(files, null, null);
        e.Handled = true;
    }

    // ───────────────────────── Kapanış / pencere konumu ─────────────────────────

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        CommitFocusedEditor();
        if (!_vm.ConfirmCloseAllDocuments())
        {
            e.Cancel = true;
            return;
        }
        SavePlacement();
        _recoveryTimer.Stop();
    }

    private void RestorePlacement()
    {
        var p = _vm.Settings.Window;
        if (p.Width >= MinWidth && p.Height >= MinHeight)
        {
            Width = p.Width;
            Height = p.Height;
        }
        if (p.Left is { } left && p.Top is { } top && double.IsFinite(left) && double.IsFinite(top) &&
            left >= SystemParameters.VirtualScreenLeft - 50 && top >= SystemParameters.VirtualScreenTop - 50 &&
            left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
            top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        if (p.Maximized) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        var p = _vm.Settings.Window;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!bounds.IsEmpty)
        {
            p.Left = bounds.Left;
            p.Top = bounds.Top;
            p.Width = bounds.Width;
            p.Height = bounds.Height;
        }
        p.Maximized = WindowState == WindowState.Maximized;
        _vm.SaveSettings();
    }
}
