using QuicRemote.Core.Media;
using QuicRemote.Core.Session;
using QuicRemote.Network.Protocol;

namespace QuicRemote.Core.Control;

/// <summary>
/// Event arguments for input events
/// </summary>
public class InputEventArgs : EventArgs
{
    public required string ClientId { get; init; }
    public required InputEvent InputEvent { get; init; }
}

/// <summary>
/// Provides unified remote control functionality
/// Coordinates input injection, permission checking, and display mapping
/// </summary>
public class RemoteControlService : IDisposable
{
    private readonly InputWrapper _input;
    private readonly CoordinateMapper _mapper;
    private readonly SessionManager? _sessionManager;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Gets the coordinate mapper for display configuration
    /// </summary>
    public CoordinateMapper Mapper => _mapper;

    /// <summary>
    /// Raised when an input event is received (for logging/auditing)
    /// </summary>
    public event EventHandler<InputEventArgs>? InputReceived;

    /// <summary>
    /// Raised when an input event is blocked due to permission
    /// </summary>
    public event EventHandler<InputEventArgs>? InputBlocked;

    /// <summary>
    /// Creates a new RemoteControlService
    /// </summary>
    /// <param name="sessionManager">Optional session manager for permission checks</param>
    public RemoteControlService(SessionManager? sessionManager = null)
    {
        _input = new InputWrapper();
        _mapper = new CoordinateMapper();
        _sessionManager = sessionManager;
    }

