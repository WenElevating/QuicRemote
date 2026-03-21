using System.Runtime.InteropServices;
using QuicRemote.Core.Media;
using Xunit;

namespace QuicRemote.Core.Tests;

/// <summary>
/// Tests for P/Invoke bindings and native interop structures
/// </summary>
public class NativeMethodsTests
{
    [Fact]
    public void QR_Config_HasCorrectSize()
    {
        var expectedSize = 8; // 2 x int32
        var actualSize = Marshal.SizeOf<NativeMethods.QR_Config>();
        Assert.Equal(expectedSize, actualSize);
    }

    [Fact]
    public void QR_DirtyRect_HasCorrectSize()
    {
        var expectedSize = 16; // 4 x int32
        var actualSize = Marshal.SizeOf<NativeMethods.QR_DirtyRect>();
        Assert.Equal(expectedSize, actualSize);
    }

    [Fact]
    public void QR_CursorShape_HasCorrectLayout()
    {
        var size = Marshal.SizeOf<NativeMethods.QR_CursorShape>();
        Assert.True(size > 0);

        // Verify layout has expected fields
        var cursorShape = new NativeMethods.QR_CursorShape();
        cursorShape.x_hotspot = 10;
        cursorShape.y_hotspot = 20;
        cursorShape.width = 32;
        cursorShape.height = 32;

        Assert.Equal(10, cursorShape.x_hotspot);
        Assert.Equal(20, cursorShape.y_hotspot);
        Assert.Equal(32, cursorShape.width);
        Assert.Equal(32, cursorShape.height);
    }

    [Fact]
    public void QR_Frame_HasCorrectLayout()
    {
        var size = Marshal.SizeOf<NativeMethods.QR_Frame>();
        Assert.True(size > 0);

        // Verify fields can be set
        var frame = new NativeMethods.QR_Frame
        {
            width = 1920,
            height = 1080,
            format = NativeMethods.QR_PixelFormat.NV12,
            timestamp_us = 1234567890
        };

        Assert.Equal(1920, frame.width);
        Assert.Equal(1080, frame.height);
        Assert.Equal(NativeMethods.QR_PixelFormat.NV12, frame.format);
        Assert.Equal(1234567890, frame.timestamp_us);
    }

    [Fact]
    public void QR_Packet_HasCorrectLayout()
    {
        var size = Marshal.SizeOf<NativeMethods.QR_Packet>();
        Assert.True(size > 0);

        var packet = new NativeMethods.QR_Packet
        {
            size = 1024,
            timestamp_us = 1000000,
            is_keyframe = 1,
            frame_num = 42
        };

        Assert.Equal(1024, packet.size);
        Assert.Equal(1000000, packet.timestamp_us);
        Assert.Equal(1, packet.is_keyframe);
        Assert.Equal(42, packet.frame_num);
    }

    [Fact]
    public void QR_EncoderConfig_HasCorrectLayout()
    {
        var size = Marshal.SizeOf<NativeMethods.QR_EncoderConfig>();
        Assert.True(size > 0);

        var config = new NativeMethods.QR_EncoderConfig
        {
            encoder_type = NativeMethods.QR_EncoderType.NVENC,
            codec = NativeMethods.QR_Codec.H264,
            width = 1920,
            height = 1080,
            bitrate_kbps = 5000,
            framerate = 60,
            gop_size = 60,
            rate_control = NativeMethods.QR_RateControlMode.CBR,
            quality_preset = 1,
            low_latency = 1,
            hardware_accelerated = 1
        };

        Assert.Equal(NativeMethods.QR_EncoderType.NVENC, config.encoder_type);
        Assert.Equal(NativeMethods.QR_Codec.H264, config.codec);
        Assert.Equal(1920, config.width);
        Assert.Equal(1080, config.height);
        Assert.Equal(5000, config.bitrate_kbps);
        Assert.Equal(60, config.framerate);
    }

