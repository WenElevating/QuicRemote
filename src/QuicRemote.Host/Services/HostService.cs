using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using QuicRemote.Core.Media;
using QuicRemote.Core.Session;
using QuicRemote.Network.Quic;

namespace QuicRemote.Host.Services;

/// <summary>
/// Host service that manages screen capture, encoding, and client connections
/// </summary>
public partial class HostService : ObservableObject, IAsyncDisposable
{
    private readonly CaptureWrapper _capture = new();
    private readonly InputWrapper _input = new();
    private readonly List<QuicConnection> _clients = new();
    private readonly List<QuicStream> _streams = new();

    private EncoderWrapper? _encoder;
    private QuicListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private bool _disposed;
    private EncoderConfig _encoderConfig = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private int _clientCount;

    [ObservableProperty]
    private string _status = "Stopped";

    [ObservableProperty]
    private int _framesPerSecond;

    [ObservableProperty]
    private float _encoderLatencyMs;

    [ObservableProperty]
    private int _bitrateKbps;

    /// <summary>
    /// Gets available monitors
    /// </summary>
    public IReadOnlyList<MonitorInfo> Monitors => CaptureWrapper.GetAllMonitors();

    /// <summary>
    /// Event raised when a client connects
    /// </summary>
    public event EventHandler<string>? ClientConnected;

    /// <summary>
    /// Event raised when a client disconnects
    /// </summary>
    public event EventHandler<string>? ClientDisconnected;

    /// <summary>
    /// Event raised when an error occurs
    /// </summary>
    public event EventHandler<Exception>? ErrorOccurred;

    /// <summary>
    /// Starts the host service
    /// </summary>
    public async Task StartAsync(int port, int monitorIndex, EncoderConfig config, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Service is already running");
        }

        try
        {
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

            // Initialize input
            _input.Initialize();

            // Start capture
            _capture.StartCapture(monitorIndex);

            // Initialize encoder
            var monitors = CaptureWrapper.GetAllMonitors();
            if (monitorIndex >= 0 && monitorIndex < monitors.Count)
            {
                _encoderConfig = config;
                _encoderConfig.Width = monitors[monitorIndex].Width;
                _encoderConfig.Height = monitors[monitorIndex].Height;
            }
            _encoder = new EncoderWrapper();
            _encoder.Create(_encoderConfig);

            // Start QUIC listener
            var endpoint = new IPEndPoint(IPAddress.Any, port);
            _listener = await QuicListener.CreateAsync(endpoint, null, cancellationToken);

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Start accept loop
            _ = AcceptClientsAsync(_cts.Token);

            // Start capture loop
            _captureTask = CaptureLoopAsync(_cts.Token);

            IsRunning = true;
            Status = $"Listening on port {port}";
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    /// <summary>
    /// Stops the host service
    /// </summary>
    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        Status = "Stopping...";
        _cts?.Cancel();

        // Stop capture
        _capture.StopCapture();

        // Close all streams and clients
        foreach (var stream in _streams)
        {
            await stream.DisposeAsync();
        }
        _streams.Clear();

        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }
        _clients.Clear();
        ClientCount = 0;

        // Stop listener
        if (_listener != null)
        {
            await _listener.DisposeAsync();
            _listener = null;
        }

        // Dispose encoder
        _encoder?.Dispose();
        _encoder = null;

        // Shutdown native
        NativeMethods.QR_Shutdown();

