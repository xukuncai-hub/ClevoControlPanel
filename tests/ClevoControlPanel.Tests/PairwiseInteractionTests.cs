using System;
using System.Threading.Tasks;
using Xunit;
using ClevoControlPanel.Tests.Mocks;

namespace ClevoControlPanel.Tests
{
    [Trait("Tier", "3")]
    public class PairwiseInteractionTests
    {
        [Fact]
        public void T3_01_PowerMode_SwitchToQuiet_DisarmsActiveTurboFan()
        {
            // Preconditions: Initial Performance mode with TurboFan active
            var hw = new MockHwService();
            hw.SetPowerMode(PowerMode.Performance);
            hw.SetTurboFan(true);

            Assert.Equal(PowerMode.Performance, hw.GetPowerMode());
            Assert.True(hw.GetTurboFan());
            Assert.Equal(1, hw.VirtualAppSettings[1032]);

            // Act: Switch to Quiet
            hw.SetPowerMode(PowerMode.Quiet);

            // Assert: Quiet mode prioritizes acoustic comfort, disarming TurboFan
            Assert.Equal(PowerMode.Quiet, hw.GetPowerMode());
            Assert.Equal(0, hw.VirtualAppSettings[257]);
            Assert.False(hw.GetTurboFan());
            Assert.Equal(0, hw.VirtualAppSettings[1032]);

            var telemetry = hw.GetTelemetry();
            Assert.True(telemetry.CpuFanRpm < 6000);
        }

        [Fact]
        public void T3_02_TurboFan_ForcedOnInQuietMode_PreservesQuietModeSetting()
        {
            // Preconditions: Quiet mode engaged
            var hw = new MockHwService();
            hw.SetPowerMode(PowerMode.Quiet);

            // Act: User engages emergency cooling TurboFan
            hw.SetTurboFan(true);

            // Assert: PowerMode remains Quiet, but Turbo is active
            Assert.Equal(PowerMode.Quiet, hw.GetPowerMode());
            Assert.Equal(0, hw.VirtualAppSettings[257]);
            Assert.True(hw.GetTurboFan());
            Assert.Equal(1, hw.VirtualAppSettings[1032]);

            var turboTelemetry = hw.GetTelemetry();
            Assert.Equal(7461, turboTelemetry.CpuFanRpm);

            // Turn off TurboFan -> reverts cleanly
            hw.SetTurboFan(false);
            Assert.False(hw.GetTurboFan());
            Assert.Equal(PowerMode.Quiet, hw.GetPowerMode());
        }

        [Fact]
        public async Task T3_03_Telemetry_PollingConcurrencyDuringRapidPowerModeSwitching()
        {
            var hw = new MockHwService();
            bool running = true;
            int successfulReads = 0;

            // Background task polling telemetry continuously
            var pollTask = Task.Run(async () =>
            {
                while (running)
                {
                    var t = hw.GetTelemetry();
                    Assert.NotNull(t);
                    Assert.InRange(t.CpuTemperature, 10, 115);
                    System.Threading.Interlocked.Increment(ref successfulReads);
                    await Task.Delay(2);
                }
            });

            // Concurrently on main thread, execute 30 rapid alternating PowerMode switches
            for (int i = 0; i < 30; i++)
            {
                var mode = (PowerMode)(i % 4);
                hw.SetPowerMode(mode);
                Assert.Equal(mode, hw.GetPowerMode());
                await Task.Delay(3);
            }

            // End background poll
            running = false;
            await Task.WhenAny(pollTask, Task.Delay(1000));

            Assert.True(successfulReads >= 10);
        }

        [Fact]
        public void T3_04_KeyboardRgb_StateIsolatedAcrossPowerModeTransitions()
        {
            var hw = new MockHwService();

            // Setup custom 3-zone color and wave mode
            hw.SetKeyboardRgbColor(new RgbColor(255, 0, 0), KeyboardZone.Left);
            hw.SetKeyboardRgbColor(new RgbColor(0, 255, 0), KeyboardZone.Middle);
            hw.SetKeyboardRgbColor(new RgbColor(0, 0, 255), KeyboardZone.Right);
            hw.SetKeyboardRgbEffect(RgbEffectMode.Wave, speed: 7);
            hw.SetKeyboardBrightness(3);

            // Switch across PowerModes
            hw.SetPowerMode(PowerMode.Performance);
            hw.SetPowerMode(PowerMode.PowerSaving);
            hw.SetPowerMode(PowerMode.Entertainment);

            // Assert: RGB settings in Page 2 are completely uncorrupted
            var state = hw.GetKeyboardState();
            Assert.Equal(RgbEffectMode.Wave, state.Mode);
            Assert.Equal(7, state.Speed);
            Assert.Equal(3, state.Brightness);
            Assert.Equal(new RgbColor(255, 0, 0), hw.GetKeyboardZoneColor(KeyboardZone.Left));
            Assert.Equal(new RgbColor(0, 255, 0), hw.GetKeyboardZoneColor(KeyboardZone.Middle));
            Assert.Equal(new RgbColor(0, 0, 255), hw.GetKeyboardZoneColor(KeyboardZone.Right));
        }

