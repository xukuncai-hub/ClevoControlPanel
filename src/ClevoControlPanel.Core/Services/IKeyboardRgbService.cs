using ClevoControlPanel.Core.Models;

namespace ClevoControlPanel.Core.Services;

/// <summary>
/// Keyboard backlight controller contract: three zones, effects, brightness,
/// master power and the EC autonomous idle timer.
/// </summary>
public interface IKeyboardRgbService
{
    KeyboardLightingState GetState();
    RgbColor GetZoneColor(KeyboardZone zone);

    void SetColor(RgbColor color, KeyboardZone zone = KeyboardZone.All);
    void SetEffect(RgbEffectMode mode, byte speed = 5);
    void SendLiveColors(RgbColor left, RgbColor mid, RgbColor right);
    void SetBrightness(byte level);
    void SetPower(bool on);
    void SetSleepTimeout(int seconds);
}
