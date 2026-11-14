using System;
using ClevoControlPanel.Core.Models;

namespace ClevoControlPanel.Core.Services;

/// <summary>
/// Power-adaptive profile selector. Estimates a 0..100 power/load index from CPU
/// utilization, CPU/GPU temperatures and AC/battery state, then switches the
/// hardware power mode with hysteresis, dwell counters and a switch cooldown.
/// </summary>
public sealed class AutoModeController
{
    private readonly object _lock = new();
    private readonly IHwService _hw;
    private PowerMode? _candidate;
    private int _candidateCount;
    private int _samplesSinceSwitch;

    public AutoModeController(IHwService hw, AutoModeOptions? options = null)
    {
        _hw = hw ?? throw new ArgumentNullException(nameof(hw));
        Options = options ?? AutoModeOptions.Default;
    }

    public AutoModeOptions Options { get; }

    public bool IsEnabled { get; private set; }

    public PowerMode EffectiveMode { get; private set; } = PowerMode.Entertainment;

    public PowerMode? CandidateMode => _candidate;

    public double LastPowerIndex { get; private set; }

    public double LastCpuLoadPercent { get; private set; }

    public bool LastSampleWasOnBattery { get; private set; }

    public string? LastError { get; private set; }

    public void Enable()
    {
        lock (_lock)
        {
            IsEnabled = true;

            try
            {
                EffectiveMode = _hw.GetPowerMode();
            }
            catch
            {
                EffectiveMode = PowerMode.Entertainment;
            }

            _candidate = null;
            _candidateCount = 0;
            _samplesSinceSwitch = 0;
            LastError = null;
        }
    }

    public void Disable()
    {
        lock (_lock)
        {
            IsEnabled = false;
            _candidate = null;
            _candidateCount = 0;
        }
    }

    /// <summary>
    /// Feeds one sample (typically 1 Hz) and returns the effective power mode.
    /// Safe to call while disabled; it then leaves hardware untouched.
    /// </summary>
    public PowerMode Observe(TelemetryData telemetry, double cpuLoadPercent, bool onBattery)
    {
        ArgumentNullException.ThrowIfNull(telemetry);

        lock (_lock)
        {
            if (!IsEnabled)
                return EffectiveMode;

            _samplesSinceSwitch++;
            _samplesSinceSwitch = Math.Min(_samplesSinceSwitch, 1_000_000);

            LastCpuLoadPercent = Math.Clamp(cpuLoadPercent, 0, 100);
            LastSampleWasOnBattery = onBattery;
            LastPowerIndex = ComputePowerIndex(telemetry, LastCpuLoadPercent);

            bool thermal = IsThermalOverride(telemetry);
            PowerMode target = DecideTarget(LastPowerIndex, onBattery, thermal);

            if (target == EffectiveMode)
            {
                _candidate = null;
                _candidateCount = 0;
                return EffectiveMode;
            }

            if (_candidate != target)
            {
                _candidate = target;
                _candidateCount = 1;
            }
            else
            {
                _candidateCount++;
            }

            int required = thermal
                ? Options.ThermalSwitchSamples
                : ModeRank(target) > ModeRank(EffectiveMode)
                    ? Options.UpSwitchSamples
                    : Options.DownSwitchSamples;

            bool cooldownElapsed = thermal || _samplesSinceSwitch >= Options.MinSamplesBetweenSwitches;

            if (_candidateCount >= required && cooldownElapsed)
                ApplyTarget(target);

            return EffectiveMode;
        }
    }

