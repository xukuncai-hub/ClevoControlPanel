using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace ClevoControlPanel.Core.Telemetry;

public record HardwareSpecs(
    string CpuName,
    string GpuName,
    string OsDescription);

public record MachineIdentity(string Manufacturer, string Model, string BaseBoard)
{
    public string DisplayText
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(Manufacturer)) parts.Add(Manufacturer);
            if (!string.IsNullOrWhiteSpace(Model) &&
                !Model.Equals(Manufacturer, StringComparison.OrdinalIgnoreCase))
                parts.Add(Model);
            return parts.Count > 0 ? string.Join(" ", parts) : "未知机型";
        }
    }
}

public record SystemMetrics(
    double RamUsagePercent,
    double RamUsedGb,
    double RamTotalGb,
    double DiskUsagePercent,
    double DiskUsedGb,
    double DiskTotalGb,
    string DiskName);

public static class SystemInfoSampler
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    public static SystemMetrics Sample()
    {
        double ramUsagePercent = 0;
        double ramUsedGb = 0;
        double ramTotalGb = 0;

        try
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                ramUsagePercent = mem.dwMemoryLoad;
                ramTotalGb = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                ramUsedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0);
            }
        }
        catch { }

        double diskUsagePercent = 0;
        double diskUsedGb = 0;
        double diskTotalGb = 0;
        string diskName = "C:";

        try
        {
            string systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(systemRoot);
            if (drive.IsReady)
            {
                diskName = drive.Name.TrimEnd('\\');
                diskTotalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                diskUsedGb = (drive.TotalSize - drive.AvailableFreeSpace) / (1024.0 * 1024.0 * 1024.0);
                diskUsagePercent = diskTotalGb > 0 ? (diskUsedGb / diskTotalGb) * 100.0 : 0;
            }
        }
        catch { }

        return new SystemMetrics(
            Math.Round(ramUsagePercent, 0),
            Math.Round(ramUsedGb, 1),
            Math.Round(ramTotalGb, 1),
            Math.Round(diskUsagePercent, 0),
            Math.Round(diskUsedGb, 1),
            Math.Round(diskTotalGb, 1),
            diskName);
    }

    private static MachineIdentity? _cachedIdentity;

    public static MachineIdentity GetMachineIdentity()
    {
        if (_cachedIdentity != null) return _cachedIdentity;

        string manufacturer = "未知";
        string model = "未知";
        string board = "";

        try
        {
            using var bios = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            if (bios != null)
            {
                manufacturer = TrimOr(bios.GetValue("SystemManufacturer") as string, manufacturer);
                model = TrimOr(bios.GetValue("SystemProductName") as string, model);

                string boardManufacturer = TrimOr(bios.GetValue("BaseBoardManufacturer") as string, "");
                string boardProduct = TrimOr(bios.GetValue("BaseBoardProduct") as string, "");
                board = string.Join(" ", new[] { boardManufacturer, boardProduct }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
            }
        }
        catch { }

        _cachedIdentity = new MachineIdentity(manufacturer, model, board);
        return _cachedIdentity;
    }

    private static string TrimOr(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static HardwareSpecs? _cachedSpecs;

    public static HardwareSpecs GetHardwareSpecs()
    {
        if (_cachedSpecs != null) return _cachedSpecs;

        string cpu = "未知处理器";
        string gpu = "未知显卡";
        string os = RuntimeInformation.OSDescription;

        try
        {
            using var cpuKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            string? cpuVal = cpuKey?.GetValue("ProcessorNameString") as string;
            if (!string.IsNullOrWhiteSpace(cpuVal))
                cpu = cpuVal.Trim();
        }
        catch { }

        try
        {
            using var classKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey != null)
            {
                var candidateGpus = new System.Collections.Generic.List<string>();
                foreach (string subName in classKey.GetSubKeyNames())
                {
                    if (subName.Length != 4 || !char.IsDigit(subName[0])) continue;
                    using var subKey = classKey.OpenSubKey(subName);
                    string? desc = subKey?.GetValue("DriverDesc") as string;
                    if (!string.IsNullOrWhiteSpace(desc))
                    {
                        string trimmed = desc.Trim();
                        if (!trimmed.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                            !trimmed.Contains("Remote", StringComparison.OrdinalIgnoreCase))
                        {
                            candidateGpus.Add(trimmed);
                        }
                    }
                }

                // Prefer discrete NVIDIA or AMD GPU
                string? dGpu = candidateGpus.Find(g => g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                                                      g.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                                                      g.Contains("Radeon", StringComparison.OrdinalIgnoreCase));
                gpu = dGpu ?? (candidateGpus.Count > 0 ? candidateGpus[0] : "集成显卡");
            }
        }
        catch { }

        _cachedSpecs = new HardwareSpecs(cpu, gpu, os);
        return _cachedSpecs;
    }
}
