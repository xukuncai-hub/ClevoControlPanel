using System;
using Xunit;
using ClevoControlPanel.Tests.Mocks;

namespace ClevoControlPanel.Tests
{
    [Trait("Tier", "1")]
    [Trait("Tier", "2")]
    public class KeyboardRgbTests
    {
        [Fact]
        public void SetKeyboardRgbColor_AllZones_SynchronizesAllThreeZones()
        {
            var mock = new MockHwService();
            var red = new RgbColor(255, 0, 0);

            mock.SetKeyboardRgbColor(red, KeyboardZone.All);

            Assert.Equal(red, mock.GetKeyboardZoneColor(KeyboardZone.Left));
            Assert.Equal(red, mock.GetKeyboardZoneColor(KeyboardZone.Middle));
            Assert.Equal(red, mock.GetKeyboardZoneColor(KeyboardZone.Right));

            // Verify AppSettings
            Assert.Equal(255, mock.VirtualAppSettings[593]); // Global R
            Assert.Equal(0, mock.VirtualAppSettings[594]);   // Global G
            Assert.Equal(0, mock.VirtualAppSettings[595]);   // Global B
            Assert.Equal(255, mock.VirtualAppSettings[597]); // Left R
            Assert.Equal(255, mock.VirtualAppSettings[601]); // Middle R
            Assert.Equal(255, mock.VirtualAppSettings[605]); // Right R
        }

        [Fact]
        public void SetKeyboardRgbColor_IndividualZones_MaintainsStrictIsolation()
        {
            var mock = new MockHwService();
            var red = new RgbColor(255, 0, 0);
            var green = new RgbColor(0, 255, 0);
            var blue = new RgbColor(0, 0, 255);

            mock.SetKeyboardRgbColor(red, KeyboardZone.Left);
            mock.SetKeyboardRgbColor(green, KeyboardZone.Middle);
            mock.SetKeyboardRgbColor(blue, KeyboardZone.Right);

            Assert.Equal(red, mock.GetKeyboardZoneColor(KeyboardZone.Left));
            Assert.Equal(green, mock.GetKeyboardZoneColor(KeyboardZone.Middle));
            Assert.Equal(blue, mock.GetKeyboardZoneColor(KeyboardZone.Right));

            // Mutate Left only
            var white = new RgbColor(255, 255, 255);
            mock.SetKeyboardRgbColor(white, KeyboardZone.Left);

            Assert.Equal(white, mock.GetKeyboardZoneColor(KeyboardZone.Left));
            Assert.Equal(green, mock.GetKeyboardZoneColor(KeyboardZone.Middle)); // Unchanged
            Assert.Equal(blue, mock.GetKeyboardZoneColor(KeyboardZone.Right));   // Unchanged
        }

        [Fact]
        public void SetKeyboardRgbColor_CyanWorkaround_AppliesPatchToBlueByte()
        {
            var mock = new MockHwService();
            var cyan = new RgbColor(0, 255, 127);

            mock.SetKeyboardRgbColor(cyan, KeyboardZone.Left);

            // Packet layout: G, R, B, Header
            Assert.Equal(255, mock.LastDispatchedWmiBuffer[0]); // Green
            Assert.Equal(0, mock.LastDispatchedWmiBuffer[1]);   // Red
            Assert.Equal(0x46, mock.LastDispatchedWmiBuffer[2]); // Patched Blue (0x46 instead of 127)
            Assert.Equal(0xF0, mock.LastDispatchedWmiBuffer[3]); // Left header
        }

        [Fact]
        public void SetKeyboardRgbColor_InvalidZone_ThrowsArgumentOutOfRangeException()
        {
            var mock = new MockHwService();
            Assert.Throws<ArgumentOutOfRangeException>(() => mock.SetKeyboardRgbColor(RgbColor.Red, (KeyboardZone)99));
        }

        [Theory]
        [InlineData(RgbEffectMode.Static, 1)]
        [InlineData(RgbEffectMode.Breath, 2)]
        [InlineData(RgbEffectMode.Cycle, 3)]
        [InlineData(RgbEffectMode.Wave, 4)]
        [InlineData(RgbEffectMode.Flash, 7)]
        public void SetKeyboardRgbEffect_ValidModes_AppliesAndPersists(RgbEffectMode mode, byte expectedByte)
        {
            var mock = new MockHwService();

            mock.SetKeyboardRgbEffect(mode, speed: 5);

            var state = mock.GetKeyboardState();
            Assert.Equal(mode, state.Mode);
            Assert.Equal(5, state.Speed);
            Assert.Equal(expectedByte, mock.VirtualAppSettings[544]);
        }

