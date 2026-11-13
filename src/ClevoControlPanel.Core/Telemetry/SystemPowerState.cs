using System;
using System.Runtime.InteropServices;

namespace ClevoControlPanel.Core.Telemetry;

/// <summary>
/// AC/battery state from GetSystemPowerStatus. Desktop machines and unknown
/// states are treated as AC-powered so auto mode never unnecessarily throttles.
/// </summary>
public static class SystemPowerState
{
    public static bool IsOnAcPower
    {
        get
        {
            try
            {
                return !GetSystemPowerStatus(out SystemPowerStatus status) ||
                       status.ACLineStatus != 0;
            }
            catch
            {
                return true;
            }
        }
    }

    public static bool HasBattery
    {
        get
        {
            try
            {
                return GetSystemPowerStatus(out SystemPowerStatus status) &&
                       (status.BatteryFlag & 128) == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public static int BatteryPercent
    {
        get
        {
            try
            {
                if (GetSystemPowerStatus(out SystemPowerStatus status) && status.BatteryLifePercent <= 100)
                    return status.BatteryLifePercent;
                return -1;
            }
            catch
            {
                return -1;
            }
        }
    }

    public static bool IsCharging
    {
        get
        {
            try
            {
                return GetSystemPowerStatus(out SystemPowerStatus status) &&
                       (status.BatteryFlag & 8) != 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public static string PowerStatusSummary
    {
        get
        {
            try
            {
                if (!GetSystemPowerStatus(out SystemPowerStatus status))
                    return "电源状态未知";

                if (status.ACLineStatus == 1)
                {
                    if ((status.BatteryFlag & 8) != 0)
                        return status.BatteryLifePercent <= 100
                            ? $"交流供电 (充电中 {status.BatteryLifePercent}%)"
                            : "交流供电 (充电中)";
                    if (status.BatteryLifePercent <= 100)
                        return $"交流供电 ({status.BatteryLifePercent}%)";
                    return "交流供电";
                }

                if (status.BatteryLifePercent <= 100)
                    return $"电池供电 ({status.BatteryLifePercent}%)";

                return "电池供电";
            }
            catch
            {
                return "交流供电";
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
