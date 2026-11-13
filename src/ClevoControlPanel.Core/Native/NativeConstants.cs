using System;

namespace ClevoControlPanel.Core.Native;

/// <summary>
/// Authoritative constants for Clevo InsydeDCHU P/Invoke, WMI ACPI commands, and AppSettings NVRAM layout.
/// </summary>
public static class NativeConstants
{
    // Flat Command Constants
    public const int CMD_FAN_TELEMETRY = 12;
    public const int CMD_HW_CAPABILITIES = 13;
    public const int CMD_FAN_DUTY_TARGET = 22;
    public const int CMD_SET_WHITE_LED_KB = 39;
    public const int CMD_GET_WHITE_LED_KB = 61;
    public const int CMD_GET_SUPPORT_FEATURE = 70;
    public const int CMD_ENERGY_STAR = 79;
    public const int CMD_FAN_CONTROL_ALT = 98;
    public const int CMD_KB_LED = 103;
    public const int CMD_SYS_CONTROL = 121;
    public const int CMD_BIOS_SPECIAL_FEATURE = 122;

    // Flat Command 121 SubCommands
    public const int SUB_FAN_MODE = 1;
    public const int SUB_FAN_OFFSET = 14;      // 0x0E
    public const int SUB_SLEEP_TIMER = 24;     // 0x18
    public const int SUB_POWER_MODE = 25;      // 0x19
    public const int SUB_FAN_SCHEDULE = 40;    // 0x28
    public const int SUB_ANTI_DUST = 41;       // 0x29

    // Flat Command 98 SubCommands
    public const int SUB_FAN_MODE_ALT = 1;
    public const int SUB_FAN_OFFSET_ALT = 2;
    public const int SUB_TURBO_FAN_ALT = 3;

    // Flat AppSettings Keys
    public const int REG_POWER_MODE = 257;            // Page 1, Offset 1
    public const int REG_DTT_STATUS = 288;            // Page 1, Offset 32
    public const int REG_KB_TYPE = 516;               // Page 2, Offset 4
    public const int REG_OVERRIDE_BOOT_EFFECT = 519;  // Page 2, Offset 7
    public const int REG_LED_KB_APP_LAUNCH = 520;     // Page 2, Offset 8
    public const int REG_KB_MODE = 544;               // Page 2, Offset 32
    public const int REG_KB_STATIC_STATUS = 545;      // Page 2, Offset 33
    public const int REG_KB_SPEED = 546;              // Page 2, Offset 34
    public const int REG_KB_BRIGHTNESS = 547;         // Page 2, Offset 35
    public const int REG_KB_SLEEP_TRIGGER = 548;      // Page 2, Offset 36
    public const int REG_KB_SLEEP_HOUR = 549;         // Page 2, Offset 37
    public const int REG_KB_SLEEP_MINUTE = 550;       // Page 2, Offset 38
    public const int REG_KB_SLEEP_SECOND = 551;       // Page 2, Offset 39
    public const int REG_KB_POWER_STATUS = 592;       // Page 2, Offset 80
    public const int REG_KB_ALL_COLOR = 593;          // Page 2, Offset 81
    public const int REG_KB_LEFT_COLOR = 597;         // Page 2, Offset 85
    public const int REG_KB_MID_COLOR = 601;          // Page 2, Offset 89
    public const int REG_KB_RIGHT_COLOR = 605;        // Page 2, Offset 93

    public const int REG_FAN_MODE = 1029;             // Page 4, Offset 5
    public const int REG_FAN_OFFSET = 1031;           // Page 4, Offset 7
    public const int REG_TURBO_FAN = 1032;            // Page 4, Offset 8
    public const int REG_NOISELESS_FAN = 1033;        // Page 4, Offset 9

    public const int REG_FAN_MODE_QUIET = 1094;       // Page 4, Offset 70
    public const int REG_FAN_MODE_POWERSAVING = 1095; // Page 4, Offset 71
    public const int REG_FAN_MODE_PERFORMANCE = 1096; // Page 4, Offset 72
    public const int REG_FAN_MODE_ENTERTAINMENT = 1097;// Page 4, Offset 73

    public const int REG_FAN_OFFSET_QUIET = 1098;     // Page 4, Offset 74
    public const int REG_FAN_OFFSET_POWERSAVING = 1099;// Page 4, Offset 75
    public const int REG_FAN_OFFSET_PERFORMANCE = 1100;// Page 4, Offset 76
    public const int REG_FAN_OFFSET_ENTERTAINMENT = 1101;// Page 4, Offset 77

