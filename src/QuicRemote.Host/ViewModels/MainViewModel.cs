using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuicRemote.Core.Media;
using QuicRemote.Host.Services;

namespace QuicRemote.Host.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly HostService _hostService;
    private readonly SettingsService _settingsService;
    private bool _isDisposed;

    [ObservableProperty]
    private string _deviceId = GenerateDeviceId();

    [ObservableProperty]
    private int _port = 4820;

    [ObservableProperty]
    private int _selectedMonitorIndex;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "Ready to start";

    [ObservableProperty]
    private string _statusColor = "#86868B";

    [ObservableProperty]
    private int _connectedClients;

    [ObservableProperty]
    private int _framesPerSecond;

    [ObservableProperty]
    private float _latency;

    [ObservableProperty]
    private bool _showStats;

    [ObservableProperty]
    private bool _showSettings;

    // Settings properties
    [ObservableProperty]
    private string _selectedCodec = "H264";

    [ObservableProperty]
    private int _bitrateKbps = 5000;

    [ObservableProperty]
    private int _framerate = 60;

    [ObservableProperty]
    private string _password = string.Empty;

    public ObservableCollection<MonitorInfo> Monitors { get; } = new();

    public string[] CodecOptions { get; } = new[] { "H264", "H265" };
    public int[] FramerateOptions { get; } = new[] { 30, 60, 120 };
    public int[] BitratePresets { get; } = new[] { 1000, 2000, 5000, 10000, 20000, 50000 };

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _hostService = new HostService();
        _hostService.ClientConnected += OnClientConnected;
        _hostService.ClientDisconnected += OnClientDisconnected;
        _hostService.ErrorOccurred += OnErrorOccurred;

        LoadMonitors();
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Settings;
        Port = settings.Port;

        // Set monitor index from settings, but clamp to available monitors
        if (settings.MonitorIndex >= 0 && settings.MonitorIndex < Monitors.Count)
        {
            SelectedMonitorIndex = settings.MonitorIndex;
        }

        // Load encoder settings
        SelectedCodec = settings.Codec;
        BitrateKbps = settings.BitrateKbps;
        Framerate = settings.Framerate;
        Password = settings.Password ?? string.Empty;
    }

    private void LoadMonitors()
    {
        Monitors.Clear();
        var monitors = CaptureWrapper.GetAllMonitors();
        foreach (var monitor in monitors)
        {
            Monitors.Add(monitor);
        }

        // Select primary monitor by default
        for (int i = 0; i < Monitors.Count; i++)
        {
            if (Monitors[i].IsPrimary)
            {
                SelectedMonitorIndex = i;
                break;
            }
        }
    }

    partial void OnPortChanged(int value)
    {
        _settingsService.UpdatePort(value);
    }

    partial void OnSelectedMonitorIndexChanged(int value)
    {
        if (value >= 0 && value < Monitors.Count)
        {
            _settingsService.UpdateMonitorIndex(value);
        }
    }

    partial void OnSelectedCodecChanged(string value)
    {
        _settingsService.UpdateCodec(value);
    }

    partial void OnBitrateKbpsChanged(int value)
    {
        _settingsService.UpdateBitrate(value);
    }

    partial void OnFramerateChanged(int value)
    {
        _settingsService.UpdateFramerate(value);
    }

    partial void OnPasswordChanged(string value)
    {
        _settingsService.UpdatePassword(string.IsNullOrEmpty(value) ? null : value);
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        ShowSettings = !ShowSettings;
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsRunning) return;

        try
        {
            var codec = SelectedCodec == "H265"
                ? NativeMethods.QR_Codec.H265
                : NativeMethods.QR_Codec.H264;

            var config = new EncoderConfig
            {
                Codec = codec,
                BitrateKbps = BitrateKbps,
                Framerate = Framerate,
                GopSize = Framerate, // 1 second GOP
                RateControl = NativeMethods.QR_RateControlMode.CBR,
                QualityPreset = 1,
                LowLatency = true,
                HardwareAccelerated = true
            };

            await _hostService.StartAsync(Port, SelectedMonitorIndex, config);

            IsRunning = true;
            StatusText = "Running";
            StatusColor = "#34C759";
            ShowStats = true;

            // Start stats update timer
            _ = UpdateStatsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        if (!IsRunning) return;

        try
        {
            await _hostService.StopAsync();

            IsRunning = false;
            StatusText = "Stopped";
            StatusColor = "#86868B";
            ShowStats = false;
            ConnectedClients = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to stop: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CopyDeviceId()
    {
        Clipboard.SetText(DeviceId);
        StatusText = "Device ID copied!";
        _ = ResetStatusTextAsync();
    }

    private async Task UpdateStatsAsync()
    {
        while (IsRunning && !_isDisposed)
        {
            FramesPerSecond = _hostService.FramesPerSecond;
            Latency = _hostService.EncoderLatencyMs;
            ConnectedClients = _hostService.ClientCount;

            await Task.Delay(500);
        }
    }

    private async Task ResetStatusTextAsync()
    {
        await Task.Delay(2000);
        if (IsRunning)
        {
            StatusText = "Running";
        }
        else
        {
            StatusText = "Ready to start";
        }
    }

    private void OnClientConnected(object? sender, string clientId)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ConnectedClients = _hostService.ClientCount;
            StatusText = $"Client connected: {clientId}";
            _ = ResetStatusTextAsync();
        });
    }

    private void OnClientDisconnected(object? sender, string clientId)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ConnectedClients = _hostService.ClientCount;
            StatusText = $"Client disconnected";
            _ = ResetStatusTextAsync();
        });
    }

    private void OnErrorOccurred(object? sender, Exception e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            StatusText = $"Error: {e.Message}";
            StatusColor = "#FF3B30";
            _ = ResetStatusTextAsync();
        });
    }

    private static string GenerateDeviceId()
    {
        var random = new Random();
        var chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var result = new char[6];
        for (int i = 0; i < 6; i++)
        {
            result[i] = chars[random.Next(chars.Length)];
        }
        return new string(result);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (IsRunning)
        {
            await _hostService.StopAsync();
        }

        await _settingsService.SaveSettingsAsync();
        await _hostService.DisposeAsync();
    }
}
