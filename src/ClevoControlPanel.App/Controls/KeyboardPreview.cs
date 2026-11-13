using System;
using System.Windows;
using System.Windows.Media;

namespace ClevoControlPanel.App.Controls;

public sealed class KeyboardPreview : FrameworkElement
{
    public static readonly DependencyProperty LeftColorProperty = DependencyProperty.Register(
        nameof(LeftColor), typeof(Color), typeof(KeyboardPreview),
        new FrameworkPropertyMetadata(Color.FromRgb(0, 0, 200), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MiddleColorProperty = DependencyProperty.Register(
        nameof(MiddleColor), typeof(Color), typeof(KeyboardPreview),
        new FrameworkPropertyMetadata(Color.FromRgb(0, 0, 200), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RightColorProperty = DependencyProperty.Register(
        nameof(RightColor), typeof(Color), typeof(KeyboardPreview),
        new FrameworkPropertyMetadata(Color.FromRgb(0, 0, 200), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BrightnessProperty = DependencyProperty.Register(
        nameof(Brightness), typeof(double), typeof(KeyboardPreview),
        new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsPoweredProperty = DependencyProperty.Register(
        nameof(IsPowered), typeof(bool), typeof(KeyboardPreview),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public Color LeftColor
    {
        get => (Color)GetValue(LeftColorProperty);
        set => SetValue(LeftColorProperty, value);
    }

    public Color MiddleColor
    {
        get => (Color)GetValue(MiddleColorProperty);
        set => SetValue(MiddleColorProperty, value);
    }

    public Color RightColor
    {
        get => (Color)GetValue(RightColorProperty);
        set => SetValue(RightColorProperty, value);
    }

    public double Brightness
    {
        get => (double)GetValue(BrightnessProperty);
        set => SetValue(BrightnessProperty, value);
    }

    public bool IsPowered
    {
        get => (bool)GetValue(IsPoweredProperty);
        set => SetValue(IsPoweredProperty, value);
    }

    public KeyboardPreview()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width < 120 || height < 70)
            return;

        double radius = Math.Min(20, height * 0.10);
        var body = new Rect(0, 0, width, height);

        var bodyBrush = new SolidColorBrush(Color.FromRgb(13, 15, 20));
        bodyBrush.Freeze();
        var bodyPen = new Pen(new SolidColorBrush(Color.FromArgb(44, 255, 255, 255)), 1);
        bodyPen.Brush.Freeze();
        bodyPen.Freeze();

        dc.DrawRoundedRectangle(bodyBrush, bodyPen, body, radius, radius);

        double glowStrength = IsPowered ? Math.Clamp(Brightness / 4.0, 0.0, 1.0) * 0.34 : 0.0;
        if (glowStrength > 0.001)
        {
            var glow = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5)
            };
            glow.GradientStops.Add(new GradientStop(WithAlpha(LeftColor, glowStrength), 0.0));
            glow.GradientStops.Add(new GradientStop(WithAlpha(LeftColor, glowStrength), 0.18));
            glow.GradientStops.Add(new GradientStop(WithAlpha(MiddleColor, glowStrength * 0.95), 0.5));
            glow.GradientStops.Add(new GradientStop(WithAlpha(RightColor, glowStrength), 0.82));
            glow.GradientStops.Add(new GradientStop(WithAlpha(RightColor, glowStrength), 1.0));
            glow.Freeze();

            dc.DrawRoundedRectangle(glow, null, body, radius, radius);

            var edge = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5)
            };
            edge.GradientStops.Add(new GradientStop(WithAlpha(LeftColor, 0.55), 0.0));
            edge.GradientStops.Add(new GradientStop(WithAlpha(MiddleColor, 0.50), 0.5));
            edge.GradientStops.Add(new GradientStop(WithAlpha(RightColor, 0.55), 1.0));
            edge.Freeze();

            var edgePen = new Pen(edge, 1.4);
            edgePen.Freeze();
            dc.DrawRoundedRectangle(null, edgePen, new Rect(0.7, 0.7, width - 1.4, height - 1.4), radius, radius);
        }

        var keyFill = new SolidColorBrush(Color.FromArgb(IsPowered ? (byte)20 : (byte)12, 255, 255, 255));
        keyFill.Freeze();
        var keyPen = new Pen(new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)), 1);
        keyPen.Brush.Freeze();
        keyPen.Freeze();

        double marginX = width * 0.042;
        double marginTop = height * 0.115;
        double marginBottom = height * 0.18;
        double availableWidth = width - marginX * 2;
        double availableHeight = height - marginTop - marginBottom;
        double gap = Math.Max(3.0, availableWidth * 0.0065);
        double rowGap = Math.Max(3.0, availableHeight * 0.030);
        double rowHeight = (availableHeight - rowGap * 4) / 5.0;

        dc.PushClip(new RectangleGeometry(body, radius, radius));

        int[] rowKeyCounts = [14, 14, 13, 12];
        for (int row = 0; row < rowKeyCounts.Length; row++)
        {
            int count = rowKeyCounts[row];
            double keyWidth = (availableWidth - gap * (count - 1)) / count;
            double y = marginTop + row * (rowHeight + rowGap);

            for (int key = 0; key < count; key++)
            {
                double x = marginX + key * (keyWidth + gap);
                DrawKey(dc, keyFill, keyPen, x, y, keyWidth, rowHeight);
            }
        }

        double[] bottomUnits = [1.25, 1.0, 1.25, 5.9, 1.25, 1.0, 1.0, 1.0, 1.0];
        double totalUnits = 0;
        foreach (double unit in bottomUnits)
            totalUnits += unit;

        double unitWidth = (availableWidth - gap * (bottomUnits.Length - 1)) / totalUnits;
        double bottomY = marginTop + 4 * (rowHeight + rowGap);
        double bottomX = marginX;
        foreach (double unit in bottomUnits)
        {
            double keyWidth = unitWidth * unit;
            DrawKey(dc, keyFill, keyPen, bottomX, bottomY, keyWidth, rowHeight);
            bottomX += keyWidth + gap;
        }

        dc.Pop();

        double barHeight = Math.Max(3.0, height * 0.014);
        double barY = height - marginBottom * 0.52;
        double barAlpha = IsPowered ? Math.Clamp(Brightness / 4.0, 0.15, 1.0) : 0.14;

        DrawZoneBar(dc, marginX, barY, availableWidth, barHeight, LeftColor, barAlpha);

        var sheen = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(18, 255, 255, 255), 0));
        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.32));
        sheen.Freeze();
        dc.DrawRoundedRectangle(sheen, null, body, radius, radius);
    }

    private static void DrawKey(DrawingContext dc, Brush fill, Pen pen, double x, double y, double width, double height)
    {
        double radius = Math.Min(4.0, height * 0.22);
        dc.DrawRoundedRectangle(fill, pen, new Rect(x, y, width, height), radius, radius);
    }

    private static void DrawZoneBar(DrawingContext dc, double x, double y, double width, double height, Color color, double alpha)
    {
        var brush = new SolidColorBrush(WithAlpha(color, alpha));
        brush.Freeze();
        dc.DrawRoundedRectangle(brush, null, new Rect(x, y, width, height), height / 2, height / 2);
    }

    private static Color WithAlpha(Color color, double alpha)
    {
        byte a = (byte)Math.Clamp(Math.Round(alpha * 255.0), 0, 255);
        return Color.FromArgb(a, color.R, color.G, color.B);
    }
}
