using System;
using ClevoControlPanel.Core.Models;
using ClevoControlPanel.Core.Native;
using FluentAssertions;
using Xunit;

namespace ClevoControlPanel.Core.Tests;

public class RgbLightingTests
{
    [Fact]
    public void RgbColor_ScaledBlue_AttenuatesDominantBlueLED()
    {
        // 200 / 255 * 255 = 200
        new RgbColor(0, 0, 255).ScaledBlue.Should().Be(200);

        // 200 / 255 * 0 = 0
        new RgbColor(255, 255, 0).ScaledBlue.Should().Be(0);

        // 200 / 255 * 128 = 100.39 -> 100
        new RgbColor(10, 20, 128).ScaledBlue.Should().Be(100);
    }

    [Fact]
    public void RgbColor_HexConversion_SerializesAndDeserializesAccurately()
    {
        var color = new RgbColor(0x12, 0x34, 0x56);
        color.ToHex().Should().Be("#123456");

        var parsed = RgbColor.FromHex("#123456");
        parsed.Red.Should().Be(0x12);
        parsed.Green.Should().Be(0x34);
        parsed.Blue.Should().Be(0x56);
        parsed.R.Should().Be(0x12);
        parsed.G.Should().Be(0x34);
        parsed.B.Should().Be(0x56);
    }

    [Fact]
    public void BuildColorPacket_StrictByteOrder_MatchesGreenRedBlueZone()
    {
        // Target: Left zone (0xF0), Red=0x11, Green=0x22, Blue=255 -> ScaledBlue=200 (0xC8)
        var color = new RgbColor(0x11, 0x22, 255);
        byte[] packet = RgbPacketBuilder.BuildColorPacket(KeyboardZone.Left, color, scaleBlue: true);

        packet.Length.Should().Be(4);
        packet[0].Should().Be(0x22); // Green
        packet[1].Should().Be(0x11); // Red
        packet[2].Should().Be(200);  // Blue scaled (0xC8)
        packet[3].Should().Be(0xF0); // Zone prefix: Left
    }

    [Fact]
    public void BuildColorPacket_UnscaledBlue_PreservesOriginalBlue()
    {
        var color = new RgbColor(10, 20, 255);
        byte[] packet = RgbPacketBuilder.BuildColorPacket(KeyboardZone.Mid, color, scaleBlue: false);

        packet[0].Should().Be(20);   // Green
        packet[1].Should().Be(10);   // Red
        packet[2].Should().Be(255);  // Blue untouched
        packet[3].Should().Be(0xF1); // Zone prefix: Mid
    }

    [Theory]
    [InlineData(KeyboardZone.Left, 0xF0)]
    [InlineData(KeyboardZone.Mid, 0xF1)]
    [InlineData(KeyboardZone.Right, 0xF2)]
    [InlineData(KeyboardZone.LightBar, 0xF3)]
    [InlineData(KeyboardZone.Logo, 0xF6)]
    public void BuildColorPacket_AllZones_EmitCorrectPrefixByte(KeyboardZone zone, byte expectedByte)
    {
        var color = new RgbColor(100, 150, 200);
        byte[] packet = RgbPacketBuilder.BuildColorPacket(zone, color);
        packet[3].Should().Be(expectedByte);
    }

    [Theory]
    [InlineData(0, 0x00, 0xF4)]
    [InlineData(1, 0x2F, 0xF4)] // 47
    [InlineData(2, 0x5F, 0xF4)] // 95
    [InlineData(3, 0x8F, 0xF4)] // 143
    [InlineData(4, 0xBF, 0xF4)] // 191
    [InlineData(99, 0x00, 0xF4)] // Invalid level defaults to 0
    public void BuildBrightnessPacket_Levels0Through4_ProduceExpectedPayload(byte level, byte expectedStepByte, byte expectedPrefixByte)
    {
        byte[] packet = RgbPacketBuilder.BuildBrightnessPacket(level);

        packet.Length.Should().Be(4);
        packet[0].Should().Be(expectedStepByte);
        packet[1].Should().Be(0x00);
        packet[2].Should().Be(0x00);
        packet[3].Should().Be(expectedPrefixByte);
    }

