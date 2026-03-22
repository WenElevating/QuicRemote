using System.Text;

namespace QuicRemote.Network.Protocol;

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
        var passwordBytes = Password != null ? Encoding.UTF8.GetBytes(Password) : Array.Empty<byte>();
        var clientIdBytes = ClientId != null ? Encoding.UTF8.GetBytes(ClientId) : Array.Empty<byte>();

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
                password = Encoding.UTF8.GetString(data.Slice(offset, passwordLen));
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
                clientId = Encoding.UTF8.GetString(data.Slice(offset, clientIdLen));
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
        var errorBytes = ErrorMessage != null ? Encoding.UTF8.GetBytes(ErrorMessage) : Array.Empty<byte>();

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
                errorMessage = Encoding.UTF8.GetString(data.Slice(offset, errorLen));
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