    // Buffer and Math Constants
    public const int BUFFER_SIZE_256 = 256;
    public const int MINIMUM_TELEMETRY_BUFFER_SIZE = 24;
    public const int GPU_STANDBY_TEMP_THRESHOLD = 20;
    public const double RPM_FORMULA_CONSTANT = 2156250.0;

    /// <summary>
    /// Primary WMI / InsydeDCHU Command IDs.
    /// </summary>
    public static class WmiCommands
    {
        public const int CMD_FAN_TELEMETRY = 12;
        public const int CMD_HW_CAPABILITIES = 13;
        public const int CMD_FAN_DUTY_TARGET = 22;
        public const int CMD_SET_WHITE_LED_KB = 39;
        public const int CMD_GET_WHITE_LED_KB = 61;
        public const int CMD_GET_SUPPORT_FEATURE = 70;
        public const int CMD_ENERGY_STAR = 79;
        public const int CMD_FAN_CONTROL_ALT = 98;
        public const int CMD_KB_LED = 103;
        public const int CMD_SYS_CONTROL = 121;
        public const int CMD_BIOS_SPECIAL_FEATURE = 122;
    }

    /// <summary>
    /// SubCommand indices passed in byte 3 of 4-byte command packets.
    /// </summary>
    public static class WmiSubCommands
    {
        // Command 121 (CMD_SYS_CONTROL) SubCommands
        public const int SUB_FAN_MODE = 1;
        public const int SUB_FAN_OFFSET = 14;      // 0x0E
        public const int SUB_SLEEP_TIMER = 24;     // 0x18
        public const int SUB_POWER_MODE = 25;      // 0x19
        public const int SUB_FAN_SCHEDULE = 40;    // 0x28
        public const int SUB_ANTI_DUST = 41;       // 0x29

        // Command 98 (CMD_FAN_CONTROL_ALT) SubCommands
        public const int SUB_FAN_MODE_ALT = 1;
        public const int SUB_FAN_OFFSET_ALT = 2;
        public const int SUB_TURBO_FAN_ALT = 3;
    }

    /// <summary>
    /// Linear addressing constants for persistent AppSettings NVRAM storage.
    /// Formula: Page = Key / 256, Offset = Key % 256.
    /// </summary>
    public static class AppSettings
    {
        /// <summary>Computes the 256-byte Page index from a flat integer key.</summary>
        public static int GetPage(int key) => key / 256;

        /// <summary>Computes the byte Offset within the page from a flat integer key.</summary>
        public static int GetOffset(int key) => key % 256;

        // Page 1: Power & System Settings
        public const int PowerMode = 257;             // Page 1, Offset 1
        public const int DttStatus = 288;              // Page 1, Offset 32

        // Page 2: Keyboard & RGB Lighting
        public const int KBType = 516;                 // Page 2, Offset 4
        public const int OverrideBootEffect = 519;     // Page 2, Offset 7
        public const int LedKBAppLaunch = 520;         // Page 2, Offset 8
        public const int KbEffect = 544;               // Page 2, Offset 32
        public const int KbStaticStatus = 545;         // Page 2, Offset 33
        public const int KbSpeed = 546;                // Page 2, Offset 34
        public const int KbBrightness = 547;           // Page 2, Offset 35
        public const int KbSleepTimerTrigger = 548;    // Page 2, Offset 36
        public const int KbSleepTimerHour = 549;       // Page 2, Offset 37
        public const int KbSleepTimerMinute = 550;     // Page 2, Offset 38
        public const int KbSleepTimerSecond = 551;     // Page 2, Offset 39
        public const int KbPowerStatus = 592;          // Page 2, Offset 80
        public const int KbAllColor = 593;             // Page 2, Offset 81
        public const int KbAllRed = 593;               // Page 2, Offset 81
        public const int KbAllGreen = 594;             // Page 2, Offset 82
        public const int KbAllBlue = 595;              // Page 2, Offset 83
        public const int KbLeftStatus = 596;           // Page 2, Offset 84
        public const int KbLeftColor = 597;            // Page 2, Offset 85
        public const int KbLeftRed = 597;              // Page 2, Offset 85
        public const int KbLeftGreen = 598;            // Page 2, Offset 86
        public const int KbLeftBlue = 599;             // Page 2, Offset 87
        public const int KbMidStatus = 600;            // Page 2, Offset 88
        public const int KbMidColor = 601;             // Page 2, Offset 89
        public const int KbMidRed = 601;               // Page 2, Offset 89
        public const int KbMidGreen = 602;             // Page 2, Offset 90
        public const int KbMidBlue = 603;              // Page 2, Offset 91
        public const int KbRightStatus = 604;          // Page 2, Offset 92
        public const int KbRightColor = 605;           // Page 2, Offset 93
        public const int KbRightRed = 605;             // Page 2, Offset 93
        public const int KbRightGreen = 606;           // Page 2, Offset 94
        public const int KbRightBlue = 607;            // Page 2, Offset 95

