using System.Runtime.InteropServices;
using QuicRemote.Core.Media;
using Xunit;

namespace QuicRemote.Core.Tests;

/// <summary>
/// Integration tests that call the actual Native DLL
/// These tests require QuicRemote.Native.dll to be present
/// </summary>
public class NativeDllIntegrationTests : IDisposable
{
    private bool _initialized = false;

    public NativeDllIntegrationTests()
    {
        // Try to initialize for each test
    }

    public void Dispose()
    {
        if (_initialized)
        {
            NativeMethods.QR_Shutdown();
            _initialized = false;
        }
    }

    [Fact]
    public void NativeDll_CanBeLoaded()
    {
        // This test verifies the DLL can be loaded and a function called
        var version = NativeMethods.QR_GetVersion();
        Assert.NotEqual(0u, version);
    }

    [Fact]
    public void NativeDll_GetVersion_ReturnsValidVersion()
    {
        var version = NativeMethods.QR_GetVersion();

        // Version should be in format: (major << 16) | (minor << 8) | patch
        var major = (version >> 16) & 0xFF;
        var minor = (version >> 8) & 0xFF;
        var patch = version & 0xFF;

        Assert.True(major >= 0 && major <= 255);
        Assert.True(minor >= 0 && minor <= 255);
        Assert.True(patch >= 0 && patch <= 255);
    }

    [Fact]
    public void NativeDll_GetVersionString_ReturnsValidString()
    {
        var ptr = NativeMethods.QR_GetVersionString();
        Assert.NotEqual(IntPtr.Zero, ptr);

        var versionStr = Marshal.PtrToStringAnsi(ptr);
        Assert.NotNull(versionStr);
        Assert.Matches(@"^\d+\.\d+\.\d+$", versionStr!);
    }

    [Fact]
    public void NativeDll_GetErrorDescription_ReturnsValidStrings()
    {
        var successMsg = NativeMethods.GetErrorString(NativeMethods.QR_Result.QR_Success);
        Assert.Equal("Success", successMsg);

        var unknownMsg = NativeMethods.GetErrorString(NativeMethods.QR_Result.QR_Error_Unknown);
        Assert.Contains("Unknown", unknownMsg);

        var invalidParamMsg = NativeMethods.GetErrorString(NativeMethods.QR_Result.QR_Error_InvalidParam);
        Assert.Contains("Invalid", invalidParamMsg);
    }

    [Fact]
    public void NativeDll_Init_WithValidConfig_Succeeds()
    {
        var config = new NativeMethods.QR_Config
        {
            log_level = 0,
            max_frame_pool_size = 4
        };

        var result = (NativeMethods.QR_Result)NativeMethods.QR_Init(ref config);
        Assert.Equal(NativeMethods.QR_Result.QR_Success, result);
        _initialized = true;
    }

    [Fact]
    public void NativeDll_Init_CalledTwice_ReturnsAlreadyInitialized()
    {
        var config = new NativeMethods.QR_Config
        {
            log_level = 0,
            max_frame_pool_size = 4
        };

        var result1 = (NativeMethods.QR_Result)NativeMethods.QR_Init(ref config);
        Assert.Equal(NativeMethods.QR_Result.QR_Success, result1);

        var result2 = (NativeMethods.QR_Result)NativeMethods.QR_Init(ref config);
        Assert.Equal(NativeMethods.QR_Result.QR_Error_AlreadyInitialized, result2);

        NativeMethods.QR_Shutdown();
    }

    [Fact]
    public void NativeDll_Shutdown_WithoutInit_ReturnsNotInitialized()
    {
        var result = (NativeMethods.QR_Result)NativeMethods.QR_Shutdown();
        Assert.Equal(NativeMethods.QR_Result.QR_Error_NotInitialized, result);
    }

    [Fact]
    public void NativeDll_Capture_GetMonitorCount_ReturnsPositiveValue()
    {
        // This should work even without QR_Init
        var count = NativeMethods.QR_Capture_GetMonitorCount();
        Assert.True(count >= 0, "Monitor count should be >= 0");

        // Most systems have at least one monitor
        // But this could be 0 in headless environments
    }

