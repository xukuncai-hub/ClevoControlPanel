using System;
using System.Collections.Generic;

namespace ClevoControlPanel.Tests.Mocks
{
    public enum HardwareCommandType
    {
        SetPowerMode,
        SetFanMode,
        SetFanOffset,
        SetTurboFan,
        SetKeyboardRgbColor,
        SetKeyboardRgbEffect,
        SetKeyboardBrightness,
        SetKeyboardSleepTimeout
    }

    public record HardwareCommandRecord(
        DateTime Timestamp,
        HardwareCommandType CommandType,
        IReadOnlyDictionary<string, object> Parameters
    );

    public record DispatchedWmiRecord(
        int Command,
        byte[] Payload
    );

    public class MockHwService : IHwService
    {
        private readonly object _lock = new();
        private readonly List<HardwareCommandRecord> _commandHistory = new();
        private readonly List<DispatchedWmiRecord> _recordedWmiPackets = new();
        private readonly Random _rand = new(42);

        // State Fields
        private PowerMode _powerMode = PowerMode.Entertainment;
        private FanMode _fanMode = FanMode.Auto;
        private byte _fanOffset = 0;
        private bool _turboFan = false;
        private bool _noiselessFan = false;
        private readonly KeyboardLightingState _keyboardState = new();
        private bool _simulateD3cold = false;
        private bool _failOnNextCommand = false;

        // Inactivity tracking
        private DateTime _lastUserActivity = DateTime.UtcNow;
        private DateTime _simulatedNow = DateTime.UtcNow;

        // Custom injection
        private byte[]? _injectedRawBuffer = null;
        private TelemetryFrame? _injectedFrame = null;

        // Flat AppSettings memory: 5 pages * 256 bytes = 1280 bytes
        public byte[] VirtualAppSettings { get; } = new byte[1280];

        public int LastDispatchedWmiCommand { get; private set; }
        public uint LastDispatchedWmiData { get; private set; }
        public byte[] LastDispatchedWmiBuffer { get; private set; } = new byte[4];

        public bool IsLiveHardware => false;

        public IReadOnlyList<HardwareCommandRecord> CommandHistory
        {
            get { lock (_lock) return _commandHistory.ToArray(); }
        }

        public IReadOnlyList<DispatchedWmiRecord> RecordedWmiPackets
        {
            get { lock (_lock) return _recordedWmiPackets.ToArray(); }
        }

        public PowerMode CurrentPowerMode => _powerMode;
        public FanMode CurrentFanMode => _fanMode;
        public byte CurrentFanOffset => _fanOffset;
        public bool CurrentTurboFan => _turboFan;
        public bool CurrentNoiselessFan => _noiselessFan;

        public MockHwService()
        {
            ResetToDefaults();
        }

        public void ResetToDefaults()
        {
            lock (_lock)
            {
                _commandHistory.Clear();
                _recordedWmiPackets.Clear();
                Array.Clear(VirtualAppSettings, 0, VirtualAppSettings.Length);

                _powerMode = PowerMode.Entertainment;
                _fanMode = FanMode.Auto;
                _fanOffset = 0;
                _turboFan = false;
                _noiselessFan = false;
                _simulateD3cold = false;
                _failOnNextCommand = false;
                _injectedRawBuffer = null;
                _injectedFrame = null;
                _simulatedNow = DateTime.UtcNow;
                _lastUserActivity = _simulatedNow;

                _keyboardState.Brightness = 4;
                _keyboardState.Mode = RgbEffectMode.Static;
                _keyboardState.Speed = 5;
                _keyboardState.SleepTimeoutSeconds = 30;
                _keyboardState.LeftColor = RgbColor.Blue;
                _keyboardState.MiddleColor = RgbColor.Blue;
                _keyboardState.RightColor = RgbColor.Blue;
                _keyboardState.IsSleeping = false;

                // Sync initial AppSettings
                VirtualAppSettings[257] = (byte)PowerMode.Entertainment; // Page 1 Offset 1
                VirtualAppSettings[1029] = (byte)FanMode.Auto;          // Page 4 Offset 5
                VirtualAppSettings[1031] = 0;                           // Page 4 Offset 7
                VirtualAppSettings[1032] = 0;                           // Page 4 Offset 8
                VirtualAppSettings[1033] = 0;                           // Page 4 Offset 9

                // Default Per-PowerMode fan memory (1094..1097 modes, 1098..1101 offsets)
                VirtualAppSettings[1094] = (byte)FanMode.Silent;  // Quiet -> Silent
                VirtualAppSettings[1095] = (byte)FanMode.Auto;    // PowerSaving -> Auto
                VirtualAppSettings[1096] = (byte)FanMode.Maximum; // Perf -> Maximum
                VirtualAppSettings[1097] = (byte)FanMode.Auto;    // Entert -> Auto

                VirtualAppSettings[1098] = 0;  // Quiet offset
                VirtualAppSettings[1099] = 0;  // PowerSaving offset
                VirtualAppSettings[1100] = 50; // Perf offset
                VirtualAppSettings[1101] = 0;  // Entert offset

                // RGB Page 2 AppSettings
                VirtualAppSettings[544] = (byte)RgbEffectMode.Static;
                VirtualAppSettings[546] = 5;
                VirtualAppSettings[547] = 4;
                VirtualAppSettings[548] = 1;
                VirtualAppSettings[592] = 1; // Master On
            }
        }

