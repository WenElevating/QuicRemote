using QuicRemote.Network.Protocol;

namespace QuicRemote.Core.Control;

/// <summary>
/// Maps coordinates between client and host display spaces
/// Handles DPI scaling and multi-monitor configurations
/// </summary>
public class CoordinateMapper
{
    private DisplayInfo? _activeDisplay;
    private List<DisplayInfo> _displays = new();
    private int _sourceWidth;
    private int _sourceHeight;

    /// <summary>
    /// Gets the currently active display
    /// </summary>
    public DisplayInfo? ActiveDisplay => _activeDisplay;

    /// <summary>
    /// Gets all available displays
    /// </summary>
    public IReadOnlyList<DisplayInfo> Displays => _displays;

    /// <summary>
    /// Sets the display configuration from the host
    /// </summary>
    public void SetDisplayConfig(DisplayConfigMessage config)
    {
        _displays = config.Displays.ToList();

        if (config.ActiveDisplayIndex >= 0 && config.ActiveDisplayIndex < _displays.Count)
        {
            _activeDisplay = _displays[config.ActiveDisplayIndex];
        }
        else if (_displays.Count > 0)
        {
            _activeDisplay = _displays[0];
        }
    }

    /// <summary>
    /// Sets the source (client) display dimensions
    /// </summary>
    public void SetSourceDimensions(int width, int height)
    {
        _sourceWidth = width;
        _sourceHeight = height;
    }

    /// <summary>
    /// Maps a client coordinate to host coordinate
    /// </summary>
    /// <param name="clientX">X coordinate in client space</param>
    /// <param name="clientY">Y coordinate in client space</param>
    /// <returns>Mapped coordinate in host space</returns>
    public (int HostX, int HostY) MapToHost(int clientX, int clientY)
    {
        if (_activeDisplay == null || _sourceWidth <= 0 || _sourceHeight <= 0)
        {
            return (clientX, clientY);
        }

        // Scale from client dimensions to host dimensions
        var scaleX = (double)_activeDisplay.Width / _sourceWidth;
        var scaleY = (double)_activeDisplay.Height / _sourceHeight;

        // Apply scaling
        var hostX = (int)(clientX * scaleX);
        var hostY = (int)(clientY * scaleY);

        // Clamp to display bounds
        hostX = Math.Clamp(hostX, 0, _activeDisplay.Width - 1);
        hostY = Math.Clamp(hostY, 0, _activeDisplay.Height - 1);

        // Add virtual desktop offset for multi-monitor
        hostX += _activeDisplay.OffsetX;
        hostY += _activeDisplay.OffsetY;

        return (hostX, hostY);
    }

    /// <summary>
    /// Maps a host coordinate to client coordinate (for cursor position feedback)
    /// </summary>
    public (int ClientX, int ClientY) MapToClient(int hostX, int hostY)
    {
        if (_activeDisplay == null || _sourceWidth <= 0 || _sourceHeight <= 0)
        {
            return (hostX, hostY);
        }

        // Remove virtual desktop offset
        var localX = hostX - _activeDisplay.OffsetX;
        var localY = hostY - _activeDisplay.OffsetY;

        // Scale from host dimensions to client dimensions
        var scaleX = (double)_sourceWidth / _activeDisplay.Width;
        var scaleY = (double)_sourceHeight / _activeDisplay.Height;

        var clientX = (int)(localX * scaleX);
        var clientY = (int)(localY * scaleY);

        // Clamp to client bounds
        clientX = Math.Clamp(clientX, 0, _sourceWidth - 1);
        clientY = Math.Clamp(clientY, 0, _sourceHeight - 1);

        return (clientX, clientY);
    }

    /// <summary>
    /// Maps a relative movement delta to host space
    /// </summary>
    public (int DeltaX, int DeltaY) MapDeltaToHost(int deltaX, int deltaY)
    {
        if (_activeDisplay == null || _sourceWidth <= 0 || _sourceHeight <= 0)
        {
            return (deltaX, deltaY);
        }

        var scaleX = (double)_activeDisplay.Width / _sourceWidth;
        var scaleY = (double)_activeDisplay.Height / _sourceHeight;

        return ((int)(deltaX * scaleX), (int)(deltaY * scaleY));
    }

    /// <summary>
    /// Switches the active display
    /// </summary>
    public bool SwitchDisplay(int displayIndex)
    {
        if (displayIndex < 0 || displayIndex >= _displays.Count)
        {
            return false;
        }

        _activeDisplay = _displays[displayIndex];
        return true;
    }

    /// <summary>
    /// Gets the scale factors for current mapping
    /// </summary>
    public (double ScaleX, double ScaleY) GetScaleFactors()
    {
        if (_activeDisplay == null || _sourceWidth <= 0 || _sourceHeight <= 0)
        {
            return (1.0, 1.0);
        }

        return (
            (double)_activeDisplay.Width / _sourceWidth,
            (double)_activeDisplay.Height / _sourceHeight
        );
    }

    /// <summary>
    /// Checks if a host coordinate is within the active display bounds
    /// </summary>
    public bool IsWithinActiveDisplay(int hostX, int hostY)
    {
        if (_activeDisplay == null)
        {
            return false;
        }

        return hostX >= _activeDisplay.OffsetX &&
               hostX < _activeDisplay.OffsetX + _activeDisplay.Width &&
               hostY >= _activeDisplay.OffsetY &&
               hostY < _activeDisplay.OffsetY + _activeDisplay.Height;
    }

    /// <summary>
    /// Gets the display at a given host coordinate
    /// </summary>
    public DisplayInfo? GetDisplayAtPoint(int hostX, int hostY)
    {
        foreach (var display in _displays)
        {
            if (hostX >= display.OffsetX &&
                hostX < display.OffsetX + display.Width &&
                hostY >= display.OffsetY &&
                hostY < display.OffsetY + display.Height)
            {
                return display;
            }
        }

        return null;
    }
}
