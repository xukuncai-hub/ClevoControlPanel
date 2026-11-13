using System;
using System.Linq;
using System.Threading.Tasks;
using ClevoControlPanel.Core.Models;
using ClevoControlPanel.Core.Native;
using ClevoControlPanel.Core.Services;
using FluentAssertions;
using Xunit;

namespace ClevoControlPanel.Core.Tests;

public class HardwareServiceTests
{
    [Fact]
    public void Constructor_LoadsPersistedPowerFanAndOffset()
    {
        var fake = FakeDchuNative.CreateInitialized();
        using var service = new ClevoHwService(fake);

        service.IsLiveHardware.Should().BeTrue();
        service.GetPowerMode().Should().Be(PowerMode.Entertainment);
        service.GetFanMode().Should().Be(FanMode.Auto);
        service.GetFanOffset().Should().Be(10);
        service.GetTurboFan().Should().BeFalse();
    }

    [Fact]
    public void SetPowerMode_PersistsSendsPacketAndRestoresTargetFanProfile()
    {
        var fake = FakeDchuNative.CreateInitialized();
        using var service = new ClevoHwService(fake);
        fake.Commands.Clear();

        service.SetPowerMode(PowerMode.Performance);

        service.GetPowerMode().Should().Be(PowerMode.Performance);
        fake.Settings[NativeConstants.AppSettings.PowerMode].Should().Be((byte)PowerMode.Performance);

        // Current Entertainment profile saved before switching.
        fake.Settings[NativeConstants.AppSettings.FanMode_Entertainment].Should().Be((byte)FanMode.Auto);
        fake.Settings[NativeConstants.AppSettings.FanOffset_Entertainment].Should().Be(10);

        // Performance profile (Maximum, 50%) restored.
        service.GetFanMode().Should().Be(FanMode.Maximum);
        service.GetFanOffset().Should().Be(50);
        fake.Settings[NativeConstants.AppSettings.FanMode].Should().Be((byte)FanMode.Maximum);
        fake.Settings[NativeConstants.AppSettings.FanOffset].Should().Be(50);

        fake.Commands.Should().Contain(c => c.Command == 121 && c.Payload.SequenceEqual(new byte[] { 2, 0, 0, 25 }));
        fake.Commands.Should().Contain(c => c.Command == 121 && c.Payload.SequenceEqual(new byte[] { 1, 0, 0, 1 }));
        fake.Commands.Should().Contain(c => c.Command == 121 && c.Payload.SequenceEqual(new byte[] { 128, 0, 0, 14 }));
    }

    [Fact]
    public void SetPowerMode_QuietDisarmsTurboFan()
    {
        var fake = FakeDchuNative.CreateInitialized();
        fake.Settings[NativeConstants.AppSettings.TurboFan_Status] = 1;
        using var service = new ClevoHwService(fake);
        service.GetTurboFan().Should().BeTrue();
        fake.Commands.Clear();

        service.SetPowerMode(PowerMode.Quiet);

        service.GetTurboFan().Should().BeFalse();
        fake.Settings[NativeConstants.AppSettings.TurboFan_Status].Should().Be(0);
        fake.Commands.Should().Contain(c => c.Command == 121 && c.Payload.SequenceEqual(new byte[] { 2, 0, 0, 25 }));
    }

    [Fact]
    public void SetFanMode_PersistsGlobalAndPerPowerModeMemory()
    {
        var fake = FakeDchuNative.CreateInitialized();
        using var service = new ClevoHwService(fake);
        fake.Commands.Clear();

        service.SetFanMode(FanMode.Silent);

        fake.Settings[NativeConstants.AppSettings.FanMode].Should().Be((byte)FanMode.Silent);
        fake.Settings[NativeConstants.AppSettings.FanMode_Entertainment].Should().Be((byte)FanMode.Silent);
        fake.Settings[NativeConstants.AppSettings.NoiselessFan_status].Should().Be(0);
        fake.Commands.Should().ContainSingle(c => c.Command == 121 && c.Payload.SequenceEqual(new byte[] { 2, 0, 0, 1 }));
    }

    [Fact]
    public void SetFanMode_NoiselessClearsTurboFan()
    {
        var fake = FakeDchuNative.CreateInitialized();
        fake.Settings[NativeConstants.AppSettings.TurboFan_Status] = 1;
        using var service = new ClevoHwService(fake);
        fake.Commands.Clear();

        service.SetFanMode(FanMode.Noiseless);

        fake.Settings[NativeConstants.AppSettings.NoiselessFan_status].Should().Be(1);
        fake.Settings[NativeConstants.AppSettings.TurboFan_Status].Should().Be(0);
        service.GetTurboFan().Should().BeFalse();
        fake.Commands.Should().Contain(c => c.Command == 121 && c.Payload.SequenceEqual(new byte[] { 2, 0, 0, 25 }));
        fake.Commands.Should().Contain(c => c.Command == 121 && c.Payload.SequenceEqual(new byte[] { 8, 0, 0, 1 }));
    }

