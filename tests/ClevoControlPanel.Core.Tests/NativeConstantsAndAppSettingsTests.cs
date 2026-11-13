using ClevoControlPanel.Core.Native;
using FluentAssertions;
using Xunit;

namespace ClevoControlPanel.Core.Tests;

public class NativeConstantsAndAppSettingsTests
{
    [Theory]
    [InlineData(257, 1, 1)]      // PowerMode
    [InlineData(288, 1, 32)]     // DttStatus
    [InlineData(516, 2, 4)]      // KBType
    [InlineData(519, 2, 7)]      // OverrideBootEffect
    [InlineData(520, 2, 8)]      // LedKBAppLaunch
    [InlineData(544, 2, 32)]     // KbEffect
    [InlineData(545, 2, 33)]     // KbStaticStatus
    [InlineData(546, 2, 34)]     // KbSpeed
    [InlineData(547, 2, 35)]     // KbBrightness
    [InlineData(548, 2, 36)]     // KbSleepTimerTrigger
    [InlineData(549, 2, 37)]     // KbSleepTimerHour
    [InlineData(550, 2, 38)]     // KbSleepTimerMinute
    [InlineData(551, 2, 39)]     // KbSleepTimerSecond
    [InlineData(592, 2, 80)]     // KbPowerStatus
    [InlineData(593, 2, 81)]     // KbAllColor (Red)
    [InlineData(597, 2, 85)]     // KbLeftColor (Red)
    [InlineData(601, 2, 89)]     // KbMidColor (Red)
    [InlineData(605, 2, 93)]     // KbRightColor (Red)
    [InlineData(1029, 4, 5)]     // FanMode
    [InlineData(1031, 4, 7)]     // FanOffset
    [InlineData(1032, 4, 8)]     // TurboFan_Status
    [InlineData(1033, 4, 9)]     // NoiselessFan_status
    [InlineData(1094, 4, 70)]    // FanMode_Quiet
    [InlineData(1095, 4, 71)]    // FanMode_PowerSaving
    [InlineData(1096, 4, 72)]    // FanMode_Performance
    [InlineData(1097, 4, 73)]    // FanMode_Entertainment
    [InlineData(1098, 4, 74)]    // FanOffset_Quiet
    [InlineData(1099, 4, 75)]    // FanOffset_PowerSaving
    [InlineData(1100, 4, 76)]    // FanOffset_Performance
    [InlineData(1101, 4, 77)]    // FanOffset_Entertainment
    public void AppSettings_LinearAddressingFormula_CalculatesExpectedPageAndOffset(int key, int expectedPage, int expectedOffset)
    {
        NativeConstants.AppSettings.GetPage(key).Should().Be(expectedPage);
        NativeConstants.AppSettings.GetOffset(key).Should().Be(expectedOffset);

        // Cross-check mathematical inverse
        (expectedPage * 256 + expectedOffset).Should().Be(key);
    }

    [Fact]
    public void NativeConstants_WmiCommandIds_MatchAuthoritativeSpecifications()
    {
        NativeConstants.WmiCommands.CMD_FAN_TELEMETRY.Should().Be(12);
        NativeConstants.WmiCommands.CMD_HW_CAPABILITIES.Should().Be(13);
        NativeConstants.WmiCommands.CMD_FAN_DUTY_TARGET.Should().Be(22);
        NativeConstants.WmiCommands.CMD_SET_WHITE_LED_KB.Should().Be(39);
        NativeConstants.WmiCommands.CMD_GET_WHITE_LED_KB.Should().Be(61);
        NativeConstants.WmiCommands.CMD_GET_SUPPORT_FEATURE.Should().Be(70);
        NativeConstants.WmiCommands.CMD_ENERGY_STAR.Should().Be(79);
        NativeConstants.WmiCommands.CMD_FAN_CONTROL_ALT.Should().Be(98);
        NativeConstants.WmiCommands.CMD_KB_LED.Should().Be(103);
        NativeConstants.WmiCommands.CMD_SYS_CONTROL.Should().Be(121);
        NativeConstants.WmiCommands.CMD_BIOS_SPECIAL_FEATURE.Should().Be(122);
    }

    [Fact]
    public void NativeConstants_SubCommands_MatchAuthoritativeSpecifications()
    {
        NativeConstants.WmiSubCommands.SUB_FAN_MODE.Should().Be(1);
        NativeConstants.WmiSubCommands.SUB_FAN_OFFSET.Should().Be(14);
        NativeConstants.WmiSubCommands.SUB_SLEEP_TIMER.Should().Be(24);
        NativeConstants.WmiSubCommands.SUB_POWER_MODE.Should().Be(25);
        NativeConstants.WmiSubCommands.SUB_FAN_SCHEDULE.Should().Be(40);
        NativeConstants.WmiSubCommands.SUB_ANTI_DUST.Should().Be(41);

        NativeConstants.WmiSubCommands.SUB_FAN_MODE_ALT.Should().Be(1);
        NativeConstants.WmiSubCommands.SUB_FAN_OFFSET_ALT.Should().Be(2);
        NativeConstants.WmiSubCommands.SUB_TURBO_FAN_ALT.Should().Be(3);
    }

    [Fact]
    public void NativeConstants_BufferAndMathConstants_MatchHardwareSpecifications()
    {
        NativeConstants.BUFFER_SIZE_256.Should().Be(256);
        NativeConstants.MINIMUM_TELEMETRY_BUFFER_SIZE.Should().Be(24);
        NativeConstants.GPU_STANDBY_TEMP_THRESHOLD.Should().Be(20);
        NativeConstants.RPM_FORMULA_CONSTANT.Should().Be(2156250.0);
    }
}
