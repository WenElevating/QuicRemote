using System.Windows;
using QuicRemote.Host.Services;

namespace QuicRemote.Host;

public partial class App : Application
{
    private TrayIconService? _trayIcon;
    private MainWindow? _mainWindow;
    private SettingsService? _settingsService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize settings service
        _settingsService = new SettingsService();

        // Initialize tray icon with auto-start state
        var autoStartEnabled = SettingsService.IsAutoStartEnabled();
        _trayIcon = new TrayIconService();
        _trayIcon.Initialize(autoStartEnabled);
        _trayIcon.ShowWindowRequested += OnShowWindowRequested;
        _trayIcon.ExitRequested += OnExitRequested;
        _trayIcon.StartStopRequested += OnStartStopRequested;
        _trayIcon.AutoStartChanged += OnAutoStartChanged;
    }

    internal void SetMainWindow(MainWindow window)
    {
        _mainWindow = window;
    }

    private void OnShowWindowRequested(object? sender, System.EventArgs e)
    {
        if (_mainWindow != null)
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }
    }

    private void OnExitRequested(object? sender, System.EventArgs e)
    {
        _mainWindow?.CloseForReal();
        Shutdown();
    }

    private void OnStartStopRequested(object? sender, System.EventArgs e)
    {
        if (_mainWindow?.DataContext is ViewModels.MainViewModel vm)
        {
            if (vm.IsRunning)
            {
                vm.StopCommand.Execute(null);
            }
            else
            {
                vm.StartCommand.Execute(null);
            }
        }
    }

    private void OnAutoStartChanged(object? sender, bool autoStart)
    {
        _settingsService?.UpdateAutoStart(autoStart);
    }

    public void UpdateTrayStatus(string status, bool isRunning, int clientCount)
    {
        _trayIcon?.UpdateStatus(status, isRunning, clientCount);
    }

    public void ShowNotification(string title, string message)
    {
        _trayIcon?.ShowNotification(title, message);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settingsService?.SaveSettingsAsync().Wait();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
