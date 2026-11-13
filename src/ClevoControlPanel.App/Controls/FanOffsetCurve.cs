using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ClevoControlPanel.App.Controls;

public sealed class FanOffsetCurve : FrameworkElement
{
    public static readonly DependencyProperty OffsetPercentProperty = DependencyProperty.Register(
        nameof(OffsetPercent), typeof(double), typeof(FanOffsetCurve),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(FanOffsetCurve),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DefaultBrushProperty = DependencyProperty.Register(
        nameof(DefaultBrush), typeof(Brush), typeof(FanOffsetCurve),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(FanOffsetCurve),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush), typeof(Brush), typeof(FanOffsetCurve),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public double OffsetPercent
    {
        get => (double)GetValue(OffsetPercentProperty);
        set => SetValue(OffsetPercentProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public Brush DefaultBrush
    {
        get => (Brush)GetValue(DefaultBrushProperty);
        set => SetValue(DefaultBrushProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public Brush LabelBrush
    {
        get => (Brush)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width < 120 || height < 60)
            return;

        const double padLeft = 4;
        const double padRight = 4;
        const double padTop = 6;
        const double padBottom = 18;
        var plot = new Rect(padLeft, padTop, width - padLeft - padRight, height - padTop - padBottom);
        if (plot.Width <= 0 || plot.Height <= 0)
            return;

        var gridBrush = GridBrush ?? Brushes.LightGray;
        var gridPen = new Pen(gridBrush, 1) { DashStyle = new DashStyle([2.0, 4.0], 0) };
        gridPen.Freeze();

        for (int i = 1; i <= 3; i++)
        {
            double y = plot.Bottom - plot.Height * i / 4.0;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        var axisPen = new Pen(gridBrush, 1);
        axisPen.Freeze();
        dc.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        const int samples = 64;
        var basePoints = new Point[samples + 1];
        var currentPoints = new Point[samples + 1];
        double offset = Math.Clamp(OffsetPercent, 0, 100) / 100.0;

        for (int i = 0; i <= samples; i++)
        {
            double t = i / (double)samples;
            double baseLevel = BaseFanLevel(40 + t * 60);
            double currentLevel = Math.Min(1.0, baseLevel + offset);
            double x = plot.Left + t * plot.Width;
            basePoints[i] = new Point(x, plot.Bottom - baseLevel * plot.Height);
            currentPoints[i] = new Point(x, plot.Bottom - currentLevel * plot.Height);
        }

        var baseArea = BuildArea(plot, basePoints);
        dc.DrawGeometry(Translucent(DefaultBrush, 22), null, baseArea);

        if (offset > 0.001)
        {
            var band = BuildBand(basePoints, currentPoints);
            dc.DrawGeometry(Translucent(AccentBrush, 36), null, band);
        }

        var accentPen = new Pen(AccentBrush ?? Brushes.DodgerBlue, 2.2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        accentPen.Freeze();
        dc.DrawGeometry(null, accentPen, BuildLine(currentPoints));

        // Drawn last so the factory curve stays visible when the offset is zero.
        var basePen = new Pen(DefaultBrush ?? Brushes.Gray, 1.2) { DashStyle = new DashStyle([4.0, 3.0], 0) };
        basePen.Freeze();
        dc.DrawGeometry(null, basePen, BuildLine(basePoints));

        var labelBrush = LabelBrush ?? Brushes.Gray;
        DrawLabel(dc, "40°C", new Point(plot.Left, plot.Bottom + 4), labelBrush, 9.5);
        DrawLabel(dc, "70°C", new Point(plot.Left + plot.Width / 2, plot.Bottom + 4), labelBrush, 9.5, centered: true);
        DrawLabel(dc, "100°C", new Point(plot.Right, plot.Bottom + 4), labelBrush, 9.5, rightAligned: true);
    }

    private static double BaseFanLevel(double temperature)
    {
        double t = Math.Clamp((temperature - 40.0) / 55.0, 0.0, 1.0);
        return Math.Min(1.0, 0.15 + 0.85 * Math.Pow(t, 1.35));
    }

    private static Geometry BuildLine(Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: false);
            for (int i = 1; i < points.Length; i++)
                context.LineTo(points[i], isStroked: true, isSmoothJoin: true);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Geometry BuildArea(Rect plot, Point[] curve)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(plot.Left, plot.Bottom), isFilled: true, isClosed: true);
            foreach (var point in curve)
                context.LineTo(point, isStroked: true, isSmoothJoin: true);
            context.LineTo(new Point(plot.Right, plot.Bottom), isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Geometry BuildBand(Point[] basePoints, Point[] currentPoints)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(basePoints[0], isFilled: true, isClosed: true);
            foreach (var point in currentPoints)
                context.LineTo(point, isStroked: true, isSmoothJoin: true);
            for (int i = basePoints.Length - 1; i >= 0; i--)
                context.LineTo(basePoints[i], isStroked: true, isSmoothJoin: true);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Brush Translucent(Brush? brush, byte alpha)
    {
        if (brush is SolidColorBrush solid)
        {
            var result = new SolidColorBrush(Color.FromArgb(alpha, solid.Color.R, solid.Color.G, solid.Color.B));
            result.Freeze();
            return result;
        }

        return Brushes.Transparent;
    }

    private void DrawLabel(DrawingContext dc, string text, Point origin, Brush brush, double size,
        bool centered = false, bool rightAligned = false)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Text"),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        double x = origin.X;
        if (centered) x -= formatted.Width / 2;
        else if (rightAligned) x -= formatted.Width;
        dc.DrawText(formatted, new Point(x, origin.Y));
    }
}
