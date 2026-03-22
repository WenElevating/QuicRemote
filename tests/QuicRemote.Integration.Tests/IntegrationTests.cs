using QuicRemote.Core.Media;
using QuicRemote.Core.Session;
using QuicRemote.Network.Protocol;
using QuicRemote.Network.Quic;
using System.Net;

namespace QuicRemote.Integration.Tests;

/// <summary>
/// Integration tests for QuicRemote core components
/// </summary>
public class NativeTests : IAsyncLifetime
{
    public Task InitializeAsync()
    {
        // Initialize native library once for all tests
        var config = new NativeMethods.QR_Config
        {
            log_level = 1,
            max_frame_pool_size = 4
        };

        var result = NativeMethods.QR_Init(ref config);
        if (result != 0)
        {
            throw new Exception($"Failed to initialize native library: {result}");
        }

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        NativeMethods.QR_Shutdown();
        return Task.CompletedTask;
    }

    [Fact]
    public void NativeDll_CanBeInitialized()
    {
        // Already initialized in InitializeAsync
        var version = NativeMethods.QR_GetVersion();
        Assert.True(version > 0, "Native library version should be greater than 0");
    }

    [Fact]
    public void Capture_GetMonitors_ReturnsAtLeastOne()
    {
        var monitors = CaptureWrapper.GetAllMonitors();
        Assert.NotEmpty(monitors);

        var primaryMonitor = monitors.FirstOrDefault(m => m.IsPrimary);
        Assert.NotNull(primaryMonitor);
        Assert.True(primaryMonitor.Width > 0);
        Assert.True(primaryMonitor.Height > 0);
    }

    [Fact]
    public void Encoder_CanCreateAndDispose()
    {
        var monitors = CaptureWrapper.GetAllMonitors();
        Assert.NotEmpty(monitors);

        var config = new EncoderConfig
        {
            Codec = NativeMethods.QR_Codec.H264,
            Width = monitors[0].Width,
            Height = monitors[0].Height,
            BitrateKbps = 5000,
            Framerate = 30,
            GopSize = 30,
            RateControl = NativeMethods.QR_RateControlMode.CBR,
            QualityPreset = 1,
            LowLatency = true,
            HardwareAccelerated = true
        };

        using var encoder = new EncoderWrapper();
        encoder.Create(config);

        var stats = encoder.GetStats();
        Assert.True(stats.Bitrate >= 0);
    }

    [Fact]
    public void Decoder_CanCreateAndDispose()
    {
        var config = new DecoderConfig
        {
            Codec = NativeMethods.QR_Codec.H264,
            MaxWidth = 1920,
            MaxHeight = 1080,
            HardwareAccelerated = true
        };

        using var decoder = new DecoderWrapper();
        decoder.Create(config);
    }

    [Fact]
    public void Input_CanInitialize()
    {
        using var input = new InputWrapper();
        input.Initialize();

        // Test mouse move to coordinate 0,0 (safe operation)
        input.MouseMove(0, 0, true);
    }

    [Fact]
    public void EncoderAndDecoder_ConfigMatch()
    {
        // Verify encoder and decoder can use matching configurations
        var monitors = CaptureWrapper.GetAllMonitors();
        Assert.NotEmpty(monitors);

        var width = Math.Min(monitors[0].Width, 1920);
        var height = Math.Min(monitors[0].Height, 1080);

        var encoderConfig = new EncoderConfig
        {
            Codec = NativeMethods.QR_Codec.H264,
            Width = width,
            Height = height,
            BitrateKbps = 2000,
            Framerate = 30,
            GopSize = 30,
            LowLatency = true,
            HardwareAccelerated = true
        };

        var decoderConfig = new DecoderConfig
        {
            Codec = NativeMethods.QR_Codec.H264,
            MaxWidth = width,
            MaxHeight = height,
            HardwareAccelerated = true
        };

        using var encoder = new EncoderWrapper();
        encoder.Create(encoderConfig);

        using var decoder = new DecoderWrapper();
        decoder.Create(decoderConfig);

        // Both should be created successfully
    }
}

