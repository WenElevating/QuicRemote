using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace QuicRemote.Core.Media;

/// <summary>
/// P/Invoke bindings for QuicRemote.Native DLL
/// </summary>
public static class NativeMethods
{
    private const string DllName = "QuicRemote.Native.dll";

    #region Error Codes

    public enum QR_Result
    {
        QR_Success = 0,
        QR_Error_Unknown = -1,
        QR_Error_InvalidParam = -2,
        QR_Error_OutOfMemory = -3,
        QR_Error_NotInitialized = -4,
        QR_Error_AlreadyInitialized = -5,
        QR_Error_Timeout = -6,
        QR_Error_OperationCancelled = -7,
        QR_Error_BufferTooSmall = -8,
        QR_Error_NotSupported = -9,
        QR_Error_DeviceNotFound = -100,
        QR_Error_DeviceLost = -101,
        QR_Error_DeviceBusy = -102,
        QR_Error_EncoderNotSupported = -200,
        QR_Error_EncoderCreateFailed = -201,
        QR_Error_EncoderEncodeFailed = -202,
        QR_Error_EncoderReconfigureFailed = -203,
        QR_Error_DecoderNotSupported = -300,
        QR_Error_DecoderCreateFailed = -301,
        QR_Error_DecoderDecodeFailed = -302,
        QR_Error_CaptureFailed = -400,
        QR_Error_CaptureAccessDenied = -401,
        QR_Error_CaptureDesktopSwitched = -402,
        QR_Error_CaptureNoFrame = -403,
        QR_Error_InputAccessDenied = -500,
        QR_Error_InputBlocked = -501,
        QR_Error_AudioDeviceNotFound = -600,
        QR_Error_AudioCaptureFailed = -601,
        QR_Error_ConnectionLost = -700,
        QR_Error_ConnectionRefused = -701,
        QR_Error_TimeoutConnect = -702,
        QR_Error_ProtocolError = -703,
    }

    #endregion

    #region Enumerations

    public enum QR_EncoderType
    {
        Auto = 0,
        NVENC = 1,
        AMF = 2,
        QSV = 3,
        Software = 4,
    }

    public enum QR_Codec
    {
        H264 = 0,
        H265 = 1,
        VP9 = 2,
    }

    public enum QR_PixelFormat
    {
        NV12 = 0,
        RGB32 = 1,
        RGBA = 2,
    }

    public enum QR_RateControlMode
    {
        CBR = 0,
        VBR = 1,
        CQP = 2,
    }

    public enum QR_MouseButton
    {
        Left = 0,
        Right = 1,
        Middle = 2,
        X1 = 3,
        X2 = 4,
    }

    public enum QR_ButtonAction
    {
        Press = 0,
        Release = 1,
    }

    public enum QR_KeyAction
    {
        Press = 0,
        Release = 1,
    }

    #endregion

    #region Structures

