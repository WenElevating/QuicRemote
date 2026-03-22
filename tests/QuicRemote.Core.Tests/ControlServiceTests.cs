using QuicRemote.Core.Control;
using QuicRemote.Core.Media;
using QuicRemote.Core.Session;
using QuicRemote.Network.Protocol;
using Xunit;

namespace QuicRemote.Core.Tests;

public class CoordinateMapperTests
{
    [Fact]
    public void SetDisplayConfig_SetsDisplaysCorrectly()
    {
        var mapper = new CoordinateMapper();
        var config = new DisplayConfigMessage
        {
            Displays = new List<DisplayInfo>
            {
                new DisplayInfo { Index = 0, Width = 1920, Height = 1080, IsPrimary = true }
            },
            ActiveDisplayIndex = 0
        };

        mapper.SetDisplayConfig(config);

        Assert.NotNull(mapper.ActiveDisplay);
        Assert.Equal(1920, mapper.ActiveDisplay.Width);
        Assert.Equal(1080, mapper.ActiveDisplay.Height);
    }

    [Fact]
    public void SetSourceDimensions_SetsDimensions()
    {
        var mapper = new CoordinateMapper();
        mapper.SetSourceDimensions(1280, 720);
        var (scaleX, scaleY) = mapper.GetScaleFactors();
        Assert.Equal(1.0, scaleX);
        Assert.Equal(1.0, scaleY);
    }

    [Fact]
    public void MapToHost_ScalesCoordinatesCorrectly()
    {
        var mapper = new CoordinateMapper();
        mapper.SetDisplayConfig(new DisplayConfigMessage
        {
            Displays = new List<DisplayInfo>
            {
                new DisplayInfo { Index = 0, Width = 1920, Height = 1080, OffsetX = 0, OffsetY = 0 }
            },
            ActiveDisplayIndex = 0
        });
        mapper.SetSourceDimensions(1280, 720);

        // 1280 -> 1920 is 1.5x scale
        var (hostX, hostY) = mapper.MapToHost(640, 360);

        Assert.Equal(960, hostX);  // 640 * 1.5
        Assert.Equal(540, hostY);  // 360 * 1.5
    }

    [Fact]
    public void MapToHost_ClampsToBounds()
    {
        var mapper = new CoordinateMapper();
        mapper.SetDisplayConfig(new DisplayConfigMessage
        {
            Displays = new List<DisplayInfo>
            {
                new DisplayInfo { Index = 0, Width = 1920, Height = 1080, OffsetX = 0, OffsetY = 0 }
            },
            ActiveDisplayIndex = 0
        });
        mapper.SetSourceDimensions(1280, 720);

        // Coordinates outside bounds should be clamped
        var (hostX, hostY) = mapper.MapToHost(2000, 1000);

        Assert.Equal(1919, hostX);  // Clamped to Width - 1
        Assert.Equal(1079, hostY);  // Clamped to Height - 1
    }

    [Fact]
    public void MapToHost_WithVirtualDesktopOffset()
    {
        var mapper = new CoordinateMapper();
        mapper.SetDisplayConfig(new DisplayConfigMessage
        {
            Displays = new List<DisplayInfo>
            {
                new DisplayInfo { Index = 0, Width = 1920, Height = 1080, OffsetX = 1920, OffsetY = 0 }
            },
            ActiveDisplayIndex = 0
        });
        mapper.SetSourceDimensions(1920, 1080);

        var (hostX, hostY) = mapper.MapToHost(0, 0);

        Assert.Equal(1920, hostX);  // Offset added
        Assert.Equal(0, hostY);
    }

    [Fact]
    public void MapToClient_ReverseMapping()
    {
        var mapper = new CoordinateMapper();
        mapper.SetDisplayConfig(new DisplayConfigMessage
        {
            Displays = new List<DisplayInfo>
            {
                new DisplayInfo { Index = 0, Width = 1920, Height = 1080, OffsetX = 0, OffsetY = 0 }
            },
            ActiveDisplayIndex = 0
        });
        mapper.SetSourceDimensions(1280, 720);

        var (clientX, clientY) = mapper.MapToClient(960, 540);

        Assert.Equal(640, clientX);
        Assert.Equal(360, clientY);
    }