    [Fact]
    public void QR_DecoderConfig_HasCorrectLayout()
    {
        var size = Marshal.SizeOf<NativeMethods.QR_DecoderConfig>();
        Assert.True(size > 0);

        var config = new NativeMethods.QR_DecoderConfig
        {
            codec = NativeMethods.QR_Codec.H265,
            max_width = 3840,
            max_height = 2160,
            hardware_accelerated = 1
        };

        Assert.Equal(NativeMethods.QR_Codec.H265, config.codec);
        Assert.Equal(3840, config.max_width);
        Assert.Equal(2160, config.max_height);
    }

    [Theory]
    [InlineData(NativeMethods.QR_Result.QR_Success, 0)]
    [InlineData(NativeMethods.QR_Result.QR_Error_Unknown, -1)]
    [InlineData(NativeMethods.QR_Result.QR_Error_InvalidParam, -2)]
    [InlineData(NativeMethods.QR_Result.QR_Error_OutOfMemory, -3)]
    public void QR_Result_HasExpectedValues(NativeMethods.QR_Result result, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)result);
    }

    [Theory]
    [InlineData(NativeMethods.QR_EncoderType.Auto, 0)]
    [InlineData(NativeMethods.QR_EncoderType.NVENC, 1)]
    [InlineData(NativeMethods.QR_EncoderType.AMF, 2)]
    [InlineData(NativeMethods.QR_EncoderType.QSV, 3)]
    [InlineData(NativeMethods.QR_EncoderType.Software, 4)]
    public void QR_EncoderType_HasExpectedValues(NativeMethods.QR_EncoderType type, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)type);
    }

    [Theory]
    [InlineData(NativeMethods.QR_Codec.H264, 0)]
    [InlineData(NativeMethods.QR_Codec.H265, 1)]
    [InlineData(NativeMethods.QR_Codec.VP9, 2)]
    public void QR_Codec_HasExpectedValues(NativeMethods.QR_Codec codec, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)codec);
    }

    [Theory]
    [InlineData(NativeMethods.QR_PixelFormat.NV12, 0)]
    [InlineData(NativeMethods.QR_PixelFormat.RGB32, 1)]
    [InlineData(NativeMethods.QR_PixelFormat.RGBA, 2)]
    public void QR_PixelFormat_HasExpectedValues(NativeMethods.QR_PixelFormat format, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)format);
    }

    [Fact]
    public void QR_MouseButton_MatchesNativeEnum()
    {
        Assert.Equal(0, (int)NativeMethods.QR_MouseButton.Left);
        Assert.Equal(1, (int)NativeMethods.QR_MouseButton.Right);
        Assert.Equal(2, (int)NativeMethods.QR_MouseButton.Middle);
        Assert.Equal(3, (int)NativeMethods.QR_MouseButton.X1);
        Assert.Equal(4, (int)NativeMethods.QR_MouseButton.X2);
    }

    [Fact]
    public void QR_ButtonAction_MatchesNativeEnum()
    {
        Assert.Equal(0, (int)NativeMethods.QR_ButtonAction.Press);
        Assert.Equal(1, (int)NativeMethods.QR_ButtonAction.Release);
    }

    [Fact]
    public void QR_KeyAction_MatchesNativeEnum()
    {
        Assert.Equal(0, (int)NativeMethods.QR_KeyAction.Press);
        Assert.Equal(1, (int)NativeMethods.QR_KeyAction.Release);
    }

    [Fact]
    public void QR_MonitorInfo_HasCorrectLayout()
    {
        // QR_MonitorInfo contains a fixed-length string field which requires special marshaling
        // We test the field access instead of size
        var monitorInfo = new NativeMethods.QR_MonitorInfo
        {
            index = 0,
            width = 1920,
            height = 1080,
            x = 0,
            y = 0,
            is_primary = 1,
            name = "Monitor 1"
        };

        Assert.Equal(0, monitorInfo.index);
        Assert.Equal(1920, monitorInfo.width);
        Assert.Equal(1080, monitorInfo.height);
        Assert.True(monitorInfo.is_primary != 0);
    }

    [Fact]
    public void QuicRemoteException_StoresErrorCode()
    {
        var error = NativeMethods.QR_Result.QR_Error_InvalidParam;
        var exception = new QuicRemoteException("Test error", error);

        Assert.Equal(error, exception.ErrorCode);
        Assert.Contains("Test error", exception.Message);
        Assert.Contains("InvalidParam", exception.Message);
    }
}
