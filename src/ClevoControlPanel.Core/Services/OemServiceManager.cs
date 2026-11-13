using System;
using System.Diagnostics;

namespace ClevoControlPanel.Core.Services;

/// <summary>
/// Manages coordination with OEM background services (e.g. CCDCHUService).
/// Allows taking exclusive control of the EC without background policy contention.
/// </summary>
public static class OemServiceManager
{
    public const string OemServiceName = "CCDCHUService";

    public static bool IsOemServiceInstalled()
    {
        try
        {
            var psi = new ProcessStartInfo("sc.exe", $"query {OemServiceName}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);
            return !output.Contains("FAILED 1060"); // 1060 = service does not exist
        }
        catch
        {
            return false;
        }
    }

    public static bool IsOemServiceRunning()
    {
        try
        {
            var psi = new ProcessStartInfo("sc.exe", $"query {OemServiceName}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);
            return output.Contains("STATE") && output.Contains("RUNNING");
        }
        catch
        {
            return false;
        }
    }

    public static bool StopOemService(out string message)
    {
        message = string.Empty;

        // First try standard sc command
        if (RunScCommand($"stop {OemServiceName}", "挂起官方服务成功", out message))
        {
            RunScCommand($"config {OemServiceName} start= demand", "已设置为手动启动", out _);
            return true;
        }

        // If access denied (Win32 Error 5), request UAC elevation
        if (message.Contains("5") || message.Contains("拒绝访问") || message.Contains("Access is denied") || string.IsNullOrEmpty(message))
        {
            if (RunElevatedScCommand($"stop {OemServiceName} & sc.exe config {OemServiceName} start= demand", out string elevatedMsg))
            {
                for (int i = 0; i < 6; i++)
                {
                    if (!IsOemServiceRunning()) break;
                    System.Threading.Thread.Sleep(500);
                }

                if (!IsOemServiceRunning())
                {
                    message = "挂起官方服务成功（已通过 UAC 提权并设为手动启动）";
                    return true;
                }
            }
            message = elevatedMsg;
            return false;
        }

        if (string.IsNullOrEmpty(message))
            message = "挂起官方服务失败。";

        return false;
    }

    public static bool StartOemService(out string message)
    {
        message = string.Empty;

        // First try standard sc command
        RunScCommand($"config {OemServiceName} start= auto", "", out _);
        if (RunScCommand($"start {OemServiceName}", "恢复官方服务成功", out message))
        {
            return true;
        }

        // If access denied (Win32 Error 5), request UAC elevation
        if (message.Contains("5") || message.Contains("拒绝访问") || message.Contains("Access is denied") || string.IsNullOrEmpty(message))
        {
            if (RunElevatedScCommand($"config {OemServiceName} start= auto & sc.exe start {OemServiceName}", out string elevatedMsg))
            {
                for (int i = 0; i < 6; i++)
                {
                    if (IsOemServiceRunning()) break;
                    System.Threading.Thread.Sleep(500);
                }

                if (IsOemServiceRunning())
                {
                    message = "恢复官方服务成功（已通过 UAC 提权并恢复开机自启）";
                    return true;
                }
            }
            message = elevatedMsg;
            return false;
        }

        if (string.IsNullOrEmpty(message))
            message = "启动官方服务失败。";

        return false;
    }

    private static bool RunElevatedScCommand(string scArgs, out string message)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c sc.exe {scArgs}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                message = "无法启动权限提升进程。";
                return false;
            }
            proc.WaitForExit(6000);
            if (proc.ExitCode == 0)
            {
                message = "操作成功";
                return true;
            }
            message = $"权限提升执行失败 (错误码: {proc.ExitCode})";
            return false;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            message = "已取消管理员 UAC 授权。";
            return false;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    private static bool RunScCommand(string args, string successMessage, out string message)
    {
        try
        {
            var psi = new ProcessStartInfo("sc.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                message = "无法启动 sc.exe";
                return false;
            }
            string output = proc.StandardOutput.ReadToEnd();
            string error = proc.StandardError.ReadToEnd();
            proc.WaitForExit(4000);

            if (proc.ExitCode == 0 || output.Contains("SUCCESS") || output.Contains("STATE"))
            {
                message = successMessage;
                return true;
            }

            message = !string.IsNullOrWhiteSpace(error) ? error.Trim() : output.Trim();
            return false;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }
}
