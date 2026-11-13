namespace ClevoControlPanel.Core.Models;

/// <summary>
/// Operational power and thermal profiles supported by Clevo ACPI/EC firmware.
/// Matches Command 121 SubCommand 25 and AppSetting 257.
/// </summary>
public enum PowerMode : byte
{
    /// <summary>
    /// Quiet / Silent mode (0): Minimizes acoustic noise and throttles TDP limits.
    /// </summary>
    Quiet = 0,

    /// <summary>
    /// Power Saving / Eco mode (1): Maximizes battery life and limits CPU boost clocks.
    /// </summary>
    PowerSaving = 1,

    /// <summary>
    /// Performance mode (2): Maximizes sustained CPU/GPU power limits and cooling headroom.
    /// </summary>
    Performance = 2,

    /// <summary>
    /// Entertainment / Balanced mode (3): Standard balanced profile for general multimedia use.
    /// </summary>
    Entertainment = 3
}
