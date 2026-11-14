using System.Windows.Input;
using AkelEtiket.App.Mac.Services;
using AkelEtiket.Core.Geometry;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.App.Mac.Controls;

/// <summary>Tuval sağ tık menüleri.</summary>
internal static class ContextMenus
{
    private const KeyModifiers Shift = KeyModifiers.Shift;

    public static ContextMenu ForSelection(MainViewModel vm)
    {
        var menu = new ContextMenu();
        var session = vm.Session;
        bool locked = session.SelectedModels.Count > 0 && session.SelectedModels.All(m => m.Locked);
        menu.Items.Add(Item("Kes", vm.CutCommand, PlatformKeys.Gesture(Key.X), "Glyph.Cut"));
        menu.Items.Add(Item("Kopyala", vm.CopyCommand, PlatformKeys.Gesture(Key.C), "Glyph.Copy"));
        menu.Items.Add(Item("Yapıştır", vm.PasteCommand, PlatformKeys.Gesture(Key.V), "Glyph.Paste"));
        menu.Items.Add(Item("Çoğalt", vm.DuplicateCommand, PlatformKeys.Gesture(Key.D), "Glyph.Duplicate"));
        menu.Items.Add(Item("Sil", vm.DeleteCommand, new KeyGesture(Key.Delete), "Glyph.Delete"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Biçimi kopyala", vm.CopyStyleCommand, PlatformKeys.Gesture(Key.C, Shift)));
        menu.Items.Add(Item("Biçimi yapıştır", vm.PasteStyleCommand, PlatformKeys.Gesture(Key.V, Shift)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(locked ? "Kilidi aç" : "Kilitle", vm.ToggleLockCommand, PlatformKeys.Gesture(Key.L), locked ? "Glyph.Unlock" : "Glyph.Lock"));
        if (session.SelectedElements.Count > 1 && !session.SelectionHasGroup)
            menu.Items.Add(Item("Grupla", vm.GroupSelectionCommand, PlatformKeys.Gesture(Key.G)));
        if (session.SelectionHasGroup)
            menu.Items.Add(Item("Grubu çöz", vm.UngroupSelectionCommand, PlatformKeys.Gesture(Key.G, Shift)));
        if (vm.SelectedElement is { } single)
            menu.Items.Add(Item("Yeniden adlandır…", vm.RenameElementCommand, new KeyGesture(Key.F2), "Glyph.Edit", single));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("En öne getir", vm.BringToFrontCommand, PlatformKeys.Gesture(Key.PageUp, Shift), "Icon.BringToFront"));
        menu.Items.Add(Item("Öne getir", vm.BringForwardCommand, PlatformKeys.Gesture(Key.PageUp), "Icon.BringForward"));
        menu.Items.Add(Item("Arkaya gönder", vm.SendBackwardCommand, PlatformKeys.Gesture(Key.PageDown), "Icon.SendBackward"));
        menu.Items.Add(Item("En arkaya gönder", vm.SendToBackCommand, PlatformKeys.Gesture(Key.PageDown, Shift), "Icon.SendToBack"));
        menu.Items.Add(new Separator());
        var align = new MenuItem { Header = L10n.T(vm.HasMultipleSelection ? "Hizala" : "Etikete hizala") };
        align.Items.Add(Item("Sola", vm.AlignCommand, null, "Icon.AlignLeft", AlignMode.Left));
        align.Items.Add(Item("Yatay ortala", vm.AlignCommand, null, "Icon.AlignCenterH", AlignMode.CenterHorizontal));
        align.Items.Add(Item("Sağa", vm.AlignCommand, null, "Icon.AlignRight", AlignMode.Right));
        align.Items.Add(Item("Üste", vm.AlignCommand, null, "Icon.AlignTop", AlignMode.Top));
        align.Items.Add(Item("Dikey ortala", vm.AlignCommand, null, "Icon.AlignCenterV", AlignMode.CenterVertical));
        align.Items.Add(Item("Alta", vm.AlignCommand, null, "Icon.AlignBottom", AlignMode.Bottom));
        if (session.SelectedElements.Count >= 3)
        {
            align.Items.Add(new Separator());
            align.Items.Add(Item("Yatay eşit dağıt", vm.AlignCommand, null, "Icon.DistributeH", AlignMode.DistributeHorizontal));
            align.Items.Add(Item("Dikey eşit dağıt", vm.AlignCommand, null, "Icon.DistributeV", AlignMode.DistributeVertical));
        }
        menu.Items.Add(align);
        if (vm.SelectedElement?.Model is BarcodeElement)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Okunabilirlik kontrolü", vm.VerifyBarcodeCommand, null, "Glyph.Check"));
        }
        return menu;
    }

    public static ContextMenu ForCanvas(MainViewModel vm)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Yapıştır", vm.PasteCommand, PlatformKeys.Gesture(Key.V), "Glyph.Paste"));
        menu.Items.Add(Item("Tümünü seç", vm.SelectAllCommand, PlatformKeys.Gesture(Key.A)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Metin ekle", vm.AddTextCommand, null, "Glyph.Text"));
        menu.Items.Add(Item("Görsel ekle…", vm.AddImageCommand, null, "Glyph.Image"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Etiket ölçüsü…", vm.LabelSetupCommand, null, "Glyph.Label"));
        menu.Items.Add(Item("Pencereye sığdır", vm.ZoomFitCommand, PlatformKeys.Gesture(Key.D0), "Glyph.Fit"));
        return menu;
    }

    /// <summary>Menü öğesi; <paramref name="header"/> Türkçe kaynak metindir ve arayüz diline çevrilir.</summary>
    public static MenuItem Item(string header, ICommand? command, KeyGesture? gesture = null, string? icon = null, object? parameter = null)
    {
        var item = new MenuItem
        {
            Header = L10n.T(header),
            Command = command,
            CommandParameter = parameter,
            InputGesture = gesture,
        };
        if (icon is not null && Application.Current is { } app && app.TryFindResource(icon, out object? data) && data is Geometry geometry)
            item.Icon = new PathIcon { Data = geometry, Width = 14, Height = 14 };
        return item;
    }
}
