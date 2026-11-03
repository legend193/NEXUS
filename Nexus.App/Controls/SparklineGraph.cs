using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace Nexus.App.Controls;

public class SparklineGraph : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(IEnumerable), typeof(SparklineGraph),
            new FrameworkPropertyMetadata(null, OnPointsChanged));

    public static readonly DependencyProperty LineBrushProperty =
        DependencyProperty.Register(nameof(LineBrush), typeof(Brush), typeof(SparklineGraph),
            new FrameworkPropertyMetadata(Brushes.Cyan, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillBrushProperty =
        DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(SparklineGraph),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(SparklineGraph),
            new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(SparklineGraph),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(SparklineGraph),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? Points
    {
        get => (IEnumerable?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public Brush? FillBrush
    {
        get => (Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
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

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SparklineGraph graph)
        {
            if (e.OldValue is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= graph.OnCollectionChanged;
            }
            if (e.NewValue is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += graph.OnCollectionChanged;
            }
            graph.InvalidateVisual();
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 4 || h <= 4) return;

        List<double> values = new();
        if (Points != null)
        {
            foreach (var item in Points)
            {
                if (item is double d) values.Add(d);
                else if (item is float f) values.Add(f);
                else if (item is int i) values.Add(i);
                else if (item != null && double.TryParse(item.ToString(), out double parsed)) values.Add(parsed);
            }
        }

        // If no points yet, provide a graceful cyber flatline or preview
        if (values.Count < 2)
        {
            values = new List<double> { 22, 28, 25, 34, 30, 42, 38, 45, 41, 55, 48, 52, 49, 58, 54, 60 };
        }

        double min = Minimum;
        double max = Maximum;
        if (max <= min) max = min + 1.0;

        int count = values.Count;
        double stepX = w / (count - 1);

        PointCollection pts = new PointCollection(count);
        for (int i = 0; i < count; i++)
        {
            double val = Math.Clamp(values[i], min, max);
            double norm = (val - min) / (max - min);
            double x = i * stepX;
            double y = h - (norm * (h - StrokeThickness * 2.0)) - StrokeThickness;
            pts.Add(new Point(x, y));
        }

        // Draw fill area under the line if fill brush is provided
        Brush? fill = FillBrush;
        if (fill == null && LineBrush is SolidColorBrush scb)
        {
            Color c = scb.Color;
            fill = new LinearGradientBrush(
                Color.FromArgb(50, c.R, c.G, c.B),
                Color.FromArgb(2, c.R, c.G, c.B),
                new Point(0, 0),
                new Point(0, 1));
        }

        if (fill != null)
        {
            StreamGeometry fillGeom = new StreamGeometry();
            using (StreamGeometryContext ctx = fillGeom.Open())
            {
                ctx.BeginFigure(new Point(0, h), true, true);
                ctx.LineTo(pts[0], true, false);
                for (int i = 1; i < pts.Count; i++)
                {
                    ctx.LineTo(pts[i], true, false);
                }
                ctx.LineTo(new Point(w, h), true, false);
            }
            fillGeom.Freeze();
            dc.DrawGeometry(fill, null, fillGeom);
        }

        // Draw the stroke line
        StreamGeometry lineGeom = new StreamGeometry();
        using (StreamGeometryContext ctx = lineGeom.Open())
        {
            ctx.BeginFigure(pts[0], false, false);
            for (int i = 1; i < pts.Count; i++)
            {
                ctx.LineTo(pts[i], true, false);
            }
        }
        lineGeom.Freeze();

        Pen pen = new Pen(LineBrush, StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        dc.DrawGeometry(null, pen, lineGeom);
    }
}