        public HardwareTelemetry GetTelemetry()
        {
            lock (_lock)
            {
                if (_injectedRawBuffer != null)
                {
                    return TelemetryParser.ParsePackage12(_injectedRawBuffer);
                }

                if (_injectedFrame != null)
                {
                    int frameCpuRpm = TelemetryParser.CalculateRpm(_injectedFrame.CpuRawTach);
                    bool isStandby = _injectedFrame.GpuTemp == 20;
                    int frameGpuRpm = isStandby ? 0 : TelemetryParser.CalculateRpm(_injectedFrame.GpuRawTach);
                    int cpuDutyPct = (int)Math.Round(_injectedFrame.CpuDuty * 100.0 / 255.0);
                    int gpuDutyPct = isStandby ? 0 : (int)Math.Round(_injectedFrame.GpuDuty * 100.0 / 255.0);

                    return new HardwareTelemetry
                    {
                        CpuTemperature = _injectedFrame.CpuTemp,
                        GpuTemperature = isStandby ? 0 : _injectedFrame.GpuTemp,
                        CpuFanRpm = frameCpuRpm,
                        Gpu1FanRpm = frameGpuRpm,
                        CpuFanDuty = _injectedFrame.CpuDuty,
                        Gpu1FanDuty = isStandby ? (byte)0 : _injectedFrame.GpuDuty,
                        CpuFanDutyPercent = cpuDutyPct,
                        GpuFanDutyPercent = gpuDutyPct,
                        IsGpuActive = !isStandby && _injectedFrame.GpuTemp > 0,
                        IsGpuStandby = isStandby,
                        RawCpuTach = _injectedFrame.CpuRawTach,
                        RawGpu1Tach = _injectedFrame.GpuRawTach,
                        SystemTemperature = 38
                    };
                }

                int cpuJitter = _rand.Next(-1, 2);
                int gpuJitter = _rand.Next(-1, 2);

                int cpuBaseTemp = _powerMode switch
                {
                    PowerMode.Quiet => 42,
                    PowerMode.PowerSaving => 40,
                    PowerMode.Performance => 75,
                    _ => 58
                };
                int cpuTemp = Math.Clamp(cpuBaseTemp + cpuJitter, 35, 105);

                int gpuTemp;
                int gpuRpm;
                int gpuDuty;

                if (_simulateD3cold || _powerMode == PowerMode.PowerSaving)
                {
                    gpuTemp = 0; // Displayed as 0 / Standby (raw is 20)
                    gpuRpm = 0;
                    gpuDuty = 0;
                }
                else
                {
                    int gpuBaseTemp = _powerMode switch
                    {
                        PowerMode.Quiet => 40,
                        PowerMode.Performance => 68,
                        _ => 52
                    };
                    gpuTemp = Math.Clamp(gpuBaseTemp + gpuJitter, 30, 95);
                    gpuDuty = _turboFan ? 255 : (_fanMode == FanMode.Maximum ? 230 : Math.Clamp(90 + _fanOffset * 2, 50, 255));
                    gpuRpm = (int)Math.Round(gpuDuty * 7461.0 / 255.0);
                }

                int cpuDuty = _turboFan ? 255 : (_fanMode == FanMode.Maximum ? 230 : (_fanMode == FanMode.Noiseless ? 75 : Math.Clamp(100 + _fanOffset * 2, 60, 255)));
                int cpuRpm = (int)Math.Round(cpuDuty * 7461.0 / 255.0);

                bool d3Active = !_simulateD3cold && _powerMode != PowerMode.PowerSaving;

                return new HardwareTelemetry
                {
                    CpuTemperature = cpuTemp,
                    GpuTemperature = gpuTemp,
                    CpuFanRpm = cpuRpm,
                    Gpu1FanRpm = gpuRpm,
                    CpuFanDuty = (byte)cpuDuty,
                    Gpu1FanDuty = (byte)gpuDuty,
                    CpuFanDutyPercent = (int)Math.Round(cpuDuty * 100.0 / 255.0),
                    GpuFanDutyPercent = (int)Math.Round(gpuDuty * 100.0 / 255.0),
                    SystemTemperature = 38,
                    IsGpuActive = d3Active,
                    IsGpuStandby = !d3Active
                };
            }
        }

