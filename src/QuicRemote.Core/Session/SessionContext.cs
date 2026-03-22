using System;
using System.Threading;
using System.Threading.Tasks;
using QuicRemote.Core.Media;

namespace QuicRemote.Core.Session;

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

/// <summary>
/// Control message type for session management
/// </summary>
public enum ControlMessageType : byte
{
    /// <summary>
    /// Ping/heartbeat request
    /// </summary>
    Ping = 0x10,

    /// <summary>
    /// Pong/heartbeat response
    /// </summary>
    Pong = 0x11,

    /// <summary>
    /// Authentication request (client -> server)
    /// </summary>
    AuthRequest = 0x20,

    /// <summary>
    /// Authentication response (server -> client)
    /// </summary>
    AuthResponse = 0x21,

    /// <summary>
    /// Session pause request
    /// </summary>
    PauseSession = 0x30,

    /// <summary>
    /// Session resume request
    /// </summary>
    ResumeSession = 0x31,

    /// <summary>
    /// Session configuration update
    /// </summary>
    ConfigUpdate = 0x40,

    /// <summary>
    /// Client capabilities
    /// </summary>
    ClientCapabilities = 0x50,

    /// <summary>
    /// Server capabilities
    /// </summary>
    ServerCapabilities = 0x51
}

/// <summary>
/// Base class for control messages
/// </summary>
public class ControlMessage
{
    public ControlMessageType Type { get; init; }
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// Serializes the control message to a byte array
    /// </summary>
    public virtual byte[] Serialize()
    {
        return new byte[]
        {
            (byte)Type,
            (byte)(Timestamp & 0xFF),
            (byte)((Timestamp >> 8) & 0xFF),
            (byte)((Timestamp >> 16) & 0xFF),
            (byte)((Timestamp >> 24) & 0xFF),
            (byte)((Timestamp >> 32) & 0xFF),
            (byte)((Timestamp >> 40) & 0xFF),
            (byte)((Timestamp >> 48) & 0xFF),
            (byte)((Timestamp >> 56) & 0xFF)
        };
    }

    /// <summary>
    /// Deserializes a control message from a byte array
    /// </summary>
    public static ControlMessage? Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 9) return null;

        var type = (ControlMessageType)data[0];
        var timestamp = BitConverter.ToInt64(data.Slice(1, 8));

        return type switch
        {
            ControlMessageType.Ping or ControlMessageType.Pong => new ControlMessage { Type = type, Timestamp = timestamp },
            ControlMessageType.AuthRequest => AuthRequestMessage.Deserialize(data),
            ControlMessageType.AuthResponse => AuthResponseMessage.Deserialize(data),
            ControlMessageType.ConfigUpdate => ConfigUpdateMessage.Deserialize(data),
            _ => new ControlMessage { Type = type, Timestamp = timestamp }
        };
    }
}

/// <summary>
/// Authentication request message
/// </summary>
public class AuthRequestMessage : ControlMessage
{
    public string? Password { get; init; }
    public string? ClientId { get; init; }
    public int ProtocolVersion { get; init; } = 1;

    public override byte[] Serialize()
    {
        var passwordBytes = Password != null ? System.Text.Encoding.UTF8.GetBytes(Password) : Array.Empty<byte>();
        var clientIdBytes = ClientId != null ? System.Text.Encoding.UTF8.GetBytes(ClientId) : Array.Empty<byte>();

        var result = new byte[9 + 4 + 2 + passwordBytes.Length + 2 + clientIdBytes.Length];
        var offset = 0;

        // Base header
        result[offset++] = (byte)ControlMessageType.AuthRequest;
        BitConverter.TryWriteBytes(result.AsSpan(offset, 8), Timestamp);
        offset += 8;

        // Protocol version
        BitConverter.TryWriteBytes(result.AsSpan(offset, 4), ProtocolVersion);
        offset += 4;

        // Password
        BitConverter.TryWriteBytes(result.AsSpan(offset, 2), (short)passwordBytes.Length);
        offset += 2;
        passwordBytes.CopyTo(result, offset);
        offset += passwordBytes.Length;

        // Client ID
        BitConverter.TryWriteBytes(result.AsSpan(offset, 2), (short)clientIdBytes.Length);
        offset += 2;
        clientIdBytes.CopyTo(result, offset);

        return result;
    }

