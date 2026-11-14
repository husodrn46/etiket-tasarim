using AkelEtiket.App.Mac.Services;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Documents;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;

namespace AkelEtiket.App.Mac.Tests;

/// <summary>
/// Tuvalin gerçek fare ve klavye girdisiyle davranışı: seçim, taşıma, boyutlandırma, lastik bant, yerinde yazı
/// düzenleme, kılavuz çizgisi ve geri alma. Girdi, pencereye işletim sisteminden gelir gibi gönderilir.
/// </summary>
public class CanvasInteractionTests
{
    private static RawInputModifiers Command =>
        PlatformKeys.Command == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static ElementViewModel Title(AppHarness app) =>
        app.Vm.Session.Elements.First(e => e.Model is TextElement t && t.Text.Contains("KOLİ"));

    private static Point Center(AppHarness app, ElementViewModel element) =>
        app.At(element.Model.X + element.Model.Width / 2, element.Model.Y + element.Model.Height / 2);

    [Fact]
    public void Window_ShowsTheLabel_FittedAndRendered() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        var label = app.Vm.Session.Document.Label;
        Assert.Equal(100, label.WidthMm);
        Assert.Equal(150, label.HeightMm);
        Assert.Contains("Ornek-100x150-Koli-Etiketi-GS1", app.Window.Title);

        // Etiket görünür alana sığdırılmıştır: dört köşesi de tuvalin kaydırma alanının içindedir.
        var scroll = app.Window.FindControl<ScrollViewer>("CanvasScroll")!;
        Ui.WaitFor(() => scroll.Extent.Height <= scroll.Viewport.Height + 1, "etiket pencereye sığdı");
        Assert.InRange(app.Vm.Zoom, 0.5, 4);

        string path = app.Screenshot("main-window");
        using var frame = new Bitmap(path);
        Assert.Equal(1440, frame.PixelSize.Width);

        // Kağıt beyaz, çalışma alanı koyu: etiketin içi ve dışı ayırt edilir.
        var inside = Pixel(path, app.At(50, 4));
        var outside = Pixel(path, app.At(-8, 75));
        Assert.True(inside.R > 240 && inside.G > 240 && inside.B > 240, $"kağıt beyaz olmalı: {inside}");
        Assert.True(outside.R < 60 && outside.G < 60 && outside.B < 60, $"çalışma alanı koyu olmalı: {outside}");