        public void SetPowerMode(PowerMode mode)
        {
            lock (_lock)
            {
                CheckFail();
                if (!Enum.IsDefined(typeof(PowerMode), mode))
                    throw new ArgumentOutOfRangeException(nameof(mode), $"Undefined PowerMode: {mode}");

                _powerMode = mode;
                VirtualAppSettings[257] = (byte)mode;

                // WMI 121:25 packet: [ (byte)mode, 0x00, 0x00, 0x19 ]
                byte[] packet = [(byte)mode, 0x00, 0x00, 0x19];
                DispatchWmi(121, packet);

                // If switching to Quiet, disarm TurboFan
                if (mode == PowerMode.Quiet && _turboFan)
                {
                    _turboFan = false;
                    VirtualAppSettings[1032] = 0;
                }

                // Restore per-power-mode fan profile
                int modeIndex = (int)mode;
                byte savedFanModeByte = VirtualAppSettings[1094 + modeIndex];
                byte savedFanOffsetByte = VirtualAppSettings[1098 + modeIndex];

                if (Enum.IsDefined(typeof(FanMode), savedFanModeByte))
                {
                    _fanMode = (FanMode)savedFanModeByte;
                    VirtualAppSettings[1029] = savedFanModeByte;
                }

                _fanOffset = Math.Clamp(savedFanOffsetByte, (byte)0, (byte)100);
                VirtualAppSettings[1031] = _fanOffset;

                RecordCommand(HardwareCommandType.SetPowerMode, new Dictionary<string, object> { ["mode"] = mode });
            }
        }

        public PowerMode GetPowerMode()
        {
            lock (_lock) return _powerMode;
        }

        public void SetFanMode(FanMode mode)
        {
            lock (_lock)
            {
                CheckFail();
                // Valid FanModes per Clevo specs: 0=Auto, 1=Max, 2=Silent, 5=MaxQ, 6=Custom, 8=Noiseless
                if (!Enum.IsDefined(typeof(FanMode), mode) ||
                    (mode != FanMode.Auto && mode != FanMode.Maximum && mode != FanMode.Silent &&
                     mode != FanMode.MaxQ && mode != FanMode.Custom && mode != FanMode.Noiseless))
                {
                    throw new ArgumentOutOfRangeException(nameof(mode), $"Undefined FanMode: {mode}");
                }

                _fanMode = mode;
                VirtualAppSettings[1029] = (byte)mode;

                // Save to current power mode's fan profile
                int modeIndex = (int)_powerMode;
                VirtualAppSettings[1094 + modeIndex] = (byte)mode;

                if (mode == FanMode.Noiseless)
                {
                    _noiselessFan = true;
                    VirtualAppSettings[1033] = 1;
                    if (_turboFan)
                    {
                        _turboFan = false;
                        VirtualAppSettings[1032] = 0;
                    }
                }
                else
                {
                    _noiselessFan = false;
                    VirtualAppSettings[1033] = 0;
                }

                // WMI 121:1 packet: [ (byte)mode, 0x00, 0x00, 0x01 ]
                byte[] packet = [(byte)mode, 0x00, 0x00, 0x01];
                DispatchWmi(121, packet);

                RecordCommand(HardwareCommandType.SetFanMode, new Dictionary<string, object> { ["mode"] = mode });
            }
        }

