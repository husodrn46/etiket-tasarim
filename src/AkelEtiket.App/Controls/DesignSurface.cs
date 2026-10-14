using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AkelEtiket.Core.Geometry;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Documents;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;

namespace AkelEtiket.App.Controls;

/// <summary>
/// Tasarım tuvali. Etiketi gerçek ölçüsünde (mm → ekran) çizer; öğeler yazıcı çözünürlüğünde render edilmiş
/// 1-bit katmanlardır (ekran = baskı). Seçim, taşıma, boyutlandırma, döndürme, yakalama, lastik bant,
/// kılavuz çizgileri, yakınlaştırma ve dosya bırakma burada işlenir; tüm değişiklikler görünüm modeline gider.
/// Metin öğeleri çift tıklayınca yerinde düzenlenir (üstte açılan metin kutusu); tamamen seçilmiş bir grup tek
/// çerçeveyle orantılı boyutlandırılır; taşırken komşu öğelere/kenarlara uzaklıklar gösterilir.
/// </summary>
public sealed class DesignSurface : FrameworkElement
{
    public const double MarginDip = 56;
    private const double HandleSize = 9;
    private const double RotateHandleDistance = 26;
    private const double DragThreshold = 3;

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(MainViewModel), typeof(DesignSurface), new PropertyMetadata(null, OnViewModelChanged));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(DesignSurface),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnZoomChanged));

    private DocumentSession? _session;
    private readonly HashSet<INotifyPropertyChanged> _subscribed = [];

    private enum DragMode { None, Pending, Move, Resize, Rotate, RubberBand, Guide, NewGuide, Pan, GroupResize }

    private DragMode _mode;
    private Point _startPoint;
    private (double X, double Y) _startMm;
    private IDisposable? _transaction;
    private readonly List<(LabelElement Element, double X, double Y)> _moveStarts = [];
    private RectMm _moveBounds;
    private List<RectMm> _otherBounds = [];
    private ElementViewModel? _activeElement;
    private (double X, double Y, double Width, double Height, double Rotation) _resizeStart;
    private ResizeHandle _resizeHandle;
    private GuideLine? _guide;
    private GuideOrientation _newGuideOrientation;
    private double? _newGuidePosition;
    private Point _panStart;
    private Point _panOffsetStart;
    private RectMm? _rubberBand;
    private IReadOnlyList<SnapLine> _snapLines = [];
    private ElementViewModel? _hover;
    private List<TransformMath.GroupMemberStart> _groupStarts = [];
    private RectMm _groupStartBounds;
    private TextBox? _textEditor;
    private ElementViewModel? _editingVm;
    private Window? _editWindow;

    public DesignSurface()
    {
        Focusable = true;
        FocusVisualStyle = null;
        AllowDrop = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public MainViewModel? ViewModel
    {
        get => (MainViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>mm başına ekran birimi (DIP).</summary>
    public double Scale => Zoom * 96.0 / 25.4;

    public Point LabelOrigin => new(MarginDip, MarginDip);

    /// <summary>Görünüm değişti (cetveller yeniden çizilsin).</summary>
    public event EventHandler? ViewChanged;

    /// <summary>İmleç konumu (mm) değişti.</summary>
    public event EventHandler<(double X, double Y)?>? PointerMoved;

    public Point MmToDip(double xMm, double yMm) => new(MarginDip + xMm * Scale, MarginDip + yMm * Scale);

    public (double X, double Y) DipToMm(Point p) => ((p.X - MarginDip) / Scale, (p.Y - MarginDip) / Scale);

    public ScrollViewer? ScrollHost => FindParent<ScrollViewer>(this);

    // ───────────────────────── Bağlantılar ─────────────────────────

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var surface = (DesignSurface)d;
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.PropertyChanged -= surface.OnMainPropertyChanged;
            oldVm.InlineTextEditRequested -= surface.OnInlineTextEditRequested;
        }
        if (e.NewValue is MainViewModel newVm)
        {
            newVm.PropertyChanged += surface.OnMainPropertyChanged;
            newVm.InlineTextEditRequested += surface.OnInlineTextEditRequested;
            surface.AttachSession(newVm.Session);
        }
    }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var surface = (DesignSurface)d;
        surface.UpdateScalingMode();
        if (surface._textEditor is not null) surface.InvalidateArrange();
        surface.ViewChanged?.Invoke(surface, EventArgs.Empty);
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Session) && ViewModel is { } vm) AttachSession(vm.Session);
        if (e.PropertyName is nameof(MainViewModel.Settings) or nameof(MainViewModel.DotsPerMm))
        {
            UpdateScalingMode();
            InvalidateVisual();
        }
    }

    private void AttachSession(DocumentSession session)
    {
        if (ReferenceEquals(_session, session)) return;
        CloseTextEditor();
        CancelDrag();
        if (_session is not null)
        {
            _session.Elements.CollectionChanged -= OnElementsChanged;
            _session.SelectionChanged -= OnInvalidate;
            _session.LabelSetupChanged -= OnLabelSetupChanged;
            _session.GuidesChanged -= OnInvalidate;
            _session.Changed -= OnInvalidate;
        }
        foreach (var item in _subscribed) item.PropertyChanged -= OnItemPropertyChanged;
        _subscribed.Clear();
        _session = session;
        session.Elements.CollectionChanged += OnElementsChanged;
        session.SelectionChanged += OnInvalidate;
        session.LabelSetupChanged += OnLabelSetupChanged;
        session.GuidesChanged += OnInvalidate;
        session.Changed += OnInvalidate;
        SubscribeElements();
        _hover = null;
        InvalidateMeasure();
        InvalidateVisual();
        UpdateScalingMode();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnElementsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        SubscribeElements();
        // Düzenlenen öğe silindiyse ya da geri alma ile değiştiyse düzenleyici kapanır.
        if (_editingVm is not null && _session is not null && !_session.Elements.Contains(_editingVm)) CloseTextEditor();
        InvalidateVisual();
    }

    private void SubscribeElements()
    {
        if (_session is null) return;
        var current = new HashSet<INotifyPropertyChanged>();
        foreach (var vm in _session.Elements)
        {
            current.Add(vm);
            current.Add(vm.Model);
        }
        foreach (var stale in _subscribed.Where(s => !current.Contains(s)).ToList())
        {
            stale.PropertyChanged -= OnItemPropertyChanged;
            _subscribed.Remove(stale);
        }
        foreach (var item in current)
            if (_subscribed.Add(item)) item.PropertyChanged += OnItemPropertyChanged;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvalidateVisual();
        if (_textEditor is not null) InvalidateArrange();
    }

    private void OnInvalidate(object? sender, EventArgs e)
    {
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnLabelSetupChanged(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ekran pikseli başına bir yazıcı noktası veya daha büyükse noktalar keskin (en yakın komşu) gösterilir.</summary>
    private void UpdateScalingMode()
    {
        double dpmm = ViewModel?.DotsPerMm ?? 8;
        double dpiScale = PresentationSource.FromVisual(this) is { CompositionTarget: { } target } ? target.TransformToDevice.M11 : 1.0;
        double pixelsPerDot = Scale / dpmm * dpiScale;
        RenderOptions.SetBitmapScalingMode(this, pixelsPerDot >= 0.99 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_textEditor is not null && _editingVm?.Model is TextElement t)
        {
            var box = InlineTextEditing.ComputeBox(t, Scale, MarginDip);
            _textEditor.Measure(new Size(box.Width, box.Height));
        }
        if (_session is null) return new Size(0, 0);
        var label = _session.Document.Label;
        return new Size(label.WidthMm * Scale + 2 * MarginDip, label.HeightMm * Scale + 2 * MarginDip);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_textEditor is not null && _editingVm?.Model is TextElement t)
        {
            var box = InlineTextEditing.ComputeBox(t, Scale, MarginDip);
            _textEditor.FontSize = box.FontSizeDip;
            _textEditor.Arrange(new Rect(box.Left, box.Top, box.Width, box.Height));
            _textEditor.RenderTransform = box.Angle == 0 ? Transform.Identity : new RotateTransform(box.Angle, box.Width / 2, box.Height / 2);
        }
        return finalSize;
    }

    protected override int VisualChildrenCount => _textEditor is null ? 0 : 1;

    protected override Visual GetVisualChild(int index) =>
        _textEditor is not null && index == 0 ? _textEditor : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>Etiketi görünür alana sığdıracak yakınlaştırma.</summary>
    public double ComputeFitZoom(double viewportWidth, double viewportHeight)
    {
        if (_session is null || viewportWidth <= 0 || viewportHeight <= 0) return 1;
        var label = _session.Document.Label;
        double perMm = 96.0 / 25.4;
        double zx = (viewportWidth - 2 * MarginDip - 8) / (label.WidthMm * perMm);
        double zy = (viewportHeight - 2 * MarginDip - 8) / (label.HeightMm * perMm);
        return Math.Clamp(Math.Min(zx, zy), MainViewModel.MinZoom, MainViewModel.MaxZoom);
    }

    // ───────────────────────── Çizim ─────────────────────────

    private Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Magenta;

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_session is null || ViewModel is null) return;
        var settings = ViewModel.Settings;
        var label = _session.Document.Label;
        double s = Scale;
        var paper = new Rect(MarginDip, MarginDip, label.WidthMm * s, label.HeightMm * s);
        double radius = Math.Min(label.CornerRadiusMm * s, Math.Min(paper.Width, paper.Height) / 4);

        // Gölge ve kağıt
        var shadow = Res("Brush.Shadow");
        dc.DrawRoundedRectangle(shadow, null, new Rect(paper.X + 1, paper.Y + 3, paper.Width, paper.Height), radius, radius);
        dc.DrawRoundedRectangle(Brushes.White, null, paper, radius, radius);

        // Izgara (yalnızca kağıt üzerinde)
        if (settings.ShowGrid && settings.GridSizeMm * s >= 6)
        {
            dc.PushClip(new RectangleGeometry(paper, radius, radius));
            var minor = new Pen(new SolidColorBrush(Color.FromArgb(0x24, 0x40, 0x60, 0x90)), 1);
            var major = new Pen(new SolidColorBrush(Color.FromArgb(0x48, 0x40, 0x60, 0x90)), 1);
            minor.Freeze();
            major.Freeze();
            double step = settings.GridSizeMm;
            int count = (int)Math.Ceiling(label.WidthMm / step);
            for (int i = 1; i < count; i++)
            {
                double mm = i * step;
                double x = Math.Round(paper.X + mm * s) + 0.5;
                dc.DrawLine(IsMajor(mm) ? major : minor, new Point(x, paper.Top), new Point(x, paper.Bottom));
            }
            count = (int)Math.Ceiling(label.HeightMm / step);
            for (int i = 1; i < count; i++)
            {
                double mm = i * step;
                double y = Math.Round(paper.Y + mm * s) + 0.5;
                dc.DrawLine(IsMajor(mm) ? major : minor, new Point(paper.Left, y), new Point(paper.Right, y));
            }
            dc.Pop();
        }

        // Öğe katmanları (z-sırasıyla)
        foreach (var vm in _session.Elements)
        {
            if (!vm.Model.Visible || vm.LayerImage is not ImageSource image) continue;
            var rect = new Rect(MarginDip + vm.LayerLeftMm * s, MarginDip + vm.LayerTopMm * s, vm.LayerWidthMm * s, vm.LayerHeightMm * s);
            dc.DrawImage(image, rect);
        }

        // Etiket dışı alanı karart: dışarıda kalan kısım basılmaz.
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(RenderSize)), new RectangleGeometry(paper, radius, radius));
        dc.DrawGeometry(Res("Brush.WorkspaceDim"), null, outside);
        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0x80, 0x80, 0x80)), 1), paper, radius, radius);

        // Hatalı öğeler
        var errorPen = new Pen(Res("Brush.Error"), 1.5) { DashStyle = DashStyles.Dash };
        foreach (var vm in _session.Elements.Where(e => e.Model.Visible && e.HasErrors))
            DrawElementOutline(dc, vm.Model, errorPen, 2);

        // Kılavuz çizgileri
        var guidePen = new Pen(Res("Brush.Guide"), 1);
        foreach (var guide in _session.Document.Guides)
            DrawGuide(dc, guide.Orientation, guide.Position, guidePen);
        if (_mode == DragMode.NewGuide && _newGuidePosition is { } newPos)
            DrawGuide(dc, _newGuideOrientation, newPos, new Pen(Res("Brush.Guide"), 1) { DashStyle = DashStyles.Dash });

        // Üzerine gelinen öğe
        var selectionBrush = Res("Brush.Selection");
        if (_hover is { IsSelected: false } && _mode == DragMode.None && _hover.Model.Visible)
            DrawElementOutline(dc, _hover.Model, new Pen(selectionBrush, 1) { DashStyle = DashStyles.Dot }, 0);

        // Seçim
        var selected = _session.SelectedElements.Where(e => e.Model.Visible).ToList();
        var selectionPen = new Pen(selectionBrush, 1.5);
        var lockedPen = new Pen(selectionBrush, 1.5) { DashStyle = DashStyles.Dash };
        foreach (var vm in selected) DrawElementOutline(dc, vm.Model, vm.Model.Locked ? lockedPen : selectionPen, 0);
        if (selected.Count == 1 && !selected[0].Model.Locked && _mode is DragMode.None or DragMode.Pending or DragMode.Resize or DragMode.Rotate)
            DrawHandles(dc, selected[0].Model, selectionBrush);
        else if (selected.Count > 1)
        {
            var union = selected.Select(e => ElementGeometry.GetRotatedBoundsMm(e.Model)).Aggregate(RectMm.Union);
            bool group = TryGetGroupSelection(out _, out _);
            dc.DrawRectangle(null, new Pen(selectionBrush, group ? 1.5 : 1) { DashStyle = group ? DashStyles.Solid : DashStyles.Dash }, ToDip(union));
            if (group && _mode is DragMode.None or DragMode.Pending or DragMode.GroupResize)
                DrawBoxHandles(dc, RectCornersDip(union), selectionBrush, small: union.Width * Scale < 30 || union.Height * Scale < 30);
        }

        // Taşırken uzaklıklar (komşu öğe ya da etiket kenarı)
        if (_mode == DragMode.Move && _moveStarts.Count > 0) DrawDistances(dc);

        // Yakalama çizgileri
        var snapPen = new Pen(Res("Brush.Snap"), 1);
        foreach (var line in _snapLines) DrawGuide(dc, line.Orientation, line.Position, snapPen);

        // Lastik bant
        if (_rubberBand is { } band)
            dc.DrawRectangle(Res("Brush.SelectionSoft"), new Pen(selectionBrush, 1), ToDip(band));
    }

    private static bool IsMajor(double mm) => Math.Abs(mm / 10 - Math.Round(mm / 10)) < 1e-6;

    private Rect ToDip(RectMm r) => new(MarginDip + r.X * Scale, MarginDip + r.Y * Scale, Math.Max(0, r.Width * Scale), Math.Max(0, r.Height * Scale));

    private void DrawGuide(DrawingContext dc, GuideOrientation orientation, double position, Pen pen)
    {
        if (orientation == GuideOrientation.Vertical)
        {
            double x = Math.Round(MarginDip + position * Scale) + 0.5;
            dc.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));
        }
        else
        {
            double y = Math.Round(MarginDip + position * Scale) + 0.5;
            dc.DrawLine(pen, new Point(0, y), new Point(ActualWidth, y));
        }
    }

    private Point[] CornersDip(LabelElement e, double inflateDip)
    {
        double inflate = inflateDip / Scale;
        var corners = ElementGeometry.GetCorners(e.X - inflate, e.Y - inflate, e.Width + 2 * inflate, e.Height + 2 * inflate, e.Rotation);
        return corners.Select(c => MmToDip(c.X, c.Y)).ToArray();
    }

    private void DrawElementOutline(DrawingContext dc, LabelElement e, Pen pen, double inflateDip)
    {
        var pts = CornersDip(e, inflateDip);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(pts[0], false, true);
            ctx.PolyLineTo(pts[1..], true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private IEnumerable<(ResizeHandle Handle, Point Point)> HandlePoints(LabelElement e) => HandlePoints(CornersDip(e, 0));

    private Point[] RectCornersDip(RectMm r) =>
    [
        MmToDip(r.X, r.Y), MmToDip(r.Right, r.Y), MmToDip(r.Right, r.Bottom), MmToDip(r.X, r.Bottom),
    ];

    private static IEnumerable<(ResizeHandle Handle, Point Point)> HandlePoints(Point[] c)
    {
        Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        yield return (ResizeHandle.TopLeft, c[0]);
        yield return (ResizeHandle.Top, Mid(c[0], c[1]));
        yield return (ResizeHandle.TopRight, c[1]);
        yield return (ResizeHandle.Right, Mid(c[1], c[2]));
        yield return (ResizeHandle.BottomRight, c[2]);
        yield return (ResizeHandle.Bottom, Mid(c[2], c[3]));
        yield return (ResizeHandle.BottomLeft, c[3]);
        yield return (ResizeHandle.Left, Mid(c[3], c[0]));
    }

    private Point RotateHandlePoint(LabelElement e)
    {
        var c = CornersDip(e, 0);
        var top = new Point((c[0].X + c[1].X) / 2, (c[0].Y + c[1].Y) / 2);
        double rad = e.Rotation * Math.PI / 180;
        return new Point(top.X + Math.Sin(rad) * RotateHandleDistance, top.Y - Math.Cos(rad) * RotateHandleDistance);
    }

    private void DrawHandles(DrawingContext dc, LabelElement e, Brush brush)
    {
        var c = CornersDip(e, 0);
        DrawBoxHandles(dc, c, brush, small: e.Width * Scale < 30 || e.Height * Scale < 30);
        var pen = new Pen(brush, 1.5);
        var top = new Point((c[0].X + c[1].X) / 2, (c[0].Y + c[1].Y) / 2);
        var rot = RotateHandlePoint(e);
        dc.DrawLine(pen, top, rot);
        dc.DrawEllipse(Brushes.White, pen, rot, 5.5, 5.5);
    }

    /// <summary>Kutunun 8 boyutlandırma tutamacı (küçük kutularda yalnız köşeler).</summary>
    private static void DrawBoxHandles(DrawingContext dc, Point[] corners, Brush brush, bool small)
    {
        var pen = new Pen(brush, 1.5);
        foreach (var (handle, point) in HandlePoints(corners))
        {
            if (small && handle is ResizeHandle.Top or ResizeHandle.Bottom or ResizeHandle.Left or ResizeHandle.Right) continue;
            dc.DrawRectangle(Brushes.White, pen, new Rect(point.X - HandleSize / 2, point.Y - HandleSize / 2, HandleSize, HandleSize));
        }
    }

    /// <summary>
    /// Taşınan seçimden sol/sağ/üst/alt yöndeki en yakın engele (diğer öğe ya da etiket kenarı) uzaklık çizgileri
    /// ve mm değerleri.
    /// </summary>
    private void DrawDistances(DrawingContext dc)
    {
        if (_session is null) return;
        var moving = _moveStarts.Select(m => ElementGeometry.GetRotatedBoundsMm(m.Element)).Aggregate(RectMm.Union);
        var label = _session.Document.Label;
        var pen = new Pen(Res("Brush.Snap"), 1) { DashStyle = DashStyles.Dot };
        var gaps = MeasureGaps(moving, _otherBounds, label.WidthMm, label.HeightMm);
        foreach (var gap in gaps)
        {
            if (gap.Length < 0.05) continue;
            var a = MmToDip(gap.FromX, gap.FromY);
            var b = MmToDip(gap.ToX, gap.ToY);
            dc.DrawLine(pen, a, b);
            var text = new FormattedText($"{gap.Length:0.#}", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 10.5, Res("Brush.Snap"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            var origin = gap.Horizontal ? new Point(mid.X - text.Width / 2, mid.Y - text.Height - 1) : new Point(mid.X + 3, mid.Y - text.Height / 2);
            dc.DrawRectangle(Res("Brush.Panel"), null, new Rect(origin.X - 2, origin.Y, text.Width + 4, text.Height));
            dc.DrawText(text, origin);
        }
    }

    /// <summary>Uzaklık ölçümü (mm): yatay olanlar sol/sağ, dikey olanlar üst/alt.</summary>
    internal readonly record struct Gap(double FromX, double FromY, double ToX, double ToY, double Length, bool Horizontal);

    /// <summary>Taşınan kutudan dört yöndeki en yakın engele uzaklıklar (aynı satır/sütunda kalan öğeler veya etiket kenarı).</summary>
    internal static IReadOnlyList<Gap> MeasureGaps(RectMm moving, IReadOnlyList<RectMm> others, double labelWidth, double labelHeight)
    {
        double cy = moving.CenterY, cx = moving.CenterX;
        bool RowOverlap(RectMm o) => o.Bottom > moving.Y && o.Y < moving.Bottom;
        bool ColOverlap(RectMm o) => o.Right > moving.X && o.X < moving.Right;
        double left = others.Where(o => RowOverlap(o) && o.Right <= moving.X + 0.01).Select(o => o.Right).DefaultIfEmpty(0).Max();
        double right = others.Where(o => RowOverlap(o) && o.X >= moving.Right - 0.01).Select(o => o.X).DefaultIfEmpty(labelWidth).Min();
        double top = others.Where(o => ColOverlap(o) && o.Bottom <= moving.Y + 0.01).Select(o => o.Bottom).DefaultIfEmpty(0).Max();
        double bottom = others.Where(o => ColOverlap(o) && o.Y >= moving.Bottom - 0.01).Select(o => o.Y).DefaultIfEmpty(labelHeight).Min();
        var gaps = new List<Gap>();
        if (moving.X >= left) gaps.Add(new Gap(left, cy, moving.X, cy, moving.X - left, true));
        if (right >= moving.Right) gaps.Add(new Gap(moving.Right, cy, right, cy, right - moving.Right, true));
        if (moving.Y >= top) gaps.Add(new Gap(cx, top, cx, moving.Y, moving.Y - top, false));
        if (bottom >= moving.Bottom) gaps.Add(new Gap(cx, moving.Bottom, cx, bottom, bottom - moving.Bottom, false));
        return gaps;
    }

    /// <summary>Seçim tam olarak tek bir grubun bütün (kilitsiz, görünür) üyeleri mi?</summary>
    private bool TryGetGroupSelection(out List<ElementViewModel> members, out RectMm bounds)
    {
        members = [];
        bounds = default;
        if (_session is null || _session.SelectedElements.Count < 2) return false;
        string? groupId = _session.SelectedElements[0].Model.GroupId;
        if (groupId is null || _session.SelectedElements.Any(e => e.Model.GroupId != groupId || e.Model.Locked || !e.Model.Visible)) return false;
        members = _session.Elements.Where(e => e.Model.GroupId == groupId).ToList();
        if (members.Count != _session.SelectedElements.Count) return false;
        bounds = members.Select(e => ElementGeometry.GetRotatedBoundsMm(e.Model)).Aggregate(RectMm.Union);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    // ───────────────────────── İsabet testleri ─────────────────────────

    /// <summary>Tek bir grubun çerçeve tutamacı mı?</summary>
    private bool TryHitGroupHandle(Point p, out ResizeHandle handle)
    {
        handle = default;
        if (!TryGetGroupSelection(out _, out var bounds)) return false;
        bool small = bounds.Width * Scale < 30 || bounds.Height * Scale < 30;
        foreach (var (h, point) in HandlePoints(RectCornersDip(bounds)))
        {
            if (small && h is ResizeHandle.Top or ResizeHandle.Bottom or ResizeHandle.Left or ResizeHandle.Right) continue;
            if (Math.Abs(point.X - p.X) <= HandleSize / 2 + 3 && Math.Abs(point.Y - p.Y) <= HandleSize / 2 + 3)
            {
                handle = h;
                return true;
            }
        }
        return false;
    }

    private bool TryHitHandle(Point p, out ResizeHandle handle, out bool rotate)
    {
        handle = default;
        rotate = false;
        if (_session is not { SelectedElements.Count: 1 } || _session.SelectedElements[0] is not { } vm || vm.Model.Locked || !vm.Model.Visible) return false;
        if ((RotateHandlePoint(vm.Model) - p).Length <= 8)
        {
            rotate = true;
            return true;
        }
        bool small = vm.Model.Width * Scale < 30 || vm.Model.Height * Scale < 30;
        foreach (var (h, point) in HandlePoints(vm.Model))
        {
            if (small && h is ResizeHandle.Top or ResizeHandle.Bottom or ResizeHandle.Left or ResizeHandle.Right) continue;
            if (Math.Abs(point.X - p.X) <= HandleSize / 2 + 3 && Math.Abs(point.Y - p.Y) <= HandleSize / 2 + 3)
            {
                handle = h;
                return true;
            }
        }
        return false;
    }

    private GuideLine? HitGuide(Point p)
    {
        if (_session is null) return null;
        foreach (var guide in _session.Document.Guides)
        {
            double pos = MarginDip + guide.Position * Scale;
            double d = guide.Orientation == GuideOrientation.Vertical ? Math.Abs(p.X - pos) : Math.Abs(p.Y - pos);
            if (d <= 3.5) return guide;
        }
        return null;
    }

    private Cursor CursorForHandle(ResizeHandle handle, double rotation)
    {
        double baseAngle = handle switch
        {
            ResizeHandle.Top or ResizeHandle.Bottom => 90,
            ResizeHandle.TopRight or ResizeHandle.BottomLeft => 45,
            ResizeHandle.TopLeft or ResizeHandle.BottomRight => 135,
            _ => 0,
        };
        double angle = ((baseAngle - rotation) % 180 + 180) % 180;
        return angle switch
        {
            < 22.5 or >= 157.5 => Cursors.SizeWE,
            < 67.5 => Cursors.SizeNESW,
            < 112.5 => Cursors.SizeNS,
            _ => Cursors.SizeNWSE,
        };
    }

    // ───────────────────────── Fare ─────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_session is null || ViewModel is null) return;
        Focus();
        var p = e.GetPosition(this);
        var mm = DipToMm(p);
        bool additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (e.ClickCount == 2)
        {
            var hit = _session.HitTest(mm.X, mm.Y, 2 / Scale);
            if (hit is not null)
            {
                if (InlineTextEditing.CanEdit(hit.Model))
                {
                    BeginTextEdit(hit);
                }
                else
                {
                    _session.Select(hit);
                    ViewModel.RequestFocusContentEditor();
                }
                e.Handled = true;
            }
            return;
        }

        _startPoint = p;
        _startMm = mm;
        _snapLines = [];

        if (TryHitHandle(p, out var handle, out bool rotate))
        {
            _activeElement = _session.SelectedElements[0];
            var m = _activeElement.Model;
            _resizeStart = (m.X, m.Y, m.Width, m.Height, m.Rotation);
            _resizeHandle = handle;
            _mode = rotate ? DragMode.Rotate : DragMode.Resize;
            _transaction = _session.BeginTransaction(rotate ? $"{m.DisplayName} döndürüldü" : $"{m.DisplayName} boyutlandırıldı");
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (TryHitGroupHandle(p, out var groupHandle) && TryGetGroupSelection(out var members, out var groupBounds))
        {
            _groupStarts = members.Select(vm => TransformMath.GroupMemberStart.Of(vm.Model)).ToList();
            _groupStartBounds = groupBounds;
            _resizeHandle = groupHandle;
            _mode = DragMode.GroupResize;
            _transaction = _session.BeginTransaction("Grup boyutlandırıldı");
            CaptureMouse();
            e.Handled = true;
            return;
        }

        var guide = HitGuide(p);
        var element = _session.HitTest(mm.X, mm.Y, 2 / Scale);
        if (guide is not null && (element is null || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || !element.IsSelected))
        {
            _guide = guide;
            _mode = DragMode.Guide;
            _transaction = _session.BeginTransaction("Kılavuz çizgisi taşındı");
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (element is not null)
        {
            if (additive) _session.Select(element, toggle: true);
            else if (!element.IsSelected) _session.Select(element);
            if (element.IsSelected) _mode = DragMode.Pending;
        }
        else
        {
            if (!additive) _session.ClearSelection();
            _mode = DragMode.RubberBand;
            _rubberBand = new RectMm(mm.X, mm.Y, 0, 0);
        }
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton == MouseButton.Middle && ScrollHost is { } sv)
        {
            _mode = DragMode.Pan;
            _panStart = e.GetPosition(sv);
            _panOffsetStart = new Point(sv.HorizontalOffset, sv.VerticalOffset);
            Cursor = Cursors.SizeAll;
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_session is null || ViewModel is null) return;
        var p = e.GetPosition(this);
        var mm = DipToMm(p);
        PointerMoved?.Invoke(this, mm);
        ViewModel.CursorText = $"X {mm.X:0.0}  Y {mm.Y:0.0} mm";

        switch (_mode)
        {
            case DragMode.None:
                UpdateHover(p, mm);
                return;
            case DragMode.Pan:
                if (ScrollHost is { } sv)
                {
                    var now = e.GetPosition(sv);
                    sv.ScrollToHorizontalOffset(_panOffsetStart.X - (now.X - _panStart.X));
                    sv.ScrollToVerticalOffset(_panOffsetStart.Y - (now.Y - _panStart.Y));
                }
                return;
            case DragMode.Pending:
                if ((p - _startPoint).Length >= DragThreshold) BeginMove();
                return;
            case DragMode.Move:
                UpdateMove(mm, e);
                break;
            case DragMode.Resize:
                UpdateResize(mm);
                break;
            case DragMode.GroupResize:
                UpdateGroupResize(mm);
                break;
            case DragMode.Rotate:
                if (_activeElement is { } vm)
                {
                    var m = vm.Model;
                    m.Rotation = TransformMath.RotationFromPointer(m.X + m.Width / 2, m.Y + m.Height / 2, mm.X, mm.Y,
                        snap: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                    ViewModel.StatusMessage = $"Döndürme: {m.Rotation:0.#}°" + (m is BarcodeElement ? " (barkodlar 90° adımlarla döner)" : " (Shift: 15° adım)");
                }
                break;
            case DragMode.RubberBand:
                _rubberBand = RectMm.FromPoints(_startMm.X, _startMm.Y, mm.X, mm.Y);
                break;
            case DragMode.Guide:
                if (_guide is not null)
                    _guide.Position = Math.Round(_guide.Orientation == GuideOrientation.Vertical ? mm.X : mm.Y, 1);
                break;
            case DragMode.NewGuide:
                _newGuidePosition = Math.Round(_newGuideOrientation == GuideOrientation.Vertical ? mm.X : mm.Y, 1);
                break;
        }
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateHover(Point p, (double X, double Y) mm)
    {
        if (_session is null) return;
        if (TryHitHandle(p, out var handle, out bool rotate))
        {
            Cursor = rotate ? Cursors.Hand : CursorForHandle(handle, _session.SelectedElements[0].Model.Rotation);
            SetHover(null);
            return;
        }
        if (TryHitGroupHandle(p, out var groupHandle))
        {
            Cursor = CursorForHandle(groupHandle, 0);
            SetHover(null);
            return;
        }
        if (HitGuide(p) is { } guide)
        {
            Cursor = guide.Orientation == GuideOrientation.Vertical ? Cursors.SizeWE : Cursors.SizeNS;
            SetHover(null);
            return;
        }
        var element = _session.HitTest(mm.X, mm.Y, 2 / Scale);
        Cursor = element is null ? Cursors.Arrow : element.Model.Locked ? Cursors.No : Cursors.SizeAll;
        SetHover(element);
    }

    private void SetHover(ElementViewModel? vm)
    {
        if (ReferenceEquals(_hover, vm)) return;
        _hover = vm;
        InvalidateVisual();
    }

    private void BeginMove()
    {
        if (_session is null) return;
        _moveStarts.Clear();
        foreach (var vm in _session.SelectedElements)
            if (!vm.Model.Locked) _moveStarts.Add((vm.Model, vm.Model.X, vm.Model.Y));
        if (_moveStarts.Count == 0)
        {
            _mode = DragMode.None;
            if (ViewModel is not null) ViewModel.StatusMessage = "Kilitli öğe taşınamaz. Kilidi sağ panelden veya sağ tık menüsünden açın.";
            return;
        }
        _moveBounds = _moveStarts.Select(m => ElementGeometry.GetRotatedBoundsMm(m.Element)).Aggregate(RectMm.Union);
        var moving = _moveStarts.Select(m => m.Element).ToHashSet();
        _otherBounds = _session.Elements.Where(e => e.Model.Visible && !moving.Contains(e.Model))
            .Select(e => ElementGeometry.GetRotatedBoundsMm(e.Model)).ToList();
        _transaction = _session.BeginTransaction(_moveStarts.Count == 1 ? $"{_moveStarts[0].Element.DisplayName} taşındı" : $"{_moveStarts.Count} öğe taşındı");
        _mode = DragMode.Move;
    }

    private SnapEngine.Options SnapOptions(bool disabled)
    {
        var settings = ViewModel!.Settings;
        return new SnapEngine.Options
        {
            ThresholdMm = 6 / Scale,
            SnapToGrid = settings.SnapToGrid && !disabled,
            GridSizeMm = settings.GridSizeMm,
            SnapToObjects = settings.SnapToObjects && !disabled,
            SnapToGuides = !disabled,
        };
    }

    private void UpdateMove((double X, double Y) mm, MouseEventArgs e)
    {
        if (_session is null) return;
        double dx = mm.X - _startMm.X, dy = mm.Y - _startMm.Y;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (Math.Abs(dx) > Math.Abs(dy)) dy = 0;
            else dx = 0;
        }
        bool noSnap = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var snap = SnapEngine.SnapMove(_moveBounds, dx, dy, _session.Document.Label, _session.Document.Guides, _otherBounds, SnapOptions(noSnap));
        _snapLines = snap.Lines;
        foreach (var (element, x, y) in _moveStarts)
        {
            element.X = Math.Round(x + snap.DeltaX, 3);
            element.Y = Math.Round(y + snap.DeltaY, 3);
        }
        if (ViewModel is not null)
            ViewModel.StatusMessage = $"Konum: X {_moveBounds.X + snap.DeltaX:0.0} mm, Y {_moveBounds.Y + snap.DeltaY:0.0} mm (Shift: eksene kilitle, Alt: yakalamasız)";
    }

    private void UpdateResize((double X, double Y) mm)
    {
        if (_activeElement is null || _session is null) return;
        var model = _activeElement.Model;
        bool defaultAspect = model is ImageElement || model is BarcodeElement { Is2D: true };
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool keepAspect = defaultAspect ^ shift;
        double dx = mm.X - _startMm.X, dy = mm.Y - _startMm.Y;
        var r = TransformMath.Resize(_resizeStart, _resizeHandle, dx, dy, keepAspect);

        // Döndürülmemiş öğelerde hareket eden kenarları ızgaraya/kılavuzlara yakala.
        _snapLines = [];
        bool noSnap = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        if (_resizeStart.Rotation == 0 && !keepAspect && !noSnap)
        {
            var options = SnapOptions(false);
            var label = _session.Document.Label;
            var others = _session.Elements.Where(e => e.Model.Visible && !ReferenceEquals(e, _activeElement))
                .Select(e => ElementGeometry.GetRotatedBoundsMm(e.Model)).ToList();
            var vTargets = new List<double> { 0, label.WidthMm / 2, label.WidthMm };
            var hTargets = new List<double> { 0, label.HeightMm / 2, label.HeightMm };
            foreach (var o in others)
            {
                vTargets.AddRange([o.X, o.Right]);
                hTargets.AddRange([o.Y, o.Bottom]);
            }
            foreach (var g in _session.Document.Guides)
                (g.Orientation == GuideOrientation.Vertical ? vTargets : hTargets).Add(g.Position);
            var lines = new List<SnapLine>();
            if (_resizeHandle is ResizeHandle.Right or ResizeHandle.TopRight or ResizeHandle.BottomRight)
            {
                var (right, line) = SnapEngine.SnapValue(r.X + r.Width, GuideOrientation.Vertical, vTargets, options);
                r.Width = Math.Max(TransformMath.MinSizeMm, right - r.X);
                if (line is { } l) lines.Add(l);
            }
            if (_resizeHandle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft)
            {
                double right = r.X + r.Width;
                var (left, line) = SnapEngine.SnapValue(r.X, GuideOrientation.Vertical, vTargets, options);
                r.X = Math.Min(left, right - TransformMath.MinSizeMm);
                r.Width = right - r.X;
                if (line is { } l) lines.Add(l);
            }
            if (_resizeHandle is ResizeHandle.Bottom or ResizeHandle.BottomLeft or ResizeHandle.BottomRight)
            {
                var (bottom, line) = SnapEngine.SnapValue(r.Y + r.Height, GuideOrientation.Horizontal, hTargets, options);
                r.Height = Math.Max(TransformMath.MinSizeMm, bottom - r.Y);
                if (line is { } l) lines.Add(l);
            }
            if (_resizeHandle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight)
            {
                double bottom = r.Y + r.Height;
                var (top, line) = SnapEngine.SnapValue(r.Y, GuideOrientation.Horizontal, hTargets, options);
                r.Y = Math.Min(top, bottom - TransformMath.MinSizeMm);
                r.Height = bottom - r.Y;
                if (line is { } l) lines.Add(l);
            }
            _snapLines = lines;
        }
        model.SetBounds(Math.Round(r.X, 3), Math.Round(r.Y, 3), Math.Round(r.Width, 3), Math.Round(r.Height, 3));
        if (ViewModel is not null)
            ViewModel.StatusMessage = $"Boyut: {model.Width:0.0} × {model.Height:0.0} mm" + (keepAspect ? " (oran korunuyor; Shift ile serbest)" : " (Shift: oranı koru)");
    }

    /// <summary>Grup çerçevesi sürükleniyor: üyeler orantılı ölçeklenir (köşelerde oran korunur; Shift ile serbest).</summary>
    private void UpdateGroupResize((double X, double Y) mm)
    {
        if (_groupStarts.Count == 0) return;
        bool keepAspect = !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        double dx = mm.X - _startMm.X, dy = mm.Y - _startMm.Y;
        var b = _groupStartBounds;
        var r = TransformMath.Resize((b.X, b.Y, b.Width, b.Height, 0), _resizeHandle, dx, dy, keepAspect);
        TransformMath.ScaleGroup(_groupStarts, b, new RectMm(r.X, r.Y, r.Width, r.Height));
        if (ViewModel is not null)
            ViewModel.StatusMessage = $"Grup: {r.Width:0.0} × {r.Height:0.0} mm" + (keepAspect ? " (oran korunuyor; Shift ile serbest)" : " (serbest)");
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        FinishDrag(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton == MouseButton.Middle && _mode == DragMode.Pan)
        {
            _mode = DragMode.None;
            Cursor = Cursors.Arrow;
            ReleaseMouseCapture();
        }
    }

    private void FinishDrag(Point p)
    {
        if (_session is null) return;
        var mode = _mode;
        _mode = DragMode.None;
        switch (mode)
        {
            case DragMode.RubberBand when _rubberBand is { } band:
                if (band.Width > 0.3 || band.Height > 0.3)
                    _session.SelectMany(_session.ElementsInRect(band), add: Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                break;
            case DragMode.Guide when _guide is not null:
                if (!IsInsideViewport(p)) _session.RemoveGuide(_guide);
                break;
            case DragMode.NewGuide when _newGuidePosition is { } position:
                if (IsInsideViewport(p)) _session.AddGuide(_newGuideOrientation, position);
                break;
        }
        _rubberBand = null;
        _guide = null;
        _newGuidePosition = null;
        _snapLines = [];
        _activeElement = null;
        _groupStarts = [];
        _transaction?.Dispose();
        _transaction = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_mode is not DragMode.None and not DragMode.Pan) FinishDrag(e.GetPosition(this));
    }

    /// <summary>Escape: süren sürüklemeyi iptal eder ve başlangıç durumuna döner.</summary>
    public void CancelDrag()
    {
        if (_mode == DragMode.Move)
            foreach (var (element, x, y) in _moveStarts) element.SetBounds(x, y, element.Width, element.Height);
        if (_mode is DragMode.Resize or DragMode.Rotate && _activeElement is { } vm)
        {
            vm.Model.SetBounds(_resizeStart.X, _resizeStart.Y, _resizeStart.Width, _resizeStart.Height);
            vm.Model.Rotation = _resizeStart.Rotation;
        }
        if (_mode == DragMode.GroupResize)
        {
            foreach (var start in _groupStarts)
            {
                start.Element.SetBounds(start.X, start.Y, start.Width, start.Height);
                if (start.Element is TextElement text && start.FontSize > 0) text.FontSize = start.FontSize;
            }
            _groupStarts = [];
        }
        _mode = DragMode.None;
        _rubberBand = null;
        _newGuidePosition = null;
        _snapLines = [];
        _transaction?.Dispose();
        _transaction = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }

    public bool IsDragging => _mode is not DragMode.None;

    // ───────────────────────── Tuvalde yazı düzenleme ─────────────────────────

    /// <summary>Bir metin öğesi tuvalde düzenleniyor mu?</summary>
    public bool IsTextEditing => _textEditor is not null;

    private void OnInlineTextEditRequested(object? sender, ElementViewModel vm) =>
        Dispatcher.BeginInvoke(() => BeginTextEdit(vm), DispatcherPriority.Background);

    /// <summary>
    /// Metin öğesinin üstünde, aynı yazı tipi ve hizayla bir metin kutusu açar (yakınlaştırma ve döndürmeyi izler).
    /// Enter bitirir (Shift+Enter yeni satır), Esc vazgeçer, başka yere tıklamak bitirir. Ekrandaki görünüm yaklaşıktır;
    /// bittiğinde tuvalde yine baskıyla birebir aynı 1-bit çıktı görünür.
    /// </summary>
    public void BeginTextEdit(ElementViewModel vm)
    {
        if (_session is null || ViewModel is null || vm.Model is not TextElement text || !InlineTextEditing.CanEdit(text)) return;
        if (!_session.Elements.Contains(vm)) return;
        CommitTextEdit();
        CancelDrag();
        if (!vm.IsSelected) _session.Select(vm);
        _editingVm = vm;
        var box = new TextBox
        {
            Text = text.Text,
            AcceptsReturn = true,
            AcceptsTab = false,
            IsUndoEnabled = true,
            MinWidth = 0,
            MinHeight = 0,
            TextWrapping = text.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            FontFamily = new FontFamily(text.FontFamily),
            FontWeight = text.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = text.Italic ? FontStyles.Italic : FontStyles.Normal,
            TextAlignment = text.HorizontalAlignment switch
            {
                TextHorizontalAlignment.Center => TextAlignment.Center,
                TextHorizontalAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            },
            VerticalContentAlignment = text.VerticalAlignment switch
            {
                TextVerticalAlignment.Middle => VerticalAlignment.Center,
                TextVerticalAlignment.Bottom => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Top,
            },
            Foreground = text.TextColor == InkColor.White && text.Background == FillStyle.Black ? Brushes.White : Brushes.Black,
            Background = text.Background == FillStyle.Black ? Brushes.Black : Brushes.White,
            BorderBrush = Res("Brush.Selection"),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(0),
            CaretBrush = text.Background == FillStyle.Black ? Brushes.White : Brushes.Black,
            ToolTip = "Enter: bitir • Shift+Enter: yeni satır • Esc: vazgeç",
        };
        ContextMenuService.SetIsEnabled(box, false);
        box.PreviewKeyDown += OnTextEditorKeyDown;
        box.LostKeyboardFocus += OnTextEditorLostFocus;
        _textEditor = box;
        AddVisualChild(box);
        // Odak alamayan bir yere (boş panel, cetvel) tıklamak da düzenlemeyi bitirsin.
        _editWindow = Window.GetWindow(this);
        if (_editWindow is not null) _editWindow.PreviewMouseDown += OnWindowPreviewMouseDown;
        InvalidateMeasure();
        InvalidateVisual();
        ViewModel.StatusMessage = "Metni yazın: Enter bitirir, Shift+Enter yeni satır, Esc vazgeçer.";
        Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(_textEditor, box)) return;
            box.Focus();
            Keyboard.Focus(box);
            box.SelectAll();
        }, DispatcherPriority.Input);
    }

    /// <summary>Düzenlemeyi bitirir; metin değiştiyse tek geri alma adımıyla yazılır.</summary>
    public void CommitTextEdit()
    {
        if (_textEditor is not { } box || _editingVm is not { } vm) return;
        string text = box.Text;
        CloseTextEditor();
        ViewModel?.CommitInlineText(vm, text);
    }

    /// <summary>Düzenlemeden vazgeçer (öğe değişmez).</summary>
    public void CancelTextEdit()
    {
        CloseTextEditor();
        if (ViewModel is not null) ViewModel.StatusMessage = "Düzenlemeden vazgeçildi.";
    }

    private void CloseTextEditor()
    {
        if (_textEditor is not { } box) return;
        _textEditor = null;
        _editingVm = null;
        box.PreviewKeyDown -= OnTextEditorKeyDown;
        box.LostKeyboardFocus -= OnTextEditorLostFocus;
        if (_editWindow is not null)
        {
            _editWindow.PreviewMouseDown -= OnWindowPreviewMouseDown;
            _editWindow = null;
        }
        // Odak hâlâ düzenleyicideyse (Enter/Esc) tuvale döner; başka yere geçtiyse dokunulmaz.
        if (ReferenceEquals(Keyboard.FocusedElement, box)) Focus();
        RemoveVisualChild(box);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_textEditor is { IsMouseOver: false }) CommitTextEdit();
    }

    private void OnTextEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelTextEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            CommitTextEdit();
            e.Handled = true;
        }
    }

    private void OnTextEditorLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitTextEdit();

    private bool IsInsideViewport(Point surfacePoint)
    {
        if (ScrollHost is not { } sv) return true;
        var p = TranslatePoint(surfacePoint, sv);
        return p.X >= 0 && p.Y >= 0 && p.X <= sv.ViewportWidth && p.Y <= sv.ViewportHeight;
    }

    /// <summary>Cetvelden sürüklenerek yeni kılavuz oluşturma.</summary>
    public void BeginNewGuide(GuideOrientation orientation)
    {
        if (_session is null) return;
        _newGuideOrientation = orientation;
        _newGuidePosition = null;
        _mode = DragMode.NewGuide;
        CaptureMouse();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_mode == DragMode.None) SetHover(null);
        PointerMoved?.Invoke(this, null);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (ViewModel is null || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            base.OnMouseWheel(e);
            return;
        }
        var p = e.GetPosition(this);
        double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        ZoomAt(p, ViewModel.Zoom * factor);
        e.Handled = true;
    }

    /// <summary>Belirtilen ekran noktası sabit kalacak şekilde yakınlaştırır.</summary>
    public void ZoomAt(Point anchor, double newZoom)
    {
        if (ViewModel is null) return;
        var mm = DipToMm(anchor);
        var sv = ScrollHost;
        Point viewportPoint = sv is null ? anchor : TranslatePoint(anchor, sv);
        ViewModel.Zoom = Math.Clamp(newZoom, MainViewModel.MinZoom, MainViewModel.MaxZoom);
        if (sv is null) return;
        sv.UpdateLayout();
        var after = TranslatePoint(MmToDip(mm.X, mm.Y), sv);
        sv.ScrollToHorizontalOffset(sv.HorizontalOffset + (after.X - viewportPoint.X));
        sv.ScrollToVerticalOffset(sv.VerticalOffset + (after.Y - viewportPoint.Y));
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (_session is null || ViewModel is null) return;
        var mm = DipToMm(e.GetPosition(this));
        var hit = _session.HitTest(mm.X, mm.Y, 2 / Scale);
        if (hit is not null && !hit.IsSelected) _session.Select(hit);
        if (hit is null && _session.SelectedElements.Count == 0)
        {
            ContextMenu = ContextMenus.ForCanvas(ViewModel, mm);
        }
        else
        {
            ContextMenu = ContextMenus.ForSelection(ViewModel);
        }
        ContextMenu.PlacementTarget = this;
        ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    // ───────────────────────── Sürükle-bırak ─────────────────────────

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (ViewModel is null || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var mm = DipToMm(e.GetPosition(this));
        ViewModel.HandleFileDrop(files, mm.X, mm.Y);
        e.Handled = true;
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent is not null and not T) parent = VisualTreeHelper.GetParent(parent);
        return parent as T;
    }
}
