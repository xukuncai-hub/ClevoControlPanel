using System;
using ClevoControlPanel.Core.Models;
using ClevoControlPanel.Core.Native;
using ClevoControlPanel.Core.Telemetry;

namespace ClevoControlPanel.Core.Services;

/// <summary>
/// Live controller for genuine Clevo hardware. Executes Command 121/103 packets,
/// persists AppSettings, and restores per-power-mode fan profiles.
/// </summary>
public sealed class ClevoHwService : HwServiceBase
{
    private readonly object _hwLock = new();
    private readonly IDchuNative _native;
    private readonly KeyboardRgbService _rgb;
    private readonly byte[] _telemetryBuffer = new byte[NativeConstants.BUFFER_SIZE_256];

    private PowerMode _powerMode;
    private FanMode _fanMode;
    private byte _fanOffset;
    private bool _turboFan;

    public ClevoHwService(IDchuNative? native = null)
    {
        _native = native ?? DchuNative.Instance;
        Capabilities = HardwareCapabilityProbe.Detect(_native);
        _rgb = new KeyboardRgbService(_native);
        LoadState();
    }

    public override bool IsLiveHardware => true;

    public override HardwareCapabilities Capabilities { get; }

    protected override TelemetryData ReadTelemetryCore()
    {
        lock (_hwLock)
        {
            int status = _native.ReadTelemetry(_telemetryBuffer);
            if (status == 0)
                throw new HwCommunicationException("Telemetry read (WMI 12) failed.");

            return TelemetryParser.ParsePackage12(_telemetryBuffer);
        }
    }

    #region Power Modes

    public override PowerMode GetPowerMode()
    {
        lock (_hwLock) return _powerMode;
    }

