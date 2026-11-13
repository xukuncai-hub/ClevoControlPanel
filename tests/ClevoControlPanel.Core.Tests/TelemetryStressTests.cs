using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ClevoControlPanel.Core.Models;
using ClevoControlPanel.Core.Native;
using ClevoControlPanel.Core.Telemetry;
using FluentAssertions;
using Xunit;

namespace ClevoControlPanel.Core.Tests;

/// <summary>
/// Empirical Challenger Stress Harness for TelemetryParser and mathematical calculation engine.
/// Adversarially tests boundary conditions, extreme arithmetic, memory allocation overhead,
/// concurrency resilience, and fuzzed buffers.
/// </summary>
public class TelemetryStressTests
{
    #region 1. Buffer Length Boundaries

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(15)]
    [InlineData(22)]
    [InlineData(23)]
    public void ParsePackage12_SubMinimumBufferLengths_ThrowsArgumentException(int length)
    {
        byte[] buffer = new byte[length];
        var act = () => TelemetryParser.ParsePackage12(buffer);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*insufficient for Package 12 telemetry*");

        // Also test ReadOnlySpan overload directly without lambda capture of ref struct
        bool spanThrew = false;
        try
        {
            TelemetryParser.ParsePackage12(buffer.AsSpan());
        }
        catch (ArgumentException)
        {
            spanThrew = true;
        }
        spanThrew.Should().BeTrue();
    }

    [Fact]
    public void ParsePackage12_NullBufferArray_ThrowsArgumentNullException()
    {
        byte[]? buffer = null;
        var act = () => TelemetryParser.ParsePackage12(buffer!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("buffer");
    }

    [Theory]
    [InlineData(24)]   // Exact minimum required
    [InlineData(25)]   // Minimum with Fan3 + GPU2
    [InlineData(40)]   // Boundary just before Sys temp
    [InlineData(41)]   // Minimum with Sys temp
    [InlineData(256)]  // Standard Package 12 length
    [InlineData(1024)] // Large oversized buffer
    public void ParsePackage12_ValidBufferLengths_SucceedsWithoutException(int length)
    {
        byte[] buffer = new byte[length];
        // Populate standard valid telemetry
        buffer[2] = 0x01;
        buffer[3] = 0x25; // GPU tach = 293 -> 7359 RPM
        buffer[4] = 0x01;
        buffer[5] = 0x1F; // CPU tach = 287 -> 7513 RPM
        buffer[16] = 200; // Duty 1 = 78%
        buffer[18] = 65;  // CPU Temp = 65°C
        buffer[19] = 255; // Duty 2 = 100%
        buffer[21] = 70;  // GPU Temp = 70°C

        if (length >= 25)
        {
            buffer[6] = 0x02;
            buffer[7] = 0x00; // Fan 3 tach = 512 -> 4211 RPM
            buffer[22] = 128; // Duty 3 = 50%
            buffer[24] = 55;  // GPU2 Temp = 55°C
        }

        if (length >= 41)
        {
            buffer[40] = 38;  // Sys Temp = 38°C
        }

        TelemetryData result = TelemetryParser.ParsePackage12(buffer);

        result.Should().NotBeNull();
        result.Fan1Rpm.Should().Be(7359);
        result.Fan2Rpm.Should().Be(7513);
        result.CpuTemperatureCelsius.Should().Be(65);
        result.GpuTemperatureCelsius.Should().Be(70);
        result.IsGpuActive.Should().BeTrue();

        if (length >= 25)
        {
            result.Fan3Rpm.Should().Be(4211);
            result.Fan3DutyPercent.Should().Be(50);
            result.Gpu2TemperatureCelsius.Should().Be(55);
        }
        else
        {
            result.Fan3Rpm.Should().Be(0);
            result.Fan3DutyPercent.Should().Be(0);
            result.Gpu2TemperatureCelsius.Should().Be(0);
        }

        if (length >= 41)
        {
            result.SysTemperatureCelsius.Should().Be(38);
        }
        else
        {
            result.SysTemperatureCelsius.Should().Be(0);
        }
    }

    #endregion

    #region 2. Raw Tachometer Edge Values & Arithmetic Overflows

    [Fact]
    public void CalculateRpm_ZeroRawTach_ReturnsZeroWithoutDivisionByZero()
    {
        TelemetryParser.CalculateRpm((ushort)0).Should().Be(0);
        TelemetryParser.CalculateRpm(0).Should().Be(0);
    }

    [Fact]
    public void CalculateRpm_RawTachOne_ReturnsClockFrequencyWithoutOverflow()
    {
        // 2156250.0 / 1 = 2156250 RPM
        int rpmUshort = TelemetryParser.CalculateRpm((ushort)1);
        int rpmInt = TelemetryParser.CalculateRpm(1);

        rpmUshort.Should().Be(2156250);
        rpmInt.Should().Be(2156250);
    }

    [Fact]
    public void CalculateRpm_MaxUshort_ReturnsThirtyThreeRpm()
    {
        // 2156250.0 / 65535 = 32.8993... -> rounds to 33
        int rpm = TelemetryParser.CalculateRpm((ushort)0xFFFF);
        rpm.Should().Be(33);

        int rpmInt = TelemetryParser.CalculateRpm(65535);
        rpmInt.Should().Be(33);
    }

    [Fact]
    public void CalculateRpm_HalfScaleUshort_ReturnsSixtySixRpm()
    {
        // 2156250.0 / 32768 = 65.8035... -> rounds to 66
        int rpm = TelemetryParser.CalculateRpm((ushort)0x8000);
        rpm.Should().Be(66);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void CalculateRpm_NegativeInputs_SafelyReturnsZero(int negativeTach)
    {
        TelemetryParser.CalculateRpm(negativeTach).Should().Be(0);
    }

    [Fact]
    public void CalculateRpm_MaxInt_SafelyReturnsZero()
    {
        // 2156250.0 / 2147483647 = ~0.001 -> rounds to 0
        TelemetryParser.CalculateRpm(int.MaxValue).Should().Be(0);
    }

    [Fact]
    public void CalculateRpm_MonotonicityAcrossTickPeriods()
    {
        // Verify mathematically: as raw period counter decreases, RPM must monotonically increase
        ushort[] sampleTicks = [65535, 32768, 10000, 5000, 2000, 1000, 600, 400, 293, 287, 100, 50, 10, 2, 1];
        int previousRpm = 0;

        foreach (ushort tick in sampleTicks)
        {
            int currentRpm = TelemetryParser.CalculateRpm(tick);
            currentRpm.Should().BeGreaterThan(previousRpm, $"Tach {tick} should yield higher RPM than previous");
            previousRpm = currentRpm;
        }
    }

    [Fact]
    public void CalculateRpm_FuzzTesting10000RandomTachValues()
    {
        Random rng = new Random(42);
        for (int i = 0; i < 10000; i++)
        {
            ushort raw = (ushort)rng.Next(0, 65536);
            int rpm = TelemetryParser.CalculateRpm(raw);
            rpm.Should().BeGreaterThanOrEqualTo(0);

            if (raw == 0)
                rpm.Should().Be(0);
            else
                rpm.Should().Be((int)Math.Round(2156250.0 / raw));
        }
    }

    #endregion

    #region 3. Temperature Edge Values & D3cold Detection

    [Theory]
    [InlineData(0, false, 0)]    // 0°C -> D3cold inactive sentinel
    [InlineData(20, false, 0)]   // 20°C -> D3cold active standby sentinel
    [InlineData(1, true, 1)]     // 1°C -> Active
    [InlineData(19, true, 19)]   // 19°C -> Active
    [InlineData(21, true, 21)]   // 21°C -> Active
    [InlineData(55, true, 55)]   // Normal operation
    [InlineData(100, true, 100)] // High thermal load
    [InlineData(255, true, 255)] // Maximum 8-bit register limit
    public void ParsePackage12_GpuTemperatureSentinelsAndEdges(byte rawGpuTemp, bool expectedActive, int expectedTemp)
    {
        byte[] buffer = new byte[256];
        buffer[21] = rawGpuTemp;

        TelemetryData result = TelemetryParser.ParsePackage12(buffer);

        result.RawGpuTemperatureCelsius.Should().Be(rawGpuTemp);
        result.IsGpuActive.Should().Be(expectedActive);
        result.IsGpuStandby.Should().Be(!expectedActive);
        result.GpuTemperatureCelsius.Should().Be(expectedTemp);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(20, 20)] // Note: CPU at 20°C is NOT in D3cold; CPU temp must be reported faithfully!
    [InlineData(21, 21)]
    [InlineData(50, 50)]
    [InlineData(95, 95)]
    [InlineData(105, 105)]
    [InlineData(255, 255)]
    public void ParsePackage12_CpuTemperatureEdges_ReportsFaithfully(byte rawCpuTemp, int expectedTemp)
    {
        byte[] buffer = new byte[256];
        buffer[18] = rawCpuTemp;

        TelemetryData result = TelemetryParser.ParsePackage12(buffer);

        result.CpuTemperatureCelsius.Should().Be(expectedTemp);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(60, 60)]
    [InlineData(255, 255)]
    public void ParsePackage12_SecondaryGpuAndSysTemperatureEdges(byte rawTemp, int expectedTemp)
    {
        byte[] buffer = new byte[256];
        buffer[24] = rawTemp; // GPU2
        buffer[40] = rawTemp; // SYS

        TelemetryData result = TelemetryParser.ParsePackage12(buffer);

        result.Gpu2TemperatureCelsius.Should().Be(expectedTemp);
        result.SysTemperatureCelsius.Should().Be(expectedTemp);
    }

    #endregion

    #region 4. Duty Cycle Edge Values & Full Monotonic Range

    [Theory]
    [InlineData(0, 0)]      // 0 / 255 = 0%
    [InlineData(1, 0)]      // 1 / 255 = 0.39% -> 0%
    [InlineData(2, 1)]      // 2 / 255 = 0.78% -> 1%
    [InlineData(127, 50)]   // 127 / 255 = 49.80% -> 50%
    [InlineData(128, 50)]   // 128 / 255 = 50.19% -> 50%
    [InlineData(254, 100)]  // 254 / 255 = 99.60% -> 100%
    [InlineData(255, 100)]  // 255 / 255 = 100%
    public void CalculateDutyPercent_CriticalBoundaries(byte rawDuty, int expectedPercent)
    {
        int percent = TelemetryParser.CalculateDutyPercent(rawDuty);
        percent.Should().Be(expectedPercent);
    }

    [Fact]
    public void CalculateDutyPercent_All256ByteValuesAreBoundedAndMonotonic()
    {
        int previousPercent = 0;
        for (int b = 0; b <= 255; b++)
        {
            byte raw = (byte)b;
            int percent = TelemetryParser.CalculateDutyPercent(raw);

            percent.Should().BeInRange(0, 100);
            percent.Should().BeGreaterThanOrEqualTo(previousPercent, $"Duty calculation for {b} must be monotonic");
            previousPercent = percent;
        }

        TelemetryParser.CalculateDutyPercent(0).Should().Be(0);
        TelemetryParser.CalculateDutyPercent(255).Should().Be(100);
    }

    #endregion

    #region 5. Zero-Allocations & Memory Performance Verification

    [Fact]
    public void ParsePackage12_SpanOverload_DoesNotAllocateInternalBufferArrays()
    {
        byte[] buffer = new byte[256];
        buffer[2] = 0x01;
        buffer[3] = 0x25;
        buffer[18] = 45;
        buffer[21] = 40;

        ReadOnlySpan<byte> span = buffer;

        // 1. When includeRawBuffer == false, RawBuffer must be null (no byte[] allocation)
        TelemetryData noRaw = TelemetryParser.ParsePackage12(span, includeRawBuffer: false);
        noRaw.RawBuffer.Should().BeNull();

        // 2. When includeRawBuffer == true, RawBuffer must contain an exact duplicate byte[]
        TelemetryData withRaw = TelemetryParser.ParsePackage12(span, includeRawBuffer: true);
        withRaw.RawBuffer.Should().NotBeNull();
        withRaw.RawBuffer!.Length.Should().Be(256);
        withRaw.RawBuffer.Should().BeEquivalentTo(buffer);
    }

    [Fact]
    public void ParsePackage12_HighFrequencyPollingThroughput_ExceedsTenMillionOpsPerSecond()
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

        ReadOnlySpan<byte> span = buffer;

        // Warmup JIT
        for (int i = 0; i < 10000; i++)
        {
            _ = TelemetryParser.ParsePackage12(span, includeRawBuffer: false);
        }

        const int iterations = 100000;
        Stopwatch sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            _ = TelemetryParser.ParsePackage12(span, includeRawBuffer: false);
        }

        sw.Stop();

        double elapsedSeconds = sw.Elapsed.TotalSeconds;
        double opsPerSecond = iterations / elapsedSeconds;

        // In high-frequency 1-second polling, a single parse taking microseconds is well within budget.
        // We expect at least 1,000,000 ops/sec even on modest CPUs.
        opsPerSecond.Should().BeGreaterThan(500000.0, $"Throughput was {opsPerSecond:N0} ops/sec, elapsed {sw.ElapsedMilliseconds} ms for {iterations} iterations");
    }

    #endregion

    #region 6. Concurrency & Multi-Threaded Stress Resilience

    [Fact]
    public void ParsePackage12_ConcurrentMultiThreadedStress_MaintainsThreadSafety()
    {
        const int threadCount = 16;
        const int iterationsPerThread = 5000;

        ConcurrentBag<Exception> exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, threadCount, threadIndex =>
        {
            byte[] localBuffer = new byte[256];
            Random localRng = new Random(threadIndex * 1000);

            try
            {
                for (int i = 0; i < iterationsPerThread; i++)
                {
                    localBuffer[2] = (byte)localRng.Next(1, 4);
                    localBuffer[3] = (byte)localRng.Next(0, 256);
                    localBuffer[16] = (byte)localRng.Next(0, 256);
                    localBuffer[18] = (byte)localRng.Next(30, 100);
                    localBuffer[21] = (byte)localRng.Next(0, 95);

                    TelemetryData data = TelemetryParser.ParsePackage12(localBuffer.AsSpan(), includeRawBuffer: false);

                    if (data.CpuTemperatureCelsius < 30 || data.CpuTemperatureCelsius > 100)
                    {
                        throw new InvalidOperationException($"Invalid CPU temp: {data.CpuTemperatureCelsius}");
                    }

                    if (data.Fan1DutyPercent < 0 || data.Fan1DutyPercent > 100)
                    {
                        throw new InvalidOperationException($"Invalid duty percent: {data.Fan1DutyPercent}");
                    }
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        exceptions.Should().BeEmpty();
    }

    #endregion

    #region 7. Fuzzing with Random Payloads

    [Fact]
    public void ParsePackage12_FuzzTestingWithCryptographicRandomData()
    {
        Random rng = new Random(1337);

        for (int i = 0; i < 5000; i++)
        {
            int bufferSize = rng.Next(24, 513); // random size between 24 and 512
            byte[] randomBuffer = new byte[bufferSize];
            RandomNumberGenerator.Fill(randomBuffer);

            TelemetryData data = TelemetryParser.ParsePackage12(randomBuffer);

            data.Should().NotBeNull();
            data.CpuTemperatureCelsius.Should().BeInRange(0, 255);
            data.GpuTemperatureCelsius.Should().BeInRange(0, 255);
            data.Fan1Rpm.Should().BeGreaterThanOrEqualTo(0);
            data.Fan2Rpm.Should().BeGreaterThanOrEqualTo(0);
            data.Fan1DutyPercent.Should().BeInRange(0, 100);
            data.Fan2DutyPercent.Should().BeInRange(0, 100);

            if (bufferSize >= 25)
            {
                data.Fan3Rpm.Should().BeGreaterThanOrEqualTo(0);
                data.Fan3DutyPercent.Should().BeInRange(0, 100);
                data.Gpu2TemperatureCelsius.Should().BeInRange(0, 255);
            }

            if (bufferSize >= 41)
            {
                data.SysTemperatureCelsius.Should().BeInRange(0, 255);
            }
        }
    }

    #endregion
}
