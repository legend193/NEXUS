using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexus.App.ViewModels;

namespace Nexus.App.Controls;

public class FanCurveCanvas : FrameworkElement
{
    private static readonly Typeface LabelTypeface = new(
        new FontFamily("Segoe UI, -apple-system, sans-serif"),
        FontStyles.Normal,
        FontWeights.SemiBold,
        FontStretches.Normal);

    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(255, 26, 32, 46)), 1.0);
    private static readonly Pen AxisPen = new(new SolidColorBrush(Color.FromArgb(255, 38, 46, 66)), 1.2);
    private static readonly Pen CurvePen = new(new SolidColorBrush(Color.FromRgb(0, 210, 255)), 2.5); // #00D2FF
    private static readonly Pen NeedlePen = new(new SolidColorBrush(Color.FromRgb(255, 159, 10)), 1.8); // #FF9F0A
    private static readonly Brush NeedleGlowBrush = new SolidColorBrush(Color.FromArgb(70, 255, 159, 10));
    private static readonly Brush NeedleDotBrush = new SolidColorBrush(Color.FromRgb(255, 159, 10));
    private static readonly Brush NeedlePillBg = new SolidColorBrush(Color.FromArgb(230, 26, 18, 10));
    private static readonly Pen NeedlePillBorder = new(new SolidColorBrush(Color.FromRgb(255, 159, 10)), 1.0);

    private static readonly Brush NodeFillBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42));
    private static readonly Pen NodePen = new(new SolidColorBrush(Color.FromRgb(0, 210, 255)), 2.0);
    private static readonly Brush NodeCenterBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));

    private static readonly Brush TextMutedBrush = new SolidColorBrush(Color.FromArgb(255, 120, 132, 158));
    private static readonly Brush TextNeedleBrush = new SolidColorBrush(Color.FromRgb(255, 200, 115));
    private static readonly Brush BackgroundCardBrush = new SolidColorBrush(Color.FromArgb(255, 13, 16, 23));
    private static readonly Pen BackgroundBorderPen = new(new SolidColorBrush(Color.FromArgb(255, 28, 35, 50)), 1.0);

    private static readonly Brush NodeHoverGlow = new SolidColorBrush(Color.FromArgb(90, 0, 210, 255));
    private static readonly Pen NodeHoverPen = new(new SolidColorBrush(Color.FromRgb(255, 255, 255)), 2.5);

    private int _hoveredIndex = -1;
    private int _draggedIndex = -1;

    static FanCurveCanvas()
    {
        GridPen.Freeze();
        AxisPen.Freeze();
        CurvePen.Freeze();
        NeedlePen.Freeze();
        NeedleGlowBrush.Freeze();
        NeedleDotBrush.Freeze();
        NeedlePillBg.Freeze();
        NeedlePillBorder.Freeze();
        NodeFillBrush.Freeze();
        NodePen.Freeze();
        NodeCenterBrush.Freeze();
        NodeHoverGlow.Freeze();
        NodeHoverPen.Freeze();
        TextMutedBrush.Freeze();
        TextNeedleBrush.Freeze();
        BackgroundCardBrush.Freeze();
        BackgroundBorderPen.Freeze();
    }

    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(IEnumerable<CurvePointItem>), typeof(FanCurveCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentTemperatureProperty =
        DependencyProperty.Register(nameof(CurrentTemperature), typeof(double), typeof(FanCurveCanvas),
            new FrameworkPropertyMetadata(45.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CalculatedSpeedProperty =
        DependencyProperty.Register(nameof(CalculatedSpeed), typeof(double), typeof(FanCurveCanvas),
            new FrameworkPropertyMetadata(50.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<CurvePointItem>? Points
    {
        get => (IEnumerable<CurvePointItem>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public double CurrentTemperature
    {
        get => (double)GetValue(CurrentTemperatureProperty);
        set => SetValue(CurrentTemperatureProperty, value);
    }

    public double CalculatedSpeed
    {
        get => (double)GetValue(CalculatedSpeedProperty);
        set => SetValue(CalculatedSpeedProperty, value);
    }

    public FanCurveCanvas()
    {
        Height = 220;
        MinHeight = 180;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 100 || h <= 80) return;

        // Card Frame
        dc.DrawRoundedRectangle(BackgroundCardBrush, BackgroundBorderPen, new Rect(0, 0, w, h), 10, 10);

        // Layout bounds
        double padLeft = 42.0;
        double padRight = 24.0;
        double padTop = 26.0;
        double padBottom = 26.0;

        double plotX = padLeft;
        double plotY = padTop;
        double plotW = Math.Max(10, w - padLeft - padRight);
        double plotH = Math.Max(10, h - padTop - padBottom);

        double plotRight = plotX + plotW;
        double plotBottom = plotY + plotH;

        // 1. Horizontal Speed Grid Lines (0%, 25%, 50%, 75%, 100%)
        int[] speedSteps = { 0, 25, 50, 75, 100 };
        foreach (int sp in speedSteps)
        {
            double y = plotBottom - (sp / 100.0) * plotH;
            dc.DrawLine(GridPen, new Point(plotX, y), new Point(plotRight, y));

            var ft = new FormattedText(
                $"{sp}%",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                9.5,
                TextMutedBrush,
                1.25);
            dc.DrawText(ft, new Point(plotX - ft.Width - 6, y - ft.Height / 2));
        }

        // 2. Vertical Temperature Grid Lines (20°C, 40°C, 60°C, 80°C, 100°C)
        int[] tempSteps = { 20, 40, 60, 80, 100 };
        foreach (int t in tempSteps)
        {
            double x = plotX + (t / 100.0) * plotW;
            dc.DrawLine(GridPen, new Point(x, plotY), new Point(x, plotBottom));

            var ft = new FormattedText(
                $"{t}°C",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                9.5,
                TextMutedBrush,
                1.25);
            dc.DrawText(ft, new Point(x - ft.Width / 2, plotBottom + 6));
        }

        // Axes borders
        dc.DrawLine(AxisPen, new Point(plotX, plotBottom), new Point(plotRight, plotBottom));
        dc.DrawLine(AxisPen, new Point(plotX, plotY), new Point(plotX, plotBottom));

        // 3. Extract and map curve points
        var rawPoints = Points?.OrderBy(p => p.TemperatureCelsius).ToList() ?? new List<CurvePointItem>();
        if (rawPoints.Count == 0)
        {
            rawPoints = new List<CurvePointItem>
            {
                new() { TemperatureCelsius = 20, FanSpeedPercent = 25 },
                new() { TemperatureCelsius = 40, FanSpeedPercent = 35 },
                new() { TemperatureCelsius = 60, FanSpeedPercent = 55 },
                new() { TemperatureCelsius = 75, FanSpeedPercent = 80 },
                new() { TemperatureCelsius = 85, FanSpeedPercent = 100 }
            };
        }

        // Create boundary-clamped coordinate list (including 0°C and 100°C)
        var coords = new List<Point>();

        // Point at 0°C (flat baseline or same as first)
        double firstSpeed = rawPoints[0].FanSpeedPercent;
        coords.Add(new Point(plotX, plotBottom - (firstSpeed / 100.0) * plotH));

        foreach (var pt in rawPoints)
        {
            double cx = plotX + Math.Clamp(pt.TemperatureCelsius / 100.0, 0, 1) * plotW;
            double cy = plotBottom - Math.Clamp(pt.FanSpeedPercent / 100.0, 0, 1) * plotH;
            coords.Add(new Point(cx, cy));
        }

        // Point at 100°C (100% fan speed)
        coords.Add(new Point(plotRight, plotBottom - (100.0 / 100.0) * plotH));

        // 4. Draw Smooth Spline Curve with Area Gradient Fill
        var fillGeo = new StreamGeometry();
        using (var ctx = fillGeo.Open())
        {
            ctx.BeginFigure(new Point(plotX, plotBottom), true, true);
            ctx.LineTo(coords[0], true, false);

            for (int i = 0; i < coords.Count - 1; i++)
            {
                var p0 = i > 0 ? coords[i - 1] : coords[i];
                var p1 = coords[i];
                var p2 = coords[i + 1];
                var p3 = i < coords.Count - 2 ? coords[i + 2] : p2;

                // Catmull-Rom to Cubic Bezier control points
                var cp1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
                var cp2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);

                ctx.BezierTo(cp1, cp2, p2, true, false);
            }

            ctx.LineTo(new Point(plotRight, plotBottom), true, false);
        }
        fillGeo.Freeze();

        var areaBrush = new LinearGradientBrush(
            Color.FromArgb(55, 0, 210, 255),
            Color.FromArgb(4, 0, 210, 255),
            90.0);
        areaBrush.Freeze();
        dc.DrawGeometry(areaBrush, null, fillGeo);

        // Curve stroke only
        var strokeGeo = new StreamGeometry();
        using (var ctx = strokeGeo.Open())
        {
            ctx.BeginFigure(coords[0], false, false);
            for (int i = 0; i < coords.Count - 1; i++)
            {
                var p0 = i > 0 ? coords[i - 1] : coords[i];
                var p1 = coords[i];
                var p2 = coords[i + 1];
                var p3 = i < coords.Count - 2 ? coords[i + 2] : p2;

                var cp1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
                var cp2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);

                ctx.BezierTo(cp1, cp2, p2, true, false);
            }
        }
        strokeGeo.Freeze();
        dc.DrawGeometry(null, CurvePen, strokeGeo);

        // 5. Draw Control Nodes
        for (int i = 0; i < rawPoints.Count; i++)
        {
            var pt = rawPoints[i];
            double nx = plotX + Math.Clamp(pt.TemperatureCelsius / 100.0, 0, 1) * plotW;
            double ny = plotBottom - Math.Clamp(pt.FanSpeedPercent / 100.0, 0, 1) * plotH;

            bool isHighlighted = (i == _hoveredIndex || i == _draggedIndex);
            if (isHighlighted)
            {
                dc.DrawEllipse(NodeHoverGlow, null, new Point(nx, ny), 12.0, 12.0);
                dc.DrawEllipse(NodeFillBrush, NodeHoverPen, new Point(nx, ny), 7.0, 7.0);
                dc.DrawEllipse(NodeCenterBrush, null, new Point(nx, ny), 3.0, 3.0);

                // Show dynamic tooltip for the dragged/hovered node
                string nodeTag = $"{pt.TemperatureCelsius:0}°C : {pt.FanSpeedPercent:0}%";
                var nodeFt = new FormattedText(
                    nodeTag,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    9.0,
                    TextNeedleBrush,
                    1.25);
                double nPillW = nodeFt.Width + 10;
                double nPillH = nodeFt.Height + 4;
                double nPillX = Math.Clamp(nx - nPillW / 2, plotX, plotRight - nPillW);
                double nPillY = ny - nPillH - 10;
                if (nPillY < plotY) nPillY = ny + 12;

                dc.DrawRoundedRectangle(NeedlePillBg, NeedlePillBorder, new Rect(nPillX, nPillY, nPillW, nPillH), 4, 4);
                dc.DrawText(nodeFt, new Point(nPillX + 5, nPillY + 2));
            }
            else
            {
                dc.DrawEllipse(NodeFillBrush, NodePen, new Point(nx, ny), 5.5, 5.5);
                dc.DrawEllipse(NodeCenterBrush, null, new Point(nx, ny), 2.0, 2.0);
            }
        }

        // 6. LIVE CURRENT TEMPERATURE NEEDLE
        double tempClamped = Math.Clamp(CurrentTemperature, 0.0, 100.0);
        double needleX = plotX + (tempClamped / 100.0) * plotW;
        double speedClamped = Math.Clamp(CalculatedSpeed, 0.0, 100.0);
        double needleY = plotBottom - (speedClamped / 100.0) * plotH;

        // Vertical glowing needle line
        dc.DrawLine(NeedlePen, new Point(needleX, plotY), new Point(needleX, plotBottom));

        // Glowing junction point
        dc.DrawEllipse(NeedleGlowBrush, null, new Point(needleX, needleY), 9.0, 9.0);
        dc.DrawEllipse(NeedleDotBrush, null, new Point(needleX, needleY), 4.5, 4.5);
        dc.DrawEllipse(NodeCenterBrush, null, new Point(needleX, needleY), 2.0, 2.0);

        // Live telemetry pill tag at the needle
        string tagText = $"{tempClamped:0}°C • {speedClamped:0}% RPM";
        var tagFt = new FormattedText(
            tagText,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            9.5,
            TextNeedleBrush,
            1.25);

        double pillW = tagFt.Width + 12;
        double pillH = tagFt.Height + 6;
        double pillX = Math.Clamp(needleX - pillW / 2, plotX + 4, plotRight - pillW - 4);
        double pillY = plotY + 4;

        dc.DrawRoundedRectangle(NeedlePillBg, NeedlePillBorder, new Rect(pillX, pillY, pillW, pillH), 4, 4);
        dc.DrawText(tagFt, new Point(pillX + 6, pillY + 3));
    }

    private void GetPlotBounds(out double plotX, out double plotY, out double plotW, out double plotH, out double plotBottom)
    {
        double padLeft = 42.0;
        double padRight = 24.0;
        double padTop = 26.0;
        double padBottom = 26.0;

        plotX = padLeft;
        plotY = padTop;
        plotW = Math.Max(10, ActualWidth - padLeft - padRight);
        plotH = Math.Max(10, ActualHeight - padTop - padBottom);
        plotBottom = plotY + plotH;
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var pos = e.GetPosition(this);
        var pointsList = Points?.ToList();
        if (pointsList == null || pointsList.Count == 0) return;

        GetPlotBounds(out double plotX, out _, out double plotW, out double plotH, out double plotBottom);

        for (int i = 0; i < pointsList.Count; i++)
        {
            var pt = pointsList[i];
            double nx = plotX + Math.Clamp(pt.TemperatureCelsius / 100.0, 0, 1) * plotW;
            double ny = plotBottom - Math.Clamp(pt.FanSpeedPercent / 100.0, 0, 1) * plotH;

            double distSq = (pos.X - nx) * (pos.X - nx) + (pos.Y - ny) * (pos.Y - ny);
            if (distSq <= 16 * 16)
            {
                _draggedIndex = i;
                _hoveredIndex = i;
                CaptureMouse();
                Cursor = System.Windows.Input.Cursors.Hand;
                InvalidateVisual();
                e.Handled = true;
                break;
            }
        }
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);
        var pointsList = Points?.ToList();
        if (pointsList == null || pointsList.Count == 0) return;

        GetPlotBounds(out double plotX, out _, out double plotW, out double plotH, out double plotBottom);

        if (_draggedIndex >= 0 && IsMouseCaptured && _draggedIndex < pointsList.Count)
        {
            double newTemp = ((pos.X - plotX) / plotW) * 100.0;
            double newSpeed = ((plotBottom - pos.Y) / plotH) * 100.0;

            // Clamping rules to maintain sorted curve
            double minTemp = _draggedIndex > 0 ? pointsList[_draggedIndex - 1].TemperatureCelsius + 3.0 : 0.0;
            double maxTemp = _draggedIndex < pointsList.Count - 1 ? pointsList[_draggedIndex + 1].TemperatureCelsius - 3.0 : 100.0;

            newTemp = Math.Clamp(newTemp, minTemp, maxTemp);
            newSpeed = Math.Clamp(newSpeed, 0.0, 100.0);

            pointsList[_draggedIndex].TemperatureCelsius = Math.Round(newTemp, 0);
            pointsList[_draggedIndex].FanSpeedPercent = Math.Round(newSpeed, 0);

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // Hit-test for hover
        int newHover = -1;
        for (int i = 0; i < pointsList.Count; i++)
        {
            var pt = pointsList[i];
            double nx = plotX + Math.Clamp(pt.TemperatureCelsius / 100.0, 0, 1) * plotW;
            double ny = plotBottom - Math.Clamp(pt.FanSpeedPercent / 100.0, 0, 1) * plotH;

            double distSq = (pos.X - nx) * (pos.X - nx) + (pos.Y - ny) * (pos.Y - ny);
            if (distSq <= 14 * 14)
            {
                newHover = i;
                break;
            }
        }

        if (newHover != _hoveredIndex)
        {
            _hoveredIndex = newHover;
            Cursor = _hoveredIndex >= 0 ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_draggedIndex >= 0)
        {
            _draggedIndex = -1;
            ReleaseMouseCapture();
            Cursor = System.Windows.Input.Cursors.Arrow;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_draggedIndex < 0 && _hoveredIndex >= 0)
        {
            _hoveredIndex = -1;
            Cursor = System.Windows.Input.Cursors.Arrow;
            InvalidateVisual();
        }
    }
}
