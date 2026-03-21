using System;
using System.Runtime.InteropServices;

namespace QuicRemote.Core.Media;

/// <summary>
/// Managed wrapper for video decoder functionality
/// </summary>
public class DecoderWrapper : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    /// <summary>
    /// Creates a new decoder with the specified configuration
    /// </summary>
    public void Create(DecoderConfig config)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle != IntPtr.Zero)
        {
            throw new InvalidOperationException("Decoder already created");
        }

        var nativeConfig = new NativeMethods.QR_DecoderConfig
        {
            codec = config.Codec,
            max_width = config.MaxWidth,
            max_height = config.MaxHeight,
            hardware_accelerated = config.HardwareAccelerated ? 1 : 0,
            device = config.Device
        };

        var result = NativeMethods.QR_Decoder_Create(ref nativeConfig, out _handle);
        NativeMethods.ThrowOnError((NativeMethods.QR_Result)result);
    }

    /// <summary>
    /// Decodes a packet
    /// </summary>
    public DecodedFrame? Decode(Packet packet)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Decoder not created");
        }

        if (packet == null)
        {
            throw new ArgumentNullException(nameof(packet));
        }

        // Build native packet structure
        var nativePacket = new NativeMethods.QR_Packet
        {
            data = packet.Data,
            size = packet.Size,
            timestamp_us = packet.TimestampUs,
            is_keyframe = packet.IsKeyframe ? 1 : 0,
            frame_num = packet.FrameNum
        };

        var packetPtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.QR_Packet>());
        try
        {
            Marshal.StructureToPtr(nativePacket, packetPtr, false);

            var result = NativeMethods.QR_Decoder_Decode(_handle, packetPtr, out var framePtr);
            NativeMethods.ThrowOnError((NativeMethods.QR_Result)result);

            if (framePtr == IntPtr.Zero)
            {
                return null;
            }

            var frame = Marshal.PtrToStructure<NativeMethods.QR_Frame>(framePtr);
            return new DecodedFrame(framePtr, frame);
        }
        finally
        {
            Marshal.FreeHGlobal(packetPtr);
        }
    }

    /// <summary>
    /// Resets the decoder
    /// </summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Decoder not created");
        }

        var result = NativeMethods.QR_Decoder_Reset(_handle);
        NativeMethods.ThrowOnError((NativeMethods.QR_Result)result);
    }

    /// <summary>
    /// Gets decoder statistics
    /// </summary>
    public DecoderStats GetStats()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Decoder not created");
        }

        var result = NativeMethods.QR_Decoder_GetStats(_handle, out var fps, out var latencyMs);
        NativeMethods.ThrowOnError((NativeMethods.QR_Result)result);

        return new DecoderStats
        {
            Fps = fps,
            LatencyMs = latencyMs
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_handle != IntPtr.Zero)
        {
            NativeMethods.QR_Decoder_Destroy(_handle);
            _handle = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Decoder configuration
/// </summary>
public class DecoderConfig
{
    public NativeMethods.QR_Codec Codec { get; set; } = NativeMethods.QR_Codec.H264;
    public int MaxWidth { get; set; } = 1920;
    public int MaxHeight { get; set; } = 1080;
    public bool HardwareAccelerated { get; set; } = true;
    public IntPtr Device { get; set; } = IntPtr.Zero;
}

/// <summary>
/// Decoder statistics
/// </summary>
public class DecoderStats
{
    public int Fps { get; init; }
    public float LatencyMs { get; init; }
}

/// <summary>
/// Decoded frame data
/// </summary>
public class DecodedFrame : IDisposable
{
    private readonly IntPtr _framePtr;
    private bool _disposed;

    internal DecodedFrame(IntPtr framePtr, NativeMethods.QR_Frame frame)
    {
        _framePtr = framePtr;
        Width = frame.width;
        Height = frame.height;
        Format = frame.format;
        TimestampUs = frame.timestamp_us;
        Texture = frame.texture;
        Device = frame.device;
        Data = frame.data;
        Stride = frame.stride;
    }

    public int Width { get; }
    public int Height { get; }
    public NativeMethods.QR_PixelFormat Format { get; }
    public long TimestampUs { get; }

    // GPU resources
    public IntPtr Texture { get; }
    public IntPtr Device { get; }

    // CPU resources
    public IntPtr Data { get; }
    public int Stride { get; }

    /// <summary>
    /// Copies the frame data to a byte array
    /// </summary>
    public byte[]? GetDataCopy()
    {
        if (Data == IntPtr.Zero || Stride == 0 || Height == 0)
        {
            return null;
        }

        var size = Stride * Height;
        var buffer = new byte[size];
        Marshal.Copy(Data, buffer, 0, size);
        return buffer;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Frame memory is managed internally by the native library
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
