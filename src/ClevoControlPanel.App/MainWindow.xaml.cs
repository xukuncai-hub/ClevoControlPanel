using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ClevoControlPanel.App.Services;
using ClevoControlPanel.App.ViewModels;
using ClevoControlPanel.Core.Models;
using Wpf.Ui.Controls;

namespace ClevoControlPanel.App;

public partial class MainWindow : FluentWindow
{
    private Win32TrayService? _trayService;
    private TrayFlyoutWindow? _trayFlyout;

    public MainWindow()
    {
        InitializeComponent();

        _trayFlyout = new TrayFlyoutWindow(
            onOpenMain: () => _trayService?.RestoreAndActivate(),
            onExitApp: () =>
            {
                if (ViewModel != null)
                    ViewModel.MinimizeToTrayOnClose = false;
                _trayFlyout?.ForceClose();
                System.Windows.Application.Current.Shutdown();
            });

        _trayService = new Win32TrayService(
            this,
            onTrayRequested: (x, y) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (_trayFlyout != null)
                    {
                        _trayFlyout.DataContext = DataContext;
                        _trayFlyout.ShowAtCursor(x, y);
                    }
                });
            },
            contextMenuProvider: CreateTrayContextMenu);

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.TelemetryRefreshed += OnTelemetryRefreshed;
        }
    }

    private void OnTelemetryRefreshed()
    {
        var vm = ViewModel;
        if (vm == null) return;
        _trayService?.UpdateTooltip($"Clevo Control Panel\n{vm.CpuSummary}\n{vm.GpuSummary}\n{vm.PowerModeDisplay}模式");
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private System.Windows.Controls.ContextMenu CreateTrayContextMenu()
    {
        var vm = ViewModel;
        var menu = new System.Windows.Controls.ContextMenu();

        var headerItem = new System.Windows.Controls.MenuItem
        {
            Header = $"Clevo Control Panel ({(vm?.IsLiveHardware == true ? "实机模式" : "模拟模式")})",
            IsEnabled = false
        };
        menu.Items.Add(headerItem);
        menu.Items.Add(new Separator());

        var showItem = new System.Windows.Controls.MenuItem { Header = "显示主界面" };
        showItem.Click += (_, _) => _trayService?.RestoreAndActivate();
        menu.Items.Add(showItem);

        menu.Items.Add(new Separator());

        // Quick Power Modes
        var powerMenu = new System.Windows.Controls.MenuItem { Header = "性能模式" };
        if (vm != null)
        {
            foreach (var opt in vm.PowerModeOptions)
            {
                var pItem = new System.Windows.Controls.MenuItem
                {
                    Header = opt.Title,
                    IsChecked = vm.SelectedPowerModeOption?.Mode == opt.Mode
                };
                var mode = opt.Mode;
                pItem.Click += (_, _) =>
                {
                    if (vm.IsAutoMode) vm.IsAutoMode = false;
                    var match = vm.PowerModeOptions.FindOption(mode);
                    if (match != null) vm.SelectedPowerModeOption = match;
                };
                powerMenu.Items.Add(pItem);
            }
        }
        menu.Items.Add(powerMenu);

        // Quick Fan Modes
        var fanMenu = new System.Windows.Controls.MenuItem { Header = "风扇策略" };
        if (vm != null)
        {
            foreach (var opt in vm.FanModeOptions)
            {
                var fItem = new System.Windows.Controls.MenuItem
                {
                    Header = opt.Title,
                    IsChecked = vm.SelectedFanModeOption?.Mode == opt.Mode
                };
                var fMode = opt.Mode;
                fItem.Click += (_, _) =>
                {
                    var match = vm.FanModeOptions.FindOption(fMode);
                    if (match != null) vm.SelectedFanModeOption = match;
                };
                fanMenu.Items.Add(fItem);
            }
        }
        menu.Items.Add(fanMenu);

        menu.Items.Add(new Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "彻底退出" };
        exitItem.Click += (_, _) =>
        {
            if (vm != null)
                vm.MinimizeToTrayOnClose = false;
            System.Windows.Application.Current.Shutdown();
        };
        menu.Items.Add(exitItem);

        return menu;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (ViewModel?.MinimizeToTrayOnClose == true)
        {
            e.Cancel = true;
            Hide();
            if (ViewModel != null)
            {
                _trayService?.UpdateTooltip($"Clevo Control Panel (后台运行中)\n{ViewModel.CpuSummary}\n{ViewModel.PowerModeDisplay}模式");
            }
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _trayFlyout?.ForceClose();
        _trayFlyout = null;
        _trayService?.Dispose();
        _trayService = null;
        ViewModel?.Dispose();
    }
}
