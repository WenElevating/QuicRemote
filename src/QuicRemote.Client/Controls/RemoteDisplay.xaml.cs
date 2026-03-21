using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuicRemote.Core.Media;
using QuicRemote.Core.Session;

namespace QuicRemote.Client.Controls;

/// <summary>
/// Remote desktop display control that renders decoded frames and captures input
/// </summary>
public partial class RemoteDisplay : FrameworkElement, IDisposable
{
    private WriteableBitmap? _bitmap;
    private bool _disposed;
    private int _lastWidth;
    private int _lastHeight;

    #region Dependency Properties

    public static readonly DependencyProperty SessionProperty =
        DependencyProperty.Register(nameof(Session), typeof(SessionContext), typeof(RemoteDisplay),
            new PropertyMetadata(null, OnSessionChanged));

    public static readonly DependencyProperty ScaleModeProperty =
        DependencyProperty.Register(nameof(ScaleMode), typeof(ScaleMode), typeof(RemoteDisplay),
            new PropertyMetadata(ScaleMode.AspectFit, OnScaleModeChanged));

    /// <summary>
    /// Gets or sets the session context
    /// </summary>
    public SessionContext? Session
    {
        get => (SessionContext?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>
    /// Gets or sets the scale mode
    /// </summary>
    public ScaleMode ScaleMode
    {
        get => (ScaleMode)GetValue(ScaleModeProperty);
        set => SetValue(ScaleModeProperty, value);
    }

    #endregion

    /// <summary>
    /// Event raised when input should be sent to the remote
    /// </summary>
    public event EventHandler<InputEvent>? InputRequired;

    public RemoteDisplay()
    {
        ClipToBounds = true;
        Focusable = true;

        // Input events
        MouseMove += OnMouseMove;
        MouseDown += OnMouseDown;
        MouseUp += OnMouseUp;
        MouseWheel += OnMouseWheel;
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
    }

    private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (RemoteDisplay)d;

        if (e.OldValue is SessionContext oldSession)
        {
            oldSession.FrameDecoded -= control.OnFrameDecoded;
        }

        if (e.NewValue is SessionContext newSession)
        {
            newSession.FrameDecoded += control.OnFrameDecoded;
        }
    }

    private static void OnScaleModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (RemoteDisplay)d;
        control.InvalidateVisual();
    }

    private void OnFrameDecoded(object? sender, DecodedFrame frame)
    {
        if (_disposed || frame == null)
        {
            return;
        }

        // Ensure bitmap is created with correct size
        EnsureBitmap(frame.Width, frame.Height);

        if (_bitmap == null)
        {
            return;
        }

        // Update bitmap from frame data
        try
        {
            _bitmap.Lock();

            // Copy frame data to bitmap
            if (frame.Data != IntPtr.Zero && frame.Stride > 0)
            {
                var sourceStride = frame.Stride;
                var destStride = _bitmap.BackBufferStride;
                var rowCount = Math.Min(frame.Height, _bitmap.PixelHeight);

                unsafe
                {
                    var source = (byte*)frame.Data;
                    var dest = (byte*)_bitmap.BackBuffer;

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

                _bitmap.AddDirtyRect(new Int32Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight));
            }

            _bitmap.Unlock();
        }
        catch
        {
            // Handle copy errors
        }

        // Request render
        Dispatcher.BeginInvoke(() => InvalidateVisual());
    }

    private void EnsureBitmap(int width, int height)
    {
        if (_bitmap == null || _lastWidth != width || _lastHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            _lastWidth = width;
            _lastHeight = height;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (_bitmap == null)
        {
            // Draw placeholder
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, ActualWidth, ActualHeight));
            return;
        }

        // Calculate destination rect based on scale mode
        var destRect = CalculateDestRect(_bitmap.PixelWidth, _bitmap.PixelHeight);

