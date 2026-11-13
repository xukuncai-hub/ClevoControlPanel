using System;
using ClevoControlPanel.Core.Models;

namespace ClevoControlPanel.Core.Services;

/// <summary>
/// Unified hardware abstraction consumed by the WPF view models.
/// Provides power profiles, fan regulation, sensor telemetry and keyboard RGB control.
/// </summary>
public interface IHwService : IDisposable
{
    /// <summary>True when commands reach the real Clevo EC; false for the simulated backend.</summary>
    bool IsLiveHardware { get; }

    /// <summary>Detected keyboard/fan capabilities used to gate platform-specific controls.</summary>
    HardwareCapabilities Capabilities { get; }

    /// <summary>True while the background telemetry sampler is active.</summary>
    bool IsTelemetryRunning { get; }

    /// <summary>Raised on a background thread after every successful sample.</summary>
    event EventHandler<TelemetryData>? TelemetryUpdated;

    TelemetryData GetTelemetry();
    void StartTelemetry(int intervalMs = 1000);
    void StopTelemetry();

    PowerMode GetPowerMode();
    void SetPowerMode(PowerMode mode);

    FanMode GetFanMode();
    void SetFanMode(FanMode mode);

    byte GetFanOffset();
    void SetFanOffset(byte offsetPercent);

    bool GetTurboFan();
    void SetTurboFan(bool enabled);

    void SetKeyboardRgbColor(RgbColor color, KeyboardZone zone = KeyboardZone.All);
    void SetKeyboardRgbEffect(RgbEffectMode mode, byte speed = 5);
    void SetKeyboardLiveColors(RgbColor left, RgbColor mid, RgbColor right);
    void SetKeyboardBrightness(byte level);
    void SetKeyboardPower(bool on);
    void SetKeyboardSleepTimeout(int seconds);
    KeyboardLightingState GetKeyboardState();
    RgbColor GetKeyboardZoneColor(KeyboardZone zone);
}
