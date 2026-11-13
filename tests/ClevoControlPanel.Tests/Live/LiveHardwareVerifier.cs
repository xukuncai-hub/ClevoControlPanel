using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using ClevoControlPanel.Core.Models;
using ClevoControlPanel.Core.Native;
using ClevoControlPanel.Core.Services;
using ClevoControlPanel.Core.Telemetry;

namespace ClevoControlPanel.Tests.Live;

/// <summary>
/// Non-destructive live hardware verifier. Safe mode re-applies the current state
/// so every command path is exercised without leaving the machine reconfigured.
/// Full mode additionally sweeps all profiles and restores the original snapshot.
/// </summary>
internal static class LiveHardwareVerifier
{
    private const string Reset = "\u001b[0m";
    private const string Green = "\u001b[32m";
    private const string Red = "\u001b[31m";
    private const string Yellow = "\u001b[33m";
    private const string Cyan = "\u001b[36m";
    private const string Bold = "\u001b[1m";

    private sealed record CheckResult(string Id, string Name, string Status, string Detail);

    public static int Run(bool full, string jsonPath)
    {
        Console.WriteLine($"{Bold}{Cyan}======================================================================{Reset}");
        Console.WriteLine($"{Bold}{Cyan}      ClevoControlPanel Live Hardware Verification (InsydeDCHU)       {Reset}");
        Console.WriteLine($"{Bold}{Cyan}======================================================================{Reset}");
        Console.WriteLine($"Mode: {(full ? $"{Yellow}Full sweep + restore{Reset}" : $"{Green}Safe re-apply (no net state change){Reset}")}");
        Console.WriteLine();

        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"{Red}[ERROR] Live verification requires Windows x64.{Reset}");
            return 2;
        }

        IDchuNative native = DchuNative.Instance;
        if (!native.Probe())
        {
            Console.WriteLine($"{Red}[ERROR] InsydeDCHU.dll 探测失败：未安装 OEM 驱动或非 Clevo 硬件。{Reset}");
            return 2;
        }

        LiveSnapshot? snapshot = LiveSnapshot.Capture(native);
        if (snapshot == null)
        {
            Console.WriteLine($"{Red}[ERROR] 无法读取初始硬件状态，已中止以避免破坏配置。{Reset}");
            return 2;
        }

        Console.WriteLine($"Detected state: PowerMode={snapshot.PowerMode}, FanMode={snapshot.FanMode}, Offset={snapshot.FanOffset}%, " +
                          $"Turbo={(snapshot.Turbo != 0 ? "on" : "off")}, KB brightness={snapshot.Brightness}, Sleep={DescribeSleep(snapshot)}");
        Console.WriteLine();

        var results = new List<CheckResult>();
        int passed = 0;
        int failed = 0;
        int warned = 0;

        void Pass(string id, string name, string detail)
        {
            passed++;
            results.Add(new CheckResult(id, name, "PASS", detail));
            Console.WriteLine($"[{id}] {name} ... {Green}[PASS]{Reset} {detail}");
        }

        void Fail(string id, string name, string detail)
        {
            failed++;
            results.Add(new CheckResult(id, name, "FAIL", detail));
            Console.WriteLine($"[{id}] {name} ... {Red}[FAIL]{Reset} {detail}");
        }

        void Warn(string id, string name, string detail)
        {
            warned++;
            results.Add(new CheckResult(id, name, "WARN", detail));
            Console.WriteLine($"[{id}] {name} ... {Yellow}[WARN]{Reset} {detail}");
        }

        void Check(string id, string name, bool required, Func<string> action)
        {
            try
            {
                string detail = action();
                Pass(id, name, detail);
            }
            catch (Exception ex)
            {
                if (required)
                    Fail(id, name, $"{ex.GetType().Name}: {ex.Message}");
                else
                    Warn(id, name, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        bool restoreOk = true;
        string restoreDetail = string.Empty;

        try
        {
            Check("L1", "传感器遥测 (WMI 12)", true, () =>
            {
                var samples = new List<TelemetryData>();
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 3; i++)
                {
                    byte[] buffer = new byte[NativeConstants.BUFFER_SIZE_256];
                    int status = native.ReadTelemetry(buffer);
                    if (status == 0)
                        throw new HwCommunicationException("GetDCHU_Data_Buffer(12) 返回 0");

                    var telemetry = TelemetryParser.ParsePackage12(buffer);
                    samples.Add(telemetry);
                    Thread.Sleep(120);
                }

                var last = samples[^1];
                if (last.CpuTemperatureCelsius is < 0 or > 120)
                    throw new InvalidOperationException($"CPU 温度异常: {last.CpuTemperatureCelsius}°C");
                if (last.Fan1Rpm is < 0 or > 12000)
                    throw new InvalidOperationException($"CPU 转速异常: {last.Fan1Rpm} RPM");

                string gpu = last.IsGpuActive
                    ? $"{last.GpuTemperatureCelsius}°C / {last.Fan2Rpm} RPM"
                    : "D3cold standby";
                return $"CPU {last.CpuTemperatureCelsius}°C / {last.Fan1Rpm} RPM, GPU {gpu}, {sw.ElapsedMilliseconds}ms";
            });

            Check("L2", "AppSettings 写入回环 (257)", true, () =>
            {
                if (!native.TryWriteSettings(NativeConstants.AppSettings.PowerMode, new[] { snapshot.PowerMode }))
                    throw new HwCommunicationException("WriteAppSettings(257) 返回失败");
                byte readback = MustRead(native, NativeConstants.AppSettings.PowerMode);
                if (readback != snapshot.PowerMode)
                    throw new InvalidOperationException($"回读 {readback} != 原值 {snapshot.PowerMode}");
                return $"persisted value {readback}";
            });

            Check("L3", "服务层状态读取", true, () =>
            {
                using var service = new ClevoHwService(native);
                var lighting = service.GetKeyboardState();
                if ((byte)service.GetPowerMode() != NormalizePower(snapshot.PowerMode))
                    throw new InvalidOperationException($"PowerMode {service.GetPowerMode()} 与寄存器不一致");
                if ((byte)service.GetFanMode() != snapshot.FanMode)
                    throw new InvalidOperationException($"FanMode {service.GetFanMode()} 与寄存器不一致");
                if (service.GetFanOffset() != Math.Min(snapshot.FanOffset, (byte)100))
                    throw new InvalidOperationException($"FanOffset {service.GetFanOffset()} 与寄存器不一致");
                if (service.GetTurboFan() != (snapshot.Turbo != 0))
                    throw new InvalidOperationException("Turbo 状态与寄存器不一致");

                return $"Power={service.GetPowerMode()}, Fan={service.GetFanMode()}, Offset={service.GetFanOffset()}%, " +
                       $"KB={lighting.Brightness}/4, effect={lighting.Mode}, sleep={lighting.SleepTimeoutSeconds}s";
            });

            Check("L4", "性能模式命令 (121:25)", true, () =>
            {
                uint payload = snapshot.PowerMode;
                if (NormalizePower(snapshot.PowerMode) == (byte)PowerMode.Performance && snapshot.Dtt != 0)
                    payload |= 0x80;

                Send121(native, NativeConstants.WmiSubCommands.SUB_POWER_MODE, payload);
                byte readback = MustRead(native, NativeConstants.AppSettings.PowerMode);
                if (readback != snapshot.PowerMode)
                    throw new InvalidOperationException($"模式回读 {readback} != 原值 {snapshot.PowerMode}");
                return $"mode {snapshot.PowerMode} re-applied (payload 0x{payload:X2})";
            });

            Check("L5", "风扇模式命令 (121:1)", true, () =>
            {
                Send121(native, NativeConstants.WmiSubCommands.SUB_FAN_MODE, snapshot.FanMode);
                byte readback = MustRead(native, NativeConstants.AppSettings.FanMode);
                if (readback != snapshot.FanMode)
                    throw new InvalidOperationException($"模式回读 {readback} != 原值 {snapshot.FanMode}");
                return $"fan mode {snapshot.FanMode} re-applied";
            });

            Check("L6", "风扇偏移命令 (121:14)", true, () =>
            {
                byte percent = Math.Min(snapshot.FanOffset, (byte)100);
                byte scaled = ScaleOffset(percent);
                Send121(native, NativeConstants.WmiSubCommands.SUB_FAN_OFFSET, scaled);
                byte readback = MustRead(native, NativeConstants.AppSettings.FanOffset);
                if (readback != snapshot.FanOffset)
                    throw new InvalidOperationException($"偏移回读 {readback} != 原值 {snapshot.FanOffset}");
                return $"{percent}% -> register {scaled}";
            });

            if (snapshot.Turbo != 0 || NormalizePower(snapshot.PowerMode) == (byte)PowerMode.Performance)
            {
                Check("L7", "Turbo 掩码命令 (121:25)", true, () =>
                {
                    byte mask = TurboMask(snapshot.Turbo != 0, snapshot.Dtt != 0);
                    Send121(native, NativeConstants.WmiSubCommands.SUB_POWER_MODE, mask);
                    return $"mask 0x{mask:X2} (turbo={(snapshot.Turbo != 0 ? "on" : "off")})";
                });
            }
            else
            {
                Warn("L7", "Turbo 掩码命令 (121:25)", "跳过：Turbo 仅与性能模式联用，避免改变当前功率模式。");
            }

            Check("L8", "键盘亮度命令 (103)", false, () =>
            {
                SendCommand(native, NativeConstants.CMD_KB_LED, RgbPacketBuilder.BuildBrightnessPacket(snapshot.Brightness));
                return $"level {snapshot.Brightness} re-applied";
            });

            Check("L9", "键盘三区颜色命令 (103)", false, () =>
            {
                SendCommand(native, NativeConstants.CMD_KB_LED, RgbPacketBuilder.BuildColorPacket(KeyboardZone.Left, snapshot.LeftColor));
                SendCommand(native, NativeConstants.CMD_KB_LED, RgbPacketBuilder.BuildColorPacket(KeyboardZone.Mid, snapshot.MidColor));
                SendCommand(native, NativeConstants.CMD_KB_LED, RgbPacketBuilder.BuildColorPacket(KeyboardZone.Right, snapshot.RightColor));
                return $"L={snapshot.LeftColor.ToHex()} M={snapshot.MidColor.ToHex()} R={snapshot.RightColor.ToHex()}";
            });

            Check("L10", "键盘休眠计时器 (121:24)", false, () =>
            {
                int seconds = SleepSeconds(snapshot);
                SendCommand(native, NativeConstants.CMD_SYS_CONTROL, RgbPacketBuilder.BuildSleepTimerPacket(seconds));
                return seconds == 0 ? "disabled re-applied" : $"{seconds}s re-applied";
            });

            if (full)
                RunFullSweep(native, snapshot, Check);
        }
        finally
        {
            try
            {
                LiveSnapshot.Restore(native, snapshot);
                restoreDetail = "所有电源/风扇/键盘寄存器与 NVRAM 已恢复";
            }
            catch (Exception ex)
            {
                restoreOk = false;
                restoreDetail = $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        if (restoreOk)
            Pass("R1", "硬件状态恢复", restoreDetail);
        else
            Fail("R1", "硬件状态恢复", restoreDetail);

        Console.WriteLine();
        Console.WriteLine($"{Bold}{Cyan}----------------------------------------------------------------------{Reset}");
        Console.WriteLine($"Passed: {Green}{passed}{Reset}   Warnings: {Yellow}{warned}{Reset}   Failed: {(failed > 0 ? Red : Green)}{failed}{Reset}");
        Console.WriteLine(restoreOk
            ? $"{Green}>>> LIVE HARDWARE VERIFICATION COMPLETE <<<{Reset}"
            : $"{Red}>>> RESTORE FAILED - CHECK DEVICE SETTINGS MANUALLY <<<{Reset}");
        Console.WriteLine($"{Bold}{Cyan}----------------------------------------------------------------------{Reset}");

        if (!string.IsNullOrWhiteSpace(jsonPath))
        {
            try
            {
                var report = new
                {
                    Timestamp = DateTime.UtcNow,
                    Mode = full ? "live-full" : "live-safe",
                    snapshot.PowerMode,
                    snapshot.FanMode,
                    snapshot.FanOffset,
                    Passed = passed,
                    Warnings = warned,
                    Failed = failed,
                    Restored = restoreOk,
                    Checks = results
                };
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"Results exported to: {jsonPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{Red}[WARN] JSON export failed: {ex.Message}{Reset}");
            }
        }

        return failed == 0 ? 0 : 1;
    }

    private static void RunFullSweep(IDchuNative native, LiveSnapshot snapshot, Action<string, string, bool, Func<string>> check)
    {
        check("F1", "性能模式全档位切换", true, () =>
        {
            using var service = new ClevoHwService(native);
            var visited = new List<string>();
            foreach (var mode in new[] { PowerMode.Quiet, PowerMode.PowerSaving, PowerMode.Performance, PowerMode.Entertainment })
            {
                service.SetPowerMode(mode);
                if (service.GetPowerMode() != mode)
                    throw new InvalidOperationException($"切换到 {mode} 后回读 {service.GetPowerMode()}");
                visited.Add($"{mode}({(byte)mode})");
                Thread.Sleep(120);
            }

            service.SetPowerMode((PowerMode)NormalizePower(snapshot.PowerMode));
            return string.Join(", ", visited);
        });

        check("F2", "风扇策略/偏移/Turbo 切换", true, () =>
        {
            using var service = new ClevoHwService(native);
            foreach (var mode in new[] { FanMode.Auto, FanMode.Silent, FanMode.Noiseless, FanMode.Maximum, FanMode.Auto })
            {
                service.SetFanMode(mode);
                if (service.GetFanMode() != mode)
                    throw new InvalidOperationException($"风扇模式 {mode} 回读失败");
                Thread.Sleep(80);
            }

            foreach (byte offset in new byte[] { 0, 50, 100 })
            {
                service.SetFanOffset(offset);
                if (service.GetFanOffset() != offset)
                    throw new InvalidOperationException($"偏移 {offset}% 回读失败");
                Thread.Sleep(80);
            }

            service.SetTurboFan(true);
            if (!service.GetTurboFan() || service.GetFanMode() != FanMode.Auto)
                throw new InvalidOperationException("Turbo 开启后状态不一致");
            Thread.Sleep(600);

            service.SetTurboFan(false);
            if (service.GetTurboFan())
                throw new InvalidOperationException("Turbo 关闭后状态不一致");

            service.SetFanOffset(Math.Min(snapshot.FanOffset, (byte)100));
            return "auto/silent/noiseless/max + offset 0/50/100 + turbo on/off";
        });

        check("F3", "键盘灯效全流程", false, () =>
        {
            using var service = new ClevoHwService(native);
            service.SetKeyboardPower(true);
            service.SetKeyboardBrightness(4);
            service.SetKeyboardRgbColor(RgbColor.RedColor, KeyboardZone.All);
            service.SetKeyboardRgbEffect(RgbEffectMode.Wave, 8);
            Thread.Sleep(150);

            var state = service.GetKeyboardState();
            if (state.Mode != RgbEffectMode.Wave || state.Speed != 8 || state.LeftColor != RgbColor.RedColor)
                throw new InvalidOperationException("键盘灯效状态回读失败");

            service.SetKeyboardBrightness(0);
            if (service.GetKeyboardState().Brightness != 0)
                throw new InvalidOperationException("亮度 0 回读失败");

            service.SetKeyboardBrightness(4);
            service.SetKeyboardRgbEffect(RgbEffectMode.Static);
            return "brightness 4/0/4, red global, wave speed 8, back to static";
        });

        check("F4", "风扇转速响应观测", false, () =>
        {
            using var service = new ClevoHwService(native);
            service.SetFanMode(FanMode.Maximum);
            Thread.Sleep(1500);
            var maximum = service.GetTelemetry();

            service.SetFanMode(FanMode.Noiseless);
            Thread.Sleep(1500);
            var noiseless = service.GetTelemetry();

            return $"maximum {maximum.Fan1Rpm}/{maximum.Fan2Rpm} RPM -> noiseless {noiseless.Fan1Rpm}/{noiseless.Fan2Rpm} RPM";
        });
    }

    private static byte MustRead(IDchuNative native, int key)
    {
        if (!native.TryReadSettings(key, 1, out byte[] data) || data.Length < 1)
            throw new HwCommunicationException($"读取 AppSetting {key} 失败");
        return data[0];
    }

    private static byte[] MustReadBytes(IDchuNative native, int key, int length)
    {
        if (!native.TryReadSettings(key, length, out byte[] data) || data.Length < length)
            throw new HwCommunicationException($"读取 AppSetting {key} ({length} 字节) 失败");
        return data;
    }

    private static void Send121(IDchuNative native, int subCommand, uint payload)
    {
        byte[] packet =
        [
            (byte)(payload & 0xFF),
            (byte)((payload >> 8) & 0xFF),
            (byte)((payload >> 16) & 0xFF),
            (byte)subCommand
        ];
        SendCommand(native, NativeConstants.CMD_SYS_CONTROL, packet);
    }

    private static void SendCommand(IDchuNative native, int command, byte[] payload)
    {
        if (!native.TrySendCommand(command, payload))
            throw new HwCommunicationException($"SetDCHU_Data({command}) 返回失败");
    }

    private static byte ScaleOffset(byte percent) =>
        (byte)Math.Clamp((int)Math.Round(255.0 * percent / 100.0), 0, 255);

    private static byte TurboMask(bool enabled, bool dtt) =>
        (byte)(2 | (enabled ? 1 << 6 : 0) | (dtt ? 1 << 7 : 0));

    private static byte NormalizePower(byte raw) => raw <= 3 ? raw : (byte)0;

    private static int SleepSeconds(LiveSnapshot snapshot)
    {
        if (snapshot.SleepTrigger == 0)
            return 0;
        return (snapshot.SleepHour * 3600) + (snapshot.SleepMinute * 60) + snapshot.SleepSecond;
    }

    private static string DescribeSleep(LiveSnapshot snapshot) =>
        snapshot.SleepTrigger == 0 ? "off" : $"{SleepSeconds(snapshot)}s";

    private sealed class LiveSnapshot
    {
        public byte PowerMode { get; init; }
        public byte FanMode { get; init; }
        public byte FanOffset { get; init; }
        public byte Turbo { get; init; }
        public byte Noiseless { get; init; }
        public byte Dtt { get; init; }

        public byte Effect { get; init; }
        public byte Speed { get; init; }
        public byte Brightness { get; init; }
        public byte KeyboardPower { get; init; }
        public byte SleepTrigger { get; init; }
        public byte SleepHour { get; init; }
        public byte SleepMinute { get; init; }
        public byte SleepSecond { get; init; }

        public RgbColor GlobalColor { get; init; }
        public RgbColor LeftColor { get; init; }
        public RgbColor MidColor { get; init; }
        public RgbColor RightColor { get; init; }

        public byte[] PerModeFanModes { get; init; } = new byte[4];
        public byte[] PerModeOffsets { get; init; } = new byte[4];

        public static LiveSnapshot? Capture(IDchuNative native)
        {
            try
            {
                byte[] global = MustReadBytes(native, NativeConstants.AppSettings.KbAllColor, 3);
                byte[] left = MustReadBytes(native, NativeConstants.AppSettings.KbLeftColor, 3);
                byte[] mid = MustReadBytes(native, NativeConstants.AppSettings.KbMidColor, 3);
                byte[] right = MustReadBytes(native, NativeConstants.AppSettings.KbRightColor, 3);

                return new LiveSnapshot
                {
                    PowerMode = MustRead(native, NativeConstants.AppSettings.PowerMode),
                    FanMode = MustRead(native, NativeConstants.AppSettings.FanMode),
                    FanOffset = MustRead(native, NativeConstants.AppSettings.FanOffset),
                    Turbo = MustRead(native, NativeConstants.AppSettings.TurboFan_Status),
                    Noiseless = MustRead(native, NativeConstants.AppSettings.NoiselessFan_status),
                    Dtt = MustRead(native, NativeConstants.AppSettings.DttStatus),
                    Effect = MustRead(native, NativeConstants.AppSettings.KbEffect),
                    Speed = MustRead(native, NativeConstants.AppSettings.KbSpeed),
                    Brightness = MustRead(native, NativeConstants.AppSettings.KbBrightness),
                    KeyboardPower = MustRead(native, NativeConstants.AppSettings.KbPowerStatus),
                    SleepTrigger = MustRead(native, NativeConstants.AppSettings.KbSleepTimerTrigger),
                    SleepHour = MustRead(native, NativeConstants.AppSettings.KbSleepTimerHour),
                    SleepMinute = MustRead(native, NativeConstants.AppSettings.KbSleepTimerMinute),
                    SleepSecond = MustRead(native, NativeConstants.AppSettings.KbSleepTimerSecond),
                    GlobalColor = new RgbColor(global[0], global[1], global[2]),
                    LeftColor = new RgbColor(left[0], left[1], left[2]),
                    MidColor = new RgbColor(mid[0], mid[1], mid[2]),
                    RightColor = new RgbColor(right[0], right[1], right[2]),
                    PerModeFanModes = MustReadBytes(native, NativeConstants.AppSettings.FanMode_Quiet, 4),
                    PerModeOffsets = MustReadBytes(native, NativeConstants.AppSettings.FanOffset_Quiet, 4)
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{Red}[ERROR] Snapshot failed: {ex.Message}{Reset}");
                return null;
            }
        }

        public static void Restore(IDchuNative native, LiveSnapshot s)
        {
            var errors = new List<string>();

            void Write(int key, params byte[] data)
            {
                if (!native.TryWriteSettings(key, data))
                    errors.Add($"write {key}");
            }

            void Send(int command, byte[] payload)
            {
                if (!native.TrySendCommand(command, payload))
                    errors.Add($"cmd {command}");
            }

            Write(NativeConstants.AppSettings.PowerMode, s.PowerMode);
            Write(NativeConstants.AppSettings.FanMode, s.FanMode);
            Write(NativeConstants.AppSettings.FanOffset, s.FanOffset);
            Write(NativeConstants.AppSettings.TurboFan_Status, s.Turbo);
            Write(NativeConstants.AppSettings.NoiselessFan_status, s.Noiseless);
            Write(NativeConstants.AppSettings.FanMode_Quiet, s.PerModeFanModes);
            Write(NativeConstants.AppSettings.FanOffset_Quiet, s.PerModeOffsets);

            Write(NativeConstants.AppSettings.KbEffect, s.Effect);
            Write(NativeConstants.AppSettings.KbSpeed, s.Speed);
            Write(NativeConstants.AppSettings.KbBrightness, s.Brightness);
            Write(NativeConstants.AppSettings.KbSleepTimerTrigger, s.SleepTrigger);
            Write(NativeConstants.AppSettings.KbSleepTimerHour, s.SleepHour, s.SleepMinute, s.SleepSecond);
            Write(NativeConstants.AppSettings.KbPowerStatus, s.KeyboardPower);
            Write(NativeConstants.AppSettings.KbAllColor, s.GlobalColor.Red, s.GlobalColor.Green, s.GlobalColor.Blue);
            Write(NativeConstants.AppSettings.KbLeftColor, s.LeftColor.Red, s.LeftColor.Green, s.LeftColor.Blue);
            Write(NativeConstants.AppSettings.KbMidColor, s.MidColor.Red, s.MidColor.Green, s.MidColor.Blue);
            Write(NativeConstants.AppSettings.KbRightColor, s.RightColor.Red, s.RightColor.Green, s.RightColor.Blue);

            uint powerPayload = (uint)NormalizePower(s.PowerMode);
            if ((byte)powerPayload == (byte)ClevoControlPanel.Core.Models.PowerMode.Performance && s.Dtt != 0)
                powerPayload |= 0x80;
            Send121(native, NativeConstants.WmiSubCommands.SUB_POWER_MODE, powerPayload);

            Send121(native, NativeConstants.WmiSubCommands.SUB_FAN_MODE, s.FanMode);
            Send121(native, NativeConstants.WmiSubCommands.SUB_FAN_OFFSET, ScaleOffset(Math.Min(s.FanOffset, (byte)100)));

            if (NormalizePower(s.PowerMode) == (byte)ClevoControlPanel.Core.Models.PowerMode.Performance)
                Send121(native, NativeConstants.WmiSubCommands.SUB_POWER_MODE, TurboMask(s.Turbo != 0, s.Dtt != 0));
            else if (s.Turbo != 0)
                Send121(native, NativeConstants.WmiSubCommands.SUB_POWER_MODE, TurboMask(true, s.Dtt != 0));

            if (s.KeyboardPower == 0)
            {
                Send(NativeConstants.CMD_KB_LED, RgbPacketBuilder.BuildBrightnessPacket(0));
                Send(NativeConstants.CMD_KB_LED, RgbPacketBuilder.BuildMasterPowerPacket(false));
            }
            else
            {
                Send(NativeConstants.CMD_KB_LED, RgbPacketBuilder.BuildMasterPowerPacket(true));
                Send(NativeConstants.CMD_KB_LED, RgbPacketBuilder.BuildBrightnessPacket(s.Brightness));
            }

            Send(NativeConstants.CMD_KB_LED, RgbPacketBuilder.BuildColorPacket(KeyboardZone.Left, s.LeftColor));
            Send(NativeConstants.CMD_KB_LED, RgbPacketBuilder.BuildColorPacket(KeyboardZone.Mid, s.MidColor));
            Send(NativeConstants.CMD_KB_LED, RgbPacketBuilder.BuildColorPacket(KeyboardZone.Right, s.RightColor));

            if (s.Effect is >= (byte)RgbEffectMode.Random and <= (byte)RgbEffectMode.All)
            {
                byte[]? effectPacket = RgbPacketBuilder.BuildEffectPacket((RgbEffectMode)s.Effect);
                if (effectPacket != null)
                    Send(NativeConstants.CMD_KB_LED, effectPacket);
            }

            Send(NativeConstants.CMD_SYS_CONTROL, RgbPacketBuilder.BuildSleepTimerPacket(SleepSeconds(s)));

            if (errors.Count > 0)
                throw new HwCommunicationException("恢复失败: " + string.Join(", ", errors));
        }
    }
}
