using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ClevoControlPanel.App.Controls;

public sealed class ArcGauge : FrameworkElement
{
    private static readonly Typeface ValueTypeface =
        new(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static readonly Typeface LabelTypeface =
        new(new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WarningThresholdProperty = DependencyProperty.Register(
        nameof(WarningThreshold), typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ArcGauge),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(ArcGauge),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SubTextProperty = DependencyProperty.Register(
        nameof(SubText), typeof(string), typeof(ArcGauge),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentColorProperty = DependencyProperty.Register(
        nameof(AccentColor), typeof(Color), typeof(ArcGauge),
        new FrameworkPropertyMetadata(Color.FromRgb(0x4C, 0xC2, 0xFF), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WarningColorProperty = DependencyProperty.Register(
        nameof(WarningColor), typeof(Color), typeof(ArcGauge),
        new FrameworkPropertyMetadata(Color.FromRgb(0xFF, 0x7A, 0x45), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(ArcGauge),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(ArcGauge),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MutedTextBrushProperty = DependencyProperty.Register(
        nameof(MutedTextBrush), typeof(Brush), typeof(ArcGauge),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

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

    public double WarningThreshold
    {
        get => (double)GetValue(WarningThresholdProperty);
        set => SetValue(WarningThresholdProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public string SubText
    {
        get => (string)GetValue(SubTextProperty);
        set => SetValue(SubTextProperty, value);
    }

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public Color WarningColor
    {
        get => (Color)GetValue(WarningColorProperty);
        set => SetValue(WarningColorProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush TextBrush
    {
        get => (Brush)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public Brush MutedTextBrush
    {
        get => (Brush)GetValue(MutedTextBrushProperty);
        set => SetValue(MutedTextBrushProperty, value);
    }

    public ArcGauge()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 180 : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? 180 : availableSize.Height;
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 48)
            return;

        double stroke = Math.Max(7.0, size * 0.065);
        double radius = (size - stroke - 16) / 2.0;
        var center = new Point(ActualWidth / 2.0, ActualHeight / 2.0 + size * 0.015);

        const double startAngle = 135.0;
        const double sweepAngle = 270.0;

        var trackPen = new Pen(TrackBrush, stroke)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        DrawArc(drawingContext, trackPen, center, radius, startAngle, sweepAngle);

        double fraction = Math.Clamp((Value - Minimum) / Math.Max(0.0001, Maximum - Minimum), 0.0, 1.0);
        if (fraction > 0.001)
        {
            bool warning = WarningThreshold > 0 && Value >= WarningThreshold;
            Color color = warning ? WarningColor : AccentColor;

            var glowBrush = new SolidColorBrush(Color.FromArgb(58, color.R, color.G, color.B));
            glowBrush.Freeze();
            var glowPen = new Pen(glowBrush, stroke * 1.9)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            DrawArc(drawingContext, glowPen, center, radius, startAngle, sweepAngle * fraction);

            var accentBrush = new SolidColorBrush(color);
            accentBrush.Freeze();
            var accentPen = new Pen(accentBrush, stroke)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            DrawArc(drawingContext, accentPen, center, radius, startAngle, sweepAngle * fraction);
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        if (!string.IsNullOrWhiteSpace(Title))
        {
            var title = MakeText(Title, size * 0.068, MutedTextBrush, pixelsPerDip);
            drawingContext.DrawText(title, new Point(center.X - title.Width / 2, center.Y - size * 0.235));
        }

        bool isD3cold = !string.IsNullOrEmpty(SubText) && SubText.Contains("D3cold", StringComparison.OrdinalIgnoreCase);
        string valueText = isD3cold
            ? "待机"
            : (Value >= 1000
                ? Math.Round(Value).ToString("N0", CultureInfo.CurrentCulture)
                : Math.Round(Value).ToString("F0", CultureInfo.CurrentCulture));

        double fontSize = isD3cold ? size * 0.165 : size * 0.215;
        var value = MakeText(valueText, fontSize, TextBrush, pixelsPerDip, ValueTypeface);
        drawingContext.DrawText(value, new Point(center.X - value.Width / 2, center.Y - value.Height / 2 - size * 0.015));

        if (!string.IsNullOrWhiteSpace(Unit) && !isD3cold)
        {
            var unit = MakeText(Unit, size * 0.072, MutedTextBrush, pixelsPerDip);
            drawingContext.DrawText(unit, new Point(center.X - unit.Width / 2, center.Y + size * 0.105));
        }

        if (!string.IsNullOrWhiteSpace(SubText))
        {
            var sub = MakeText(SubText, size * 0.062, MutedTextBrush, pixelsPerDip);
            drawingContext.DrawText(sub, new Point(center.X - sub.Width / 2, center.Y + size * 0.205));
        }
    }

    private static FormattedText MakeText(string text, double size, Brush brush, double pixelsPerDip, Typeface? typeface = null)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface ?? LabelTypeface,
            size,
            brush,
            pixelsPerDip);
    }

    private static void DrawArc(DrawingContext dc, Pen pen, Point center, double radius, double startDegrees, double sweepDegrees)
    {
        double startRad = startDegrees * Math.PI / 180.0;
        double endRad = (startDegrees + sweepDegrees) * Math.PI / 180.0;

        var start = new Point(center.X + radius * Math.Cos(startRad), center.Y + radius * Math.Sin(startRad));
        var end = new Point(center.X + radius * Math.Cos(endRad), center.Y + radius * Math.Sin(endRad));

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(
                end,
                new Size(radius, radius),
                0,
                sweepDegrees > 180,
                SweepDirection.Clockwise,
                true,
                false);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}