    [Fact]
    public void MapDeltaToHost_ScalesDeltas()
    {
        var mapper = new CoordinateMapper();
        mapper.SetDisplayConfig(new DisplayConfigMessage
        {
            Displays = new List<DisplayInfo>
            {
                new DisplayInfo { Index = 0, Width = 1920, Height = 1080, OffsetX = 0, OffsetY = 0 }
            },
            ActiveDisplayIndex = 0
        });
        mapper.SetSourceDimensions(1280, 720);

        var (deltaX, deltaY) = mapper.MapDeltaToHost(10, 10);

        Assert.Equal(15, deltaX);  // 10 * 1.5
        Assert.Equal(15, deltaY);
    }

    [Fact]
    public void SwitchDisplay_ChangesActiveDisplay()
    {
        var mapper = new CoordinateMapper();
        mapper.SetDisplayConfig(new DisplayConfigMessage
        {
            Displays = new List<DisplayInfo>
            {
                new DisplayInfo { Index = 0, Width = 1920, Height = 1080, OffsetX = 0, OffsetY = 0 },
                new DisplayInfo { Index = 1, Width = 2560, Height = 1440, OffsetX = 1920, OffsetY = 0 }
            },
            ActiveDisplayIndex = 0
        });

        var result = mapper.SwitchDisplay(1);

        Assert.True(result);
        Assert.Equal(2560, mapper.ActiveDisplay?.Width);
    }

    [Fact]
    public void SwitchDisplay_InvalidIndex_ReturnsFalse()
    {
        var mapper = new CoordinateMapper();
        mapper.SetDisplayConfig(new DisplayConfigMessage
        {
            Displays = new List<DisplayInfo>
            {
                new DisplayInfo { Index = 0, Width = 1920, Height = 1080 }
            },
            ActiveDisplayIndex = 0
        });

        var result = mapper.SwitchDisplay(5);

        Assert.False(result);
    }

    [Fact]
    public void IsWithinActiveDisplay_ReturnsCorrectValue()
    {
        var mapper = new CoordinateMapper();
        mapper.SetDisplayConfig(new DisplayConfigMessage
        {
            Displays = new List<DisplayInfo>
            {
                new DisplayInfo { Index = 0, Width = 1920, Height = 1080, OffsetX = 0, OffsetY = 0 }
            },
            ActiveDisplayIndex = 0
        });

        Assert.True(mapper.IsWithinActiveDisplay(100, 100));
        Assert.True(mapper.IsWithinActiveDisplay(1919, 1079));
        Assert.False(mapper.IsWithinActiveDisplay(1920, 1080));
        Assert.False(mapper.IsWithinActiveDisplay(-1, 0));
    }

    [Fact]
    public void GetDisplayAtPoint_FindsCorrectDisplay()
    {
        var mapper = new CoordinateMapper();
        mapper.SetDisplayConfig(new DisplayConfigMessage
        {
            Displays = new List<DisplayInfo>
            {
                new DisplayInfo { Index = 0, Width = 1920, Height = 1080, OffsetX = 0, OffsetY = 0 },
                new DisplayInfo { Index = 1, Width = 2560, Height = 1440, OffsetX = 1920, OffsetY = 0 }
            },
            ActiveDisplayIndex = 0
        });

        var display0 = mapper.GetDisplayAtPoint(100, 100);
        var display1 = mapper.GetDisplayAtPoint(2000, 100);

        Assert.NotNull(display0);
        Assert.Equal(0, display0.Index);
        Assert.NotNull(display1);
        Assert.Equal(1, display1.Index);
    }
}

public class RemoteControlServiceTests
{
    [Fact]
    public void SetDisplayConfig_SetsMapperConfig()
    {
        using var service = new RemoteControlService();
        var config = new DisplayConfigMessage
        {
            Displays = new List<DisplayInfo>
            {
                new DisplayInfo { Index = 0, Width = 1920, Height = 1080 }
            }
        };

        service.SetDisplayConfig(config);

        Assert.NotNull(service.Mapper.ActiveDisplay);
    }

