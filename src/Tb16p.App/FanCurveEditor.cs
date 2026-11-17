using System.Drawing.Drawing2D;
using Tb16p.Core.Tuning;

namespace Tb16p.App;

/// <summary>
/// Drag-and-drop fan curve chart: rpm on the left axis, temperature along the bottom.
/// Points move freely on both axes (snapped to 1 °C / 100 rpm) but stay ordered by
/// temperature and never let the speed drop as the temperature rises.
/// Double-click empty space adds a point, right-click a point removes it.
/// </summary>
internal sealed class FanCurveEditor : Control
{
    private const int AxisMinRpm = 1000;
    private const int AxisMaxRpm = 6000;
    private const int RpmSnap = 100;
    private const int HitRadius = 10;
    private const int PointRadius = 6;

    private readonly List<FanPoint> _points = [];
    private int _dragIndex = -1;
    private int _hoverIndex = -1;
    private int? _liveTempC;
    private int? _liveRpm;

    public FanCurveEditor()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Color.White;
        MinimumSize = new Size(560, 280);
        Cursor = Cursors.Cross;
    }

    /// <summary>Raised after every edit (drag, add, remove).</summary>
    public event Action? CurveChanged;

    public List<FanPoint> GetPoints() => _points.Select(p => p.Clone()).ToList();

    public void SetPoints(IEnumerable<FanPoint> points)
    {
        _points.Clear();
        _points.AddRange(points.Select(p => p.Clone()).OrderBy(p => p.TempC));
        _dragIndex = _hoverIndex = -1;
        Invalidate();
    }

    /// <summary>Current temperature and fan speed, drawn as a live marker.</summary>
    public void SetLive(int? tempC, int? rpm)
    {
        if (tempC == _liveTempC && rpm == _liveRpm)
            return;
        _liveTempC = tempC;
        _liveRpm = rpm;
        Invalidate();
    }

    // ------------------------------------------------------------------ geometry

    private Rectangle Plot => new(58, 14, Math.Max(10, Width - 58 - 18), Math.Max(10, Height - 14 - 40));

    private float X(double tempC) => Plot.Left + (float)((tempC - FanCurve.MinTempC) / (FanCurve.MaxTempC - FanCurve.MinTempC) * Plot.Width);
    private float Y(double rpm) => Plot.Bottom - (float)((rpm - AxisMinRpm) / (AxisMaxRpm - AxisMinRpm) * Plot.Height);

    private int TempAt(float x) =>
        (int)Math.Round(FanCurve.MinTempC + (x - Plot.Left) / Plot.Width * (FanCurve.MaxTempC - FanCurve.MinTempC));

    private int RpmAt(float y)
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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled)
            return;
        Focus();
        var index = HitTest(e.Location);
        if (e.Button == MouseButtons.Left && index >= 0)
        {
            _dragIndex = index;
            Capture = true;
        }
        else if (e.Button == MouseButtons.Right && index >= 0 && _points.Count > FanCurve.MinPoints)
        {
            _points.RemoveAt(index);
            _hoverIndex = -1;
            Invalidate();
            CurveChanged?.Invoke();
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (!Enabled || e.Button != MouseButtons.Left || HitTest(e.Location) >= 0 || _points.Count >= FanCurve.MaxPoints)
            return;

        var temp = Math.Clamp(TempAt(e.X), FanCurve.MinTempC, FanCurve.MaxTempC);
        if (_points.Any(p => p.TempC == temp))
            return;
        var index = _points.FindIndex(p => p.TempC > temp);
        if (index < 0)
            index = _points.Count;
        var point = new FanPoint(temp, RpmAt(e.Y));
        _points.Insert(index, point);
        Constrain(index, point.TempC, point.Rpm);
        _hoverIndex = index;
        Invalidate();
        CurveChanged?.Invoke();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragIndex >= 0)
        {
            Constrain(_dragIndex, TempAt(e.X), RpmAt(e.Y));
            Invalidate();
            return;
        }

        var hover = Enabled ? HitTest(e.Location) : -1;
        Cursor = hover >= 0 ? Cursors.Hand : Cursors.Cross;
        if (hover != _hoverIndex)
        {
            _hoverIndex = hover;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragIndex < 0)
            return;
        _hoverIndex = _dragIndex;
        _dragIndex = -1;
        Capture = false;
        Invalidate();
        CurveChanged?.Invoke();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragIndex < 0 && _hoverIndex >= 0)
        {
            _hoverIndex = -1;
            Invalidate();
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

    // ------------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        var plot = Plot;
        var dim = Enabled ? 255 : 110;

        using var gridPen = new Pen(Color.FromArgb(dim, 230, 230, 230));
        using var axisPen = new Pen(Color.FromArgb(dim, 150, 150, 150));
        using var labelBrush = new SolidBrush(Color.FromArgb(dim, 90, 90, 90));
        using var smallFont = new Font(Font.FontFamily, 8f);

        // Speeds outside what the fans can do are shaded.
        using (var outOfRange = new SolidBrush(Color.FromArgb(Enabled ? 40 : 20, 200, 200, 200)))
        {
            g.FillRectangle(outOfRange, plot.Left, Y(AxisMaxRpm), plot.Width, Y(FanCurve.MaxRpm) - Y(AxisMaxRpm));
            g.FillRectangle(outOfRange, plot.Left, Y(FanCurve.MinRpm), plot.Width, Y(AxisMinRpm) - Y(FanCurve.MinRpm));
        }

        for (var t = FanCurve.MinTempC; t <= FanCurve.MaxTempC; t += 5)
        {
            g.DrawLine(gridPen, X(t), plot.Top, X(t), plot.Bottom);
            if (t % 10 == 0)
                DrawCentered(g, $"{t}", smallFont, labelBrush, X(t), plot.Bottom + 4);
        }
        for (var r = AxisMinRpm; r <= AxisMaxRpm; r += 500)
        {
            g.DrawLine(gridPen, plot.Left, Y(r), plot.Right, Y(r));
            if (r % 1000 == 0)
            {
                var size = g.MeasureString($"{r}", smallFont);
                g.DrawString($"{r}", smallFont, labelBrush, plot.Left - size.Width - 4, Y(r) - size.Height / 2);
            }
        }
        g.DrawRectangle(axisPen, plot);
        DrawCentered(g, "Nhiệt độ (°C)", Font, labelBrush, plot.Left + plot.Width / 2f, plot.Bottom + 20);
        g.DrawString("rpm", smallFont, labelBrush, 4, 0);

        if (_points.Count > 0)
            DrawCurve(g, plot, dim);

        DrawLiveMarker(g, plot, smallFont);
    }

    private void DrawCurve(Graphics g, Rectangle plot, int dim)
    {
        var line = new List<PointF> { new(plot.Left, Y(_points[0].Rpm)) };
        line.AddRange(_points.Select(p => new PointF(X(p.TempC), Y(p.Rpm))));
        line.Add(new PointF(plot.Right, Y(_points[^1].Rpm)));

        var accent = Color.FromArgb(dim, 0, 120, 215);
        using (var fill = new SolidBrush(Color.FromArgb(Enabled ? 35 : 15, 0, 120, 215)))
        {
            var area = new List<PointF>(line) { new(plot.Right, plot.Bottom), new(plot.Left, plot.Bottom) };
            g.FillPolygon(fill, area.ToArray());
        }
        using (var pen = new Pen(accent, 2.5f))
            g.DrawLines(pen, line.ToArray());

        using var pointBrush = new SolidBrush(accent);
        using var activeBrush = new SolidBrush(Color.FromArgb(dim, 230, 80, 0));
        for (var i = 0; i < _points.Count; i++)
        {
            var active = i == _dragIndex || i == _hoverIndex;
            var radius = active ? PointRadius + 2 : PointRadius;
            var x = X(_points[i].TempC);
            var y = Y(_points[i].Rpm);
            g.FillEllipse(active ? activeBrush : pointBrush, x - radius, y - radius, radius * 2, radius * 2);
            g.DrawEllipse(Pens.White, x - radius, y - radius, radius * 2, radius * 2);
        }

        var labelIndex = _dragIndex >= 0 ? _dragIndex : _hoverIndex;
        if (labelIndex >= 0)
        {
            var p = _points[labelIndex];
            var text = $"{p.TempC} °C · {p.Rpm} rpm";
            using var font = new Font(Font, FontStyle.Bold);
            var size = g.MeasureString(text, font);
            var x = Math.Clamp(X(p.TempC) - size.Width / 2, plot.Left, plot.Right - size.Width);
            var y = Y(p.Rpm) - size.Height - 12;
            if (y < plot.Top)
                y = Y(p.Rpm) + 12;
            using var back = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
            g.FillRectangle(back, x - 3, y - 1, size.Width + 6, size.Height + 2);
            g.DrawString(text, font, activeBrush, x, y);
        }
    }

    private void DrawLiveMarker(Graphics g, Rectangle plot, Font font)
    {
        if (_liveTempC is not { } temp)
            return;
        var x = Math.Clamp(X(temp), plot.Left, plot.Right);
        using var pen = new Pen(Color.FromArgb(200, 200, 40, 40), 1.5f) { DashStyle = DashStyle.Dash };
        g.DrawLine(pen, x, plot.Top, x, plot.Bottom);

        var text = _liveRpm is { } rpm ? $"Hiện tại: {temp} °C · {rpm} rpm" : $"Hiện tại: {temp} °C";
        var size = g.MeasureString(text, font);
        var tx = Math.Clamp(x + 4, plot.Left, plot.Right - size.Width);
        using var brush = new SolidBrush(Color.FromArgb(200, 40, 40));
        g.DrawString(text, font, brush, tx, plot.Top + 2);

        if (_liveRpm is { } live)
        {
            var y = Math.Clamp(Y(live), plot.Top, plot.Bottom);
            g.FillEllipse(brush, x - 4, y - 4, 8, 8);
        }
    }

    private static void DrawCentered(Graphics g, string text, Font font, Brush brush, float centerX, float top)
    {
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, brush, centerX - size.Width / 2, top);
    }
}
