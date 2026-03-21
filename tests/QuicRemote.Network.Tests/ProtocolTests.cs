using QuicRemote.Network.Protocol;
using Xunit;

namespace QuicRemote.Network.Tests;

public class ProtocolTests
{
    [Fact]
    public void SessionRequest_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new SessionRequestMessage
        {
            DeviceId = "QR-AB1234",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Nonce = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            SequenceNumber = 1
        };

        var serialized = original.Serialize();
        var result = MessageSerializer.Deserialize(serialized);

        Assert.True(result.Success);
        Assert.IsType<SessionRequestMessage>(result.Message);

        var deserialized = (SessionRequestMessage)result.Message!;
        Assert.Equal(original.DeviceId, deserialized.DeviceId);
        Assert.Equal(original.Timestamp, deserialized.Timestamp);
        Assert.Equal(original.Nonce, deserialized.Nonce);
        Assert.Equal(original.SequenceNumber, deserialized.SequenceNumber);
    }

    [Fact]
    public void Message_WithInvalidMagic_ReturnsError()
    {
        var buffer = new byte[20];

        var result = MessageSerializer.Deserialize(buffer);

        Assert.False(result.Success);
        Assert.Contains("magic", result.Error?.ToLower() ?? "");
    }

    [Fact]
    public void Message_WithCorruptedCrc_ReturnsError()
    {
        var message = new HeartbeatMessage
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        var serialized = message.Serialize();

        serialized[^1] ^= 0xFF;

        var result = MessageSerializer.Deserialize(serialized);

        Assert.False(result.Success);
        Assert.Contains("crc", result.Error?.ToLower() ?? "");
    }

    [Fact]
    public void MouseEvent_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new MouseEventMessage
        {
            X = 100,
            Y = 200,
            Button = MouseButton.Left,
            Action = MouseAction.Press,
            SequenceNumber = 42
        };

        var serialized = original.Serialize();
        var result = MessageSerializer.Deserialize(serialized);

        Assert.True(result.Success);
        var deserialized = (MouseEventMessage)result.Message!;
        Assert.Equal(original.X, deserialized.X);
        Assert.Equal(original.Y, deserialized.Y);
        Assert.Equal(original.Button, deserialized.Button);
        Assert.Equal(original.Action, deserialized.Action);
    }

    [Fact]
    public void KeyboardEvent_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new KeyboardEventMessage
        {
            KeyCode = 65,
            Action = KeyAction.Press,
            Shift = true,
            Ctrl = false,
            Alt = true,
            SequenceNumber = 100
        };

        var serialized = original.Serialize();
        var result = MessageSerializer.Deserialize(serialized);

        Assert.True(result.Success);
        var deserialized = (KeyboardEventMessage)result.Message!;
        Assert.Equal(original.KeyCode, deserialized.KeyCode);
        Assert.Equal(original.Action, deserialized.Action);
        Assert.Equal(original.Shift, deserialized.Shift);
        Assert.Equal(original.Ctrl, deserialized.Ctrl);
        Assert.Equal(original.Alt, deserialized.Alt);
    }

    [Fact]
    public void Heartbeat_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new HeartbeatMessage
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = 999
        };

        var serialized = original.Serialize();
        var result = MessageSerializer.Deserialize(serialized);

        Assert.True(result.Success);
        var deserialized = (HeartbeatMessage)result.Message!;
        Assert.Equal(original.Timestamp, deserialized.Timestamp);
        Assert.Equal(original.SequenceNumber, deserialized.SequenceNumber);
    }
}
