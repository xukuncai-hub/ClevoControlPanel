using System;
using ClevoControlPanel.Core.Models;
using ClevoControlPanel.Core.Services;
using FluentAssertions;
using Xunit;

namespace ClevoControlPanel.Core.Tests;

public class AutoModeControllerTests
{
    private static AutoModeOptions FastOptions(int minSamples = 1) => new()
    {
        UpSwitchSamples = 2,
        DownSwitchSamples = 3,
        ThermalSwitchSamples = 1,
        MinSamplesBetweenSwitches = minSamples
    };

    private static TelemetryData Sample(int cpuTemp, int gpuTemp, bool gpuActive = true) => new()
    {
        CpuTemperatureCelsius = cpuTemp,
        GpuTemperatureCelsius = gpuTemp,
        IsGpuActive = gpuActive,
        Fan1Rpm = 3000,
        Fan2Rpm = gpuActive ? 3000 : 0
    };

    private static TelemetryData HighLoadSample() => Sample(90, 80);

    private static TelemetryData MediumSample() => Sample(75, 75);

    private static TelemetryData LowLoadSample() => Sample(48, 0, gpuActive: false);

    [Fact]
    public void Enable_ReadsCurrentHardwareMode_AndDoesNotSwitchImmediately()
    {
        using var hw = new MockHwService();
        var controller = new AutoModeController(hw, FastOptions());

        controller.Enable();

        controller.EffectiveMode.Should().Be(PowerMode.Entertainment);
        hw.GetPowerMode().Should().Be(PowerMode.Entertainment);
    }

    [Fact]
    public void HighSustainedLoad_SwitchesToPerformance_AfterDwell()
    {
        using var hw = new MockHwService();
        var controller = new AutoModeController(hw, FastOptions());
        controller.Enable();

        controller.Observe(HighLoadSample(), 100, onBattery: false);
        controller.EffectiveMode.Should().Be(PowerMode.Entertainment);

        controller.Observe(HighLoadSample(), 100, onBattery: false);
        controller.EffectiveMode.Should().Be(PowerMode.Performance);
        hw.GetPowerMode().Should().Be(PowerMode.Performance);
    }

    [Fact]
    public void GpuGaming_ElevatedTempsWithLowCpuLoad_SwitchesToPerformance()
    {
        using var hw = new MockHwService();
        var controller = new AutoModeController(hw, FastOptions());
        controller.Enable();

        // Real-world scenario: RTX 4060 gaming load with GPU at 77°C, CPU at 84°C, but CPU load is only 18%
        var gamingSample = Sample(cpuTemp: 84, gpuTemp: 77, gpuActive: true);

        controller.Observe(gamingSample, cpuLoadPercent: 18, onBattery: false);
        controller.EffectiveMode.Should().Be(PowerMode.Entertainment);

        controller.Observe(gamingSample, cpuLoadPercent: 18, onBattery: false);
        controller.EffectiveMode.Should().Be(PowerMode.Performance);
        hw.GetPowerMode().Should().Be(PowerMode.Performance);
        controller.LastPowerIndex.Should().BeGreaterThanOrEqualTo(64);
    }

    [Fact]
    public void LowLoad_SwitchesToQuiet_AfterDownDwell()
    {
        using var hw = new MockHwService();
        var controller = new AutoModeController(hw, FastOptions());
        controller.Enable();

        controller.Observe(LowLoadSample(), 2, onBattery: false);
        controller.Observe(LowLoadSample(), 2, onBattery: false);
        controller.EffectiveMode.Should().Be(PowerMode.Entertainment);

        controller.Observe(LowLoadSample(), 2, onBattery: false);
        controller.EffectiveMode.Should().Be(PowerMode.Quiet);
        hw.GetPowerMode().Should().Be(PowerMode.Quiet);
    }

