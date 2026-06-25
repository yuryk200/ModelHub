using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ModelHub.Controls;

public class LoadingSpinner : Control
{
    private readonly DispatcherTimer _timer;
    private double _angle;

    public LoadingSpinner()
    {
        Width = 24;
        Height = 24;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _timer.Tick += (_, _) =>
        {
            _angle = (_angle + 6) % 360;
            InvalidateVisual();
        };

        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        var center = new Point(bounds.Width / 2, bounds.Height / 2);
        var radius = Math.Min(bounds.Width, bounds.Height) / 2 - 3;

        if (radius <= 0)
        {
            return;
        }

        var startAngle = _angle;
        var endAngle = _angle + 270;

        var startPoint = PointOnCircle(center, radius, startAngle);
        var endPoint = PointOnCircle(center, radius, endAngle);

        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(startPoint, false);
            ctx.ArcTo(
                endPoint,
                new Size(radius, radius),
                0,
                true,
                SweepDirection.Clockwise);
        }

        var pen = new Pen(Brushes.DodgerBlue, 3)
        {
            LineCap = PenLineCap.Round
        };

        context.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var angleRadians = Math.PI * angleDegrees / 180.0;

        return new Point(
            center.X + radius * Math.Cos(angleRadians),
            center.Y + radius * Math.Sin(angleRadians));
    }
}