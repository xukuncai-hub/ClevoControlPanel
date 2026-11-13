using System;

namespace ClevoControlPanel.Core.Services;

/// <summary>
/// Abstraction over the InsydeDCHU.dll transport so services stay testable without hardware.
/// All methods are synchronous and must never crash the process.
/// </summary>
public interface IDchuNative
{
    /// <summary>Probes whether the driver and Clevo ACPI bridge are operational.</summary>
    bool Probe();

    /// <summary>Reads the 256-byte Package 12 telemetry buffer and returns the native status code.</summary>
    int ReadTelemetry(byte[] buffer);

    /// <summary>Reads a 32-bit capability/feature value through GetDCHU_Data_Integer.</summary>
    bool TryReadWmi(int command, out int value);

    /// <summary>Reads a 256-byte WMI package (e.g. package 13 for keyboard capabilities).</summary>
    bool TryReadWmiBuffer(int command, byte[] buffer);

    /// <summary>Reads AppSettings NVRAM by flat key. Returns false when the driver call fails.</summary>
    bool TryReadSettings(int key, int length, out byte[] data);

    /// <summary>Writes AppSettings NVRAM by flat key. Returns false when the driver call fails.</summary>
    bool TryWriteSettings(int key, byte[] data);

    /// <summary>Sends a command payload through SetDCHU_Data. Returns false when the driver call fails.</summary>
    bool TrySendCommand(int command, byte[] buffer);
}

/// <summary>
/// Raised when a live hardware command is rejected by the driver.
/// </summary>
public sealed class HwCommunicationException : Exception
{
    public HwCommunicationException(string message) : base(message)
    {
    }

    public HwCommunicationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