        [Fact]
        public void SetKeyboardRgbEffect_InvalidMode_ThrowsArgumentOutOfRangeException()
        {
            var mock = new MockHwService();
            Assert.Throws<ArgumentOutOfRangeException>(() => mock.SetKeyboardRgbEffect((RgbEffectMode)99));
        }

        [Theory]
        [InlineData(0, 1)]   // Min clamp
        [InlineData(5, 5)]   // Nominal
        [InlineData(10, 10)] // Max nominal
        [InlineData(25, 10)] // Max clamp
        public void SetKeyboardRgbEffect_SpeedClamping_RestrictsToValidRange(byte speed, byte expectedClamped)
        {
            var mock = new MockHwService();

            mock.SetKeyboardRgbEffect(RgbEffectMode.Cycle, speed);

            Assert.Equal(expectedClamped, mock.GetKeyboardState().Speed);
            Assert.Equal(expectedClamped, mock.VirtualAppSettings[546]);
        }

        [Theory]
        [InlineData(0, 0, false, 0u)]
        [InlineData(1, 1, true, 47u)]
        [InlineData(2, 2, true, 95u)]
        [InlineData(3, 3, true, 143u)]
        [InlineData(4, 4, true, 191u)]
        public void SetKeyboardBrightness_ValidLevels_AppliesCorrectly(byte level, byte expectedLevel, bool expectedEnabled, uint expectedStep)
        {
            var mock = new MockHwService();

            mock.SetKeyboardBrightness(level);

            Assert.Equal(expectedLevel, mock.GetKeyboardBrightness());
            Assert.Equal(expectedLevel, mock.VirtualAppSettings[547]);
            Assert.Equal(expectedEnabled ? (byte)1 : (byte)0, mock.VirtualAppSettings[592]);

            var state = mock.GetKeyboardState();
            Assert.Equal(expectedEnabled, state.IsEnabled);
            Assert.Equal(expectedStep, mock.LastDispatchedWmiData & 0xFF);
        }

        [Theory]
        [InlineData(5, 4)]
        [InlineData(255, 4)]
        public void SetKeyboardBrightness_OverrangeValues_ClampsToMaxLevel4(byte overrange, byte expectedClamped)
        {
            var mock = new MockHwService();

            mock.SetKeyboardBrightness(overrange);

            Assert.Equal(expectedClamped, mock.GetKeyboardBrightness());
            Assert.Equal(expectedClamped, mock.VirtualAppSettings[547]);
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(15, 1, 15)]
        [InlineData(30, 1, 30)]
        [InlineData(60, 1, 60)]
        [InlineData(300, 1, 300)]
        public void SetKeyboardSleepTimeout_Presets_AppliesCorrectly(int seconds, byte expectedActive, int expectedSeconds)
        {
            var mock = new MockHwService();

            mock.SetKeyboardSleepTimeout(seconds);

            Assert.Equal(expectedSeconds, mock.GetKeyboardSleepTimeout());
            Assert.Equal(expectedActive, mock.VirtualAppSettings[548]);
            Assert.Equal(121, mock.LastDispatchedWmiCommand);
            Assert.Equal(0x18, mock.LastDispatchedWmiBuffer[3]); // Subcommand 24
        }

        [Fact]
        public void SetKeyboardSleepTimeout_NegativeValue_ClampsToZero()
        {
            var mock = new MockHwService();

            mock.SetKeyboardSleepTimeout(-50);

            Assert.Equal(0, mock.GetKeyboardSleepTimeout());
            Assert.Equal(0, mock.VirtualAppSettings[548]);
            Assert.Equal(0x00, mock.LastDispatchedWmiBuffer[0]); // Disabled flag
        }

        [Fact]
        public void SetKeyboardSleepTimeout_LargeOverrange_ClampsTo3600()
        {
            var mock = new MockHwService();

            mock.SetKeyboardSleepTimeout(999999);

            Assert.Equal(3600, mock.GetKeyboardSleepTimeout());
        }

        [Fact]
        public void KeyboardSleepWakeCycle_InactivityTurnsOff_UserActivityRestores()
        {
            var mock = new MockHwService();
            mock.SetKeyboardBrightness(3);
            mock.SetKeyboardSleepTimeout(15);

            Assert.Equal(3, mock.GetEffectiveKeyboardBrightness());
            Assert.False(mock.IsKeyboardBacklightSleeping());

            // Advance time past 15 seconds
            mock.AdvanceTime(TimeSpan.FromSeconds(16));

            Assert.True(mock.IsKeyboardBacklightSleeping());
            Assert.Equal(0, mock.GetEffectiveKeyboardBrightness());

            // Trigger user activity
            mock.TriggerUserActivity();

            Assert.False(mock.IsKeyboardBacklightSleeping());
            Assert.Equal(3, mock.GetEffectiveKeyboardBrightness());
        }
    }
}
