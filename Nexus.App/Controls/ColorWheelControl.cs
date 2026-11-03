using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexus.App.Controls;

public class ColorWheelControl : FrameworkElement
{
    public static readonly DependencyProperty SelectedColorHexProperty =
        DependencyProperty.Register(nameof(SelectedColorHex), typeof(string), typeof(ColorWheelControl),
            new FrameworkPropertyMetadata("#00D2FF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorHexChanged));

    public string SelectedColorHex
    {
        get => (string)GetValue(SelectedColorHexProperty);
        set => SetValue(SelectedColorHexProperty, value);
    }

    private Point _thumbPosition = new Point(75, 75);
    private bool _isDragging = false;

    private static void OnSelectedColorHexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorWheelControl wheel && !wheel._isDragging)
        {
            wheel.InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 10 || h <= 10) return;

        double cx = w / 2.0;
        double cy = h / 2.0;
        double radius = Math.Min(cx, cy) - 6.0;
        if (radius <= 0) return;

        // Draw 36 angular segments of the color wheel
        const int segments = 36;
        double angleStep = 360.0 / segments;

        for (int i = 0; i < segments; i++)
        {
            double a1 = (i * angleStep) * Math.PI / 180.0;
            double a2 = ((i + 1) * angleStep) * Math.PI / 180.0;

            Point p0 = new Point(cx, cy);
            Point p1 = new Point(cx + radius * Math.Cos(a1), cy + radius * Math.Sin(a1));
            Point p2 = new Point(cx + radius * Math.Cos(a2), cy + radius * Math.Sin(a2));

            Color col = ColorFromHsv(i * angleStep, 1.0, 1.0);
            SolidColorBrush brush = new SolidColorBrush(col);
            brush.Freeze();

            StreamGeometry geom = new StreamGeometry();
            using (StreamGeometryContext ctx = geom.Open())
            {
                ctx.BeginFigure(p0, true, true);
                ctx.LineTo(p1, false, false);
                ctx.LineTo(p2, false, false);
            }
            geom.Freeze();
            dc.DrawGeometry(brush, null, geom);
        }

        // Draw center radial white/dark wash for saturation & brightness
        RadialGradientBrush centerWash = new RadialGradientBrush(
            Color.FromArgb(230, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255))
        {
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        centerWash.Freeze();
        dc.DrawEllipse(centerWash, null, new Point(cx, cy), radius, radius);

        // Dark obsidian inner border
        Pen borderPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 31, 37, 54)), 2.0);
        borderPen.Freeze();
        dc.DrawEllipse(null, borderPen, new Point(cx, cy), radius, radius);

        // Draw draggable selector thumb
        Color activeCol;
        try
        {
            activeCol = (Color)ColorConverter.ConvertFromString(SelectedColorHex);
        }
        catch
        {
            activeCol = Color.FromRgb(0, 210, 255);
        }

        Brush thumbBrush = new SolidColorBrush(activeCol);
        thumbBrush.Freeze();
        Pen thumbBorder = new Pen(Brushes.White, 2.5);
        thumbBorder.Freeze();

        dc.DrawEllipse(thumbBrush, thumbBorder, _thumbPosition, 7, 7);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        CaptureMouse();
        _isDragging = true;
        UpdateColorFromPoint(e.GetPosition(this));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_isDragging)
        {
            UpdateColorFromPoint(e.GetPosition(this));
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
        }
    }

    private void UpdateColorFromPoint(Point pt)
    {
        double cx = ActualWidth / 2.0;
        double cy = ActualHeight / 2.0;
        double radius = Math.Min(cx, cy) - 6.0;
        if (radius <= 0) return;

        double dx = pt.X - cx;
        double dy = pt.Y - cy;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist > radius)
        {
            dx = (dx / dist) * radius;
            dy = (dy / dist) * radius;
            dist = radius;
            pt = new Point(cx + dx, cy + dy);
        }

        _thumbPosition = pt;

        double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (angle < 0) angle += 360.0;

        double sat = Math.Clamp(dist / radius, 0.05, 1.0);
        Color c = ColorFromHsv(angle, sat, 1.0);

        SelectedColorHex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        InvalidateVisual();
    }

    private static Color ColorFromHsv(double hue, double saturation, double value)
    {
        int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
        double f = hue / 60 - Math.Floor(hue / 60);

        value = value * 255;
        byte v = Convert.ToByte(Math.Clamp(value, 0, 255));
        byte p = Convert.ToByte(Math.Clamp(value * (1 - saturation), 0, 255));
        byte q = Convert.ToByte(Math.Clamp(value * (1 - f * saturation), 0, 255));
        byte t = Convert.ToByte(Math.Clamp(value * (1 - (1 - f) * saturation), 0, 255));

        return hi switch
        {
            0 => Color.FromRgb(v, t, p),
            1 => Color.FromRgb(q, v, p),
            2 => Color.FromRgb(p, v, t),
            3 => Color.FromRgb(p, q, v),
            4 => Color.FromRgb(t, p, v),
            _ => Color.FromRgb(v, p, q),
        };
    }
}