    [Fact]
    public void SetFanOffset_ClampsAboveHundredAndScalesToRegisterByte()
    {
        var fake = FakeDchuNative.CreateInitialized();
        using var service = new ClevoHwService(fake);
        fake.Commands.Clear();

        service.SetFanOffset(150);

        service.GetFanOffset().Should().Be(100);
        fake.Settings[NativeConstants.AppSettings.FanOffset].Should().Be(100);
        fake.Settings[NativeConstants.AppSettings.FanOffset_Entertainment].Should().Be(100);
        fake.Commands.Should().ContainSingle(c => c.Command == 121 && c.Payload.SequenceEqual(new byte[] { 255, 0, 0, 14 }));
    }

    [Fact]
    public void SetTurboFan_EnablesAutoModeAndEmitsBitmask()
    {
        var fake = FakeDchuNative.CreateInitialized();
        using var service = new ClevoHwService(fake);
        service.SetFanMode(FanMode.Silent);
        fake.Commands.Clear();

        service.SetTurboFan(true);

        service.GetTurboFan().Should().BeTrue();
        service.GetFanMode().Should().Be(FanMode.Auto);
        fake.Settings[NativeConstants.AppSettings.TurboFan_Status].Should().Be(1);
        fake.Settings[NativeConstants.AppSettings.FanMode].Should().Be((byte)FanMode.Auto);
        fake.Settings[NativeConstants.AppSettings.NoiselessFan_status].Should().Be(0);
        fake.Commands.Should().Contain(c => c.Command == 121 && c.Payload.SequenceEqual(new byte[] { 0, 0, 0, 1 }));
        fake.Commands.Last().Payload.SequenceEqual(new byte[] { 66, 0, 0, 25 }).Should().BeTrue();

        fake.Commands.Clear();
        service.SetTurboFan(false);

        service.GetTurboFan().Should().BeFalse();
        fake.Settings[NativeConstants.AppSettings.TurboFan_Status].Should().Be(0);
        fake.Commands.Last().Payload.SequenceEqual(new byte[] { 2, 0, 0, 25 }).Should().BeTrue();
    }

    [Fact]
    public void TelemetryRead_ParsesLiveBuffer()
    {
        var fake = FakeDchuNative.CreateInitialized();
        using var service = new ClevoHwService(fake);

        var telemetry = service.GetTelemetry();

        telemetry.CpuTemperatureCelsius.Should().Be(87);
        telemetry.GpuTemperatureCelsius.Should().Be(76);
        telemetry.Fan1Rpm.Should().Be(7513);
        telemetry.Fan2Rpm.Should().Be(7359);
        telemetry.IsGpuActive.Should().BeTrue();
    }

    [Fact]
    public void GetTelemetry_WhenDriverFails_ReturnsSafeDefault()
    {
        var fake = FakeDchuNative.CreateInitialized();
        fake.TelemetryStatus = 0;
        using var service = new ClevoHwService(fake);

        var telemetry = service.GetTelemetry();

        telemetry.CpuTemperatureCelsius.Should().Be(0);
        telemetry.Fan1Rpm.Should().Be(0);
    }

    [Fact]
    public async Task StartTelemetry_RaisesBackgroundEventsAndStopsCleanly()
    {
        var fake = FakeDchuNative.CreateInitialized();
        using var service = new ClevoHwService(fake);

        var tcs = new TaskCompletionSource<TelemetryData>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.TelemetryUpdated += (_, data) => tcs.TrySetResult(data);

        service.StartTelemetry(50);
        service.IsTelemetryRunning.Should().BeTrue();

        TelemetryData data = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        data.Fan1Rpm.Should().Be(7513);
        data.CpuTemperatureCelsius.Should().Be(87);

        service.StopTelemetry();
        service.IsTelemetryRunning.Should().BeFalse();
    }

    [Fact]
    public void WriteFailure_SurfacesAsHwCommunicationException()
    {
        var fake = FakeDchuNative.CreateInitialized();
        fake.FailCommands = true;
        using var service = new ClevoHwService(fake);

        Action act = () => service.SetFanMode(FanMode.Silent);

        act.Should().Throw<HwCommunicationException>();
    }
}