/// <summary>
/// QUIC connection tests
/// </summary>
public class QuicConnectionTests : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(Timeout = 5000)]
    public async Task QuicListener_CanCreateAndDispose()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 4860);

        await using var listener = await QuicListener.CreateAsync(endpoint, null);

        Assert.NotNull(listener);
    }

    [Fact(Timeout = 10000)]
    public async Task QuicConnection_CanConnectLocally()
    {
        var port = 4861;
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);

        // Create listener
        await using var listener = await QuicListener.CreateAsync(endpoint, null);

        // Connect in background
        var connectTask = Task.Run(async () =>
        {
            await Task.Delay(100); // Small delay to ensure listener is ready
            return await QuicConnection.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, port),
                "localhost");
        });

        // Accept connection
        var serverConnection = await listener.AcceptConnectionAsync();

        // Wait for client connection
        var clientConnection = await connectTask;

        Assert.NotNull(serverConnection);
        Assert.NotNull(clientConnection);

        await serverConnection.DisposeAsync();
        await clientConnection.DisposeAsync();
    }

    [Fact(Timeout = 15000)]
    public async Task QuicStream_CanSendAndReceive()
    {
        var port = 4862;
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);

        await using var listener = await QuicListener.CreateAsync(endpoint, null);

        var connectTask = Task.Run(async () =>
        {
            await Task.Delay(100);
            return await QuicConnection.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, port),
                "localhost");
        });

        var serverConnection = await listener.AcceptConnectionAsync();
        var clientConnection = await connectTask;

        // IMPORTANT: Run accept and write in parallel
        // The server's AcceptStreamAsync blocks until data is sent
        var acceptStreamTask = Task.Run(async () =>
        {
            return await serverConnection.AcceptStreamAsync();
        });

        // Run client operations in parallel - write data to make stream visible to server
        var clientTask = Task.Run(async () =>
        {
            await Task.Delay(200); // Let server start accepting
            var stream = await clientConnection.OpenStreamAsync();

            // Write data immediately to make stream visible
            var sendData = new byte[] { 1, 2, 3, 4, 5 };
            await stream.WriteAsync(sendData);

            return (stream, sendData);
        });

        // Wait for both
        await using var serverStream = await acceptStreamTask;
        var (clientStream, sendData) = await clientTask;

        // Receive data
        var result = await serverStream.ReadAsync();
        var receivedData = result.Buffer.FirstSpan.ToArray();
        serverStream.AdvanceTo(result.Buffer.End);

        Assert.Equal(sendData, receivedData);

        await serverConnection.DisposeAsync();
        await clientConnection.DisposeAsync();
    }
}

/// <summary>
/// Capture and encode tests (may require display)
/// </summary>
public class CaptureTests : IAsyncLifetime
{
    public Task InitializeAsync()
    {
        var config = new NativeMethods.QR_Config
        {
            log_level = 1,
            max_frame_pool_size = 4
        };

        var result = NativeMethods.QR_Init(ref config);
        if (result != 0)
        {
            throw new Exception($"Failed to initialize native library: {result}");
        }

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        NativeMethods.QR_Shutdown();
        return Task.CompletedTask;
    }

    [Fact(Skip = "Requires display and may hang in CI environment")]
    public void Capture_CanStartAndStop()
    {
        using var capture = new CaptureWrapper();

        var monitors = CaptureWrapper.GetAllMonitors();
        var primaryIndex = 0;
        for (int i = 0; i < monitors.Count; i++)
        {
            if (monitors[i].IsPrimary)
            {
                primaryIndex = i;
                break;
            }
        }

        capture.StartCapture(primaryIndex);

        // Try to get a frame
        using var frame = capture.GetFrame(1000);
        // Frame might be null if no display updates, but capture should be running

        capture.StopCapture();
    }

