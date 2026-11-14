using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Documents;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AkelEtiket.App.Mac.Views;

/// <summary>Sağ panel: seçili öğenin özellikleri.</summary>
public partial class PropertiesPanel : UserControl
{
    public PropertiesPanel() => InitializeComponent();

    /// <summary>Metin/barkod içerik kutusuna odaklanır (tuvalde çift tıklama).</summary>
    public void FocusContentEditor() => Dispatcher.UIThread.Post(() =>
    {
        var box = SingleScroll.GetVisualDescendants().OfType<TextBox>()
            .FirstOrDefault(candidate => candidate.Tag as string == "ContentEditor" && candidate.IsEffectivelyVisible);
        if (box is null) return;
        box.Focus();
        box.SelectAll();
        box.BringIntoView();
    }, DispatcherPriority.Loaded);

    private void OnRotate90(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ElementViewModel vm && !vm.Model.Locked)
            vm.Model.Rotation = (vm.Model.Rotation + 90) % 360;
    }
}

/// <summary>Öğe türüne göre özellik şablonu seçer.</summary>
public sealed class ElementTemplateSelector : IDataTemplate
{
    public IDataTemplate? TextTemplate { get; set; }
    public IDataTemplate? BarcodeTemplate { get; set; }
    public IDataTemplate? ImageTemplate { get; set; }
    public IDataTemplate? ShapeTemplate { get; set; }

    private IDataTemplate? Select(object? item) => (item as ElementViewModel)?.Model switch
    {
        TextElement => TextTemplate,
        BarcodeElement => BarcodeTemplate,
        ImageElement => ImageTemplate,
        ShapeElement => ShapeTemplate,
        _ => null,
    };

    public bool Match(object? data) => Select(data) is not null;

    public Control? Build(object? param) => Select(param)?.Build(param);
}
