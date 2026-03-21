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

    /// <summary>
    /// Event raised when a frame is decoded and ready for display
    /// </summary>
    public event EventHandler<DecodedFrame>? FrameDecoded;

    /// <summary>
    /// Event raised when an error occurs
    /// </summary>
    public event EventHandler<Exception>? ErrorOccurred;

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
        if (ConnectionState == ConnectionState.Disconnected)
        {
            return;
        }

        ConnectionState = ConnectionState.Disconnecting;
        Status = "Disconnecting...";

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
                ErrorOccurred?.Invoke(this, ex);
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
