using System;
using ClevoControlPanel.Core.Models;
using ClevoControlPanel.Core.Native;

namespace ClevoControlPanel.Core.Services;

/// <summary>
/// Stateful keyboard backlight controller. Translates high-level color, effect,
/// brightness and timeout requests into Command 103 / Command 121 packets and
/// mirrors the configuration into AppSettings NVRAM.
/// </summary>
public sealed class KeyboardRgbService : IKeyboardRgbService
{
    private readonly object _lock = new();
    private readonly IDchuNative _native;

    private RgbEffectMode _effect = RgbEffectMode.Static;
    private byte _speed = 5;
    private byte _brightness = 4;
    private byte _lastNonZeroBrightness = 4;
    private bool _isPowered = true;
    private int _sleepTimeoutSeconds = 30;
    private RgbColor _left = RgbColor.BlueColor;
    private RgbColor _mid = RgbColor.BlueColor;
    private RgbColor _right = RgbColor.BlueColor;

    public KeyboardRgbService(IDchuNative native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        LoadState();
    }

    public KeyboardLightingState GetState()
    {
        lock (_lock)
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

    public RgbColor GetZoneColor(KeyboardZone zone)
    {
        lock (_lock)
        {
            return zone switch
            {
                KeyboardZone.Left => _left,
                KeyboardZone.Mid => _mid,
                KeyboardZone.Right => _right,
                KeyboardZone.All => _left,
                _ => _left
            };
        }
    }

    public void SetColor(RgbColor color, KeyboardZone zone = KeyboardZone.All)
    {
        if (!Enum.IsDefined(typeof(KeyboardZone), zone))
            throw new ArgumentOutOfRangeException(nameof(zone), $"Undefined keyboard zone: {zone}");

        lock (_lock)
        {
            switch (zone)
            {
                case KeyboardZone.All:
                    SendZoneColor(KeyboardZone.Left, color);
                    SendZoneColor(KeyboardZone.Mid, color);
                    SendZoneColor(KeyboardZone.Right, color);
                    _left = _mid = _right = color;

                    WriteColor(NativeConstants.AppSettings.KbAllColor, color);
                    WriteColor(NativeConstants.AppSettings.KbLeftColor, color);
                    WriteColor(NativeConstants.AppSettings.KbMidColor, color);
                    WriteColor(NativeConstants.AppSettings.KbRightColor, color);
                    break;

                case KeyboardZone.Left:
                    SendZoneColor(KeyboardZone.Left, color);
                    _left = color;
                    WriteColor(NativeConstants.AppSettings.KbLeftColor, color);
                    break;

                case KeyboardZone.Mid:
                    SendZoneColor(KeyboardZone.Mid, color);
                    _mid = color;
                    WriteColor(NativeConstants.AppSettings.KbMidColor, color);
                    break;

                case KeyboardZone.Right:
                    SendZoneColor(KeyboardZone.Right, color);
                    _right = color;
                    WriteColor(NativeConstants.AppSettings.KbRightColor, color);
                    break;

                default:
                    SendZoneColor(zone, color);
                    break;
            }
        }
    }

    public void SetEffect(RgbEffectMode mode, byte speed = 5)
    {
        if (!Enum.IsDefined(typeof(RgbEffectMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode), $"Undefined RGB effect mode: {mode}");

        byte clampedSpeed = Math.Clamp(speed, (byte)1, (byte)10);

        lock (_lock)
        {
            // OEM SetMode sequence: zones/brightness are re-asserted before the effect packet,
            // and the mode is persisted before the packet so the EC applies the animation.
            if (_isPowered)
            {
                WriteSetting(NativeConstants.AppSettings.KbLeftStatus, 1);
                WriteSetting(NativeConstants.AppSettings.KbMidStatus, 1);
                WriteSetting(NativeConstants.AppSettings.KbRightStatus, 1);
                SendCommand(NativeConstants.CMD_KB_LED, RgbPacketBuilder.BuildBrightnessPacket(_brightness));
            }

            WriteSetting(NativeConstants.AppSettings.KbEffect, (byte)mode);
            WriteSetting(NativeConstants.AppSettings.KbSpeed, clampedSpeed);

            // Breath and static effects read the EC color registers, so refresh them first.
            if (mode is RgbEffectMode.Breath or RgbEffectMode.Static or RgbEffectMode.Custom or RgbEffectMode.All)
            {
                SendZoneColor(KeyboardZone.Left, _left);
                SendZoneColor(KeyboardZone.Mid, _mid);
                SendZoneColor(KeyboardZone.Right, _right);
            }

            byte[]? effectPacket = RgbPacketBuilder.BuildEffectPacket(mode);
            if (effectPacket != null)
                SendCommand(NativeConstants.CMD_KB_LED, effectPacket);

            _effect = mode;
            _speed = clampedSpeed;
        }
    }

    /// <summary>
    /// Writes a transient three-zone frame without touching NVRAM or the base colors.
    /// Used by the app-side animation loop when the EC does not animate itself.
    /// </summary>
    public void SendLiveColors(RgbColor left, RgbColor mid, RgbColor right)
    {
        lock (_lock)
        {
            if (!_isPowered || _brightness == 0)
                return;

            SendZoneColor(KeyboardZone.Left, left);
            SendZoneColor(KeyboardZone.Mid, mid);
            SendZoneColor(KeyboardZone.Right, right);
        }
    }

    public void SetBrightness(byte level)
    {
        byte clamped = Math.Clamp(level, (byte)0, (byte)4);

        lock (_lock)
        {
            SendCommand(NativeConstants.CMD_KB_LED, RgbPacketBuilder.BuildBrightnessPacket(clamped));
            WriteSetting(NativeConstants.AppSettings.KbBrightness, clamped);

            _brightness = clamped;
            if (clamped > 0)
                _lastNonZeroBrightness = clamped;
        }
    }

    public void SetPower(bool on)
    {
        lock (_lock)
        {
            if (on)
            {
                SendCommand(NativeConstants.CMD_KB_LED, RgbPacketBuilder.BuildMasterPowerPacket(true));

                byte restoreLevel = _brightness > 0
                    ? _brightness
                    : (_lastNonZeroBrightness > 0 ? _lastNonZeroBrightness : (byte)4);

                SendCommand(NativeConstants.CMD_KB_LED, RgbPacketBuilder.BuildBrightnessPacket(restoreLevel));
                WriteSetting(NativeConstants.AppSettings.KbBrightness, restoreLevel);
                _brightness = restoreLevel;
                _lastNonZeroBrightness = restoreLevel;
                _isPowered = true;

                // Re-send zone colors so EC firmware restores custom colors immediately
                SendZoneColor(KeyboardZone.Left, _left);
                SendZoneColor(KeyboardZone.Mid, _mid);
                SendZoneColor(KeyboardZone.Right, _right);
            }
            else
            {
                if (_brightness > 0)
                    _lastNonZeroBrightness = _brightness;

                // Force the PWM core to zero before gating the zones, otherwise LEDs ghost.
                SendCommand(NativeConstants.CMD_KB_LED, RgbPacketBuilder.BuildBrightnessPacket(0));
                SendCommand(NativeConstants.CMD_KB_LED, RgbPacketBuilder.BuildMasterPowerPacket(false));
                _isPowered = false;
            }

            WriteSetting(NativeConstants.AppSettings.KbPowerStatus, (byte)(_isPowered ? 1 : 0));
        }
    }

    public void SetSleepTimeout(int seconds)
    {
        if (seconds < 0)
            seconds = 0;
        if (seconds > ushort.MaxValue)
            seconds = ushort.MaxValue;
        if (seconds is > 0 and < NativeConstants.RgbLightingConstants.MIN_SLEEP_TIMEOUT_SECONDS)
            seconds = NativeConstants.RgbLightingConstants.MIN_SLEEP_TIMEOUT_SECONDS;

        lock (_lock)
        {
            SendCommand(NativeConstants.CMD_SYS_CONTROL, RgbPacketBuilder.BuildSleepTimerPacket(seconds));

            byte trigger = (byte)(seconds > 0 ? 1 : 0);
            byte hours = (byte)(seconds / 3600);
            byte minutes = (byte)((seconds % 3600) / 60);
            byte secs = (byte)(seconds % 60);

            WriteSetting(NativeConstants.AppSettings.KbSleepTimerTrigger, trigger);
            if (!_native.TryWriteSettings(NativeConstants.AppSettings.KbSleepTimerHour, new[] { hours, minutes, secs }))
                throw new HwCommunicationException("Failed to persist keyboard sleep timer.");

            _sleepTimeoutSeconds = seconds;
        }
    }

    private void LoadState()
    {
        _brightness = Math.Clamp(ReadByte(NativeConstants.AppSettings.KbBrightness, 4), (byte)0, (byte)4);
        _lastNonZeroBrightness = _brightness > 0 ? _brightness : (byte)4;
        _isPowered = ReadByte(NativeConstants.AppSettings.KbPowerStatus, 1) != 0;

        byte effect = ReadByte(NativeConstants.AppSettings.KbEffect, (byte)RgbEffectMode.Static);
        _effect = Enum.IsDefined(typeof(RgbEffectMode), effect) ? (RgbEffectMode)effect : RgbEffectMode.Static;

        _speed = Math.Clamp(ReadByte(NativeConstants.AppSettings.KbSpeed, 5), (byte)1, (byte)10);

        byte trigger = ReadByte(NativeConstants.AppSettings.KbSleepTimerTrigger, 0);
        if (trigger != 0 &&
            _native.TryReadSettings(NativeConstants.AppSettings.KbSleepTimerHour, 3, out byte[] timer))
        {
            _sleepTimeoutSeconds = timer.Length >= 3
                ? (timer[0] * 3600) + (timer[1] * 60) + timer[2]
                : 0;
        }
        else
        {
            _sleepTimeoutSeconds = 0;
        }

        _left = ReadColor(NativeConstants.AppSettings.KbLeftColor, RgbColor.BlueColor);
        _mid = ReadColor(NativeConstants.AppSettings.KbMidColor, RgbColor.BlueColor);
        _right = ReadColor(NativeConstants.AppSettings.KbRightColor, RgbColor.BlueColor);
    }

    private byte ReadByte(int key, byte fallback)
    {
        return _native.TryReadSettings(key, 1, out byte[] data) && data.Length >= 1
            ? data[0]
            : fallback;
    }

    private RgbColor ReadColor(int key, RgbColor fallback)
    {
        return _native.TryReadSettings(key, 3, out byte[] data) && data.Length >= 3
            ? new RgbColor(data[0], data[1], data[2])
            : fallback;
    }

    private void SendZoneColor(KeyboardZone zone, RgbColor color)
    {
        SendCommand(NativeConstants.CMD_KB_LED, RgbPacketBuilder.BuildColorPacket(zone, color, scaleBlue: true));
    }

    private void SendCommand(int command, byte[] payload)
    {
        if (!_native.TrySendCommand(command, payload))
            throw new HwCommunicationException($"Keyboard RGB command {command} was rejected by the EC.");
    }

    private void WriteColor(int key, RgbColor color)
    {
        if (!_native.TryWriteSettings(key, new[] { color.Red, color.Green, color.Blue }))
            throw new HwCommunicationException($"Failed to persist keyboard color at AppSetting {key}.");
    }

    private void WriteSetting(int key, byte value)
    {
        if (!_native.TryWriteSettings(key, new[] { value }))
            throw new HwCommunicationException($"Failed to persist AppSetting {key}.");
    }
}
