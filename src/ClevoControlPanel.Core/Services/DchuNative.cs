using System;
using ClevoControlPanel.Core.Native;

namespace ClevoControlPanel.Core.Services;

/// <summary>
/// Production transport for InsydeDCHU.dll. Serializes every native call so the
/// ACPI bridge is never entered concurrently.
/// </summary>
public sealed class DchuNative : IDchuNative
{
    private static readonly object DriverLock = new();

    public static DchuNative Instance { get; } = new();

    public bool Probe()
    {
        try
        {
            return InsydeDchuNative.IsHardwareSupported();
        }
        catch
        {
            return false;
        }
    }

    public int ReadTelemetry(byte[] buffer)
    {
        ValidateBuffer(buffer, nameof(buffer), NativeConstants.BUFFER_SIZE_256);
        lock (DriverLock)
        {
            return InsydeDchuNative.GetDCHU_Data_Buffer(NativeConstants.CMD_FAN_TELEMETRY, buffer);
        }
    }

    public bool TryReadWmi(int command, out int value)
    {
        value = 0;
        try
        {
            lock (DriverLock)
            {
                return InsydeDchuNative.GetDCHU_Data_Integer(command, ref value) != 0;
            }
        }
        catch
        {
            return false;
        }
    }

    public bool TryReadWmiBuffer(int command, byte[] buffer)
    {
        ValidateBuffer(buffer, nameof(buffer), NativeConstants.BUFFER_SIZE_256);
        try
        {
            lock (DriverLock)
            {
                return InsydeDchuNative.GetDCHU_Data_Buffer(command, buffer) != 0;
            }
        }
        catch
        {
            return false;
        }
    }

    public bool TryReadSettings(int key, int length, out byte[] data)
    {
        ValidateKey(key, length);
        data = new byte[length];

        int page = NativeConstants.AppSettings.GetPage(key);
        int offset = NativeConstants.AppSettings.GetOffset(key);

        try
        {
            lock (DriverLock)
            {
                return InsydeDchuNative.ReadAppSettings(page, offset, length, data) != 0;
            }
        }
        catch
        {
            data = Array.Empty<byte>();
            return false;
        }
    }

    public bool TryWriteSettings(int key, byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        ValidateKey(key, data.Length);

        int page = NativeConstants.AppSettings.GetPage(key);
        int offset = NativeConstants.AppSettings.GetOffset(key);

        try
        {
            lock (DriverLock)
            {
                return InsydeDchuNative.WriteAppSettings(page, offset, data.Length, data) != 0;
            }
        }
        catch
        {
            return false;
        }
    }

    public bool TrySendCommand(int command, byte[] buffer)
    {
        ValidateBuffer(buffer, nameof(buffer), 1);
        try
        {
            lock (DriverLock)
            {
                return InsydeDchuNative.SetDCHU_Data(command, buffer, buffer.Length) != 0;
            }
        }
        catch
        {
            return false;
        }
    }

    private static void ValidateBuffer(byte[] buffer, string parameterName, int minimumLength)
    {
        if (buffer == null)
            throw new ArgumentNullException(parameterName);

        if (buffer.Length < minimumLength)
            throw new ArgumentException($"Buffer must be at least {minimumLength} bytes.", parameterName);
    }

    private static void ValidateKey(int key, int length)
    {
        if (key < 0)
            throw new ArgumentOutOfRangeException(nameof(key));
        if (length <= 0 || length > NativeConstants.BUFFER_SIZE_256)
            throw new ArgumentOutOfRangeException(nameof(length));

        int offset = NativeConstants.AppSettings.GetOffset(key);
        if (offset + length > NativeConstants.BUFFER_SIZE_256)
            throw new ArgumentOutOfRangeException(nameof(length), "AppSettings reads may not cross a 256-byte page.");
    }
}
