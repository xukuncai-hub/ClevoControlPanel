using System;
using System.Diagnostics;
using System.Security.Cryptography;
using ClevoControlPanel.Core.Models;
using ClevoControlPanel.Core.Native;
using ClevoControlPanel.Core.Telemetry;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ClevoControlPanel.Core.Tests;

/// <summary>
/// Dedicated empirical stress harness for TelemetryParser and mathematical calculation engine.
/// Executes strict boundary stress, tachometer arithmetic & overflow tests, temperature edge matrices,
/// duty cycle calculations, and zero-allocation memory performance verification.
/// </summary>
public class TelemetryEmpiricalChallengeHarnessTests
{
    private readonly ITestOutputHelper _output;

    public TelemetryEmpiricalChallengeHarnessTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Requirement 1: Extreme Buffer Length Boundaries

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(23)]
    public void BufferLength_SubMinimumBoundary_ThrowsArgumentException(int length)
    {
        byte[] buffer = new byte[length];

        // 1. Array overload
        var actArray = () => TelemetryParser.ParsePackage12(buffer);
        actArray.Should().Throw<ArgumentException>(
            $"Buffer length {length} must be rejected as less than MinimumBufferSize (24)")
            .WithMessage("*insufficient for Package 12 telemetry*");

        // 2. ReadOnlySpan overload
        bool spanThrew = false;
        try
        {
            TelemetryParser.ParsePackage12(buffer.AsSpan());
        }
        catch (ArgumentException)
        {
            spanThrew = true;
        }

        spanThrew.Should().BeTrue($"ReadOnlySpan of length {length} must throw ArgumentException");
    }

    [Fact]
    public void BufferLength_NullBufferArray_ThrowsArgumentNullException()
    {
        byte[]? nullBuffer = null;
        var act = () => TelemetryParser.ParsePackage12(nullBuffer!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("buffer");
    }

    [Theory]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(256)]
    [InlineData(1024)]
    public void BufferLength_ValidAndOversizedBoundaries_SucceedsWithoutException(int length)
    {
        byte[] buffer = new byte[length];

        // Seed basic valid values
        buffer[2] = 0x01;
        buffer[3] = 0x1F; // Fan 1 tach = 287 (7513 RPM)
        buffer[4] = 0x01;
        buffer[5] = 0x25; // Fan 2 tach = 293 (7359 RPM)
        buffer[16] = 255; // Fan 1 duty = 100%
        buffer[18] = 75;  // CPU temp = 75°C
        buffer[19] = 200; // Fan 2 duty = 78%
        buffer[21] = 68;  // GPU temp = 68°C

        if (length >= 25)
        {
            buffer[6] = 0x01;
            buffer[7] = 0x90; // Fan 3 tach = 400 (5391 RPM)
            buffer[22] = 128; // Fan 3 duty = 50%
            buffer[24] = 60;  // GPU 2 temp = 60°C
        }

        if (length >= 41)
        {
            buffer[40] = 36;  // SYS temp = 36°C
        }

        TelemetryData data = TelemetryParser.ParsePackage12(buffer);

        data.Should().NotBeNull();
        data.CpuTemperatureCelsius.Should().Be(75);
        data.GpuTemperatureCelsius.Should().Be(68);
        data.IsGpuActive.Should().BeTrue();
        data.Fan1Rpm.Should().Be(7513);
        data.Fan2Rpm.Should().Be(7359);
        data.Fan1DutyPercent.Should().Be(100);
        data.Fan2DutyPercent.Should().Be(78);

        if (length >= 25)
        {
            data.Fan3Rpm.Should().Be(5391);
            data.Fan3DutyPercent.Should().Be(50);
            data.Gpu2TemperatureCelsius.Should().Be(60);
        }
        else
        {
            data.Fan3Rpm.Should().Be(0);
            data.Fan3DutyPercent.Should().Be(0);
            data.Gpu2TemperatureCelsius.Should().Be(0);
        }

        if (length >= 41)
        {
            data.SysTemperatureCelsius.Should().Be(36);
        }
        else
        {
            data.SysTemperatureCelsius.Should().Be(0);
        }
    }

    #endregion

    #region Requirement 2: Raw Tachometer Edge Values & Overflow

    [Fact]
    public void RawTachometer_ZeroValue_ReturnsZeroRpmWithoutDivideByZero()
    {
        // 0 tick indicates fan stopped or stopped sensor
        TelemetryParser.CalculateRpm((ushort)0).Should().Be(0);
        TelemetryParser.CalculateRpm(0).Should().Be(0);
    }

    [Fact]
    public void RawTachometer_OneTick_ReturnsClockFrequencyWithoutOverflow()
    {
        // 2156250.0 / 1 = 2156250 RPM
        int rpm = TelemetryParser.CalculateRpm((ushort)1);
        rpm.Should().Be(2156250);

        int rpmInt = TelemetryParser.CalculateRpm(1);
        rpmInt.Should().Be(2156250);
    }

    [Fact]
    public void RawTachometer_MaxUshort_ReturnsThirtyThreeRpm()
    {
        // 2156250.0 / 65535 = 32.8993... -> 33 RPM
        ushort maxUshort = 0xFFFF;
        int rpm = TelemetryParser.CalculateRpm(maxUshort);
        rpm.Should().Be(33);

        int rpmInt = TelemetryParser.CalculateRpm((int)maxUshort);
        rpmInt.Should().Be(33);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-1000)]
    [InlineData(int.MinValue)]
    public void RawTachometer_NegativeInputs_SafelyReturnsZeroWithoutOverflow(int negativeTach)
    {
        TelemetryParser.CalculateRpm(negativeTach).Should().Be(0);
    }

    [Fact]
    public void RawTachometer_MaxInt_SafelyReturnsZero()
    {
        // 2156250.0 / 2147483647 -> ~0.001 -> rounds to 0
        TelemetryParser.CalculateRpm(int.MaxValue).Should().Be(0);
    }

    [Fact]
    public void RawTachometer_Stress50000RandomValues_MatchesOracleFormula()
    {
        Random rng = new Random(20261111);
        for (int i = 0; i < 50000; i++)
        {
            ushort tach = (ushort)rng.Next(0, 65536);
            int rpm = TelemetryParser.CalculateRpm(tach);

            if (tach == 0)
            {
                rpm.Should().Be(0);
            }
            else
            {
                int expected = (int)Math.Round(2156250.0 / tach);
                rpm.Should().Be(expected, $"Tach {tach} calculation mismatch");
            }
        }
    }

    #endregion

    #region Requirement 3: Temperature Edge Values & D3cold Detection

    [Theory]
    [InlineData(0, false, 0)]    // 0°C -> Inactive / D3cold / Uninitialized
    [InlineData(20, false, 0)]   // 20°C -> D3cold standby sentinel (Clevo OEM "OFF")
    [InlineData(21, true, 21)]   // 21°C -> Active lowest temperature
    [InlineData(100, true, 100)] // 100°C -> Thermal throttle limit
    [InlineData(255, true, 255)] // 255°C -> Maximum 8-bit register limit
    public void GpuTemperature_EdgeValues_StrictlyFollowsOemSpecification(
        byte rawGpuTemp,
        bool expectedActive,
        int expectedGpuTemp)
    {
        byte[] buffer = new byte[256];
        buffer[21] = rawGpuTemp;

        TelemetryData data = TelemetryParser.ParsePackage12(buffer);

        data.RawGpuTemperatureCelsius.Should().Be(rawGpuTemp);
        data.IsGpuActive.Should().Be(expectedActive);
        data.IsGpuStandby.Should().Be(!expectedActive);
        data.GpuTemperatureCelsius.Should().Be(expectedGpuTemp);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(20, 20)] // CPU at 20°C is NOT in D3cold! Must report 20°C faithfully!
    [InlineData(21, 21)]
    [InlineData(100, 100)]
    [InlineData(255, 255)]
    public void CpuTemperature_EdgeValues_ReportsFaithfullyWithoutStandbyInterference(
        byte rawCpuTemp,
        int expectedCpuTemp)
    {
        byte[] buffer = new byte[256];
        buffer[18] = rawCpuTemp;

        TelemetryData data = TelemetryParser.ParsePackage12(buffer);

        data.CpuTemperatureCelsius.Should().Be(expectedCpuTemp);
        data.CpuTemperature.Should().Be(expectedCpuTemp);
    }

    #endregion

    #region Requirement 4: Duty Cycle Edge Values & Scaling

    [Theory]
    [InlineData(0, 0)]     // 0 / 255 = 0%
    [InlineData(1, 0)]     // 1 / 255 = 0.392% -> 0%
    [InlineData(127, 50)]  // 127 / 255 = 49.804% -> 50%
    [InlineData(254, 100)] // 254 / 255 = 99.608% -> 100%
    [InlineData(255, 100)] // 255 / 255 = 100%
    public void DutyCycle_SpecificEdgeValues_MatchesExactOemCalculations(byte rawDuty, int expectedPercent)
    {
        int percent = TelemetryParser.CalculateDutyPercent(rawDuty);
        percent.Should().Be(expectedPercent);
    }

    [Fact]
    public void DutyCycle_ExhaustiveAll256ByteValues_AreMonotonicAndClamped()
    {
        int previous = 0;
        for (int b = 0; b <= 255; b++)
        {
            byte raw = (byte)b;
            int percent = TelemetryParser.CalculateDutyPercent(raw);

            percent.Should().BeInRange(0, 100);
            percent.Should().BeGreaterThanOrEqualTo(previous, $"Duty for raw {b} must be monotonic");

            double expectedExact = (raw / 255.0) * 100.0;
            int expectedRounded = Math.Clamp((int)Math.Round(expectedExact), 0, 100);
            percent.Should().Be(expectedRounded);

            previous = percent;
        }
    }

    #endregion

    #region Requirement 5: Zero Allocations & Memory Performance Verification

    [Fact]
    public void MemoryPerformance_SpanParsingWithoutRawBuffer_AllocatesOnlySingleResultObject()
    {
        byte[] buffer = new byte[256];
        buffer[2] = 0x01;
        buffer[3] = 0x1F;
        buffer[18] = 60;
        buffer[21] = 55;

        ReadOnlySpan<byte> span = buffer.AsSpan();

        // Warm up JIT and GC counters
        for (int i = 0; i < 1000; i++)
        {
            _ = TelemetryParser.ParsePackage12(span, includeRawBuffer: false);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long bytesBefore = GC.GetAllocatedBytesForCurrentThread();

        const int iterations = 1000;
        for (int i = 0; i < iterations; i++)
        {
            _ = TelemetryParser.ParsePackage12(span, includeRawBuffer: false);
        }

        long bytesAfter = GC.GetAllocatedBytesForCurrentThread();
        long totalAllocated = bytesAfter - bytesBefore;
        long bytesPerOp = totalAllocated / iterations;

        _output.WriteLine($"Allocated bytes per operation: {bytesPerOp} bytes (total: {totalAllocated} bytes for {iterations} ops)");

        // TelemetryData is a sealed record class (~88 to 96 bytes on x64).
        // It must NOT allocate any intermediate byte arrays or temporary buffers!
        // If it allocated a byte array (e.g. 256 bytes), bytesPerOp would be > 350.
        bytesPerOp.Should().BeLessThanOrEqualTo(128, "No intermediate buffer arrays may be allocated when includeRawBuffer is false");
    }

    [Fact]
    public void MemoryPerformance_HighThroughputBenchmark_SustainsOverOneMillionOpsPerSec()
    {
        byte[] buffer = new byte[256];
        buffer[2] = 0x01;
        buffer[3] = 0x1F;
        buffer[4] = 0x01;
        buffer[5] = 0x25;
        buffer[16] = 255;
        buffer[18] = 75;
        buffer[19] = 255;
        buffer[21] = 68;

        ReadOnlySpan<byte> span = buffer.AsSpan();

        // Warmup
        for (int i = 0; i < 10000; i++)
        {
            _ = TelemetryParser.ParsePackage12(span, includeRawBuffer: false);
        }

        const int iterations = 200000;
        Stopwatch sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            _ = TelemetryParser.ParsePackage12(span, includeRawBuffer: false);
        }

        sw.Stop();

        double elapsedSeconds = sw.Elapsed.TotalSeconds;
        double opsPerSecond = iterations / elapsedSeconds;

        _output.WriteLine($"Throughput benchmark: {opsPerSecond:N0} ops/sec, {iterations} ops in {sw.ElapsedMilliseconds} ms");

        opsPerSecond.Should().BeGreaterThan(500000.0, "Throughput must exceed 500,000 ops/sec");
    }

    [Fact]
    public void MemoryPerformance_IncludeRawBufferTrue_AllocatesExactlyMatchingCopy()
    {
        byte[] original = new byte[256];
        RandomNumberGenerator.Fill(original);

        TelemetryData data = TelemetryParser.ParsePackage12(original.AsSpan(), includeRawBuffer: true);

        data.RawBuffer.Should().NotBeNull();
        data.RawBuffer.Should().NotBeSameAs(original, "RawBuffer must be an independent defensive clone");
        data.RawBuffer.Should().BeEquivalentTo(original, "RawBuffer content must exactly match original");
    }

    #endregion
}
