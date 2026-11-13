using System;
using System.Buffers.Binary;
using ClevoControlPanel.Core.Models;
using ClevoControlPanel.Core.Native;

namespace ClevoControlPanel.Core.Telemetry;

/// <summary>
/// High-performance static parser and calculation engine for Clevo EC Package 12 telemetry.
/// Operates on ReadOnlySpan&lt;byte&gt; to ensure zero heap allocations in high-frequency polling.
/// </summary>
public static class TelemetryParser
{
    /// <summary>
    /// Minimum buffer length required to safely extract CPU and GPU1 telemetry (offsets 0..21).
    /// </summary>
    public const int MinimumBufferSize = NativeConstants.MINIMUM_TELEMETRY_BUFFER_SIZE; // 24

    /// <summary>
    /// Standard buffer size returned by InsydeDCHU.dll GetDCHU_Data_Buffer(12, buffer).
    /// </summary>
    public const int StandardPackage12Size = NativeConstants.BUFFER_SIZE_256; // 256

    /// <summary>
    /// Clock frequency of the EC fan tachometer counter (2.15625 MHz).
    /// </summary>
    public const double TachometerClockFrequency = NativeConstants.RPM_FORMULA_CONSTANT; // 2156250.0

    /// <summary>
    /// Sentinel temperature byte returned by EC when discrete GPU is in D3cold / standby state.
    /// </summary>
    public const byte GpuStandbyTemperature = NativeConstants.GPU_STANDBY_TEMP_THRESHOLD; // 20

    /// <summary>
    /// Parses a raw Package 12 byte buffer into structured <see cref="TelemetryData"/>.
    /// </summary>
    /// <param name="buffer">Raw telemetry byte array.</param>
    /// <param name="includeRawBuffer">If true, stores a copy of the buffer in <see cref="TelemetryData.RawBuffer"/>.</param>
    /// <returns>Populated <see cref="TelemetryData"/> record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when buffer is null.</exception>
    /// <exception cref="ArgumentException">Thrown when buffer length is less than <see cref="MinimumBufferSize"/>.</exception>
    public static TelemetryData ParsePackage12(byte[] buffer, bool includeRawBuffer = false)
    {
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer), "Buffer cannot be null.");

        return ParsePackage12(buffer.AsSpan(), includeRawBuffer);
    }

    /// <summary>
    /// Parses a raw Package 12 byte span into structured <see cref="TelemetryData"/>.
    /// </summary>
    /// <param name="buffer">Raw telemetry byte span (minimum 24 bytes).</param>
    /// <param name="includeRawBuffer">If true, allocates a copy of the span in <see cref="TelemetryData.RawBuffer"/>.</param>
    /// <returns>Populated <see cref="TelemetryData"/> record.</returns>
    /// <exception cref="ArgumentException">Thrown when buffer length is less than <see cref="MinimumBufferSize"/>.</exception>
    public static TelemetryData ParsePackage12(ReadOnlySpan<byte> buffer, bool includeRawBuffer = false)
    {
        if (buffer.Length < MinimumBufferSize)
        {
            throw new ArgumentException(
                $"Buffer length {buffer.Length} is insufficient for Package 12 telemetry. Expected at least {MinimumBufferSize} bytes.",
                nameof(buffer));
        }

        // 1. Tachometer Big-Endian 16-bit words
        ushort fan1RawTach = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(2, 2));
        ushort fan2RawTach = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(4, 2));

        // 2. RPM Calculations
        int fan1Rpm = CalculateRpm(fan1RawTach);
        int fan2Rpm = CalculateRpm(fan2RawTach);

        // 3. Duty cycle raw bytes and percentages
        byte fan1DutyRaw = buffer[16];
        byte fan2DutyRaw = buffer[19];
        int fan1DutyPercent = CalculateDutyPercent(fan1DutyRaw);
        int fan2DutyPercent = CalculateDutyPercent(fan2DutyRaw);

        // 4. CPU Temperature
        int cpuTemp = buffer[18];

        // 5. GPU Temperature & D3cold detection
        int rawGpuTemp = buffer[21];
        bool isGpuActive;
        int gpuTemp;

        if (rawGpuTemp == GpuStandbyTemperature || rawGpuTemp == 0)
        {
            isGpuActive = false;
            gpuTemp = 0;
        }
        else
        {
            isGpuActive = true;
            gpuTemp = rawGpuTemp;
        }

        // 6. Optional Fan 3 & Secondary GPU (if at least 25 bytes provided)
        int fan3Rpm = 0;
        int fan3DutyPercent = 0;
        int gpu2Temp = 0;

        if (buffer.Length >= 25)
        {
            ushort fan3RawTach = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(6, 2));
            fan3Rpm = CalculateRpm(fan3RawTach);
            fan3DutyPercent = CalculateDutyPercent(buffer[22]);
            gpu2Temp = buffer[24];
        }

        // 7. Optional SYS / Ambient Temperature (if at least 41 bytes provided)
        int sysTemp = 0;
        if (buffer.Length >= 41)
        {
            sysTemp = buffer[40];
        }

        return new TelemetryData
        {
            CpuTemperatureCelsius = cpuTemp,
            GpuTemperatureCelsius = gpuTemp,
            IsGpuActive = isGpuActive,
            Fan1Rpm = fan1Rpm,
            Fan2Rpm = fan2Rpm,
            Fan1DutyPercent = fan1DutyPercent,
            Fan2DutyPercent = fan2DutyPercent,
            RawBuffer = includeRawBuffer ? buffer.ToArray() : null,

            Fan1DutyRaw = fan1DutyRaw,
            Fan2DutyRaw = fan2DutyRaw,
            Fan1RawTach = fan1RawTach,
            Fan2RawTach = fan2RawTach,
            RawGpuTemperatureCelsius = rawGpuTemp,
            Fan3Rpm = fan3Rpm,
            Fan3DutyPercent = fan3DutyPercent,
            Gpu2TemperatureCelsius = gpu2Temp,
            SysTemperatureCelsius = sysTemp
        };
    }

    /// <summary>
    /// Converts a raw 16-bit tachometer period count into fan Revolutions Per Minute (RPM).
    /// Formula: RPM = round(2156250.0 / rawTach).
    /// Divide-by-zero protection: returns 0 if rawTach is 0.
    /// </summary>
    /// <param name="rawTach">16-bit raw tachometer period count.</param>
    /// <returns>Fan speed in RPM.</returns>
    public static int CalculateRpm(ushort rawTach)
    {
        if (rawTach == 0)
            return 0;

        return (int)Math.Round(TachometerClockFrequency / rawTach);
    }

    /// <summary>
    /// Integer overload for tachometer period count to fan RPM conversion.
    /// </summary>
    public static int CalculateRpm(int rawTach)
    {
        if (rawTach <= 0)
            return 0;

        return (int)Math.Round(TachometerClockFrequency / rawTach);
    }

    /// <summary>
    /// Converts an 8-bit duty register value (0 to 255) into a percentage (0 to 100%).
    /// </summary>
    /// <param name="rawDuty">Raw duty byte from EC register.</param>
    /// <returns>Integer percentage clamped between 0 and 100.</returns>
    public static int CalculateDutyPercent(byte rawDuty)
    {
        int percent = (int)Math.Round((rawDuty / 255.0) * 100.0);
        return Math.Clamp(percent, 0, 100);
    }
}
