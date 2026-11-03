using System;
using System.Windows;
using System.Windows.Media;

namespace Nexus.App.Controls;

public class StereoVuMeter : FrameworkElement
{
    private static readonly Brush UnlitGreenBrush = new SolidColorBrush(Color.FromArgb(255, 18, 38, 30));
    private static readonly Brush UnlitAmberBrush = new SolidColorBrush(Color.FromArgb(255, 42, 32, 18));
    private static readonly Brush UnlitRedBrush = new SolidColorBrush(Color.FromArgb(255, 42, 18, 20));

    private static readonly Brush LitGreenBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // #10B981
    private static readonly Brush LitAmberBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // #F59E0B
    private static readonly Brush LitRedBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));    // #EF4444

    static StereoVuMeter()
    {
        UnlitGreenBrush.Freeze();
        UnlitAmberBrush.Freeze();
        UnlitRedBrush.Freeze();
        LitGreenBrush.Freeze();
        LitAmberBrush.Freeze();
        LitRedBrush.Freeze();
    }

    public static readonly DependencyProperty LevelLeftProperty =
        DependencyProperty.Register(nameof(LevelLeft), typeof(double), typeof(StereoVuMeter),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LevelRightProperty =
        DependencyProperty.Register(nameof(LevelRight), typeof(double), typeof(StereoVuMeter),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SegmentCountProperty =
        DependencyProperty.Register(nameof(SegmentCount), typeof(int), typeof(StereoVuMeter),
            new FrameworkPropertyMetadata(22, FrameworkPropertyMetadataOptions.AffectsRender));

    public double LevelLeft
    {
        get => (double)GetValue(LevelLeftProperty);
        set => SetValue(LevelLeftProperty, value);
    }

    public double LevelRight
    {
        get => (double)GetValue(LevelRightProperty);
        set => SetValue(LevelRightProperty, value);
    }

    public int SegmentCount
    {
        get => (int)GetValue(SegmentCountProperty);
        set => SetValue(SegmentCountProperty, value);
    }

    public StereoVuMeter()
    {
        Width = 14;
        Height = 240;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        int count = Math.Max(4, SegmentCount);
        double gap = 1.5;
        double totalGaps = gap * (count - 1);
        double segHeight = Math.Max(1.0, (h - totalGaps) / count);

        double barWidth = Math.Max(2.0, (w - 3.0) / 2.0);
        double leftX = 0;
        double rightX = leftX + barWidth + 3.0;

        double normLeft = Math.Clamp(LevelLeft > 1.0 ? LevelLeft / 100.0 : LevelLeft, 0.0, 1.0);
        double normRight = Math.Clamp(LevelRight > 1.0 ? LevelRight / 100.0 : LevelRight, 0.0, 1.0);

        for (int i = 0; i < count; i++)
        {
            // Bottom to top
            double normPos = (double)(i + 1) / count;
            double y = h - ((i + 1) * segHeight + i * gap);

            // Determine LED color bracket
            Brush litBrush;
            Brush unlitBrush;

            if (normPos <= 0.65)
            {
                litBrush = LitGreenBrush;
                unlitBrush = UnlitGreenBrush;
            }
            else if (normPos <= 0.85)
            {
                litBrush = LitAmberBrush;
                unlitBrush = UnlitAmberBrush;
            }
            else
            {
                litBrush = LitRedBrush;
                unlitBrush = UnlitRedBrush;
            }

            // Left segment
            Brush currentLeftBrush = normLeft >= normPos ? litBrush : unlitBrush;
            dc.DrawRoundedRectangle(currentLeftBrush, null, new Rect(leftX, y, barWidth, segHeight), 1, 1);

            // Right segment
            Brush currentRightBrush = normRight >= normPos ? litBrush : unlitBrush;
            dc.DrawRoundedRectangle(currentRightBrush, null, new Rect(rightX, y, barWidth, segHeight), 1, 1);
        }
    }
}
