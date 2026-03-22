using System;
using System.Threading;
using System.Threading.Tasks;
using QuicRemote.Core.Media;

namespace QuicRemote.Core.Session;

/// <summary>
/// Protocol version for compatibility between client and host
/// </summary>
public static class ProtocolVersion
{
    /// <summary>
    /// Current protocol version
    /// </summary>
    public const int Current = 1;

    /// <summary>
    /// Minimum supported protocol version
    /// </summary>
    public const int Minimum = 1;
}

/// <summary>
/// Manages a remote desktop session with media pipeline
/// </summary>
public class SessionContext : IAsyncDisposable
{
    private readonly SessionInfo _sessionInfo;
    private readonly CaptureWrapper? _capture;
    private readonly EncoderWrapper? _encoder;
    private readonly DecoderWrapper? _decoder;
    private readonly InputWrapper? _input;

    private bool _disposed;
    private bool _running;
    private CancellationTokenSource? _pipelineCts;

    /// <summary>
    /// Gets the session information
    /// </summary>
    public SessionInfo SessionInfo => _sessionInfo;

    /// <summary>
    /// Gets whether this is a host (controlled) session
    /// </summary>
    public bool IsHost { get; }

    /// <summary>
    /// Event raised when a frame is encoded (host mode)
    /// </summary>
    public event EventHandler<Packet>? FrameEncoded;

    /// <summary>
    /// Event raised when a frame is decoded (client mode)
    /// </summary>
    public event EventHandler<DecodedFrame>? FrameDecoded;

    /// <summary>
    /// Creates a host session context (screen capture + encoding)
    /// </summary>
    public SessionContext(string deviceId, bool isHost, EncoderConfig? encoderConfig = null)
    {
        _sessionInfo = new SessionInfo
        {
            SessionId = Guid.NewGuid(),
            DeviceId = deviceId,
            CreatedAt = DateTime.UtcNow,
            State = SessionState.Idle
        };

        IsHost = isHost;

        if (isHost)
        {
            _capture = new CaptureWrapper();
            _encoder = new EncoderWrapper();
            _input = new InputWrapper();
        }
        else
        {
            _decoder = new DecoderWrapper();
        }
    }

    /// <summary>
    /// Starts the media pipeline
    /// </summary>
    public async Task StartAsync(int monitorIndex = 0, EncoderConfig? config = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_running)
        {
            throw new InvalidOperationException("Session is already running");
        }

        _pipelineCts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _pipelineCts.Token);

        _sessionInfo.State = SessionState.Connecting;

        try
        {
            if (IsHost)
            {
                await StartHostPipelineAsync(monitorIndex, config ?? new EncoderConfig(), linkedCts.Token);
            }
            else
            {
                await StartClientPipelineAsync(config, linkedCts.Token);
            }

            _running = true;
            _sessionInfo.State = SessionState.Active;
        }
        catch
        {
            _sessionInfo.State = SessionState.Failed;
            throw;
        }
    }

    /// <summary>
    /// Stops the media pipeline
    /// </summary>
    public async Task StopAsync()
    {
        if (!_running)
        {
            return;
        }

        _pipelineCts?.Cancel();
        _running = false;

        if (IsHost)
        {
            _capture?.StopCapture();
        }

        _sessionInfo.State = SessionState.Disconnected;
        await Task.CompletedTask;
    }

    /// <summary>
    /// Processes an incoming encoded packet (client mode)
    /// </summary>
    public void ProcessPacket(Packet packet)
    {
        if (IsHost || _decoder == null)
        {
            throw new InvalidOperationException("ProcessPacket is only available in client mode");
        }

        try
        {
            var frame = _decoder.Decode(packet);
            if (frame != null)
            {
                FrameDecoded?.Invoke(this, frame);
            }
        }
        catch (Exception ex)
        {
            // Request keyframe on decode error
            _decoder.Reset();
            throw new QuicRemoteException("Decode failed, requesting keyframe: " + ex.Message, NativeMethods.QR_Result.QR_Error_DecoderDecodeFailed);
        }
    }

    /// <summary>
    /// Injects input (host mode)
    /// </summary>
    public void InjectInput(InputEvent inputEvent)
    {
        if (!IsHost || _input == null)
        {
            throw new InvalidOperationException("InjectInput is only available in host mode");
        }

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

    /// <summary>
    /// Requests a keyframe (host mode)
    /// </summary>
    public void RequestKeyframe()
    {
        if (!IsHost || _encoder == null)
        {
            throw new InvalidOperationException("RequestKeyframe is only available in host mode");
        }

        _encoder.RequestKeyframe();
    }

    /// <summary>
    /// Reconfigures the encoder (host mode)
    /// </summary>
    public void ReconfigureEncoder(int bitrateKbps)
    {
        if (!IsHost || _encoder == null)
        {
            throw new InvalidOperationException("ReconfigureEncoder is only available in host mode");
        }

        _encoder.Reconfigure(bitrateKbps);
    }

    private Task StartHostPipelineAsync(int monitorIndex, EncoderConfig config, CancellationToken cancellationToken)
    {
        // Initialize capture
        _capture!.StartCapture(monitorIndex);

        // Get monitor info for encoder config
        var monitors = CaptureWrapper.GetAllMonitors();
        if (monitorIndex < 0 || monitorIndex >= monitors.Count)
        {
            throw new ArgumentException($"Invalid monitor index: {monitorIndex}");
        }

        var monitor = monitors[monitorIndex];
        config.Width = monitor.Width;
        config.Height = monitor.Height;

        // Initialize encoder
        _encoder!.Create(config);

        // Initialize input
        _input!.Initialize();

        // Start capture loop
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var frame = _capture.GetFrame(100);
                    if (frame != null)
                    {
                        var packet = _encoder.Encode(frame);
                        if (packet != null)
                        {
                            FrameEncoded?.Invoke(this, packet);
                        }
                        frame.Dispose();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Log and continue
                }

                await Task.Delay(1, cancellationToken);
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    private Task StartClientPipelineAsync(EncoderConfig? config, CancellationToken cancellationToken)
    {
        // Initialize decoder
        var decoderConfig = new DecoderConfig
        {
            Codec = config?.Codec ?? NativeMethods.QR_Codec.H264,
            MaxWidth = config?.Width ?? 1920,
            MaxHeight = config?.Height ?? 1080,
            HardwareAccelerated = true
        };

        _decoder!.Create(decoderConfig);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_running)
        {
            await StopAsync();
        }

        _pipelineCts?.Dispose();
        _capture?.Dispose();
        _encoder?.Dispose();
        _decoder?.Dispose();
        _input?.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Input event for remote injection
/// </summary>
public class InputEvent
{
    public InputEventType Type { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public bool Absolute { get; init; }
    public MouseButton MouseButton { get; init; }
    public int WheelDelta { get; init; }
    public bool HorizontalScroll { get; init; }
    public KeyCode KeyCode { get; init; }
}

/// <summary>
/// Input event type
/// </summary>
public enum InputEventType
{
    MouseMove,
    MouseDown,
    MouseUp,
    MouseWheel,
    KeyDown,
    KeyUp
}
