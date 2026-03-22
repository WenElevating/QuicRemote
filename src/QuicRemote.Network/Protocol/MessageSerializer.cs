using System.Buffers.Binary;

namespace QuicRemote.Network.Protocol;

public static class MessageSerializer
{
    public static MessageDeserializeResult Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
        {
            return MessageDeserializeResult.InsufficientData(12);
        }

        var magic = BinaryPrimitives.ReadUInt16BigEndian(data);
        if (magic != MessageConstants.Magic)
        {
            return MessageDeserializeResult.WithError("Invalid magic number");
        }

        var type = (MessageType)data[2];
        var flags = (MessageFlags)data[3];
        var sequenceNumber = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4));
        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8));

        var totalLength = 12 + (int)payloadLength + 4;
        if (data.Length < totalLength)
        {
            return MessageDeserializeResult.InsufficientData(totalLength);
        }

        var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(12 + (int)payloadLength));
        var actualCrc = CalculateCrc32(data.Slice(0, 12 + (int)payloadLength));
        if (expectedCrc != actualCrc)
        {
            return MessageDeserializeResult.WithError("CRC32 mismatch");
        }

        var payload = data.Slice(12, (int)payloadLength).ToArray();
        var message = CreateMessage(type, payload);
        if (message == null)
        {
            return MessageDeserializeResult.WithError($"Unknown message type: {type}");
        }

        message.Flags = flags;
        message.SequenceNumber = sequenceNumber;

        return MessageDeserializeResult.Ok(message, totalLength);
    }

    private static Message? CreateMessage(MessageType type, byte[] payload)
    {
        return type switch
        {
            MessageType.SessionRequest => SessionRequestMessage.Deserialize(payload),
            MessageType.Heartbeat => HeartbeatMessage.Deserialize(payload),
            MessageType.MouseEvent => MouseEventMessage.Deserialize(payload),
            MessageType.KeyboardEvent => KeyboardEventMessage.Deserialize(payload),
            MessageType.RoleChange => RoleChangeMessage.Deserialize(payload),
            MessageType.ControlRequest => ControlRequestMessage.Deserialize(payload),
            MessageType.ControlResponse => ControlResponseMessage.Deserialize(payload),
            MessageType.PermissionGrant => PermissionGrantMessage.Deserialize(payload),
            MessageType.PermissionRevoke => PermissionRevokeMessage.Deserialize(payload),
            MessageType.DisplayConfig => DisplayConfigMessage.Deserialize(payload),
            MessageType.KeyframeRequest => KeyframeRequestMessage.Deserialize(payload),
            _ => null
        };
    }

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

public readonly struct MessageDeserializeResult
{
    public bool Success { get; }
    public Message? Message { get; }
    public int BytesConsumed { get; }
    public int BytesRequired { get; }
    public string? Error { get; }

    private MessageDeserializeResult(bool success, Message? message, int bytesConsumed, int bytesRequired, string? error)
    {
        Success = success;
        Message = message;
        BytesConsumed = bytesConsumed;
        BytesRequired = bytesRequired;
        Error = error;
    }

    public static MessageDeserializeResult Ok(Message message, int bytesConsumed)
        => new(true, message, bytesConsumed, 0, null);

    public static MessageDeserializeResult InsufficientData(int bytesRequired)
        => new(false, null, 0, bytesRequired, null);

    public static MessageDeserializeResult WithError(string error)
        => new(false, null, 0, 0, error);
}
