using System.Windows;
using System.Windows.Media;

namespace Stellary.PX.Controls;

public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueBrushProperty = DependencyProperty.Register(
        nameof(ValueBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush ValueBrush { get => (Brush)GetValue(ValueBrushProperty); set => SetValue(ValueBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 4) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = size / 2 - Thickness / 2 - 2;

        var trackPen = new Pen(TrackBrush, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var valuePen = new Pen(ValueBrush, 2.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        // Tick ring — engraved increments.
        var tickPen = new Pen(new SolidColorBrush(Color.FromArgb(44, 255, 255, 255)), 1);
        for (var i = 0; i <= 48; i++)
        {
            var angle = 135 + i * (270.0 / 48);
            var outer = PointOn(center, radius + Thickness / 2 + 5, angle);
            var inner = PointOn(center, radius + Thickness / 2 + (i % 4 == 0 ? 1 : 2.5), angle);
            dc.DrawLine(tickPen, inner, outer);
        }

        dc.DrawGeometry(null, trackPen, Arc(center, radius, 135, 270));
        var value = Math.Clamp(Value, 0, 100);
        if (value <= 0.4) return;
        var sweep = 270 * value / 100.0;
        dc.DrawGeometry(null, valuePen, Arc(center, radius, 135, sweep));

        // Needle notch at the value position, crossing the track.
        var tipAngle = 135 + sweep;
        var markerPen = new Pen(ValueBrush, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(markerPen, PointOn(center, radius - 6, tipAngle), PointOn(center, radius + 6, tipAngle));
    }

    private static Point PointOn(Point center, double radius, double degrees)
    {
        var rad = degrees * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
    }

    private static Geometry Arc(Point center, double radius, double startDeg, double sweepDeg)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var start = PointOn(center, radius, startDeg);
            var end = PointOn(center, radius, startDeg + sweepDeg);
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, sweepDeg > 180,
                sweepDeg >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }
}
