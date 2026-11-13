using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClevoControlPanel.Core.Models;
using ClevoControlPanel.Core.Services;
using ClevoControlPanel.Core.Telemetry;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ClevoControlPanel.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IHwService _hw;
    private readonly AutoModeController _autoController;
    private readonly CpuLoadSampler _cpuLoadSampler = new();
    private readonly DispatcherTimer _offsetDebounce;
    private readonly DispatcherTimer _colorDebounce;
    private readonly DispatcherTimer _effectSpeedDebounce;
    private readonly DispatcherTimer _effectAnimationTimer;
    private double _effectPhase;
    private PowerMode _lastAutoEffectiveMode;
    private string? _lastAutoError;
    private bool _syncing = true;
    private bool _disposed;
    private int _highTempCounter;
    private bool _isThermalSafetyActive;
    private FanMode _preFailsafeFanMode = FanMode.Auto;

    public MainViewModel()
    {
        _hw = HwServiceFactory.Create(out string backendStatus);
        _autoController = new AutoModeController(_hw);
        BackendStatus = backendStatus;
        IsLiveHardware = _hw.IsLiveHardware;
        IsAdministrator = ElevationHelper.IsAdministrator;
        IsAutoStartupEnabled = StartupHelper.IsStartupEnabled();
        RefreshOemServiceStatus();
        BackendBadgeText = IsLiveHardware ? "实时硬件" : "模拟数据";
        BackendBadgeBrush = Freeze(new SolidColorBrush(IsLiveHardware
            ? Color.FromRgb(0x3A, 0xB7, 0x95)
            : Color.FromRgb(0xE0, 0x9B, 0x3D)));

        PowerModeOptions =
        [
            new PowerModeOption(
                PowerMode.Quiet, "安静", "低噪音优先", "限制持续功耗，风扇曲线更保守，适合夜间与轻负载。",
                Wpf.Ui.Controls.SymbolRegular.WeatherMoon24, "#6C8CFF"),
            new PowerModeOption(
                PowerMode.PowerSaving, "省电", "最长续航", "压低 CPU/GPU 功耗上限，并让独显尽快进入 D3cold 待机。",
                Wpf.Ui.Controls.SymbolRegular.BatterySaver24, "#3AB795"),
            new PowerModeOption(
                PowerMode.Performance, "性能", "释放全部功耗", "提高 CPU/GPU 持续功耗上限，冷却策略更激进。",
                Wpf.Ui.Controls.SymbolRegular.Rocket24, "#FF6B4A"),
            new PowerModeOption(
                PowerMode.Entertainment, "娱乐", "均衡策略", "在性能、温度与噪音之间取得平衡，适合日常使用。",
                Wpf.Ui.Controls.SymbolRegular.TopSpeed24, "#B07CFF")
        ];

        FanModeOptions =
        [
            new FanModeOption(FanMode.Auto, "自动", "EC 温控曲线", Wpf.Ui.Controls.SymbolRegular.Gauge24),
            new FanModeOption(FanMode.Maximum, "最大", "全速冷却", Wpf.Ui.Controls.SymbolRegular.Rocket24),
            new FanModeOption(FanMode.Silent, "静音", "降低噪音", Wpf.Ui.Controls.SymbolRegular.WeatherMoon24),
            new FanModeOption(FanMode.Noiseless, "无噪", "最低转速", Wpf.Ui.Controls.SymbolRegular.LeafOne24)
        ];

        EffectOptions =
        [
            new EffectOption(RgbEffectMode.Static, "常亮", Wpf.Ui.Controls.SymbolRegular.Lightbulb24),
            new EffectOption(RgbEffectMode.Breath, "呼吸", Wpf.Ui.Controls.SymbolRegular.Heart24),
            new EffectOption(RgbEffectMode.Cycle, "循环", Wpf.Ui.Controls.SymbolRegular.ArrowSync24),
            new EffectOption(RgbEffectMode.Wave, "波浪", Wpf.Ui.Controls.SymbolRegular.Water24),
            new EffectOption(RgbEffectMode.Flash, "闪烁", Wpf.Ui.Controls.SymbolRegular.Flash24)
        ];

        SleepOptions =
        [
            new SleepOption(0, "关闭"),
            new SleepOption(15, "15 秒"),
            new SleepOption(30, "30 秒"),
            new SleepOption(60, "1 分钟"),
            new SleepOption(300, "5 分钟")
        ];

        ColorPresets =
        [
            new ColorPresetOption("冰白", RgbColor.White),
            new ColorPresetOption("烈焰红", RgbColor.RedColor),
            new ColorPresetOption("活力橙", RgbColor.Orange),
            new ColorPresetOption("琥珀金", new RgbColor(255, 179, 0)),
            new ColorPresetOption("柠檬黄", RgbColor.Yellow),
            new ColorPresetOption("荧光绿", new RgbColor(0, 255, 100)),
            new ColorPresetOption("原野绿", RgbColor.GreenColor),
            new ColorPresetOption("极光青", RgbColor.Cyan),
            new ColorPresetOption("冰川蓝", new RgbColor(0, 210, 255)),
            new ColorPresetOption("电竞蓝", RgbColor.BlueColor),
            new ColorPresetOption("魅影紫", new RgbColor(153, 0, 255)),
            new ColorPresetOption("品红", RgbColor.Magenta),
            new ColorPresetOption("赛博粉", new RgbColor(255, 0, 127)),
            new ColorPresetOption("暖白", new RgbColor(255, 240, 215))
        ];

        var specs = SystemInfoSampler.GetHardwareSpecs();
        CpuModelName = specs.CpuName;
        GpuModelName = specs.GpuName;
        OsDescription = specs.OsDescription;
        HardwareSummary = BuildHardwareSummary(specs.CpuName, specs.GpuName);

        var machine = SystemInfoSampler.GetMachineIdentity();
        MachineDisplay = machine.DisplayText;
        MachineManufacturer = machine.Manufacturer;
        MachineModel = machine.Model;
        IsUntestedModel = !TestedModels.Any(t =>
            machine.Manufacturer.Contains(t.Manufacturer, StringComparison.OrdinalIgnoreCase) &&
            machine.Model.Contains(t.Model, StringComparison.OrdinalIgnoreCase));
        CompatibilitySummary = $"{machine.DisplayText} · {Capabilities.Summary}";
        CompatibilityNote = IsUntestedModel
            ? "该机型未在本机验证。风扇/灯效指令按蓝天标准 EC 协议发送，请先小范围测试；某项控制异常时请停用该项。"
            : "已在同型号机型上验证。";

        SelectedPowerModeOption = PowerModeOptions[3];
        SelectedFanModeOption = FanModeOptions[0];
        SelectedEffectOption = EffectOptions[0];
        SelectedSleepOption = SleepOptions[2];
        CurrentColorBrush = Freeze(new SolidColorBrush(Color.FromRgb(0, 0, 200)));
        CurrentColorHex = "#0000C8";

        _offsetDebounce = CreateTimer(220, ApplyFanOffset);
        _colorDebounce = CreateTimer(180, ApplyCurrentColor);
        _effectSpeedDebounce = CreateTimer(180, ApplyCurrentEffect);
        _effectAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _effectAnimationTimer.Tick += (_, _) => TickEffectAnimation();

        SyncAllFromService();
        _syncing = false;
        AutoEffectiveModeTitle = SelectedPowerModeOption.Title;
        AutoModeStatusText = "自动模式关闭 · 手动选择生效";

        if (IsLiveHardware)
        {
            // OEM Init re-asserts the stored lighting mode after the EC/service restarts.
            Execute(() => _hw.SetKeyboardRgbEffect(
                    SelectedEffectOption.Mode,
                    (byte)Math.Clamp((int)Math.Round(EffectSpeed), 1, 10)),
                "恢复键盘灯效");
        }

        RefreshEffectAnimation();

        if (IsSimulation)
        {
            StatusTitle = "模拟模式";
            StatusMessage = BackendStatus;
            StatusSeverity = InfoBarSeverity.Warning;
            IsStatusOpen = true;
        }
        else if (IsUntestedModel)
        {
            StatusTitle = "兼容模式";
            StatusMessage = $"{MachineDisplay} 未在本机验证，风扇/灯效控制请先小范围测试。";
            StatusSeverity = InfoBarSeverity.Warning;
            IsStatusOpen = true;
        }

        _hw.TelemetryUpdated += OnTelemetryUpdated;
        _hw.StartTelemetry(1000);
        ApplyTelemetry(_hw.GetTelemetry());
    }

    private static readonly (string Manufacturer, string Model)[] TestedModels =
    [
        ("COLORFUL", "P15 24")
    ];

    public bool IsLiveHardware { get; }
    public bool IsSimulation => !IsLiveHardware;
    public string AppDisplayName => "Clevo Control Panel";
    public string AppVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    public string AuthorName => "xukuncai";
    public string AuthorEmail => "xukuncai0001@gmail.com";
    public HardwareCapabilities Capabilities => _hw.Capabilities;
    public bool SupportsRgbKeyboard => Capabilities.SupportsRgbKeyboard;
    public bool HasKeyboardBacklight => Capabilities.HasKeyboardBacklight;
    public bool SupportsPerKeyRgb => Capabilities.SupportsPerKeyRgb;
    public bool IsFanless => Capabilities.IsFanless;
    public bool IsFanControlAvailable => Capabilities.SupportsFanControl && !IsFanless;
    public bool IsFanOffsetAvailable => Capabilities.SupportsFanOffset && !IsFanless;
    public bool IsFanControlUnavailable => IsFanless;
    public bool IsKeyboardCapabilityWarningOpen => !SupportsRgbKeyboard || !HasKeyboardBacklight;
    public bool IsUntestedModel { get; }

    public string KeyboardCapabilityNotice => !HasKeyboardBacklight
        ? "未检测到键盘背光，键盘灯效控制已禁用。"
        : !SupportsRgbKeyboard
            ? "此机型为单色背光键盘，颜色与动画效果已禁用，仅保留亮度与开关。"
            : string.Empty;
    public string FanControlNotice => IsFanless
        ? "未检测到风扇控制，风扇设置已禁用。"
        : string.Empty;

    public string BackendStatus { get; }
    public bool IsAdministrator { get; }
    public bool IsElevationButtonVisible => !IsAdministrator;
    public Brush BackendBadgeBrush { get; }
    public string BackendBadgeText { get; }
    public IReadOnlyList<PowerModeOption> PowerModeOptions { get; }
    public IReadOnlyList<FanModeOption> FanModeOptions { get; }
    public IReadOnlyList<EffectOption> EffectOptions { get; }
    public IReadOnlyList<SleepOption> SleepOptions { get; }
    public IReadOnlyList<ColorPresetOption> ColorPresets { get; }

    [ObservableProperty] private int _selectedPageIndex;
    [ObservableProperty] private PowerModeOption _selectedPowerModeOption = null!;
    [ObservableProperty] private FanModeOption _selectedFanModeOption = null!;
    [ObservableProperty] private EffectOption _selectedEffectOption = null!;
    [ObservableProperty] private SleepOption _selectedSleepOption = null!;
    [ObservableProperty] private string _powerModeDisplay = "娱乐";

    [ObservableProperty] private int _cpuTemperature;
    [ObservableProperty] private int _gpuTemperature;
    [ObservableProperty] private int _cpuFanRpm;
    [ObservableProperty] private int _gpuFanRpm;
    [ObservableProperty] private int _cpuFanDuty;
    [ObservableProperty] private int _gpuFanDuty;
    [ObservableProperty] private int _systemTemperature;
    [ObservableProperty] private bool _isGpuStandby;
    [ObservableProperty] private string _lastUpdated = "--:--:--";

    [ObservableProperty] private double _fanOffset;
    [ObservableProperty] private bool _isTurboFan;

    [ObservableProperty] private double _red;
    [ObservableProperty] private double _green;
    [ObservableProperty] private double _blue;
    [ObservableProperty] private double _effectSpeed = 5;
    [ObservableProperty] private double _keyboardBrightness = 4;
    [ObservableProperty] private bool _isKeyboardPowerOn = true;
    [ObservableProperty] private Brush _currentColorBrush = Brushes.Transparent;
    [ObservableProperty] private string _currentColorHex = "#0000C8";
    [ObservableProperty] private Color _leftZoneColor = Color.FromRgb(0, 0, 200);
    [ObservableProperty] private Color _middleZoneColor = Color.FromRgb(0, 0, 200);
    [ObservableProperty] private Color _rightZoneColor = Color.FromRgb(0, 0, 200);
    [ObservableProperty] private bool _isDarkTheme = false;

    [ObservableProperty] private bool _isStatusOpen;
    [ObservableProperty] private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;
    [ObservableProperty] private string _statusTitle = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] private bool _isAutoMode;
    [ObservableProperty] private string _autoModeStatusText = "自动模式关闭 · 手动选择生效";
    [ObservableProperty] private string _autoEffectiveModeTitle = "娱乐";
    [ObservableProperty] private double _cpuLoadPercent;
    [ObservableProperty] private double _autoPowerIndex;

    [ObservableProperty] private bool _minimizeToTrayOnClose = true;
    [ObservableProperty] private bool _isAutoStartupEnabled;
    [ObservableProperty] private bool _isThermalSafetyEnabled = true;

    [ObservableProperty] private double _ramUsagePercent;
    [ObservableProperty] private string _ramDetails = "-- GB / -- GB";
    [ObservableProperty] private double _diskUsagePercent;
    [ObservableProperty] private string _diskDetails = "-- GB / -- GB";

    [ObservableProperty] private bool _isOemServiceRunning;
    [ObservableProperty] private string _oemServiceStatusText = "检测中...";
    [ObservableProperty] private string _oemServiceButtonText = "挂起官方服务";

    [ObservableProperty] private string _cpuModelName = "正在检测...";
    [ObservableProperty] private string _gpuModelName = "正在检测...";
    [ObservableProperty] private string _osDescription = "Windows";
    [ObservableProperty] private string _hardwareSummary = "Clevo Laptop";
    [ObservableProperty] private string _machineDisplay = "正在检测...";
    [ObservableProperty] private string _machineManufacturer = "";
    [ObservableProperty] private string _machineModel = "";
    [ObservableProperty] private string _compatibilitySummary = "";
    [ObservableProperty] private string _compatibilityNote = "";
    [ObservableProperty] private string _powerStatusSummary = "交流供电";
    [ObservableProperty] private bool _isOnAcPower = true;
    [ObservableProperty] private int _batteryPercent = -1;

    public event Action? TelemetryRefreshed;

    public bool IsManualModeEnabled => !IsAutoMode;

    public string CpuSummary => $"CPU {CpuTemperature}°C · {CpuFanRpm:N0} RPM";
    public string GpuSummary => IsGpuStandby ? "GPU D3cold · 待机" : $"GPU {GpuTemperature}°C · {GpuFanRpm:N0} RPM";
    public string CpuTempSubText => $"风扇 {CpuFanRpm:N0} RPM";
    public string GpuTempSubText => IsGpuStandby ? "D3cold 待机" : $"风扇 {GpuFanRpm:N0} RPM";
    public string CpuFanSubText => $"PWM {CpuFanDuty}%";
    public string GpuFanSubText => IsGpuStandby ? "D3cold · 停转" : $"PWM {GpuFanDuty}%";
    public string GpuStateText => IsGpuStandby ? "D3cold 待机" : "活动 / 供电中";
    public string SystemTempSummary => SystemTemperature > 0
        ? $"{SystemTemperature}°C"
        : "未配备探头 (双风扇模具)";
    public string FanOffsetText => $"{FanOffset:F0}%";
    public string FanOffsetSummary => FanOffset < 0.5
        ? "跟随 EC 出厂曲线"
        : $"比出厂曲线高 {FanOffset:F0}%";
    public string BrightnessText => $"{Math.Round(KeyboardBrightness):F0} / 4";
    public string EffectSpeedText => $"{Math.Round(EffectSpeed):F0} / 10";
    public bool IsThermalWarning => CpuTemperature >= 90;
    public string ThermalWarningText => $"CPU 温度已达到 {CpuTemperature}°C，建议开启 Turbo 强冷或切换到性能模式。";

    private RgbColor SelectedRgbColor => new(
        (byte)Math.Clamp((int)Math.Round(Red), 0, 255),
        (byte)Math.Clamp((int)Math.Round(Green), 0, 255),
        (byte)Math.Clamp((int)Math.Round(Blue), 0, 255));

    partial void OnSelectedPowerModeOptionChanged(PowerModeOption value)
    {
        if (_syncing || value == null)
            return;

        if (IsAutoMode)
        {
            IsAutoMode = false;
        }

        Execute(() => _hw.SetPowerMode(value.Mode), $"切换到{value.Title}模式");
        PowerModeDisplay = value.Title;
        SyncFanStateFromService();
    }

    partial void OnIsAutoModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsManualModeEnabled));

        if (value)
        {
            _autoController.Enable();
            _lastAutoEffectiveMode = _autoController.EffectiveMode;
            AutoEffectiveModeTitle = GetPowerModeTitle(_lastAutoEffectiveMode);
            AutoModeStatusText = $"已启用 · 当前 {AutoEffectiveModeTitle} · 正在采样功耗…";
            PowerModeDisplay = $"自动 · {AutoEffectiveModeTitle}";
            EvaluateAutoMode(_hw.GetTelemetry());
        }
        else
        {
            _autoController.Disable();
            AutoModeStatusText = "自动模式关闭 · 手动选择生效";
            SyncAllFromService();
            AutoEffectiveModeTitle = SelectedPowerModeOption.Title;
            PowerModeDisplay = SelectedPowerModeOption.Title;
        }
    }

    partial void OnSelectedFanModeOptionChanged(FanModeOption value)
    {
        if (_syncing || value == null)
            return;

        Execute(() => _hw.SetFanMode(value.Mode), $"切换风扇策略为{value.Title}");
        SyncFanStateFromService();
    }

    partial void OnIsTurboFanChanged(bool value)
    {
        if (_syncing)
            return;

        Execute(() => _hw.SetTurboFan(value), value ? "开启 Turbo 强冷" : "关闭 Turbo 强冷");
        SyncFanStateFromService();
    }

    partial void OnFanOffsetChanged(double value)
    {
        OnPropertyChanged(nameof(FanOffsetText));
        OnPropertyChanged(nameof(FanOffsetSummary));
        if (_syncing)
            return;

        _offsetDebounce.Stop();
        _offsetDebounce.Start();
    }

    partial void OnRedChanged(double value) => OnRgbComponentChanged();
    partial void OnGreenChanged(double value) => OnRgbComponentChanged();
    partial void OnBlueChanged(double value) => OnRgbComponentChanged();

    partial void OnSelectedEffectOptionChanged(EffectOption value)
    {
        if (_syncing || value == null)
            return;

        ApplyCurrentEffect();
        RefreshEffectAnimation();
    }

    partial void OnEffectSpeedChanged(double value)
    {
        OnPropertyChanged(nameof(EffectSpeedText));
        if (_syncing)
            return;

        _effectSpeedDebounce.Stop();
        _effectSpeedDebounce.Start();
    }

    partial void OnKeyboardBrightnessChanged(double value)
    {
        OnPropertyChanged(nameof(BrightnessText));
        if (_syncing)
            return;

        byte level = (byte)Math.Clamp((int)Math.Round(value), 0, 4);
        Execute(() => _hw.SetKeyboardBrightness(level), "调整键盘亮度");
        RefreshEffectAnimation();
    }

    partial void OnIsKeyboardPowerOnChanged(bool value)
    {
        if (_syncing)
            return;

        Execute(() => _hw.SetKeyboardPower(value), value ? "开启键盘背光" : "关闭键盘背光");
        RefreshEffectAnimation();
    }

    partial void OnSelectedSleepOptionChanged(SleepOption value)
    {
        if (_syncing || value == null)
            return;

        Execute(() => _hw.SetKeyboardSleepTimeout(value.Seconds), "设置键盘休眠计时");
    }

    [RelayCommand]
    private void ApplyPreset(RgbColor color)
    {
        _syncing = true;
        SetRgbFields(color);
        UpdateKeyboardPreviewColor(color);
        _syncing = false;

        ApplyCurrentColor();
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        ApplicationThemeManager.Apply(
            IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.Mica,
            true);
    }

    [RelayCommand]
    private void RestartAsAdmin()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
                return;

            App.ReleaseSingleInstanceLock();

            Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Verb = "runas"
            });

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            ShowError("管理员提权", ex.Message);
        }
    }

    [RelayCommand]
    private void OpenAuthorEmail()
    {
        try
        {
            Process.Start(new ProcessStartInfo($"mailto:{AuthorEmail}") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError("打开邮箱", ex.Message);
        }
    }

    [RelayCommand]
    private void SetQuickOffset(string percentStr)
    {
        if (double.TryParse(percentStr, out double val))
        {
            FanOffset = Math.Clamp(val, 0, 100);
        }
    }

    [RelayCommand]
    private void ToggleKeyboardPower()
    {
        IsKeyboardPowerOn = !IsKeyboardPowerOn;
    }

    [RelayCommand]
    private void SetQuickBrightness(string levelStr)
    {
        if (double.TryParse(levelStr, out double level))
        {
            if (level == 0)
            {
                KeyboardBrightness = 0;
            }
            else
            {
                if (!IsKeyboardPowerOn) IsKeyboardPowerOn = true;
                KeyboardBrightness = Math.Clamp(level, 0, 4);
            }
        }
    }

    private void ApplyFanOffset()
    {
        byte offset = (byte)Math.Clamp((int)Math.Round(FanOffset), 0, 100);
        Execute(() => _hw.SetFanOffset(offset), "设置风扇偏移");
    }

    private void ApplyCurrentColor()
    {
        var color = SelectedRgbColor;
        Execute(() => _hw.SetKeyboardRgbColor(color, KeyboardZone.All), "设置键盘颜色");
        SyncZoneColorsFromService();
    }

    private void ApplyCurrentEffect()
    {
        int speed = Math.Clamp((int)Math.Round(EffectSpeed), 1, 10);
        Execute(() => _hw.SetKeyboardRgbEffect(SelectedEffectOption.Mode, (byte)speed),
            $"设置{SelectedEffectOption.Title}灯效");
    }

    private static bool IsAnimatedEffect(RgbEffectMode mode) =>
        mode is RgbEffectMode.Breath or RgbEffectMode.Cycle or RgbEffectMode.Wave or RgbEffectMode.Flash;

    private void RefreshEffectAnimation()
    {
        if (_disposed)
            return;

        bool animate = SelectedEffectOption != null &&
                       IsAnimatedEffect(SelectedEffectOption.Mode) &&
                       IsKeyboardPowerOn &&
                       KeyboardBrightness > 0;

        if (animate)
        {
            if (!_effectAnimationTimer.IsEnabled)
            {
                _effectPhase = 0;
                _effectAnimationTimer.Start();
            }
        }
        else
        {
            _effectAnimationTimer.Stop();
            RestorePreviewColors();
        }
    }

    private void TickEffectAnimation()
    {
        if (_disposed || _syncing)
            return;

        if (SelectedEffectOption == null ||
            !IsAnimatedEffect(SelectedEffectOption.Mode) ||
            !IsKeyboardPowerOn ||
            KeyboardBrightness <= 0)
        {
            RefreshEffectAnimation();
            return;
        }

        try
        {
            double speed = Math.Clamp(EffectSpeed, 1, 10);
            _effectPhase += ComputePhaseStep(SelectedEffectOption.Mode, speed);
            if (_effectPhase > Math.PI * 200)
                _effectPhase -= Math.PI * 200;

            var state = _hw.GetKeyboardState();
            var (left, mid, right) = ComputeEffectColors(
                SelectedEffectOption.Mode,
                state.LeftColor,
                state.MiddleColor,
                state.RightColor,
                _effectPhase);

            _hw.SetKeyboardLiveColors(left, mid, right);
            LeftZoneColor = ToMediaColor(left);
            MiddleZoneColor = ToMediaColor(mid);
            RightZoneColor = ToMediaColor(right);
        }
        catch (Exception ex)
        {
            _effectAnimationTimer.Stop();
            ShowError("键盘灯效动画", ex.Message);
        }
    }

    private void RestorePreviewColors()
    {
        var state = _hw.GetKeyboardState();
        LeftZoneColor = ToMediaColor(state.LeftColor);
        MiddleZoneColor = ToMediaColor(state.MiddleColor);
        RightZoneColor = ToMediaColor(state.RightColor);
    }

    private static double ComputePhaseStep(RgbEffectMode mode, double speed)
    {
        double periodSeconds = mode switch
        {
            RgbEffectMode.Breath => 7.5 - 0.5 * speed,
            RgbEffectMode.Cycle => 12.0 - 0.8 * speed,
            RgbEffectMode.Wave => 5.0 - 0.3 * speed,
            RgbEffectMode.Flash => 1.6 - 0.1 * speed,
            _ => 5.0
        };

        return Math.PI * 2 / (Math.Max(0.4, periodSeconds) * 10);
    }

    private static (RgbColor Left, RgbColor Mid, RgbColor Right) ComputeEffectColors(
        RgbEffectMode mode,
        RgbColor left,
        RgbColor mid,
        RgbColor right,
        double phase)
    {
        switch (mode)
        {
            case RgbEffectMode.Breath:
            {
                double factor = 0.18 + 0.82 * (0.5 - 0.5 * Math.Cos(phase));
                return (Scale(left, factor), Scale(mid, factor), Scale(right, factor));
            }
            case RgbEffectMode.Cycle:
            {
                var color = FromHsv((phase * 180.0 / Math.PI) % 360.0, 1.0, 1.0);
                return (color, color, color);
            }
            case RgbEffectMode.Wave:
            {
                double factor = 0.18 + 0.82 * (0.5 - 0.5 * Math.Cos(phase));
                var color = Scale(left, factor);
                return (color, color, color);
            }
            case RgbEffectMode.Flash:
            {
                double factor = Math.Sin(phase) > 0 ? 1.0 : 0.05;
                return (Scale(left, factor), Scale(mid, factor), Scale(right, factor));
            }
            default:
                return (left, mid, right);
        }
    }

    private static RgbColor Scale(RgbColor color, double factor) =>
        RgbColor.CreateClamped(
            (int)Math.Round(color.Red * factor),
            (int)Math.Round(color.Green * factor),
            (int)Math.Round(color.Blue * factor));

    private static RgbColor FromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        double c = value * saturation;
        double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = value - c;
        double r = 0, g = 0, b = 0;

        if (hue < 60) { r = c; g = x; }
        else if (hue < 120) { r = x; g = c; }
        else if (hue < 180) { g = c; b = x; }
        else if (hue < 240) { g = x; b = c; }
        else if (hue < 300) { r = x; b = c; }
        else { r = c; b = x; }

        return RgbColor.CreateClamped(
            (int)Math.Round((r + m) * 255),
            (int)Math.Round((g + m) * 255),
            (int)Math.Round((b + m) * 255));
    }

    private void OnRgbComponentChanged()
    {
        UpdateColorPreview();
        if (_syncing)
            return;

        UpdateKeyboardPreviewColor(SelectedRgbColor);
        _colorDebounce.Stop();
        _colorDebounce.Start();
    }

    private void UpdateColorPreview()
    {
        var color = SelectedRgbColor;
        var brush = Freeze(new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue)));
        CurrentColorBrush = brush;
        CurrentColorHex = color.ToHex().ToUpperInvariant();
    }

    private void UpdateKeyboardPreviewColor(RgbColor color)
    {
        var mediaColor = Color.FromRgb(color.Red, color.Green, color.Blue);
        LeftZoneColor = mediaColor;
        MiddleZoneColor = mediaColor;
        RightZoneColor = mediaColor;
    }

    private void SetRgbFields(RgbColor color)
    {
        Red = color.Red;
        Green = color.Green;
        Blue = color.Blue;
    }

    private void SyncAllFromService()
    {
        _syncing = true;
        try
        {
            var power = PowerModeOptions.FirstOrDefault(o => o.Mode == _hw.GetPowerMode()) ?? PowerModeOptions[3];
            SelectedPowerModeOption = power;
            PowerModeDisplay = IsAutoMode ? $"自动 · {power.Title}" : power.Title;

            SyncFanFieldsCore();
            SyncKeyboardFieldsCore();
        }
        finally
        {
            _syncing = false;
        }

        UpdateColorPreview();
    }

    private void SyncFanStateFromService()
    {
        _syncing = true;
        try
        {
            SyncFanFieldsCore();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SyncFanFieldsCore()
    {
        var mode = _hw.GetFanMode();
        SelectedFanModeOption = FanModeOptions.FirstOrDefault(o => o.Mode == mode) ?? FanModeOptions[0];
        FanOffset = _hw.GetFanOffset();
        IsTurboFan = _hw.GetTurboFan();
    }

    private void SyncKeyboardFieldsCore()
    {
        var state = _hw.GetKeyboardState();

        SelectedEffectOption = EffectOptions.FirstOrDefault(o => o.Mode == state.Mode) ?? EffectOptions[0];

        EffectSpeed = state.Speed;
        KeyboardBrightness = state.Brightness;
        IsKeyboardPowerOn = state.IsPowered;

        var sleep = SleepOptions.FirstOrDefault(o => o.Seconds == state.SleepTimeoutSeconds);
        if (sleep != null)
            SelectedSleepOption = sleep;
        else if (state.SleepTimeoutSeconds == 0)
            SelectedSleepOption = SleepOptions[0];

        UpdateKeyboardPreviewColor(state.LeftColor);
        SetRgbFields(state.LeftColor);
    }

    private void SyncZoneColorsFromService()
    {
        var state = _hw.GetKeyboardState();
        _syncing = true;
        try
        {
            UpdateKeyboardPreviewColor(state.LeftColor);
            SetRgbFields(state.LeftColor);
        }
        finally
        {
            _syncing = false;
        }

        UpdateColorPreview();
    }

    private void OnTelemetryUpdated(object? sender, TelemetryData telemetry)
    {
        var app = Application.Current;
        if (app == null || app.Dispatcher.HasShutdownStarted)
            return;

        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => ApplyTelemetry(telemetry)));
    }

    private void ApplyTelemetry(TelemetryData telemetry)
    {
        if (_disposed)
            return;

        CpuTemperature = telemetry.CpuTemperatureCelsius;
        GpuTemperature = telemetry.GpuTemperatureCelsius;
        CpuFanRpm = telemetry.Fan1Rpm;
        GpuFanRpm = telemetry.Fan2Rpm;
        CpuFanDuty = telemetry.Fan1DutyPercent;
        GpuFanDuty = telemetry.Fan2DutyPercent;
        SystemTemperature = telemetry.SysTemperatureCelsius;
        IsGpuStandby = !telemetry.IsGpuActive;
        LastUpdated = telemetry.Timestamp.ToLocalTime().ToString("HH:mm:ss");

        OnPropertyChanged(nameof(CpuSummary));
        OnPropertyChanged(nameof(GpuSummary));
        OnPropertyChanged(nameof(CpuTempSubText));
        OnPropertyChanged(nameof(GpuTempSubText));
        OnPropertyChanged(nameof(CpuFanSubText));
        OnPropertyChanged(nameof(GpuFanSubText));
        OnPropertyChanged(nameof(GpuStateText));
        OnPropertyChanged(nameof(SystemTempSummary));
        OnPropertyChanged(nameof(IsThermalWarning));
        OnPropertyChanged(nameof(ThermalWarningText));

        try
        {
            var metrics = SystemInfoSampler.Sample();
            RamUsagePercent = metrics.RamUsagePercent;
            RamDetails = $"{metrics.RamUsedGb:F1} GB / {metrics.RamTotalGb:F1} GB";
            DiskUsagePercent = metrics.DiskUsagePercent;
            DiskDetails = $"{metrics.DiskUsedGb:F1} GB / {metrics.DiskTotalGb:F1} GB ({metrics.DiskName})";

            IsOnAcPower = SystemPowerState.IsOnAcPower;
            BatteryPercent = SystemPowerState.BatteryPercent;
            PowerStatusSummary = SystemPowerState.PowerStatusSummary;
        }
        catch { }

        if (IsThermalSafetyEnabled)
        {
            if (CpuTemperature >= 94 || GpuTemperature >= 86)
            {
                _highTempCounter++;
                if (_highTempCounter >= 3 && !_isThermalSafetyActive)
                {
                    _isThermalSafetyActive = true;
                    _preFailsafeFanMode = SelectedFanModeOption?.Mode ?? FanMode.Auto;
                    _hw.SetTurboFan(true);
                    _syncing = true;
                    IsTurboFan = true;
                    _syncing = false;
                    ShowError("高温应急保护激活", $"核心温度已达到 CPU {CpuTemperature}°C / GPU {GpuTemperature}°C，已自动强制开启强冷散热！");
                }
            }
            else if (_isThermalSafetyActive && CpuTemperature < 76 && GpuTemperature < 72)
            {
                _highTempCounter = 0;
                _isThermalSafetyActive = false;
                _hw.SetTurboFan(false);
                _hw.SetFanMode(_preFailsafeFanMode);
                _syncing = true;
                IsTurboFan = false;
                SyncFanFieldsCore();
                _syncing = false;
                StatusTitle = "高温恢复";
                StatusMessage = "硬件核心温度已降至安全区间，恢复原散热策略。";
                StatusSeverity = InfoBarSeverity.Success;
                IsStatusOpen = true;
            }
            else
            {
                _highTempCounter = Math.Max(0, _highTempCounter - 1);
            }
        }

        EvaluateAutoMode(telemetry);
        TelemetryRefreshed?.Invoke();
    }

    private void EvaluateAutoMode(TelemetryData telemetry)
    {
        double load = _cpuLoadSampler.Sample();
        CpuLoadPercent = load;

        if (!IsAutoMode)
            return;

        bool onBattery = !SystemPowerState.IsOnAcPower;
        PowerMode effective = _autoController.Observe(telemetry, load, onBattery);
        AutoPowerIndex = _autoController.LastPowerIndex;

        string title = GetPowerModeTitle(effective);
        AutoEffectiveModeTitle = title;
        AutoModeStatusText =
            $"当前 {title} · 功耗指数 {AutoPowerIndex:F0} · CPU 负载 {CpuLoadPercent:F0}% · {(onBattery ? "电池供电" : "交流供电")}";

        if (effective != _lastAutoEffectiveMode)
        {
            _lastAutoEffectiveMode = effective;

            _syncing = true;
            try
            {
                SelectedPowerModeOption = PowerModeOptions.FirstOrDefault(o => o.Mode == effective)
                                         ?? SelectedPowerModeOption;
            }
            finally
            {
                _syncing = false;
            }

            PowerModeDisplay = $"自动 · {title}";
            SyncFanStateFromService();
        }

        if (!string.IsNullOrEmpty(_autoController.LastError) &&
            _autoController.LastError != _lastAutoError)
        {
            _lastAutoError = _autoController.LastError;
            ShowError("自动模式切换失败", _lastAutoError);
        }
    }

    private string GetPowerModeTitle(PowerMode mode) =>
        PowerModeOptions.FirstOrDefault(o => o.Mode == mode)?.Title ?? mode.ToString();

    private void Execute(Action action, string description)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ShowError(description, ex.Message);
            SyncAllFromService();
        }
    }

    private void ShowError(string title, string message)
    {
        StatusTitle = title;
        StatusMessage = message;
        StatusSeverity = InfoBarSeverity.Error;
        IsStatusOpen = true;
    }

    private static DispatcherTimer CreateTimer(int milliseconds, Action action)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        return timer;
    }

    private static Color ToMediaColor(RgbColor color) => Color.FromRgb(color.Red, color.Green, color.Blue);

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    partial void OnIsAutoStartupEnabledChanged(bool value)
    {
        StartupHelper.SetStartup(value);
    }

    [RelayCommand]
    private void ToggleOemService()
    {
        if (OemServiceManager.IsOemServiceRunning())
        {
            if (OemServiceManager.StopOemService(out string msg))
            {
                StatusTitle = "官方服务已挂起";
                StatusMessage = msg;
                StatusSeverity = InfoBarSeverity.Success;
                IsStatusOpen = true;
            }
            else
            {
                ShowError("挂起服务失败", msg);
            }
        }
        else
        {
            if (OemServiceManager.StartOemService(out string msg))
            {
                StatusTitle = "官方服务已恢复";
                StatusMessage = msg;
                StatusSeverity = InfoBarSeverity.Success;
                IsStatusOpen = true;
            }
            else
            {
                ShowError("启动服务失败", msg);
            }
        }
        RefreshOemServiceStatus();
    }

    public void RefreshOemServiceStatus()
    {
        bool installed = OemServiceManager.IsOemServiceInstalled();
        if (!installed)
        {
            OemServiceStatusText = "未安装官方服务";
            OemServiceButtonText = "未安装";
            IsOemServiceRunning = false;
            return;
        }

        bool running = OemServiceManager.IsOemServiceRunning();
        IsOemServiceRunning = running;
        OemServiceStatusText = running ? "运行中 (可能产生冲突)" : "已挂起 (独占控制生效)";
        OemServiceButtonText = running ? "挂起官方服务" : "恢复官方服务";
    }

    private static string BuildHardwareSummary(string cpu, string gpu)
    {
        string c = cpu;
        int iIdx = c.IndexOf("i5-", StringComparison.OrdinalIgnoreCase);
        if (iIdx < 0) iIdx = c.IndexOf("i7-", StringComparison.OrdinalIgnoreCase);
        if (iIdx < 0) iIdx = c.IndexOf("i9-", StringComparison.OrdinalIgnoreCase);
        if (iIdx >= 0)
        {
            string part = c.Substring(iIdx).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            c = part;
        }

        string g = gpu;
        int rtxIdx = g.IndexOf("RTX ", StringComparison.OrdinalIgnoreCase);
        if (rtxIdx >= 0)
        {
            var parts = g.Substring(rtxIdx).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            g = parts.Length >= 2 ? $"{parts[0]} {parts[1]}" : parts[0];
        }

        return $"{c} · {g}";
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _hw.TelemetryUpdated -= OnTelemetryUpdated;
        _offsetDebounce.Stop();
        _colorDebounce.Stop();
        _effectSpeedDebounce.Stop();
        _effectAnimationTimer.Stop();

        if (IsLiveHardware && SelectedEffectOption != null && IsAnimatedEffect(SelectedEffectOption.Mode))
        {
            try
            {
                _hw.SetKeyboardRgbEffect(
                    RgbEffectMode.Static,
                    (byte)Math.Clamp((int)Math.Round(EffectSpeed), 1, 10));
            }
            catch
            {
                // The keyboard keeps its last animated frame if the EC is already gone.
            }
        }

        _hw.Dispose();
    }
}

internal static class ElevationHelper
{
    public static bool IsAdministrator
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}

internal static class StartupHelper
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ClevoControlPanel";

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetStartup(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;
            if (enable)
            {
                string? exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(AppName, $"\"{exe}\" --tray");
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
        catch { }
    }
}