        [Fact]
        public void T3_05_FanOffset_RetainedAndScaledAcrossFanModeChanges()
        {
            var hw = new MockHwService();

            // Set fan offset to 60%
            hw.SetFanOffset(60);
            Assert.Equal(60, hw.GetFanOffset());
            Assert.Equal(60, hw.VirtualAppSettings[1031]);

            // Switch fan mode to Silent, then Custom, then back to Auto
            hw.SetFanMode(FanMode.Silent);
            hw.SetFanMode(FanMode.Custom);
            hw.SetFanMode(FanMode.Auto);

            // Offset remains preserved
            Assert.Equal(60, hw.GetFanOffset());
            Assert.Equal(60, hw.VirtualAppSettings[1031]);
        }

        [Fact]
        public void T3_06_PerPowerModeFan_MemoryRetainedAcrossModeSwitches()
        {
            var hw = new MockHwService();

            // Configure Quiet: Silent (2), 15%
            hw.SetPowerMode(PowerMode.Quiet);
            hw.SetFanMode(FanMode.Silent);
            hw.SetFanOffset(15);

            // Configure Performance: Maximum (1), 70%
            hw.SetPowerMode(PowerMode.Performance);
            hw.SetFanMode(FanMode.Maximum);
            hw.SetFanOffset(70);

            // Switch to Quiet -> restores 2, 15%
            hw.SetPowerMode(PowerMode.Quiet);
            Assert.Equal(FanMode.Silent, hw.GetFanMode());
            Assert.Equal(15, hw.GetFanOffset());

            // Switch back to Performance -> restores 1, 70%
            hw.SetPowerMode(PowerMode.Performance);
            Assert.Equal(FanMode.Maximum, hw.GetFanMode());
            Assert.Equal(70, hw.GetFanOffset());
        }

        [Fact]
        public void T3_07_Telemetry_GpuD3coldTransition_DuringHighCpuFanOffset()
        {
            var hw = new MockHwService();
            hw.SetFanOffset(100);

            // Simulate dGPU going to D3cold (raw temp 20°C, 0 RPM)
            hw.SetSimulateD3cold(true);

            var telemetry = hw.GetTelemetry();

            Assert.True(telemetry.IsGpuStandby);
            Assert.False(telemetry.IsGpuActive);
            Assert.Equal(0, telemetry.GpuTemperature);
            Assert.Equal(0, telemetry.GpuFanRpm);
            Assert.Equal(0, telemetry.GpuFanDutyPercent);

            // CPU fan is running at high offset
            Assert.True(telemetry.CpuFanRpm > 6500);
            Assert.Equal(100, telemetry.CpuFanDutyPercent);
        }

        [Fact]
        public void T3_08_TurboFan_Toggle_RestoresCustomFanOffset()
        {
            var hw = new MockHwService();
            hw.SetFanOffset(45);

            // Turbo on
            hw.SetTurboFan(true);
            Assert.Equal(7461, hw.GetTelemetry().CpuFanRpm);

            // Turbo off -> 45% offset retained
            hw.SetTurboFan(false);
            Assert.Equal(45, hw.GetFanOffset());
            Assert.Equal(45, hw.VirtualAppSettings[1031]);
        }

        [Fact]
        public async Task T3_09_KeyboardRgb_3ZoneTransmission_DuringTelemetryStream()
        {
            var hw = new MockHwService();
            bool running = true;
            int successfulReads = 0;

            var streamTask = Task.Run(async () =>
            {
                while (running)
                {
                    var t = hw.GetTelemetry();
                    Assert.NotNull(t);
                    System.Threading.Interlocked.Increment(ref successfulReads);
                    await Task.Delay(2);
                }
            });

            // Dispatches rapid 3-zone color updates
            for (int i = 0; i < 20; i++)
            {
                hw.SetKeyboardRgbColor(new RgbColor((byte)(i * 10), 0, 0), KeyboardZone.Left);
                hw.SetKeyboardRgbColor(new RgbColor(0, (byte)(i * 10), 0), KeyboardZone.Middle);
                hw.SetKeyboardRgbColor(new RgbColor(0, 0, (byte)(i * 10)), KeyboardZone.Right);
                await Task.Delay(3);
            }

            running = false;
            await Task.WhenAny(streamTask, Task.Delay(1000));

            Assert.True(successfulReads >= 5);
            Assert.Equal(new RgbColor(190, 0, 0), hw.GetKeyboardZoneColor(KeyboardZone.Left));
            Assert.Equal(new RgbColor(0, 190, 0), hw.GetKeyboardZoneColor(KeyboardZone.Middle));
            Assert.Equal(new RgbColor(0, 0, 190), hw.GetKeyboardZoneColor(KeyboardZone.Right));
        }

