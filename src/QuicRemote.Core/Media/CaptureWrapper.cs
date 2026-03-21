using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace QuicRemote.Core.Media;

/// <summary>
/// Managed wrapper for screen capture functionality
/// </summary>
public class CaptureWrapper : IDisposable
{
    private bool _disposed;
    private bool _running;

    /// <summary>
    /// Gets the number of available monitors
    /// </summary>
    public static int MonitorCount => NativeMethods.QR_Capture_GetMonitorCount();

    /// <summary>
    /// Gets monitor information for the specified index
    /// </summary>
    public static MonitorInfo GetMonitorInfo(int index)
    {
        var result = NativeMethods.QR_Capture_GetMonitorInfo(index, out var info);
        NativeMethods.ThrowOnError((NativeMethods.QR_Result)result);

        return new MonitorInfo
        {
            Index = info.index,
            Width = info.width,
            Height = info.height,
            X = info.x,
            Y = info.y,
            IsPrimary = info.is_primary != 0,
            Name = info.name ?? string.Empty
        };
    }

    /// <summary>
    /// Gets all monitor information
    /// </summary>
    public static IReadOnlyList<MonitorInfo> GetAllMonitors()
    {
        var count = MonitorCount;
        var monitors = new List<MonitorInfo>(count);

        for (int i = 0; i < count; i++)
        {
            monitors.Add(GetMonitorInfo(i));
        }

        return monitors;
    }

    /// <summary>
    /// Starts screen capture on the specified monitor
    /// </summary>
    public void StartCapture(int monitorIndex = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_running)
        {
            throw new InvalidOperationException("Capture is already running");
        }

        var result = NativeMethods.QR_Capture_Start(monitorIndex);
        NativeMethods.ThrowOnError((NativeMethods.QR_Result)result);

        _running = true;
    }

    /// <summary>
    /// Gets the next captured frame
    /// </summary>
    public FrameData? GetFrame(int timeoutMs = 1000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_running)
        {
            throw new InvalidOperationException("Capture is not running");
        }

        var result = NativeMethods.QR_Capture_GetFrame(out var framePtr, timeoutMs);

        if ((NativeMethods.QR_Result)result == NativeMethods.QR_Result.QR_Error_CaptureNoFrame)
        {
            return null;
        }

        NativeMethods.ThrowOnError((NativeMethods.QR_Result)result);

        if (framePtr == IntPtr.Zero)
        {
            return null;
        }

        var frame = Marshal.PtrToStructure<NativeMethods.QR_Frame>(framePtr);
        return new FrameData(framePtr, frame);
    }

    /// <summary>
    /// Releases a captured frame
    /// </summary>
    public void ReleaseFrame(FrameData frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (frame == null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        frame.Dispose();
    }

    /// <summary>
    /// Stops screen capture
    /// </summary>
    public void StopCapture()
    {
        if (!_running)
        {
            return;
        }

        var result = NativeMethods.QR_Capture_Stop();
        _running = false;

        NativeMethods.ThrowOnError((NativeMethods.QR_Result)result);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_running)
        {
            StopCapture();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Monitor information
/// </summary>
public class MonitorInfo
{
    public int Index { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public bool IsPrimary { get; init; }
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// Captured frame data
/// </summary>
public class FrameData : IDisposable
{
    private readonly IntPtr _framePtr;
    private bool _disposed;

    internal FrameData(IntPtr framePtr, NativeMethods.QR_Frame frame)
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
        CursorX = frame.cursor_x;
        CursorY = frame.cursor_y;
        CursorVisible = frame.cursor_visible != 0;
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

    // Cursor
    public int CursorX { get; }
    public int CursorY { get; }
    public bool CursorVisible { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_framePtr != IntPtr.Zero)
        {
            NativeMethods.QR_Capture_ReleaseFrame(_framePtr);
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
