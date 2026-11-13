using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ClevoControlPanel.App.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ClevoControlPanel.App;

public partial class App : Application
{
    private const string SingleInstanceName = @"Local\ClevoControlPanel.SingleInstance";
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), ev.ExceptionObject.ToString());
            }
            catch { }
        };

        try
        {
            _instanceMutex = new Mutex(true, SingleInstanceName, out bool createdNew);
            if (!createdNew)
            {
                // Smoothly restore the existing instance without modal popup
                uint msg = Services.Win32TrayService.WM_SHOW_PANEL;
                Services.Win32TrayService.PostMessage((IntPtr)0xffff, msg, IntPtr.Zero, IntPtr.Zero);
                _instanceMutex?.Dispose();
                _instanceMutex = null;
                Environment.Exit(0);
                return;
            }

            ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, true);

            DispatcherUnhandledException += OnDispatcherUnhandledException;

            bool startInTray = false;
            foreach (string arg in e.Args)
            {
                if (arg.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-silent", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/tray", StringComparison.OrdinalIgnoreCase))
                {
                    startInTray = true;
                    break;
                }
            }

            var window = new MainWindow
            {
                DataContext = new MainViewModel()
            };

            MainWindow = window;

            if (!startInTray)
            {
                window.Show();
            }
        }
        catch (Exception ex)
        {
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), ex.ToString());
            }
            catch { }
            throw;
        }
    }

    internal static void ReleaseSingleInstanceLock()
    {
        if (Current is not App app)
            return;

        try
        {
            app._instanceMutex?.ReleaseMutex();
        }
        catch
        {
        }

        app._instanceMutex?.Dispose();
        app._instanceMutex = null;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        System.Windows.MessageBox.Show(
            $"应用遇到未处理的异常：\n\n{e.Exception.Message}",
            "Clevo Control Panel",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (MainWindow?.DataContext as IDisposable)?.Dispose();

        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch
        {
        }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