    [Fact]
    public void Hysteresis_PreventsFlappingAroundPerformanceThreshold()
    {
        using var hw = new MockHwService();
        var controller = new AutoModeController(hw, FastOptions());
        controller.Enable();

        controller.Observe(HighLoadSample(), 100, false);
        controller.Observe(HighLoadSample(), 100, false);
        controller.EffectiveMode.Should().Be(PowerMode.Performance);

        // Index is below the enter threshold but above the exit threshold: stay Performance.
        for (int i = 0; i < 4; i++)
            controller.Observe(MediumSample(), 70, false);

        controller.EffectiveMode.Should().Be(PowerMode.Performance);

        // Falls below the exit threshold, then completes the down dwell.
        controller.Observe(Sample(60, 50), 40, false);
        controller.Observe(Sample(60, 50), 40, false);
        controller.EffectiveMode.Should().Be(PowerMode.Performance);
        controller.Observe(Sample(60, 50), 40, false);
        controller.EffectiveMode.Should().Be(PowerMode.Entertainment);
    }

    [Fact]
    public void ThermalOverride_SwitchesToPerformance_WithoutCooldown()
    {
        using var hw = new MockHwService();
        var options = new AutoModeOptions
        {
            UpSwitchSamples = 10,
            DownSwitchSamples = 10,
            ThermalSwitchSamples = 1,
            MinSamplesBetweenSwitches = 20
        };
        var controller = new AutoModeController(hw, options);
        controller.Enable();

        controller.Observe(Sample(95, 70), 2, onBattery: false);

        controller.EffectiveMode.Should().Be(PowerMode.Performance);
        hw.GetPowerMode().Should().Be(PowerMode.Performance);
        controller.LastPowerIndex.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BatteryPower_PrefersPowerSaving_AndNeverSelectsPerformanceByLoad()
    {
        using var hw = new MockHwService();
        var controller = new AutoModeController(hw, FastOptions());
        controller.Enable();

        for (int i = 0; i < 3; i++)
            controller.Observe(LowLoadSample(), 2, onBattery: true);
        controller.EffectiveMode.Should().Be(PowerMode.PowerSaving);

        for (int i = 0; i < 3; i++)
            controller.Observe(HighLoadSample(), 100, onBattery: true);
        controller.EffectiveMode.Should().Be(PowerMode.Entertainment);
        controller.LastSampleWasOnBattery.Should().BeTrue();
    }

    [Fact]
    public void BatteryPower_ThermalEmergency_StillUsesPerformance()
    {
        using var hw = new MockHwService();
        var controller = new AutoModeController(hw, FastOptions());
        controller.Enable();

        controller.Observe(Sample(96, 70), 10, onBattery: true);

        controller.EffectiveMode.Should().Be(PowerMode.Performance);
    }

    [Fact]
    public void Disable_StopsAllSwitching()
    {
        using var hw = new MockHwService();
        var controller = new AutoModeController(hw, FastOptions());
        controller.Enable();
        controller.Disable();

        for (int i = 0; i < 10; i++)
            controller.Observe(HighLoadSample(), 100, onBattery: false);

        controller.EffectiveMode.Should().Be(PowerMode.Entertainment);
        hw.GetPowerMode().Should().Be(PowerMode.Entertainment);
    }

    [Fact]
    public void PowerIndex_ReflectsLoadTemperaturesAndGpuState()
    {
        using var hw = new MockHwService();
        var controller = new AutoModeController(hw, FastOptions());
        controller.Enable();

        controller.Observe(Sample(48, 0, gpuActive: false), 2, false);
        double idleIndex = controller.LastPowerIndex;

        controller.Observe(HighLoadSample(), 100, false);
        double loadIndex = controller.LastPowerIndex;

        idleIndex.Should().BeInRange(0, 100);
        loadIndex.Should().BeGreaterThan(idleIndex);
        loadIndex.Should().BeGreaterThan(60);
    }

    [Fact]
    public void CpuLoadSampler_ReturnsZeroOnFirstSample_ThenValidPercent()
    {
        var sampler = new ClevoControlPanel.Core.Telemetry.CpuLoadSampler();

        sampler.Sample().Should().Be(0);
        System.Threading.Thread.Sleep(80);
        double second = sampler.Sample();

        second.Should().BeInRange(0, 100);
    }

    [Fact]
    public void SystemPowerState_CanBeReadWithoutThrowing()
    {
        Action act = () =>
        {
            _ = ClevoControlPanel.Core.Telemetry.SystemPowerState.IsOnAcPower;
            _ = ClevoControlPanel.Core.Telemetry.SystemPowerState.HasBattery;
        };

        act.Should().NotThrow();
    }
}
