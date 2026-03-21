using System;
using System.Runtime.InteropServices;

namespace QuicRemote.Core.Media;

/// <summary>
/// Managed wrapper for video encoder functionality
/// </summary>
public class EncoderWrapper : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    /// <summary>
    /// Gets the number of available encoders of the specified type
    /// </summary>
    public static int GetAvailableCount(NativeMethods.QR_EncoderType type)
    {
        return NativeMethods.QR_Encoder_GetAvailableCount(type);
    }

    /// <summary>
    /// Creates a new encoder with the specified configuration
    /// </summary>
    public void Create(EncoderConfig config)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle != IntPtr.Zero)
        {
            throw new InvalidOperationException("Encoder already created");
        }

        var nativeConfig = new NativeMethods.QR_EncoderConfig
        {
            encoder_type = config.EncoderType,
            codec = config.Codec,
            width = config.Width,
            height = config.Height,
            bitrate_kbps = config.BitrateKbps,
            framerate = config.Framerate,
            gop_size = config.GopSize,
            rate_control = config.RateControl,
            quality_preset = config.QualityPreset,
            low_latency = config.LowLatency ? 1 : 0,
            hardware_accelerated = config.HardwareAccelerated ? 1 : 0
        };

        var result = NativeMethods.QR_Encoder_Create(ref nativeConfig, out _handle);
        NativeMethods.ThrowOnError((NativeMethods.QR_Result)result);
    }

    /// <summary>
    /// Encodes a frame
    /// </summary>
    public Packet? Encode(FrameData frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Encoder not created");
        }

        if (frame == null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        // Build native frame structure
        var nativeFrame = new NativeMethods.QR_Frame
        {
            width = frame.Width,
            height = frame.Height,
            format = frame.Format,
            timestamp_us = frame.TimestampUs,
            texture = frame.Texture,
            device = frame.Device,
            data = frame.Data,
            stride = frame.Stride
        };

        var framePtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.QR_Frame>());
        try
        {
            Marshal.StructureToPtr(nativeFrame, framePtr, false);

            var result = NativeMethods.QR_Encoder_Encode(_handle, framePtr, out var packetPtr);
            NativeMethods.ThrowOnError((NativeMethods.QR_Result)result);

            if (packetPtr == IntPtr.Zero)
            {
                return null;
            }

            var packet = Marshal.PtrToStructure<NativeMethods.QR_Packet>(packetPtr);
            return new Packet(packetPtr, packet);
        }
        finally
        {
            Marshal.FreeHGlobal(framePtr);
        }
    }

    /// <summary>
    /// Requests a keyframe
    /// </summary>
    public void RequestKeyframe()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Encoder not created");
        }

        var result = NativeMethods.QR_Encoder_RequestKeyframe(_handle);
        NativeMethods.ThrowOnError((NativeMethods.QR_Result)result);
    }

    /// <summary>
    /// Reconfigures the encoder with a new bitrate
    /// </summary>
    public void Reconfigure(int bitrateKbps)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Encoder not created");
        }

        var result = NativeMethods.QR_Encoder_Reconfigure(_handle, bitrateKbps);
        NativeMethods.ThrowOnError((NativeMethods.QR_Result)result);
    }

    /// <summary>
    /// Gets encoder statistics
    /// </summary>
    public EncoderStats GetStats()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Encoder not created");
        }

        var result = NativeMethods.QR_Encoder_GetStats(_handle, out var bitrate, out var fps, out var latencyMs);
        NativeMethods.ThrowOnError((NativeMethods.QR_Result)result);

        return new EncoderStats
        {
            Bitrate = bitrate,
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
            NativeMethods.QR_Encoder_Destroy(_handle);
            _handle = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Encoder configuration
/// </summary>
public class EncoderConfig
{
    public NativeMethods.QR_EncoderType EncoderType { get; set; } = NativeMethods.QR_EncoderType.Auto;
    public NativeMethods.QR_Codec Codec { get; set; } = NativeMethods.QR_Codec.H264;
    public int Width { get; set; }
    public int Height { get; set; }
    public int BitrateKbps { get; set; } = 5000;
    public int Framerate { get; set; } = 60;
    public int GopSize { get; set; } = 60;
    public NativeMethods.QR_RateControlMode RateControl { get; set; } = NativeMethods.QR_RateControlMode.CBR;
    public int QualityPreset { get; set; } = 2;
    public bool LowLatency { get; set; } = true;
    public bool HardwareAccelerated { get; set; } = true;
}

/// <summary>
/// Encoder statistics
/// </summary>
public class EncoderStats
{
    public int Bitrate { get; init; }
    public int Fps { get; init; }
    public float LatencyMs { get; init; }
}

/// <summary>
/// Encoded packet data
/// </summary>
public class Packet : IDisposable
{
    private readonly IntPtr _packetPtr;
    private bool _disposed;

    internal Packet(IntPtr packetPtr, NativeMethods.QR_Packet packet)
    {
        _packetPtr = packetPtr;
        Data = packet.data;
        Size = packet.size;
        TimestampUs = packet.timestamp_us;
        IsKeyframe = packet.is_keyframe != 0;
        FrameNum = packet.frame_num;
    }

    public IntPtr Data { get; }
    public int Size { get; }
    public long TimestampUs { get; }
    public bool IsKeyframe { get; }
    public int FrameNum { get; }

    /// <summary>
    /// Copies the packet data to a byte array
    /// </summary>
    public byte[]? GetDataCopy()
    {
        if (Data == IntPtr.Zero || Size == 0)
        {
            return null;
        }

        var buffer = new byte[Size];
        Marshal.Copy(Data, buffer, 0, Size);
        return buffer;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Packet memory is managed internally by the native library
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