        public FanMode GetFanMode()
        {
            lock (_lock) return _fanMode;
        }

        public void SetFanOffset(byte offsetPercent)
        {
            lock (_lock)
            {
                CheckFail();
                byte clampedPercent = Math.Clamp(offsetPercent, (byte)0, (byte)100);
                _fanOffset = clampedPercent;

                VirtualAppSettings[1031] = clampedPercent;

                // Save to current power mode's fan offset memory
                int modeIndex = (int)_powerMode;
                VirtualAppSettings[1098 + modeIndex] = clampedPercent;

                // Scale to 0..255 register byte: round(255 * offset / 100)
                byte scaledByte = (byte)Math.Round(255.0 * clampedPercent / 100.0);

                // WMI 121:14 packet: [ scaledByte, 0x00, 0x00, 0x0E ]
                byte[] packet = [scaledByte, 0x00, 0x00, 0x0E];
                DispatchWmi(121, packet);

                RecordCommand(HardwareCommandType.SetFanOffset, new Dictionary<string, object> { ["offsetPercent"] = clampedPercent, ["scaledByte"] = scaledByte });
            }
        }

        public byte GetFanOffset()
        {
            lock (_lock) return _fanOffset;
        }

        public void SetTurboFan(bool enabled)
        {
            lock (_lock)
            {
                CheckFail();
                _turboFan = enabled;
                VirtualAppSettings[1032] = (byte)(enabled ? 1 : 0);

                if (enabled)
                {
                    _fanMode = FanMode.Auto;
                    VirtualAppSettings[1029] = 0;
                    _noiselessFan = false;
                    VirtualAppSettings[1033] = 0;
                }

                // WMI 121:25 mask: 2 | (enable << 6)
                byte maskByte = (byte)(2 | ((enabled ? 1 : 0) << 6));
                byte[] packet = [maskByte, 0x00, 0x00, 0x19];
                DispatchWmi(121, packet);

                RecordCommand(HardwareCommandType.SetTurboFan, new Dictionary<string, object> { ["enabled"] = enabled });
            }
        }

        public bool GetTurboFan()
        {
            lock (_lock) return _turboFan;
        }

        public void SetKeyboardRgbColor(RgbColor color, KeyboardZone zone = KeyboardZone.All)
        {
            lock (_lock)
            {
                CheckFail();
                if (!Enum.IsDefined(typeof(KeyboardZone), zone))
                    throw new ArgumentOutOfRangeException(nameof(zone), $"Undefined KeyboardZone: {zone}");

                switch (zone)
                {
                    case KeyboardZone.All:
                        _keyboardState.LeftColor = color;
                        _keyboardState.MiddleColor = color;
                        _keyboardState.RightColor = color;
                        VirtualAppSettings[593] = color.R;
                        VirtualAppSettings[594] = color.G;
                        VirtualAppSettings[595] = color.B;
                        VirtualAppSettings[597] = color.R;
                        VirtualAppSettings[598] = color.G;
                        VirtualAppSettings[599] = color.B;
                        VirtualAppSettings[601] = color.R;
                        VirtualAppSettings[602] = color.G;
                        VirtualAppSettings[603] = color.B;
                        VirtualAppSettings[605] = color.R;
                        VirtualAppSettings[606] = color.G;
                        VirtualAppSettings[607] = color.B;

                        // Dispatch for all 3 zones
                        DispatchRgbZonePacket(0xF0, color);
                        DispatchRgbZonePacket(0xF1, color);
                        DispatchRgbZonePacket(0xF2, color);
                        break;

                    case KeyboardZone.Left:
                        _keyboardState.LeftColor = color;
                        VirtualAppSettings[597] = color.R;
                        VirtualAppSettings[598] = color.G;
                        VirtualAppSettings[599] = color.B;
                        DispatchRgbZonePacket(0xF0, color);
                        break;

                    case KeyboardZone.Middle:
                        _keyboardState.MiddleColor = color;
                        VirtualAppSettings[601] = color.R;
                        VirtualAppSettings[602] = color.G;
                        VirtualAppSettings[603] = color.B;
                        DispatchRgbZonePacket(0xF1, color);
                        break;

                    case KeyboardZone.Right:
                        _keyboardState.RightColor = color;
                        VirtualAppSettings[605] = color.R;
                        VirtualAppSettings[606] = color.G;
                        VirtualAppSettings[607] = color.B;
                        DispatchRgbZonePacket(0xF2, color);
                        break;
                }

                RecordCommand(HardwareCommandType.SetKeyboardRgbColor, new Dictionary<string, object>
                {
                    ["color"] = color,
                    ["zone"] = zone
                });
            }
        }

