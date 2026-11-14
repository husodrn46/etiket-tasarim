using AkelEtiket.App.Mac.Controls;
using AkelEtiket.App.Mac.Views;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Documents;
using AkelEtiket.Editor.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace AkelEtiket.App.Mac.Tests;

/// <summary>
/// Sol panel ve özellikler paneli: her öğe türü ve her sol panel sekmesi gerçek pencerede açılır, bağlamaların
/// tamamı çözülmelidir; paneldeki düğme ve kutular gerçek fare/klavye girdisiyle modeli değiştirir.
/// </summary>
public class PanelTests
{
    private static T Find<T>(Control root, Func<T, bool> match) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => c.IsEffectivelyVisible && match(c));

    private static Point CenterOf(AppHarness app, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), app.Window)
        ?? throw new InvalidOperationException("Denetim pencerede değil.");

    private static PropertiesPanel Properties(AppHarness app) => app.Window.FindControl<PropertiesPanel>("PropertiesHost")!;

    private static Views.LeftPanel Left(AppHarness app) => app.Window.FindControl<Views.LeftPanel>("LeftPanelHost")!;

    [Fact]
    public void PropertiesPanel_ResolvesEveryBinding_ForEachElementKind() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        // Örnek etikette görsel yok: bir PNG bırakılarak eklenir.
        app.Vm.HandleFileDrop([AppHarness.SamplePath("Ornek-58x40-Raf-Etiketi.png")], 20, 20);
        app.WaitForLayers();
        BindingErrors.Take();

        var kinds = new List<string>();
        foreach (var element in app.Vm.Session.Elements.GroupBy(e => e.Model.GetType()).Select(g => g.First()))
        {
            app.Vm.Session.Select(element);
            Ui.Pump();
            app.WaitForLayers();
            Assert.Same(element, app.Vm.SelectedElement);
            app.Screenshot("properties-" + element.Model.GetType().Name);
            kinds.Add(element.Model.GetType().Name);
            Assert.Empty(BindingErrors.Take());
        }
        Assert.Equal(["BarcodeElement", "ImageElement", "ShapeElement", "TextElement"], kinds.Order());

        // Seçim yok ve çoklu seçim görünümleri
        app.Vm.Session.ClearSelection();
        Ui.Pump();
        Assert.Empty(BindingErrors.Take());
        app.Vm.SelectAllCommand.Execute(null);
        Ui.Pump();
        app.Screenshot("properties-multiple");
        Assert.Empty(BindingErrors.Take());
    });

    [Fact]
    public void LeftPanel_ResolvesEveryBinding_OnEachTab() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        BindingErrors.Take();
        foreach (var panel in Enum.GetValues<Editor.ViewModels.LeftPanel>())
        {
            app.Vm.ActivePanel = panel;
            if (panel == Editor.ViewModels.LeftPanel.Templates) app.Vm.Templates.Refresh();
            Ui.Pump();
            Ui.Pump();
            app.Screenshot("left-" + panel);
            Assert.Empty(BindingErrors.Take());
        }
    });

    [Fact]
    public void LeftPanel_CardsAddElements() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        int count = app.Vm.Session.Elements.Count;

        app.Vm.ActivePanel = Editor.ViewModels.LeftPanel.Text;
        Ui.Pump();
        app.Click(CenterOf(app, Find<Button>(Left(app), b => Equals(b.CommandParameter, "Baslik"))));
        Assert.Equal(count + 1, app.Vm.Session.Elements.Count);
        Assert.IsType<TextElement>(app.Vm.Session.Elements[^1].Model);

        app.Vm.ActivePanel = Editor.ViewModels.LeftPanel.Shapes;
        Ui.Pump();
        app.Click(CenterOf(app, Find<Button>(Left(app), b => Equals(b.CommandParameter, "Circle"))));
        Assert.Equal(count + 2, app.Vm.Session.Elements.Count);
        Assert.IsType<ShapeElement>(app.Vm.Session.Elements[^1].Model);

        app.Vm.ActivePanel = Editor.ViewModels.LeftPanel.Barcode;
        Ui.Pump();
        app.Click(CenterOf(app, Find<Button>(Left(app), b => b.Classes.Contains("card"))));
        Assert.Equal(count + 3, app.Vm.Session.Elements.Count);
        Assert.IsType<BarcodeElement>(app.Vm.Session.Elements[^1].Model);
        Assert.Empty(BindingErrors.Take());
    });

    [Fact]
    public void LayersTab_MirrorsSelection_AndTogglesVisibility() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        app.Vm.ActivePanel = Editor.ViewModels.LeftPanel.Layers;
        Ui.Pump();
        var list = Left(app).FindControl<ListBox>("LayerList")!;
        Assert.Equal(app.Vm.Session.Elements.Count, list.ItemCount);

        // Tuvalde seçilen öğe listede de seçilir.
        var title = app.Vm.Session.Elements.First(e => e.Model is TextElement t && t.Text.Contains("KOLİ"));
        app.Vm.Session.Select(title);
        Ui.Pump();
        Assert.Same(title, list.SelectedItem);

        // Listede en üstteki satır en üstteki (son çizilen) öğedir; göz düğmesi görünürlüğü değiştirir.
        var top = app.Vm.Session.Elements[^1];
        var row = list.ContainerFromIndex(0)!;
        Assert.Same(top, row.DataContext);
        var eye = row.GetVisualDescendants().OfType<Button>().First();
        Assert.True(top.Model.Visible);
        app.Click(CenterOf(app, eye));
        Assert.False(top.Model.Visible);
    });

    [Fact]
    public void NumberBox_AppliesTypedValue_OnEnter_AndRejectsGarbage() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        var title = app.Vm.Session.Elements.First(e => e.Model is TextElement t && t.Text.Contains("KOLİ"));
        app.Vm.Session.Select(title);
        Ui.Pump();
        // Konum bölümündeki ilk sayı kutusu X'tir.
        var x = Find<NumberBox>(Properties(app), _ => true);
        Assert.Equal(title.Model.X, x.Value, 3);

        app.Click(CenterOf(app, x));
        Assert.True(x.IsFocused);
        // Odak alınca kutunun tamamı seçilir: yazılan değer eskisinin yerine geçer. Ondalık virgül kabul edilir.
        Assert.Equal(x.Text!.Length, x.SelectionEnd - x.SelectionStart);
        app.Window.KeyTextInput("12,5");
        Assert.Equal("12,5", x.Text);
        Assert.NotEqual(12.5, title.Model.X); // yazarken model değişmez
        app.Key(Key.Enter);
        Assert.Equal(12.5, title.Model.X, 3);

        app.Window.KeyTextInput("abc");
        app.Key(Key.Enter);
        Assert.Equal(12.5, title.Model.X, 3);
        Assert.Equal(x.Value, NumberBox.TryParse(x.Text, out double shown) ? shown : double.NaN, 3);

        // Ok tuşu bir adım (0,5 mm) artırır.
        app.Key(Key.Up);
        Assert.Equal(13.0, title.Model.X, 3);
    });

    [Fact]
    public void ContentEditor_ChangesText_AndCanvasDoubleClickFocusesIt() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        var barcode = app.Vm.Session.Elements.First(e => e.Model is BarcodeElement);
        var p = app.At(barcode.Model.X + barcode.Model.Width / 2, barcode.Model.Y + barcode.Model.Height / 2);

        // Barkoda çift tıklamak içerik kutusuna odaklanır (barkod tuvalde düzenlenmez).
        app.Window.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseUp(p, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseUp(p, MouseButton.Left, RawInputModifiers.None);
        Ui.WaitFor(() => app.Window.FocusManager?.GetFocusedElement() is TextBox { Tag: "ContentEditor" }, "içerik kutusu odaklandı");

        var box = (TextBox)app.Window.FocusManager!.GetFocusedElement()!;
        Assert.Equal(((BarcodeElement)barcode.Model).Content, box.Text);
        app.Window.KeyTextInput("(01)08691234567890");
        // İçerik modele kısa bir gecikmeyle yazılır (her tuşta barkod yeniden çizilmesin).
        Assert.Equal("(01)08691234567890", box.Text);
        try
        {
            Ui.WaitFor(() => ((BarcodeElement)app.Vm.SelectedElement!.Model).Content == "(01)08691234567890", "barkod içeriği yazıldı", 2000);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"model: {((BarcodeElement)app.Vm.SelectedElement!.Model).Content} | kutu: {box.Text}");
        }
    });

    private sealed class Sample
    {
        public Sample? Child { get; set; }
        public string Name { get; set; } = "ad";
    }

    [Fact]
    public void BindingCheck_ReportsMisspelledMembers_ButNotEmptyLinks() => Ui.Run(() =>
    {
        BindingErrors.Take();
        var empty = new TextBlock { DataContext = new Sample() };
        empty.Bind(TextBlock.TextProperty, new Avalonia.Data.ReflectionBinding("Child.Name"));
        var wrong = new TextBlock { DataContext = new Sample() };
        wrong.Bind(TextBlock.TextProperty, new Avalonia.Data.ReflectionBinding("Nmae"));
        var window = new Window { Content = new StackPanel { Children = { empty, wrong } } };
        window.Show();
        Ui.Pump();
        window.Close();

        string error = Assert.Single(BindingErrors.Take());
        Assert.Contains("Nmae", error);
    });
}
