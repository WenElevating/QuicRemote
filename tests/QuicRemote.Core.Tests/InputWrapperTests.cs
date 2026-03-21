using QuicRemote.Core.Media;
using Xunit;

namespace QuicRemote.Core.Tests;

/// <summary>
/// Tests for input wrapper enumerations and types
/// </summary>
public class InputWrapperTests
{
    [Fact]
    public void MouseButton_EnumMatchesNativeEnum()
    {
        // Verify that our public enum matches the native enum
        Assert.Equal((int)MouseButton.Left, (int)NativeMethods.QR_MouseButton.Left);
        Assert.Equal((int)MouseButton.Right, (int)NativeMethods.QR_MouseButton.Right);
        Assert.Equal((int)MouseButton.Middle, (int)NativeMethods.QR_MouseButton.Middle);
        Assert.Equal((int)MouseButton.X1, (int)NativeMethods.QR_MouseButton.X1);
        Assert.Equal((int)MouseButton.X2, (int)NativeMethods.QR_MouseButton.X2);
    }

    [Fact]
    public void ButtonAction_EnumMatchesNativeEnum()
    {
        Assert.Equal((int)ButtonAction.Press, (int)NativeMethods.QR_ButtonAction.Press);
        Assert.Equal((int)ButtonAction.Release, (int)NativeMethods.QR_ButtonAction.Release);
    }

    [Fact]
    public void KeyAction_EnumMatchesNativeEnum()
    {
        Assert.Equal((int)KeyAction.Press, (int)NativeMethods.QR_KeyAction.Press);
        Assert.Equal((int)KeyAction.Release, (int)NativeMethods.QR_KeyAction.Release);
    }

    [Theory]
    [InlineData(KeyCode.A, 0x41)]
    [InlineData(KeyCode.Z, 0x5A)]
    [InlineData(KeyCode.D0, 0x30)]
    [InlineData(KeyCode.D9, 0x39)]
    [InlineData(KeyCode.F1, 0x70)]
    [InlineData(KeyCode.F12, 0x7B)]
    [InlineData(KeyCode.Return, 0x0D)]
    [InlineData(KeyCode.Escape, 0x1B)]
    [InlineData(KeyCode.Space, 0x20)]
    [InlineData(KeyCode.Left, 0x25)]
    [InlineData(KeyCode.Up, 0x26)]
    [InlineData(KeyCode.Right, 0x27)]
    [InlineData(KeyCode.Down, 0x28)]
    [InlineData(KeyCode.Shift, 0x10)]
    [InlineData(KeyCode.Control, 0x11)]
    [InlineData(KeyCode.Alt, 0x12)]
    public void KeyCode_HasCorrectVirtualKeyCodes(KeyCode key, ushort expectedVk)
    {
        Assert.Equal(expectedVk, (ushort)key);
    }

    [Fact]
    public void KeyCode_ContainsAllLetters()
    {
        var letters = new[] {
            KeyCode.A, KeyCode.B, KeyCode.C, KeyCode.D, KeyCode.E,
            KeyCode.F, KeyCode.G, KeyCode.H, KeyCode.I, KeyCode.J,
            KeyCode.K, KeyCode.L, KeyCode.M, KeyCode.N, KeyCode.O,
            KeyCode.P, KeyCode.Q, KeyCode.R, KeyCode.S, KeyCode.T,
            KeyCode.U, KeyCode.V, KeyCode.W, KeyCode.X, KeyCode.Y, KeyCode.Z
        };

        Assert.Equal(26, letters.Length);
        Assert.All(letters, key => Assert.True((ushort)key >= 0x41 && (ushort)key <= 0x5A));
    }

    [Fact]
    public void KeyCode_ContainsAllDigits()
    {
        var digits = new[] {
            KeyCode.D0, KeyCode.D1, KeyCode.D2, KeyCode.D3, KeyCode.D4,
            KeyCode.D5, KeyCode.D6, KeyCode.D7, KeyCode.D8, KeyCode.D9
        };

        Assert.Equal(10, digits.Length);
        Assert.All(digits, key => Assert.True((ushort)key >= 0x30 && (ushort)key <= 0x39));
    }

    [Fact]
    public void KeyCode_ContainsAllFunctionKeys()
    {
        var functionKeys = new[] {
            KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F5, KeyCode.F6,
            KeyCode.F7, KeyCode.F8, KeyCode.F9, KeyCode.F10, KeyCode.F11, KeyCode.F12
        };

        Assert.Equal(12, functionKeys.Length);
        // F1 = 0x70 (112), F12 = 0x7B (123)
        Assert.All(functionKeys, key => Assert.True((ushort)key >= 0x70 && (ushort)key <= 0x7B));
    }

    [Fact]
    public void KeyCode_ContainsNavigationKeys()
    {
        Assert.Equal(0x25, (ushort)KeyCode.Left);
        Assert.Equal(0x26, (ushort)KeyCode.Up);
        Assert.Equal(0x27, (ushort)KeyCode.Right);
        Assert.Equal(0x28, (ushort)KeyCode.Down);
        Assert.Equal(0x24, (ushort)KeyCode.Home);
        Assert.Equal(0x23, (ushort)KeyCode.End);
        Assert.Equal(0x21, (ushort)KeyCode.PageUp);
        Assert.Equal(0x22, (ushort)KeyCode.PageDown);
        Assert.Equal(0x2D, (ushort)KeyCode.Insert);
        Assert.Equal(0x2E, (ushort)KeyCode.Delete);
    }

    [Fact]
    public void KeyCode_ContainsNumpadKeys()
    {
        Assert.Equal(0x60, (ushort)KeyCode.NumPad0);
        Assert.Equal(0x61, (ushort)KeyCode.NumPad1);
        Assert.Equal(0x69, (ushort)KeyCode.NumPad9);
        Assert.Equal(0x6A, (ushort)KeyCode.Multiply);
        Assert.Equal(0x6B, (ushort)KeyCode.Add);
        Assert.Equal(0x6D, (ushort)KeyCode.Subtract);
        Assert.Equal(0x6E, (ushort)KeyCode.Decimal);
        Assert.Equal(0x6F, (ushort)KeyCode.Divide);
    }

    [Fact]
    public void InputWrapper_ThrowsWhenNotInitialized()
    {
        using var wrapper = new InputWrapper();

        // Should throw because Initialize() was not called
        Assert.Throws<InvalidOperationException>(() => wrapper.MouseMove(100, 100));
        Assert.Throws<InvalidOperationException>(() => wrapper.MouseButton(MouseButton.Left, ButtonAction.Press));
        Assert.Throws<InvalidOperationException>(() => wrapper.MouseWheel(1));
        Assert.Throws<InvalidOperationException>(() => wrapper.Key(KeyCode.A, KeyAction.Press));
    }

    [Fact]
    public void InputWrapper_ThrowsObjectDisposedAfterDispose()
    {
        var wrapper = new InputWrapper();
        wrapper.Dispose();

        Assert.Throws<ObjectDisposedException>(() => wrapper.MouseMove(100, 100));
        Assert.Throws<ObjectDisposedException>(() => wrapper.Initialize());
    }

    [Fact]
    public void InputWrapper_CanBeDisposedMultipleTimes()
    {
        var wrapper = new InputWrapper();
        wrapper.Dispose();
        wrapper.Dispose(); // Should not throw
    }
}
