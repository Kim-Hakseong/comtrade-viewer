using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Ncv.App.ViewModels;
using Ncv.Core.Analysis;
using Ncv.Core.Model;

namespace Ncv.App.Controls;

/// <summary>
/// 파형 플롯 커스텀 드로잉 (DESIGN §5). 외부 차트 라이브러리 금지 — DrawingContext 직접 렌더.
/// 오버레이 모드: 단위 그룹별 공통 Y 스케일. 레인 모드: 채널당 밴드 + 개별 스케일.
/// 디지털 채널은 항상 하단 레인(0/1 스텝).
/// </summary>
public class WaveformPlotControl : Control
{
    private const double MarginLeft = 64;
    private const double MarginRight = 12;
    private const double MarginTop = 10;
    private const double MarginBottom = 26;
    private const double DigitalLaneHeight = 22;

    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#EDEBE6"));
    private static readonly IBrush AxisTextBrush = new SolidColorBrush(Color.Parse("#8B897F"));
    private static readonly IBrush PlotBackground = Brushes.White;
    private static readonly Pen BorderPen = new(new SolidColorBrush(Color.Parse("#DDDBD3")), 1);
    private static readonly Pen LaneSeparatorPen = new(new SolidColorBrush(Color.Parse("#EDEBE6")), 1);
    private static readonly Typeface AxisTypeface = new("Inter, sans-serif");
    private static readonly Pen TriggerPen = new(new SolidColorBrush(Color.Parse("#7A1020")), 1.4,
        dashStyle: new DashStyle(new double[] { 4, 3 }, 0));
    private static readonly IBrush TriggerBrush = new SolidColorBrush(Color.Parse("#7A1020"));
    private static readonly Pen Cursor1Pen = new(new SolidColorBrush(Color.Parse("#16171A")), 1.3);
    private static readonly Pen Cursor2Pen = new(new SolidColorBrush(Color.Parse("#1F6FEB")), 1.3);
    private static readonly IBrush Cursor1Brush = new SolidColorBrush(Color.Parse("#16171A"));
    private static readonly IBrush Cursor2Brush = new SolidColorBrush(Color.Parse("#1F6FEB"));

    private MainWindowViewModel? _vm;

    private enum DragMode
    {
        None,
        Pan,
        Cursor1,
        Cursor2,
    }

    private DragMode _drag = DragMode.None;
    private Point _lastPointer;