    public override void SetPowerMode(PowerMode mode)
    {
        if (!Enum.IsDefined(typeof(PowerMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode), $"Undefined power mode: {mode}");

        lock (_hwLock)
        {
            SaveFanProfile(_powerMode);

            WriteSetting(NativeConstants.AppSettings.PowerMode, (byte)mode);

            uint payload = (uint)mode;
            if (mode == PowerMode.Performance && TryReadByte(NativeConstants.AppSettings.DttStatus, out byte dtt) && dtt != 0)
                payload |= 0x80; // Intel Dynamic Tuning Technology is active.

            SendSubCommand(NativeConstants.WmiSubCommands.SUB_POWER_MODE, payload);
            _powerMode = mode;

            if (mode == PowerMode.Quiet && _turboFan)
                DisarmTurboFan();

            RestoreFanProfile(mode);
        }
    }

    #endregion

    #region Fan Control

    public override FanMode GetFanMode()
    {
        lock (_hwLock) return _fanMode;
    }

    public override void SetFanMode(FanMode mode)
    {
        ValidateFanMode(mode);

        lock (_hwLock)
        {
            if (_turboFan && mode != FanMode.Maximum)
                DisarmTurboFan();

            _fanMode = mode;

            WriteSetting(NativeConstants.AppSettings.FanMode, (byte)mode);
            SendSubCommand(NativeConstants.WmiSubCommands.SUB_FAN_MODE, (uint)mode);
            WriteSetting(NativeConstants.AppSettings.NoiselessFan_status, (byte)(mode == FanMode.Noiseless ? 1 : 0));
            SaveCurrentFanModeToProfile();
        }
    }

    public override byte GetFanOffset()
    {
        lock (_hwLock) return _fanOffset;
    }

    public override void SetFanOffset(byte offsetPercent)
    {
        byte clamped = Math.Clamp(offsetPercent, (byte)0, (byte)100);

        lock (_hwLock)
        {
            _fanOffset = clamped;

            WriteSetting(NativeConstants.AppSettings.FanOffset, clamped);
            SendSubCommand(NativeConstants.WmiSubCommands.SUB_FAN_OFFSET, ScaleFanOffset(clamped));
            SaveCurrentFanOffsetToProfile();
        }
    }

    public override bool GetTurboFan()
    {
        lock (_hwLock) return _turboFan;
    }

    public override void SetTurboFan(bool enabled)
    {
        lock (_hwLock)
        {
            if (enabled)
            {
                _turboFan = true;
                WriteSetting(NativeConstants.AppSettings.TurboFan_Status, 1);

                if (_fanMode != FanMode.Auto)
                {
                    _fanMode = FanMode.Auto;
                    WriteSetting(NativeConstants.AppSettings.FanMode, (byte)FanMode.Auto);
                    SendSubCommand(NativeConstants.WmiSubCommands.SUB_FAN_MODE, (uint)FanMode.Auto);
                }

                WriteSetting(NativeConstants.AppSettings.NoiselessFan_status, 0);
            }
            else
            {
                _turboFan = false;
                WriteSetting(NativeConstants.AppSettings.TurboFan_Status, 0);
            }

            SendTurboMask(enabled);
        }
    }

    private void DisarmTurboFan()
    {
        _turboFan = false;
        WriteSetting(NativeConstants.AppSettings.TurboFan_Status, 0);
        SendTurboMask(false);
    }

    private void SendTurboMask(bool enabled)
    {
        uint mask = 2 | ((uint)(enabled ? 1 : 0) << 6);
        if (TryReadByte(NativeConstants.AppSettings.DttStatus, out byte dtt) && dtt != 0)
            mask |= 1u << 7;

        SendSubCommand(NativeConstants.WmiSubCommands.SUB_POWER_MODE, mask);
    }

    private void SaveFanProfile(PowerMode mode)
    {
        SaveCurrentFanModeToProfile(mode);
        SaveCurrentFanOffsetToProfile(mode);
    }

    private void SaveCurrentFanModeToProfile()
    {
        SaveCurrentFanModeToProfile(_powerMode);
    }

    private void SaveCurrentFanModeToProfile(PowerMode mode)
    {
        WriteSetting(NativeConstants.AppSettings.FanMode_Quiet + (int)mode, (byte)_fanMode);
    }

    private void SaveCurrentFanOffsetToProfile()
    {
        SaveCurrentFanOffsetToProfile(_powerMode);
    }

    private void SaveCurrentFanOffsetToProfile(PowerMode mode)
    {
        WriteSetting(NativeConstants.AppSettings.FanOffset_Quiet + (int)mode, _fanOffset);
    }

    private void RestoreFanProfile(PowerMode mode)
    {
        int index = (int)mode;

        byte savedModeByte = ReadByte(NativeConstants.AppSettings.FanMode_Quiet + index, (byte)FanMode.Auto);
        FanMode savedMode = IsSupportedFanMode((FanMode)savedModeByte) ? (FanMode)savedModeByte : FanMode.Auto;

        byte savedOffset = ReadByte(NativeConstants.AppSettings.FanOffset_Quiet + index, 0);
        savedOffset = Math.Clamp(savedOffset, (byte)0, (byte)100);

        if (_turboFan && savedMode != FanMode.Maximum)
            DisarmTurboFan();

        _fanMode = savedMode;
        _fanOffset = savedOffset;

        WriteSetting(NativeConstants.AppSettings.FanMode, (byte)savedMode);
        SendSubCommand(NativeConstants.WmiSubCommands.SUB_FAN_MODE, (uint)savedMode);

        WriteSetting(NativeConstants.AppSettings.FanOffset, savedOffset);
        SendSubCommand(NativeConstants.WmiSubCommands.SUB_FAN_OFFSET, ScaleFanOffset(savedOffset));

        WriteSetting(NativeConstants.AppSettings.NoiselessFan_status, (byte)(savedMode == FanMode.Noiseless ? 1 : 0));
    }

    private static byte ScaleFanOffset(byte percent) =>
        (byte)Math.Clamp((int)Math.Round(255.0 * percent / 100.0), 0, 255);

    private static void ValidateFanMode(FanMode mode)
    {
        if (mode is not (FanMode.Auto or FanMode.Maximum or FanMode.Silent or FanMode.MaxQ or FanMode.Custom or FanMode.Noiseless))
            throw new ArgumentOutOfRangeException(nameof(mode), $"Undefined fan mode: {mode}");
    }

    private static bool IsSupportedFanMode(FanMode mode) =>
        mode is FanMode.Auto or FanMode.Maximum or FanMode.Silent or FanMode.MaxQ or FanMode.Custom or FanMode.Noiseless;

    #endregion

    #region Keyboard RGB

    public override void SetKeyboardRgbColor(RgbColor color, KeyboardZone zone = KeyboardZone.All) =>
        _rgb.SetColor(color, zone);

    public override void SetKeyboardRgbEffect(RgbEffectMode mode, byte speed = 5) =>
        _rgb.SetEffect(mode, speed);

    public override void SetKeyboardLiveColors(RgbColor left, RgbColor mid, RgbColor right) =>
        _rgb.SendLiveColors(left, mid, right);

    public override void SetKeyboardBrightness(byte level) =>
        _rgb.SetBrightness(level);

    public override void SetKeyboardPower(bool on) =>
        _rgb.SetPower(on);

    public override void SetKeyboardSleepTimeout(int seconds) =>
        _rgb.SetSleepTimeout(seconds);

    public override KeyboardLightingState GetKeyboardState() =>
        _rgb.GetState();

    public override RgbColor GetKeyboardZoneColor(KeyboardZone zone) =>
        _rgb.GetZoneColor(zone);

    #endregion

    #region Native Helpers

    private void LoadState()
    {
        byte power = ReadByte(NativeConstants.AppSettings.PowerMode, (byte)PowerMode.Entertainment);
        _powerMode = Enum.IsDefined(typeof(PowerMode), power) ? (PowerMode)power : PowerMode.Entertainment;

        byte fan = ReadByte(NativeConstants.AppSettings.FanMode, (byte)FanMode.Auto);
        _fanMode = IsSupportedFanMode((FanMode)fan) ? (FanMode)fan : FanMode.Auto;

        _fanOffset = Math.Clamp(ReadByte(NativeConstants.AppSettings.FanOffset, 0), (byte)0, (byte)100);
        _turboFan = ReadByte(NativeConstants.AppSettings.TurboFan_Status, 0) != 0;
    }

    private byte ReadByte(int key, byte fallback)
    {
        return _native.TryReadSettings(key, 1, out byte[] data) && data.Length >= 1
            ? data[0]
            : fallback;
    }

    private bool TryReadByte(int key, out byte value)
    {
        if (_native.TryReadSettings(key, 1, out byte[] data) && data.Length >= 1)
        {
            value = data[0];
            return true;
        }

        value = 0;
        return false;
    }

    private void WriteSetting(int key, byte value)
    {
        if (!_native.TryWriteSettings(key, new[] { value }))
            throw new HwCommunicationException($"Failed to persist AppSetting {key}.");
    }

    private void SendSubCommand(int subCommand, uint payload)
    {
        byte[] buffer =
        [
            (byte)(payload & 0xFF),
            (byte)((payload >> 8) & 0xFF),
            (byte)((payload >> 16) & 0xFF),
            (byte)subCommand
        ];

        if (!_native.TrySendCommand(NativeConstants.CMD_SYS_CONTROL, buffer))
            throw new HwCommunicationException($"Command 121:{subCommand} was rejected by the EC.");
    }

    #endregion
}
