using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuicRemote.Client.Services;
using QuicRemote.Core.Session;

namespace QuicRemote.Client.ViewModels;

public partial class ConnectViewModel : ObservableObject
{
    private readonly ClientService _clientService;
    private bool _isDisposed;

    [ObservableProperty]
    private string _host = string.Empty;

    [ObservableProperty]
    private int _port = 4820;

    [ObservableProperty]
    private string _deviceId = string.Empty;

    [ObservableProperty]
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    private string _statusText = "Ready to connect";

    [ObservableProperty]
    private string _statusColor = "#86868B";

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private int _framesPerSecond;

    [ObservableProperty]
    private float _latency;

    [ObservableProperty]
    private int _remoteWidth;

    [ObservableProperty]
    private int _remoteHeight;

    public event EventHandler? Connected;
    public event EventHandler<Exception>? ConnectionFailed;

    public ConnectViewModel()
    {
        _clientService = new ClientService();
        _clientService.FrameDecoded += OnFrameDecoded;
        _clientService.ErrorOccurred += OnErrorOccurred;
    }

    partial void OnDeviceIdChanged(string value)
    {
        // Auto-format device ID (6 characters, uppercase)
        if (value.Length > 6)
        {
            DeviceId = value.Substring(0, 6).ToUpper();
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(Host) && string.IsNullOrWhiteSpace(DeviceId))
        {
            MessageBox.Show("Please enter a host address or device ID", "Input Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (IsConnecting || IsConnected) return;

        try
        {
            IsConnecting = true;
            ConnectionState = ConnectionState.Connecting;
            StatusText = "Connecting...";
            StatusColor = "#0071E3";

            var hostToUse = Host;
            if (string.IsNullOrWhiteSpace(hostToUse))
            {
                // If only device ID is provided, try to discover on local network
                // For now, just show error
                MessageBox.Show("Please enter a host address", "Host Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ResetState();
                return;
            }

            await _clientService.ConnectAsync(hostToUse, Port);

            IsConnected = true;
            ConnectionState = ConnectionState.Connected;
            StatusText = "Connected";
            StatusColor = "#34C759";

            // Start stats monitoring
            _ = UpdateStatsAsync();

            Connected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ResetState();
            ConnectionFailed?.Invoke(this, ex);
            MessageBox.Show($"Connection failed: {ex.Message}", "Connection Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (!IsConnected) return;

        try
        {
            await _clientService.DisconnectAsync();
        }
        catch
        {
            // Ignore disconnect errors
        }
        finally
        {
            ResetState();
        }
    }

    private void ResetState()
    {
        IsConnected = false;
        IsConnecting = false;
        ConnectionState = ConnectionState.Disconnected;
        StatusText = "Ready to connect";
        StatusColor = "#86868B";
        FramesPerSecond = 0;
        Latency = 0;
    }

    private async Task UpdateStatsAsync()
    {
        while (IsConnected && !_isDisposed)
        {
            FramesPerSecond = _clientService.FramesPerSecond;
            Latency = _clientService.DecoderLatencyMs;
            RemoteWidth = _clientService.RemoteWidth;
            RemoteHeight = _clientService.RemoteHeight;

            await Task.Delay(500);
        }
    }

    private void OnFrameDecoded(object? sender, Core.Media.DecodedFrame frame)
    {
        // Frame decoded event - handled by RemoteDisplay control
    }

    private void OnErrorOccurred(object? sender, Exception e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (IsConnected)
            {
                StatusText = $"Error: {e.Message}";
                StatusColor = "#FF3B30";
            }
        });
    }

    public ClientService GetClientService() => _clientService;

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (IsConnected)
        {
            await _clientService.DisconnectAsync();
        }

        await _clientService.DisposeAsync();
    }
}
