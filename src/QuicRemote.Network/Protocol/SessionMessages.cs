using System.Buffers.Binary;
using System.Text;

namespace QuicRemote.Network.Protocol;

/// <summary>
/// Session role enumeration
/// </summary>
public enum SessionRole : byte
{
    /// <summary>
    /// Host (controlled device)
    /// </summary>
    Host = 0,

    /// <summary>
    /// Controller (has full control)
    /// </summary>
    Controller = 1,

    /// <summary>
    /// Viewer (view only, no control)
    /// </summary>
    Viewer = 2
}

/// <summary>
/// Control permission levels
/// </summary>
public enum ControlPermission : byte
{
    /// <summary>
    /// No control, view only
    /// </summary>
    None = 0,

    /// <summary>
    /// Can send input events
    /// </summary>
    Input = 1,

    /// <summary>
    /// Full control including clipboard and files
    /// </summary>
    Full = 2
}

/// <summary>
/// Message for role change notification
/// </summary>
public class RoleChangeMessage : Message
{
    public override MessageType Type => MessageType.RoleChange;
    public SessionRole NewRole { get; set; }
    public string ClientId { get; set; } = string.Empty;

    protected override byte[] SerializePayload()
    {
        var clientIdBytes = Encoding.UTF8.GetBytes(ClientId);
        var buffer = new byte[1 + 2 + clientIdBytes.Length];
        var span = buffer.AsSpan();

        span[0] = (byte)NewRole;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(1), (ushort)clientIdBytes.Length);
        clientIdBytes.CopyTo(span.Slice(3));

        return buffer;
    }

    public static RoleChangeMessage Deserialize(byte[] payload)
    {
        if (payload.Length < 3)
            throw new ArgumentException("Payload too short for RoleChangeMessage");

        var message = new RoleChangeMessage
        {
            NewRole = (SessionRole)payload[0]
        };

        var clientIdLength = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(1));
        if (payload.Length >= 3 + clientIdLength)
        {
            message.ClientId = Encoding.UTF8.GetString(payload.AsSpan(3, clientIdLength));
        }

        return message;
    }
}

/// <summary>
/// Message for requesting control permission
/// </summary>
public class ControlRequestMessage : Message
{
    public override MessageType Type => MessageType.ControlRequest;
    public ControlPermission RequestedPermission { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string? Reason { get; set; }

    protected override byte[] SerializePayload()
    {
        var clientIdBytes = Encoding.UTF8.GetBytes(ClientId);
        var reasonBytes = Reason != null ? Encoding.UTF8.GetBytes(Reason) : Array.Empty<byte>();
        var buffer = new byte[1 + 2 + clientIdBytes.Length + 2 + reasonBytes.Length];
        var span = buffer.AsSpan();

        span[0] = (byte)RequestedPermission;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(1), (ushort)clientIdBytes.Length);
        clientIdBytes.CopyTo(span.Slice(3));
        var offset = 3 + clientIdBytes.Length;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(offset), (ushort)reasonBytes.Length);
        reasonBytes.CopyTo(span.Slice(offset + 2));

        return buffer;
    }

    public static ControlRequestMessage Deserialize(byte[] payload)
    {
        if (payload.Length < 5)
            throw new ArgumentException("Payload too short for ControlRequestMessage");

        var message = new ControlRequestMessage
        {
            RequestedPermission = (ControlPermission)payload[0]
        };

        var clientIdLength = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(1));
        if (payload.Length >= 3 + clientIdLength)
        {
            message.ClientId = Encoding.UTF8.GetString(payload.AsSpan(3, clientIdLength));
        }

        var offset = 3 + clientIdLength;
        if (payload.Length >= offset + 2)
        {
            var reasonLength = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(offset));
            if (payload.Length >= offset + 2 + reasonLength && reasonLength > 0)
            {
                message.Reason = Encoding.UTF8.GetString(payload.AsSpan(offset + 2, reasonLength));
            }
        }

        return message;
    }
}

/// <summary>
/// Message for responding to control request
/// </summary>
public class ControlResponseMessage : Message
{
    public override MessageType Type => MessageType.ControlResponse;
    public bool Approved { get; set; }
    public ControlPermission GrantedPermission { get; set; }
    public string? DenyReason { get; set; }

