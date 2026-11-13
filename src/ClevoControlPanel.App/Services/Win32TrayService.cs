using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace ClevoControlPanel.App.Services;

public sealed class Win32TrayService : IDisposable
{
    public const int WM_USER = 0x0400;
    public const int WM_TRAY_CALLBACK = WM_USER + 2048;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const uint WM_NULL = 0x0000;

    public static readonly uint WM_SHOW_PANEL = RegisterWindowMessage("ClevoControlPanel_ShowWindow");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx(
        byte[] pbIconBits,
        uint cbIconBits,
        [MarshalAs(UnmanagedType.Bool)] bool fIcon,
        uint dwVersion,
        int cxDesired,
        int cyDesired,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private readonly Window _owner;
    private readonly Action<int, int>? _onTrayRequested;
    private readonly Func<ContextMenu?>? _contextMenuProvider;
    private IntPtr _hWnd;
    private HwndSource? _hwndSource;
    private bool _isAdded;
    private bool _ignoreNextLButtonUp;
    private IntPtr _hIcon;
    private bool _ownsIcon;

    public Win32TrayService(
        Window owner,
        Action<int, int>? onTrayRequested = null,
        Func<ContextMenu?>? contextMenuProvider = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _onTrayRequested = onTrayRequested;
        _contextMenuProvider = contextMenuProvider;

        _hWnd = new WindowInteropHelper(_owner).EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(_hWnd);
        _hwndSource?.AddHook(WndProc);

        (_hIcon, _ownsIcon) = LoadAppTrayIcon();
        AddTrayIcon("Clevo Control Panel");
    }

    private static (IntPtr Handle, bool OwnsHandle) LoadAppTrayIcon()
    {
        try
        {
            var resourceInfo = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (resourceInfo?.Stream != null)
            {
                using var ms = new MemoryStream();
                resourceInfo.Stream.CopyTo(ms);
                byte[] icoData = ms.ToArray();

                if (icoData.Length >= 22 && BitConverter.ToUInt16(icoData, 2) == 1)
                {
                    int targetW = Math.Max(16, GetSystemMetrics(SM_CXSMICON));
                    int targetH = Math.Max(16, GetSystemMetrics(SM_CYSMICON));
                    ushort count = BitConverter.ToUInt16(icoData, 4);

                    int bestOffset = -1;
                    int bestSize = 0;
                    int bestDiff = int.MaxValue;

                    for (int i = 0; i < count; i++)
                    {
                        int entryOffset = 6 + i * 16;
                        if (entryOffset + 16 > icoData.Length) break;

                        int w = icoData[entryOffset] == 0 ? 256 : icoData[entryOffset];
                        int sz = BitConverter.ToInt32(icoData, entryOffset + 8);
                        int dataOffset = BitConverter.ToInt32(icoData, entryOffset + 12);

                        if (sz <= 0 || dataOffset < 0 || dataOffset + sz > icoData.Length)
                            continue;

                        int diff = Math.Abs(w - targetW);
                        if (diff < bestDiff || (diff == bestDiff && w >= targetW))
                        {
                            bestDiff = diff;
                            bestOffset = dataOffset;
                            bestSize = sz;
                        }
                    }

                    if (bestOffset >= 0 && bestSize > 0)
                    {
                        byte[] frame = new byte[bestSize];
                        Buffer.BlockCopy(icoData, bestOffset, frame, 0, bestSize);
                        IntPtr handle = CreateIconFromResourceEx(
                            frame,
                            (uint)frame.Length,
                            true,
                            0x00030000,
                            targetW,
                            targetH,
                            0);
                        if (handle != IntPtr.Zero)
                        {
                            return (handle, true);
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return (LoadIcon(IntPtr.Zero, new IntPtr(32512)), false);
    }

    public void UpdateTooltip(string tooltip)
    {
        if (!_isAdded || _hWnd == IntPtr.Zero) return;

        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hWnd,
            uID = 1,
            uFlags = NIF_TIP,
            szTip = tooltip.Length > 120 ? tooltip.Substring(0, 120) : tooltip
        };
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private void AddTrayIcon(string tooltip)
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hWnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAY_CALLBACK,
            hIcon = _hIcon,
            szTip = tooltip
        };
        _isAdded = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    private void RemoveTrayIcon()
    {
        if (!_isAdded || _hWnd == IntPtr.Zero) return;

        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hWnd,
            uID = 1
        };
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _isAdded = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)WM_SHOW_PANEL)
        {
            _owner.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(RestoreAndActivate));
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_TRAY_CALLBACK)
        {
            int action = lParam.ToInt32();
            if (action == WM_LBUTTONDBLCLK)
            {
                _ignoreNextLButtonUp = true;
                _owner.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(RestoreAndActivate));
                handled = true;
            }
            else if (action == WM_LBUTTONUP || action == WM_RBUTTONUP)
            {
                if (action == WM_LBUTTONUP && _ignoreNextLButtonUp)
                {
                    _ignoreNextLButtonUp = false;
                    handled = true;
                    return IntPtr.Zero;
                }

                GetCursorPos(out POINT pt);
                if (_onTrayRequested != null)
                {
                    _onTrayRequested(pt.X, pt.Y);
                    handled = true;
                }
                else
                {
                    ShowContextMenu();
                    handled = true;
                }
            }
        }
        return IntPtr.Zero;
    }

    public void RestoreAndActivate()
    {
        if (!_owner.IsVisible)
            _owner.Show();

        if (_owner.WindowState == WindowState.Minimized)
            _owner.WindowState = WindowState.Normal;

        _owner.Activate();
        _owner.Focus();
        SetForegroundWindow(_hWnd);
    }

    private void ShowContextMenu()
    {
        var menu = _contextMenuProvider?.Invoke();
        if (menu == null) return;

        GetCursorPos(out POINT pt);
        SetForegroundWindow(_hWnd);

        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = pt.X;
        menu.VerticalOffset = pt.Y;

        RoutedEventHandler? closedHandler = null;
        closedHandler = (_, _) =>
        {
            menu.Closed -= closedHandler;
            PostMessage(_hWnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        };
        menu.Closed += closedHandler;

        menu.IsOpen = true;
    }

    public void Dispose()
    {
        RemoveTrayIcon();
        if (_ownsIcon && _hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
            _ownsIcon = false;
        }
        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }
}