        // Page 4: Fan & Thermal Settings
        public const int FanMode = 1029;               // Page 4, Offset 5
        public const int FanOffset = 1031;             // Page 4, Offset 7
        public const int TurboFan_Status = 1032;       // Page 4, Offset 8
        public const int NoiselessFan_status = 1033;   // Page 4, Offset 9

        // Per-PowerMode Saved Fan Profiles
        public const int FanMode_Quiet = 1094;         // Page 4, Offset 70
        public const int FanMode_PowerSaving = 1095;   // Page 4, Offset 71
        public const int FanMode_Performance = 1096;   // Page 4, Offset 72
        public const int FanMode_Entertainment = 1097; // Page 4, Offset 73

        public const int FanOffset_Quiet = 1098;       // Page 4, Offset 74
        public const int FanOffset_PowerSaving = 1099; // Page 4, Offset 75
        public const int FanOffset_Performance = 1100; // Page 4, Offset 76
        public const int FanOffset_Entertainment = 1101;// Page 4, Offset 77
    }

    /// <summary>
    /// Constants for Keyboard RGB LED hardware command protocol.
    /// </summary>
    public static class RgbLightingConstants
    {
        // Zone Prefixes (Byte 3 of Color Packet)
        public const byte ZONE_LEFT = 0xF0;
        public const byte ZONE_MID = 0xF1;
        public const byte ZONE_RIGHT = 0xF2;
        public const byte ZONE_LIGHTBAR = 0xF3;
        public const byte ZONE_LOGO = 0xF6;

        // Brightness Constants (Prefix 0xF4000000)
        public const uint BRIGHTNESS_PREFIX = 0xF4000000u;
        public const byte BRIGHTNESS_STEP_0 = 0;       // Off (0%)
        public const byte BRIGHTNESS_STEP_1 = 47;      // Low (25%)
        public const byte BRIGHTNESS_STEP_2 = 95;      // Medium (50%)
        public const byte BRIGHTNESS_STEP_3 = 143;     // High (75%)
        public const byte BRIGHTNESS_STEP_4 = 191;     // Maximum (100%)

        // Master Power Constants (Prefix 0xE0000000)
        public const uint MASTER_POWER_PREFIX = 0xE0000000u;
        public const uint MASTER_POWER_SUBFLAG = 7u;
        public const int MASTER_POWER_BIT_ENABLE = 12;
        public const int MASTER_POWER_BIT_LOGO = 13;
        public const int MASTER_POWER_BIT_LIGHTBAR = 15;
        public const int MASTER_POWER_BIT_LEFT = 16;
        public const int MASTER_POWER_BIT_MID = 17;
        public const int MASTER_POWER_BIT_RIGHT = 18;

        // Effect Mode Command Packets
        public const uint EFFECT_RANDOM = 0x70000000u;
        public const uint EFFECT_BREATH = 0x1002A000u;
        public const uint EFFECT_CYCLE  = 0x33010000u;
        public const uint EFFECT_WAVE   = 0xB0000000u;
        public const uint EFFECT_DANCE  = 0x80000000u;
        public const uint EFFECT_TEMPO  = 0x90000000u;
        public const uint EFFECT_FLASH  = 0xA0000000u;

        // Sleep Timer Constants (Command 121)
        public const uint SLEEP_TIMER_PREFIX = 0x18000000u;
        public const byte SLEEP_TIMER_ENABLE_FLAG = 0xFF;
        public const byte SLEEP_TIMER_DISABLE_FLAG = 0x00;
        public const int MIN_SLEEP_TIMEOUT_SECONDS = 5;

        // Blue Luminance Scaling Ratio
        public const double BLUE_SCALE_FACTOR = 200.0 / 255.0;
    }
}