    public MainWindowViewModel? ViewModel
    {
        get => _vm;
        set
        {
            if (_vm is not null)
                _vm.PlotInvalidated -= InvalidateVisual;
            _vm = value;
            if (_vm is not null)
                _vm.PlotInvalidated += InvalidateVisual;
            InvalidateVisual();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        InvalidateVisual();
    }

    private Rect PlotRect => new(
        MarginLeft, MarginTop,
        Math.Max(10, Bounds.Width - MarginLeft - MarginRight),
        Math.Max(10, Bounds.Height - MarginTop - MarginBottom));

    private double XToTime(double x)
    {
        var r = PlotRect;
        return _vm is null ? 0 : _vm.ViewStart + (x - r.X) / r.Width * _vm.ViewSpan;
    }

    private double TimeToX(double t)
    {
        var r = PlotRect;
        return _vm is null ? 0 : r.X + (t - _vm.ViewStart) / _vm.ViewSpan * r.Width;
    }

    // ---- 포인터 상호작용 (C-05/C-06): 휠 줌(커서 중심), 드래그 팬, 커서 드래그 ----

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var vm = _vm;
        if (vm?.Record is null)
            return;

        double factor = e.Delta.Y > 0 ? 1.25 : 1 / 1.25;
        vm.ZoomAt(XToTime(e.GetPosition(this).X), factor);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var vm = _vm;
        if (vm?.Record is null)
            return;

        var p = e.GetPosition(this);
        _lastPointer = p;

        if (vm.CursorsActive)
        {
            double x1 = TimeToX(vm.Cursor1Time);
            double x2 = TimeToX(vm.Cursor2Time);
            if (Math.Abs(p.X - x1) <= 6)
                _drag = DragMode.Cursor1;
            else if (Math.Abs(p.X - x2) <= 6)
                _drag = DragMode.Cursor2;
            else
                _drag = DragMode.Pan;
        }
        else
        {
            _drag = DragMode.Pan;
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var vm = _vm;
        if (vm?.Record is null || _drag == DragMode.None)
        {
            UpdateHoverCursor(e);
            return;
        }

        var p = e.GetPosition(this);
        switch (_drag)
        {
            case DragMode.Pan:
                double dt = (p.X - _lastPointer.X) / PlotRect.Width * vm.ViewSpan;
                vm.PanBy(-dt);
                break;
            case DragMode.Cursor1:
                vm.Cursor1Time = Math.Clamp(XToTime(p.X), vm.ViewStart, vm.ViewStart + vm.ViewSpan);
                break;
            case DragMode.Cursor2:
                vm.Cursor2Time = Math.Clamp(XToTime(p.X), vm.ViewStart, vm.ViewStart + vm.ViewSpan);
                break;
        }

        _lastPointer = p;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = DragMode.None;
        e.Pointer.Capture(null);
    }

    private void UpdateHoverCursor(PointerEventArgs e)
    {
        var vm = _vm;
        if (vm?.Record is null || !vm.CursorsActive)
        {
            Cursor = Avalonia.Input.Cursor.Default;
            return;
        }

        double x = e.GetPosition(this).X;
        bool near = Math.Abs(x - TimeToX(vm.Cursor1Time)) <= 6 || Math.Abs(x - TimeToX(vm.Cursor2Time)) <= 6;
        Cursor = near ? new Cursor(StandardCursorType.SizeWestEast) : Avalonia.Input.Cursor.Default;
    }

    public override void Render(DrawingContext ctx)
    {
        var bounds = Bounds;
        var plotRect = new Rect(
            MarginLeft, MarginTop,
            Math.Max(10, bounds.Width - MarginLeft - MarginRight),
            Math.Max(10, bounds.Height - MarginTop - MarginBottom));

        ctx.FillRectangle(PlotBackground, plotRect);
        ctx.DrawRectangle(BorderPen, plotRect);

        var vm = _vm;
        var rec = vm?.Record;
        if (vm is null || rec is null || vm.ViewSpan <= 0)
            return;

        var visibleAnalog = vm.AnalogChannels.Where(c => c.IsVisible).ToList();
        var visibleDigital = vm.DigitalChannels.Where(c => c.IsVisible).ToList();

        double digitalAreaH = Math.Min(visibleDigital.Count * DigitalLaneHeight, plotRect.Height * 0.4);
        var analogRect = new Rect(plotRect.X, plotRect.Y, plotRect.Width, plotRect.Height - digitalAreaH);
        var digitalRect = new Rect(plotRect.X, analogRect.Bottom, plotRect.Width, digitalAreaH);

        DrawTimeAxis(ctx, vm, plotRect);

        using (ctx.PushClip(plotRect))
        {
            if (visibleAnalog.Count > 0)
            {
                if (vm.LaneMode)
                    DrawAnalogLanes(ctx, vm, rec, visibleAnalog, analogRect);
                else
                    DrawAnalogOverlay(ctx, vm, rec, visibleAnalog, analogRect);
            }

            if (visibleDigital.Count > 0)
                DrawDigitalLanes(ctx, vm, rec, visibleDigital, digitalRect);

            DrawTriggerMark(ctx, vm, rec, plotRect);
            DrawCursors(ctx, vm, plotRect);
        }
    }

    /// <summary>트리거 시각 세로선 (C-07, 와인레드 대시).</summary>
    private void DrawTriggerMark(DrawingContext ctx, MainWindowViewModel vm, ComtradeRecord rec, Rect plotRect)
    {
        if (rec.Time.TriggerIndex < 0)
            return;
        double tt = rec.Time.TimeAt((int)rec.Time.TriggerIndex);
        double x = TimeToX(tt);
        if (x < plotRect.X || x > plotRect.Right)
            return;

        ctx.DrawLine(TriggerPen, new Point(x, plotRect.Y), new Point(x, plotRect.Bottom));
        DrawText(ctx, "T", x + 3, plotRect.Y + 1, 20, TextAlignment.Left, TriggerBrush);
    }

    /// <summary>커서 2개 세로선 + 라벨 (C-06).</summary>
    private void DrawCursors(DrawingContext ctx, MainWindowViewModel vm, Rect plotRect)
    {
        if (!vm.CursorsActive)
            return;

        DrawOneCursor(ctx, plotRect, TimeToX(vm.Cursor1Time), Cursor1Pen, Cursor1Brush, "C1");
        DrawOneCursor(ctx, plotRect, TimeToX(vm.Cursor2Time), Cursor2Pen, Cursor2Brush, "C2");
    }

    private void DrawOneCursor(DrawingContext ctx, Rect plotRect, double x, Pen pen, IBrush brush, string label)
    {
        if (x < plotRect.X || x > plotRect.Right)
            return;
        ctx.DrawLine(pen, new Point(x, plotRect.Y), new Point(x, plotRect.Bottom));
        DrawText(ctx, label, x + 3, plotRect.Bottom - 14, 24, TextAlignment.Left, brush);
    }

    // ---- 시간축 ----

    private void DrawTimeAxis(DrawingContext ctx, MainWindowViewModel vm, Rect plotRect)
    {
        // t=0 기준 상대축 토글 (C-07): 표시 도메인에서 눈금을 잡고 절대 시각으로 역변환해 그린다.
        double offset = 0;
        if (vm.RelativeToTrigger && vm.Record is { } rec && rec.Time.TriggerIndex >= 0)
            offset = rec.Time.TimeAt((int)rec.Time.TriggerIndex);

        double t0 = vm.ViewStart - offset;
        double span = vm.ViewSpan;
        double step = NiceStep(span, Math.Max(3, (int)(plotRect.Width / 90)));
        double first = Math.Ceiling(t0 / step) * step;

        for (double t = first; t <= t0 + span + step * 0.001; t += step)
        {
            double x = plotRect.X + (t - t0) / span * plotRect.Width;
            if (x < plotRect.X - 0.5 || x > plotRect.Right + 0.5)
                continue;
            ctx.DrawLine(new Pen(GridBrush, 1), new Point(x, plotRect.Y), new Point(x, plotRect.Bottom));
            double displayT = Math.Abs(t) < step * 1e-9 ? 0 : t;
            DrawText(ctx, FormatTime(displayT, span), x - 24, plotRect.Bottom + 4, 48, TextAlignment.Center);
        }
    }

    internal static string FormatTime(double seconds, double span)
    {
        if (span < 0.5)
            return (seconds * 1000).ToString("0.###", CultureInfo.InvariantCulture) + "ms";
        return seconds.ToString("0.###", CultureInfo.InvariantCulture) + "s";
    }

    /// <summary>1-2-5 스텝 자동 눈금 (DESIGN §5).</summary>
    internal static double NiceStep(double span, int targetTicks)
    {
        double raw = span / Math.Max(1, targetTicks);
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double norm = raw / mag;
        double nice = norm switch
        {
            < 1.5 => 1,
            < 3.5 => 2,
            < 7.5 => 5,
            _ => 10,
        };
        return nice * mag;
    }

    // ---- 아날로그: 오버레이 ----

    private void DrawAnalogOverlay(DrawingContext ctx, MainWindowViewModel vm, ComtradeRecord rec,
        List<ChannelViewModel> channels, Rect area)
    {
        var (i0, i1) = VisibleIndexRange(vm, rec);
        if (i1 <= i0)
            return;

        int buckets = Math.Max(2, (int)area.Width);

        // 단위 그룹별 공통 스케일
        var groups = channels.GroupBy(c => c.Unit).ToList();
        var groupScale = new Dictionary<string, (double Min, double Max)>();
        var decimated = new Dictionary<int, MinMax[]>();
        foreach (var g in groups)
        {
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            foreach (var ch in g)
            {
                var env = Decimator.Decimate(rec.Analog[ch.ChannelIndex], i0, i1, buckets);
                decimated[ch.ChannelIndex] = env;
                foreach (var mm in env)
                {
                    if (mm.Min < min)
                        min = mm.Min;
                    if (mm.Max > max)
                        max = mm.Max;
                }
            }

            groupScale[g.Key] = PadRange(min, max);
        }

        foreach (var ch in channels)
        {
            var (min, max) = groupScale[ch.Unit];
            DrawEnvelope(ctx, vm, rec, decimated[ch.ChannelIndex], i0, i1, area, min, max,
                new Pen(ch.Brush, 1.3, lineJoin: PenLineJoin.Round));
        }

        // Y축 눈금: 첫 단위 그룹 기준
        var firstGroup = groups[0];
        DrawValueAxis(ctx, area, groupScale[firstGroup.Key], firstGroup.Key.Length > 0 ? firstGroup.Key : "");
    }

    // ---- 아날로그: 레인 ----

    private void DrawAnalogLanes(DrawingContext ctx, MainWindowViewModel vm, ComtradeRecord rec,
        List<ChannelViewModel> channels, Rect area)
    {
        var (i0, i1) = VisibleIndexRange(vm, rec);
        if (i1 <= i0)
            return;

        int buckets = Math.Max(2, (int)area.Width);
        double laneH = area.Height / channels.Count;

        for (int li = 0; li < channels.Count; li++)
        {
            var ch = channels[li];
            var lane = new Rect(area.X, area.Y + li * laneH, area.Width, laneH);
            var env = Decimator.Decimate(rec.Analog[ch.ChannelIndex], i0, i1, buckets);
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            foreach (var mm in env)
            {
                if (mm.Min < min)
                    min = mm.Min;
                if (mm.Max > max)
                    max = mm.Max;
            }

            var (pMin, pMax) = PadRange(min, max);
            DrawEnvelope(ctx, vm, rec, env, i0, i1, lane.Deflate(new Thickness(0, 2)), pMin, pMax,
                new Pen(ch.Brush, 1.3, lineJoin: PenLineJoin.Round));

            if (li > 0)
                ctx.DrawLine(LaneSeparatorPen, new Point(lane.X, lane.Y), new Point(lane.Right, lane.Y));
            DrawText(ctx, ch.DisplayName, lane.X + 6, lane.Y + 3, 220, TextAlignment.Left, ch.Brush);
        }
    }

    // ---- 디지털 레인 ----

    private void DrawDigitalLanes(DrawingContext ctx, MainWindowViewModel vm, ComtradeRecord rec,
        List<ChannelViewModel> channels, Rect area)
    {
        var (i0, i1) = VisibleIndexRange(vm, rec);
        if (i1 <= i0 || area.Height < 4)
            return;

        double laneH = area.Height / channels.Count;
        int px = Math.Max(2, (int)area.Width);
        int count = i1 - i0;

        for (int li = 0; li < channels.Count; li++)
        {
            var ch = channels[li];
            var data = rec.Digital[ch.ChannelIndex];
            var lane = new Rect(area.X, area.Y + li * laneH, area.Width, laneH);
            double yLow = lane.Bottom - 3;
            double yHigh = lane.Y + Math.Min(14, laneH * 0.55);

            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                bool started = false;
                double lastY = yLow;
                for (int p = 0; p < px; p++)
                {
                    int from = i0 + (int)((long)count * p / px);
                    int to = Math.Max(from + 1, i0 + (int)((long)count * (p + 1) / px));
                    bool anyHigh = false, anyLow = false;
                    for (int i = from; i < to && !(anyHigh && anyLow); i++)
                    {
                        if (data[i])
                            anyHigh = true;
                        else
                            anyLow = true;
                    }

                    double x = lane.X + (p + 0.5) / px * lane.Width;
                    double y = anyHigh ? yHigh : yLow;
                    if (!started)
                    {
                        g.BeginFigure(new Point(x, y), false);
                        started = true;
                    }
                    else
                    {
                        if (Math.Abs(y - lastY) > 0.1 || (anyHigh && anyLow))
                            g.LineTo(new Point(x, lastY)); // 스텝 전환: 수직 엣지
                        g.LineTo(new Point(x, y));
                    }

                    if (anyHigh && anyLow)
                    {
                        g.LineTo(new Point(x, yLow));
                        g.LineTo(new Point(x, yHigh));
                        y = yHigh;
                    }

                    lastY = y;
                }

                g.EndFigure(false);
            }

            ctx.DrawGeometry(null, new Pen(ch.Brush, 1.2), geo);
            ctx.DrawLine(LaneSeparatorPen, new Point(lane.X, lane.Y), new Point(lane.Right, lane.Y));
            DrawText(ctx, ch.Name, lane.X + 6, lane.Y + 2, 200, TextAlignment.Left, ch.Brush);
        }
    }

