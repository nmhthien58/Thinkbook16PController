using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Tb16p.Core.Tuning;
using Tb16p.Ui.Localization;
using Wpf.Ui.Appearance;

namespace Tb16p.Ui.Controls;

/// <summary>
/// Drag-and-drop fan curve chart: rpm on the left axis, temperature along the bottom.
/// Points move freely on both axes (snapped to 1 °C / 100 rpm) but stay ordered by
/// temperature and never let the speed drop as the temperature rises.
/// Double-click empty space adds a point, right-click a point removes it.
/// Every edit writes a new list to <see cref="Points"/> (two-way bindable).
/// </summary>
public sealed class FanCurveChart : FrameworkElement
{
    private const int AxisMinRpm = 1000;
    private const int AxisMaxRpm = 6000;
    private const int RpmSnap = 100;
    private const double HitRadius = 11;
    private const double PointRadius = 6;
    private const double PadLeft = 52, PadTop = 14, PadRight = 16, PadBottom = 40;

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(List<FanPoint>), typeof(FanCurveChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPointsChanged));

    public static readonly DependencyProperty MaxOverrideProperty = DependencyProperty.Register(
        nameof(MaxOverride), typeof(bool), typeof(FanCurveChart),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((FanCurveChart)d).CancelDrag()));

    public static readonly DependencyProperty LiveTempCProperty = DependencyProperty.Register(
        nameof(LiveTempC), typeof(int?), typeof(FanCurveChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LiveRpmProperty = DependencyProperty.Register(
        nameof(LiveRpm), typeof(int?), typeof(FanCurveChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly List<FanPoint> _points = [];
    private List<FanPoint>? _published;
    private int _dragIndex = -1;
    private int _hoverIndex = -1;

    public FanCurveChart()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        MinHeight = 260;
        IsEnabledChanged += (_, _) => { CancelDrag(); InvalidateVisual(); };
        Loaded += (_, _) =>
        {
            ApplicationThemeManager.Changed += OnThemeChanged;
            Loc.Instance.LanguageChanged += InvalidateVisual;
        };
        Unloaded += (_, _) =>
        {
            ApplicationThemeManager.Changed -= OnThemeChanged;
            Loc.Instance.LanguageChanged -= InvalidateVisual;
        };
    }

    public List<FanPoint>? Points
    {
        get => (List<FanPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>When set, the chart shows the fans locked at full speed and ignores edits.</summary>
    public bool MaxOverride
    {
        get => (bool)GetValue(MaxOverrideProperty);
        set => SetValue(MaxOverrideProperty, value);
    }

    public int? LiveTempC
    {
        get => (int?)GetValue(LiveTempCProperty);
        set => SetValue(LiveTempCProperty, value);
    }

    public int? LiveRpm
    {
        get => (int?)GetValue(LiveRpmProperty);
        set => SetValue(LiveRpmProperty, value);
    }

    private bool Editable => IsEnabled && !MaxOverride;

    private void OnThemeChanged(ApplicationTheme theme, Color accent) => InvalidateVisual();

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (FanCurveChart)d;
        // Our own edits come back through the binding; only reload lists from outside.
        if (ReferenceEquals(e.NewValue, chart._published))
            return;
        chart._points.Clear();
        if (e.NewValue is List<FanPoint> points)
            chart._points.AddRange(points.Select(p => p.Clone()).OrderBy(p => p.TempC));
        chart.CancelDrag();
        chart.InvalidateVisual();
    }

    private void Publish()
    {
        _published = _points.Select(p => p.Clone()).ToList();
        SetCurrentValue(PointsProperty, _published);
    }

    private void CancelDrag()
    {
        if (_dragIndex >= 0)
            ReleaseMouseCapture();
        _dragIndex = _hoverIndex = -1;
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ geometry

    private Rect Plot => new(PadLeft, PadTop, Math.Max(10, ActualWidth - PadLeft - PadRight), Math.Max(10, ActualHeight - PadTop - PadBottom));

    private double X(double tempC) => Plot.Left + (tempC - FanCurve.MinTempC) / (FanCurve.MaxTempC - FanCurve.MinTempC) * Plot.Width;
    private double Y(double rpm) => Plot.Bottom - (rpm - AxisMinRpm) / (AxisMaxRpm - AxisMinRpm) * Plot.Height;

    private int TempAt(double x) =>
        (int)Math.Round(FanCurve.MinTempC + (x - Plot.Left) / Plot.Width * (FanCurve.MaxTempC - FanCurve.MinTempC));

    private int RpmAt(double y)
    {
        var rpm = AxisMinRpm + (Plot.Bottom - y) / Plot.Height * (AxisMaxRpm - AxisMinRpm);
        return (int)Math.Round(rpm / RpmSnap) * RpmSnap;
    }

    private int HitTest(Point location)
    {
        for (var i = 0; i < _points.Count; i++)
        {
            var dx = X(_points[i].TempC) - location.X;
            var dy = Y(_points[i].Rpm) - location.Y;
            if (dx * dx + dy * dy <= HitRadius * HitRadius)
                return i;
        }
        return -1;
    }

    // ------------------------------------------------------------------ editing

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!Editable)
            return;
        Focus();
        var location = e.GetPosition(this);
        var index = HitTest(location);
        if (e.ClickCount == 2 && index < 0)
        {
            AddPoint(location);
        }
        else if (index >= 0)
        {
            _dragIndex = index;
            CaptureMouse();
        }
        e.Handled = true;
    }

    private void AddPoint(Point location)
    {
        if (_points.Count >= FanCurve.MaxPoints)
            return;
        var temp = Math.Clamp(TempAt(location.X), FanCurve.MinTempC, FanCurve.MaxTempC);
        if (_points.Any(p => p.TempC == temp))
            return;
        var index = _points.FindIndex(p => p.TempC > temp);
        if (index < 0)
            index = _points.Count;
        var point = new FanPoint(temp, RpmAt(location.Y));
        _points.Insert(index, point);
        Constrain(index, point.TempC, point.Rpm);
        _hoverIndex = index;
        InvalidateVisual();
        Publish();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (!Editable)
            return;
        var index = HitTest(e.GetPosition(this));
        if (index >= 0 && _points.Count > FanCurve.MinPoints)
        {
            _points.RemoveAt(index);
            _hoverIndex = -1;
            InvalidateVisual();
            Publish();
        }
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var location = e.GetPosition(this);
        if (_dragIndex >= 0)
        {
            Constrain(_dragIndex, TempAt(location.X), RpmAt(location.Y));
            InvalidateVisual();
            return;
        }

        var hover = Editable ? HitTest(location) : -1;
        Cursor = hover >= 0 ? Cursors.Hand : Cursors.Cross;
        if (hover != _hoverIndex)
        {
            _hoverIndex = hover;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragIndex < 0)
            return;
        _hoverIndex = _dragIndex;
        _dragIndex = -1;
        ReleaseMouseCapture();
        InvalidateVisual();
        Publish();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragIndex < 0 && _hoverIndex >= 0)
        {
            _hoverIndex = -1;
            InvalidateVisual();
        }
    }

    /// <summary>Moves point <paramref name="index"/> as close as allowed to the requested spot.</summary>
    private void Constrain(int index, int tempC, int rpm)
    {
        var minTemp = index > 0 ? _points[index - 1].TempC + 1 : FanCurve.MinTempC;
        var maxTemp = index < _points.Count - 1 ? _points[index + 1].TempC - 1 : FanCurve.MaxTempC;
        var minRpm = index > 0 ? _points[index - 1].Rpm : FanCurve.MinRpm;
        var maxRpm = index < _points.Count - 1 ? _points[index + 1].Rpm : FanCurve.MaxRpm;

        _points[index].TempC = Math.Clamp(tempC, minTemp, Math.Max(minTemp, maxTemp));
        _points[index].Rpm = Math.Clamp(rpm, minRpm, Math.Max(minRpm, maxRpm));
    }

    // ------------------------------------------------------------------ rendering

    private Brush Res(string key, Color fallback) => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    private static Brush WithOpacity(Brush brush, double opacity)
    {
        var copy = brush.CloneCurrentValue();
        copy.Opacity = opacity;
        copy.Freeze();
        return copy;
    }

    private FormattedText Text(string text, double size, Brush brush, bool bold = false) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnRender(DrawingContext dc)
    {
        var plot = Plot;
        var dim = IsEnabled ? 1.0 : 0.45;
        var text = WithOpacity(Res("TextFillColorSecondaryBrush", Colors.Gray), dim);
        var grid = new Pen(WithOpacity(Res("TextFillColorPrimaryBrush", Colors.Gray), 0.08), 1);
        var axis = new Pen(WithOpacity(Res("TextFillColorPrimaryBrush", Colors.Gray), 0.25), 1);
        var accent = WithOpacity(Res("SystemAccentColorPrimaryBrush", Color.FromRgb(0, 120, 215)), dim);
        var critical = Res("SystemFillColorCriticalBrush", Color.FromRgb(196, 43, 28));
        var surface = Res("SolidBackgroundFillColorBaseBrush", Colors.White);

        // Transparent background so empty space gets mouse events (double-click to add).
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        // Speeds the fans cannot do are shaded.
        var shade = WithOpacity(Res("TextFillColorPrimaryBrush", Colors.Gray), 0.05);
        dc.DrawRectangle(shade, null, new Rect(plot.Left, Y(AxisMaxRpm), plot.Width, Y(FanCurve.MaxRpm) - Y(AxisMaxRpm)));
        dc.DrawRectangle(shade, null, new Rect(plot.Left, Y(FanCurve.MinRpm), plot.Width, Y(AxisMinRpm) - Y(FanCurve.MinRpm)));

        for (var t = FanCurve.MinTempC; t <= FanCurve.MaxTempC; t += 5)
        {
            dc.DrawLine(grid, new Point(X(t), plot.Top), new Point(X(t), plot.Bottom));
            if (t % 10 == 0)
            {
                var label = Text($"{t}", 11, text);
                dc.DrawText(label, new Point(X(t) - label.Width / 2, plot.Bottom + 4));
            }
        }
        for (var r = AxisMinRpm; r <= AxisMaxRpm; r += 500)
        {
            dc.DrawLine(grid, new Point(plot.Left, Y(r)), new Point(plot.Right, Y(r)));
            if (r % 1000 == 0)
            {
                var label = Text($"{r}", 11, text);
                dc.DrawText(label, new Point(plot.Left - label.Width - 6, Y(r) - label.Height / 2));
            }
        }
        dc.DrawRectangle(null, axis, plot);
        var axisTitle = Text(Loc.T("Chart_Temp"), 12, text);
        dc.DrawText(axisTitle, new Point(plot.Left + (plot.Width - axisTitle.Width) / 2, plot.Bottom + 20));
        dc.DrawText(Text("rpm", 11, text), new Point(4, 0));

        if (_points.Count > 0)
            DrawCurve(dc, plot, accent, surface);
        if (MaxOverride)
            DrawMaxOverride(dc, plot, critical, surface);
        DrawLiveMarker(dc, plot, critical);
    }

    private void DrawCurve(DrawingContext dc, Rect plot, Brush accent, Brush surface)
    {
        var line = new List<Point> { new(plot.Left, Y(_points[0].Rpm)) };
        line.AddRange(_points.Select(p => new Point(X(p.TempC), Y(p.Rpm))));
        line.Add(new Point(plot.Right, Y(_points[^1].Rpm)));

        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(plot.Left, plot.Bottom), true, true);
            ctx.PolyLineTo(line, true, true);
            ctx.LineTo(new Point(plot.Right, plot.Bottom), true, true);
        }
        dc.DrawGeometry(WithOpacity(accent, IsEnabled ? 0.18 : 0.08), null, area);

        var stroke = new StreamGeometry();
        using (var ctx = stroke.Open())
        {
            ctx.BeginFigure(line[0], false, false);
            ctx.PolyLineTo(line.Skip(1).ToList(), true, true);
        }
        dc.DrawGeometry(null, new Pen(accent, 2.5) { LineJoin = PenLineJoin.Round }, stroke);

        var active = Res("SystemFillColorCautionBrush", Color.FromRgb(230, 120, 0));
        var ring = new Pen(surface, 2);
        for (var i = 0; i < _points.Count; i++)
        {
            var isActive = Editable && (i == _dragIndex || i == _hoverIndex);
            var radius = isActive ? PointRadius + 2 : PointRadius;
            dc.DrawEllipse(isActive ? active : accent, ring, new Point(X(_points[i].TempC), Y(_points[i].Rpm)), radius, radius);
        }

        var labelIndex = _dragIndex >= 0 ? _dragIndex : _hoverIndex;
        if (labelIndex >= 0 && Editable)
        {
            var p = _points[labelIndex];
            var label = Text($"{p.TempC} °C · {p.Rpm} rpm", 12, Res("TextFillColorPrimaryBrush", Colors.Black), bold: true);
            var x = Math.Clamp(X(p.TempC) - label.Width / 2, plot.Left, plot.Right - label.Width);
            var y = Y(p.Rpm) - label.Height - 14;
            if (y < plot.Top)
                y = Y(p.Rpm) + 14;
            dc.DrawRoundedRectangle(surface, new Pen(active, 1), new Rect(x - 6, y - 3, label.Width + 12, label.Height + 6), 4, 4);
            dc.DrawText(label, new Point(x, y));
        }
    }

    private void DrawMaxOverride(DrawingContext dc, Rect plot, Brush critical, Brush surface)
    {
        var y = Y(FanCurve.MaxRpm);
        dc.DrawRectangle(WithOpacity(critical, 0.15), null, new Rect(plot.Left, y, plot.Width, plot.Bottom - y));
        dc.DrawLine(new Pen(critical, 3), new Point(plot.Left, y), new Point(plot.Right, y));

        var label = Text(Loc.F("Chart_MaxOverlay", FanCurve.MaxRpm), 15, critical, bold: true);
        var x = plot.Left + (plot.Width - label.Width) / 2;
        var ty = y + (plot.Bottom - y - label.Height) / 2;
        dc.DrawRoundedRectangle(surface, new Pen(critical, 1), new Rect(x - 10, ty - 5, label.Width + 20, label.Height + 10), 6, 6);
        dc.DrawText(label, new Point(x, ty));
    }

    private void DrawLiveMarker(DrawingContext dc, Rect plot, Brush critical)
    {
        if (LiveTempC is not { } temp)
            return;
        var x = Math.Clamp(X(temp), plot.Left, plot.Right);
        dc.DrawLine(new Pen(WithOpacity(critical, 0.8), 1.5) { DashStyle = DashStyles.Dash }, new Point(x, plot.Top), new Point(x, plot.Bottom));

        var label = Text(LiveRpm is { } rpm ? Loc.F("Chart_NowRpm", temp, rpm) : Loc.F("Chart_Now", temp), 11, critical);
        var tx = Math.Clamp(x + 5, plot.Left, plot.Right - label.Width);
        dc.DrawText(label, new Point(tx, plot.Top + 3));

        if (LiveRpm is { } live)
            dc.DrawEllipse(critical, null, new Point(x, Math.Clamp(Y(live), plot.Top, plot.Bottom)), 4, 4);
    }
}
