namespace ClevoControlPanel.Core.Models;

/// <summary>
/// Best-effort capability snapshot for the detected Clevo EC. Unknown values
/// stay permissive so the app keeps today’s behavior instead of hiding controls.
/// </summary>
public sealed record HardwareCapabilities
{
    public byte KeyboardType { get; init; } = 0xFF;
    public bool HasKeyboardBacklight { get; init; } = true;
    public bool SupportsRgbKeyboard { get; init; } = true;
    public bool SupportsPerKeyRgb { get; init; }
    public bool IsFanless { get; init; }
    public bool SupportsFanControl { get; init; } = true;
    public bool SupportsFanOffset { get; init; } = true;

    public string KeyboardSummary => KeyboardType switch
    {
        0 => "无键盘背光",
        1 or 6 or 22 => "白色背光键盘",
        3 or 19 or 35 or 51 or 243 => "单键 RGB 键盘（整块调色）",
        2 => "分区 RGB 键盘（整块调色）",
        0xFF => "键盘类型未知",
        _ => $"RGB 键盘（类型 {KeyboardType}）"
    };

    public string FanSummary => IsFanless
        ? "无风扇/未检测到风扇"
        : SupportsFanControl ? "支持风扇控制" : "不支持风扇控制";

    public string Summary => $"{KeyboardSummary} · {FanSummary}";
}