        public void SetKeyboardRgbEffect(RgbEffectMode mode, byte speed = 5)
        {
            lock (_lock)
            {
                CheckFail();
                if (!Enum.IsDefined(typeof(RgbEffectMode), mode) ||
                    (mode != RgbEffectMode.Static && mode != RgbEffectMode.Breath &&
                     mode != RgbEffectMode.Cycle && mode != RgbEffectMode.Wave && mode != RgbEffectMode.Flash))
                {
                    throw new ArgumentOutOfRangeException(nameof(mode), $"Undefined RgbEffectMode: {mode}");
                }

                _keyboardState.Mode = mode;
                byte clampedSpeed = Math.Clamp(speed, (byte)1, (byte)10);
                _keyboardState.Speed = clampedSpeed;

                VirtualAppSettings[544] = (byte)mode;
                VirtualAppSettings[546] = clampedSpeed;

                // Packets per mode
                byte[] packet = mode switch
                {
                    RgbEffectMode.Breath => BitConverter.GetBytes(0x1002A000u),
                    RgbEffectMode.Cycle => BitConverter.GetBytes(0x33010000u),
                    RgbEffectMode.Wave => BitConverter.GetBytes(0xB0000000u),
                    RgbEffectMode.Flash => BitConverter.GetBytes(0xA0000000u),
                    _ => [0xF0, _keyboardState.LeftColor.G, _keyboardState.LeftColor.R, _keyboardState.LeftColor.B]
                };

                DispatchWmi(103, packet);

                RecordCommand(HardwareCommandType.SetKeyboardRgbEffect, new Dictionary<string, object>
                {
                    ["mode"] = mode,
                    ["speed"] = clampedSpeed
                });
            }
        }

        public void SetKeyboardLiveColors(RgbColor left, RgbColor mid, RgbColor right)
        {
            // Transient animation frame; the test mock does not need to persist it.
        }

        public void SetKeyboardBrightness(byte level)
        {
            lock (_lock)
            {
                CheckFail();
                byte clampedLevel = Math.Clamp(level, (byte)0, (byte)4);
                _keyboardState.Brightness = clampedLevel;

                VirtualAppSettings[547] = clampedLevel;
                VirtualAppSettings[592] = (byte)(clampedLevel > 0 ? 1 : 0);

                uint stepValue = clampedLevel switch
                {
                    1 => 47,
                    2 => 95,
                    3 => 143,
                    4 => 191,
                    _ => 0
                };

                uint packetUint = 0xF4000000u | stepValue;
                byte[] packet = BitConverter.GetBytes(packetUint);
                DispatchWmi(103, packet);

                RecordCommand(HardwareCommandType.SetKeyboardBrightness, new Dictionary<string, object>
                {
                    ["level"] = clampedLevel,
                    ["stepValue"] = stepValue
                });
            }
        }

        public byte GetKeyboardBrightness()
        {
            lock (_lock) return _keyboardState.Brightness;
        }

        public void SetKeyboardSleepTimeout(int seconds)
        {
            lock (_lock)
            {
                CheckFail();
                if (seconds < 0) seconds = 0;
                // Max reasonable timeout 3600s
                if (seconds > 3600) seconds = 3600;

                _keyboardState.SleepTimeoutSeconds = seconds;

                VirtualAppSettings[548] = (byte)(seconds > 0 ? 1 : 0);
                VirtualAppSettings[549] = (byte)(seconds / 3600);
                VirtualAppSettings[550] = (byte)((seconds % 3600) / 60);
                VirtualAppSettings[551] = (byte)(seconds % 60);

                // Sleep timeout packet: Subcommand 24
                byte[] packet = seconds > 0
                    ? [0xFF, (byte)(seconds & 0xFF), (byte)((seconds >> 8) & 0xFF), 0x18]
                    : [0x00, 0x00, 0x00, 0x18];

                DispatchWmi(121, packet);

                RecordCommand(HardwareCommandType.SetKeyboardSleepTimeout, new Dictionary<string, object>
                {
                    ["seconds"] = seconds
                });
            }
        }

