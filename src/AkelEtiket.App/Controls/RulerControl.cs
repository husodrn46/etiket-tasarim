using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AkelEtiket.Core.Geometry;
using AkelEtiket.Core.Model;

namespace AkelEtiket.App.Controls;

/// <summary>
/// mm cetveli (üst: yatay, sol: dikey). Tuvalin kaydırma ve yakınlaştırmasını izler; seçili öğenin
/// izdüşümünü ve imleç konumunu gösterir. Cetvelden tuvale sürükleyerek kılavuz çizgisi oluşturulur.
/// </summary>
public sealed class RulerControl : FrameworkElement
{
    public static readonly DependencyProperty SurfaceProperty = DependencyProperty.Register(
        nameof(Surface), typeof(DesignSurface), typeof(RulerControl), new PropertyMetadata(null, OnSurfaceChanged));

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(RulerControl), new FrameworkPropertyMetadata(Orientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsRender));

    private double? _pointer;
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    public RulerControl()
    {
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        ToolTip = "Kılavuz çizgisi eklemek için tuvale doğru sürükleyin";
    }

    public DesignSurface? Surface
    {
        get => (DesignSurface?)GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    private static void OnSurfaceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ruler = (RulerControl)d;
        if (e.OldValue is DesignSurface old)
        {
            old.ViewChanged -= ruler.OnViewChanged;
            old.PointerMoved -= ruler.OnPointerMoved;
        }
        if (e.NewValue is DesignSurface surface)
        {
            surface.ViewChanged += ruler.OnViewChanged;
            surface.PointerMoved += ruler.OnPointerMoved;
        }
        ruler.InvalidateVisual();
    }

    private void OnViewChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnPointerMoved(object? sender, (double X, double Y)? mm)
    {
        _pointer = mm is { } p ? (Orientation == Orientation.Horizontal ? p.X : p.Y) : null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var background = TryFindResource("Brush.Ruler") as Brush ?? Brushes.Black;
        var textBrush = TryFindResource("Brush.RulerText") as Brush ?? Brushes.Gray;
        var borderBrush = TryFindResource("Brush.Border") as Brush ?? Brushes.Gray;
        var selectionBrush = TryFindResource("Brush.SelectionSoft") as Brush ?? Brushes.Transparent;
        var accent = TryFindResource("Brush.Selection") as Brush ?? Brushes.Blue;
        bool horizontal = Orientation == Orientation.Horizontal;
        dc.DrawRectangle(background, null, new Rect(RenderSize));
        var tickPen = new Pen(textBrush, 1);
        tickPen.Freeze();
        if (horizontal) dc.DrawLine(new Pen(borderBrush, 1), new Point(0, ActualHeight - 0.5), new Point(ActualWidth, ActualHeight - 0.5));
        else dc.DrawLine(new Pen(borderBrush, 1), new Point(ActualWidth - 0.5, 0), new Point(ActualWidth - 0.5, ActualHeight));

        var surface = Surface;
        if (surface is null || !surface.IsLoaded || surface.ViewModel is not { } vm) return;
        Point origin;
        try
        {
            origin = surface.TranslatePoint(surface.LabelOrigin, this);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        double start = horizontal ? origin.X : origin.Y;
        double scale = surface.Scale;
        double length = horizontal ? ActualWidth : ActualHeight;
        double thickness = horizontal ? ActualHeight : ActualWidth;
        var label = vm.Session.Document.Label;
        double labelLength = horizontal ? label.WidthMm : label.HeightMm;

        // Etiket alanı ve seçimin izdüşümü
        var labelBand = new SolidColorBrush(Color.FromArgb(0x18, 0x80, 0x80, 0x80));
        Rect Band(double fromMm, double toMm) => horizontal
            ? new Rect(start + fromMm * scale, 0, Math.Max(0, (toMm - fromMm) * scale), thickness)
            : new Rect(0, start + fromMm * scale, thickness, Math.Max(0, (toMm - fromMm) * scale));
        dc.DrawRectangle(labelBand, null, Band(0, labelLength));
        if (vm.Session.SelectedElements.Count > 0)
        {
            var bounds = vm.Session.SelectedElements.Select(e => ElementGeometry.GetRotatedBoundsMm(e.Model)).Aggregate(RectMm.Union);
            dc.DrawRectangle(selectionBrush, null, horizontal ? Band(bounds.X, bounds.Right) : Band(bounds.Y, bounds.Bottom));
        }

        // Etiket adımı: yazılar en az ~48 DIP aralıklı
        double[] steps = [1, 2, 5, 10, 20, 50, 100, 200];
        double labelStep = steps.FirstOrDefault(s => s * scale >= 48, 500);
        double minorStep = labelStep / (labelStep * scale / 5 >= 10 ? 10 : 5);
        if (minorStep * scale < 4) minorStep = labelStep / 2;
        double firstMm = Math.Floor(-start / scale / minorStep) * minorStep;
        double lastMm = (length - start) / scale;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (double mm = firstMm; mm <= lastMm; mm += minorStep)
        {
            double pos = Math.Round(start + mm * scale) + 0.5;
            bool isLabel = Math.Abs(mm / labelStep - Math.Round(mm / labelStep)) < 1e-6;
            bool isHalf = !isLabel && Math.Abs(mm / (labelStep / 2) - Math.Round(mm / (labelStep / 2))) < 1e-6;
            double tick = isLabel ? thickness * 0.55 : isHalf ? thickness * 0.35 : thickness * 0.2;
            if (horizontal) dc.DrawLine(tickPen, new Point(pos, thickness - tick), new Point(pos, thickness));
            else dc.DrawLine(tickPen, new Point(thickness - tick, pos), new Point(thickness, pos));
            if (!isLabel) continue;
            var text = new FormattedText(Math.Round(mm).ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, LabelTypeface, 9.5, textBrush, dpi);
            if (horizontal)
            {
                dc.DrawText(text, new Point(pos + 3, 1));
            }
            else
            {
                dc.PushTransform(new RotateTransform(-90, 2, pos - 3));
                dc.DrawText(text, new Point(2, pos - 3));
                dc.Pop();
            }
        }

        if (_pointer is { } pointer)
        {
            double pos = Math.Round(start + pointer * scale) + 0.5;
            var pen = new Pen(accent, 1);
            if (horizontal) dc.DrawLine(pen, new Point(pos, 0), new Point(pos, thickness));
            else dc.DrawLine(pen, new Point(0, pos), new Point(thickness, pos));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        // Üst cetvelden yatay, sol cetvelden dikey kılavuz çekilir.
        Surface?.BeginNewGuide(Orientation == Orientation.Horizontal ? GuideOrientation.Horizontal : GuideOrientation.Vertical);
        e.Handled = true;
    }
}
