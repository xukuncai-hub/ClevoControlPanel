using System;
using Xunit;
using ClevoControlPanel.Tests.Mocks;

namespace ClevoControlPanel.Tests
{
    [Trait("Tier", "1")]
    [Trait("Tier", "2")]
    public class FanControlTests
    {
        [Theory]
        [InlineData(FanMode.Auto, 0)]
        [InlineData(FanMode.Maximum, 1)]
        [InlineData(FanMode.Silent, 2)]
        [InlineData(FanMode.MaxQ, 5)]
        [InlineData(FanMode.Custom, 6)]
        [InlineData(FanMode.Noiseless, 8)]
        public void SetFanMode_ValidModes_AppliesAndPersists(FanMode mode, byte expectedByte)
        {
            var mock = new MockHwService();

            mock.SetFanMode(mode);

            Assert.Equal(mode, mock.GetFanMode());
            Assert.Equal(expectedByte, mock.VirtualAppSettings[1029]); // Page 4 Offset 5
            Assert.Equal(121, mock.LastDispatchedWmiCommand);
            Assert.Equal(expectedByte, mock.LastDispatchedWmiBuffer[0]);
            Assert.Equal(0x01, mock.LastDispatchedWmiBuffer[3]); // Subcommand 1
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(7)]
        [InlineData(9)]
        [InlineData(99)]
        public void SetFanMode_InvalidModes_ThrowsArgumentOutOfRangeException(byte invalidMode)
        {
            var mock = new MockHwService();
            byte initialMode = mock.VirtualAppSettings[1029];

            Assert.Throws<ArgumentOutOfRangeException>(() => mock.SetFanMode((FanMode)invalidMode));
            Assert.Equal(initialMode, mock.VirtualAppSettings[1029]);
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(25, 64, 25)]
        [InlineData(50, 128, 50)]
        [InlineData(75, 191, 75)]
        [InlineData(100, 255, 100)]
        public void SetFanOffset_NominalPercentages_ScalesRegisterByteAccurately(byte percent, byte expectedScaledByte, byte expectedStoredPercent)
        {
            var mock = new MockHwService();

            mock.SetFanOffset(percent);

            Assert.Equal(expectedStoredPercent, mock.GetFanOffset());
            Assert.Equal(expectedStoredPercent, mock.VirtualAppSettings[1031]); // Page 4 Offset 7
            Assert.Equal(121, mock.LastDispatchedWmiCommand);
            Assert.Equal(expectedScaledByte, mock.LastDispatchedWmiBuffer[0]);
            Assert.Equal(0x0E, mock.LastDispatchedWmiBuffer[3]); // Subcommand 14
        }

        [Theory]
        [InlineData(101, 255, 100)]
        [InlineData(150, 255, 100)]
        [InlineData(255, 255, 100)]
        public void SetFanOffset_OverrangeInputs_ClampsTo100Percent(byte overrangePercent, byte expectedScaledByte, byte expectedStoredPercent)
        {
            var mock = new MockHwService();

            mock.SetFanOffset(overrangePercent);

            Assert.Equal(expectedStoredPercent, mock.GetFanOffset());
            Assert.Equal(expectedStoredPercent, mock.VirtualAppSettings[1031]);
            Assert.Equal(expectedScaledByte, mock.LastDispatchedWmiBuffer[0]);
        }

        [Fact]
        public void SetFanOffset_SavesToCurrentPowerModeMemory()
        {
            var mock = new MockHwService();

            mock.SetPowerMode(PowerMode.Quiet);
            mock.SetFanOffset(35);
            Assert.Equal(35, mock.VirtualAppSettings[1098]); // FanOffset_Quiet

            mock.SetPowerMode(PowerMode.Performance);
            mock.SetFanOffset(80);
            Assert.Equal(80, mock.VirtualAppSettings[1100]); // FanOffset_Performance
        }

        [Fact]
        public void SetTurboFan_Toggle_EnablesAndDisablesWithBitmask()
        {
            var mock = new MockHwService();

            // Enable Turbo
            mock.SetTurboFan(true);
            Assert.True(mock.GetTurboFan());
            Assert.Equal(1, mock.VirtualAppSettings[1032]);
            Assert.Equal((byte)(2 | (1 << 6)), mock.LastDispatchedWmiBuffer[0]); // 0x42
            Assert.Equal(0x19, mock.LastDispatchedWmiBuffer[3]);

            // Disable Turbo
            mock.SetTurboFan(false);
            Assert.False(mock.GetTurboFan());
            Assert.Equal(0, mock.VirtualAppSettings[1032]);
            Assert.Equal((byte)(2 | (0 << 6)), mock.LastDispatchedWmiBuffer[0]); // 0x02
        }

        [Fact]
        public void SetTurboFan_CancelsNoiselessMode()
        {
            var mock = new MockHwService();
            mock.SetFanMode(FanMode.Noiseless);
            Assert.Equal(1, mock.VirtualAppSettings[1033]);

            mock.SetTurboFan(true);
            Assert.True(mock.GetTurboFan());
            Assert.Equal(1, mock.VirtualAppSettings[1032]);
            Assert.Equal(0, mock.VirtualAppSettings[1033]); // Noiseless disarmed
            Assert.NotEqual(FanMode.Noiseless, mock.GetFanMode());
            Assert.Equal(FanMode.Auto, mock.GetFanMode());
            Assert.Equal(0, mock.VirtualAppSettings[1029]);
        }

        [Fact]
        public void SetFanMode_Noiseless_CancelsTurboFan()
        {
            var mock = new MockHwService();
            mock.SetTurboFan(true);
            Assert.True(mock.GetTurboFan());

            mock.SetFanMode(FanMode.Noiseless);
            Assert.False(mock.GetTurboFan());
            Assert.Equal(0, mock.VirtualAppSettings[1032]); // Turbo disarmed
            Assert.Equal(1, mock.VirtualAppSettings[1033]); // Noiseless engaged
        }
    }
}