        // Barkod çizilmiştir: barkodun ortasından geçen yatay çizgide hem siyah hem beyaz noktalar vardır.
        var barcode = app.Vm.Session.Elements.First(e => e.Model is BarcodeElement);
        var row = Enumerable.Range(0, 60).Select(i => Pixel(path, app.At(
            barcode.Model.X + barcode.Model.Width * (0.15 + 0.7 * i / 60.0), barcode.Model.Y + barcode.Model.Height * 0.4))).ToList();
        Assert.Contains(row, c => c.R < 40);
        Assert.Contains(row, c => c.R > 220);
    });

    [Fact]
    public void Click_SelectsElement_AndClickOnEmptyPaperClears() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        var title = Title(app);
        Assert.Empty(app.Vm.Session.SelectedElements);

        app.Click(Center(app, title));
        Assert.Same(title, Assert.Single(app.Vm.Session.SelectedElements));

        // Etiketin dışındaki boş alana tıklamak seçimi kaldırır.
        app.Click(app.At(-6, 75));
        Assert.Empty(app.Vm.Session.SelectedElements);
    });

    [Fact]
    public void Drag_MovesElement_AsOneUndoStep() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        app.Vm.Settings.SnapToGrid = false;
        app.Vm.Settings.SnapToObjects = false;
        var title = Title(app);
        double x = title.Model.X, y = title.Model.Y;
        var from = Center(app, title);
        // Alt: yakalamasız taşıma; 10 mm sola, 6 mm aşağı.
        var to = new Point(from.X - 10 * app.Surface.Scale, from.Y + 6 * app.Surface.Scale);

        app.Drag(from, to, RawInputModifiers.Alt);

        Assert.Equal(x - 10, title.Model.X, 1);
        Assert.Equal(y + 6, title.Model.Y, 1);
        Assert.True(app.Vm.Session.IsDirty);

        app.Vm.UndoCommand.Execute(null);
        Ui.Pump();
        var restored = Title(app);
        Assert.Equal(x, restored.Model.X, 3);
        Assert.Equal(y, restored.Model.Y, 3);
    });

    [Fact]
    public void Escape_CancelsDragInProgress() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        var title = Title(app);
        double x = title.Model.X;
        var from = Center(app, title);

        app.Drag(from, new Point(from.X - 80, from.Y + 40), release: false);
        Assert.True(app.Surface.IsDragging);
        Assert.NotEqual(x, title.Model.X);

        app.Key(Key.Escape);
        Assert.False(app.Surface.IsDragging);
        Assert.Equal(x, title.Model.X, 3);
    });

    [Fact]
    public void DraggingCornerHandle_ResizesSelectedElement() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        var shape = app.Vm.Session.Elements.First(e => e.Model is ShapeElement && e.Model.Rotation == 0 && e.Model.Width > 20 && e.Model.Height > 20);
        // Çerçevenin kenarına tıklanır (içi boş bir şeklin ortasında başka öğeler olabilir).
        app.Vm.Session.Select(shape);
        Ui.Pump();
        double width = shape.Model.Width, height = shape.Model.Height;
        var corner = app.At(shape.Model.X + shape.Model.Width, shape.Model.Y + shape.Model.Height);

        app.Drag(corner, new Point(corner.X - 5 * app.Surface.Scale, corner.Y - 8 * app.Surface.Scale), RawInputModifiers.Alt);

        Assert.Equal(width - 5, shape.Model.Width, 1);
        Assert.Equal(height - 8, shape.Model.Height, 1);
    });

    [Fact]
    public void RubberBand_SelectsEveryElementInside() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        var label = app.Vm.Session.Document.Label;

        // Etiketin tamamını saran bant, etiketin dışından başlar.
        app.Drag(app.At(-4, -4), app.At(label.WidthMm + 4, label.HeightMm + 4));

        Assert.Equal(app.Vm.Session.Elements.Count(e => e.Model.Visible && !e.Model.Locked),
            app.Vm.Session.SelectedElements.Count(e => !e.Model.Locked));
        Assert.True(app.Vm.Session.SelectedElements.Count > 3);
    });

    [Fact]
    public void DoubleClick_EditsTextInPlace_AndEnterCommits() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        var title = Title(app);
        var p = Center(app, title);

        app.Window.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseUp(p, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseUp(p, MouseButton.Left, RawInputModifiers.None);
        Ui.WaitFor(() => app.Surface.IsTextEditing && app.Window.FocusManager?.GetFocusedElement() is TextBox, "yazı düzenleyici açıldı");

        // Düzenleyici açıldığında metnin tamamı seçilidir: yazılan metin eskisinin yerine geçer.
        app.Window.KeyTextInput("PALET");
        app.Key(Key.Enter);

        Assert.False(app.Surface.IsTextEditing);
        Assert.Equal("PALET", ((TextElement)Title2(app, "PALET").Model).Text);

        app.Vm.UndoCommand.Execute(null);
        Ui.Pump();
        Assert.Contains(app.Vm.Session.Elements, e => e.Model is TextElement t && t.Text.Contains("KOLİ"));
    });

    private static ElementViewModel Title2(AppHarness app, string text) =>
        app.Vm.Session.Elements.First(e => e.Model is TextElement t && t.Text == text);

    [Fact]
    public void Keyboard_DeletesNudgesAndUndoes() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        var title = Title(app);
        int count = app.Vm.Session.Elements.Count;
        double x = title.Model.X;
        app.Click(Center(app, title));

        app.Key(Key.Right);
        Assert.Equal(x + 0.5, Title(app).Model.X, 3);

        app.Key(Key.Back);
        Assert.Equal(count - 1, app.Vm.Session.Elements.Count);

        // Komut tuşu + Z (macOS'ta ⌘Z): silme geri alınır.
        app.Key(Key.Z, Command);
        Assert.Equal(count, app.Vm.Session.Elements.Count);
    });

    [Fact]
    public void DraggingFromRuler_AddsGuide() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        Assert.Empty(app.Vm.Session.Document.Guides);
        var ruler = app.Window.FindControl<Controls.RulerControl>("TopRuler")!;
        var start = ruler.TranslatePoint(new Point(300, 10), app.Window)!.Value;

        app.Drag(start, app.At(50, 40));

        var guide = Assert.Single(app.Vm.Session.Document.Guides);
        Assert.Equal(GuideOrientation.Horizontal, guide.Orientation);
        Assert.Equal(40, guide.Position, 0);
    });

    [Fact]
    public void CommandWheel_ZoomsAroundPointer() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        // Etiket görünür alandan büyükken (iki yönde de kaydırılabilirken) imlecin altındaki nokta sabit kalır.
        var scroll = app.Window.FindControl<ScrollViewer>("CanvasScroll")!;
        app.Vm.Zoom *= 3;
        Ui.WaitFor(() => scroll.Extent.Width > scroll.Viewport.Width + 200 && scroll.Extent.Height > scroll.Viewport.Height + 200, "etiket büyütüldü");
        scroll.Offset = new Vector(300, 400);
        Ui.Pump();
        double zoom = app.Vm.Zoom;
        var anchor = app.At(30, 30);

        app.Window.MouseWheel(anchor, new Vector(0, 1), Command);
        Ui.Pump();

        Assert.Equal(zoom * 1.15, app.Vm.Zoom, 3);
        // İmlecin altındaki etiket noktası yerinde kalır.
        var after = app.At(30, 30);
        Assert.InRange(after.X, anchor.X - 2, anchor.X + 2);
        Assert.InRange(after.Y, anchor.Y - 2, anchor.Y + 2);
    });

    [Fact]
    public void InterfaceFont_DoesNotClipAccentsOnCapitals() => Ui.Run(() =>
    {
        // "Ü" harfinin mürekkebi "U" harfinden daha yukarıda başlamalı: noktalar çizilmiş demektir.
        static int InkTop(string text)
        {
            var window = new Window
            {
                Width = 120, Height = 80, Background = Avalonia.Media.Brushes.White,
                Content = new TextBlock { Text = text, FontSize = 40, Margin = new Thickness(20), Foreground = Avalonia.Media.Brushes.Black },
            };
            window.Show();
            Ui.Pump();
            using var frame = window.CaptureRenderedFrame()!;
            using var stream = new MemoryStream();
            frame.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            window.Close();
            using var bitmap = SkiaSharp.SKBitmap.Decode(stream.ToArray());
            for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).Red < 128) return y;
            return int.MaxValue;
        }

        int plain = InkTop("U");
        int accented = InkTop("Ü");
        Assert.True(accented < plain - 3, $"Ü üstündeki noktalar çizilmedi (U: {plain}, Ü: {accented}).");
    });

    private static (byte R, byte G, byte B) Pixel(string pngPath, Point p)
    {
        using var skia = SkiaSharp.SKBitmap.Decode(pngPath);
        var c = skia.GetPixel((int)Math.Round(p.X), (int)Math.Round(p.Y));
        return (c.Red, c.Green, c.Blue);
    }
}