    // ---- 공통 ----

    private (int, int) VisibleIndexRange(MainWindowViewModel vm, ComtradeRecord rec)
    {
        int i0 = rec.Time.IndexAtOrBefore(vm.ViewStart);
        int i1 = rec.Time.IndexAtOrBefore(vm.ViewStart + vm.ViewSpan) + 2;
        i0 = Math.Max(0, i0 - 1);
        i1 = Math.Min(rec.SampleCount, i1);
        return (i0, i1);
    }

    /// <summary>데시메이션 봉투를 시간→X 매핑으로 그린다. 패스스루면 실선 연결.</summary>
    private void DrawEnvelope(DrawingContext ctx, MainWindowViewModel vm, ComtradeRecord rec,
        MinMax[] env, int i0, int i1, Rect area, double vMin, double vMax, Pen pen)
    {
        if (env.Length == 0 || vMax <= vMin)
            return;

        int count = i1 - i0;
        bool passthrough = env.Length == count;

        double YOf(double v) => area.Bottom - (v - vMin) / (vMax - vMin) * area.Height;

        double XOfIndex(double idx)
        {
            int i = Math.Clamp((int)idx, 0, rec.SampleCount - 1);
            double t = rec.Time.TimeAt(i);
            return area.X + (t - vm.ViewStart) / vm.ViewSpan * area.Width;
        }

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            if (passthrough)
            {
                g.BeginFigure(new Point(XOfIndex(i0), YOf(env[0].Min)), false);
                for (int i = 1; i < env.Length; i++)
                    g.LineTo(new Point(XOfIndex(i0 + i), YOf(env[i].Min)));
            }
            else
            {
                double x0 = XOfIndex(i0 + (double)count * 0 / env.Length);
                g.BeginFigure(new Point(x0, YOf(env[0].Min)), false);
                for (int b = 0; b < env.Length; b++)
                {
                    double mid = i0 + ((double)count * b / env.Length + (double)count * (b + 1) / env.Length) / 2;
                    double x = XOfIndex(mid);
                    g.LineTo(new Point(x, YOf(env[b].Min)));
                    g.LineTo(new Point(x, YOf(env[b].Max)));
                }
            }

            g.EndFigure(false);
        }

