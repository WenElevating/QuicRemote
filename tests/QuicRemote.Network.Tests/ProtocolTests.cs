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

    // New message type tests

    [Fact]
    public void RoleChangeMessage_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new RoleChangeMessage
        {
            NewRole = SessionRole.Controller,
            ClientId = "client-123",
            SequenceNumber = 1
        };

        var serialized = original.Serialize();
        var result = MessageSerializer.Deserialize(serialized);

        Assert.True(result.Success);
        var deserialized = (RoleChangeMessage)result.Message!;
        Assert.Equal(original.NewRole, deserialized.NewRole);
        Assert.Equal(original.ClientId, deserialized.ClientId);
    }

    [Fact]
    public void ControlRequestMessage_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new ControlRequestMessage
        {
            RequestedPermission = ControlPermission.Input,
            ClientId = "client-456",
            Reason = "Need to control mouse",
            SequenceNumber = 2
        };

        var serialized = original.Serialize();
        var result = MessageSerializer.Deserialize(serialized);

        Assert.True(result.Success);
        var deserialized = (ControlRequestMessage)result.Message!;
        Assert.Equal(original.RequestedPermission, deserialized.RequestedPermission);
        Assert.Equal(original.ClientId, deserialized.ClientId);
        Assert.Equal(original.Reason, deserialized.Reason);
    }

    [Fact]
    public void ControlResponseMessage_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new ControlResponseMessage
        {
            Approved = true,
            GrantedPermission = ControlPermission.Full,
            DenyReason = null,
            SequenceNumber = 3
        };

        var serialized = original.Serialize();
        var result = MessageSerializer.Deserialize(serialized);

        Assert.True(result.Success);
        var deserialized = (ControlResponseMessage)result.Message!;
        Assert.Equal(original.Approved, deserialized.Approved);
        Assert.Equal(original.GrantedPermission, deserialized.GrantedPermission);
    }

    [Fact]
    public void ControlResponseMessage_WithDenyReason_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new ControlResponseMessage
        {
            Approved = false,
            GrantedPermission = ControlPermission.None,
            DenyReason = "Another client has control",
            SequenceNumber = 4
        };

        var serialized = original.Serialize();
        var result = MessageSerializer.Deserialize(serialized);

        Assert.True(result.Success);
        var deserialized = (ControlResponseMessage)result.Message!;
        Assert.Equal(original.Approved, deserialized.Approved);
        Assert.Equal(original.GrantedPermission, deserialized.GrantedPermission);
        Assert.Equal(original.DenyReason, deserialized.DenyReason);
    }

    [Fact]
    public void PermissionGrantMessage_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new PermissionGrantMessage
        {
            Permission = ControlPermission.Full,
            ClientId = "client-789",
            SequenceNumber = 5
        };

        var serialized = original.Serialize();
        var result = MessageSerializer.Deserialize(serialized);

        Assert.True(result.Success);
        var deserialized = (PermissionGrantMessage)result.Message!;
        Assert.Equal(original.Permission, deserialized.Permission);
        Assert.Equal(original.ClientId, deserialized.ClientId);
    }

    [Fact]
    public void PermissionRevokeMessage_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new PermissionRevokeMessage
        {
            ClientId = "client-abc",
            Reason = "Session ended",
            SequenceNumber = 6
        };

        var serialized = original.Serialize();
        var result = MessageSerializer.Deserialize(serialized);

        Assert.True(result.Success);
        var deserialized = (PermissionRevokeMessage)result.Message!;
        Assert.Equal(original.ClientId, deserialized.ClientId);
        Assert.Equal(original.Reason, deserialized.Reason);
    }

    [Fact]
    public void DisplayConfigMessage_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new DisplayConfigMessage
        {
            ProtocolVersion = 1,
            ActiveDisplayIndex = 0,
            Displays = new List<DisplayInfo>
            {
                new DisplayInfo
                {
                    Index = 0,
                    Width = 1920,
                    Height = 1080,
                    DpiScale = 100,
                    IsPrimary = true,
                    Name = "Monitor 1",
                    OffsetX = 0,
                    OffsetY = 0
                },
                new DisplayInfo
                {
                    Index = 1,
                    Width = 2560,
                    Height = 1440,
                    DpiScale = 150,
                    IsPrimary = false,
                    Name = "Monitor 2",
                    OffsetX = 1920,
                    OffsetY = 0
                }
            },
            SequenceNumber = 7
        };

        var serialized = original.Serialize();
        var result = MessageSerializer.Deserialize(serialized);

        Assert.True(result.Success);
        var deserialized = (DisplayConfigMessage)result.Message!;
        Assert.Equal(original.ProtocolVersion, deserialized.ProtocolVersion);
        Assert.Equal(original.ActiveDisplayIndex, deserialized.ActiveDisplayIndex);
        Assert.Equal(2, deserialized.Displays.Count);

        var display0 = deserialized.Displays[0];
        Assert.Equal(1920, display0.Width);
        Assert.Equal(1080, display0.Height);
        Assert.Equal(100, display0.DpiScale);
        Assert.True(display0.IsPrimary);

        var display1 = deserialized.Displays[1];
        Assert.Equal(2560, display1.Width);
        Assert.Equal(1440, display1.Height);
        Assert.Equal(150, display1.DpiScale);
        Assert.False(display1.IsPrimary);
    }

    [Fact]
    public void KeyframeRequestMessage_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new KeyframeRequestMessage
        {
            SequenceNumber = 8
        };

        var serialized = original.Serialize();
        var result = MessageSerializer.Deserialize(serialized);

        Assert.True(result.Success);
        Assert.IsType<KeyframeRequestMessage>(result.Message);
    }
}

