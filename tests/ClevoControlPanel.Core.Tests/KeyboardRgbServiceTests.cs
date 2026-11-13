using System;
using System.Linq;
using ClevoControlPanel.Core.Models;
using ClevoControlPanel.Core.Native;
using ClevoControlPanel.Core.Services;
using FluentAssertions;
using Xunit;

namespace ClevoControlPanel.Core.Tests;

public class KeyboardRgbServiceTests
{
    [Fact]
    public void Constructor_LoadsPersistedLightingState()
    {
        var fake = FakeDchuNative.CreateInitialized();
        var service = new KeyboardRgbService(fake);

        var state = service.GetState();

        state.Brightness.Should().Be(4);
        state.IsPowered.Should().BeTrue();
        state.Mode.Should().Be(RgbEffectMode.Static);
        state.Speed.Should().Be(5);
        state.SleepTimeoutSeconds.Should().Be(30);
        state.LeftColor.Should().Be(new RgbColor(0, 0, 200));
        state.MiddleColor.Should().Be(new RgbColor(0, 0, 200));
        state.RightColor.Should().Be(new RgbColor(0, 0, 200));
    }

    [Fact]
    public void SetColor_All_EmitsThreeZonePacketsAndPersistsNvram()
    {
        var fake = FakeDchuNative.CreateInitialized();
        var service = new KeyboardRgbService(fake);
        fake.Commands.Clear();
        var color = new RgbColor(10, 20, 30);

        service.SetColor(color, KeyboardZone.All);

        var packets = fake.Commands.Where(c => c.Command == NativeConstants.CMD_KB_LED).ToList();
        packets.Should().HaveCount(3);
        packets.Select(p => p.Payload[3]).Should().Equal((byte)KeyboardZone.Left, (byte)KeyboardZone.Mid, (byte)KeyboardZone.Right);

        byte scaledBlue = color.ScaledBlue; // round(200/255*30) = 24
        scaledBlue.Should().Be(24);
        foreach (var packet in packets)
            packet.Payload.Should().Equal(color.Green, color.Red, scaledBlue, packet.Payload[3]);

        fake.Settings[NativeConstants.AppSettings.KbAllColor].Should().Be(10);
        fake.Settings[NativeConstants.AppSettings.KbAllColor + 1].Should().Be(20);
        fake.Settings[NativeConstants.AppSettings.KbAllColor + 2].Should().Be(30);

        service.GetState().LeftColor.Should().Be(color);
        service.GetState().MiddleColor.Should().Be(color);
        service.GetState().RightColor.Should().Be(color);
    }

    [Fact]
    public void SetColor_Left_OnlyTouchesLeftZone()
    {
        var fake = FakeDchuNative.CreateInitialized();
        var service = new KeyboardRgbService(fake);
        fake.Commands.Clear();
        var color = new RgbColor(200, 0, 0);

        service.SetColor(color, KeyboardZone.Left);

        fake.Commands.Should().ContainSingle();
        fake.Commands[0].Payload.Should().Equal((byte)0, (byte)200, (byte)0, (byte)KeyboardZone.Left);
        fake.Settings[NativeConstants.AppSettings.KbLeftColor].Should().Be(200);

        var state = service.GetState();
        state.LeftColor.Should().Be(color);
        state.MiddleColor.Should().Be(new RgbColor(0, 0, 200));
    }

    [Theory]
    [InlineData(RgbEffectMode.Breath, new byte[] { 0x00, 0xA0, 0x02, 0x10 })]
    [InlineData(RgbEffectMode.Cycle, new byte[] { 0x00, 0x00, 0x01, 0x33 })]
    [InlineData(RgbEffectMode.Wave, new byte[] { 0x00, 0x00, 0x00, 0xB0 })]
    [InlineData(RgbEffectMode.Flash, new byte[] { 0x00, 0x00, 0x00, 0xA0 })]
    public void SetEffect_EmitsExpectedControlWordAndPersists(RgbEffectMode mode, byte[] expected)
    {
        var fake = FakeDchuNative.CreateInitialized();
        var service = new KeyboardRgbService(fake);
        fake.Commands.Clear();

        service.SetEffect(mode, 7);

        fake.Commands.Last().Command.Should().Be(NativeConstants.CMD_KB_LED);
        fake.Commands.Last().Payload.Should().Equal(expected);
        fake.Settings[NativeConstants.AppSettings.KbEffect].Should().Be((byte)mode);
        fake.Settings[NativeConstants.AppSettings.KbSpeed].Should().Be(7);
        service.GetState().Mode.Should().Be(mode);
        service.GetState().Speed.Should().Be(7);
    }

