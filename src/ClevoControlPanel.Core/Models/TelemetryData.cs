using System;

namespace ClevoControlPanel.Core.Models;

/// <summary>
/// Structured hardware telemetry parsed from Clevo Embedded Controller Package 12.
/// Immutable record designed for thread-safe distribution across background polling and UI layers.
/// </summary>
public sealed record TelemetryData
{
    /// <summary>
    /// CPU core temperature in degrees Celsius (°C).
    /// </summary>
    public int CpuTemperatureCelsius { get; init; }

    /// <summary>
    /// Discrete GPU temperature in degrees Celsius (°C).
    /// Returns 0 when the GPU is inactive or powered down in D3cold.
    /// </summary>
    public int GpuTemperatureCelsius { get; init; }

    /// <summary>
    /// Indicates whether the discrete GPU is powered on and actively monitored.
    /// False if the EC reports 20°C (D3cold sentinel) or 0°C (uninitialized/absent).
    /// </summary>
    public bool IsGpuActive { get; init; }

    /// <summary>
    /// Indicates whether the discrete GPU is in D3cold / standby power-gated state.
    /// </summary>
    public bool IsGpuStandby => !IsGpuActive;

    /// <summary>
    /// Primary CPU cooling fan speed in Revolutions Per Minute (RPM).
    /// </summary>
    public int Fan1Rpm { get; init; }

    /// <summary>
    /// Primary GPU cooling fan speed in Revolutions Per Minute (RPM).
    /// </summary>
    public int Fan2Rpm { get; init; }

    /// <summary>
    /// Primary CPU cooling fan PWM duty cycle percentage (0 to 100%).
    /// </summary>
    public int Fan1DutyPercent { get; init; }

    /// <summary>
    /// Primary GPU cooling fan PWM duty cycle percentage (0 to 100%).
    /// </summary>
    public int Fan2DutyPercent { get; init; }

    /// <summary>
    /// Optional copy of the raw telemetry byte buffer (e.g. 256 bytes) for diagnostic logging.
    /// Null unless explicitly requested during parsing.
    /// </summary>
    public byte[]? RawBuffer { get; init; }

    #region Compatibility Aliases

    /// <summary>Compatibility alias for CpuTemperatureCelsius.</summary>
    public int CpuTemperature => CpuTemperatureCelsius;

    /// <summary>Compatibility alias for GpuTemperatureCelsius.</summary>
    public int GpuTemperature => GpuTemperatureCelsius;

    /// <summary>Compatibility alias for Fan1Rpm (CPU fan).</summary>
    public int CpuFanRpm => Fan1Rpm;

    /// <summary>Compatibility alias for Fan2Rpm (GPU fan).</summary>
    public int GpuFanRpm => Fan2Rpm;

    /// <summary>Compatibility alias for Fan1DutyPercent.</summary>
    public int CpuFanDutyPercent => Fan1DutyPercent;

    /// <summary>Compatibility alias for Fan2DutyPercent.</summary>
    public int GpuFanDutyPercent => Fan2DutyPercent;

    #endregion

    #region Diagnostic & Secondary Extended Properties

    /// <summary>
    /// Raw unscaled PWM duty register for Fan 1 (0 to 255).
    /// </summary>
    public byte Fan1DutyRaw { get; init; }

    /// <summary>
    /// Raw unscaled PWM duty register for Fan 2 (0 to 255).
    /// </summary>
    public byte Fan2DutyRaw { get; init; }

    /// <summary>
    /// Raw 16-bit big-endian tachometer period count for Fan 1.
    /// </summary>
    public ushort Fan1RawTach { get; init; }

    /// <summary>
    /// Raw 16-bit big-endian tachometer period count for Fan 2.
    /// </summary>
    public ushort Fan2RawTach { get; init; }

    /// <summary>
    /// Unfiltered temperature byte read directly from EC offset 21 (contains 20 when standby).
    /// </summary>
    public int RawGpuTemperatureCelsius { get; init; }

    /// <summary>
    /// Secondary GPU cooling fan speed in RPM (0 if laptop has single GPU fan).
    /// </summary>
    public int Fan3Rpm { get; init; }

    /// <summary>
    /// Secondary GPU cooling fan PWM duty percentage (0 to 100%).
    /// </summary>
    public int Fan3DutyPercent { get; init; }

    /// <summary>
    /// Secondary discrete GPU core temperature in °C (0 if nonexistent).
    /// </summary>
    public int Gpu2TemperatureCelsius { get; init; }

    /// <summary>
    /// Ambient motherboard / chassis system temperature in °C (0 if unsupported).
    /// </summary>
    public int SysTemperatureCelsius { get; init; }

    /// <summary>
    /// UTC timestamp when telemetry was parsed.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    #endregion
}