        ctx.DrawGeometry(null, pen, geo);
    }

    private void DrawValueAxis(DrawingContext ctx, Rect area, (double Min, double Max) range, string unit)
    {
        double span = range.Max - range.Min;
        if (span <= 0)
            return;
        double step = NiceStep(span, Math.Max(3, (int)(area.Height / 48)));
        double first = Math.Ceiling(range.Min / step) * step;
        for (double v = first; v <= range.Max + step * 0.001; v += step)
        {
            double y = area.Bottom - (v - range.Min) / span * area.Height;
            if (y < area.Y - 0.5 || y > area.Bottom + 0.5)
                continue;
            ctx.DrawLine(new Pen(GridBrush, 1), new Point(area.X, y), new Point(area.Right, y));
            DrawText(ctx, v.ToString("0.###", CultureInfo.InvariantCulture), 2, y - 7, MarginLeft - 6,
                TextAlignment.Right);
        }

        if (unit.Length > 0)
            DrawText(ctx, unit, 2, area.Y - 2, MarginLeft - 6, TextAlignment.Right);
    }

    internal static (double Min, double Max) PadRange(double min, double max)
    {
        if (double.IsInfinity(min) || double.IsInfinity(max))
            return (0, 1);
        if (max - min < 1e-12)
        {
            double pad = Math.Max(Math.Abs(max) * 0.1, 0.5);
            return (min - pad, max + pad);
        }

        double margin = (max - min) * 0.06;
        return (min - margin, max + margin);
    }

    private void DrawText(DrawingContext ctx, string text, double x, double y, double maxWidth,
        TextAlignment align, IBrush? brush = null)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            AxisTypeface, 10.5, brush ?? AxisTextBrush)
        {
            TextAlignment = align,
            MaxTextWidth = maxWidth,
        };
        ctx.DrawText(ft, new Point(x, y));
    }
}