    public static new AuthRequestMessage Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 15) return new AuthRequestMessage();

        var offset = 1; // Skip type
        var timestamp = BitConverter.ToInt64(data.Slice(offset, 8));
        offset += 8;

        var protocolVersion = BitConverter.ToInt32(data.Slice(offset, 4));
        offset += 4;

        string? password = null;
        if (offset + 2 <= data.Length)
        {
            var passwordLen = BitConverter.ToInt16(data.Slice(offset, 2));
            offset += 2;
            if (offset + passwordLen <= data.Length && passwordLen > 0)
            {
                password = System.Text.Encoding.UTF8.GetString(data.Slice(offset, passwordLen));
                offset += passwordLen;
            }
        }

        string? clientId = null;
        if (offset + 2 <= data.Length)
        {
            var clientIdLen = BitConverter.ToInt16(data.Slice(offset, 2));
            offset += 2;
            if (offset + clientIdLen <= data.Length && clientIdLen > 0)
            {
                clientId = System.Text.Encoding.UTF8.GetString(data.Slice(offset, clientIdLen));
            }
        }

        return new AuthRequestMessage
        {
            Type = ControlMessageType.AuthRequest,
            Timestamp = timestamp,
            ProtocolVersion = protocolVersion,
            Password = password,
            ClientId = clientId
        };
    }
}

/// <summary>
/// Authentication response message
/// </summary>
public class AuthResponseMessage : ControlMessage
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int SessionId { get; init; }

    public override byte[] Serialize()
    {
        var errorBytes = ErrorMessage != null ? System.Text.Encoding.UTF8.GetBytes(ErrorMessage) : Array.Empty<byte>();

        var result = new byte[9 + 1 + 4 + 2 + errorBytes.Length];
        var offset = 0;

        result[offset++] = (byte)ControlMessageType.AuthResponse;
        BitConverter.TryWriteBytes(result.AsSpan(offset, 8), Timestamp);
        offset += 8;

        result[offset++] = (byte)(Success ? 1 : 0);
        BitConverter.TryWriteBytes(result.AsSpan(offset, 4), SessionId);
        offset += 4;

        BitConverter.TryWriteBytes(result.AsSpan(offset, 2), (short)errorBytes.Length);
        offset += 2;
        errorBytes.CopyTo(result, offset);

        return result;
    }

    public static new AuthResponseMessage Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16) return new AuthResponseMessage();

        var offset = 1;
        var timestamp = BitConverter.ToInt64(data.Slice(offset, 8));
        offset += 8;

        var success = data[offset++] == 1;
        var sessionId = BitConverter.ToInt32(data.Slice(offset, 4));
        offset += 4;

        string? errorMessage = null;
        if (offset + 2 <= data.Length)
        {
            var errorLen = BitConverter.ToInt16(data.Slice(offset, 2));
            offset += 2;
            if (offset + errorLen <= data.Length && errorLen > 0)
            {
                errorMessage = System.Text.Encoding.UTF8.GetString(data.Slice(offset, errorLen));
            }
        }

        return new AuthResponseMessage
        {
            Type = ControlMessageType.AuthResponse,
            Timestamp = timestamp,
            Success = success,
            SessionId = sessionId,
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>
/// Configuration update message
/// </summary>
public class ConfigUpdateMessage : ControlMessage
{
    public int? BitrateKbps { get; init; }
    public int? Framerate { get; init; }
    public int? Quality { get; init; }

    public override byte[] Serialize()
    {
        var result = new byte[9 + 2 + 4 + 4 + 4];
        var offset = 0;

        result[offset++] = (byte)ControlMessageType.ConfigUpdate;
        BitConverter.TryWriteBytes(result.AsSpan(offset, 8), Timestamp);
        offset += 8;

        // Flags for which values are present
        var flags = (byte)((BitrateKbps.HasValue ? 1 : 0) |
                           (Framerate.HasValue ? 2 : 0) |
                           (Quality.HasValue ? 4 : 0));
        result[offset++] = flags;
        result[offset++] = 0; // Reserved

        BitConverter.TryWriteBytes(result.AsSpan(offset, 4), BitrateKbps ?? 0);
        offset += 4;
        BitConverter.TryWriteBytes(result.AsSpan(offset, 4), Framerate ?? 0);
        offset += 4;
        BitConverter.TryWriteBytes(result.AsSpan(offset, 4), Quality ?? 0);

        return result;
    }

    public static new ConfigUpdateMessage Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 23) return new ConfigUpdateMessage();

        var offset = 1;
        var timestamp = BitConverter.ToInt64(data.Slice(offset, 8));
        offset += 8;

        var flags = data[offset];
        offset += 2;

        int? bitrate = null, framerate = null, quality = null;

        if ((flags & 1) != 0) bitrate = BitConverter.ToInt32(data.Slice(offset, 4));
        offset += 4;
        if ((flags & 2) != 0) framerate = BitConverter.ToInt32(data.Slice(offset, 4));
        offset += 4;
        if ((flags & 4) != 0) quality = BitConverter.ToInt32(data.Slice(offset, 4));

        return new ConfigUpdateMessage
        {
            Type = ControlMessageType.ConfigUpdate,
            Timestamp = timestamp,
            BitrateKbps = bitrate,
            Framerate = framerate,
            Quality = quality
        };
    }
}
