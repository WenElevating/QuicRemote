using System;
using System.Buffers;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using QuicRemote.Core.Media;
using QuicRemote.Core.Session;
using QuicRemote.Network.Quic;

namespace QuicRemote.Client.Services;

/// <summary>
/// Client service that manages connection, decoding, and input sending
/// </summary>
public partial class ClientService : ObservableObject, IAsyncDisposable
{
    private QuicConnection? _connection;
    private QuicStream? _stream;
    private DecoderWrapper? _decoder;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private bool _disposed;

    // Reconnection support
    private string? _lastHost;
    private int _lastPort;
    private string? _lastPassword;
    private bool _autoReconnect = true;
    private int _reconnectAttempts;
    private int _maxReconnectAttempts = 5;
    private TimeSpan _initialReconnectDelay = TimeSpan.FromSeconds(1);
    private TimeSpan _maxReconnectDelay = TimeSpan.FromSeconds(30);

    [ObservableProperty]
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    private string _status = "Disconnected";

    [ObservableProperty]
    private int _framesPerSecond;

    [ObservableProperty]
    private float _latencyMs;

    [ObservableProperty]
    private float _decoderLatencyMs;

    [ObservableProperty]
    private int _bitrateKbps;

    [ObservableProperty]
    private int _remoteWidth;

    [ObservableProperty]
    private int _remoteHeight;

    [ObservableProperty]
    private bool _isReconnecting;

    [ObservableProperty]
    private int _reconnectAttemptCount;

    /// <summary>
    /// Event raised when a frame is decoded and ready for display
    /// </summary>
    public event EventHandler<DecodedFrame>? FrameDecoded;

    /// <summary>
    /// Event raised when an error occurs
    /// </summary>
    public event EventHandler<Exception>? ErrorOccurred;

    /// <summary>
    /// Event raised when reconnection attempt starts
    /// </summary>
    public event EventHandler<int>? Reconnecting;

    /// <summary>
    /// Event raised when reconnection succeeds
    /// </summary>
    public event EventHandler? Reconnected;

    /// <summary>
    /// Event raised when reconnection fails after max attempts
    /// </summary>
    public event EventHandler? ReconnectionFailed;

    /// <summary>
    /// Gets or sets whether automatic reconnection is enabled
    /// </summary>
    public bool AutoReconnect
    {
        get => _autoReconnect;
        set => _autoReconnect = value;
    }

    /// <summary>
    /// Gets or sets the maximum number of reconnection attempts
    /// </summary>
    public int MaxReconnectAttempts
    {
        get => _maxReconnectAttempts;
        set => _maxReconnectAttempts = Math.Max(1, value);
    }

    /// <summary>
    /// Connects to a remote host
    /// </summary>
    public async Task ConnectAsync(string host, int port, string? password = null, CancellationToken cancellationToken = default)
    {
        if (ConnectionState != ConnectionState.Disconnected)
        {
            throw new InvalidOperationException("Already connected or connecting");
        }

        try
        {
            ConnectionState = ConnectionState.Connecting;
            Status = $"Connecting to {host}:{port}...";

            // Save connection parameters for reconnection
            _lastHost = host;
            _lastPort = port;
            _lastPassword = password;
            _reconnectAttempts = 0;

            // Parse endpoint
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            if (addresses.Length == 0)
            {
                throw new Exception($"Could not resolve host: {host}");
            }

            var endpoint = new IPEndPoint(addresses[0], port);

            // Create connection
            _connection = await QuicConnection.ConnectAsync(endpoint, host, cancellationToken);

            ConnectionState = ConnectionState.Initializing;
            Status = "Initializing session...";

            // Create bidirectional stream
            _stream = await _connection.OpenStreamAsync(cancellationToken);

            // Initialize native library
            var nativeConfig = new NativeMethods.QR_Config
            {
                log_level = 1,
                max_frame_pool_size = 4
            };

            var initResult = NativeMethods.QR_Init(ref nativeConfig);
            if (initResult != 0)
            {
                throw new QuicRemoteException("Failed to initialize native library",
                    (NativeMethods.QR_Result)initResult);
            }

            // Initialize decoder
            _decoder = new DecoderWrapper();
            var decoderConfig = new DecoderConfig
            {
                Codec = NativeMethods.QR_Codec.H264,
                MaxWidth = 3840,
                MaxHeight = 2160,
                HardwareAccelerated = true
            };
            _decoder.Create(decoderConfig);

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _receiveTask = ReceiveLoopAsync(_cts.Token);

            ConnectionState = ConnectionState.Connected;
            Status = "Connected";
            IsReconnecting = false;
            ReconnectAttemptCount = 0;
        }
        catch
        {
            await DisconnectAsync();
            throw;
        }
    }