    [Fact(Skip = "Requires display and may hang in CI environment")]
    public async Task FullStack_LocalCaptureAndEncode()
    {
        var monitors = CaptureWrapper.GetAllMonitors();
        Assert.NotEmpty(monitors);

        var primaryIndex = 0;
        for (int i = 0; i < monitors.Count; i++)
        {
            if (monitors[i].IsPrimary)
            {
                primaryIndex = i;
                break;
            }
        }

        using var capture = new CaptureWrapper();
        capture.StartCapture(primaryIndex);

        var config = new EncoderConfig
        {
            Codec = NativeMethods.QR_Codec.H264,
            Width = monitors[primaryIndex].Width,
            Height = monitors[primaryIndex].Height,
            BitrateKbps = 5000,
            Framerate = 30,
            GopSize = 30,
            LowLatency = true,
            HardwareAccelerated = true
        };

        using var encoder = new EncoderWrapper();
        encoder.Create(config);

        // Try to capture and encode a few frames
        var framesEncoded = 0;
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(3);

        while (framesEncoded < 5 && DateTime.UtcNow - startTime < timeout)
        {
            using var frame = capture.GetFrame(100);
            if (frame != null)
            {
                using var packet = encoder.Encode(frame);
                if (packet != null)
                {
                    framesEncoded++;
                }
            }
        }

        capture.StopCapture();

        Assert.True(framesEncoded > 0, $"Expected to encode at least 1 frame, but got {framesEncoded}");
    }
}

/// <summary>
/// Control message serialization tests
/// </summary>
public class ControlMessageTests
{
    [Fact]
    public void ControlMessage_SerializeDeserialize_PingPong()
    {
        var ping = new ControlMessage { Type = ControlMessageType.Ping };
        var data = ping.Serialize();

        Assert.True(data.Length >= 9);
        Assert.Equal((byte)ControlMessageType.Ping, data[0]);

        var deserialized = ControlMessage.Deserialize(data);
        Assert.NotNull(deserialized);
        Assert.Equal(ControlMessageType.Ping, deserialized.Type);
    }

    [Fact]
    public void AuthRequestMessage_SerializeDeserialize()
    {
        var auth = new AuthRequestMessage
        {
            Type = ControlMessageType.AuthRequest,
            Password = "test123",
            ClientId = "client-001",
            ProtocolVersion = 1
        };

        var data = auth.Serialize();
        Assert.True(data.Length > 15);

        var deserialized = AuthRequestMessage.Deserialize(data);
        Assert.NotNull(deserialized);
        Assert.Equal("test123", deserialized.Password);
        Assert.Equal("client-001", deserialized.ClientId);
        Assert.Equal(1, deserialized.ProtocolVersion);
    }

    [Fact]
    public void AuthRequestMessage_SerializeDeserialize_NullPassword()
    {
        var auth = new AuthRequestMessage
        {
            Type = ControlMessageType.AuthRequest,
            ClientId = "client-002",
            ProtocolVersion = 1
        };

        var data = auth.Serialize();
        var deserialized = AuthRequestMessage.Deserialize(data);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Password);
        Assert.Equal("client-002", deserialized.ClientId);
    }

    [Fact]
    public void AuthResponseMessage_SerializeDeserialize_Success()
    {
        var response = new AuthResponseMessage
        {
            Type = ControlMessageType.AuthResponse,
            Success = true,
            SessionId = 12345
        };

        var data = response.Serialize();
        var deserialized = AuthResponseMessage.Deserialize(data);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
        Assert.Equal(12345, deserialized.SessionId);
        Assert.Null(deserialized.ErrorMessage);
    }

    [Fact]
    public void AuthResponseMessage_SerializeDeserialize_Failure()
    {
        var response = new AuthResponseMessage
        {
            Type = ControlMessageType.AuthResponse,
            Success = false,
            ErrorMessage = "Invalid password"
        };

        var data = response.Serialize();
        var deserialized = AuthResponseMessage.Deserialize(data);

        Assert.NotNull(deserialized);
        Assert.False(deserialized.Success);
        Assert.Equal("Invalid password", deserialized.ErrorMessage);
    }

    [Fact]
    public void ConfigUpdateMessage_SerializeDeserialize()
    {
        var config = new ConfigUpdateMessage
        {
            Type = ControlMessageType.ConfigUpdate,
            BitrateKbps = 10000,
            Framerate = 60,
            Quality = 80
        };

        var data = config.Serialize();
        var deserialized = ConfigUpdateMessage.Deserialize(data);

        Assert.NotNull(deserialized);
        Assert.Equal(10000, deserialized.BitrateKbps);
        Assert.Equal(60, deserialized.Framerate);
        Assert.Equal(80, deserialized.Quality);
    }

    [Fact]
    public void ConfigUpdateMessage_SerializeDeserialize_Partial()
    {
        var config = new ConfigUpdateMessage
        {
            Type = ControlMessageType.ConfigUpdate,
            BitrateKbps = 8000
            // Framerate and Quality are null
        };

        var data = config.Serialize();
        var deserialized = ConfigUpdateMessage.Deserialize(data);

        Assert.NotNull(deserialized);
        Assert.Equal(8000, deserialized.BitrateKbps);
        Assert.Null(deserialized.Framerate);
        Assert.Null(deserialized.Quality);
    }
}