    [Fact]
    public void SetEffect_Breath_RefreshesZoneColorsBeforeControlWord()
    {
        var fake = FakeDchuNative.CreateInitialized();
        var service = new KeyboardRgbService(fake);
        fake.Commands.Clear();

        service.SetEffect(RgbEffectMode.Breath);

        fake.Commands.Should().HaveCount(5);
        fake.Commands[0].Payload.Should().Equal(new byte[] { 0xBF, 0x00, 0x00, 0xF4 });
        fake.Commands.Skip(1).Take(3).Should().OnlyContain(c =>
            c.Payload[3] == (byte)KeyboardZone.Left ||
            c.Payload[3] == (byte)KeyboardZone.Mid ||
            c.Payload[3] == (byte)KeyboardZone.Right);
        fake.Commands[4].Payload.Should().Equal(new byte[] { 0x00, 0xA0, 0x02, 0x10 });
        fake.Settings[NativeConstants.AppSettings.KbLeftStatus].Should().Be(1);
        fake.Settings[NativeConstants.AppSettings.KbMidStatus].Should().Be(1);
        fake.Settings[NativeConstants.AppSettings.KbRightStatus].Should().Be(1);
    }

    [Theory]
    [InlineData(0, new byte[] { 0x00, 0x00, 0x00, 0xF4 })]
    [InlineData(1, new byte[] { 0x2F, 0x00, 0x00, 0xF4 })]
    [InlineData(2, new byte[] { 0x5F, 0x00, 0x00, 0xF4 })]
    [InlineData(3, new byte[] { 0x8F, 0x00, 0x00, 0xF4 })]
    [InlineData(4, new byte[] { 0xBF, 0x00, 0x00, 0xF4 })]
    public void SetBrightness_EmitsLadderPacket(byte level, byte[] expected)
    {
        var fake = FakeDchuNative.CreateInitialized();
        var service = new KeyboardRgbService(fake);
        fake.Commands.Clear();

        service.SetBrightness(level);

        fake.Commands.Last().Payload.Should().Equal(expected);
        fake.Settings[NativeConstants.AppSettings.KbBrightness].Should().Be(level);
    }

    [Fact]
    public void SetBrightness_AboveFour_ClampsToMax()
    {
        var fake = FakeDchuNative.CreateInitialized();
        var service = new KeyboardRgbService(fake);

        service.SetBrightness(9);

        service.GetState().Brightness.Should().Be(4);
    }

    [Fact]
    public void SetPower_Off_SendsBrightnessZeroThenMasterGateOff()
    {
        var fake = FakeDchuNative.CreateInitialized();
        var service = new KeyboardRgbService(fake);
        fake.Commands.Clear();

        service.SetPower(false);

        fake.Commands.Should().HaveCount(2);
        fake.Commands[0].Payload.Should().Equal(new byte[] { 0x00, 0x00, 0x00, 0xF4 });
        fake.Commands[1].Payload.Should().Equal(new byte[] { 0x07, 0x00, 0x00, 0xE0 });
        fake.Settings[NativeConstants.AppSettings.KbPowerStatus].Should().Be(0);
        service.GetState().IsPowered.Should().BeFalse();
        service.GetState().IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void SetPower_On_RestoresBrightnessAndPowerStatus()
    {
        var fake = FakeDchuNative.CreateInitialized();
        var service = new KeyboardRgbService(fake);
        service.SetPower(false);
        fake.Commands.Clear();

        service.SetPower(true);

        service.GetState().IsPowered.Should().BeTrue();
        service.GetState().Brightness.Should().Be(4);
        fake.Settings[NativeConstants.AppSettings.KbPowerStatus].Should().Be(1);
        fake.Commands.Should().Contain(c => c.Payload.SequenceEqual(new byte[] { 0xBF, 0x00, 0x00, 0xF4 }));
    }

    [Theory]
    [InlineData(30, new byte[] { 0xFF, 30, 0, 0x18 })]
    [InlineData(60, new byte[] { 0xFF, 60, 0, 0x18 })]
    [InlineData(300, new byte[] { 0xFF, 0x2C, 0x01, 0x18 })]
    [InlineData(2, new byte[] { 0xFF, 5, 0, 0x18 })]
    [InlineData(0, new byte[] { 0x00, 0x00, 0x00, 0x18 })]
    public void SetSleepTimeout_EmitsPacketAndPersistsTimer(int seconds, byte[] expected)
    {
        var fake = FakeDchuNative.CreateInitialized();
        var service = new KeyboardRgbService(fake);
        fake.Commands.Clear();

        service.SetSleepTimeout(seconds);

        fake.Commands.Last().Command.Should().Be(NativeConstants.CMD_SYS_CONTROL);
        fake.Commands.Last().Payload.Should().Equal(expected);

        int effective = seconds is > 0 and < 5 ? 5 : seconds;
        fake.Settings[NativeConstants.AppSettings.KbSleepTimerTrigger].Should().Be((byte)(effective > 0 ? 1 : 0));
        fake.Settings[NativeConstants.AppSettings.KbSleepTimerHour].Should().Be((byte)(effective / 3600));
        fake.Settings[NativeConstants.AppSettings.KbSleepTimerMinute].Should().Be((byte)((effective % 3600) / 60));
        fake.Settings[NativeConstants.AppSettings.KbSleepTimerSecond].Should().Be((byte)(effective % 60));
        service.GetState().SleepTimeoutSeconds.Should().Be(effective);
    }

    [Fact]
    public void InvalidZone_Throws()
    {
        var fake = FakeDchuNative.CreateInitialized();
        var service = new KeyboardRgbService(fake);

        Action act = () => service.SetColor(RgbColor.White, (KeyboardZone)0x11);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
