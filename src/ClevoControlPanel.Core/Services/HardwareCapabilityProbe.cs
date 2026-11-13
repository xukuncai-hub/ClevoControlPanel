using ClevoControlPanel.Core.Models;

namespace ClevoControlPanel.Core.Services;

/// <summary>
/// Reads the keyboard/fan capability hints published by the Clevo EC. These bits
/// vary between platforms, so unknown data keeps the pre-existing (permissive)
/// behavior and only definitive values disable a subsystem.
/// </summary>
public static class HardwareCapabilityProbe
{
    private const int KeyboardTypeUnknown = 0xFF;
    private const int WmiKeyboardType = 13;

    public static HardwareCapabilities Detect(IDchuNative native)
    {
        if (native == null)
            throw new System.ArgumentNullException(nameof(native));

        bool gotWmi96 = native.TryReadWmi(96, out int wmi96);
        bool gotWmi122 = native.TryReadWmi(122, out int wmi122);

        bool isFanless = gotWmi122 && ((wmi122 >> 15) & 1) != 0;
        bool supportsFanOffset = !(gotWmi96 && ((wmi96 >> 10) & 1) != 0);

        byte keyboardType = KeyboardTypeUnknown;
        if (native.TryReadWmi(70, out _))
        {
            var buffer = new byte[256];
            if (native.TryReadWmiBuffer(WmiKeyboardType, buffer))
                keyboardType = buffer[15];
        }

        bool hasBacklight = keyboardType != 0;
        bool supportsRgb = keyboardType switch
        {
            0 or 1 or 6 or 22 => false,
            _ => true
        };
        bool supportsPerKey = keyboardType is 3 or 19 or 35 or 51 or 243;

        return new HardwareCapabilities
        {
            KeyboardType = keyboardType,
            HasKeyboardBacklight = hasBacklight,
            SupportsRgbKeyboard = supportsRgb,
            SupportsPerKeyRgb = supportsPerKey,
            IsFanless = isFanless,
            SupportsFanControl = !isFanless,
            SupportsFanOffset = supportsFanOffset && !isFanless
        };
    }
}
