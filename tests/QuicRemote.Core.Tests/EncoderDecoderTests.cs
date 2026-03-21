using QuicRemote.Core.Media;
using Xunit;

namespace QuicRemote.Core.Tests;

/// <summary>
/// Tests for encoder and decoder wrapper types
/// </summary>
public class EncoderDecoderTests
{
    #region Encoder Tests

    [Fact]
    public void EncoderConfig_DefaultValuesAreCorrect()
    {
        var config = new EncoderConfig
        {
            EncoderType = NativeMethods.QR_EncoderType.Auto,
            Codec = NativeMethods.QR_Codec.H264,
            Width = 1920,
            Height = 1080,
            BitrateKbps = 5000,
            Framerate = 60,
            GopSize = 60,
            RateControl = NativeMethods.QR_RateControlMode.CBR,
            QualityPreset = 1,
            LowLatency = true,
            HardwareAccelerated = true
        };

        Assert.Equal(NativeMethods.QR_EncoderType.Auto, config.EncoderType);
        Assert.Equal(NativeMethods.QR_Codec.H264, config.Codec);
        Assert.Equal(1920, config.Width);
        Assert.Equal(1080, config.Height);
        Assert.Equal(5000, config.BitrateKbps);
        Assert.Equal(60, config.Framerate);
        Assert.Equal(60, config.GopSize);
        Assert.Equal(NativeMethods.QR_RateControlMode.CBR, config.RateControl);
        Assert.Equal(1, config.QualityPreset);
        Assert.True(config.LowLatency);
        Assert.True(config.HardwareAccelerated);
    }

    [Fact]
    public void EncoderStats_HasCorrectProperties()
    {
        var stats = new EncoderStats
        {
            Bitrate = 5000,
            Fps = 60,
            LatencyMs = 3.5f
        };

        Assert.Equal(5000, stats.Bitrate);
        Assert.Equal(60, stats.Fps);
        Assert.Equal(3.5f, stats.LatencyMs);
    }

    [Fact]
    public void EncoderConfig_ToNativeConfig_ProducesCorrectStructure()
    {
        var managedConfig = new EncoderConfig
        {
            EncoderType = NativeMethods.QR_EncoderType.NVENC,
            Codec = NativeMethods.QR_Codec.H265,
            Width = 2560,
            Height = 1440,
            BitrateKbps = 10000,
            Framerate = 60,
            GopSize = 120,
            RateControl = NativeMethods.QR_RateControlMode.VBR,
            QualityPreset = 2,
            LowLatency = true,
            HardwareAccelerated = true
        };

        // Convert to native
        var nativeConfig = new NativeMethods.QR_EncoderConfig
        {
            encoder_type = managedConfig.EncoderType,
            codec = managedConfig.Codec,
            width = managedConfig.Width,
            height = managedConfig.Height,
            bitrate_kbps = managedConfig.BitrateKbps,
            framerate = managedConfig.Framerate,
            gop_size = managedConfig.GopSize,
            rate_control = managedConfig.RateControl,
            quality_preset = managedConfig.QualityPreset,
            low_latency = managedConfig.LowLatency ? 1 : 0,
            hardware_accelerated = managedConfig.HardwareAccelerated ? 1 : 0
        };

        Assert.Equal(NativeMethods.QR_EncoderType.NVENC, nativeConfig.encoder_type);
        Assert.Equal(NativeMethods.QR_Codec.H265, nativeConfig.codec);
        Assert.Equal(2560, nativeConfig.width);
        Assert.Equal(1440, nativeConfig.height);
        Assert.Equal(10000, nativeConfig.bitrate_kbps);
        Assert.Equal(60, nativeConfig.framerate);
        Assert.Equal(120, nativeConfig.gop_size);
        Assert.Equal(NativeMethods.QR_RateControlMode.VBR, nativeConfig.rate_control);
    }

