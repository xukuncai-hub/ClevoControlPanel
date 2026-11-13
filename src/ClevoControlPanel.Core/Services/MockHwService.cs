using System;
using ClevoControlPanel.Core.Models;
using ClevoControlPanel.Core.Native;
using ClevoControlPanel.Core.Telemetry;

namespace ClevoControlPanel.Core.Services;

/// <summary>
/// Deterministic in-memory hardware simulator used on non-Clevo machines and in
/// tests. Models thermal inertia, fan spool-up, D3cold and all control surfaces.
/// </summary>
public sealed class MockHwService : HwServiceBase
{
    private readonly object _stateLock = new();
    private readonly Random _random = new(20261111);

    // Virtual AppSettings: pages 0..4 (1280 bytes).
    private readonly byte[] _settings = new byte[1280];

    private PowerMode _powerMode = PowerMode.Entertainment;
    private FanMode _fanMode = FanMode.Auto;
    private byte _fanOffset;
    private bool _turboFan;
    private bool _noiselessFan;

    private RgbEffectMode _effect = RgbEffectMode.Static;
    private byte _speed = 5;
    private byte _brightness = 4;
    private byte _lastNonZeroBrightness = 4;
    private bool _isPowered = true;
    private int _sleepTimeoutSeconds = 30;
    private RgbColor _left = RgbColor.BlueColor;
    private RgbColor _mid = RgbColor.BlueColor;
    private RgbColor _right = RgbColor.BlueColor;

    private double _cpuTemp = 46;
    private double _gpuTemp = 42;
    private double _fan1Duty = 110;
    private double _fan2Duty = 100;
    private double _fan1Rpm = 2400;
    private double _fan2Rpm = 2200;
    private double _tick;

    public MockHwService()
    {
        ResetSettings();
    }

    public override bool IsLiveHardware => false;

    public override HardwareCapabilities Capabilities { get; } = new();

    /// <summary>Forces the discrete GPU into D3cold standby for UI testing.</summary>
    public bool SimulateD3Cold
    {
        get { lock (_stateLock) return _simulateD3Cold; }
        set { lock (_stateLock) _simulateD3Cold = value; }
    }

    private bool _simulateD3Cold;

    protected override TelemetryData ReadTelemetryCore()
    {
        lock (_stateLock)
        {
            _tick++;

            double baseDuty = _fanMode switch
            {
                FanMode.Maximum => 245,
                FanMode.Noiseless => 70,
                FanMode.Silent => 105,
                FanMode.Custom => 135,
                FanMode.MaxQ => 118,
                _ => 125
            };

            baseDuty += _fanOffset * 1.2;
            if (_turboFan) baseDuty = 255;
            baseDuty += _powerMode switch
            {
                PowerMode.Performance => 15,
                PowerMode.PowerSaving => -12,
                PowerMode.Quiet => -22,
                _ => 0
            };
            baseDuty = Math.Clamp(baseDuty, 55, 255);

            _fan1Duty += (baseDuty - _fan1Duty) * 0.22;
            double fan2TargetDuty = baseDuty * 0.94;
            _fan2Duty += (fan2TargetDuty - _fan2Duty) * 0.18;

            double cpuLoad = _powerMode switch
            {
                PowerMode.Performance => 36,
                PowerMode.Entertainment => 22,
                PowerMode.Quiet => 13,
                _ => 10
            };
            cpuLoad += 3.0 * Math.Sin(_tick * 0.12) + (_random.NextDouble() * 2.0);

            double cooling = _fan1Duty / 255.0 * 19.0;
            double targetCpu = Math.Clamp(33 + cpuLoad - cooling + (_turboFan ? -2 : 0), 30, 98);
            _cpuTemp += (targetCpu - _cpuTemp) * 0.18;

            bool gpuActive = !_simulateD3Cold && _powerMode != PowerMode.PowerSaving;
            double gpuDuty;
            double gpuTemp;
            double gpuRpm;

            if (gpuActive)
            {
                double gpuLoad = _powerMode switch
                {
                    PowerMode.Performance => 30,
                    PowerMode.Entertainment => 17,
                    PowerMode.Quiet => 9,
                    _ => 7
                };
                gpuLoad += 2.0 * Math.Sin(_tick * 0.09 + 1.3) + (_random.NextDouble() * 1.5);

                double gpuCooling = _fan2Duty / 255.0 * 15.0;
                double targetGpu = Math.Clamp(33 + gpuLoad - gpuCooling, 28, 92);
                _gpuTemp += (targetGpu - _gpuTemp) * 0.14;

                gpuDuty = Math.Max(_fan2Duty, 60);
                double targetGpuRpm = gpuDuty / 255.0 * 7400.0;
                _fan2Rpm += (targetGpuRpm - _fan2Rpm) * 0.2;
                gpuTemp = _gpuTemp;
                gpuRpm = _fan2Rpm;
            }
            else
            {
                gpuDuty = 0;
                gpuTemp = 0;
                gpuRpm = 0;
                _gpuTemp = 38;
                _fan2Rpm = 0;
            }

            double targetFan1Rpm = _fan1Duty / 255.0 * 7450.0;
            _fan1Rpm += (targetFan1Rpm - _fan1Rpm) * 0.2;

            byte fan1DutyRaw = (byte)Math.Clamp(Math.Round(_fan1Duty), 0, 255);
            byte fan2DutyRaw = (byte)Math.Clamp(Math.Round(gpuDuty), 0, 255);

            return new TelemetryData
            {
                CpuTemperatureCelsius = (int)Math.Round(_cpuTemp),
                GpuTemperatureCelsius = gpuActive ? (int)Math.Round(gpuTemp) : 0,
                IsGpuActive = gpuActive,
                Fan1Rpm = (int)Math.Round(_fan1Rpm),
                Fan2Rpm = (int)Math.Round(gpuRpm),
                Fan1DutyPercent = TelemetryParser.CalculateDutyPercent(fan1DutyRaw),
                Fan2DutyPercent = TelemetryParser.CalculateDutyPercent(fan2DutyRaw),
                Fan1DutyRaw = fan1DutyRaw,
                Fan2DutyRaw = fan2DutyRaw,
                RawGpuTemperatureCelsius = gpuActive ? (int)Math.Round(gpuTemp) : 20,
                Fan3Rpm = 0,
                Fan3DutyPercent = 0,
                Gpu2TemperatureCelsius = 0,
                SysTemperatureCelsius = 36 + _random.Next(0, 3),
                Timestamp = DateTime.UtcNow
            };
        }
    }

