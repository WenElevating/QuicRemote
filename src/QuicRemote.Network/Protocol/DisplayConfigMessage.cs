using System.Buffers.Binary;
using System.Text;

namespace QuicRemote.Network.Protocol;

/// <summary>
/// Information about a single display/monitor
/// </summary>
public class DisplayInfo
{
    /// <summary>
    /// Display index
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Display width in pixels
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Display height in pixels
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// DPI scaling factor (e.g., 150 = 150% scaling)
    /// </summary>
    public int DpiScale { get; set; }

    /// <summary>
    /// Whether this is the primary display
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Display name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// X offset in virtual desktop
    /// </summary>
    public int OffsetX { get; set; }

    /// <summary>
    /// Y offset in virtual desktop
    /// </summary>
    public int OffsetY { get; set; }

    public int SerializedSize => 4 + 4 + 4 + 4 + 1 + 2 + Encoding.UTF8.GetByteCount(Name) + 4 + 4;

    public byte[] Serialize()
    {
        var nameBytes = Encoding.UTF8.GetBytes(Name);
        var buffer = new byte[SerializedSize];
        var span = buffer.AsSpan();

        var offset = 0;
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(offset), Index); offset += 4;
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(offset), Width); offset += 4;
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(offset), Height); offset += 4;
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(offset), DpiScale); offset += 4;
        span[offset++] = (byte)(IsPrimary ? 1 : 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(offset), (ushort)nameBytes.Length); offset += 2;
        nameBytes.CopyTo(span.Slice(offset)); offset += nameBytes.Length;
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(offset), OffsetX); offset += 4;
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(offset), OffsetY);

        return buffer;
    }

    public static DisplayInfo Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 27)
            throw new ArgumentException("Data too short for DisplayInfo");

        var info = new DisplayInfo();
        var offset = 0;

        info.Index = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset)); offset += 4;
        info.Width = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset)); offset += 4;
        info.Height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset)); offset += 4;
        info.DpiScale = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset)); offset += 4;
        info.IsPrimary = data[offset++] == 1;
        var nameLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset)); offset += 2;
        if (data.Length >= offset + nameLength)
        {
            info.Name = Encoding.UTF8.GetString(data.Slice(offset, nameLength));
        }
        offset += nameLength;
        if (data.Length >= offset + 8)
        {
            info.OffsetX = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset)); offset += 4;
            info.OffsetY = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset));
        }

        return info;
    }
}

/// <summary>
/// Message for exchanging display configuration between host and client
/// </summary>
public class DisplayConfigMessage : Message
{
    public override MessageType Type => MessageType.DisplayConfig;

    /// <summary>
    /// List of displays on the host
    /// </summary>
    public List<DisplayInfo> Displays { get; set; } = new();

    /// <summary>
    /// Currently active display index
    /// </summary>
    public int ActiveDisplayIndex { get; set; }

    /// <summary>
    /// Protocol version for compatibility
    /// </summary>
    public int ProtocolVersion { get; set; } = 1;

    protected override byte[] SerializePayload()
    {
        // Calculate total size
        var displayCount = Displays.Count;
        var totalSize = 4 + 4 + 4; // ProtocolVersion + ActiveDisplayIndex + DisplayCount
        foreach (var display in Displays)
        {
            totalSize += 4 + display.SerializedSize; // 4 bytes for size prefix
        }

        var buffer = new byte[totalSize];
        var span = buffer.AsSpan();
        var offset = 0;

        // Header
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(offset), ProtocolVersion); offset += 4;
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(offset), ActiveDisplayIndex); offset += 4;
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(offset), displayCount); offset += 4;

        // Displays
        foreach (var display in Displays)
        {
            var displayData = display.Serialize();
            BinaryPrimitives.WriteInt32BigEndian(span.Slice(offset), displayData.Length); offset += 4;
            displayData.CopyTo(span.Slice(offset)); offset += displayData.Length;
        }

        return buffer;
    }

    public static DisplayConfigMessage Deserialize(byte[] payload)
    {
        if (payload.Length < 12)
            throw new ArgumentException("Payload too short for DisplayConfigMessage");

        var message = new DisplayConfigMessage();
        var offset = 0;

        message.ProtocolVersion = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(offset)); offset += 4;
        message.ActiveDisplayIndex = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(offset)); offset += 4;
        var displayCount = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(offset)); offset += 4;

        for (int i = 0; i < displayCount && offset < payload.Length; i++)
        {
            var displaySize = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(offset)); offset += 4;
            if (offset + displaySize <= payload.Length)
            {
                var display = DisplayInfo.Deserialize(payload.AsSpan(offset, displaySize));
                message.Displays.Add(display);
                offset += displaySize;
            }
        }

        return message;
    }
}

/// <summary>
/// Message for requesting a keyframe (used during recovery)
/// </summary>
public class KeyframeRequestMessage : Message
{
    public override MessageType Type => MessageType.KeyframeRequest;

    protected override byte[] SerializePayload() => Array.Empty<byte>();

    public static KeyframeRequestMessage Deserialize(byte[] payload) => new();
}
