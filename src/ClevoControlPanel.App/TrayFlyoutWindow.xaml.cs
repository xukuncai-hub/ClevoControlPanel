using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ClevoControlPanel.App;

public partial class TrayFlyoutWindow : Window
{
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const int MDT_EFFECTIVE_DPI = 0;

    private readonly Action _onOpenMain;
    private readonly Action _onExitApp;
    private DateTime _lastDeactivatedUtc = DateTime.MinValue;
    private bool _allowClose;
    private bool _isClosed;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public TrayFlyoutWindow(Action onOpenMain, Action onExitApp)
    {
        _onOpenMain = onOpenMain ?? throw new ArgumentNullException(nameof(onOpenMain));
        _onExitApp = onExitApp ?? throw new ArgumentNullException(nameof(onExitApp));

        InitializeComponent();

        Deactivated += OnFlyoutDeactivated;
        PreviewKeyDown += OnFlyoutPreviewKeyDown;
    }

    public void ShowAtCursor(int physCursorX, int physCursorY)
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        if ((DateTime.UtcNow - _lastDeactivatedUtc).TotalMilliseconds < 220)
        {
            return;
        }

        var pt = new POINT { X = physCursorX, Y = physCursorY };
        IntPtr hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);

        var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        bool hasMonitor = hMonitor != IntPtr.Zero && GetMonitorInfo(hMonitor, ref monitorInfo);

        double scaleX = 1.0;
        double scaleY = 1.0;
        if (hMonitor != IntPtr.Zero)
        {
            try
            {
                if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0 && dpiX > 0 && dpiY > 0)
                {
                    scaleX = dpiX / 96.0;
                    scaleY = dpiY / 96.0;
                }
            }
            catch
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                scaleX = dpi.DpiScaleX;
                scaleY = dpi.DpiScaleY;
            }
        }

        RootCard.BeginAnimation(OpacityProperty, null);
        CardSlideTransform.BeginAnimation(TranslateTransform.YProperty, null);
        RootCard.Opacity = 0;

        Left = -10000;
        Top = -10000;
        Show();
        UpdateLayout();

        double dipWidth = ActualWidth > 0 ? ActualWidth : 384;
        double dipHeight = ActualHeight > 0 ? ActualHeight : 410;

        double cursorDipX = physCursorX / scaleX;
        double cursorDipY = physCursorY / scaleY;

        double workLeft = hasMonitor ? monitorInfo.rcWork.Left / scaleX : SystemParameters.WorkArea.Left;
        double workTop = hasMonitor ? monitorInfo.rcWork.Top / scaleY : SystemParameters.WorkArea.Top;
        double workRight = hasMonitor ? monitorInfo.rcWork.Right / scaleX : SystemParameters.WorkArea.Right;
        double workBottom = hasMonitor ? monitorInfo.rcWork.Bottom / scaleY : SystemParameters.WorkArea.Bottom;

        const double shadowPad = 14.0;
        const double edgeGap = 12.0;
        const double bottomOffset = 26.0; // Raise flyout to avoid overlapping the clicked tray icon / overflow area

        double targetLeft = cursorDipX - dipWidth * 0.5;
        double targetTop = cursorDipY - dipHeight + shadowPad - bottomOffset;

        double minLeft = workLeft + edgeGap - shadowPad;
        double maxLeft = workRight - dipWidth - edgeGap + shadowPad;
        double minTop = workTop + edgeGap - shadowPad;
        double maxTop = workBottom - dipHeight - bottomOffset + shadowPad;

        Left = Math.Clamp(targetLeft, minLeft, Math.Max(minLeft, maxLeft));
        Top = Math.Clamp(targetTop, minTop, Math.Max(minTop, maxTop));

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        Activate();
        if (hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(hwnd);
        }

        PlayOpenAnimation();
    }

    public void ForceClose()
    {
        _allowClose = true;
        if (!_isClosed)
        {
            _isClosed = true;
            Close();
        }
    }

    private void PlayOpenAnimation()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(150);

        var fade = new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease };
        var slide = new DoubleAnimation(10.0, 0.0, duration) { EasingFunction = ease };

        RootCard.BeginAnimation(OpacityProperty, fade);
        CardSlideTransform.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void OnFlyoutDeactivated(object? sender, EventArgs e)
    {
        _lastDeactivatedUtc = DateTime.UtcNow;
        if (IsVisible)
        {
            Hide();
        }
    }

    private void OnFlyoutPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _lastDeactivatedUtc = DateTime.UtcNow;
            Hide();
            e.Handled = true;
        }
    }

    private void OpenMain_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _onOpenMain();
    }

    private void ExitApp_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _onExitApp();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