    /// <summary>
    /// Initializes the input injection system
    /// </summary>
    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _input.Initialize();
    }

    /// <summary>
    /// Sets the display configuration from the host
    /// </summary>
    public void SetDisplayConfig(DisplayConfigMessage config)
    {
        _mapper.SetDisplayConfig(config);
    }

    /// <summary>
    /// Sets the source (client) display dimensions
    /// </summary>
    public void SetSourceDimensions(int width, int height)
    {
        _mapper.SetSourceDimensions(width, height);
    }

    /// <summary>
    /// Processes an input event from a remote client
    /// </summary>
    /// <param name="clientId">The client sending the input</param>
    /// <param name="inputEvent">The input event to process</param>
    /// <param name="sessionId">Optional session ID for permission check</param>
    /// <returns>True if the input was processed, false if blocked</returns>
    public bool ProcessInput(string clientId, InputEvent inputEvent, Guid? sessionId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Check permission if session manager is available
        if (_sessionManager != null && sessionId.HasValue)
        {
            var hasPermission = _sessionManager.HasControlPermission(sessionId.Value, clientId);
            if (!hasPermission)
            {
                InputBlocked?.Invoke(this, new InputEventArgs
                {
                    ClientId = clientId,
                    InputEvent = inputEvent
                });
                return false;
            }
        }

        // Map coordinates for mouse events
        var mappedEvent = MapInputEvent(inputEvent);

        // Inject the input
        InjectInput(mappedEvent);

        // Notify listeners
        InputReceived?.Invoke(this, new InputEventArgs
        {
            ClientId = clientId,
            InputEvent = inputEvent
        });

        return true;
    }

    /// <summary>
    /// Processes a mouse event message directly
    /// </summary>
    public bool ProcessMouseEvent(string clientId, MouseEventMessage message, Guid? sessionId = null)
    {
        InputEventType eventType = message.Action switch
        {
            MouseAction.Move => InputEventType.MouseMove,
            MouseAction.Press => InputEventType.MouseDown,
            MouseAction.Release => InputEventType.MouseUp,
            MouseAction.Wheel => InputEventType.MouseWheel,
            _ => InputEventType.MouseMove
        };

        var inputEvent = new InputEvent
        {
            Type = eventType,
            X = message.X,
            Y = message.Y,
            Absolute = true,
            MouseButton = MapMouseButtonFromProtocol(message.Button),
            WheelDelta = message.Delta
        };

        return ProcessInput(clientId, inputEvent, sessionId);
    }

    /// <summary>
    /// Processes a keyboard event message directly
    /// </summary>
    public bool ProcessKeyboardEvent(string clientId, KeyboardEventMessage message, Guid? sessionId = null)
    {
        var inputEvent = new InputEvent
        {
            Type = message.Action == QuicRemote.Network.Protocol.KeyAction.Press ? InputEventType.KeyDown : InputEventType.KeyUp,
            KeyCode = (KeyCode)message.KeyCode
        };

        return ProcessInput(clientId, inputEvent, sessionId);
    }

    /// <summary>
    /// Injects a mouse move event
    /// </summary>
    public void MouseMove(int x, int y, bool absolute = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (absolute)
        {
            var (hostX, hostY) = _mapper.MapToHost(x, y);
            _input.MouseMove(hostX, hostY, true);
        }
        else
        {
            var (deltaX, deltaY) = _mapper.MapDeltaToHost(x, y);
            _input.MouseMove(deltaX, deltaY, false);
        }
    }

    /// <summary>
    /// Injects a mouse button press
    /// </summary>
    public void MouseDown(Media.MouseButton button)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _input.MouseDown(button);
    }

    /// <summary>
    /// Injects a mouse button release
    /// </summary>
    public void MouseUp(Media.MouseButton button)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _input.MouseUp(button);
    }

    /// <summary>
    /// Injects a mouse click
    /// </summary>
    public void MouseClick(Media.MouseButton button)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _input.MouseClick(button);
    }

    /// <summary>
    /// Injects a mouse wheel event
    /// </summary>
    public void MouseWheel(int delta, bool horizontal = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _input.MouseWheel(delta, horizontal);
    }

    /// <summary>
    /// Injects a keyboard key press
    /// </summary>
    public void KeyDown(KeyCode key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _input.KeyDown(key);
    }

    /// <summary>
    /// Injects a keyboard key release
    /// </summary>
    public void KeyUp(KeyCode key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _input.KeyUp(key);
    }

    /// <summary>
    /// Injects a key press (down and up)
    /// </summary>
    public void KeyPress(KeyCode key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _input.KeyPress(key);
    }

    /// <summary>
    /// Types text by sending key presses
    /// </summary>
    public void TypeText(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _input.TypeText(text);
    }

    /// <summary>
    /// Switches the active display
    /// </summary>
    public bool SwitchDisplay(int displayIndex)
    {
        return _mapper.SwitchDisplay(displayIndex);
    }

    /// <summary>
    /// Gets the current display configuration
    /// </summary>
    public DisplayConfigMessage GetDisplayConfig()
    {
        var displays = _mapper.Displays;
        var activeIndex = 0;

        if (_mapper.ActiveDisplay != null)
        {
            for (int i = 0; i < displays.Count; i++)
            {
                if (displays[i] == _mapper.ActiveDisplay)
                {
                    activeIndex = i;
                    break;
                }
            }
        }

        return new DisplayConfigMessage
        {
            Displays = displays.ToList(),
            ActiveDisplayIndex = activeIndex
        };
    }

    private InputEvent MapInputEvent(InputEvent inputEvent)
    {
        // Only map coordinates for mouse events
        if (inputEvent.Type is InputEventType.MouseMove or InputEventType.MouseDown or InputEventType.MouseUp)
        {
            var (hostX, hostY) = _mapper.MapToHost(inputEvent.X, inputEvent.Y);
            return new InputEvent
            {
                Type = inputEvent.Type,
                X = hostX,
                Y = hostY,
                Absolute = inputEvent.Absolute,
                MouseButton = inputEvent.MouseButton,
                WheelDelta = inputEvent.WheelDelta,
                HorizontalScroll = inputEvent.HorizontalScroll,
                KeyCode = inputEvent.KeyCode
            };
        }

        return inputEvent;
    }

    private static Media.MouseButton MapMouseButtonFromProtocol(QuicRemote.Network.Protocol.MouseButton button)
    {
        return button switch
        {
            QuicRemote.Network.Protocol.MouseButton.Left => Media.MouseButton.Left,
            QuicRemote.Network.Protocol.MouseButton.Right => Media.MouseButton.Right,
            QuicRemote.Network.Protocol.MouseButton.Middle => Media.MouseButton.Middle,
            _ => Media.MouseButton.Left
        };
    }

    private void InjectInput(InputEvent inputEvent)
    {
        switch (inputEvent.Type)
        {
            case InputEventType.MouseMove:
                _input.MouseMove(inputEvent.X, inputEvent.Y, inputEvent.Absolute);
                break;

            case InputEventType.MouseDown:
                _input.MouseDown(inputEvent.MouseButton);
                break;

            case InputEventType.MouseUp:
                _input.MouseUp(inputEvent.MouseButton);
                break;

            case InputEventType.MouseWheel:
                _input.MouseWheel(inputEvent.WheelDelta, inputEvent.HorizontalScroll);
                break;

            case InputEventType.KeyDown:
                _input.KeyDown(inputEvent.KeyCode);
                break;

            case InputEventType.KeyUp:
                _input.KeyUp(inputEvent.KeyCode);
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _input.Dispose();
        GC.SuppressFinalize(this);
    }
}
