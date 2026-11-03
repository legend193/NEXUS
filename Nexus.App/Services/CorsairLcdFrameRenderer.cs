using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexus.Abstractions.Cooling;

namespace Nexus.App.Services;

/// <summary>
/// Autonomous 480x480 Hardware Frame Renderer for Corsair iCUE ELITE LCD Display Cap.
/// Renders crisp circular dials, telemetry gauges, and custom assets to JPEG byte streams.
/// </summary>
public static class CorsairLcdFrameRenderer
{
    private static readonly Typeface BoldTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface SemiBoldTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface RegularTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static byte[] RenderFrame(AioLcdConfig config, int cpuTemp, int gpuTemp, int coolantTemp)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Black background
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 480, 480));

            Color accentColor;
            try
            {
                accentColor = (Color)ColorConverter.ConvertFromString(config.DialAccentColorHex);
            }
            catch
            {
                accentColor = Color.FromRgb(0, 210, 255);
            }
            var accentBrush = new SolidColorBrush(accentColor);
            var accentPen = new Pen(accentBrush, 10);

            switch (config.Mode)
            {
                case AioLcdMode.CustomImage when !string.IsNullOrEmpty(config.CustomImagePath) && File.Exists(config.CustomImagePath):
                    DrawCustomImage(dc, config.CustomImagePath);
                    break;

                case AioLcdMode.NexusLogo:
                    DrawNexusLogo(dc, accentBrush);
                    break;

                case AioLcdMode.DigitalClock:
                    DrawClock(dc, accentBrush);
                    break;

                case AioLcdMode.ArcReactor:
                    DrawArcReactor(dc, accentBrush);
                    break;

                case AioLcdMode.GpuTempGauge:
                    DrawGauge(dc, gpuTemp, "°C", "GPU TEMPERATURE", "MSI RTX 3080 GAMING X", accentBrush, accentPen, 30, 95);
                    break;

                case AioLcdMode.CoolantTempGauge:
                    DrawGauge(dc, coolantTemp, "°C", "COOLANT TEMPERATURE", "CORSAIR H150i ELITE", accentBrush, accentPen, 20, 60);
                    break;

                case AioLcdMode.CpuTempGauge:
                default:
                    DrawGauge(dc, cpuTemp, "°C", "CPU TEMPERATURE", "AMD RYZEN 9 5900X", accentBrush, accentPen, 30, 95);
                    break;
            }

            // Outer Circular Bezel Ring matching Halo Ring
            Color haloColor;
            try
            {
                haloColor = (Color)ColorConverter.ConvertFromString(config.HaloRingColorHex);
            }
            catch
            {
                haloColor = accentColor;
            }
            var haloPen = new Pen(new SolidColorBrush(haloColor), 4);
            dc.DrawEllipse(null, haloPen, new Point(240, 240), 236, 236);
        }

        var rtb = new RenderTargetBitmap(480, 480, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new JpegBitmapEncoder { QualityLevel = 88 };
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static void DrawGauge(DrawingContext dc, int value, string unit, string header, string subHeader, Brush accentBrush, Pen accentPen, int minVal, int maxVal)
    {
        // Dark track ring
        var trackPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 12);
        dc.DrawEllipse(null, trackPen, new Point(240, 240), 200, 200);

        // Progress arc
        double fraction = Math.Clamp((double)(value - minVal) / (maxVal - minVal), 0.05, 1.0);
        double startAngle = 135;
        double totalSweep = 270;
        double sweepAngle = totalSweep * fraction;

        var arcGeom = CreateArcGeometry(new Point(240, 240), 200, startAngle, sweepAngle);
        dc.DrawGeometry(null, accentPen, arcGeom);

        // Header
        var ftHeader = new FormattedText(
            header,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            SemiBoldTypeface,
            18,
            new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            1.0);
        dc.DrawText(ftHeader, new Point(240 - ftHeader.Width / 2, 95));

        // Big Main Value
        var ftVal = new FormattedText(
            $"{value}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            BoldTypeface,
            128,
            Brushes.White,
            1.0);
        dc.DrawText(ftVal, new Point(240 - ftVal.Width / 2 - 15, 145));

        // Unit
        var ftUnit = new FormattedText(
            unit,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            BoldTypeface,
            42,
            accentBrush,
            1.0);
        dc.DrawText(ftUnit, new Point(240 + ftVal.Width / 2 - 10, 175));

        // Subheader
        var ftSub = new FormattedText(
            subHeader,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            RegularTypeface,
            16,
            new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
            1.0);
        dc.DrawText(ftSub, new Point(240 - ftSub.Width / 2, 320));

        // Bottom status indicator
        var ftNexus = new FormattedText(
            "NEXUS AUTONOMOUS COOLING",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            SemiBoldTypeface,
            12,
            accentBrush,
            1.0);
        dc.DrawText(ftNexus, new Point(240 - ftNexus.Width / 2, 370));
    }

    private static void DrawNexusLogo(DrawingContext dc, Brush accentBrush)
    {
        // Concentric glow rings
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(40, 0, 210, 255)), 8), new Point(240, 240), 190, 190);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(80, 0, 210, 255)), 4), new Point(240, 240), 170, 170);

        // Stylized NEXUS Isometric Hexagon / Diamond
        var p1 = new Point(240, 110);
        var p2 = new Point(340, 170);
        var p3 = new Point(340, 280);
        var p4 = new Point(240, 340);
        var p5 = new Point(140, 280);
        var p6 = new Point(140, 170);

        var hexStream = new StreamGeometry();
        using (var ctx = hexStream.Open())
        {
            ctx.BeginFigure(p1, true, true);
            ctx.LineTo(p2, true, false);
            ctx.LineTo(p3, true, false);
            ctx.LineTo(p4, true, false);
            ctx.LineTo(p5, true, false);
            ctx.LineTo(p6, true, false);
        }
        hexStream.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(50, 0, 100, 255)), new Pen(accentBrush, 6), hexStream);

        // Center line splits
        dc.DrawLine(new Pen(accentBrush, 4), new Point(240, 110), new Point(240, 340));
        dc.DrawLine(new Pen(accentBrush, 4), new Point(140, 170), new Point(240, 225));
        dc.DrawLine(new Pen(accentBrush, 4), new Point(340, 170), new Point(240, 225));

        // Official Title
        var ftTitle = new FormattedText(
            "N E X U S",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            BoldTypeface,
            36,
            Brushes.White,
            1.0);
        dc.DrawText(ftTitle, new Point(240 - ftTitle.Width / 2, 360));

        var ftSub = new FormattedText(
            "HARDWARE ECOSYSTEM",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            SemiBoldTypeface,
            14,
            accentBrush,
            1.0);
        dc.DrawText(ftSub, new Point(240 - ftSub.Width / 2, 405));
    }

    private static void DrawClock(DrawingContext dc, Brush accentBrush)
    {
        var now = DateTime.Now;
        var timeStr = now.ToString("HH:mm");
        var secStr = now.ToString("ss");
        var dateStr = now.ToString("dddd, MMM dd yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();

        var ftTime = new FormattedText(timeStr, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, BoldTypeface, 110, Brushes.White, 1.0);
        dc.DrawText(ftTime, new Point(240 - ftTime.Width / 2 - 10, 150));

        var ftSec = new FormattedText(secStr, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, BoldTypeface, 38, accentBrush, 1.0);
        dc.DrawText(ftSec, new Point(240 + ftTime.Width / 2, 175));

        var ftDate = new FormattedText(dateStr, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, SemiBoldTypeface, 16, new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 1.0);
        dc.DrawText(ftDate, new Point(240 - ftDate.Width / 2, 290));
    }

    private static void DrawArcReactor(DrawingContext dc, Brush accentBrush)
    {
        dc.DrawEllipse(null, new Pen(accentBrush, 12), new Point(240, 240), 180, 180);
        dc.DrawEllipse(null, new Pen(Brushes.White, 6), new Point(240, 240), 140, 140);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(160, 0, 210, 255)), new Pen(accentBrush, 4), new Point(240, 240), 70, 70);

        for (int i = 0; i < 10; i++)
        {
            double angle = i * 36.0 * Math.PI / 180.0;
            double x1 = 240 + Math.Cos(angle) * 75;
            double y1 = 240 + Math.Sin(angle) * 75;
            double x2 = 240 + Math.Cos(angle) * 170;
            double y2 = 240 + Math.Sin(angle) * 170;
            dc.DrawLine(new Pen(accentBrush, 5), new Point(x1, y1), new Point(x2, y2));
        }

        var ftText = new FormattedText("ARC CORE", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, BoldTypeface, 18, Brushes.White, 1.0);
        dc.DrawText(ftText, new Point(240 - ftText.Width / 2, 230));
    }

    private static void DrawCustomImage(DrawingContext dc, string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(Path.GetFullPath(path));
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 480;
            bitmap.DecodePixelHeight = 480;
            bitmap.EndInit();
            bitmap.Freeze();

            var clipGeom = new EllipseGeometry(new Point(240, 240), 238, 238);
            dc.PushClip(clipGeom);
            dc.DrawImage(bitmap, new Rect(0, 0, 480, 480));
            dc.Pop();
        }
        catch { }
    }

    private static PathGeometry CreateArcGeometry(Point center, double radius, double startAngleDeg, double sweepAngleDeg)
    {
        double endAngleDeg = startAngleDeg + sweepAngleDeg;
        double startRad = startAngleDeg * Math.PI / 180.0;
        double endRad = endAngleDeg * Math.PI / 180.0;

        var startPoint = new Point(center.X + radius * Math.Cos(startRad), center.Y + radius * Math.Sin(startRad));
        var endPoint = new Point(center.X + radius * Math.Cos(endRad), center.Y + radius * Math.Sin(endRad));
        bool isLargeArc = sweepAngleDeg > 180.0;

        var segment = new ArcSegment(endPoint, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true);
        var figure = new PathFigure(startPoint, new[] { segment }, false);
        var geom = new PathGeometry(new[] { figure });
        geom.Freeze();
        return geom;
    }
}
