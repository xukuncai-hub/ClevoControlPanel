using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ClevoControlPanel.Core.Native;

/// <summary>
/// Strict x64 P/Invoke bridge for InsydeDCHU.dll (AcpiBridge.sys / Clevo EC WMI interface).
/// Includes dynamic library resolution and safe high-level command dispatchers.
/// </summary>
public static class InsydeDchuNative
{
    public const string DllName = "InsydeDCHU.dll";

    private static readonly object SyncLock = new();
    private static bool? _isSupportedCached;

    static InsydeDchuNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(InsydeDchuNative).Assembly, DllImportResolver);
    }

    /// <summary>
    /// Resolves InsydeDCHU.dll from application directories or OEM installation locations.
    /// </summary>
    private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (string.Equals(libraryName, DllName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(libraryName, "InsydeDCHU", StringComparison.OrdinalIgnoreCase))
        {
            // 1. Local application base directory
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localPath = Path.Combine(baseDir, DllName);
            if (File.Exists(localPath) && NativeLibrary.TryLoad(localPath, out IntPtr handle))
                return handle;

            // 2. Process executable directory (stable under single-file publishing)
            string? processDir = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrEmpty(processDir))
            {
                string processPath = Path.Combine(processDir, DllName);
                if (File.Exists(processPath) && NativeLibrary.TryLoad(processPath, out handle))
                    return handle;
            }

            // 3. OEM default install directory
            string oemPath1 = @"C:\Program Files (x86)\ControlCenter\InsydeDCHU.dll";
            if (File.Exists(oemPath1) && NativeLibrary.TryLoad(oemPath1, out handle))
                return handle;

            // 4. OEM subfolder directory
            string oemPath2 = @"C:\Program Files (x86)\ControlCenter\DCHU\InsydeDCHU.dll";
            if (File.Exists(oemPath2) && NativeLibrary.TryLoad(oemPath2, out handle))
                return handle;
        }

        return IntPtr.Zero;
    }

    #region P/Invoke Signatures

    /// <summary>
    /// Reads a 32-bit integer status or feature value from the ACPI/EC driver.
    /// </summary>
    /// <param name="command">WMI / DCHU Command ID.</param>
    /// <param name="data">Reference to integer receiving data.</param>
    /// <returns>1 on success, 0 on failure.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = false)]
    public static extern int GetDCHU_Data_Integer(int command, ref int data);

    /// <summary>
    /// Reads an information buffer from the ACPI/EC driver.
    /// Buffer MUST be allocated with at least 256 bytes for standard commands like CMD 12.
    /// </summary>
    /// <param name="command">WMI / DCHU Command ID.</param>
    /// <param name="buffer">Target byte array to receive unmanaged memory data.</param>
    /// <returns>1 on success, 0 on failure.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = false)]
    public static extern int GetDCHU_Data_Buffer(int command, [Out] byte[] buffer);

    /// <summary>
    /// Sends a command data buffer to the ACPI/EC driver.
    /// </summary>
    /// <param name="command">WMI / DCHU Command ID.</param>
    /// <param name="buffer">Byte buffer containing command payload.</param>
    /// <param name="length">Number of bytes to write.</param>
    /// <returns>1 on success, 0 on failure.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = false)]
    public static extern int SetDCHU_Data(int command, [In] byte[] buffer, int length);

    /// <summary>
    /// Sends a command buffer and receives a response buffer from the ACPI/EC driver.
    /// </summary>
    /// <param name="command">WMI / DCHU Command ID.</param>
    /// <param name="inBuffer">Input buffer.</param>
    /// <param name="inLength">Input buffer length.</param>
    /// <param name="outBuffer">Target byte array to receive response data.</param>
    /// <returns>1 on success, 0 on failure.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = false)]
    public static extern int SetDCHU_DataEx(int command, [In] byte[] inBuffer, int inLength, [Out] byte[] outBuffer);

    /// <summary>
    /// Reads application configuration settings from driver NVRAM storage.
    /// </summary>
    /// <param name="page">Page index (0..255).</param>
    /// <param name="offset">Offset within page (0..255).</param>
    /// <param name="length">Length in bytes to read.</param>
    /// <param name="buffer">Output buffer to receive settings.</param>
    /// <returns>1 on success, 0 on failure.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = false)]
    public static extern int ReadAppSettings(int page, int offset, int length, [Out] byte[] buffer);

    /// <summary>
    /// Writes application configuration settings to driver NVRAM storage.
    /// </summary>
    /// <param name="page">Page index (0..255).</param>
    /// <param name="offset">Offset within page (0..255).</param>
    /// <param name="length">Length in bytes to write.</param>
    /// <param name="buffer">Buffer containing data to persist.</param>
    /// <returns>1 on success, 0 on failure.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = false)]
    public static extern int WriteAppSettings(int page, int offset, int length, [In] byte[] buffer);

    #endregion

    #region Helper Methods

    /// <summary>
    /// Constructs a 4-byte little-endian SubCommand packet and sends it via SetDCHU_Data:
    /// buffer[0] = data &amp; 0xFF,
    /// buffer[1] = (data &gt;&gt; 8) &amp; 0xFF,
    /// buffer[2] = (data &gt;&gt; 16) &amp; 0xFF,
    /// buffer[3] = (byte)subCommand.
    /// </summary>
    /// <param name="command">WMI Command (e.g. 121 or 98).</param>
    /// <param name="subCommand">SubCommand ID placed in byte 3.</param>
    /// <param name="data">24-bit little-endian payload placed in bytes 0..2.</param>
    /// <returns>Result code from native SetDCHU_Data.</returns>
    public static int SetWmiSubCommand(int command, int subCommand, uint data)
    {
        byte[] buffer = new byte[4]
        {
            (byte)(data & 0xFF),
            (byte)((data >> 8) & 0xFF),
            (byte)((data >> 16) & 0xFF),
            (byte)subCommand
        };
        return SetDCHU_Data(command, buffer, 4);
    }

    /// <summary>
    /// Checks whether InsydeDCHU.dll is loadable and communicating with compatible Clevo hardware.
    /// Never throws exceptions; caches the result after initial probe.
    /// </summary>
    public static bool IsHardwareSupported()
    {
        lock (SyncLock)
        {
            if (_isSupportedCached.HasValue)
                return _isSupportedCached.Value;

            try
            {
                int testVal = 0;
                int ret = GetDCHU_Data_Integer(NativeConstants.CMD_GET_SUPPORT_FEATURE, ref testVal);
                _isSupportedCached = (ret != 0);
            }
            catch
            {
                _isSupportedCached = false;
            }

            return _isSupportedCached.Value;
        }
    }

    /// <summary>
    /// Resets cached hardware support probe result (e.g. for testing).
    /// </summary>
    public static void ResetHardwareSupportedCache()
    {
        lock (SyncLock)
        {
            _isSupportedCached = null;
        }
    }

    /// <summary>
    /// Reads a single byte from AppSettings NVRAM by flat key index.
    /// </summary>
    public static byte ReadAppSettingByte(int key)
    {
        int page = NativeConstants.AppSettings.GetPage(key);
        int offset = NativeConstants.AppSettings.GetOffset(key);
        byte[] buffer = new byte[1];
        ReadAppSettings(page, offset, 1, buffer);
        return buffer[0];
    }

    /// <summary>
    /// Writes a single byte to AppSettings NVRAM by flat key index.
    /// </summary>
    public static int WriteAppSettingByte(int key, byte value)
    {
        int page = NativeConstants.AppSettings.GetPage(key);
        int offset = NativeConstants.AppSettings.GetOffset(key);
        return WriteAppSettings(page, offset, 1, new byte[] { value });
    }

    #endregion
}
