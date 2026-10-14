using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AkelEtiket.Editor.ViewModels;

namespace AkelEtiket.App.Views;

/// <summary>Veri tablosu: arama/süzme, satır seçimi (satır numarasıyla da), satır başına adet, durum ve tüm sütunlar.</summary>
public partial class DataTableWindow : ThemedWindow
{
    private readonly DataSourceViewModel _vm;

    public DataTableWindow(DataSourceViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        BuildColumns(vm);
        Grid.ItemsSource = vm.VisibleRows;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => vm.PropertyChanged -= OnViewModelPropertyChanged;
        // Odak tablodan çıkınca (arama, süzgeç, düğmeler) yarım kalan hücre/satır düzenlemesi kaydedilir.
        Grid.LostKeyboardFocus += (_, e) =>
        {
            if (e.NewFocus is Visual next && Grid.IsAncestorOf(next)) return;
            CommitGridEdit();
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DataSourceViewModel.VisibleRows)) return;
        CommitGridEdit();
        Grid.ItemsSource = _vm.VisibleRows;
    }

    private void CommitGridEdit()
    {
        if (!Grid.CommitEdit(DataGridEditingUnit.Row, true)) Grid.CancelEdit(DataGridEditingUnit.Row);
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        // Enter pencereyi kapatmasın; beklemeden arar.
        BindingOperations.GetBindingExpression(SearchBox, TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }

    private void OnRangeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        BindingOperations.GetBindingExpression(RangeBox, TextBox.TextProperty)?.UpdateSource();
        _vm.SelectRowRangeCommand.Execute(null);
        e.Handled = true;
    }

    private void BuildColumns(DataSourceViewModel vm)
    {
        Grid.Columns.Clear();
        Grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Bas",
            Binding = new Binding(nameof(DataRowViewModel.IsSelected)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = 44,
        });
        Grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Satır",
            Binding = new Binding(nameof(DataRowViewModel.RowNumber)),
            IsReadOnly = true,
            Width = 56,
        });
        Grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Adet",
            Binding = new Binding(nameof(DataRowViewModel.Copies)) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
            Width = 60,
        });
        var statusStyle = new Style(typeof(TextBlock));
        statusStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty,
            new Binding(nameof(DataRowViewModel.Status)) { Converter = (IValueConverter)FindResource("SeverityToBrush") }));
        statusStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        statusStyle.Setters.Add(new Setter(ToolTipProperty, new Binding(nameof(DataRowViewModel.Problem))));
        Grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Durum",
            Binding = new Binding(nameof(DataRowViewModel.StatusText)),
            IsReadOnly = true,
            Width = 70,
            ElementStyle = statusStyle,
        });
        for (int i = 0; i < vm.Columns.Count; i++)
        {
            Grid.Columns.Add(new DataGridTextColumn
            {
                Header = vm.Columns[i],
                Binding = new Binding($"Values[{i}]"),
                IsReadOnly = true,
                MinWidth = 70,
                MaxWidth = 340,
            });
        }
        var problemStyle = new Style(typeof(TextBlock));
        problemStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, FindResource("Brush.Error")));
        problemStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        problemStyle.Setters.Add(new Setter(ToolTipProperty, new Binding(nameof(DataRowViewModel.Problem))));
        Grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Sorun",
            Binding = new Binding(nameof(DataRowViewModel.Problem)),
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 200,
            ElementStyle = problemStyle,
        });
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        CommitGridEdit();
        Close();
    }
}