    [StructLayout(LayoutKind.Sequential)]
    public struct QR_Config
    {
        public int log_level;
        public int max_frame_pool_size;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct QR_DirtyRect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct QR_CursorShape
    {
        public int x_hotspot;
        public int y_hotspot;
        public int width;
        public int height;
        public IntPtr data;
        public int data_size;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct QR_Frame
    {
        public int width;
        public int height;
        public QR_PixelFormat format;
        public long timestamp_us;

        // D3D11 texture
        public IntPtr texture;
        public IntPtr device;

        // System memory
        public IntPtr data;
        public int stride;

        // Dirty rects
        public IntPtr dirty_rects;
        public int dirty_rect_count;

        // Cursor
        public int cursor_x;
        public int cursor_y;
        public int cursor_visible;
        public IntPtr cursor_shape;

        // Internal
        public IntPtr internal_data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct QR_Packet
    {
        public IntPtr data;
        public int size;
        public long timestamp_us;
        public int is_keyframe;
        public int frame_num;
        public IntPtr internal_data;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct QR_MonitorInfo
    {
        public int index;
        public int width;
        public int height;
        public int x;
        public int y;
        [MarshalAs(UnmanagedType.U1)] public int is_primary;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string name;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct QR_EncoderConfig
    {
        public QR_EncoderType encoder_type;
        public QR_Codec codec;
        public int width;
        public int height;
        public int bitrate_kbps;
        public int framerate;
        public int gop_size;
        public QR_RateControlMode rate_control;
        public int quality_preset;
        public int low_latency;
        public int hardware_accelerated;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct QR_DecoderConfig
    {
        public QR_Codec codec;
        public int max_width;
        public int max_height;
        public int hardware_accelerated;
        public IntPtr device;
    }

    #endregion

    #region SafeHandle Implementations

    public sealed class NativeFrameHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public NativeFrameHandle() : base(true) { }

        protected override bool ReleaseHandle()
        {
            return QR_Capture_ReleaseFrame(handle) == (int)QR_Result.QR_Success;
        }
    }

    public sealed class EncoderHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public EncoderHandle() : base(true) { }

        protected override bool ReleaseHandle()
        {
            return QR_Encoder_Destroy(handle) == (int)QR_Result.QR_Success;
        }
    }

    public sealed class DecoderHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public DecoderHandle() : base(true) { }

        protected override bool ReleaseHandle()
        {
            return QR_Decoder_Destroy(handle) == (int)QR_Result.QR_Success;
        }
    }

    #endregion

    #region Version API

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint QR_GetVersion();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr QR_GetVersionString();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr QR_GetErrorDescription(QR_Result result);

    #endregion

    #region Init/Shutdown API

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Init(ref QR_Config config);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Shutdown();

    #endregion

    #region Capture API

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Capture_GetMonitorCount();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Capture_GetMonitorInfo(int index, out QR_MonitorInfo info);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Capture_Start(int monitor_index);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Capture_GetFrame(out IntPtr frame, int timeout_ms);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Capture_ReleaseFrame(IntPtr frame);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Capture_Stop();

    #endregion

    #region Encoder API

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Encoder_GetAvailableCount(QR_EncoderType type);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Encoder_Create(ref QR_EncoderConfig config, out IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Encoder_Encode(IntPtr handle, IntPtr frame, out IntPtr packet);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Encoder_RequestKeyframe(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Encoder_Reconfigure(IntPtr handle, int bitrate_kbps);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Encoder_GetStats(IntPtr handle, out int bitrate, out int fps, out float latency_ms);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Encoder_Destroy(IntPtr handle);

    #endregion

    #region Decoder API

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Decoder_Create(ref QR_DecoderConfig config, out IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Decoder_Decode(IntPtr handle, IntPtr packet, out IntPtr frame);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Decoder_Reset(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Decoder_GetStats(IntPtr handle, out int fps, out float latency_ms);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Decoder_Destroy(IntPtr handle);

    #endregion

    #region Input API

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Input_Initialize();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Input_MouseMove(int x, int y, int absolute);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Input_MouseButton(QR_MouseButton button, QR_ButtonAction action);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Input_MouseWheel(int delta, int is_horizontal);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Input_Key(ushort key, QR_KeyAction action);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int QR_Input_Shutdown();

    #endregion

    #region Helper Methods

    public static string GetErrorString(QR_Result result)
    {
        var ptr = QR_GetErrorDescription(result);
        return Marshal.PtrToStringAnsi(ptr) ?? "Unknown error";
    }

    public static void ThrowOnError(QR_Result result)
    {
        if (result != QR_Result.QR_Success)
        {
            throw new QuicRemoteException(GetErrorString(result), result);
        }
    }

    #endregion
}

/// <summary>
/// Exception thrown by QuicRemote native operations
/// </summary>
public class QuicRemoteException : Exception
{
    public NativeMethods.QR_Result ErrorCode { get; }

    public QuicRemoteException(string message, NativeMethods.QR_Result errorCode)
        : base($"{message} (Error: {errorCode})")
    {
        ErrorCode = errorCode;
    }
}
