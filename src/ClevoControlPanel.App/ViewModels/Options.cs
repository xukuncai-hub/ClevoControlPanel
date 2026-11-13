using System;
using System.Windows.Media;
using ClevoControlPanel.Core.Models;
using Wpf.Ui.Controls;

namespace ClevoControlPanel.App.ViewModels;

public sealed class PowerModeOption
{
    public PowerModeOption(
        PowerMode mode,
        string title,
        string subtitle,
        string detail,
        SymbolRegular icon,
        string accentHex)
    {
        Mode = mode;
        Title = title;
        Subtitle = subtitle;
        Detail = detail;
        Icon = icon;

        Color accent = (Color)ColorConverter.ConvertFromString(accentHex);
        AccentColor = accent;
        AccentBrush = Freeze(new SolidColorBrush(accent));
        IconBackgroundBrush = Freeze(new SolidColorBrush(Color.FromArgb(42, accent.R, accent.G, accent.B)));
        SelectedBackgroundBrush = Freeze(new SolidColorBrush(Color.FromArgb(24, accent.R, accent.G, accent.B)));
    }

    public PowerMode Mode { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Detail { get; }
    public SymbolRegular Icon { get; }
    public Color AccentColor { get; }
    public Brush AccentBrush { get; }
    public Brush IconBackgroundBrush { get; }
    public Brush SelectedBackgroundBrush { get; }

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}

public sealed record FanModeOption(FanMode Mode, string Title, string Subtitle, SymbolRegular Icon);

public sealed record EffectOption(RgbEffectMode Mode, string Title, SymbolRegular Icon);

public sealed record SleepOption(int Seconds, string Title);

public sealed class ColorPresetOption
{
    public ColorPresetOption(string name, RgbColor color)
    {
        Name = name;
        Color = color;
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.Red, color.Green, color.Blue));
        brush.Freeze();
        Brush = brush;
    }

    public string Name { get; }
    public RgbColor Color { get; }
    public Brush Brush { get; }
}

public static class OptionExtensions
{
    public static PowerModeOption? FindOption(this IEnumerable<PowerModeOption> options, PowerMode mode)
    {
        foreach (var opt in options)
            if (opt.Mode == mode) return opt;
        return null;
    }

    public static FanModeOption? FindOption(this IEnumerable<FanModeOption> options, FanMode mode)
    {
        foreach (var opt in options)
            if (opt.Mode == mode) return opt;
        return null;
    }
}
