using QuicRemote.Core.Media;
using Xunit;

namespace QuicRemote.Core.Tests;

/// <summary>
/// Tests for capture wrapper types and enumerations
/// </summary>
public class CaptureWrapperTests
{
    [Fact]
    public void MonitorInfo_HasCorrectProperties()
    {
        var monitor = new MonitorInfo
        {
            Index = 0,
            Width = 1920,
            Height = 1080,
            X = 0,
            Y = 0,
            IsPrimary = true,
            Name = "Primary Monitor"
        };

        Assert.Equal(0, monitor.Index);
        Assert.Equal(1920, monitor.Width);
        Assert.Equal(1080, monitor.Height);
        Assert.Equal(0, monitor.X);
        Assert.Equal(0, monitor.Y);
        Assert.True(monitor.IsPrimary);
        Assert.Equal("Primary Monitor", monitor.Name);
    }

    [Fact]
    public void MonitorInfo_InitCreatesImmutableObject()
    {
        var monitor = new MonitorInfo
        {
            Index = 1,
            Width = 2560,
            Height = 1440
        };

        // Properties should be init-only
        Assert.Equal(1, monitor.Index);
        Assert.Equal(2560, monitor.Width);
        Assert.Equal(1440, monitor.Height);
    }

    [Fact]
    public void CaptureWrapper_ThrowsWhenNotStarted()
    {
        using var capture = new CaptureWrapper();

        // Should throw because StartCapture was not called
        Assert.Throws<InvalidOperationException>(() => capture.GetFrame());
    }

    [Fact]
    public void CaptureWrapper_ThrowsObjectDisposedAfterDispose()
    {
        var capture = new CaptureWrapper();
        capture.Dispose();

        Assert.Throws<ObjectDisposedException>(() => capture.StartCapture(0));
    }

    [Fact]
    public void CaptureWrapper_CanBeDisposedMultipleTimes()
    {
        var capture = new CaptureWrapper();
        capture.Dispose();
        capture.Dispose(); // Should not throw
    }

    [Fact]
    public void CaptureWrapper_ReleaseFrame_ThrowsOnNull()
    {
        using var capture = new CaptureWrapper();

        Assert.Throws<ArgumentNullException>(() => capture.ReleaseFrame(null!));
    }

    [Fact]
    public void CaptureWrapper_ReleaseFrame_ThrowsAfterDispose()
    {
        var capture = new CaptureWrapper();
        capture.Dispose();

        // Create a dummy FrameData - this would normally come from GetFrame
        // Here we're just testing the disposal check
        Assert.Throws<ObjectDisposedException>(() => capture.ReleaseFrame(null!));
    }
}
