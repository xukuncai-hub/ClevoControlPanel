namespace ClevoControlPanel.Tests.Mocks
{
    public interface IHwService
    {
        bool IsLiveHardware { get; }
        HardwareTelemetry GetTelemetry();

        void SetPowerMode(PowerMode mode);
        PowerMode GetPowerMode();

        void SetFanMode(FanMode mode);
        FanMode GetFanMode();
        void SetFanOffset(byte offsetPercent);
        byte GetFanOffset();
        void SetTurboFan(bool enabled);
        bool GetTurboFan();

        // Keyboard RGB Operations
        void SetKeyboardRgbColor(RgbColor color, KeyboardZone zone = KeyboardZone.All);
        void SetKeyboardRgbEffect(RgbEffectMode mode, byte speed = 5);
        void SetKeyboardLiveColors(RgbColor left, RgbColor mid, RgbColor right);
        void SetKeyboardBrightness(byte level); // 0..4
        byte GetKeyboardBrightness();
        void SetKeyboardSleepTimeout(int seconds); // 0 = off, 15, 30, 60, 300
        int GetKeyboardSleepTimeout();

        // Query Readouts
        KeyboardLightingState GetKeyboardState();
        RgbColor GetKeyboardZoneColor(KeyboardZone zone);
        bool IsKeyboardBacklightSleeping();
        byte GetEffectiveKeyboardBrightness();
    }
}