    [Fact]
    public void EncoderWrapper_ThrowsWhenNotCreated()
    {
        using var encoder = new EncoderWrapper();

        // Should throw because Create() was not called
        Assert.Throws<InvalidOperationException>(() => encoder.RequestKeyframe());
        Assert.Throws<InvalidOperationException>(() => encoder.Reconfigure(5000));
        Assert.Throws<InvalidOperationException>(() => encoder.GetStats());
    }

    [Fact]
    public void EncoderWrapper_ThrowsObjectDisposedAfterDispose()
    {
        var encoder = new EncoderWrapper();
        encoder.Dispose();

        Assert.Throws<ObjectDisposedException>(() => encoder.Create(new EncoderConfig()));
    }

    [Fact]
    public void EncoderWrapper_CanBeDisposedMultipleTimes()
    {
        var encoder = new EncoderWrapper();
        encoder.Dispose();
        encoder.Dispose(); // Should not throw
    }

    #endregion

    #region Decoder Tests

    [Fact]
    public void DecoderConfig_DefaultValuesAreCorrect()
    {
        var config = new DecoderConfig
        {
            Codec = NativeMethods.QR_Codec.H264,
            MaxWidth = 3840,
            MaxHeight = 2160,
            HardwareAccelerated = true
        };

        Assert.Equal(NativeMethods.QR_Codec.H264, config.Codec);
        Assert.Equal(3840, config.MaxWidth);
        Assert.Equal(2160, config.MaxHeight);
        Assert.True(config.HardwareAccelerated);
    }

    [Fact]
    public void DecoderConfig_ToNativeConfig_ProducesCorrectStructure()
    {
        var managedConfig = new DecoderConfig
        {
            Codec = NativeMethods.QR_Codec.H265,
            MaxWidth = 1920,
            MaxHeight = 1080,
            HardwareAccelerated = true
        };

        var nativeConfig = new NativeMethods.QR_DecoderConfig
        {
            codec = managedConfig.Codec,
            max_width = managedConfig.MaxWidth,
            max_height = managedConfig.MaxHeight,
            hardware_accelerated = managedConfig.HardwareAccelerated ? 1 : 0
        };

        Assert.Equal(NativeMethods.QR_Codec.H265, nativeConfig.codec);
        Assert.Equal(1920, nativeConfig.max_width);
        Assert.Equal(1080, nativeConfig.max_height);
        Assert.Equal(1, nativeConfig.hardware_accelerated);
    }

    [Fact]
    public void DecoderStats_HasCorrectProperties()
    {
        var stats = new DecoderStats
        {
            Fps = 60,
            LatencyMs = 2.5f
        };

        Assert.Equal(60, stats.Fps);
        Assert.Equal(2.5f, stats.LatencyMs);
    }

    [Fact]
    public void DecoderWrapper_ThrowsWhenNotCreated()
    {
        using var decoder = new DecoderWrapper();

        // Should throw because Create() was not called
        Assert.Throws<InvalidOperationException>(() => decoder.Reset());
        Assert.Throws<InvalidOperationException>(() => decoder.GetStats());
    }

    [Fact]
    public void DecoderWrapper_ThrowsObjectDisposedAfterDispose()
    {
        var decoder = new DecoderWrapper();
        decoder.Dispose();

        Assert.Throws<ObjectDisposedException>(() => decoder.Create(new DecoderConfig()));
    }

    [Fact]
    public void DecoderWrapper_CanBeDisposedMultipleTimes()
    {
        var decoder = new DecoderWrapper();
        decoder.Dispose();
        decoder.Dispose(); // Should not throw
    }

    [Fact]
    public void DecoderWrapper_Decode_ThrowsInvalidOperationExceptionWhenNotCreated()
    {
        using var decoder = new DecoderWrapper();

        // When decoder is not created, it throws InvalidOperationException
        Assert.Throws<InvalidOperationException>(() => decoder.Decode(null!));
    }

    #endregion
}
