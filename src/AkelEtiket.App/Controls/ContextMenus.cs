using AkelEtiket.Core.Geometry;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;

namespace AkelEtiket.App.Controls;

/// <summary>Tuval sağ tık menüleri.</summary>
internal static class ContextMenus
{
    public static ContextMenu ForSelection(MainViewModel vm)
    {
        var menu = new ContextMenu();
        var session = vm.Session;
        bool locked = session.SelectedModels.Count > 0 && session.SelectedModels.All(m => m.Locked);
        menu.Items.Add(Item("Kes", vm.CutCommand, "Ctrl+X", "\uE8C6"));
        menu.Items.Add(Item("Kopyala", vm.CopyCommand, "Ctrl+C", "\uE8C8"));
        menu.Items.Add(Item("Yapıştır", vm.PasteCommand, "Ctrl+V", "\uE77F"));
        menu.Items.Add(Item("Çoğalt", vm.DuplicateCommand, "Ctrl+D", "\uE8C8"));
        menu.Items.Add(Item("Sil", vm.DeleteCommand, "Del", "\uE74D"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Biçimi kopyala", vm.CopyStyleCommand, "Ctrl+Shift+C", "\uE771"));
        menu.Items.Add(Item("Biçimi yapıştır", vm.PasteStyleCommand, "Ctrl+Shift+V", "\uE771"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(locked ? "Kilidi aç" : "Kilitle", vm.ToggleLockCommand, "Ctrl+L", locked ? "\uE785" : "\uE72E"));
        if (vm.Session.SelectedElements.Count > 1 && !vm.Session.SelectionHasGroup)
            menu.Items.Add(Item("Grupla", vm.GroupSelectionCommand, "Ctrl+G", "\uF168"));
        if (vm.Session.SelectionHasGroup)
            menu.Items.Add(Item("Grubu çöz", vm.UngroupSelectionCommand, "Ctrl+Shift+G", "\uF168"));
        if (vm.SelectedElement is { } single)
            menu.Items.Add(Item("Yeniden adlandır…", vm.RenameElementCommand, "F2", "\uE70F", single));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("En öne getir", vm.BringToFrontCommand, "Ctrl+Shift+PgUp", geometry: "Icon.BringToFront"));
        menu.Items.Add(Item("Öne getir", vm.BringForwardCommand, "Ctrl+PgUp", geometry: "Icon.BringForward"));
        menu.Items.Add(Item("Arkaya gönder", vm.SendBackwardCommand, "Ctrl+PgDn", geometry: "Icon.SendBackward"));
        menu.Items.Add(Item("En arkaya gönder", vm.SendToBackCommand, "Ctrl+Shift+PgDn", geometry: "Icon.SendToBack"));
        menu.Items.Add(new Separator());
        var align = new MenuItem { Header = vm.HasMultipleSelection ? "Hizala" : "Etikete hizala" };
        align.Items.Add(Item("Sola", vm.AlignCommand, null, geometry: "Icon.AlignLeft", parameter: AlignMode.Left));
        align.Items.Add(Item("Yatay ortala", vm.AlignCommand, null, geometry: "Icon.AlignCenterH", parameter: AlignMode.CenterHorizontal));
        align.Items.Add(Item("Sağa", vm.AlignCommand, null, geometry: "Icon.AlignRight", parameter: AlignMode.Right));
        align.Items.Add(Item("Üste", vm.AlignCommand, null, geometry: "Icon.AlignTop", parameter: AlignMode.Top));
        align.Items.Add(Item("Dikey ortala", vm.AlignCommand, null, geometry: "Icon.AlignCenterV", parameter: AlignMode.CenterVertical));
        align.Items.Add(Item("Alta", vm.AlignCommand, null, geometry: "Icon.AlignBottom", parameter: AlignMode.Bottom));
        if (vm.Session.SelectedElements.Count >= 3)
        {
            align.Items.Add(new Separator());
            align.Items.Add(Item("Yatay eşit dağıt", vm.AlignCommand, null, geometry: "Icon.DistributeH", parameter: AlignMode.DistributeHorizontal));
            align.Items.Add(Item("Dikey eşit dağıt", vm.AlignCommand, null, geometry: "Icon.DistributeV", parameter: AlignMode.DistributeVertical));
        }
        menu.Items.Add(align);
        if (vm.SelectedElement?.Model is BarcodeElement)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Okunabilirlik kontrolü", vm.VerifyBarcodeCommand, null, "\uE73E"));
        }
        return menu;
    }

    public static ContextMenu ForCanvas(MainViewModel vm, (double X, double Y) position)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Yapıştır", vm.PasteCommand, "Ctrl+V", "\uE77F"));
        menu.Items.Add(Item("Tümünü seç", vm.SelectAllCommand, "Ctrl+A", null));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Metin ekle", vm.AddTextCommand, null, "\uE8D2"));
        menu.Items.Add(Item("Görsel ekle…", vm.AddImageCommand, null, "\uEB9F"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Etiket ölçüsü…", vm.LabelSetupCommand, null, "\uE8EC"));
        menu.Items.Add(Item("Pencereye sığdır", vm.ZoomFitCommand, "Ctrl+0", "\uE9A6"));
        return menu;
    }

    private static MenuItem Item(string header, ICommand command, string? gesture, string? glyph = null, object? parameter = null, string? geometry = null)
    {
        var item = new MenuItem
        {
            Header = header,
            Command = command,
            CommandParameter = parameter,
            InputGestureText = gesture ?? "",
        };
        if (glyph is not null)
        {
            item.Icon = new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)System.Windows.Application.Current.FindResource("IconFont"),
                FontSize = 14,
            };
        }
        else if (geometry is not null && System.Windows.Application.Current.TryFindResource(geometry) is Geometry data)
        {
            var path = new Path { Data = data, Width = 14, Height = 14, Stretch = Stretch.Uniform };
            path.SetResourceReference(Shape.FillProperty, "Brush.Text");
            item.Icon = path;
        }
        return item;
    }
}