    private void ApplyTarget(PowerMode target)
    {
        try
        {
            _hw.SetPowerMode(target);
            EffectiveMode = target;
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        _candidate = null;
        _candidateCount = 0;
        _samplesSinceSwitch = 0;
    }

    private PowerMode DecideTarget(double index, bool onBattery, bool thermal)
    {
        if (thermal)
            return PowerMode.Performance;

        if (onBattery)
        {
            if (EffectiveMode == PowerMode.PowerSaving)
                return index < Options.BatterySavingExitScore ? PowerMode.PowerSaving : PowerMode.Entertainment;

            if (EffectiveMode == PowerMode.Performance && index >= Options.PerformanceExitScore)
                return PowerMode.Performance;

            return index < Options.BatterySavingEnterScore
                ? PowerMode.PowerSaving
                : PowerMode.Entertainment;
        }

        switch (EffectiveMode)
        {
            case PowerMode.Quiet:
                if (index <= Options.QuietExitScore)
                    return PowerMode.Quiet;
                return index >= Options.PerformanceEnterScore ? PowerMode.Performance : PowerMode.Entertainment;

            case PowerMode.Performance:
                if (index >= Options.PerformanceExitScore)
                    return PowerMode.Performance;
                return index <= Options.QuietEnterScore ? PowerMode.Quiet : PowerMode.Entertainment;

            default:
                if (index <= Options.QuietEnterScore)
                    return PowerMode.Quiet;
                if (index >= Options.PerformanceEnterScore)
                    return PowerMode.Performance;
                return PowerMode.Entertainment;
        }
    }

    private bool IsThermalOverride(TelemetryData telemetry)
    {
        if (telemetry.CpuTemperatureCelsius >= Options.CpuThermalLimitCelsius)
            return true;

        return telemetry.IsGpuActive &&
               telemetry.GpuTemperatureCelsius >= Options.GpuThermalLimitCelsius;
    }

    private static double ComputePowerIndex(TelemetryData telemetry, double cpuLoadPercent)
    {
        // 1. Normalized thermal scores (0..100)
        // CPU baseline 45°C -> 0, 90°C -> 100
        double cpuNormalized = Math.Clamp((telemetry.CpuTemperatureCelsius - 45.0) / 45.0 * 100.0, 0, 100);

        // GPU baseline 45°C -> 0, 83°C -> 100 (discrete laptop GPUs operate at lower thermal targets than CPUs)
        double gpuNormalized = telemetry.IsGpuActive
            ? Math.Clamp((telemetry.GpuTemperatureCelsius - 45.0) / 38.0 * 100.0, 0, 100)
            : 0;

        // 2. Dual-source thermal stress: whichever chip is hotter dominates the cooling and power profile
        double maxTempNorm = Math.Max(cpuNormalized, gpuNormalized);
        double minTempNorm = Math.Min(cpuNormalized, gpuNormalized);
        double thermalStress = (maxTempNorm * 0.70) + (minTempNorm * 0.30);

        // 3. System activity estimation:
        // In modern gaming or GPU workloads, CPU overall utilization is often only 15-30% on multi-core chips,
        // while the GPU is saturated at 100% and drawing heavy wattage.
        // When the discrete GPU is active and elevated in temperature, lift the effective activity floor.
        double effectiveLoad = Math.Clamp(cpuLoadPercent, 0, 100);
        if (telemetry.IsGpuActive && telemetry.GpuTemperatureCelsius >= 60)
        {
            // At GPU 60°C -> boost begins; at GPU 76°C+ -> guarantees at least ~55-65% effective activity
            double gpuActivityFloor = Math.Clamp((telemetry.GpuTemperatureCelsius - 55.0) / 25.0 * 65.0, 0, 65);
            effectiveLoad = Math.Max(effectiveLoad, gpuActivityFloor);
        }

        // 4. Combine effective activity (40%) and thermal stress (60%)
        // Prioritizing thermal stress ensures the system responds quickly to elevated temperatures
        // and unlocks performance mode before thermal throttling occurs.
        return Math.Clamp((effectiveLoad * 0.40) + (thermalStress * 0.60), 0, 100);
    }

    private static int ModeRank(PowerMode mode) => mode switch
    {
        PowerMode.PowerSaving => 0,
        PowerMode.Quiet => 1,
        PowerMode.Entertainment => 2,
        PowerMode.Performance => 3,
        _ => 2
    };
}