        // Draw frame
        dc.DrawImage(_bitmap, destRect);
    }

    private Rect CalculateDestRect(int frameWidth, int frameHeight)
    {
        if (ScaleMode == ScaleMode.Stretch)
        {
            return new Rect(0, 0, ActualWidth, ActualHeight);
        }

        var scaleX = ActualWidth / frameWidth;
        var scaleY = ActualHeight / frameHeight;

        double scale;
        switch (ScaleMode)
        {
            case ScaleMode.AspectFit:
                scale = Math.Min(scaleX, scaleY);
                break;
            case ScaleMode.AspectFill:
                scale = Math.Max(scaleX, scaleY);
                break;
            default:
                scale = 1;
                break;
        }

        var width = frameWidth * scale;
        var height = frameHeight * scale;
        var x = (ActualWidth - width) / 2;
        var y = (ActualHeight - height) / 2;

        return new Rect(x, y, width, height);
    }

    #region Input Handling

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_disposed) return;

        var pos = e.GetPosition(this);
        var (x, y) = ScaleInputCoordinates(pos.X, pos.Y);

        InputRequired?.Invoke(this, new InputEvent
        {
            Type = InputEventType.MouseMove,
            X = x,
            Y = y,
            Absolute = true
        });
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_disposed) return;
        CaptureMouse();

        InputRequired?.Invoke(this, new InputEvent
        {
            Type = InputEventType.MouseDown,
            MouseButton = ConvertMouseButton(e.ChangedButton)
        });
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_disposed) return;
        ReleaseMouseCapture();

        InputRequired?.Invoke(this, new InputEvent
        {
            Type = InputEventType.MouseUp,
            MouseButton = ConvertMouseButton(e.ChangedButton)
        });
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_disposed) return;

        InputRequired?.Invoke(this, new InputEvent
        {
            Type = InputEventType.MouseWheel,
            WheelDelta = e.Delta
        });
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_disposed) return;

        InputRequired?.Invoke(this, new InputEvent
        {
            Type = InputEventType.KeyDown,
            KeyCode = ConvertKey(e.Key)
        });

        e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (_disposed) return;

        InputRequired?.Invoke(this, new InputEvent
        {
            Type = InputEventType.KeyUp,
            KeyCode = ConvertKey(e.Key)
        });

        e.Handled = true;
    }

    private (int x, int y) ScaleInputCoordinates(double screenX, double screenY)
    {
        if (_bitmap == null)
        {
            return ((int)screenX, (int)screenY);
        }

        var destRect = CalculateDestRect(_bitmap.PixelWidth, _bitmap.PixelHeight);

        var x = (int)((screenX - destRect.X) / destRect.Width * _bitmap.PixelWidth);
        var y = (int)((screenY - destRect.Y) / destRect.Height * _bitmap.PixelHeight);

        return (x, y);
    }

    private static Core.Media.MouseButton ConvertMouseButton(System.Windows.Input.MouseButton button)
    {
        return button switch
        {
            System.Windows.Input.MouseButton.Left => Core.Media.MouseButton.Left,
            System.Windows.Input.MouseButton.Right => Core.Media.MouseButton.Right,
            System.Windows.Input.MouseButton.Middle => Core.Media.MouseButton.Middle,
            System.Windows.Input.MouseButton.XButton1 => Core.Media.MouseButton.X1,
            System.Windows.Input.MouseButton.XButton2 => Core.Media.MouseButton.X2,
            _ => Core.Media.MouseButton.Left
        };
    }

    private static KeyCode ConvertKey(Key key)
    {
        return key switch
        {
            Key.A => KeyCode.A, Key.B => KeyCode.B, Key.C => KeyCode.C, Key.D => KeyCode.D,
            Key.E => KeyCode.E, Key.F => KeyCode.F, Key.G => KeyCode.G, Key.H => KeyCode.H,
            Key.I => KeyCode.I, Key.J => KeyCode.J, Key.K => KeyCode.K, Key.L => KeyCode.L,
            Key.M => KeyCode.M, Key.N => KeyCode.N, Key.O => KeyCode.O, Key.P => KeyCode.P,
            Key.Q => KeyCode.Q, Key.R => KeyCode.R, Key.S => KeyCode.S, Key.T => KeyCode.T,
            Key.U => KeyCode.U, Key.V => KeyCode.V, Key.W => KeyCode.W, Key.X => KeyCode.X,
            Key.Y => KeyCode.Y, Key.Z => KeyCode.Z,
            Key.D0 => KeyCode.D0, Key.D1 => KeyCode.D1, Key.D2 => KeyCode.D2, Key.D3 => KeyCode.D3,
            Key.D4 => KeyCode.D4, Key.D5 => KeyCode.D5, Key.D6 => KeyCode.D6, Key.D7 => KeyCode.D7,
            Key.D8 => KeyCode.D8, Key.D9 => KeyCode.D9,
            Key.F1 => KeyCode.F1, Key.F2 => KeyCode.F2, Key.F3 => KeyCode.F3, Key.F4 => KeyCode.F4,
            Key.F5 => KeyCode.F5, Key.F6 => KeyCode.F6, Key.F7 => KeyCode.F7, Key.F8 => KeyCode.F8,
            Key.F9 => KeyCode.F9, Key.F10 => KeyCode.F10, Key.F11 => KeyCode.F11, Key.F12 => KeyCode.F12,
            Key.Enter => KeyCode.Return,
            Key.Escape => KeyCode.Escape,
            Key.Space => KeyCode.Space,
            Key.Tab => KeyCode.Tab,
            Key.Back => KeyCode.Back,
            Key.Delete => KeyCode.Delete,
            Key.Insert => KeyCode.Insert,
            Key.Home => KeyCode.Home,
            Key.End => KeyCode.End,
            Key.PageUp => KeyCode.PageUp,
            Key.PageDown => KeyCode.PageDown,
            Key.Left => KeyCode.Left,
            Key.Right => KeyCode.Right,
            Key.Up => KeyCode.Up,
            Key.Down => KeyCode.Down,
            Key.LeftShift or Key.RightShift => KeyCode.Shift,
            Key.LeftCtrl or Key.RightCtrl => KeyCode.Control,
            Key.LeftAlt or Key.RightAlt => KeyCode.Alt,
            Key.CapsLock => KeyCode.CapsLock,
            Key.NumPad0 => KeyCode.NumPad0, Key.NumPad1 => KeyCode.NumPad1,
            Key.NumPad2 => KeyCode.NumPad2, Key.NumPad3 => KeyCode.NumPad3,
            Key.NumPad4 => KeyCode.NumPad4, Key.NumPad5 => KeyCode.NumPad5,
            Key.NumPad6 => KeyCode.NumPad6, Key.NumPad7 => KeyCode.NumPad7,
            Key.NumPad8 => KeyCode.NumPad8, Key.NumPad9 => KeyCode.NumPad9,
            _ => KeyCode.None
        };
    }

    #endregion

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(
            double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 600 : availableSize.Height
        );
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (Session != null)
        {
            Session.FrameDecoded -= OnFrameDecoded;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Scale mode for remote display
/// </summary>
public enum ScaleMode
{
    AspectFit,
    AspectFill,
    Stretch
}
