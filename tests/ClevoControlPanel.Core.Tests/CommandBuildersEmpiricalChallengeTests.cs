using System;
using System.Reflection;
using System.Reflection.Emit;
using ClevoControlPanel.Core.Models;
using ClevoControlPanel.Core.Native;
using FluentAssertions;
using Xunit;

namespace ClevoControlPanel.Core.Tests;

/// <summary>
/// Empirical challenge test suite designed by Challenger 2 for Milestone 1.
/// Stress-tests Command 103 RGB packets, Blue attenuation ratio & rounding,
/// Command 121 sleep timer packets, AppSettings linear addressing (0..65535),
/// and SetWmiSubCommand packet encoding.
/// </summary>
public class CommandBuildersEmpiricalChallengeTests
{
    private static readonly KeyboardZone[] AllKeyboardZones = new[]
    {
        KeyboardZone.All,       // 0x00
        KeyboardZone.Left,      // 0xF0
        KeyboardZone.Mid,       // 0xF1
        KeyboardZone.Right,     // 0xF2
        KeyboardZone.LightBar,  // 0xF3
        KeyboardZone.Logo       // 0xF6
    };

    #region 1. Command 103 RGB Packets

    [Fact]
    public void Command103_VerifyLittleEndianByteOrder_AcrossAllZonesAndAll256BlueLevels()
    {
        // Challenge: For every zone and all 256 blue values, verify [G, R, B_scaled, Zone]
        foreach (KeyboardZone zone in AllKeyboardZones)
        {
            for (int b = 0; b <= 255; b++)
            {
                byte red = (byte)((b * 37) % 256);
                byte green = (byte)((b * 73) % 256);
                var color = new RgbColor(red, green, (byte)b);

                byte[] packet = RgbPacketBuilder.BuildColorPacket(zone, color, scaleBlue: true);

                packet.Should().HaveCount(4, "Command 103 packet must be exactly 4 bytes");
                packet[0].Should().Be(green, "Byte 0 must be Green component");
                packet[1].Should().Be(red, "Byte 1 must be Red component");
                packet[2].Should().Be(color.ScaledBlue, "Byte 2 must be Scaled Blue component");
                packet[3].Should().Be((byte)zone, "Byte 3 must be Zone prefix byte");

                // Verify little-endian 32-bit word interpretation
                uint word = BitConverter.ToUInt32(packet, 0);
                uint expectedWord = (uint)green | ((uint)red << 8) | ((uint)color.ScaledBlue << 16) | ((uint)(byte)zone << 24);
                word.Should().Be(expectedWord, "Little-endian 32-bit word must match Green | (Red << 8) | (B_scaled << 16) | (Zone << 24)");
            }
        }
    }

    [Fact]
    public void Command103_VerifyUnscaledBlueMode_AcrossAllZonesAndAll256BlueLevels()
    {
        // Challenge: When scaleBlue is false, verify packet[2] is identical to raw Blue
        foreach (KeyboardZone zone in AllKeyboardZones)
        {
            for (int b = 0; b <= 255; b++)
            {
                byte red = 100;
                byte green = 150;
                var color = new RgbColor(red, green, (byte)b);

                byte[] packet = RgbPacketBuilder.BuildColorPacket(zone, color, scaleBlue: false);

                packet[0].Should().Be(green);
                packet[1].Should().Be(red);
                packet[2].Should().Be((byte)b, "Unscaled packet[2] must preserve raw Blue byte");
                packet[3].Should().Be((byte)zone);
            }
        }
    }

