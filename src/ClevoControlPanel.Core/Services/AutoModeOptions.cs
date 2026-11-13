using System;

namespace ClevoControlPanel.Core.Services;

/// <summary>
/// Tuning knobs for the power-adaptive controller. Scores are 0..100.
/// </summary>
public sealed class AutoModeOptions
{
    public double QuietEnterScore { get; init; } = 24;
    public double QuietExitScore { get; init; } = 34;
    public double PerformanceEnterScore { get; init; } = 72;
    public double PerformanceExitScore { get; init; } = 58;
    public double BatterySavingEnterScore { get; init; } = 36;
    public double BatterySavingExitScore { get; init; } = 48;

    public int CpuThermalLimitCelsius { get; init; } = 92;
    public int GpuThermalLimitCelsius { get; init; } = 88;

    public int UpSwitchSamples { get; init; } = 3;
    public int DownSwitchSamples { get; init; } = 6;
    public int ThermalSwitchSamples { get; init; } = 2;
    public int MinSamplesBetweenSwitches { get; init; } = 4;

    public static AutoModeOptions Default { get; } = new();
}