    protected override byte[] SerializePayload()
    {
        var denyReasonBytes = DenyReason != null ? Encoding.UTF8.GetBytes(DenyReason) : Array.Empty<byte>();
        var buffer = new byte[2 + 2 + denyReasonBytes.Length];
        var span = buffer.AsSpan();

        span[0] = (byte)(Approved ? 1 : 0);
        span[1] = (byte)GrantedPermission;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2), (ushort)denyReasonBytes.Length);
        denyReasonBytes.CopyTo(span.Slice(4));

        return buffer;
    }

    public static ControlResponseMessage Deserialize(byte[] payload)
    {
        if (payload.Length < 4)
            throw new ArgumentException("Payload too short for ControlResponseMessage");

        var message = new ControlResponseMessage
        {
            Approved = payload[0] == 1,
            GrantedPermission = (ControlPermission)payload[1]
        };

        var denyReasonLength = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(2));
        if (payload.Length >= 4 + denyReasonLength && denyReasonLength > 0)
        {
            message.DenyReason = Encoding.UTF8.GetString(payload.AsSpan(4, denyReasonLength));
        }

        return message;
    }
}

/// <summary>
/// Message for granting permission
/// </summary>
public class PermissionGrantMessage : Message
{
    public override MessageType Type => MessageType.PermissionGrant;
    public ControlPermission Permission { get; set; }
    public string ClientId { get; set; } = string.Empty;

    protected override byte[] SerializePayload()
    {
        var clientIdBytes = Encoding.UTF8.GetBytes(ClientId);
        var buffer = new byte[1 + 2 + clientIdBytes.Length];
        var span = buffer.AsSpan();

        span[0] = (byte)Permission;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(1), (ushort)clientIdBytes.Length);
        clientIdBytes.CopyTo(span.Slice(3));

        return buffer;
    }

    public static PermissionGrantMessage Deserialize(byte[] payload)
    {
        if (payload.Length < 3)
            throw new ArgumentException("Payload too short for PermissionGrantMessage");

        var message = new PermissionGrantMessage
        {
            Permission = (ControlPermission)payload[0]
        };

        var clientIdLength = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(1));
        if (payload.Length >= 3 + clientIdLength)
        {
            message.ClientId = Encoding.UTF8.GetString(payload.AsSpan(3, clientIdLength));
        }

        return message;
    }
}

/// <summary>
/// Message for revoking permission
/// </summary>
public class PermissionRevokeMessage : Message
{
    public override MessageType Type => MessageType.PermissionRevoke;
    public string ClientId { get; set; } = string.Empty;
    public string? Reason { get; set; }

    protected override byte[] SerializePayload()
    {
        var clientIdBytes = Encoding.UTF8.GetBytes(ClientId);
        var reasonBytes = Reason != null ? Encoding.UTF8.GetBytes(Reason) : Array.Empty<byte>();
        var buffer = new byte[2 + clientIdBytes.Length + 2 + reasonBytes.Length];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)clientIdBytes.Length);
        clientIdBytes.CopyTo(span.Slice(2));
        var offset = 2 + clientIdBytes.Length;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(offset), (ushort)reasonBytes.Length);
        reasonBytes.CopyTo(span.Slice(offset + 2));

        return buffer;
    }

    public static PermissionRevokeMessage Deserialize(byte[] payload)
    {
        if (payload.Length < 4)
            throw new ArgumentException("Payload too short for PermissionRevokeMessage");

        var message = new PermissionRevokeMessage();

        var clientIdLength = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(0));
        if (payload.Length >= 2 + clientIdLength)
        {
            message.ClientId = Encoding.UTF8.GetString(payload.AsSpan(2, clientIdLength));
        }

        var offset = 2 + clientIdLength;
        if (payload.Length >= offset + 2)
        {
            var reasonLength = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(offset));
            if (payload.Length >= offset + 2 + reasonLength && reasonLength > 0)
            {
                message.Reason = Encoding.UTF8.GetString(payload.AsSpan(offset + 2, reasonLength));
            }
        }

        return message;
    }
}