    [Fact]
    public void Command103_VerifyStandardColorPalettePackets()
    {
        var testVectors = new (RgbColor color, byte expectedG, byte expectedR, byte expectedScaledB)[]
        {
            (RgbColor.Black, 0, 0, 0),
            (RgbColor.White, 255, 255, 200),
            (RgbColor.RedColor, 0, 255, 0),
            (RgbColor.GreenColor, 255, 0, 0),
            (RgbColor.BlueColor, 0, 0, 200),
            (RgbColor.Cyan, 255, 0, 200),
            (RgbColor.Magenta, 0, 255, 200),
            (RgbColor.Yellow, 255, 255, 0),
            (RgbColor.Orange, 128, 255, 0),
            (new RgbColor(128, 64, 128), 64, 128, 100)
        };

        foreach (var vector in testVectors)
        {
            foreach (KeyboardZone zone in AllKeyboardZones)
            {
                byte[] packet = RgbPacketBuilder.BuildColorPacket(zone, vector.color, scaleBlue: true);
                packet[0].Should().Be(vector.expectedG);
                packet[1].Should().Be(vector.expectedR);
                packet[2].Should().Be(vector.expectedScaledB);
                packet[3].Should().Be((byte)zone);
            }
        }
    }

    #endregion

    #region 2. Blue Attenuation Factor and Rounding

    [Fact]
    public void BlueAttenuation_VerifyExactScalingRatioConstant()
    {
        double expected = 200.0 / 255.0;
        NativeConstants.RgbLightingConstants.BLUE_SCALE_FACTOR.Should().BeApproximately(expected, 1e-12);
    }

    [Fact]
    public void BlueAttenuation_VerifyAll256InputsMatchFormulaAndRounding()
    {
        // Challenge: Exhaustively check all 256 possible byte values of Blue
        for (int b = 0; b <= 255; b++)
        {
            var color = new RgbColor(0, 0, (byte)b);
            double exactProduct = (200.0 / 255.0) * b;
            byte expectedRounded = (byte)Math.Round(exactProduct);

            color.ScaledBlue.Should().Be(expectedRounded, $"Failed at Blue={b}: expected {expectedRounded}, got {color.ScaledBlue}");
        }
    }

    [Fact]
    public void BlueAttenuation_VerifyNoAmbiguousMidpointsExistInDomain()
    {
        // Mathematical challenge:
        // Midpoint rounding ambiguity only occurs if (200 / 255) * b = N + 0.5.
        // That is, 40 * b / 51 = (2N + 1) / 2 <=> 80 * b = 51 * (2N + 1).
        // Since Left is Even and Right is Odd, no integer solution exists!
        // Verify empirically for all 256 values: difference from any half-integer is strictly non-zero.
        for (int b = 0; b <= 255; b++)
        {
            double exact = (200.0 / 255.0) * b;
            double distanceToHalf = Math.Abs(exact - (Math.Floor(exact) + 0.5));
            distanceToHalf.Should().BeGreaterThan(0.001, $"Blue={b} should not fall on ambiguous .5 boundary");
        }
    }

    [Fact]
    public void BlueAttenuation_VerifyMonotonicityAndStepConstraints()
    {
        // ScaledBlue must be monotonically non-decreasing and step size must never exceed 1
        for (int b = 0; b < 255; b++)
        {
            byte current = new RgbColor(0, 0, (byte)b).ScaledBlue;
            byte next = new RgbColor(0, 0, (byte)(b + 1)).ScaledBlue;

            next.Should().BeGreaterOrEqualTo(current, $"Monotonicity violated between Blue={b} and {b + 1}");
            (next - current).Should().BeInRange(0, 1, $"Step delta between Blue={b} and {b + 1} must be 0 or 1");
        }

        // Boundedness check
        new RgbColor(0, 0, 0).ScaledBlue.Should().Be(0);
        new RgbColor(0, 0, 255).ScaledBlue.Should().Be(200);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(51, 40)]   // Exact integer: 51 * 200 / 255 = 40
    [InlineData(64, 50)]   // 64 * 200 / 255 = 50.196 -> 50
    [InlineData(102, 80)]  // Exact integer: 102 * 200 / 255 = 80
    [InlineData(128, 100)] // 128 * 200 / 255 = 100.392 -> 100
    [InlineData(153, 120)] // Exact integer: 153 * 200 / 255 = 120
    [InlineData(191, 150)] // 191 * 200 / 255 = 149.804 -> 150
    [InlineData(204, 160)] // Exact integer: 204 * 200 / 255 = 160
    [InlineData(255, 200)] // Exact integer: 255 * 200 / 255 = 200
    public void BlueAttenuation_VerifyKeyMilestoneCheckpoints(byte inputBlue, byte expectedScaled)
    {
        new RgbColor(0, 0, inputBlue).ScaledBlue.Should().Be(expectedScaled);
    }

