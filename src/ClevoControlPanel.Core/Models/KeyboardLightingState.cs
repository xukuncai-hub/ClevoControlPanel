namespace ClevoControlPanel.Core.Models;

/// <summary>
/// Immutable snapshot of the keyboard backlight configuration.
/// </summary>
public sealed record KeyboardLightingState
{
    public byte Brightness { get; init; } = 4;
    public bool IsPowered { get; init; } = true;
    public bool IsEnabled => IsPowered && Brightness > 0;
    public RgbEffectMode Mode { get; init; } = RgbEffectMode.Static;
    public byte Speed { get; init; } = 5;
    public int SleepTimeoutSeconds { get; init; } = 30;
    public RgbColor LeftColor { get; init; } = RgbColor.BlueColor;
    public RgbColor MiddleColor { get; init; } = RgbColor.BlueColor;
    public RgbColor RightColor { get; init; } = RgbColor.BlueColor;
}