    [Fact]
    public void BuildSleepTimerPacket_ZeroSeconds_EmitsDisablePacket()
    {
        byte[] packet = RgbPacketBuilder.BuildSleepTimerPacket(0);

        packet.Should().Equal(new byte[] { 0x00, 0x00, 0x00, 0x18 });
    }

    [Fact]
    public void BuildSleepTimerPacket_NegativeSeconds_EmitsDisablePacket()
    {
        byte[] packet = RgbPacketBuilder.BuildSleepTimerPacket(-10);

        packet.Should().Equal(new byte[] { 0x00, 0x00, 0x00, 0x18 });
    }

    [Theory]
    [InlineData(1, 5)]  // Clamped to 5 seconds
    [InlineData(4, 5)]  // Clamped to 5 seconds
    [InlineData(5, 5)]  // Exact minimum
    [InlineData(15, 15)]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(300, 300)]
    public void BuildSleepTimerPacket_PositiveSeconds_EmitsClampedEnablePacket(int inputSeconds, int expectedSeconds)
    {
        byte[] packet = RgbPacketBuilder.BuildSleepTimerPacket(inputSeconds);

        packet.Length.Should().Be(4);
        packet[0].Should().Be(0xFF); // Enable flag
        packet[1].Should().Be((byte)(expectedSeconds & 0xFF));
        packet[2].Should().Be((byte)((expectedSeconds >> 8) & 0xFF));
        packet[3].Should().Be(0x18); // SubCommand 24
    }

    [Fact]
    public void BuildMasterPowerPacket_TurnOff_EmitsOffBitmask()
    {
        byte[] packet = RgbPacketBuilder.BuildMasterPowerPacket(false);

        uint value = BitConverter.ToUInt32(packet, 0);
        // Prefix 0xE0000000 | 7u
        value.Should().Be(0xE0000007u);
    }

    [Fact]
    public void BuildMasterPowerPacket_TurnOn_SetsBit12AndZoneBits()
    {
        byte[] packet = RgbPacketBuilder.BuildMasterPowerPacket(true, left: true, mid: true, right: true);

        uint value = BitConverter.ToUInt32(packet, 0);
        // Prefix 0xE0000000, bit 12 (enable), bit 16 (left), bit 17 (mid), bit 18 (right), subflag 7
        uint expected = 0xE0000000u | 7u | (1u << 12) | (1u << 16) | (1u << 17) | (1u << 18);
        value.Should().Be(expected);
    }

    [Theory]
    [InlineData(LightingEffectMode.Random, 0x70000000u)]
    [InlineData(LightingEffectMode.Breath, 0x1002A000u)]
    [InlineData(LightingEffectMode.Cycle, 0x33010000u)]
    [InlineData(LightingEffectMode.Wave, 0xB0000000u)]
    [InlineData(LightingEffectMode.Dance, 0x80000000u)]
    [InlineData(LightingEffectMode.Tempo, 0x90000000u)]
    [InlineData(LightingEffectMode.Flash, 0xA0000000u)]
    public void BuildEffectPacket_AnimatedModes_ReturnExpectedHexWords(LightingEffectMode mode, uint expectedHex)
    {
        byte[]? packet = RgbPacketBuilder.BuildEffectPacket(mode);

        packet.Should().NotBeNull();
        BitConverter.ToUInt32(packet!, 0).Should().Be(expectedHex);
    }

    [Theory]
    [InlineData(LightingEffectMode.Custom)]
    [InlineData(LightingEffectMode.All)]
    public void BuildEffectPacket_DirectModes_ReturnNull(LightingEffectMode mode)
    {
        byte[]? packet = RgbPacketBuilder.BuildEffectPacket(mode);
        packet.Should().BeNull();
    }
}
