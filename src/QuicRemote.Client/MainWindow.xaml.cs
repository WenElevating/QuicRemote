using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using QuicRemote.Client.ViewModels;
using QuicRemote.Core.Media;
using QuicRemote.Core.Session;
using static QuicRemote.Core.Media.InputWrapper;

namespace QuicRemote.Client;

public partial class MainWindow : Window
{
    private WriteableBitmap? _remoteBitmap;
    private int _lastWidth;
    private int _lastHeight;
    private WindowState _previousWindowState;
    private WindowStyle _previousWindowStyle;
    private ResizeMode _previousResizeMode;
    private double _previousLeft;
    private double _previousTop;
    private double _previousWidth;
    private double _previousHeight;
    private bool _isFullscreen;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Add converters to resources if not present
        if (!Resources.Contains("BoolToVisibilityConverter"))
        {
            Resources.Add("BoolToVisibilityConverter", new BoolToVisibilityConverter());
        }
        if (!Resources.Contains("ColorToBrushConverter"))
        {
            Resources.Add("ColorToBrushConverter", new ColorToBrushConverter());
        }
        if (!Resources.Contains("CountToVisibilityConverter"))
        {
            Resources.Add("CountToVisibilityConverter", new CountToVisibilityConverter());
        }
        if (!Resources.Contains("BoolToColorConverter"))
        {
            Resources.Add("BoolToColorConverter", new BoolToColorConverter());
        }

        // Subscribe to view model events
        if (DataContext is ConnectViewModel vm)
        {
            vm.Connected += OnConnected;
            vm.ConnectionFailed += OnConnectionFailed;
            vm.ToggleFullscreenRequested += OnToggleFullscreenRequested;

            // Subscribe to frame events
            var clientService = vm.GetClientService();
            clientService.FrameDecoded += OnFrameDecoded;
        }

        // Handle mouse events on remote image
        RemoteImage.MouseMove += OnRemoteMouseMove;
        RemoteImage.MouseDown += OnRemoteMouseDown;
        RemoteImage.MouseUp += OnRemoteMouseUp;
        RemoteImage.MouseWheel += OnRemoteMouseWheel;

