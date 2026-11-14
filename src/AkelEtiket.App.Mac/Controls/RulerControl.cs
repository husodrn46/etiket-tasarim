using System.Globalization;
using AkelEtiket.Core.Geometry;
using AkelEtiket.Core.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.App.Mac.Controls;

/// <summary>
/// mm cetveli (üst: yatay, sol: dikey). Tuvalin kaydırma ve yakınlaştırmasını izler; seçili öğenin
/// izdüşümünü ve imleç konumunu gösterir. Cetvelden tuvale sürükleyerek kılavuz çizgisi oluşturulur.
/// </summary>
public sealed class RulerControl : Control
{
    public static readonly StyledProperty<DesignSurface?> SurfaceProperty =
        AvaloniaProperty.Register<RulerControl, DesignSurface?>(nameof(Surface));

    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<RulerControl, Orientation>(nameof(Orientation), Orientation.Horizontal);

    private double? _pointer;

    static RulerControl()
    {
        AffectsRender<RulerControl>(OrientationProperty);
        ClipToBoundsProperty.OverrideDefaultValue<RulerControl>(true);
    }

    public RulerControl()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(this, L10n.T("Kılavuz çizgisi eklemek için tuvale doğru sürükleyin"));
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public DesignSurface? Surface
    {
        get => GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SurfaceProperty) return;
        if (change.OldValue is DesignSurface old)
        {
            old.ViewChanged -= OnViewChanged;
            old.CursorMoved -= OnCursorMoved;
        }
        if (change.NewValue is DesignSurface surface)
        {
            surface.ViewChanged += OnViewChanged;
            surface.CursorMoved += OnCursorMoved;
        }
        InvalidateVisual();
    }

    private void OnViewChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnCursorMoved(object? sender, (double X, double Y)? mm)
    {
        _pointer = mm is { } p ? (Orientation == Orientation.Horizontal ? p.X : p.Y) : null;
        InvalidateVisual();
    }

    private IBrush Res(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush ? brush : fallback;

    public override void Render(DrawingContext dc)
    {
        var textBrush = Res("Brush.RulerText", Brushes.Gray);
        bool horizontal = Orientation == Orientation.Horizontal;
        double width = Bounds.Width, height = Bounds.Height;
        dc.DrawRectangle(Res("Brush.Ruler", Brushes.Black), null, new Rect(Bounds.Size));
        var borderPen = new Pen(Res("Brush.Border", Brushes.Gray), 1);
        if (horizontal) dc.DrawLine(borderPen, new Point(0, height - 0.5), new Point(width, height - 0.5));
        else dc.DrawLine(borderPen, new Point(width - 0.5, 0), new Point(width - 0.5, height));

        var surface = Surface;
        if (surface?.ViewModel is not { } vm || surface.TranslatePoint(surface.LabelOrigin, this) is not { } origin) return;

        double start = horizontal ? origin.X : origin.Y;
        double scale = surface.Scale;
        double length = horizontal ? width : height;
        double thickness = horizontal ? height : width;
        var label = vm.Session.Document.Label;
        double labelLength = horizontal ? label.WidthMm : label.HeightMm;

        // Etiket alanı ve seçimin izdüşümü
        Rect Band(double fromMm, double toMm) => horizontal
            ? new Rect(start + fromMm * scale, 0, Math.Max(0, (toMm - fromMm) * scale), thickness)
            : new Rect(0, start + fromMm * scale, thickness, Math.Max(0, (toMm - fromMm) * scale));
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x18, 0x80, 0x80, 0x80)), null, Band(0, labelLength));
        if (vm.Session.SelectedElements.Count > 0)
        {
            var bounds = vm.Session.SelectedElements.Select(e => ElementGeometry.GetRotatedBoundsMm(e.Model)).Aggregate(RectMm.Union);
            dc.DrawRectangle(Res("Brush.SelectionSoft", Brushes.Transparent), null, horizontal ? Band(bounds.X, bounds.Right) : Band(bounds.Y, bounds.Bottom));
        }

        // Etiket adımı: yazılar en az ~48 DIP aralıklı
        double[] steps = [1, 2, 5, 10, 20, 50, 100, 200];
        double labelStep = steps.FirstOrDefault(s => s * scale >= 48, 500);
        double minorStep = labelStep / (labelStep * scale / 5 >= 10 ? 10 : 5);
        if (minorStep * scale < 4) minorStep = labelStep / 2;
        double firstMm = Math.Floor(-start / scale / minorStep) * minorStep;
        double lastMm = (length - start) / scale;
        var tickPen = new Pen(textBrush, 1);
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
                FlowDirection.LeftToRight, Typeface.Default, 9.5, textBrush);
            if (horizontal)
            {
                dc.DrawText(text, new Point(pos + 3, 1));
            }
            else
            {
                // Dikey cetvelde yazı aşağıdan yukarı okunur: (2, pos - 3) noktası etrafında -90° döndürülür.
                var pivot = new Point(2, pos - 3);
                var rotate = Matrix.CreateTranslation(-pivot.X, -pivot.Y) * Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(pivot.X, pivot.Y);
                using (dc.PushTransform(rotate)) dc.DrawText(text, pivot);
            }
        }

        if (_pointer is { } pointer)
        {
            double pos = Math.Round(start + pointer * scale) + 0.5;
            var pen = new Pen(Res("Brush.Selection", Brushes.Blue), 1);
            if (horizontal) dc.DrawLine(pen, new Point(pos, 0), new Point(pos, thickness));
            else dc.DrawLine(pen, new Point(0, pos), new Point(thickness, pos));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        // Üst cetvelden yatay, sol cetvelden dikey kılavuz çekilir.
        Surface?.BeginNewGuide(Orientation == Orientation.Horizontal ? GuideOrientation.Horizontal : GuideOrientation.Vertical, e.Pointer);
        e.Handled = true;
    }
}
