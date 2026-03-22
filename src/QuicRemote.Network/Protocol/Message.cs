using System.Buffers.Binary;

namespace QuicRemote.Network.Protocol;

public static class MessageConstants
{
    public const ushort Magic = 0x5152;
}

[Flags]
public enum MessageFlags : byte
{
    None = 0,
    Compressed = 1 << 0,
    Encrypted = 1 << 1,
    Fragmented = 1 << 2,
    LastFragment = 1 << 3
}

public enum MessageType : byte
{
    // Session control (0x01-0x0F)
    SessionRequest = 0x01,
    SessionAccept = 0x02,
    SessionReject = 0x03,
    SessionEnd = 0x04,
    Heartbeat = 0x05,
    RoleChange = 0x06,
    ControlRequest = 0x07,
    ControlResponse = 0x08,
    PermissionGrant = 0x09,
    PermissionRevoke = 0x0A,
    DisplayConfig = 0x0B,

    // Media control (0x10-0x1F)
    VideoConfig = 0x10,
    VideoFrame = 0x11,
    AudioConfig = 0x12,
    AudioData = 0x13,
    KeyframeRequest = 0x14,

    // Input events (0x20-0x2F)
    MouseEvent = 0x20,
    KeyboardEvent = 0x21,
    ClipboardSync = 0x22,

    // File transfer (0x30-0x3F)
    FileTransferRequest = 0x30,
    FileData = 0x31,
    FileAck = 0x32,

    // Chat (0x40-0x4F)
    ChatMessage = 0x40
}

public abstract class Message
{
    public abstract MessageType Type { get; }
    public MessageFlags Flags { get; set; }
    public uint SequenceNumber { get; set; }

    public byte[] Serialize()
    {
        var payload = SerializePayload();
        var buffer = new byte[12 + payload.Length + 4];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span, MessageConstants.Magic);
        span[2] = (byte)Type;
        span[3] = (byte)Flags;
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4), SequenceNumber);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8), (uint)payload.Length);
        payload.CopyTo(span.Slice(12));

        var crc = CalculateCrc32(buffer.AsSpan(0, 12 + payload.Length));
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(12 + payload.Length), crc);

        return buffer;
    }

    protected abstract byte[] SerializePayload();

    private static uint CalculateCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320 : 0);
            }
        }
        return ~crc;
    }
}
