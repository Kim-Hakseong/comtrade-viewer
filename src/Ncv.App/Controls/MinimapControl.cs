using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Ncv.App.ViewModels;
using Ncv.Core.Analysis;

namespace Ncv.App.Controls;

/// <summary>
/// 하단 미니맵 타임라인 (C-05): 전체 레코드 봉투 + 현재 가시 구간 창. 클릭/드래그로 이동.
/// </summary>
public class MinimapControl : Control
{
    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#F1EFEA"));
    private static readonly IBrush EnvelopeBrush = new SolidColorBrush(Color.Parse("#B0AEA5"));
    private static readonly IBrush WindowBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x7A, 0x10, 0x20));
    private static readonly Pen WindowPen = new(new SolidColorBrush(Color.Parse("#7A1020")), 1.2);
    private static readonly Pen TriggerPen = new(new SolidColorBrush(Color.Parse("#7A1020")), 1,
        dashStyle: new DashStyle(new double[] { 3, 2 }, 0));

    private MainWindowViewModel? _vm;
    private bool _dragging;

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

    public override void Render(DrawingContext ctx)
    {
        var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);
        ctx.FillRectangle(Background, rect, 8);

        var vm = _vm;
        var rec = vm?.Record;
        if (vm is null || rec is null || rec.SampleCount == 0)
            return;

        double tStart = rec.Time.TimeAt(0);
        double tEnd = rec.Time.TimeAt(rec.SampleCount - 1);
        double total = Math.Max(tEnd - tStart, 1e-9);

        // 첫 가시 아날로그 채널 봉투
        var ch = vm.AnalogChannels.FirstOrDefault(c => c.IsVisible);
        if (ch is not null)
        {
            var data = rec.Analog[ch.ChannelIndex];
            int buckets = Math.Max(2, (int)rect.Width);
            var env = Decimator.Decimate(data, 0, data.Length, buckets);
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            foreach (var mm in env)
            {
                if (mm.Min < min)
                    min = mm.Min;
                if (mm.Max > max)
                    max = mm.Max;
            }

            if (max > min)
            {
                double h = rect.Height - 6;
                for (int b = 0; b < env.Length; b++)
                {
                    double x = (b + 0.5) / env.Length * rect.Width;
                    double y1 = 3 + (1 - (env[b].Max - min) / (max - min)) * h;
                    double y2 = 3 + (1 - (env[b].Min - min) / (max - min)) * h;
                    ctx.DrawLine(new Pen(EnvelopeBrush, 1), new Point(x, y1), new Point(x, Math.Max(y2, y1 + 0.8)));
                }
            }
        }

        // 트리거 위치
        if (rec.Time.TriggerIndex >= 0)
        {
            double tx = (rec.Time.TimeAt((int)rec.Time.TriggerIndex) - tStart) / total * rect.Width;
            ctx.DrawLine(TriggerPen, new Point(tx, 2), new Point(tx, rect.Height - 2));
        }

        // 가시 구간 창
        double wx = (vm.ViewStart - tStart) / total * rect.Width;
        double ww = Math.Max(4, vm.ViewSpan / total * rect.Width);
        var win = new Rect(Math.Clamp(wx, 0, Math.Max(0, rect.Width - ww)), 1, Math.Min(ww, rect.Width), rect.Height - 2);
        ctx.FillRectangle(WindowBrush, win, 6);
        ctx.DrawRectangle(WindowPen, win, 6);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm?.Record is null)
            return;
        _dragging = true;
        MoveTo(e.GetPosition(this).X);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
        {
            MoveTo(e.GetPosition(this).X);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void MoveTo(double x)
    {
        var vm = _vm;
        var rec = vm?.Record;
        if (vm is null || rec is null)
            return;

        double tStart = rec.Time.TimeAt(0);
        double tEnd = rec.Time.TimeAt(rec.SampleCount - 1);
        double t = tStart + Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1) * (tEnd - tStart);
        vm.CenterViewAt(t);
    }
}
