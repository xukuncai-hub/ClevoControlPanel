using System;
using Xunit;
using ClevoControlPanel.Tests.Mocks;

namespace ClevoControlPanel.Tests
{
    [Trait("Tier", "1")]
    [Trait("Tier", "2")]
    public class PowerModeTests
    {
        [Theory]
        [InlineData(PowerMode.Quiet, 0)]
        [InlineData(PowerMode.PowerSaving, 1)]
        [InlineData(PowerMode.Performance, 2)]
        [InlineData(PowerMode.Entertainment, 3)]
        public void SetPowerMode_ValidModes_AppliesAndPersistsToAppSetting(PowerMode mode, byte expectedByte)
        {
            var mock = new MockHwService();

            mock.SetPowerMode(mode);

            Assert.Equal(mode, mock.GetPowerMode());
            Assert.Equal(expectedByte, mock.VirtualAppSettings[257]); // Page 1 Offset 1
            Assert.Equal(121, mock.LastDispatchedWmiCommand);
            Assert.Equal(expectedByte, mock.LastDispatchedWmiBuffer[0]);
            Assert.Equal(0x19, mock.LastDispatchedWmiBuffer[3]); // Subcommand 25
        }

        [Fact]
        public void PowerMode_CycleAllTransitions_SucceedsWithoutError()
        {
            var mock = new MockHwService();
            var modes = new[]
            {
                PowerMode.Quiet,
                PowerMode.Performance,
                PowerMode.PowerSaving,
                PowerMode.Entertainment,
                PowerMode.Quiet
            };

            foreach (var mode in modes)
            {
                mock.SetPowerMode(mode);
                Assert.Equal(mode, mock.GetPowerMode());
                Assert.Equal((byte)mode, mock.VirtualAppSettings[257]);
            }
        }

        [Fact]
        public void PowerMode_RestoresSavedFanProfileForMode()
        {
            var mock = new MockHwService();

            // Setup Quiet saved profile: Silent (2), Offset 15%
            mock.VirtualAppSettings[1094] = (byte)FanMode.Silent;
            mock.VirtualAppSettings[1098] = 15;

            // Setup Performance saved profile: Maximum (1), Offset 60%
            mock.VirtualAppSettings[1096] = (byte)FanMode.Maximum;
            mock.VirtualAppSettings[1100] = 60;

            mock.SetPowerMode(PowerMode.Quiet);
            Assert.Equal(FanMode.Silent, mock.GetFanMode());
            Assert.Equal(15, mock.GetFanOffset());

            mock.SetPowerMode(PowerMode.Performance);
            Assert.Equal(FanMode.Maximum, mock.GetFanMode());
            Assert.Equal(60, mock.GetFanOffset());
        }

        [Fact]
        public void PowerMode_SwitchToQuiet_DisarmsTurboFan()
        {
            var mock = new MockHwService();
            mock.SetPowerMode(PowerMode.Performance);
            mock.SetTurboFan(true);

            Assert.True(mock.GetTurboFan());
            Assert.Equal(1, mock.VirtualAppSettings[1032]);

            // Switch to Quiet
            mock.SetPowerMode(PowerMode.Quiet);

            Assert.False(mock.GetTurboFan());
            Assert.Equal(0, mock.VirtualAppSettings[1032]);
        }

        [Theory]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(99)]
        [InlineData(255)]
        public void PowerMode_OutOfRangeValues_ThrowsArgumentOutOfRangeException(byte invalidValue)
        {
            var mock = new MockHwService();
            byte initialSetting = mock.VirtualAppSettings[257];

            Assert.Throws<ArgumentOutOfRangeException>(() => mock.SetPowerMode((PowerMode)invalidValue));
            Assert.Equal(initialSetting, mock.VirtualAppSettings[257]);
        }

        [Fact]
        public void PowerMode_CommandLogging_RecordsHistory()
        {
            var mock = new MockHwService();
            mock.SetPowerMode(PowerMode.Performance);

            Assert.Contains(mock.CommandHistory, r =>
                r.CommandType == HardwareCommandType.SetPowerMode &&
                r.Parameters["mode"].Equals(PowerMode.Performance));
        }
    }
}
