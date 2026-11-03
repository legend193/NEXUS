using System.Windows;
using System.Windows.Media;

namespace Nexus.App.Controls;

public class RadialGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(RadialGauge),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(RadialGauge),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(RadialGauge),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(RadialGauge),
            new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GaugeBrushProperty =
        DependencyProperty.Register(nameof(GaugeBrush), typeof(Brush), typeof(RadialGauge),
            new FrameworkPropertyMetadata(Brushes.Cyan, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty =
        DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(RadialGauge),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public Brush GaugeBrush
    {
        get => (Brush)GetValue(GaugeBrushProperty);
        set => SetValue(GaugeBrushProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double center = Math.Min(w, h) / 2.0;
        double radius = center - (StrokeThickness / 2.0) - 2.0;
        if (radius <= 0) return;

        Point centerPoint = new Point(w / 2.0, h / 2.0);

        // 270 degree arc, starting at 135 degrees and sweeping clockwise to 405 degrees (open at bottom)
        const double startAngle = 135.0;
        const double sweepAngle = 270.0;

        // 1. Draw Track Arc
        var trackPen = new Pen(TrackBrush, StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        trackPen.Freeze();

        Point trackStart = PointOnCircle(centerPoint, radius, startAngle);
        Point trackEnd = PointOnCircle(centerPoint, radius, startAngle + sweepAngle);

        var trackGeom = new StreamGeometry();
        using (var ctx = trackGeom.Open())
        {
            ctx.BeginFigure(trackStart, false, false);
            ctx.ArcTo(trackEnd, new Size(radius, radius), 0, true, SweepDirection.Clockwise, true, false);
        }
        trackGeom.Freeze();
        dc.DrawGeometry(null, trackPen, trackGeom);

        // 2. Draw Active Value Arc
        double range = Maximum - Minimum;
        if (range <= 0) range = 100.0;
        double progress = Math.Clamp((Value - Minimum) / range, 0.0, 1.0);
        if (progress > 0.005)
        {
            double valueSweep = progress * sweepAngle;
            Point valStart = trackStart;
            Point valEnd = PointOnCircle(centerPoint, radius, startAngle + valueSweep);
            bool isLarge = valueSweep > 180.0;

            var valPen = new Pen(GaugeBrush, StrokeThickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            valPen.Freeze();

            var valGeom = new StreamGeometry();
            using (var ctx = valGeom.Open())
            {
                ctx.BeginFigure(valStart, false, false);
                ctx.ArcTo(valEnd, new Size(radius, radius), 0, isLarge, SweepDirection.Clockwise, true, false);
            }
            valGeom.Freeze();
            dc.DrawGeometry(null, valPen, valGeom);
        }
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        double angleRadians = (angleDegrees - 90.0) * Math.PI / 180.0;
        return new Point(
            center.X + radius * Math.Cos(angleRadians),
            center.Y + radius * Math.Sin(angleRadians));
    }
}