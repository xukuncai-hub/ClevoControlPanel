using System;
using ClevoControlPanel.Core.Models;
using ClevoControlPanel.Core.Native;
using ClevoControlPanel.Core.Telemetry;
using FluentAssertions;
using Xunit;

namespace ClevoControlPanel.Core.Tests;

public class TelemetryParserTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(23)]
    public void ParsePackage12_BufferTooSmall_ThrowsArgumentException(int length)
    {
        byte[] undersizedBuffer = new byte[length];
        var act = () => TelemetryParser.ParsePackage12(undersizedBuffer);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*insufficient for Package 12 telemetry*");
    }

    [Fact]
    public void ParsePackage12_BufferNull_ThrowsArgumentNullException()
    {
        byte[]? nullBuffer = null;
        var act = () => TelemetryParser.ParsePackage12(nullBuffer!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ParsePackage12_ExactMinimumBoundary_ParsesSuccessfully()
    {
        byte[] minBuffer = new byte[24];
        // CPU tach = 1000 (0x03E8) -> 2156250 / 1000 = 2156 RPM
        minBuffer[2] = 0x03;
        minBuffer[3] = 0xE8;
        // GPU tach = 600 (0x0258) -> 2156250 / 600 = 3594 RPM
        minBuffer[4] = 0x02;
        minBuffer[5] = 0x58;
        // CPU duty = 128 (~50%)
        minBuffer[16] = 128;
        // CPU temp = 65°C
        minBuffer[18] = 65;
        // GPU duty = 255 (100%)
        minBuffer[19] = 255;
        // GPU temp = 72°C (Active)
        minBuffer[21] = 72;

        TelemetryData result = TelemetryParser.ParsePackage12(minBuffer);

        result.CpuTemperatureCelsius.Should().Be(65);
        result.GpuTemperatureCelsius.Should().Be(72);
        result.IsGpuActive.Should().BeTrue();
        result.IsGpuStandby.Should().BeFalse();
        result.Fan1Rpm.Should().Be(2156);
        result.Fan2Rpm.Should().Be(3594);
        result.Fan1DutyPercent.Should().Be(50);
        result.Fan2DutyPercent.Should().Be(100);
        result.RawBuffer.Should().BeNull();
    }

    [Fact]
    public void ParsePackage12_IncludesRawBufferWhenRequested()
    {
        byte[] buffer = new byte[256];
        buffer[18] = 45;

        TelemetryData result = TelemetryParser.ParsePackage12(buffer, includeRawBuffer: true);

        result.RawBuffer.Should().NotBeNull();
        result.RawBuffer!.Length.Should().Be(256);
        result.RawBuffer[18].Should().Be(45);
    }

    [Fact]
    public void ParsePackage12_LiveHardwareSample_MatchesVerifiedCalculations()
    {
        // Live Probe Vector from actual Clevo machine:
        // CPU Raw = 287 (0x011F), Duty = 255, Temp = 87°C
        // GPU1 Raw = 293 (0x0125), Duty = 255, Temp = 76°C
        byte[] buffer = new byte[256];
        buffer[2] = 0x01;
        buffer[3] = 0x1F;
        buffer[4] = 0x01;
        buffer[5] = 0x25;
        buffer[16] = 255;
        buffer[18] = 87;
        buffer[19] = 255;
        buffer[21] = 76;

        TelemetryData result = TelemetryParser.ParsePackage12(buffer);

        result.CpuTemperatureCelsius.Should().Be(87);
        result.GpuTemperatureCelsius.Should().Be(76);
        result.IsGpuActive.Should().BeTrue();
        result.IsGpuStandby.Should().BeFalse();
        result.Fan1Rpm.Should().Be(7513); // round(2156250.0 / 287) = 7513
        result.Fan2Rpm.Should().Be(7359); // round(2156250.0 / 293) = 7359
        result.Fan1DutyPercent.Should().Be(100);
        result.Fan2DutyPercent.Should().Be(100);
        result.CpuTemperature.Should().Be(87);
        result.GpuTemperature.Should().Be(76);
        result.CpuFanRpm.Should().Be(7513);
        result.GpuFanRpm.Should().Be(7359);
    }

    [Fact]
    public void ParsePackage12_GpuStandbySentinel_20Celsius_ReportsInactiveAndZeroTemp()
    {
        byte[] buffer = new byte[256];
        buffer[18] = 52; // CPU 52°C
        buffer[21] = 20; // GPU 20°C (D3cold sentinel)

        TelemetryData result = TelemetryParser.ParsePackage12(buffer);

        result.CpuTemperatureCelsius.Should().Be(52);
        result.GpuTemperatureCelsius.Should().Be(0);
        result.IsGpuActive.Should().BeFalse();
        result.IsGpuStandby.Should().BeTrue();
        result.RawGpuTemperatureCelsius.Should().Be(20);
    }

    [Fact]
    public void ParsePackage12_GpuZeroCelsius_ReportsInactiveAndZeroTemp()
    {
        byte[] buffer = new byte[256];
        buffer[18] = 50;
        buffer[21] = 0; // GPU 0°C (uninitialized / powered off)

        TelemetryData result = TelemetryParser.ParsePackage12(buffer);

        result.CpuTemperatureCelsius.Should().Be(50);
        result.GpuTemperatureCelsius.Should().Be(0);
        result.IsGpuActive.Should().BeFalse();
        result.IsGpuStandby.Should().BeTrue();
    }

    [Theory]
    [InlineData(19, 19)]
    [InlineData(21, 21)]
    [InlineData(50, 50)]
    [InlineData(78, 78)]
    public void ParsePackage12_GpuActiveTemperatures_ReportsActiveAndPreservesTemp(byte rawGpuTemp, int expectedTemp)
    {
        byte[] buffer = new byte[256];
        buffer[18] = 40;
        buffer[21] = rawGpuTemp;

        TelemetryData result = TelemetryParser.ParsePackage12(buffer);

        result.GpuTemperatureCelsius.Should().Be(expectedTemp);
        result.IsGpuActive.Should().BeTrue();
        result.IsGpuStandby.Should().BeFalse();
        result.RawGpuTemperatureCelsius.Should().Be(rawGpuTemp);
    }

    [Fact]
    public void ParsePackage12_AllZeroBuffer_HandlesGracefullyWithoutCrash()
    {
        byte[] zeroBuffer = new byte[256];
        TelemetryData result = TelemetryParser.ParsePackage12(zeroBuffer);

        result.CpuTemperatureCelsius.Should().Be(0);
        result.GpuTemperatureCelsius.Should().Be(0);
        result.IsGpuActive.Should().BeFalse();
        result.Fan1Rpm.Should().Be(0);
        result.Fan2Rpm.Should().Be(0);
        result.Fan1DutyPercent.Should().Be(0);
        result.Fan2DutyPercent.Should().Be(0);
    }

    [Fact]
    public void ParsePackage12_SecondaryFansAndSysTemp_PopulatesWhenBufferLargeEnough()
    {
        byte[] buffer = new byte[256];
        // Fan 3 raw tach at bytes 6..7 = 400 (0x0190) -> 2156250 / 400 = 5391 RPM
        buffer[6] = 0x01;
        buffer[7] = 0x90;
        // Fan 3 duty at byte 22 = 128 (50%)
        buffer[22] = 128;
        // GPU2 temp at byte 24 = 68°C
        buffer[24] = 68;
        // SYS temp at byte 40 = 35°C
        buffer[40] = 35;

        TelemetryData result = TelemetryParser.ParsePackage12(buffer);

        result.Fan3Rpm.Should().Be(5391);
        result.Fan3DutyPercent.Should().Be(50);
        result.Gpu2TemperatureCelsius.Should().Be(68);
        result.SysTemperatureCelsius.Should().Be(35);
    }

    [Fact]
    public void CalculateRpm_ZeroTach_ReturnsZeroWithoutDivideByZeroException()
    {
        TelemetryParser.CalculateRpm((ushort)0).Should().Be(0);
        TelemetryParser.CalculateRpm(0).Should().Be(0);
        TelemetryParser.CalculateRpm(-1).Should().Be(0);
    }

    [Theory]
    [InlineData(260, 8293)]
    [InlineData(287, 7513)]
    [InlineData(289, 7461)]
    [InlineData(293, 7359)]
    [InlineData(294, 7334)]
    [InlineData(296, 7285)]
    [InlineData(321, 6717)]
    [InlineData(400, 5391)]
    [InlineData(600, 3594)]
    [InlineData(1000, 2156)]
    public void CalculateRpm_KnownTachCounts_MatchesMathematicalQuotients(ushort rawTach, int expectedRpm)
    {
        TelemetryParser.CalculateRpm(rawTach).Should().Be(expectedRpm);
        TelemetryParser.CalculateRpm((int)rawTach).Should().Be(expectedRpm);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(64, 25)]
    [InlineData(128, 50)]
    [InlineData(192, 75)]
    [InlineData(229, 90)]
    [InlineData(255, 100)]
    public void CalculateDutyPercent_ValidBytes_ProducesCorrectPercentage(byte rawDuty, int expectedPercent)
    {
        TelemetryParser.CalculateDutyPercent(rawDuty).Should().Be(expectedPercent);
    }
}
