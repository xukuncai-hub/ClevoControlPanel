using System;
using Xunit;
using ClevoControlPanel.Tests.Mocks;

namespace ClevoControlPanel.Tests
{
    [Trait("Tier", "4")]
    public class WorkloadScenarioTests
    {
        [Fact]
        public void Scenario_01_GamingStressLoad()
        {
            var hw = new MockHwService();

            // Step 1: Engage Performance Profile and Gaming Setup
            hw.SetPowerMode(PowerMode.Performance);
            hw.SetTurboFan(true);
            hw.SetKeyboardRgbEffect(RgbEffectMode.Wave, speed: 5);
            hw.SetKeyboardBrightness(4);

            Assert.Equal(PowerMode.Performance, hw.GetPowerMode());
            Assert.True(hw.GetTurboFan());
            Assert.Equal(4, hw.GetKeyboardBrightness());

            // Step 2: Ingest Escalating Thermal Load Frames
            var thermalFrames = new[]
            {
                new TelemetryFrame { CpuTemp = 65, GpuTemp = 60, CpuRawTach = 380, GpuRawTach = 390, CpuDuty = 200, GpuDuty = 190 },
                new TelemetryFrame { CpuTemp = 75, GpuTemp = 70, CpuRawTach = 330, GpuRawTach = 340, CpuDuty = 230, GpuDuty = 220 },
                new TelemetryFrame { CpuTemp = 86, GpuTemp = 79, CpuRawTach = 295, GpuRawTach = 300, CpuDuty = 250, GpuDuty = 245 },
                new TelemetryFrame { CpuTemp = 91, GpuTemp = 83, CpuRawTach = 289, GpuRawTach = 294, CpuDuty = 255, GpuDuty = 255 }  // Peak Turbo
            };

            foreach (var frame in thermalFrames)
            {
                hw.InjectTelemetry(frame);
                var telemetry = hw.GetTelemetry();

                Assert.Equal(frame.CpuTemp, telemetry.CpuTemperature);
                Assert.Equal(frame.GpuTemp, telemetry.GpuTemperature);
                Assert.True(telemetry.CpuFanRpm > 5500);
            }

            var peak = hw.GetTelemetry();
            Assert.Equal(7461, peak.CpuFanRpm);
            Assert.Equal(7334, peak.Gpu1FanRpm);

            // Step 3: Session Complete -> Disarm TurboFan & Return to Normal
            hw.SetTurboFan(false);
            hw.SetFanMode(FanMode.Auto);

            Assert.False(hw.GetTurboFan());
            Assert.Equal(FanMode.Auto, hw.GetFanMode());
        }

        [Fact]
        public void Scenario_02_QuietOfficeEnvironment()
        {
            var hw = new MockHwService();

            // Step 1: Engage Silent Profile
            hw.SetPowerMode(PowerMode.Quiet);
            hw.SetFanMode(FanMode.Noiseless);
            hw.SetFanOffset(0);
            hw.SetTurboFan(false);
            hw.SetKeyboardBrightness(1);
            hw.SetKeyboardRgbColor(new RgbColor(255, 180, 50)); // Warm Amber

            Assert.Equal(PowerMode.Quiet, hw.GetPowerMode());
            Assert.Equal(FanMode.Noiseless, hw.GetFanMode());
            Assert.Equal(1, hw.GetKeyboardBrightness());

            // Step 2: Light Productivity Telemetry (dGPU in D3cold standby)
            hw.InjectTelemetry(new TelemetryFrame
            {
                CpuTemp = 42,
                GpuTemp = 20, // 20°C D3cold signal
                CpuRawTach = 980, // ~2200 RPM
                GpuRawTach = 0,
                CpuDuty = 80,
                GpuDuty = 0
            });

            var telemetry = hw.GetTelemetry();
            Assert.True(telemetry.IsGpuStandby);
            Assert.False(telemetry.IsGpuActive);
            Assert.Equal(0, telemetry.GpuTemperature);
            Assert.Equal(0, telemetry.GpuFanRpm);
            Assert.True(telemetry.CpuFanRpm < 2500);

            // Step 3: Brief Burst Load (document compilation)
            hw.InjectTelemetry(new TelemetryFrame
            {
                CpuTemp = 68,
                GpuTemp = 20,
                CpuRawTach = 720, // ~2995 RPM
                GpuRawTach = 0,
                CpuDuty = 100,
                GpuDuty = 0
            });

            var burst = hw.GetTelemetry();
            Assert.Equal(68, burst.CpuTemperature);
            Assert.True(burst.IsGpuStandby);
            Assert.True(burst.CpuFanRpm <= 3200); // Clamped by acoustic ceiling
        }