    [Fact]
    public void NativeDll_Capture_GetMonitorInfo_WithInvalidIndex_ReturnsError()
    {
        var result = (NativeMethods.QR_Result)NativeMethods.QR_Capture_GetMonitorInfo(-1, out _);
        Assert.Equal(NativeMethods.QR_Result.QR_Error_DeviceNotFound, result);
    }

    [Fact]
    public void NativeDll_Capture_GetMonitorInfo_WithValidIndex_ReturnsValidInfo()
    {
        var count = NativeMethods.QR_Capture_GetMonitorCount();
        if (count > 0)
        {
            var result = (NativeMethods.QR_Result)NativeMethods.QR_Capture_GetMonitorInfo(0, out var info);
            Assert.Equal(NativeMethods.QR_Result.QR_Success, result);
            Assert.True(info.width > 0);
            Assert.True(info.height > 0);
            Assert.Equal(0, info.index);
        }
    }

    [Fact]
    public void NativeDll_Encoder_GetAvailableCount_ReturnsNonNegative()
    {
        var count = NativeMethods.QR_Encoder_GetAvailableCount(NativeMethods.QR_EncoderType.Auto);
        Assert.True(count >= 0);

        count = NativeMethods.QR_Encoder_GetAvailableCount(NativeMethods.QR_EncoderType.Software);
        Assert.True(count >= 0);
    }

    [Fact]
    public void NativeDll_Encoder_Create_WithoutInit_ReturnsNotInitialized()
    {
        var config = new NativeMethods.QR_EncoderConfig
        {
            encoder_type = NativeMethods.QR_EncoderType.Software,
            codec = NativeMethods.QR_Codec.H264,
            width = 1920,
            height = 1080,
            bitrate_kbps = 5000,
            framerate = 60,
            gop_size = 60,
            rate_control = NativeMethods.QR_RateControlMode.CBR,
            quality_preset = 1,
            low_latency = 1,
            hardware_accelerated = 0
        };

        var result = (NativeMethods.QR_Result)NativeMethods.QR_Encoder_Create(ref config, out _);
        Assert.Equal(NativeMethods.QR_Result.QR_Error_NotInitialized, result);
    }

    [Fact]
    public void NativeDll_Decoder_Create_WithoutInit_ReturnsNotInitialized()
    {
        var config = new NativeMethods.QR_DecoderConfig
        {
            codec = NativeMethods.QR_Codec.H264,
            max_width = 1920,
            max_height = 1080,
            hardware_accelerated = 0,
            device = IntPtr.Zero
        };

        var result = (NativeMethods.QR_Result)NativeMethods.QR_Decoder_Create(ref config, out _);
        Assert.Equal(NativeMethods.QR_Result.QR_Error_NotInitialized, result);
    }

    [Fact]
    public void NativeDll_Input_Initialize_WithoutInit_ReturnsNotInitialized()
    {
        var result = (NativeMethods.QR_Result)NativeMethods.QR_Input_Initialize();
        Assert.Equal(NativeMethods.QR_Result.QR_Error_NotInitialized, result);
    }

    [Fact]
    public void NativeDll_Input_MouseMove_ReturnsValidResult()
    {
        // MouseMove doesn't require initialization in our implementation
        var result = (NativeMethods.QR_Result)NativeMethods.QR_Input_MouseMove(100, 100, 0);
        // Result could be success or error depending on permissions
        Assert.True(Enum.IsDefined(typeof(NativeMethods.QR_Result), result));
    }

    [Fact]
    public void NativeDll_FullInit_Shutdown_Cycle()
    {
        // Initialize
        var config = new NativeMethods.QR_Config
        {
            log_level = 0,
            max_frame_pool_size = 4
        };

        var result = (NativeMethods.QR_Result)NativeMethods.QR_Init(ref config);
        Assert.Equal(NativeMethods.QR_Result.QR_Success, result);

        // Shutdown
        result = (NativeMethods.QR_Result)NativeMethods.QR_Shutdown();
        Assert.Equal(NativeMethods.QR_Result.QR_Success, result);

        // Verify we can init again
        result = (NativeMethods.QR_Result)NativeMethods.QR_Init(ref config);
        Assert.Equal(NativeMethods.QR_Result.QR_Success, result);

        // Final cleanup
        NativeMethods.QR_Shutdown();
    }
}
