using System;
using ClevoControlPanel.Core.Native;

namespace ClevoControlPanel.Core.Models;

/// <summary>
/// Hardware lighting zone prefixes for Clevo laptop chassis.
/// Byte values correspond directly to Command 103 packet prefixes (Byte 3).
/// </summary>
public enum KeyboardZone : byte
{
    All = 0x00,
    Left = NativeConstants.RgbLightingConstants.ZONE_LEFT,         // 0xF0
    Mid = NativeConstants.RgbLightingConstants.ZONE_MID,           // 0xF1
    Right = NativeConstants.RgbLightingConstants.ZONE_RIGHT,       // 0xF2
    LightBar = NativeConstants.RgbLightingConstants.ZONE_LIGHTBAR, // 0xF3
    Logo = NativeConstants.RgbLightingConstants.ZONE_LOGO          // 0xF6
}

/// <summary>
/// Represents an 8-bit RGB color with hardware-specific blue luminance scaling for Clevo keyboards.
/// </summary>
public readonly record struct RgbColor(byte Red, byte Green, byte Blue)
{
    /// <summary>
    /// Gets the hardware-attenuated blue component scaled by 200/255 to balance LED luminosity.
    /// </summary>
    public byte ScaledBlue => (byte)Math.Round(NativeConstants.RgbLightingConstants.BLUE_SCALE_FACTOR * Blue);

    // Aliases
    public byte R => Red;
    public byte G => Green;
    public byte B => Blue;

    // Presets
    public static readonly RgbColor Black = new(0, 0, 0);
    public static readonly RgbColor White = new(255, 255, 255);
    public static readonly RgbColor RedColor = new(255, 0, 0);
    public static readonly RgbColor GreenColor = new(0, 255, 0);
    public static readonly RgbColor BlueColor = new(0, 0, 255);
    public static readonly RgbColor Cyan = new(0, 255, 255);
    public static readonly RgbColor Magenta = new(255, 0, 255);
    public static readonly RgbColor Yellow = new(255, 255, 0);
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

    public string ToHex() => $"#{Red:X2}{Green:X2}{Blue:X2}";
}

/// <summary>
/// Lighting animation modes supported by the Clevo keyboard controller.
/// </summary>
public enum LightingEffectMode : byte
{
    Random = 0,
    Custom = 1,
    Static = 1,
    Breath = 2,
    Cycle = 3,
    Wave = 4,
    Dance = 5,
    Tempo = 6,
    Flash = 7,
    All = 8
}

/// <summary>
/// Compatibility alias enum for LightingEffectMode.
/// </summary>
public enum RgbEffectMode : byte
{
    Random = 0,
    Custom = 1,
    Static = 1,
    Breath = 2,
    Cycle = 3,
    Wave = 4,
    Dance = 5,
    Tempo = 6,
    Flash = 7,
    All = 8
}

/// <summary>
/// Preset idle timeout durations for autonomous EC backlight shutoff.
/// </summary>
public static class KeyboardSleepTimeouts
{
    public const int Off = 0;
    public const int Seconds15 = 15;
    public const int Seconds30 = 30;
    public const int Seconds60 = 60;
    public const int Seconds300 = 300;

    public static readonly int[] ValidPresets = [Off, Seconds15, Seconds30, Seconds60, Seconds300];
}

/// <summary>
/// Static packet generator synthesizing low-level byte arrays for Command 103 and Command 121.
/// </summary>
public static class RgbPacketBuilder
{
    /// <summary>
    /// Builds a 4-byte little-endian color packet for Command 103: [Green, Red, Blue_scaled, Zone].
    /// </summary>
    public static byte[] BuildColorPacket(KeyboardZone zone, RgbColor color, bool scaleBlue = true)
    {
        byte blueComponent = scaleBlue ? color.ScaledBlue : color.Blue;
        return new byte[] { color.Green, color.Red, blueComponent, (byte)zone };
    }

    /// <summary>
    /// Builds a 4-byte brightness packet for Command 103: [Value, 0, 0, 0xF4].
    /// </summary>
    public static byte[] BuildBrightnessPacket(byte level)
    {
        byte val = level switch
        {
            1 => NativeConstants.RgbLightingConstants.BRIGHTNESS_STEP_1,
            2 => NativeConstants.RgbLightingConstants.BRIGHTNESS_STEP_2,
            3 => NativeConstants.RgbLightingConstants.BRIGHTNESS_STEP_3,
            4 => NativeConstants.RgbLightingConstants.BRIGHTNESS_STEP_4,
            _ => NativeConstants.RgbLightingConstants.BRIGHTNESS_STEP_0
        };

        uint packet = NativeConstants.RgbLightingConstants.BRIGHTNESS_PREFIX | val;
        return BitConverter.GetBytes(packet);
    }