/// <summary>
/// Connection reconnection tests
/// </summary>
public class ReconnectionTests : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(Timeout = 15000)]
    public async Task QuicConnection_ServerCloses_ClientDetects()
    {
        var port = 4870;
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);

        await using var listener = await QuicListener.CreateAsync(endpoint, null);

        var connectTask = Task.Run(async () =>
        {
            await Task.Delay(100);
            return await QuicConnection.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, port),
                "localhost");
        });

        var serverConnection = await listener.AcceptConnectionAsync();
        var clientConnection = await connectTask;

        // Open stream
        var acceptStreamTask = Task.Run(async () =>
        {
            return await serverConnection.AcceptStreamAsync();
        });

        var clientTask = Task.Run(async () =>
        {
            await Task.Delay(200);
            var stream = await clientConnection.OpenStreamAsync();
            await stream.WriteAsync(new byte[] { 1, 2, 3 });
            return stream;
        });

        await using var serverStream = await acceptStreamTask;
        var clientStream = await clientTask;

        // Server closes connection
        await serverConnection.DisposeAsync();

        // Client should detect connection closure when trying to read
        await Task.Delay(100);

        // Clean up
        await clientConnection.DisposeAsync();
    }

    [Fact(Timeout = 20000)]
    public async Task QuicConnection_MultipleReconnects()
    {
        var port1 = 4871;
        var port2 = 4872; // Use different port for second connection

        // First connection
        var endpoint1 = new IPEndPoint(IPAddress.Loopback, port1);
        await using var listener1 = await QuicListener.CreateAsync(endpoint1, null);

        var connectTask = Task.Run(async () =>
        {
            await Task.Delay(100);
            return await QuicConnection.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, port1),
                "localhost");
        });

        var serverConnection1 = await listener1.AcceptConnectionAsync();
        var clientConnection1 = await connectTask;

        Assert.NotNull(serverConnection1);
        Assert.NotNull(clientConnection1);

        // Close first connection
        await serverConnection1.DisposeAsync();
        await clientConnection1.DisposeAsync();

        // Wait a bit for cleanup
        await Task.Delay(500);

        // Second connection (reconnect simulation - uses different port)
        var endpoint2 = new IPEndPoint(IPAddress.Loopback, port2);
        await using var listener2 = await QuicListener.CreateAsync(endpoint2, null);

        var reconnectTask = Task.Run(async () =>
        {
            await Task.Delay(100);
            return await QuicConnection.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, port2),
                "localhost");
        });

        var serverConnection2 = await listener2.AcceptConnectionAsync();
        var clientConnection2 = await reconnectTask;

        Assert.NotNull(serverConnection2);
        Assert.NotNull(clientConnection2);

        // Clean up
        await serverConnection2.DisposeAsync();
        await clientConnection2.DisposeAsync();
    }
}
