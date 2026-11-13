using System;
using Xunit;
using ClevoControlPanel.Tests.Mocks;

namespace ClevoControlPanel.Tests
{
    [Trait("Tier", "1")]
    [Trait("Tier", "2")]
    public class TelemetryParsingTests
    {
        [Fact]
        public void CalculateRpm_ZeroTicks_ReturnsZeroRpmWithoutException()
        {
            int rpm = TelemetryParser.CalculateRpm(0);
            Assert.Equal(0, rpm);
        }

        [Fact]
        public void CalculateRpm_NegativeTicks_ClampsToZero()
        {
            int rpm = TelemetryParser.CalculateRpm(-50);
            Assert.Equal(0, rpm);
        }

        [Theory]
        [InlineData(289, 7461)]   // Max turbo verified live
        [InlineData(294, 7334)]   // GPU live verified
        [InlineData(431, 5003)]   // High gaming
        [InlineData(719, 2999)]   // Balanced
        [InlineData(1437, 1501)]  // Silent cruising
        [InlineData(65535, 33)]   // Max uint16 clock tick
        [InlineData(1, 2156250)]  // Single tick upper bound
        public void CalculateRpm_LiveVerifiedAndExtremeTicks_CalculatesAccurately(int rawTach, int expectedRpm)
        {
            int calculatedRpm = TelemetryParser.CalculateRpm(rawTach);
            Assert.InRange(calculatedRpm, expectedRpm - 1, expectedRpm + 1);
        }

        [Fact]
        public void ParsePackage12_BigEndianByteDecoding_ValidatesOrder()
        {
            byte[] buffer = new byte[256];
            // 289 = 0x0121 -> high byte 0x01, low byte 0x21
            buffer[2] = 0x01;
            buffer[3] = 0x21;

            var telemetry = TelemetryParser.ParsePackage12(buffer);

            Assert.Equal(289, telemetry.RawCpuTach);
            Assert.Equal(7461, telemetry.CpuFanRpm);
        }

        [Fact]
        public void ParsePackage12_CpuTemperatureLevels_ParseCorrectly()
        {
            byte[] buffer = new byte[256];
            buffer[18] = 82; // CPU Load Temp

            var telemetry = TelemetryParser.ParsePackage12(buffer);

            Assert.Equal(82, telemetry.CpuTemperature);
        }

        [Theory]
        [InlineData(45)]
        [InlineData(68)]
        [InlineData(98)]
        public void ParsePackage12_VariousCpuTemperatures_ParseAccurately(byte temp)
        {
            byte[] buffer = new byte[256];
            buffer[18] = temp;

            var telemetry = TelemetryParser.ParsePackage12(buffer);

            Assert.Equal(temp, telemetry.CpuTemperature);
        }

        [Fact]
        public void ParsePackage12_WhenGpuTempIs20_FlagsD3coldStandbyAndZeroesRpm()
        {
            byte[] buffer = new byte[256];
            buffer[21] = 20; // D3cold sentinel value
            buffer[4] = 0x01;
            buffer[5] = 0x26; // 294 raw tach if awake

            var telemetry = TelemetryParser.ParsePackage12(buffer);

            Assert.True(telemetry.IsGpuStandby);
            Assert.False(telemetry.IsGpuActive);
            Assert.Equal(0, telemetry.GpuTemperature);
            Assert.Equal(0, telemetry.Gpu1FanRpm);
            Assert.Equal(0, telemetry.Gpu1FanDuty);
        }

        [Theory]
        [InlineData(19, 19, true, false)]
        [InlineData(21, 21, true, false)]
        [InlineData(65, 65, true, false)]
        [InlineData(85, 85, true, false)]
        [InlineData(0, 0, false, false)]
        public void ParsePackage12_GpuTemperatureBoundaries_FlagsActiveAccurately(byte rawGpuTemp, int expectedTemp, bool expectedActive, bool expectedStandby)
        {
            byte[] buffer = new byte[256];
            buffer[21] = rawGpuTemp;

            var telemetry = TelemetryParser.ParsePackage12(buffer);

            Assert.Equal(expectedTemp, telemetry.GpuTemperature);
            Assert.Equal(expectedActive, telemetry.IsGpuActive);
            Assert.Equal(expectedStandby, telemetry.IsGpuStandby);
        }

        [Fact]
        public void ParsePackage12_DutyCyclesAndPercentages_CalculatesCorrectly()
        {
            byte[] buffer = new byte[256];
            buffer[16] = 128; // CPU ~50%
            buffer[19] = 255; // GPU 100%
            buffer[21] = 60;  // Active GPU

            var telemetry = TelemetryParser.ParsePackage12(buffer);

            Assert.Equal(128, telemetry.CpuFanDuty);
            Assert.Equal(50, telemetry.CpuFanDutyPercent);
            Assert.Equal(255, telemetry.Gpu1FanDuty);
            Assert.Equal(100, telemetry.GpuFanDutyPercent);
        }

        [Fact]
        public void ParsePackage12_DualFanIndependentParsing_ZeroCrosstalk()
        {
            byte[] buffer = new byte[256];
            buffer[2] = 0x01; buffer[3] = 0x21; // CPU = 289 -> 7461 RPM
            buffer[4] = 0x01; buffer[5] = 0x26; // GPU1 = 294 -> 7334 RPM
            buffer[21] = 70; // GPU Active

            var telemetry = TelemetryParser.ParsePackage12(buffer);

            Assert.Equal(7461, telemetry.CpuFanRpm);
            Assert.Equal(7334, telemetry.Gpu1FanRpm);
            Assert.Equal(7334, telemetry.GpuFanRpm);
        }

        [Fact]
        public void ParsePackage12_MotherboardAmbientTemp_ParsesCorrectly()
        {
            byte[] buffer = new byte[256];
            buffer[40] = 38; // Motherboard temp offset 40

            var telemetry = TelemetryParser.ParsePackage12(buffer);

            Assert.Equal(38, telemetry.SystemTemperature);
        }

        [Fact]
        public void ParsePackage12_NullBuffer_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => TelemetryParser.ParsePackage12(null!));
        }

        [Fact]
        public void ParsePackage12_UndersizedBuffer_ThrowsArgumentException()
        {
            byte[] undersized = new byte[128];
            var ex = Assert.Throws<ArgumentException>(() => TelemetryParser.ParsePackage12(undersized));
            Assert.Contains("256", ex.Message);
        }
    }
}
