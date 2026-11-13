namespace ClevoControlPanel.Core.Models;

/// <summary>
/// Fan operating profiles recognized by the Clevo Embedded Controller.
/// Matches Command 121 SubCommand 1 and AppSetting 1029.
/// </summary>
public enum FanMode : byte
{
    /// <summary>
    /// Automatic thermal curve managed by EC firmware lookup tables (0).
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Maximum cooling: Commands 100% PWM duty to all fans (1).
    /// </summary>
    Maximum = 1,

    /// <summary>
    /// Silent profile: Conservative ramp-up curve prioritizing low acoustics (2).
    /// </summary>
    Silent = 2,

    /// <summary>
    /// NVIDIA Max-Q mode: Balanced thermal target for Max-Q architectures (5).
    /// </summary>
    MaxQ = 5,

    /// <summary>
    /// Custom fan curve: Inflection points defined by user (6).
    /// </summary>
    Custom = 6,

    /// <summary>
    /// Noiseless / Slow fan mode: Ultra-quiet low RPM profile for quiet environments (8).
    /// </summary>
    Noiseless = 8
}