        IsRunning = false;
        Status = "Stopped";
    }

    /// <summary>
    /// Injects an input event from remote client
    /// </summary>
    public void InjectInput(InputEvent inputEvent)
    {
        if (!IsRunning)
        {
            return;
        }

        try
        {
            switch (inputEvent.Type)
            {
                case InputEventType.MouseMove:
                    _input.MouseMove(inputEvent.X, inputEvent.Y, inputEvent.Absolute);
                    break;

                case InputEventType.MouseDown:
                    _input.MouseDown(inputEvent.MouseButton);
                    break;

                case InputEventType.MouseUp:
                    _input.MouseUp(inputEvent.MouseButton);
                    break;

                case InputEventType.MouseWheel:
                    _input.MouseWheel(inputEvent.WheelDelta, inputEvent.HorizontalScroll);
                    break;

                case InputEventType.KeyDown:
                    _input.KeyDown(inputEvent.KeyCode);
                    break;

                case InputEventType.KeyUp:
                    _input.KeyUp(inputEvent.KeyCode);
                    break;
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }
    }

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            try
            {
                var connection = await _listener.AcceptConnectionAsync(cancellationToken);
                if (connection != null)
                {
                    _clients.Add(connection);
                    ClientCount = _clients.Count;

                    // Handle client in background
                    _ = HandleClientAsync(connection, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }
    }

    private async Task HandleClientAsync(QuicConnection connection, CancellationToken cancellationToken)
    {
        var clientId = connection.RemoteEndPoint?.ToString() ?? "Unknown";
        ClientConnected?.Invoke(this, clientId);

        QuicStream? stream = null;
        try
        {
            // Accept bidirectional stream for data transfer
            stream = await connection.AcceptStreamAsync(cancellationToken);
            if (stream != null)
            {
                _streams.Add(stream);

                // Start receiving input events
                await ReceiveInputAsync(stream, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }
        finally
        {
            if (stream != null)
            {
                _streams.Remove(stream);
                await stream.DisposeAsync();
            }

            _clients.Remove(connection);
            ClientCount = _clients.Count;
            ClientDisconnected?.Invoke(this, clientId);
            await connection.DisposeAsync();
        }
    }

    private async Task ReceiveInputAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await stream.ReadAsync(cancellationToken);
                if (result.IsCompleted || result.Buffer.IsEmpty)
                {
                    break;
                }

                // Parse input events from buffer
                foreach (var segment in result.Buffer)
                {
                    ParseAndInjectInput(segment.Span);
                }

                stream.AdvanceTo(result.Buffer.End);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }
    }

    private void ParseAndInjectInput(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return;
        }

        var type = (InputEventType)data[0];

        switch (type)
        {
            case InputEventType.MouseMove:
                if (data.Length >= 10)
                {
                    var inputEvent = new InputEvent
                    {
                        Type = InputEventType.MouseMove,
                        X = BitConverter.ToInt32(data.Slice(1, 4)),
                        Y = BitConverter.ToInt32(data.Slice(5, 4)),
                        Absolute = data[9] != 0
                    };
                    InjectInput(inputEvent);
                }
                break;

            case InputEventType.MouseDown:
            case InputEventType.MouseUp:
                if (data.Length >= 2)
                {
                    var inputEvent = new InputEvent
                    {
                        Type = type,
                        MouseButton = (MouseButton)data[1]
                    };
                    InjectInput(inputEvent);
                }
                break;

            case InputEventType.MouseWheel:
                if (data.Length >= 6)
                {
                    var inputEvent = new InputEvent
                    {
                        Type = InputEventType.MouseWheel,
                        WheelDelta = BitConverter.ToInt32(data.Slice(1, 4)),
                        HorizontalScroll = data[5] != 0
                    };
                    InjectInput(inputEvent);
                }
                break;

            case InputEventType.KeyDown:
            case InputEventType.KeyUp:
                if (data.Length >= 3)
                {
                    var inputEvent = new InputEvent
                    {
                        Type = type,
                        KeyCode = (KeyCode)BitConverter.ToUInt16(data.Slice(1, 2))
                    };
                    InjectInput(inputEvent);
                }
                break;
        }
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        var frameCount = 0;
        var lastFpsUpdate = DateTime.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Get frame from capture
                using var frame = _capture.GetFrame(16);
                if (frame == null)
                {
                    continue;
                }

                // Encode frame
                if (_encoder != null)
                {
                    using var packet = _encoder.Encode(frame);
                    if (packet != null)
                    {
                        // Send to all connected clients
                        await BroadcastFrameAsync(packet, cancellationToken);

                        // Update stats
                        frameCount++;
                        var now = DateTime.UtcNow;
                        if ((now - lastFpsUpdate).TotalSeconds >= 1.0)
                        {
                            FramesPerSecond = frameCount;
                            frameCount = 0;
                            lastFpsUpdate = now;

                            // Update encoder stats
                            var stats = _encoder.GetStats();
                            BitrateKbps = stats.Bitrate;
                            EncoderLatencyMs = stats.LatencyMs;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }
    }

    private async Task BroadcastFrameAsync(Packet packet, CancellationToken cancellationToken)
    {
        if (_streams.Count == 0)
        {
            return;
        }

        // Create frame header
        var header = new byte[17];
        header[0] = 1; // Frame type
        BitConverter.TryWriteBytes(header.AsSpan(1, 4), packet.Size);
        BitConverter.TryWriteBytes(header.AsSpan(5, 8), packet.TimestampUs);
        header[13] = (byte)(packet.IsKeyframe ? 1 : 0);
        BitConverter.TryWriteBytes(header.AsSpan(14, 4), packet.FrameNum);

        // Copy packet data
        byte[]? frameData = packet.GetDataCopy();

        // Send to all streams
        var tasks = new List<Task>();
        foreach (var stream in _streams)
        {
            tasks.Add(SendFrameToStreamAsync(stream, header, frameData, cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    private async Task SendFrameToStreamAsync(QuicStream stream, byte[] header, byte[]? frameData, CancellationToken cancellationToken)
    {
        try
        {
            await stream.WriteAsync(header, cancellationToken);

            if (frameData != null)
            {
                await stream.WriteAsync(frameData, cancellationToken);
            }
        }
        catch
        {
            // Stream may be closed, will be cleaned up in receive loop
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync();
        _capture.Dispose();
        _cts?.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