        public int GetKeyboardSleepTimeout()
        {
            lock (_lock) return _keyboardState.SleepTimeoutSeconds;
        }

        public KeyboardLightingState GetKeyboardState()
        {
            lock (_lock)
            {
                return new KeyboardLightingState
                {
                    Brightness = _keyboardState.Brightness,
                    Mode = _keyboardState.Mode,
                    Speed = _keyboardState.Speed,
                    SleepTimeoutSeconds = _keyboardState.SleepTimeoutSeconds,
                    LeftColor = _keyboardState.LeftColor,
                    MiddleColor = _keyboardState.MiddleColor,
                    RightColor = _keyboardState.RightColor,
                    IsSleeping = _keyboardState.IsSleeping
                };
            }
        }

        public RgbColor GetKeyboardZoneColor(KeyboardZone zone)
        {
            lock (_lock)
            {
                return zone switch
                {
                    KeyboardZone.Left => _keyboardState.LeftColor,
                    KeyboardZone.Middle => _keyboardState.MiddleColor,
                    KeyboardZone.Right => _keyboardState.RightColor,
                    KeyboardZone.All => _keyboardState.LeftColor,
                    _ => throw new ArgumentOutOfRangeException(nameof(zone))
                };
            }
        }

        public bool IsKeyboardBacklightSleeping()
        {
            lock (_lock) return _keyboardState.IsSleeping;
        }

        public byte GetEffectiveKeyboardBrightness()
        {
            lock (_lock)
            {
                if (_keyboardState.IsSleeping) return 0;
                return _keyboardState.Brightness;
            }
        }

        // Test Injection & Time Simulation API
        public void SetSimulateD3cold(bool enabled)
        {
            lock (_lock) _simulateD3cold = enabled;
        }

        public void SetSimulateFailure(bool fail)
        {
            lock (_lock) _failOnNextCommand = fail;
        }

        public void InjectTelemetry(TelemetryFrame frame)
        {
            lock (_lock) _injectedFrame = frame;
        }

        public void InjectRawTelemetryBuffer(byte[] rawBuffer)
        {
            lock (_lock) _injectedRawBuffer = rawBuffer;
        }

        public void AdvanceTime(TimeSpan elapsed)
        {
            lock (_lock)
            {
                _simulatedNow = _simulatedNow.Add(elapsed);
                SimulateInactivityCheck();
            }
        }

        public void TriggerUserActivity()
        {
            lock (_lock)
            {
                _lastUserActivity = _simulatedNow;
                _keyboardState.IsSleeping = false;
            }
        }

        public void SimulateInactivityCheck()
        {
            lock (_lock)
            {
                if (_keyboardState.SleepTimeoutSeconds > 0)
                {
                    var idleDuration = _simulatedNow - _lastUserActivity;
                    if (idleDuration >= TimeSpan.FromSeconds(_keyboardState.SleepTimeoutSeconds))
                    {
                        _keyboardState.IsSleeping = true;
                    }
                }
            }
        }

        private void DispatchRgbZonePacket(byte header, RgbColor color)
        {
            byte blueByte = color.B;
            // Firmware cyan bug workaround check
            if (color.R == 0 && color.G == 255 && color.B == 127)
            {
                blueByte = 0x46;
            }
            byte[] packet = [color.G, color.R, blueByte, header];
            DispatchWmi(103, packet);
        }

        private void DispatchWmi(int command, byte[] buffer)
        {
            LastDispatchedWmiCommand = command;
            LastDispatchedWmiBuffer = (byte[])buffer.Clone();
            if (buffer.Length >= 4)
                LastDispatchedWmiData = BitConverter.ToUInt32(buffer, 0);

            _recordedWmiPackets.Add(new DispatchedWmiRecord(command, (byte[])buffer.Clone()));
        }

        private void CheckFail()
        {
            if (_failOnNextCommand)
            {
                _failOnNextCommand = false;
                throw new InvalidOperationException("Simulated hardware communication failure.");
            }
        }

        private void RecordCommand(HardwareCommandType type, Dictionary<string, object> parameters)
        {
            _commandHistory.Add(new HardwareCommandRecord(DateTime.UtcNow, type, parameters));
        }
    }
}
