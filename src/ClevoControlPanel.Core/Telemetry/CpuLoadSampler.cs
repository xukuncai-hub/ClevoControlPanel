using System;
using System.Runtime.InteropServices;

namespace ClevoControlPanel.Core.Telemetry;

/// <summary>
/// Dependency-free system CPU load sampler based on GetSystemTimes deltas.
/// Returns 0 until two samples have been taken.
/// </summary>
public sealed class CpuLoadSampler
{
    private readonly object _lock = new();
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private bool _hasBaseline;

    public double Sample()
    {
        if (!GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime))
            return 0;

        ulong idle = ToUInt64(idleTime);
        ulong kernel = ToUInt64(kernelTime);
        ulong user = ToUInt64(userTime);

        lock (_lock)
        {
            if (!_hasBaseline)
            {
                _previousIdle = idle;
                _previousKernel = kernel;
                _previousUser = user;
                _hasBaseline = true;
                return 0;
            }

            ulong idleDelta = idle - _previousIdle;
            ulong kernelDelta = kernel - _previousKernel;
            ulong userDelta = user - _previousUser;

            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;

            ulong total = kernelDelta + userDelta;
            if (total == 0)
                return 0;

            ulong busy = total > idleDelta ? total - idleDelta : 0;
            return Math.Clamp(busy * 100.0 / total, 0, 100);
        }
    }

    private static ulong ToUInt64(FileTime time) =>
        ((ulong)time.dwHighDateTime << 32) | time.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);
}