    [Fact]
    public void ProcessInput_WithoutPermission_Blocked()
    {
        var sessionManager = new SessionManager();
        var session = sessionManager.CreateSession("device-001");
        sessionManager.AddClient(session.SessionId, "client-001");

        using var service = new RemoteControlService(sessionManager);
        // Don't call Initialize() - permission check happens before input injection

        var blockedEventRaised = false;
        service.InputBlocked += (s, e) => blockedEventRaised = true;

        var result = service.ProcessInput("client-001", new InputEvent
        {
            Type = InputEventType.KeyDown,
            KeyCode = KeyCode.A
        }, session.SessionId);

        Assert.False(result);
        Assert.True(blockedEventRaised);
    }

    [Fact]
    public void ProcessInput_WithPermission_Processed()
    {
        var sessionManager = new SessionManager();
        var session = sessionManager.CreateSession("device-001");
        sessionManager.AddClient(session.SessionId, "client-001");
        sessionManager.GrantPermission(session.SessionId, "client-001", Session.ControlPermission.Input);

        using var service = new RemoteControlService(sessionManager);
        // Don't call Initialize() - we're just testing permission flow

        // This will fail at injection due to not initialized, but permission check should pass
        Assert.Throws<InvalidOperationException>(() => service.ProcessInput("client-001", new InputEvent
        {
            Type = InputEventType.KeyDown,
            KeyCode = KeyCode.A
        }, session.SessionId));
    }

    [Fact]
    public void ProcessInput_WithoutSessionManager_Blocked()
    {
        using var service = new RemoteControlService();
        // Without session manager, all input is allowed (no permission check)
        // But injection will fail without native DLL init

        Assert.Throws<InvalidOperationException>(() => service.ProcessInput("client-001", new InputEvent
        {
            Type = InputEventType.KeyDown,
            KeyCode = KeyCode.A
        }));
    }

    [Fact]
    public void SwitchDisplay_ChangesActiveDisplay()
    {
        using var service = new RemoteControlService();
        service.SetDisplayConfig(new DisplayConfigMessage
        {
            Displays = new List<DisplayInfo>
            {
                new DisplayInfo { Index = 0, Width = 1920, Height = 1080 },
                new DisplayInfo { Index = 1, Width = 2560, Height = 1440 }
            },
            ActiveDisplayIndex = 0
        });

        var result = service.SwitchDisplay(1);

        Assert.True(result);
        Assert.Equal(2560, service.Mapper.ActiveDisplay?.Width);
    }

    [Fact]
    public void GetDisplayConfig_ReturnsCurrentConfig()
    {
        using var service = new RemoteControlService();
        service.SetDisplayConfig(new DisplayConfigMessage
        {
            Displays = new List<DisplayInfo>
            {
                new DisplayInfo { Index = 0, Width = 1920, Height = 1080, IsPrimary = true }
            },
            ActiveDisplayIndex = 0
        });

        var config = service.GetDisplayConfig();

        Assert.Single(config.Displays);
        Assert.Equal(1920, config.Displays[0].Width);
    }

    [Fact]
    public void MouseEvent_CoordinatesAreMapped()
    {
        using var service = new RemoteControlService();
        service.SetDisplayConfig(new DisplayConfigMessage
        {
            Displays = new List<DisplayInfo>
            {
                new DisplayInfo { Index = 0, Width = 1920, Height = 1080, OffsetX = 0, OffsetY = 0 }
            }
        });
        service.SetSourceDimensions(1280, 720);
        // Don't call Initialize() as it requires native DLL

        // Test that coordinates are mapped correctly through the mapper
        var (hostX, hostY) = service.Mapper.MapToHost(640, 360);
        Assert.Equal(960, hostX);  // 640 * 1.5
        Assert.Equal(540, hostY);  // 360 * 1.5
    }
}