    #region Power Modes

    public override PowerMode GetPowerMode()
    {
        lock (_stateLock) return _powerMode;
    }

    public override void SetPowerMode(PowerMode mode)
    {
        if (!Enum.IsDefined(typeof(PowerMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode), $"Undefined power mode: {mode}");

        lock (_stateLock)
        {
            SaveFanProfile(_powerMode);
            _powerMode = mode;
            _settings[NativeConstants.AppSettings.PowerMode] = (byte)mode;

            if (mode == PowerMode.Quiet && _turboFan)
            {
                _turboFan = false;
                _settings[NativeConstants.AppSettings.TurboFan_Status] = 0;
            }

            int index = (int)mode;
            byte savedMode = _settings[NativeConstants.AppSettings.FanMode_Quiet + index];
            _fanMode = IsSupportedFanMode((FanMode)savedMode) ? (FanMode)savedMode : FanMode.Auto;
            _fanOffset = Math.Clamp(_settings[NativeConstants.AppSettings.FanOffset_Quiet + index], (byte)0, (byte)100);

            _settings[NativeConstants.AppSettings.FanMode] = (byte)_fanMode;
            _settings[NativeConstants.AppSettings.FanOffset] = _fanOffset;
            _noiselessFan = _fanMode == FanMode.Noiseless;
            _settings[NativeConstants.AppSettings.NoiselessFan_status] = (byte)(_noiselessFan ? 1 : 0);
        }
    }

    #endregion

    #region Fan Control

    public override FanMode GetFanMode()
    {
        lock (_stateLock) return _fanMode;
    }

    public override void SetFanMode(FanMode mode)
    {
        if (mode is not (FanMode.Auto or FanMode.Maximum or FanMode.Silent or FanMode.MaxQ or FanMode.Custom or FanMode.Noiseless))
            throw new ArgumentOutOfRangeException(nameof(mode), $"Undefined fan mode: {mode}");

        lock (_stateLock)
        {
            if (_turboFan && mode != FanMode.Maximum)
            {
                _turboFan = false;
                _settings[NativeConstants.AppSettings.TurboFan_Status] = 0;
            }

            _fanMode = mode;
            _noiselessFan = mode == FanMode.Noiseless;

            _settings[NativeConstants.AppSettings.FanMode] = (byte)mode;
            _settings[NativeConstants.AppSettings.NoiselessFan_status] = (byte)(_noiselessFan ? 1 : 0);
            _settings[NativeConstants.AppSettings.FanMode_Quiet + (int)_powerMode] = (byte)mode;
        }
    }

    public override byte GetFanOffset()
    {
        lock (_stateLock) return _fanOffset;
    }

    public override void SetFanOffset(byte offsetPercent)
    {
        lock (_stateLock)
        {
            _fanOffset = Math.Clamp(offsetPercent, (byte)0, (byte)100);
            _settings[NativeConstants.AppSettings.FanOffset] = _fanOffset;
            _settings[NativeConstants.AppSettings.FanOffset_Quiet + (int)_powerMode] = _fanOffset;
        }
    }

    public override bool GetTurboFan()
    {
        lock (_stateLock) return _turboFan;
    }

    public override void SetTurboFan(bool enabled)
    {
        lock (_stateLock)
        {
            _turboFan = enabled;
            _settings[NativeConstants.AppSettings.TurboFan_Status] = (byte)(enabled ? 1 : 0);

            if (enabled)
            {
                _fanMode = FanMode.Auto;
                _noiselessFan = false;
                _settings[NativeConstants.AppSettings.FanMode] = (byte)FanMode.Auto;
                _settings[NativeConstants.AppSettings.NoiselessFan_status] = 0;
            }
        }
    }

    private void SaveFanProfile(PowerMode mode)
    {
        _settings[NativeConstants.AppSettings.FanMode_Quiet + (int)mode] = (byte)_fanMode;
        _settings[NativeConstants.AppSettings.FanOffset_Quiet + (int)mode] = _fanOffset;
    }

    private static bool IsSupportedFanMode(FanMode mode) =>
        mode is FanMode.Auto or FanMode.Maximum or FanMode.Silent or FanMode.MaxQ or FanMode.Custom or FanMode.Noiseless;

    #endregion

    #region Keyboard RGB

    public override void SetKeyboardRgbColor(RgbColor color, KeyboardZone zone = KeyboardZone.All)
    {
        if (!Enum.IsDefined(typeof(KeyboardZone), zone))
            throw new ArgumentOutOfRangeException(nameof(zone), $"Undefined keyboard zone: {zone}");

        lock (_stateLock)
        {
            switch (zone)
            {
                case KeyboardZone.All:
                    SetAllColors(color);
                    WriteColor(NativeConstants.AppSettings.KbAllColor, color);
                    break;
                case KeyboardZone.Left:
                    _left = color;
                    WriteColor(NativeConstants.AppSettings.KbLeftColor, color);
                    break;
                case KeyboardZone.Mid:
                    _mid = color;
                    WriteColor(NativeConstants.AppSettings.KbMidColor, color);
                    break;
                case KeyboardZone.Right:
                    _right = color;
                    WriteColor(NativeConstants.AppSettings.KbRightColor, color);
                    break;
            }
        }
    }

    public override void SetKeyboardRgbEffect(RgbEffectMode mode, byte speed = 5)
    {
        if (!Enum.IsDefined(typeof(RgbEffectMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode), $"Undefined RGB effect mode: {mode}");

        lock (_stateLock)
        {
            _effect = mode;
            _speed = Math.Clamp(speed, (byte)1, (byte)10);
            _settings[NativeConstants.AppSettings.KbEffect] = (byte)mode;
            _settings[NativeConstants.AppSettings.KbSpeed] = _speed;
        }
    }

    public override void SetKeyboardLiveColors(RgbColor left, RgbColor mid, RgbColor right)
    {
        lock (_stateLock)
        {
            if (!_isPowered || _brightness == 0)
                return;
        }
    }

    public override void SetKeyboardBrightness(byte level)
    {
        lock (_stateLock)
        {
            _brightness = Math.Clamp(level, (byte)0, (byte)4);
            if (_brightness > 0)
                _lastNonZeroBrightness = _brightness;
            _settings[NativeConstants.AppSettings.KbBrightness] = _brightness;
        }
    }

    public override void SetKeyboardPower(bool on)
    {
        lock (_stateLock)
        {
            _isPowered = on;
            if (on && _brightness == 0)
            {
                _brightness = _lastNonZeroBrightness > 0 ? _lastNonZeroBrightness : (byte)4;
                _settings[NativeConstants.AppSettings.KbBrightness] = _brightness;
            }

            _settings[NativeConstants.AppSettings.KbPowerStatus] = (byte)(on ? 1 : 0);
        }
    }

    public override void SetKeyboardSleepTimeout(int seconds)
    {
        if (seconds < 0) seconds = 0;
        if (seconds > ushort.MaxValue) seconds = ushort.MaxValue;
        if (seconds is > 0 and < 5) seconds = 5;

        lock (_stateLock)
        {
            _sleepTimeoutSeconds = seconds;
            _settings[NativeConstants.AppSettings.KbSleepTimerTrigger] = (byte)(seconds > 0 ? 1 : 0);
            _settings[NativeConstants.AppSettings.KbSleepTimerHour] = (byte)(seconds / 3600);
            _settings[NativeConstants.AppSettings.KbSleepTimerMinute] = (byte)((seconds % 3600) / 60);
            _settings[NativeConstants.AppSettings.KbSleepTimerSecond] = (byte)(seconds % 60);
        }
    }

    public override KeyboardLightingState GetKeyboardState()
    {
        lock (_stateLock)
        {
            return new KeyboardLightingState
            {
                Brightness = _brightness,
                IsPowered = _isPowered,
                Mode = _effect,
                Speed = _speed,
                SleepTimeoutSeconds = _sleepTimeoutSeconds,
                LeftColor = _left,
                MiddleColor = _mid,
                RightColor = _right
            };
        }
    }

    public override RgbColor GetKeyboardZoneColor(KeyboardZone zone)
    {
        lock (_stateLock)
        {
            return zone switch
            {
                KeyboardZone.Mid => _mid,
                KeyboardZone.Right => _right,
                _ => _left
            };
        }
    }

    private void SetAllColors(RgbColor color)
    {
        _left = _mid = _right = color;
        WriteColor(NativeConstants.AppSettings.KbLeftColor, color);
        WriteColor(NativeConstants.AppSettings.KbMidColor, color);
        WriteColor(NativeConstants.AppSettings.KbRightColor, color);
    }

    private void WriteColor(int key, RgbColor color)
    {
        _settings[key] = color.Red;
        _settings[key + 1] = color.Green;
        _settings[key + 2] = color.Blue;
    }

    #endregion

    private void ResetSettings()
    {
        Array.Clear(_settings, 0, _settings.Length);

        _powerMode = PowerMode.Entertainment;
        _fanMode = FanMode.Auto;
        _fanOffset = 0;
        _turboFan = false;
        _noiselessFan = false;

        _settings[NativeConstants.AppSettings.PowerMode] = (byte)_powerMode;
        _settings[NativeConstants.AppSettings.FanMode] = (byte)_fanMode;
        _settings[NativeConstants.AppSettings.FanOffset] = 0;
        _settings[NativeConstants.AppSettings.TurboFan_Status] = 0;
        _settings[NativeConstants.AppSettings.NoiselessFan_status] = 0;

        _settings[NativeConstants.AppSettings.FanMode_Quiet] = (byte)FanMode.Silent;
        _settings[NativeConstants.AppSettings.FanMode_PowerSaving] = (byte)FanMode.Auto;
        _settings[NativeConstants.AppSettings.FanMode_Performance] = (byte)FanMode.Maximum;
        _settings[NativeConstants.AppSettings.FanMode_Entertainment] = (byte)FanMode.Auto;

        _settings[NativeConstants.AppSettings.FanOffset_Quiet] = 0;
        _settings[NativeConstants.AppSettings.FanOffset_PowerSaving] = 0;
        _settings[NativeConstants.AppSettings.FanOffset_Performance] = 50;
        _settings[NativeConstants.AppSettings.FanOffset_Entertainment] = 0;

        _settings[NativeConstants.AppSettings.KbEffect] = (byte)RgbEffectMode.Static;
        _settings[NativeConstants.AppSettings.KbSpeed] = 5;
        _settings[NativeConstants.AppSettings.KbBrightness] = 4;
        _settings[NativeConstants.AppSettings.KbSleepTimerTrigger] = 1;
        _settings[NativeConstants.AppSettings.KbSleepTimerSecond] = 30;
        _settings[NativeConstants.AppSettings.KbPowerStatus] = 1;

        _effect = RgbEffectMode.Static;
        _speed = 5;
        _brightness = 4;
        _lastNonZeroBrightness = 4;
        _isPowered = true;
        _sleepTimeoutSeconds = 30;
        _left = _mid = _right = RgbColor.BlueColor;
        WriteColor(NativeConstants.AppSettings.KbAllColor, RgbColor.BlueColor);
        WriteColor(NativeConstants.AppSettings.KbLeftColor, RgbColor.BlueColor);
        WriteColor(NativeConstants.AppSettings.KbMidColor, RgbColor.BlueColor);
        WriteColor(NativeConstants.AppSettings.KbRightColor, RgbColor.BlueColor);
    }
}
