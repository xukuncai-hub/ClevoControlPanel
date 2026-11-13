using System;
using ClevoControlPanel.Core.Models;
using ClevoControlPanel.Core.Services;
using FluentAssertions;
using Xunit;

namespace ClevoControlPanel.Core.Tests;

public class HwServiceFactoryTests
{
    [Fact]
    public void Create_WhenProbeFails_ReturnsSimulationService()
    {
        var fake = FakeDchuNative.CreateInitialized();
        fake.Supported = false;

        using IHwService service = HwServiceFactory.Create(fake, out string status);

        service.Should().BeOfType<MockHwService>();
        service.IsLiveHardware.Should().BeFalse();
        status.Should().Contain("模拟模式");
    }

    [Fact]
    public void Create_WhenProbeSucceeds_ReturnsLiveService()
    {
        var fake = FakeDchuNative.CreateInitialized();

        using IHwService service = HwServiceFactory.Create(fake, out string status);

        service.Should().BeOfType<ClevoHwService>();
        service.IsLiveHardware.Should().BeTrue();
        status.Should().Contain("Clevo");
    }

    [Fact]
    public void Create_WhenTelemetryIsUnavailable_FallsBackToSimulation()
    {
        var fake = FakeDchuNative.CreateInitialized();
        fake.TelemetryStatus = 0;

        using IHwService service = HwServiceFactory.Create(fake, out string status);

        service.Should().BeOfType<MockHwService>();
        status.Should().Contain("遥测");
    }

    [Fact]
    public void CreateMock_AlwaysReturnsSimulation()
    {
        using IHwService service = HwServiceFactory.CreateMock();

        service.Should().BeOfType<MockHwService>();
        service.IsLiveHardware.Should().BeFalse();
    }
}

public class MockHwServiceTests
{
    [Fact]
    public void Telemetry_IsPlausibleAndResponsiveToPowerMode()
    {
        using var service = new MockHwService();

        var entertainment = service.GetTelemetry();
        entertainment.CpuTemperatureCelsius.Should().BeInRange(30, 98);
        entertainment.Fan1Rpm.Should().BeGreaterThan(0);
        entertainment.IsGpuActive.Should().BeTrue();

        service.SetPowerMode(PowerMode.Performance);
        var performance = service.GetTelemetry();
        performance.CpuTemperatureCelsius.Should().BeInRange(30, 98);
        performance.Fan1Rpm.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PowerSaving_PutsDiscreteGpuIntoStandby()
    {
        using var service = new MockHwService();

        service.SetPowerMode(PowerMode.PowerSaving);
        var telemetry = service.GetTelemetry();

        telemetry.IsGpuActive.Should().BeFalse();
        telemetry.IsGpuStandby.Should().BeTrue();
        telemetry.GpuTemperatureCelsius.Should().Be(0);
        telemetry.Fan2Rpm.Should().Be(0);
    }

    [Fact]
    public void PowerModeSwitch_PreservesAndRestoresFanProfiles()
    {
        using var service = new MockHwService();

        service.SetPowerMode(PowerMode.Performance);
        service.GetFanMode().Should().Be(FanMode.Maximum);
        service.GetFanOffset().Should().Be(50);

        service.SetPowerMode(PowerMode.Entertainment);
        service.GetFanMode().Should().Be(FanMode.Auto);
        service.GetFanOffset().Should().Be(0);

        service.SetPowerMode(PowerMode.Performance);
        service.GetFanMode().Should().Be(FanMode.Maximum);
        service.GetFanOffset().Should().Be(50);
    }

    [Fact]
    public void TurboAndNoiseless_AreMutuallyExclusive()
    {
        using var service = new MockHwService();

        service.SetFanMode(FanMode.Noiseless);
        service.GetFanMode().Should().Be(FanMode.Noiseless);

        service.SetTurboFan(true);
        service.GetTurboFan().Should().BeTrue();
        service.GetFanMode().Should().Be(FanMode.Auto);

        service.SetFanMode(FanMode.Noiseless);
        service.GetTurboFan().Should().BeFalse();
        service.GetFanMode().Should().Be(FanMode.Noiseless);
    }

    [Fact]
    public void KeyboardState_TracksColorsEffectsBrightnessAndPower()
    {
        using var service = new MockHwService();
        var color = new RgbColor(12, 34, 56);

        service.SetKeyboardRgbColor(color, KeyboardZone.Left);
        service.SetKeyboardRgbEffect(RgbEffectMode.Wave, 8);
        service.SetKeyboardBrightness(2);
        service.SetKeyboardSleepTimeout(60);
        service.SetKeyboardPower(false);

        var state = service.GetKeyboardState();
        state.LeftColor.Should().Be(color);
        state.RightColor.Should().Be(RgbColor.BlueColor);
        state.Mode.Should().Be(RgbEffectMode.Wave);
        state.Speed.Should().Be(8);
        state.Brightness.Should().Be(2);
        state.SleepTimeoutSeconds.Should().Be(60);
        state.IsPowered.Should().BeFalse();
        state.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void InvalidModes_AreRejected()
    {
        using var service = new MockHwService();

        Action power = () => service.SetPowerMode((PowerMode)9);
        Action fan = () => service.SetFanMode((FanMode)7);

        power.Should().Throw<ArgumentOutOfRangeException>();
        fan.Should().Throw<ArgumentOutOfRangeException>();
    }
}
