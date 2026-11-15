using System.ComponentModel;
using AkelEtiket.Editor.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.App.Mac.Views;

// Veri ızgarası kullanan pencereler: sayaçlar, veri tablosu ve üretim çalışma alanı.

/// <summary>Kalıcı sayaçlar penceresi.</summary>
public partial class CountersWindow : DialogWindow
{
    public CountersWindow() => InitializeComponent();

    public CountersWindow(CountersViewModel vm) : this() => DataContext = vm;

    private void OnClose(object? sender, RoutedEventArgs e) => Finish(null);
}

/// <summary>Veri tablosu: arama/süzme, satır seçimi (satır numarasıyla da), satır başına adet, durum ve tüm sütunlar.</summary>
public partial class DataTableWindow : DialogWindow
{
    private readonly DataSourceViewModel? _vm;

    public DataTableWindow() => InitializeComponent();

    public DataTableWindow(DataSourceViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
        BuildColumns(vm);
        Grid.ItemsSource = vm.VisibleRows;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => vm.PropertyChanged -= OnViewModelPropertyChanged;
        // Odak tablodan çıkınca (arama, süzgeç, düğmeler) yarım kalan hücre/satır düzenlemesi kaydedilir.
        Grid.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (FocusManager?.GetFocusedElement() is Visual focused && Grid.IsVisualAncestorOf(focused)) return;
            CommitGridEdit();
        });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DataSourceViewModel.VisibleRows) || _vm is null) return;
        CommitGridEdit();
        Grid.ItemsSource = _vm.VisibleRows;
    }

    private void CommitGridEdit()
    {
        if (!Grid.CommitEdit(DataGridEditingUnit.Row, true)) Grid.CancelEdit(DataGridEditingUnit.Row);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _vm is null) return;
        // Enter pencereyi kapatmasın; gecikmeyi beklemeden arar.
        _vm.SearchText = SearchBox.Text ?? "";
        e.Handled = true;
    }

    private void OnRangeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _vm is null) return;
        _vm.RowRangeText = RangeBox.Text ?? "";
        _vm.SelectRowRangeCommand.Execute(null);
        e.Handled = true;
    }

    private void BuildColumns(DataSourceViewModel vm)
    {
        Grid.Columns.Clear();
        Grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = L10n.T("Bas"),
            Binding = new ReflectionBinding(nameof(DataRowViewModel.IsSelected)),
            Width = new DataGridLength(64),
        });
        Grid.Columns.Add(new DataGridTextColumn
        {
            Header = L10n.T("Satır"),
            Binding = new ReflectionBinding(nameof(DataRowViewModel.RowNumber)),
            IsReadOnly = true,
            Width = new DataGridLength(76),
        });
        Grid.Columns.Add(new DataGridTextColumn
        {
            Header = L10n.T("Adet"),
            Binding = new ReflectionBinding(nameof(DataRowViewModel.Copies)),
            Width = new DataGridLength(76),
        });
        Grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = L10n.T("Durum"),
            IsReadOnly = true,
            Width = new DataGridLength(92),
            CellTemplate = new FuncDataTemplate<DataRowViewModel>((_, _) =>
            {
                var text = Cell(FontWeight.SemiBold);
                text.Bind(TextBlock.TextProperty, new ReflectionBinding(nameof(DataRowViewModel.StatusText)));
                text.Bind(TextBlock.ForegroundProperty, new ReflectionBinding(nameof(DataRowViewModel.Status))
                {
                    Converter = (Avalonia.Data.Converters.IValueConverter)this.FindResource("SeverityToBrush")!,
                });
                text.Bind(ToolTip.TipProperty, new ReflectionBinding(nameof(DataRowViewModel.Problem)));
                return text;
            }),
        });
        for (int i = 0; i < vm.Columns.Count; i++)
        {
            Grid.Columns.Add(new DataGridTextColumn
            {
                Header = vm.Columns[i],
                Binding = new ReflectionBinding($"Values[{i}]"),
                IsReadOnly = true,
                MinWidth = 70,
                MaxWidth = 340,
            });
        }
        Grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = L10n.T("Sorun"),
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 200,
            CellTemplate = new FuncDataTemplate<DataRowViewModel>((_, _) =>
            {
                var text = Cell(FontWeight.Normal);
                text.TextTrimming = TextTrimming.CharacterEllipsis;
                text[!TextBlock.ForegroundProperty] = this.GetResourceObservable("Brush.Error").ToBinding();
                text.Bind(TextBlock.TextProperty, new ReflectionBinding(nameof(DataRowViewModel.Problem)));
                text.Bind(ToolTip.TipProperty, new ReflectionBinding(nameof(DataRowViewModel.Problem)));
                return text;
            }),
        });
    }

    private static TextBlock Cell(FontWeight weight) => new()
    {
        FontWeight = weight,
        Margin = new Thickness(12, 0),
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
    };

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        CommitGridEdit();
        Finish(null);
    }
}

/// <summary>Üretim çalışma alanı: iş tarifleri, kalite denetimi ve toplu PDF/PNG çıktısı.</summary>
public partial class ProductionWindow : DialogWindow
{
    public ProductionWindow() => InitializeComponent();

    public ProductionWindow(ProductionViewModel vm) : this()
    {
        DataContext = vm;
        // Bulgu satırları içeriğe göre yükselir (uzun iletiler kaydırılır).
        FindingsGrid.RowHeight = double.NaN;
        // Sayı kutularına sayı olmayan bir şey yazılmışsa çıktı başlatılmaz.
        void UpdateInputErrors() =>
            vm.HasInputErrors = DataValidationErrors.GetHasErrors(CopiesBox) || DataValidationErrors.GetHasErrors(StartNumberBox);
        CopiesBox.PropertyChanged += (_, e) => { if (e.Property == DataValidationErrors.HasErrorsProperty) UpdateInputErrors(); };
        StartNumberBox.PropertyChanged += (_, e) => { if (e.Property == DataValidationErrors.HasErrorsProperty) UpdateInputErrors(); };
        Closed += (_, _) => vm.Dispose();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Finish(null);
}