    /// <summary>
    /// Builds a 4-byte master power toggle packet for Command 103 (Prefix 0xE0000000).
    /// </summary>
    public static byte[] BuildMasterPowerPacket(
        bool on,
        bool left = true,
        bool mid = true,
        bool right = true,
        bool lightBar = false,
        bool logo = false)
    {
        uint mask = NativeConstants.RgbLightingConstants.MASTER_POWER_SUBFLAG;

        if (on)
        {
            mask |= (1u << NativeConstants.RgbLightingConstants.MASTER_POWER_BIT_ENABLE);
            if (left) mask |= (1u << NativeConstants.RgbLightingConstants.MASTER_POWER_BIT_LEFT);
            if (mid) mask |= (1u << NativeConstants.RgbLightingConstants.MASTER_POWER_BIT_MID);
            if (right) mask |= (1u << NativeConstants.RgbLightingConstants.MASTER_POWER_BIT_RIGHT);
            if (lightBar) mask |= (1u << NativeConstants.RgbLightingConstants.MASTER_POWER_BIT_LIGHTBAR);
            if (logo) mask |= (1u << NativeConstants.RgbLightingConstants.MASTER_POWER_BIT_LOGO);
        }

        uint packet = NativeConstants.RgbLightingConstants.MASTER_POWER_PREFIX | mask;
        return BitConverter.GetBytes(packet);
    }

    /// <summary>
    /// Retrieves the 4-byte mode activation packet for animated effect modes.
    /// Returns null for Custom, Static, or All modes which require direct zone color packets instead.
    /// </summary>
    public static byte[]? BuildEffectPacket(LightingEffectMode mode)
    {
        uint? rawVal = mode switch
        {
            LightingEffectMode.Random => NativeConstants.RgbLightingConstants.EFFECT_RANDOM,
            LightingEffectMode.Breath => NativeConstants.RgbLightingConstants.EFFECT_BREATH,
            LightingEffectMode.Cycle  => NativeConstants.RgbLightingConstants.EFFECT_CYCLE,
            LightingEffectMode.Wave   => NativeConstants.RgbLightingConstants.EFFECT_WAVE,
            LightingEffectMode.Dance  => NativeConstants.RgbLightingConstants.EFFECT_DANCE,
            LightingEffectMode.Tempo  => NativeConstants.RgbLightingConstants.EFFECT_TEMPO,
            LightingEffectMode.Flash  => NativeConstants.RgbLightingConstants.EFFECT_FLASH,
            _ => null
        };

        return rawVal.HasValue ? BitConverter.GetBytes(rawVal.Value) : null;
    }

    /// <summary>
    /// Overload for RgbEffectMode.
    /// </summary>
    public static byte[]? BuildEffectPacket(RgbEffectMode mode) =>
        BuildEffectPacket((LightingEffectMode)mode);

    /// <summary>
    /// Builds a 4-byte Command 121 packet for the EC autonomous idle sleep timer.
    /// Disabling returns [0x00, 0x00, 0x00, 0x18].
    /// Enabling returns [0xFF, sec_lo, sec_hi, 0x18] clamped to &gt;= 5s.
    /// </summary>
    public static byte[] BuildSleepTimerPacket(int seconds)
    {
        const byte subCommand = (byte)NativeConstants.WmiSubCommands.SUB_SLEEP_TIMER;

        if (seconds <= 0)
        {
            return new byte[] { NativeConstants.RgbLightingConstants.SLEEP_TIMER_DISABLE_FLAG, 0x00, 0x00, subCommand };
        }

        int clampedSeconds = Math.Max(NativeConstants.RgbLightingConstants.MIN_SLEEP_TIMEOUT_SECONDS, seconds);
        return new byte[]
        {
            NativeConstants.RgbLightingConstants.SLEEP_TIMER_ENABLE_FLAG,
            (byte)(clampedSeconds & 0xFF),
            (byte)((clampedSeconds >> 8) & 0xFF),
            subCommand
        };
    }
}