        [Fact]
        public void Scenario_03_IdleSleepLifecycle()
        {
            var hw = new MockHwService();

            // Step 1: Active Configuration
            hw.SetKeyboardRgbColor(new RgbColor(0, 120, 255), KeyboardZone.Left);
            hw.SetKeyboardRgbColor(new RgbColor(255, 255, 255), KeyboardZone.Middle);
            hw.SetKeyboardRgbColor(new RgbColor(255, 50, 50), KeyboardZone.Right);
            hw.SetKeyboardBrightness(3);
            hw.SetKeyboardSleepTimeout(15);

            Assert.Equal(3, hw.GetEffectiveKeyboardBrightness());
            Assert.False(hw.IsKeyboardBacklightSleeping());

            // Step 2: Inactivity Period (User steps away for 16 seconds)
            hw.AdvanceTime(TimeSpan.FromSeconds(16));

            // Backlight turns off
            Assert.True(hw.IsKeyboardBacklightSleeping());
            Assert.Equal(0, hw.GetEffectiveKeyboardBrightness());

            // Background telemetry polling does NOT wake keyboard
            var t = hw.GetTelemetry();
            Assert.NotNull(t);
            Assert.True(hw.IsKeyboardBacklightSleeping());

            // Step 3: User Activity Detected (User strikes a key)
            hw.TriggerUserActivity();

            Assert.False(hw.IsKeyboardBacklightSleeping());
            Assert.Equal(3, hw.GetEffectiveKeyboardBrightness());
            Assert.Equal(new RgbColor(0, 120, 255), hw.GetKeyboardZoneColor(KeyboardZone.Left));
            Assert.Equal(new RgbColor(255, 255, 255), hw.GetKeyboardZoneColor(KeyboardZone.Middle));
            Assert.Equal(new RgbColor(255, 50, 50), hw.GetKeyboardZoneColor(KeyboardZone.Right));
        }

        [Fact]
        public void Scenario_04_BatterySaverOnTheGo()
        {
            var hw = new MockHwService();

            // Step 1: Engage Battery Conservation
            hw.SetPowerMode(PowerMode.PowerSaving);
            hw.SetFanMode(FanMode.Auto);
            hw.SetFanOffset(0);
            hw.SetKeyboardBrightness(1);
            hw.SetKeyboardSleepTimeout(15);

            Assert.Equal(PowerMode.PowerSaving, hw.GetPowerMode());
            Assert.Equal(1, hw.VirtualAppSettings[257]);
            Assert.Equal(1, hw.GetKeyboardBrightness());
            Assert.Equal(15, hw.GetKeyboardSleepTimeout());

            // Step 2: Mobile Telemetry Check
            hw.InjectTelemetry(new TelemetryFrame
            {
                CpuTemp = 44,
                GpuTemp = 20, // dGPU standby
                CpuRawTach = 1050, // ~2053 RPM
                GpuRawTach = 0,
                CpuDuty = 70,
                GpuDuty = 0
            });

            var telemetry = hw.GetTelemetry();
            Assert.True(telemetry.IsGpuStandby);
            Assert.Equal(0, telemetry.GpuTemperature);
            Assert.True(telemetry.CpuFanRpm < 2200);
        }

        [Fact]
        public void Scenario_05_ThermalRunawayMitigation()
        {
            var hw = new MockHwService();

            // Step 1: Initial Quiet Profile
            hw.SetPowerMode(PowerMode.Quiet);
            hw.SetFanMode(FanMode.Silent);

            // Step 2: Rapid Thermal Runaway Surge (96°C)
            hw.InjectTelemetry(new TelemetryFrame
            {
                CpuTemp = 96,
                GpuTemp = 89,
                CpuRawTach = 600,
                GpuRawTach = 600,
                CpuDuty = 120,
                GpuDuty = 120
            });

            var surge = hw.GetTelemetry();
            Assert.Equal(96, surge.CpuTemperature);

            // Step 3: Trigger Emergency Turbo Cooling
            hw.SetTurboFan(true);
            hw.InjectTelemetry(new TelemetryFrame
            {
                CpuTemp = 84,
                GpuTemp = 76,
                CpuRawTach = 289,
                GpuRawTach = 294,
                CpuDuty = 255,
                GpuDuty = 255
            });

            var cooled = hw.GetTelemetry();
            Assert.Equal(7461, cooled.CpuFanRpm);

            // Step 4: Temperature shed down to safe 62°C
            hw.InjectTelemetry(new TelemetryFrame
            {
                CpuTemp = 62,
                GpuTemp = 58,
                CpuRawTach = 289,
                GpuRawTach = 294,
                CpuDuty = 255,
                GpuDuty = 255
            });

            // Disarm TurboFan -> returns to Quiet
            hw.SetTurboFan(false);
            Assert.Equal(PowerMode.Quiet, hw.GetPowerMode());
        }

        [Fact]
        public void Scenario_06_HardwareFallbackAndSimulation()
        {
            var hw = new MockHwService();

            // Fallback flag check
            Assert.False(hw.IsLiveHardware);

            // Test full suite of actions on simulated fallback engine
            hw.SetPowerMode(PowerMode.Performance);
            Assert.Equal(PowerMode.Performance, hw.GetPowerMode());

            hw.SetFanMode(FanMode.Auto);
            hw.SetFanOffset(45);
            Assert.Equal(45, hw.GetFanOffset());

            var telemetry = hw.GetTelemetry();
            Assert.InRange(telemetry.CpuTemperature, 35, 95);
            Assert.True(telemetry.CpuFanRpm > 0);

            hw.SetKeyboardRgbEffect(RgbEffectMode.Breath);
            hw.SetKeyboardBrightness(2);
            Assert.Equal(RgbEffectMode.Breath, hw.GetKeyboardState().Mode);

            // Verify Fault Injection simulation
            hw.SetSimulateFailure(true);
            Assert.Throws<InvalidOperationException>(() => hw.SetPowerMode(PowerMode.Quiet));

            // Recovers after failure
            hw.SetPowerMode(PowerMode.Quiet);
            Assert.Equal(PowerMode.Quiet, hw.GetPowerMode());
        }
    }
}
