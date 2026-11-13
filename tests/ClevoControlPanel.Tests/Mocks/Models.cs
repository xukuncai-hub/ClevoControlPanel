using System;

namespace ClevoControlPanel.Tests.Mocks
{
    public enum PowerMode : byte
    {
        Quiet = 0,
        PowerSaving = 1,
        Performance = 2,
        Entertainment = 3
    }

    public enum FanMode : byte
    {
        Auto = 0,
        Maximum = 1,
        Silent = 2,
        MaxQ = 5,
        Custom = 6,
        Noiseless = 8
    }

    public enum KeyboardZone : byte
    {
        All = 0,
        Left = 1,
        Middle = 2,
        Right = 3
    }

    public enum RgbEffectMode : byte
    {
        Static = 1,
        Breath = 2,
        Cycle = 3,
        Wave = 4,
        Flash = 7
    }

    public static class KeyboardSleepTimeouts
    {
        public const int Off = 0;
        public const int Seconds15 = 15;
        public const int Seconds30 = 30;
        public const int Seconds60 = 60;
        public const int Seconds300 = 300;

        public static readonly int[] ValidPresets = [Off, Seconds15, Seconds30, Seconds60, Seconds300];
    }

    public readonly record struct RgbColor(byte R, byte G, byte B)
    {
        public static readonly RgbColor Black = new(0, 0, 0);
        public static readonly RgbColor White = new(255, 255, 255);
        public static readonly RgbColor Red = new(255, 0, 0);
        public static readonly RgbColor Green = new(0, 255, 0);
        public static readonly RgbColor Blue = new(0, 0, 255);
        public static readonly RgbColor Yellow = new(255, 255, 0);
        public static readonly RgbColor Cyan = new(0, 255, 255);
        public static readonly RgbColor Magenta = new(255, 0, 255);
        public static readonly RgbColor Orange = new(255, 128, 0);

        public static RgbColor FromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Black;
            hex = hex.TrimStart('#');
            if (hex.Length != 6) return Black;
            return new RgbColor(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16));
        }

        public static RgbColor CreateClamped(int r, int g, int b) =>
            new((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));

        public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
    }

    public class HardwareTelemetry
    {
        public int CpuTemperature { get; set; }
        public int GpuTemperature { get; set; }
        public int CpuFanRpm { get; set; }
        public int Gpu1FanRpm { get; set; }
        public int GpuFanRpm
        {
            get => Gpu1FanRpm;
            set => Gpu1FanRpm = value;
        }
        public int Gpu2FanRpm { get; set; }
        public byte CpuFanDuty { get; set; }
        public byte Gpu1FanDuty { get; set; }
        public byte Gpu2FanDuty { get; set; }
        public int CpuFanDutyPercent { get; set; }
        public int GpuFanDutyPercent { get; set; }
        public int SystemTemperature { get; set; }
        public bool IsGpuActive { get; set; } = true;
        public bool IsGpuStandby { get; set; }
        public int RawCpuTach { get; set; }
        public int RawGpu1Tach { get; set; }
    }

    public class KeyboardLightingState
    {
        public byte Brightness { get; set; } = 4;
        public bool IsEnabled => Brightness > 0;
        public RgbEffectMode Mode { get; set; } = RgbEffectMode.Static;
        public byte Speed { get; set; } = 5;
        public int SleepTimeoutSeconds { get; set; } = 30;
        public RgbColor LeftColor { get; set; } = RgbColor.Blue;
        public RgbColor MiddleColor { get; set; } = RgbColor.Blue;
        public RgbColor RightColor { get; set; } = RgbColor.Blue;
        public bool IsSleeping { get; set; }
    }

    public record TelemetryFrame
    {
        public int CpuTemp { get; init; }
        public int GpuTemp { get; init; }
        public int CpuRawTach { get; init; }
        public int GpuRawTach { get; init; }
        public byte CpuDuty { get; init; }
        public byte GpuDuty { get; init; }
    }

    public static class TelemetryParser
    {
        public const double ClockConstant = 2156250.0;

        public static int CalculateRpm(int rawTach)
        {
            if (rawTach <= 0) return 0;
            return (int)Math.Round(ClockConstant / rawTach);
        }

        public static HardwareTelemetry ParsePackage12(byte[] buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer), "Buffer cannot be null.");
            if (buffer.Length < 256)
                throw new ArgumentException("Buffer must be at least 256 bytes.", nameof(buffer));

            int cpuRaw = (buffer[2] << 8) | buffer[3];
            int gpu1Raw = (buffer[4] << 8) | buffer[5];
            int gpu2Raw = (buffer[6] << 8) | buffer[7];

            byte cpuDuty = buffer[16];
            int cpuTemp = buffer[18];
            byte gpu1Duty = buffer[19];
            byte rawGpuTemp = buffer[21];
            byte gpu2Duty = buffer[22];
            int gpu2Temp = buffer[24];
            int sysTemp = buffer[40];

            bool isStandby = rawGpuTemp == 20;
            bool isActive = !isStandby && rawGpuTemp > 0;
            int gpuTemp = isStandby ? 0 : rawGpuTemp;

            return new HardwareTelemetry
            {
                CpuTemperature = cpuTemp,
                GpuTemperature = gpuTemp,
                CpuFanRpm = CalculateRpm(cpuRaw),
                Gpu1FanRpm = isStandby ? 0 : CalculateRpm(gpu1Raw),
                Gpu2FanRpm = CalculateRpm(gpu2Raw),
                CpuFanDuty = cpuDuty,
                Gpu1FanDuty = isStandby ? (byte)0 : gpu1Duty,
                Gpu2FanDuty = gpu2Duty,
                CpuFanDutyPercent = (int)Math.Round(cpuDuty * 100.0 / 255.0),
                GpuFanDutyPercent = isStandby ? 0 : (int)Math.Round(gpu1Duty * 100.0 / 255.0),
                SystemTemperature = sysTemp,
                IsGpuActive = isActive,
                IsGpuStandby = isStandby,
                RawCpuTach = cpuRaw,
                RawGpu1Tach = gpu1Raw
            };
        }
    }
}
