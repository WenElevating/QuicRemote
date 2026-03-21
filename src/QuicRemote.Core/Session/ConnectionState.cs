namespace QuicRemote.Core.Session;

/// <summary>
/// Connection state enumeration
/// </summary>
public enum ConnectionState
{
    /// <summary>
    /// Not connected
    /// </summary>
    Disconnected,

    /// <summary>
    /// Attempting to connect
    /// </summary>
    Connecting,

    /// <summary>
    /// Connection established, initializing session
    /// </summary>
    Initializing,

    /// <summary>
    /// Connected and session active
    /// </summary>
    Connected,

    /// <summary>
    /// Connection lost, attempting to reconnect
    /// </summary>
    Reconnecting,

    /// <summary>
    /// Disconnecting
    /// </summary>
    Disconnecting,

    /// <summary>
    /// Connection failed
    /// </summary>
    Failed
}

/// <summary>
/// Connection statistics
/// </summary>
public class ConnectionStats
{
    /// <summary>
    /// Round-trip latency in milliseconds
    /// </summary>
    public double LatencyMs { get; set; }

    /// <summary>
    /// Frames per second
    /// </summary>
    public int FramesPerSecond { get; set; }

    /// <summary>
    /// Current bitrate in kbps
    /// </summary>
    public int BitrateKbps { get; set; }

    /// <summary>
    /// Total bytes sent
    /// </summary>
    public long BytesSent { get; set; }

    /// <summary>
    /// Total bytes received
    /// </summary>
    public long BytesReceived { get; set; }

    /// <summary>
    /// Packets lost
    /// </summary>
    public int PacketsLost { get; set; }

    /// <summary>
    /// Time since connection established
    /// </summary>
    public TimeSpan ConnectionDuration { get; set; }

    /// <summary>
    /// Encoder latency in milliseconds
    /// </summary>
    public float EncoderLatencyMs { get; set; }

    /// <summary>
    /// Decoder latency in milliseconds
    /// </summary>
    public float DecoderLatencyMs { get; set; }
}
