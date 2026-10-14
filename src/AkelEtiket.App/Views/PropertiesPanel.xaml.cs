using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Documents;

namespace AkelEtiket.App.Views;

/// <summary>Sağ panel: seçili öğenin özellikleri.</summary>
public partial class PropertiesPanel : UserControl
{
    public PropertiesPanel() => InitializeComponent();

    /// <summary>Metin/barkod içerik kutusuna odaklanır (tuvalde çift tıklama).</summary>
    public void FocusContentEditor()
    {
        Dispatcher.BeginInvoke(() =>
        {
            var box = FindTagged(SingleScroll, "ContentEditor");
            if (box is null) return;
            box.Focus();
            box.SelectAll();
            box.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private static TextBox? FindTagged(DependencyObject root, string tag)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox { Tag: string t } box && t == tag && box.IsVisible) return box;
            var found = FindTagged(child, tag);
            if (found is not null) return found;
        }
        return null;
    }

    private void OnRotate90(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ElementViewModel vm && !vm.Model.Locked)
            vm.Model.Rotation = (vm.Model.Rotation + 90) % 360;
    }
}

/// <summary>Öğe türüne göre özellik şablonu seçer.</summary>
public sealed class ElementTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TextTemplate { get; set; }
    public DataTemplate? BarcodeTemplate { get; set; }
    public DataTemplate? ImageTemplate { get; set; }
    public DataTemplate? ShapeTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) => (item as ElementViewModel)?.Model switch
    {
        TextElement => TextTemplate,
        BarcodeElement => BarcodeTemplate,
        ImageElement => ImageTemplate,
        ShapeElement => ShapeTemplate,
        _ => null,
    };
}
