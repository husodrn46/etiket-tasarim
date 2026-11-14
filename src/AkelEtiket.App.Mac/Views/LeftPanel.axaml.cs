using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using AkelEtiket.App.Mac.Services;
using AkelEtiket.Editor.Documents;
using AkelEtiket.Editor.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AkelEtiket.App.Mac.Views;

/// <summary>Sol panel: öğe ekleme, şablonlar, veri kaynağı ve katmanlar.</summary>
public partial class LeftPanel : UserControl
{
    private MainViewModel? _vm;
    private DocumentSession? _session;
    private readonly ObservableCollection<ElementViewModel> _layers = [];
    private bool _syncingSelection;

    public LeftPanel()
    {
        InitializeComponent();
        LayerList.ItemsSource = _layers;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnMainPropertyChanged;
        _vm = DataContext as MainViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnMainPropertyChanged;
            AttachSession(_vm.Session);
        }
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Session) && _vm is not null) AttachSession(_vm.Session);
    }

    private void AttachSession(DocumentSession session)
    {
        if (_session is not null)
        {
            _session.Elements.CollectionChanged -= OnElementsChanged;
            _session.SelectionChanged -= OnSessionSelectionChanged;
        }
        _session = session;
        session.Elements.CollectionChanged += OnElementsChanged;
        session.SelectionChanged += OnSessionSelectionChanged;
        RebuildLayers();
    }

    private void OnElementsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildLayers();

    /// <summary>Katman listesi: en üstteki (en son çizilen) öğe başta.</summary>
    private void RebuildLayers()
    {
        if (_session is null) return;
        _syncingSelection = true;
        try
        {
            var desired = _session.Elements.Reverse().ToList();
            for (int i = _layers.Count - 1; i >= 0; i--)
                if (!desired.Contains(_layers[i])) _layers.RemoveAt(i);
            for (int i = 0; i < desired.Count; i++)
            {
                int current = _layers.IndexOf(desired[i]);
                if (current < 0) _layers.Insert(i, desired[i]);
                else if (current != i) _layers.Move(current, i);
            }
        }
        finally
        {
            _syncingSelection = false;
        }
        OnSessionSelectionChanged(this, EventArgs.Empty);
    }

    private void OnSessionSelectionChanged(object? sender, EventArgs e)
    {
        if (_session is null || LayerList.SelectedItems is not { } selected) return;
        _syncingSelection = true;
        try
        {
            selected.Clear();
            foreach (var vm in _session.SelectedElements) selected.Add(vm);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void OnLayerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || _session is null || LayerList.SelectedItems is not { } selected) return;
        _session.SelectMany(selected.Cast<ElementViewModel>().ToList());
    }

    private void OnLayerDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (LayerList.SelectedItem is ElementViewModel vm) _vm?.RenameElementCommand.Execute(vm);
    }

    private void OnTemplateDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm?.Templates.SelectedItem is { } item) _vm.Templates.UseCommand.Execute(item);
    }

    private void OnOpenDataTable(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.Data.ValidateAgainst(_vm.Session.Document);
        DialogHost.Show(_vm.Data);
    }
}