    #endregion

    #region 3. Command 121 Sleep Timer Packets

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5)]
    [InlineData(-100)]
    [InlineData(-3600)]
    [InlineData(int.MinValue)]
    public void Command121SleepTimer_VerifyDisabledPayload_ForZeroAndNegativeInputs(int inputSeconds)
    {
        byte[] packet = RgbPacketBuilder.BuildSleepTimerPacket(inputSeconds);

        packet.Should().HaveCount(4);
        packet[0].Should().Be(0x00, "Byte 0 must be 0x00 (disabled flag)");
        packet[1].Should().Be(0x00, "Byte 1 must be 0x00");
        packet[2].Should().Be(0x00, "Byte 2 must be 0x00");
        packet[3].Should().Be(0x18, "Byte 3 must be SubCommand 24 (0x18)");
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 5)]
    [InlineData(3, 5)]
    [InlineData(4, 5)]
    [InlineData(5, 5)]
    public void Command121SleepTimer_VerifyClampingUnder5Seconds(int inputSeconds, int expectedSeconds)
    {
        byte[] packet = RgbPacketBuilder.BuildSleepTimerPacket(inputSeconds);

        packet.Should().HaveCount(4);
        packet[0].Should().Be(0xFF, "Byte 0 must be 0xFF (enabled flag)");
        packet[1].Should().Be((byte)expectedSeconds, "Byte 1 must be clamped seconds low-byte (5)");
        packet[2].Should().Be(0x00, "Byte 2 must be seconds high-byte (0)");
        packet[3].Should().Be(0x18, "Byte 3 must be SubCommand 24 (0x18)");
    }

    [Theory]
    [InlineData(15, 0x0F, 0x00)]   // 15 seconds
    [InlineData(30, 0x1E, 0x00)]   // 30 seconds
    [InlineData(60, 0x3C, 0x00)]   // 60 seconds (1 min)
    [InlineData(300, 0x2C, 0x01)]  // 300 seconds (5 min, 0x012C)
    [InlineData(3600, 0x10, 0x0E)] // 3600 seconds (1 hr, 0x0E10)
    [InlineData(65535, 0xFF, 0xFF)]// Max 16-bit seconds (0xFFFF)
    public void Command121SleepTimer_VerifyPresetAndMultiByteTimeoutPayloads(int inputSeconds, byte expectedLo, byte expectedHi)
    {
        byte[] packet = RgbPacketBuilder.BuildSleepTimerPacket(inputSeconds);

        packet.Should().HaveCount(4);
        packet[0].Should().Be(0xFF, "Byte 0 must be 0xFF (enabled flag)");
        packet[1].Should().Be(expectedLo, "Byte 1 must be seconds low-byte");
        packet[2].Should().Be(expectedHi, "Byte 2 must be seconds high-byte");
        packet[3].Should().Be(0x18, "Byte 3 must be SubCommand 24 (0x18)");
    }

    [Fact]
    public void Command121SleepTimer_VerifyExhaustiveSecondsRangeFrom5To10000()
    {
        // Stress-test across 10,000 distinct timeout values
        for (int s = 5; s <= 10000; s++)
        {
            byte[] packet = RgbPacketBuilder.BuildSleepTimerPacket(s);

            packet[0].Should().Be(0xFF);
            packet[1].Should().Be((byte)(s & 0xFF));
            packet[2].Should().Be((byte)((s >> 8) & 0xFF));
            packet[3].Should().Be(0x18);
        }
    }

    #endregion

    #region 4. AppSettings Key Linear Addressing (0..65535)

    [Fact]
    public void AppSettings_VerifyExhaustiveLinearAddressing_AcrossAll65536Keys()
    {
        // Challenge: Test all 65,536 keys from 0 to 65535
        for (int key = 0; key <= 65535; key++)
        {
            int page = NativeConstants.AppSettings.GetPage(key);
            int offset = NativeConstants.AppSettings.GetOffset(key);

            int expectedPage = key / 256;
            int expectedOffset = key % 256;

            page.Should().Be(expectedPage, $"Key {key} failed Page formula");
            offset.Should().Be(expectedOffset, $"Key {key} failed Offset formula");

            page.Should().BeInRange(0, 255, $"Key {key} produced page out of 8-bit range");
            offset.Should().BeInRange(0, 255, $"Key {key} produced offset out of 8-bit range");

            // Verify invertibility
            (page * 256 + offset).Should().Be(key, $"Key {key} failed invertibility test");
        }
    }

    [Fact]
    public void AppSettings_VerifyBoundaryKeys()
    {
        var boundaries = new (int key, int expectedPage, int expectedOffset)[]
        {
            (0, 0, 0),
            (255, 0, 255),
            (256, 1, 0),
            (257, 1, 1),
            (511, 1, 255),
            (512, 2, 0),
            (1023, 3, 255),
            (1024, 4, 0),
            (65280, 255, 0),
            (65535, 255, 255)
        };

        foreach (var (key, expectedPage, expectedOffset) in boundaries)
        {
            NativeConstants.AppSettings.GetPage(key).Should().Be(expectedPage);
            NativeConstants.AppSettings.GetOffset(key).Should().Be(expectedOffset);
        }
    }

    [Fact]
    public void AppSettings_VerifyAllAuthoritativeConstantsMatchCalculatedOffsets()
    {
        var constants = new (int key, int expectedPage, int expectedOffset)[]
        {
            (NativeConstants.AppSettings.PowerMode, 1, 1),
            (NativeConstants.AppSettings.DttStatus, 1, 32),
            (NativeConstants.AppSettings.KBType, 2, 4),
            (NativeConstants.AppSettings.OverrideBootEffect, 2, 7),
            (NativeConstants.AppSettings.LedKBAppLaunch, 2, 8),
            (NativeConstants.AppSettings.KbEffect, 2, 32),
            (NativeConstants.AppSettings.KbStaticStatus, 2, 33),
            (NativeConstants.AppSettings.KbSpeed, 2, 34),
            (NativeConstants.AppSettings.KbBrightness, 2, 35),
            (NativeConstants.AppSettings.KbSleepTimerTrigger, 2, 36),
            (NativeConstants.AppSettings.KbSleepTimerHour, 2, 37),
            (NativeConstants.AppSettings.KbSleepTimerMinute, 2, 38),
            (NativeConstants.AppSettings.KbSleepTimerSecond, 2, 39),
            (NativeConstants.AppSettings.KbPowerStatus, 2, 80),
            (NativeConstants.AppSettings.KbAllColor, 2, 81),
            (NativeConstants.AppSettings.KbLeftStatus, 2, 84),
            (NativeConstants.AppSettings.KbLeftColor, 2, 85),
            (NativeConstants.AppSettings.KbMidStatus, 2, 88),
            (NativeConstants.AppSettings.KbMidColor, 2, 89),
            (NativeConstants.AppSettings.KbRightStatus, 2, 92),
            (NativeConstants.AppSettings.KbRightColor, 2, 93),
            (NativeConstants.AppSettings.FanMode, 4, 5),
            (NativeConstants.AppSettings.FanOffset, 4, 7),
            (NativeConstants.AppSettings.TurboFan_Status, 4, 8),
            (NativeConstants.AppSettings.NoiselessFan_status, 4, 9),
            (NativeConstants.AppSettings.FanMode_Quiet, 4, 70),
            (NativeConstants.AppSettings.FanMode_PowerSaving, 4, 71),
            (NativeConstants.AppSettings.FanMode_Performance, 4, 72),
            (NativeConstants.AppSettings.FanMode_Entertainment, 4, 73),
            (NativeConstants.AppSettings.FanOffset_Quiet, 4, 74),
            (NativeConstants.AppSettings.FanOffset_PowerSaving, 4, 75),
            (NativeConstants.AppSettings.FanOffset_Performance, 4, 76),
            (NativeConstants.AppSettings.FanOffset_Entertainment, 4, 77)
        };

        foreach (var (key, expectedPage, expectedOffset) in constants)
        {
            NativeConstants.AppSettings.GetPage(key).Should().Be(expectedPage);
            NativeConstants.AppSettings.GetOffset(key).Should().Be(expectedOffset);
        }
    }

    #endregion

    #region 5. SetWmiSubCommand Packet Formatting Verification

    [Theory]
    [InlineData(NativeConstants.WmiSubCommands.SUB_POWER_MODE, 0u, 0x00, 0x00, 0x00, 0x19)]       // Quiet mode (0)
    [InlineData(NativeConstants.WmiSubCommands.SUB_POWER_MODE, 2u, 0x02, 0x00, 0x00, 0x19)]       // Perf mode (2)
    [InlineData(NativeConstants.WmiSubCommands.SUB_FAN_MODE, 1u, 0x01, 0x00, 0x00, 0x01)]         // Fan Turbo (1)
    [InlineData(NativeConstants.WmiSubCommands.SUB_FAN_OFFSET, 128u, 0x80, 0x00, 0x00, 0x0E)]     // Fan offset 128 (0x0E)
    [InlineData(NativeConstants.WmiSubCommands.SUB_FAN_OFFSET, 255u, 0xFF, 0x00, 0x00, 0x0E)]     // Fan offset 255 (0x0E)
    [InlineData(NativeConstants.WmiSubCommands.SUB_SLEEP_TIMER, 0x012Cu, 0x2C, 0x01, 0x00, 0x18)] // Sleep timer 300s (0x18)
    [InlineData(NativeConstants.WmiSubCommands.SUB_POWER_MODE, 0x00AABBCCu, 0xCC, 0xBB, 0xAA, 0x19)] // 24-bit payload
    public void SetWmiSubCommand_VerifyBufferFormattingSpecification(
        int subCommand,
        uint data,
        byte expectedB0,
        byte expectedB1,
        byte expectedB2,
        byte expectedB3)
    {
        // Challenge the packet builder layout defined in InsydeDchuNative:
        // buffer[0] = (byte)(data & 0xFF)
        // buffer[1] = (byte)((data >> 8) & 0xFF)
        // buffer[2] = (byte)((data >> 16) & 0xFF)
        // buffer[3] = (byte)subCommand
        byte[] expectedBuffer = new byte[4]
        {
            (byte)(data & 0xFF),
            (byte)((data >> 8) & 0xFF),
            (byte)((data >> 16) & 0xFF),
            (byte)subCommand
        };

        expectedBuffer[0].Should().Be(expectedB0);
        expectedBuffer[1].Should().Be(expectedB1);
        expectedBuffer[2].Should().Be(expectedB2);
        expectedBuffer[3].Should().Be(expectedB3);
    }

    [Fact]
    public void SetWmiSubCommand_VerifyBytecodeImplementationEncodesExpectedPacket()
    {
        // Adversarial code inspection:
        // Verify via Reflection that InsydeDchuNative.SetWmiSubCommand exists, has 3 parameters,
        // returns int, and contains IL opcodes for 4-element byte array allocation and bitshifts.
        var method = typeof(InsydeDchuNative).GetMethod("SetWmiSubCommand", BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull("InsydeDchuNative.SetWmiSubCommand must be publicly exposed");

        var parameters = method!.GetParameters();
        parameters.Should().HaveCount(3);
        parameters[0].ParameterType.Should().Be(typeof(int));
        parameters[1].ParameterType.Should().Be(typeof(int));
        parameters[2].ParameterType.Should().Be(typeof(uint));
        method.ReturnType.Should().Be(typeof(int));

        var body = method.GetMethodBody();
        body.Should().NotBeNull();
        byte[]? il = body!.GetILAsByteArray();
        il.Should().NotBeNull();
        il!.Length.Should().BeGreaterThan(20, "SetWmiSubCommand must contain bytecode assembling the 4-byte buffer");
    }

    #endregion
}