    /// <summary>
    /// Disconnects from the remote host
    /// </summary>
    public async Task DisconnectAsync()
    {
        await DisconnectAsync(userInitiated: true);
    }

    private async Task DisconnectAsync(bool userInitiated)
    {
        if (ConnectionState == ConnectionState.Disconnected)
        {
            return;
        }

        // If user didn't initiate and auto-reconnect is enabled, try to reconnect
        if (!userInitiated && _autoReconnect && _reconnectAttempts < _maxReconnectAttempts)
        {
            _ = TryReconnectAsync();
            return;
        }

        ConnectionState = ConnectionState.Disconnecting;
        Status = "Disconnecting...";
        IsReconnecting = false;
        ReconnectAttemptCount = 0;

        _cts?.Cancel();

        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        if (_stream != null)
        {
            await _stream.DisposeAsync();
            _stream = null;
        }

        if (_connection != null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        _decoder?.Dispose();
        _decoder = null;

        NativeMethods.QR_Shutdown();

        ConnectionState = ConnectionState.Disconnected;
        Status = "Disconnected";
    }

    private async Task TryReconnectAsync()
    {
        if (string.IsNullOrEmpty(_lastHost))
        {
            await DisconnectAsync(userInitiated: true);
            return;
        }

        _reconnectAttempts++;
        ReconnectAttemptCount = _reconnectAttempts;
        IsReconnecting = true;

        // Calculate delay with exponential backoff
        var delay = TimeSpan.FromMilliseconds(
            Math.Min(
                _initialReconnectDelay.TotalMilliseconds * Math.Pow(2, _reconnectAttempts - 1),
                _maxReconnectDelay.TotalMilliseconds
            )
        );

        Status = $"Connection lost. Reconnecting in {delay.TotalSeconds:F1}s (attempt {_reconnectAttempts}/{_maxReconnectAttempts})...";
        Reconnecting?.Invoke(this, _reconnectAttempts);

        await Task.Delay(delay);

        if (_disposed || ConnectionState == ConnectionState.Disconnected)
        {
            return;
        }

        try
        {
            // Clean up old connection
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            if (_stream != null)
            {
                await _stream.DisposeAsync();
                _stream = null;
            }

            if (_connection != null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            ConnectionState = ConnectionState.Connecting;
            Status = $"Reconnecting to {_lastHost}:{_lastPort}...";

            // Parse endpoint
            var addresses = await Dns.GetHostAddressesAsync(_lastHost);
            if (addresses.Length == 0)
            {
                throw new Exception($"Could not resolve host: {_lastHost}");
            }

            var endpoint = new IPEndPoint(addresses[0], _lastPort);

            // Create new connection
            _connection = await QuicConnection.ConnectAsync(endpoint, _lastHost);

            ConnectionState = ConnectionState.Initializing;
            Status = "Initializing session...";

            // Create bidirectional stream
            _stream = await _connection.OpenStreamAsync();

            // Reinitialize decoder if needed
            if (_decoder == null)
            {
                _decoder = new DecoderWrapper();
                var decoderConfig = new DecoderConfig
                {
                    Codec = NativeMethods.QR_Codec.H264,
                    MaxWidth = 3840,
                    MaxHeight = 2160,
                    HardwareAccelerated = true
                };
                _decoder.Create(decoderConfig);
            }

            _cts = new CancellationTokenSource();
            _receiveTask = ReceiveLoopAsync(_cts.Token);

            ConnectionState = ConnectionState.Connected;
            Status = "Reconnected";
            IsReconnecting = false;
            ReconnectAttemptCount = 0;
            _reconnectAttempts = 0;

            Reconnected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            if (_reconnectAttempts >= _maxReconnectAttempts)
            {
                IsReconnecting = false;
                ReconnectionFailed?.Invoke(this, EventArgs.Empty);
                await DisconnectAsync(userInitiated: true);
            }
            else
            {
                // Try again
                _ = TryReconnectAsync();
            }
        }
    }

    /// <summary>
    /// Sends an input event to the remote host
    /// </summary>
    public async Task SendInputAsync(InputEvent inputEvent, CancellationToken cancellationToken = default)
    {
        if (_stream == null || ConnectionState != ConnectionState.Connected)
        {
            return;
        }

        var buffer = SerializeInputEvent(inputEvent);
        await _stream.WriteAsync(buffer, cancellationToken);
    }

    private static byte[] SerializeInputEvent(InputEvent inputEvent)
    {
        return inputEvent.Type switch
        {
            InputEventType.MouseMove => new byte[]
            {
                (byte)InputEventType.MouseMove,
                (byte)(inputEvent.X & 0xFF),
                (byte)((inputEvent.X >> 8) & 0xFF),
                (byte)((inputEvent.X >> 16) & 0xFF),
                (byte)((inputEvent.X >> 24) & 0xFF),
                (byte)(inputEvent.Y & 0xFF),
                (byte)((inputEvent.Y >> 8) & 0xFF),
                (byte)((inputEvent.Y >> 16) & 0xFF),
                (byte)((inputEvent.Y >> 24) & 0xFF),
                (byte)(inputEvent.Absolute ? 1 : 0)
            },

            InputEventType.MouseDown or InputEventType.MouseUp => new byte[]
            {
                (byte)inputEvent.Type,
                (byte)inputEvent.MouseButton
            },

            InputEventType.MouseWheel => new byte[]
            {
                (byte)InputEventType.MouseWheel,
                (byte)(inputEvent.WheelDelta & 0xFF),
                (byte)((inputEvent.WheelDelta >> 8) & 0xFF),
                (byte)((inputEvent.WheelDelta >> 16) & 0xFF),
                (byte)((inputEvent.WheelDelta >> 24) & 0xFF),
                (byte)(inputEvent.HorizontalScroll ? 1 : 0)
            },

            InputEventType.KeyDown or InputEventType.KeyUp => new byte[]
            {
                (byte)inputEvent.Type,
                (byte)((ushort)inputEvent.KeyCode & 0xFF),
                (byte)(((ushort)inputEvent.KeyCode >> 8) & 0xFF)
            },

            _ => Array.Empty<byte>()
        };
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var frameCount = 0;
        var lastFpsUpdate = DateTime.UtcNow;

        while (!cancellationToken.IsCancellationRequested && _stream != null)
        {
            try
            {
                var result = await _stream.ReadAsync(cancellationToken);
                if (result.IsCompleted)
                {
                    // Connection closed by server, try to reconnect
                    if (_autoReconnect && !_disposed)
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(100); // Small delay before reconnect
                            if (ConnectionState == ConnectionState.Connected)
                            {
                                await DisconnectAsync(userInitiated: false);
                            }
                        }, CancellationToken.None);
                    }
                    break;
                }

                // Process received data
                ProcessReceivedData(result.Buffer, ref frameCount, ref lastFpsUpdate);

                _stream.AdvanceTo(result.Buffer.End);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Connection error, try to reconnect
                if (_autoReconnect && !_disposed && ConnectionState == ConnectionState.Connected)
                {
                    ErrorOccurred?.Invoke(this, ex);
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100);
                        if (ConnectionState == ConnectionState.Connected)
                        {
                            await DisconnectAsync(userInitiated: false);
                        }
                    }, CancellationToken.None);
                }
                break;
            }
        }
    }

    private void ProcessReceivedData(ReadOnlySequence<byte> buffer, ref int frameCount, ref DateTime lastFpsUpdate)
    {
        var reader = new SequenceReader<byte>(buffer);

        while (reader.Remaining >= 17)
        {
            // Read frame header
            var frameType = reader.TryRead(out var typeByte) ? typeByte : (byte)0;
            if (frameType != 1)
            {
                continue;
            }

            reader.TryReadLittleEndian(out int frameSize);
            reader.TryReadLittleEndian(out long timestamp);
            reader.TryRead(out var keyframeByte);
            reader.TryReadLittleEndian(out int frameNum);

            if (frameSize <= 0 || frameSize > 10 * 1024 * 1024 || reader.Remaining < frameSize)
            {
                break;
            }

            // Read frame data
            var frameData = new byte[frameSize];
            reader.TryCopyTo(frameData);
            reader.Advance(frameSize);

            // Decode frame
            if (_decoder != null)
            {
                try
                {
                    var frame = _decoder.DecodeFromData(frameData, timestamp, keyframeByte != 0);
                    if (frame != null)
                    {
                        RemoteWidth = frame.Width;
                        RemoteHeight = frame.Height;
                        FrameDecoded?.Invoke(this, frame);

                        // Update FPS
                        frameCount++;
                        var now = DateTime.UtcNow;
                        if ((now - lastFpsUpdate).TotalSeconds >= 1.0)
                        {
                            FramesPerSecond = frameCount;
                            frameCount = 0;
                            lastFpsUpdate = now;

                            _decoder.GetStats(out var fps, out var latency);
                            DecoderLatencyMs = latency;
                        }
                    }
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, ex);
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await DisconnectAsync();
        _cts?.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