        // Handle keyboard events
        RemoteImage.KeyDown += OnRemoteKeyDown;
        RemoteImage.KeyUp += OnRemoteKeyUp;
        RemoteImage.Focusable = true;
    }

    private void OnToggleFullscreenRequested(object? sender, EventArgs e)
    {
        ToggleFullscreen();
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            // Enter fullscreen
            _previousWindowState = WindowState;
            _previousWindowStyle = WindowStyle;
            _previousResizeMode = ResizeMode;
            _previousLeft = Left;
            _previousTop = Top;
            _previousWidth = Width;
            _previousHeight = Height;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal;
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            Topmost = true;

            _isFullscreen = true;
        }
        else
        {
            // Exit fullscreen
            WindowStyle = _previousWindowStyle;
            ResizeMode = _previousResizeMode;
            WindowState = _previousWindowState;
            Left = _previousLeft;
            Top = _previousTop;
            Width = _previousWidth;
            Height = _previousHeight;
            Topmost = false;

            _isFullscreen = false;
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        // F11 and Escape are handled via InputBindings
        // This is for any additional key handling if needed
    }

    private void OnToolbarMouseEnter(object sender, MouseEventArgs e)
    {
        if (RemoteToolbar != null)
        {
            RemoteToolbar.Opacity = 1.0;
        }
    }

    private void OnToolbarMouseLeave(object sender, MouseEventArgs e)
    {
        // Keep toolbar visible in non-fullscreen mode
        if (!_isFullscreen && RemoteToolbar != null)
        {
            RemoteToolbar.Opacity = 1.0;
        }
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Focus the remote image for keyboard input
            RemoteImage.Focus();
        });
    }

    private void OnConnectionFailed(object? sender, Exception ex)
    {
        Dispatcher.Invoke(() =>
        {
            _remoteBitmap = null;
            RemoteImage.Source = null;
        });
    }

    private void OnFrameDecoded(object? sender, DecodedFrame frame)
    {
        if (frame == null) return;

        Dispatcher.Invoke(() =>
        {
            try
            {
                // Ensure bitmap exists with correct size
                if (_remoteBitmap == null || _lastWidth != frame.Width || _lastHeight != frame.Height)
                {
                    _remoteBitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
                    _lastWidth = frame.Width;
                    _lastHeight = frame.Height;
                    RemoteImage.Source = _remoteBitmap;
                }

                // Copy frame data to bitmap
                if (_remoteBitmap != null && frame.Data != IntPtr.Zero && frame.Stride > 0)
                {
                    _remoteBitmap.Lock();

                    unsafe
                    {
                        var source = (byte*)frame.Data;
                        var dest = (byte*)_remoteBitmap.BackBuffer;
                        var rowCount = Math.Min(frame.Height, _remoteBitmap.PixelHeight);
                        var sourceStride = frame.Stride;
                        var destStride = _remoteBitmap.BackBufferStride;

                        for (int y = 0; y < rowCount; y++)
                        {
                            System.Buffer.MemoryCopy(
                                source + y * sourceStride,
                                dest + y * destStride,
                                Math.Min(sourceStride, destStride),
                                Math.Min(sourceStride, destStride)
                            );
                        }
                    }

                    _remoteBitmap.AddDirtyRect(new Int32Rect(0, 0, _remoteBitmap.PixelWidth, _remoteBitmap.PixelHeight));
                    _remoteBitmap.Unlock();
                }
            }
            catch
            {
                // Ignore frame copy errors
            }
        });
    }

    private void OnRemoteMouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not ConnectViewModel vm || !vm.IsConnected) return;

        var pos = e.GetPosition(RemoteImage);
        var (x, y) = ScaleCoordinates(pos.X, pos.Y);

        _ = vm.GetClientService().SendInputAsync(new InputEvent
        {
            Type = InputEventType.MouseMove,
            X = x,
            Y = y,
            Absolute = true
        });
    }

    private void OnRemoteMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ConnectViewModel vm || !vm.IsConnected) return;
        RemoteImage.CaptureMouse();

        _ = vm.GetClientService().SendInputAsync(new InputEvent
        {
            Type = InputEventType.MouseDown,
            MouseButton = ConvertMouseButton(e.ChangedButton)
        });
    }

    private void OnRemoteMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ConnectViewModel vm || !vm.IsConnected) return;
        RemoteImage.ReleaseMouseCapture();

        _ = vm.GetClientService().SendInputAsync(new InputEvent
        {
            Type = InputEventType.MouseUp,
            MouseButton = ConvertMouseButton(e.ChangedButton)
        });
    }

    private void OnRemoteMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not ConnectViewModel vm || !vm.IsConnected) return;

        _ = vm.GetClientService().SendInputAsync(new InputEvent
        {
            Type = InputEventType.MouseWheel,
            WheelDelta = e.Delta
        });
    }

    private void OnRemoteKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ConnectViewModel vm || !vm.IsConnected) return;

        // Don't send F11 (fullscreen toggle) or Escape (exit fullscreen)
        if (e.Key == Key.F11 || e.Key == Key.Escape) return;

        e.Handled = true;

        var keyCode = ConvertKey(e.Key);
        if (keyCode != KeyCode.None)
        {
            _ = vm.GetClientService().SendInputAsync(new InputEvent
            {
                Type = InputEventType.KeyDown,
                KeyCode = keyCode
            });
        }
    }

    private void OnRemoteKeyUp(object sender, KeyEventArgs e)
    {
        if (DataContext is not ConnectViewModel vm || !vm.IsConnected) return;

        // Don't send F11 (fullscreen toggle) or Escape (exit fullscreen)
        if (e.Key == Key.F11 || e.Key == Key.Escape) return;

        e.Handled = true;

        var keyCode = ConvertKey(e.Key);
        if (keyCode != KeyCode.None)
        {
            _ = vm.GetClientService().SendInputAsync(new InputEvent
            {
                Type = InputEventType.KeyUp,
                KeyCode = keyCode
            });
        }
    }

    private static KeyCode ConvertKey(Key key)
    {
        return key switch
        {
            Key.Back => KeyCode.Back,
            Key.Tab => KeyCode.Tab,
            Key.Enter => KeyCode.Return,
            Key.LeftShift => KeyCode.Shift,
            Key.RightShift => KeyCode.Shift,
            Key.LeftCtrl => KeyCode.Control,
            Key.RightCtrl => KeyCode.Control,
            Key.LeftAlt => KeyCode.Alt,
            Key.RightAlt => KeyCode.Alt,
            Key.Escape => KeyCode.Escape,
            Key.Space => KeyCode.Space,
            Key.Delete => KeyCode.Delete,
            Key.Left => KeyCode.Left,
            Key.Up => KeyCode.Up,
            Key.Right => KeyCode.Right,
            Key.Down => KeyCode.Down,
            Key.A => KeyCode.A,
            Key.B => KeyCode.B,
            Key.C => KeyCode.C,
            Key.D => KeyCode.D,
            Key.E => KeyCode.E,
            Key.F => KeyCode.F,
            Key.G => KeyCode.G,
            Key.H => KeyCode.H,
            Key.I => KeyCode.I,
            Key.J => KeyCode.J,
            Key.K => KeyCode.K,
            Key.L => KeyCode.L,
            Key.M => KeyCode.M,
            Key.N => KeyCode.N,
            Key.O => KeyCode.O,
            Key.P => KeyCode.P,
            Key.Q => KeyCode.Q,
            Key.R => KeyCode.R,
            Key.S => KeyCode.S,
            Key.T => KeyCode.T,
            Key.U => KeyCode.U,
            Key.V => KeyCode.V,
            Key.W => KeyCode.W,
            Key.X => KeyCode.X,
            Key.Y => KeyCode.Y,
            Key.Z => KeyCode.Z,
            Key.D0 => KeyCode.D0,
            Key.D1 => KeyCode.D1,
            Key.D2 => KeyCode.D2,
            Key.D3 => KeyCode.D3,
            Key.D4 => KeyCode.D4,
            Key.D5 => KeyCode.D5,
            Key.D6 => KeyCode.D6,
            Key.D7 => KeyCode.D7,
            Key.D8 => KeyCode.D8,
            Key.D9 => KeyCode.D9,
            Key.F1 => KeyCode.F1,
            Key.F2 => KeyCode.F2,
            Key.F3 => KeyCode.F3,
            Key.F4 => KeyCode.F4,
            Key.F5 => KeyCode.F5,
            Key.F6 => KeyCode.F6,
            Key.F7 => KeyCode.F7,
            Key.F8 => KeyCode.F8,
            Key.F9 => KeyCode.F9,
            Key.F10 => KeyCode.F10,
            Key.F11 => KeyCode.F11,
            Key.F12 => KeyCode.F12,
            Key.Insert => KeyCode.Insert,
            Key.Home => KeyCode.Home,
            Key.End => KeyCode.End,
            Key.PageUp => KeyCode.PageUp,
            Key.PageDown => KeyCode.PageDown,
            Key.CapsLock => KeyCode.CapsLock,
            Key.NumLock => KeyCode.NumLock,
            Key.Scroll => KeyCode.ScrollLock,
            Key.NumPad0 => KeyCode.NumPad0,
            Key.NumPad1 => KeyCode.NumPad1,
            Key.NumPad2 => KeyCode.NumPad2,
            Key.NumPad3 => KeyCode.NumPad3,
            Key.NumPad4 => KeyCode.NumPad4,
            Key.NumPad5 => KeyCode.NumPad5,
            Key.NumPad6 => KeyCode.NumPad6,
            Key.NumPad7 => KeyCode.NumPad7,
            Key.NumPad8 => KeyCode.NumPad8,
            Key.NumPad9 => KeyCode.NumPad9,
            Key.Multiply => KeyCode.Multiply,
            Key.Add => KeyCode.Add,
            Key.Subtract => KeyCode.Subtract,
            Key.Decimal => KeyCode.Decimal,
            Key.Divide => KeyCode.Divide,
            _ => KeyCode.None
        };
    }

    private (int x, int y) ScaleCoordinates(double screenX, double screenY)
    {
        if (_remoteBitmap == null || RemoteImage.ActualWidth == 0 || RemoteImage.ActualHeight == 0)
        {
            return (0, 0);
        }

        // Calculate the displayed image rect (Uniform stretch)
        var scaleX = RemoteImage.ActualWidth / _remoteBitmap.PixelWidth;
        var scaleY = RemoteImage.ActualHeight / _remoteBitmap.PixelHeight;
        var scale = Math.Min(scaleX, scaleY);

        var displayWidth = _remoteBitmap.PixelWidth * scale;
        var displayHeight = _remoteBitmap.PixelHeight * scale;
        var offsetX = (RemoteImage.ActualWidth - displayWidth) / 2;
        var offsetY = (RemoteImage.ActualHeight - displayHeight) / 2;

        // Convert screen coordinates to remote coordinates
        var x = (int)Math.Round((screenX - offsetX) / scale);
        var y = (int)Math.Round((screenY - offsetY) / scale);

        // Clamp to valid range
        x = Math.Clamp(x, 0, _remoteBitmap.PixelWidth - 1);
        y = Math.Clamp(y, 0, _remoteBitmap.PixelHeight - 1);

        return (x, y);
    }

    private static QuicRemote.Core.Media.MouseButton ConvertMouseButton(System.Windows.Input.MouseButton button)
    {
        return button switch
        {
            System.Windows.Input.MouseButton.Left => QuicRemote.Core.Media.MouseButton.Left,
            System.Windows.Input.MouseButton.Right => QuicRemote.Core.Media.MouseButton.Right,
            System.Windows.Input.MouseButton.Middle => QuicRemote.Core.Media.MouseButton.Middle,
            System.Windows.Input.MouseButton.XButton1 => QuicRemote.Core.Media.MouseButton.X1,
            System.Windows.Input.MouseButton.XButton2 => QuicRemote.Core.Media.MouseButton.X2,
            _ => QuicRemote.Core.Media.MouseButton.Left
        };
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            DragMove();
        }
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConnectViewModel vm)
        {
            vm.Password = PasswordBox.Password;
        }
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is ConnectViewModel vm)
        {
            vm.Connected -= OnConnected;
            vm.ConnectionFailed -= OnConnectionFailed;
            vm.ToggleFullscreenRequested -= OnToggleFullscreenRequested;

            var clientService = vm.GetClientService();
            clientService.FrameDecoded -= OnFrameDecoded;
            await vm.DisposeAsync();
        }
        base.OnClosing(e);
    }
}

/// <summary>
/// Converts bool to Visibility
/// </summary>
public class BoolToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var boolValue = value is bool b && b;

        // Check for "Inverse" parameter
        if (parameter is string p && p.ToString() == "Inverse")
        {
            boolValue = !boolValue;
        }

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return value is Visibility v && v == Visibility.Visible;
    }
}

/// <summary>
/// Converts color string to SolidColorBrush
/// </summary>
public class ColorToBrushConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is string colorStr)
        {
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorStr);
                return new System.Windows.Media.SolidColorBrush(color);
            }
            catch
            {
                return System.Windows.Media.Brushes.Gray;
            }
        }
        return System.Windows.Media.Brushes.Gray;
    }

    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new System.NotImplementedException();
    }
}

/// <summary>
/// Converts count to Visibility (visible if count > 0)
/// </summary>
public class CountToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is int count)
        {
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new System.NotImplementedException();
    }
}

/// <summary>
/// Converts bool to color string based on parameter
/// </summary>
public class BoolToColorConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var colors = (parameter as string)?.Split('|');
        if (colors == null || colors.Length != 2)
            return "#86868B";

        return value is bool b && b ? colors[0] : colors[1];
    }

    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new System.NotImplementedException();
    }
}
