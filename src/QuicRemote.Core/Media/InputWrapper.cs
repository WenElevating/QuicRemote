using System;

namespace QuicRemote.Core.Media;

/// <summary>
/// Managed wrapper for input injection functionality
/// </summary>
public class InputWrapper : IDisposable
{
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Initializes the input injection system
    /// </summary>
    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
        {
            return;
        }

        var result = NativeMethods.QR_Input_Initialize();
        NativeMethods.ThrowOnError((NativeMethods.QR_Result)result);

        _initialized = true;
    }

    /// <summary>
    /// Moves the mouse cursor
    /// </summary>
    /// <param name="x">X coordinate</param>
    /// <param name="y">Y coordinate</param>
    /// <param name="absolute">Whether to use absolute coordinates</param>
    public void MouseMove(int x, int y, bool absolute = true)
    {
        EnsureInitialized();
        var result = NativeMethods.QR_Input_MouseMove(x, y, absolute ? 1 : 0);
        NativeMethods.ThrowOnError((NativeMethods.QR_Result)result);
    }

    /// <summary>
    /// Performs a mouse button action
    /// </summary>
    public void MouseButton(MouseButton button, ButtonAction action)
    {
        EnsureInitialized();
        var result = NativeMethods.QR_Input_MouseButton(
            (NativeMethods.QR_MouseButton)button,
            (NativeMethods.QR_ButtonAction)action);
        NativeMethods.ThrowOnError((NativeMethods.QR_Result)result);
    }

    /// <summary>
    /// Presses a mouse button
    /// </summary>
    public void MouseDown(MouseButton button)
    {
        MouseButton(button, ButtonAction.Press);
    }

    /// <summary>
    /// Releases a mouse button
    /// </summary>
    public void MouseUp(MouseButton button)
    {
        MouseButton(button, ButtonAction.Release);
    }

    /// <summary>
    /// Performs a mouse click (press and release)
    /// </summary>
    public void MouseClick(MouseButton button)
    {
        MouseDown(button);
        MouseUp(button);
    }

    /// <summary>
    /// Scrolls the mouse wheel
    /// </summary>
    /// <param name="delta">Scroll amount (positive = up, negative = down)</param>
    /// <param name="horizontal">Whether to scroll horizontally</param>
    public void MouseWheel(int delta, bool horizontal = false)
    {
        EnsureInitialized();
        var result = NativeMethods.QR_Input_MouseWheel(delta, horizontal ? 1 : 0);
        NativeMethods.ThrowOnError((NativeMethods.QR_Result)result);
    }

    /// <summary>
    /// Performs a keyboard key action
    /// </summary>
    public void Key(KeyCode key, KeyAction action)
    {
        EnsureInitialized();
        var result = NativeMethods.QR_Input_Key((ushort)key, (NativeMethods.QR_KeyAction)action);
        NativeMethods.ThrowOnError((NativeMethods.QR_Result)result);
    }

    /// <summary>
    /// Presses a keyboard key
    /// </summary>
    public void KeyDown(KeyCode key)
    {
        Key(key, KeyAction.Press);
    }

    /// <summary>
    /// Releases a keyboard key
    /// </summary>
    public void KeyUp(KeyCode key)
    {
        Key(key, KeyAction.Release);
    }

    /// <summary>
    /// Performs a key press (press and release)
    /// </summary>
    public void KeyPress(KeyCode key)
    {
        KeyDown(key);
        KeyUp(key);
    }

    /// <summary>
    /// Types text by sending individual key presses
    /// </summary>
    public void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        EnsureInitialized();

        foreach (var c in text)
        {
            // Map character to virtual key code
            var vk = CharToVirtualKey(c);
            if (vk != 0)
            {
                KeyPress((KeyCode)vk);
            }
        }
    }

    private void EnsureInitialized()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initialized)
        {
            throw new InvalidOperationException("Input wrapper not initialized. Call Initialize() first.");
        }
    }

    private static ushort CharToVirtualKey(char c)
    {
        // Basic ASCII character mapping
        if (c >= 'A' && c <= 'Z')
        {
            return (ushort)c;
        }
        if (c >= 'a' && c <= 'z')
        {
            return (ushort)(c - 'a' + 'A');
        }
        if (c >= '0' && c <= '9')
        {
            return (ushort)c;
        }

        // Special characters
        return c switch
        {
            ' ' => 0x20,      // Space
            '\t' => 0x09,     // Tab
            '\n' => 0x0D,     // Enter
            '\r' => 0x0D,     // Enter
            '.' => 0xBE,      // Period
            ',' => 0xBC,      // Comma
            ';' => 0xBA,      // Semicolon
            ':' => 0xBA,      // Colon (same as semicolon with shift)
            '/' => 0xBF,      // Slash
            '\\' => 0xDC,     // Backslash
            '\'' => 0xDE,     // Quote
            '"' => 0xDE,      // Double quote (same as quote with shift)
            '-' => 0xBD,      // Minus
            '=' => 0xBB,      // Equals
            '[' => 0xDB,      // Left bracket
            ']' => 0xDD,      // Right bracket
            _ => 0
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_initialized)
        {
            NativeMethods.QR_Input_Shutdown();
            _initialized = false;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Mouse button enumeration
/// </summary>
public enum MouseButton
{
    Left = 0,
    Right = 1,
    Middle = 2,
    X1 = 3,
    X2 = 4
}

/// <summary>
/// Button action enumeration
/// </summary>
public enum ButtonAction
{
    Press = 0,
    Release = 1
}

/// <summary>
/// Key action enumeration
/// </summary>
public enum KeyAction
{
    Press = 0,
    Release = 1
}

/// <summary>
/// Keyboard virtual key codes (Windows VK_* subset)
/// </summary>
public enum KeyCode : ushort
{
    None = 0,

    // Function keys
    Back = 0x08,
    Tab = 0x09,
    Return = 0x0D,
    Shift = 0x10,
    Control = 0x11,
    Alt = 0x12,
    Escape = 0x1B,
    Space = 0x20,
    Delete = 0x2E,

    // Arrow keys
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,

    // Letter keys
    A = 0x41, B = 0x42, C = 0x43, D = 0x44, E = 0x45, F = 0x46, G = 0x47,
    H = 0x48, I = 0x49, J = 0x4A, K = 0x4B, L = 0x4C, M = 0x4D, N = 0x4E,
    O = 0x4F, P = 0x50, Q = 0x51, R = 0x52, S = 0x53, T = 0x54, U = 0x55,
    V = 0x56, W = 0x57, X = 0x58, Y = 0x59, Z = 0x5A,

    // Number keys
    D0 = 0x30, D1 = 0x31, D2 = 0x32, D3 = 0x33, D4 = 0x34,
    D5 = 0x35, D6 = 0x36, D7 = 0x37, D8 = 0x38, D9 = 0x39,

    // Function keys
    F1 = 0x70, F2 = 0x71, F3 = 0x72, F4 = 0x73, F5 = 0x74,
    F6 = 0x75, F7 = 0x76, F8 = 0x77, F9 = 0x78, F10 = 0x79,
    F11 = 0x7A, F12 = 0x7B,

    // Navigation keys
    Insert = 0x2D,
    Home = 0x24,
    End = 0x23,
    PageUp = 0x21,
    PageDown = 0x22,
    CapsLock = 0x14,
    NumLock = 0x90,
    ScrollLock = 0x91,

    // Numpad
    NumPad0 = 0x60, NumPad1 = 0x61, NumPad2 = 0x62, NumPad3 = 0x63,
    NumPad4 = 0x64, NumPad5 = 0x65, NumPad6 = 0x66, NumPad7 = 0x67,
    NumPad8 = 0x68, NumPad9 = 0x69,
    Multiply = 0x6A,
    Add = 0x6B,
    Subtract = 0x6D,
    Decimal = 0x6E,
    Divide = 0x6F,
}
