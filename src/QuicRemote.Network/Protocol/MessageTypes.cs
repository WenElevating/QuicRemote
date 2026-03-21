using System.Buffers.Binary;
using System.Text;

namespace QuicRemote.Network.Protocol;

public class SessionRequestMessage : Message
{
    public override MessageType Type => MessageType.SessionRequest;
    public string DeviceId { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public byte[] Nonce { get; set; } = Array.Empty<byte>();

    protected override byte[] SerializePayload()
    {
        var deviceIdBytes = Encoding.UTF8.GetBytes(DeviceId);
        var buffer = new byte[8 + 4 + deviceIdBytes.Length + 4 + Nonce.Length];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteInt64BigEndian(span, Timestamp);
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(8), deviceIdBytes.Length);
        deviceIdBytes.CopyTo(span.Slice(12));
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(12 + deviceIdBytes.Length), Nonce.Length);
        Nonce.CopyTo(span.Slice(16 + deviceIdBytes.Length));

        return buffer;
    }

    public static SessionRequestMessage Deserialize(byte[] payload)
    {
        if (payload.Length < 12)
            throw new ArgumentException("Payload too short for SessionRequestMessage");

        var message = new SessionRequestMessage();
        var span = payload.AsSpan();

        message.Timestamp = BinaryPrimitives.ReadInt64BigEndian(span);

        var deviceIdLength = BinaryPrimitives.ReadInt32BigEndian(span.Slice(8));
        if (payload.Length < 12 + deviceIdLength + 4)
            throw new ArgumentException("Invalid device ID length");

        message.DeviceId = Encoding.UTF8.GetString(span.Slice(12, deviceIdLength));

        var nonceOffset = 12 + deviceIdLength;
        var nonceLength = BinaryPrimitives.ReadInt32BigEndian(span.Slice(nonceOffset));
        if (payload.Length < nonceOffset + 4 + nonceLength)
            throw new ArgumentException("Invalid nonce length");

        message.Nonce = span.Slice(nonceOffset + 4, nonceLength).ToArray();

        return message;
    }
}

public class HeartbeatMessage : Message
{
    public override MessageType Type => MessageType.Heartbeat;
    public long Timestamp { get; set; }

    protected override byte[] SerializePayload()
    {
        var buffer = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, Timestamp);
        return buffer;
    }

    public static HeartbeatMessage Deserialize(byte[] payload)
    {
        if (payload.Length < 8)
            throw new ArgumentException("Payload too short for HeartbeatMessage");

        return new HeartbeatMessage
        {
            Timestamp = BinaryPrimitives.ReadInt64BigEndian(payload)
        };
    }
}

public class MouseEventMessage : Message
{
    public override MessageType Type => MessageType.MouseEvent;
    public int X { get; set; }
    public int Y { get; set; }
    public MouseButton Button { get; set; }
    public MouseAction Action { get; set; }
    public int Delta { get; set; }

    protected override byte[] SerializePayload()
    {
        var buffer = new byte[16];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteInt32BigEndian(span, X);
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(4), Y);
        span[8] = (byte)Button;
        span[9] = (byte)Action;
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(12), Delta);

        return buffer;
    }

    public static MouseEventMessage Deserialize(byte[] payload)
    {
        if (payload.Length < 16)
            throw new ArgumentException("Payload too short for MouseEventMessage");

        var span = payload.AsSpan();
        return new MouseEventMessage
        {
            X = BinaryPrimitives.ReadInt32BigEndian(span),
            Y = BinaryPrimitives.ReadInt32BigEndian(span.Slice(4)),
            Button = (MouseButton)span[8],
            Action = (MouseAction)span[9],
            Delta = BinaryPrimitives.ReadInt32BigEndian(span.Slice(12))
        };
    }
}

public class KeyboardEventMessage : Message
{
    public override MessageType Type => MessageType.KeyboardEvent;
    public ushort KeyCode { get; set; }
    public KeyAction Action { get; set; }
    public bool Shift { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }

    protected override byte[] SerializePayload()
    {
        var buffer = new byte[8];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span, KeyCode);
        span[2] = (byte)Action;
        span[3] = (byte)((Shift ? 1 : 0) | (Ctrl ? 2 : 0) | (Alt ? 4 : 0));

        return buffer;
    }

    public static KeyboardEventMessage Deserialize(byte[] payload)
    {
        if (payload.Length < 8)
            throw new ArgumentException("Payload too short for KeyboardEventMessage");

        var span = payload.AsSpan();
        var flags = span[3];
        return new KeyboardEventMessage
        {
            KeyCode = BinaryPrimitives.ReadUInt16BigEndian(span),
            Action = (KeyAction)span[2],
            Shift = (flags & 1) != 0,
            Ctrl = (flags & 2) != 0,
            Alt = (flags & 4) != 0
        };
    }
}

public enum MouseButton : byte
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 3
}

public enum MouseAction : byte
{
    Move = 0,
    Press = 1,
    Release = 2,
    Wheel = 3
}

public enum KeyAction : byte
{
    Press = 0,
    Release = 1
}