        [Fact]
        public void T3_10_SystemPower_ExtremeBatteryConservationTrio()
        {
            var hw = new MockHwService();

            hw.SetPowerMode(PowerMode.PowerSaving);
            hw.SetFanMode(FanMode.Noiseless);
            hw.SetKeyboardBrightness(0);
            hw.SetKeyboardSleepTimeout(15);

            Assert.Equal(PowerMode.PowerSaving, hw.GetPowerMode());
            Assert.Equal(FanMode.Noiseless, hw.GetFanMode());
            Assert.Equal(0, hw.GetKeyboardBrightness());
            Assert.Equal(15, hw.GetKeyboardSleepTimeout());

            // AppSettings verification
            Assert.Equal(1, hw.VirtualAppSettings[257]);  // PowerSaving
            Assert.Equal(8, hw.VirtualAppSettings[1029]); // Noiseless
            Assert.Equal(0, hw.VirtualAppSettings[547]);  // Brightness 0
            Assert.Equal(1, hw.VirtualAppSettings[548]);  // Timer enabled
        }

        [Fact]
        public void T3_11_MutualExclusion_TurboFan_And_NoiselessFan()
        {
            var hw = new MockHwService();

            // 1. Engage Turbo
            hw.SetTurboFan(true);
            Assert.True(hw.GetTurboFan());
            Assert.Equal(1, hw.VirtualAppSettings[1032]);

            // 2. Engage Noiseless -> Turbo must be disarmed
            hw.SetFanMode(FanMode.Noiseless);
            Assert.False(hw.GetTurboFan());
            Assert.Equal(0, hw.VirtualAppSettings[1032]);
            Assert.Equal(FanMode.Noiseless, hw.GetFanMode());
            Assert.Equal(1, hw.VirtualAppSettings[1033]);

            // 3. Re-engage Turbo -> Noiseless must be disarmed
            hw.SetTurboFan(true);
            Assert.True(hw.GetTurboFan());
            Assert.Equal(1, hw.VirtualAppSettings[1032]);
            Assert.Equal(0, hw.VirtualAppSettings[1033]);
            Assert.NotEqual(FanMode.Noiseless, hw.GetFanMode());
            Assert.Equal(FanMode.Auto, hw.GetFanMode());
            Assert.Equal(0, hw.VirtualAppSettings[1029]);
        }

        [Fact]
        public void T3_12_KeyboardRgb_IdleTimer_NotResetByTelemetryPolling()
        {
            var hw = new MockHwService();
            hw.SetKeyboardBrightness(4);
            hw.SetKeyboardSleepTimeout(15);

            // Continuously query telemetry while advancing time past 15 seconds
            for (int i = 0; i < 16; i++)
            {
                var t = hw.GetTelemetry();
                Assert.NotNull(t);
                hw.AdvanceTime(TimeSpan.FromSeconds(1));
            }

            // Keyboard backlight MUST be sleeping because telemetry does not count as user activity
            Assert.True(hw.IsKeyboardBacklightSleeping());
            Assert.Equal(0, hw.GetEffectiveKeyboardBrightness());
        }

        [Fact]
        public void T3_13_CliRunner_ArgumentValidationAndExitCodes()
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            try
            {
                using var nullWriter = new System.IO.StringWriter();
                Console.SetOut(nullWriter);
                Console.SetError(nullWriter);

                // 1. Invalid numeric tier returns exit code 2
                int invalidTierCode = Program.Main(new[] { "--tier", "99" });
                Assert.Equal(2, invalidTierCode);

                // 2. Non-numeric invalid tier returns exit code 2
                int invalidStringTierCode = Program.Main(new[] { "--tier", "invalid" });
                Assert.Equal(2, invalidStringTierCode);

                // 3. Trailing --tier option without value returns exit code 2
                int missingTierValCode = Program.Main(new[] { "--tier" });
                Assert.Equal(2, missingTierValCode);

                // 4. Unknown CLI flag returns exit code 2
                int unknownFlagCode = Program.Main(new[] { "--bogus-flag" });
                Assert.Equal(2, unknownFlagCode);

                // 5. Trailing --filter option without value returns exit code 2
                int missingFilterValCode = Program.Main(new[] { "--filter" });
                Assert.Equal(2, missingFilterValCode);

                // 6. Zero tests discovered/executed returns exit code 1
                int zeroTestsCode = Program.Main(new[] { "--filter", "NonExistentTestString_Z9X8" });
                Assert.Equal(1, zeroTestsCode);

                // 7. Valid --help returns exit code 0
                int helpCode = Program.Main(new[] { "--help" });
                Assert.Equal(0, helpCode);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }
    }
}