public class ControlMessageTests
{
    [Fact]
    public void AuthRequestMessage_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new AuthRequestMessage
        {
            Password = "secret123",
            ClientId = "client-xyz",
            ProtocolVersion = 1
        };

        var serialized = original.Serialize();
        var deserialized = AuthRequestMessage.Deserialize(serialized);

        Assert.Equal(original.Password, deserialized.Password);
        Assert.Equal(original.ClientId, deserialized.ClientId);
        Assert.Equal(original.ProtocolVersion, deserialized.ProtocolVersion);
    }

    [Fact]
    public void AuthRequestMessage_WithoutOptionalFields_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new AuthRequestMessage
        {
            ProtocolVersion = 2
        };

        var serialized = original.Serialize();
        var deserialized = AuthRequestMessage.Deserialize(serialized);

        Assert.Null(deserialized.Password);
        Assert.Null(deserialized.ClientId);
        Assert.Equal(2, deserialized.ProtocolVersion);
    }

    [Fact]
    public void AuthResponseMessage_Success_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new AuthResponseMessage
        {
            Success = true,
            SessionId = 12345,
            ErrorMessage = null
        };

        var serialized = original.Serialize();
        var deserialized = AuthResponseMessage.Deserialize(serialized);

        Assert.True(deserialized.Success);
        Assert.Equal(12345, deserialized.SessionId);
        Assert.Null(deserialized.ErrorMessage);
    }

    [Fact]
    public void AuthResponseMessage_Failure_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new AuthResponseMessage
        {
            Success = false,
            SessionId = 0,
            ErrorMessage = "Invalid password"
        };

        var serialized = original.Serialize();
        var deserialized = AuthResponseMessage.Deserialize(serialized);

        Assert.False(deserialized.Success);
        Assert.Equal(0, deserialized.SessionId);
        Assert.Equal("Invalid password", deserialized.ErrorMessage);
    }

    [Fact]
    public void ConfigUpdateMessage_AllFields_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new ConfigUpdateMessage
        {
            BitrateKbps = 5000,
            Framerate = 60,
            Quality = 80
        };

        var serialized = original.Serialize();
        var deserialized = ConfigUpdateMessage.Deserialize(serialized);

        Assert.Equal(5000, deserialized.BitrateKbps);
        Assert.Equal(60, deserialized.Framerate);
        Assert.Equal(80, deserialized.Quality);
    }

    [Fact]
    public void ConfigUpdateMessage_PartialFields_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new ConfigUpdateMessage
        {
            BitrateKbps = 8000
        };

        var serialized = original.Serialize();
        var deserialized = ConfigUpdateMessage.Deserialize(serialized);

        Assert.Equal(8000, deserialized.BitrateKbps);
        Assert.Null(deserialized.Framerate);
        Assert.Null(deserialized.Quality);
    }

    [Fact]
    public void ControlMessage_Ping_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        var original = new ControlMessage
        {
            Type = ControlMessageType.Ping
        };

        var serialized = original.Serialize();
        var deserialized = ControlMessage.Deserialize(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(ControlMessageType.Ping, deserialized.Type);
    }

    [Fact]
    public void ControlMessage_DeserializeWithInsufficientData_ReturnsNull()
    {
        var data = new byte[5]; // Less than minimum 9 bytes
        var result = ControlMessage.Deserialize(data);
        Assert.Null(result);
    }
}
