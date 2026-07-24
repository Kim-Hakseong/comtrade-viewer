using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Ncv.App.ViewModels;

namespace Ncv.App.Controls
{
    /// <summary>
    /// 극좌표 페이저 다이어그램 (DESIGN §6, 커스텀 드로잉). 반지름 자동 스케일, 0°=+X, 반시계 양수.
    /// </summary>
    public class PhasorControl : Control
    {
        private static readonly Pen RingPen = new(new SolidColorBrush(Color.Parse("#E5E3DD")), 1);
        private static readonly Pen SpokePen = new(new SolidColorBrush(Color.Parse("#F0EEE8")), 1);
        private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#8B897F"));
        private static readonly Typeface LabelTypeface = new("Inter, sans-serif");

        private MainWindowViewModel? _vm;

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
            var vm = _vm;
            if (vm is null)
                return;

            double size = Math.Min(Bounds.Width, Bounds.Height);
            var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
            double radius = size / 2 - 16;
            if (radius < 20)
                return;

            // 링 + 스포크 (30° 간격)
            for (int r = 1; r <= 3; r++)
                ctx.DrawEllipse(null, RingPen, center, radius * r / 3, radius * r / 3);
            for (int deg = 0; deg < 360; deg += 30)
            {
                double rad = deg * Math.PI / 180;
                var end = center + new Vector(Math.Cos(rad), -Math.Sin(rad)) * radius;
                ctx.DrawLine(SpokePen, center, end);
            }

            DrawLabel(ctx, "0°", center + new Vector(radius + 3, -6));
            DrawLabel(ctx, "90°", center + new Vector(-8, -radius - 14));

            var rows = vm.PhasorRows;
            if (rows.Count == 0)
                return;

            double maxMag = rows.Max(r => r.Magnitude);
            if (maxMag <= 0)
                return;

            foreach (var row in rows)
            {
                double rad = row.AngleDegrees * Math.PI / 180;
                double len = row.Magnitude / maxMag * radius;
                var dir = new Vector(Math.Cos(rad), -Math.Sin(rad));
                var tip = center + dir * len;

                var pen = new Pen(row.Brush, 1.8, lineCap: PenLineCap.Round);
                ctx.DrawLine(pen, center, tip);

                // 화살촉: 벡터 끝 양쪽 15° 짧은 선
                var back = -dir * Math.Min(8, len * 0.25);
                var left = Rotate(back, 20 * Math.PI / 180);
                var right = Rotate(back, -20 * Math.PI / 180);
                ctx.DrawLine(pen, tip, tip + left);
                ctx.DrawLine(pen, tip, tip + right);
            }
        }

        private static Vector Rotate(Vector v, double rad) => new(
            v.X * Math.Cos(rad) - v.Y * Math.Sin(rad),
            v.X * Math.Sin(rad) + v.Y * Math.Cos(rad));

        private static void DrawLabel(DrawingContext ctx, string text, Point at)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                LabelTypeface, 10, LabelBrush);
            ctx.DrawText(ft, at);
        }
    }
}
