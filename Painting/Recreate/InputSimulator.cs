using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Autodraw.Configuration;
using SharpHook.Native;

namespace Autodraw.Painting.Recreate;

/// <summary>Uses Win32 SendInput for both pointer and keyboard events.</summary>
public static class InputSimulatorFactory
{
    public static IInputSimulator Create()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows()) return new WindowsSendInputSimulator();
#endif
        return new LegacyInputSimulator();
    }
}

#if WINDOWS
/// <summary>
/// Direct SendInput implementation.  Absolute positioning with MOUSEEVENTF_VIRTUALDESK keeps the
/// pointer correct on multi-monitor and mixed-DPI desktops, which the old relative SimWin calls did
/// not guarantee.
/// </summary>
public sealed class WindowsSendInputSimulator : IInputSimulator
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;

    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseVirtualDesk = 0x4000;
    private const uint MouseAbsolute = 0x8000;

    private const uint KeyUp = 0x0002;
    private const uint KeyUnicode = 0x0004;

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, NativeInput[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    public Task MoveToAsync(ScreenPoint point, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SendAbsoluteMove(point.X, point.Y);
        return Task.CompletedTask;
    }

    public Task LeftDownAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SendMouse(MouseLeftDown, 0, 0);
        return Task.CompletedTask;
    }

    public Task LeftUpAsync(CancellationToken cancellationToken)
    {
        // Mouse-up must still be sent when the caller is cancelling, otherwise the target app can
        // remain in a drag.  The engine calls this with CancellationToken.None in its finally block.
        SendMouse(MouseLeftUp, 0, 0);
        return Task.CompletedTask;
    }

    public Task ClickAsync(ScreenPoint point, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SendAbsoluteMove(point.X, point.Y);
        SendMouse(MouseLeftDown, 0, 0);
        SendMouse(MouseLeftUp, 0, 0);
        return Task.CompletedTask;
    }

    public Task SelectAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SendVirtualKey(0x11, false); // Ctrl
        SendVirtualKey(0x41, false); // A
        SendVirtualKey(0x41, true);
        SendVirtualKey(0x11, true);
        return Task.CompletedTask;
    }

    public Task TypeTextAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text)) return Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        SendUnicodeText(text);
        return Task.CompletedTask;
    }

    public async Task ReplaceTextAsync(string text, CancellationToken cancellationToken)
    {
        await SelectAllAsync(cancellationToken).ConfigureAwait(false);
        await TypeTextAsync(text, cancellationToken).ConfigureAwait(false);
    }

    public Task PressEnterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SendVirtualKey(0x0D, false);
        SendVirtualKey(0x0D, true);
        return Task.CompletedTask;
    }

    private static void SendAbsoluteMove(int x, int y)
    {
        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        var width = Math.Max(1, GetSystemMetrics(SmCxVirtualScreen));
        var height = Math.Max(1, GetSystemMetrics(SmCyVirtualScreen));
        var normalizedX = (int)Math.Round((x - left) * 65535d / Math.Max(1, width - 1));
        var normalizedY = (int)Math.Round((y - top) * 65535d / Math.Max(1, height - 1));
        SendMouse(MouseMove | MouseAbsolute | MouseVirtualDesk, normalizedX, normalizedY);
    }

    private static void SendMouse(uint flags, int dx, int dy)
    {
        var input = new NativeInput
        {
            Type = InputMouse,
            Data = new InputUnion
            {
                Mouse = new MouseInput { Dx = dx, Dy = dy, Flags = flags }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<NativeInput>());
    }

    private static void SendVirtualKey(ushort virtualKey, bool keyUp)
    {
        var input = new NativeInput
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = keyUp ? KeyUp : 0
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<NativeInput>());
    }

    private static void SendUnicodeText(string text)
    {
        var inputs = new NativeInput[text.Length * 2];
        var index = 0;
        foreach (var character in text)
        {
            inputs[index++] = new NativeInput
            {
                Type = InputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput { ScanCode = character, Flags = KeyUnicode }
                }
            };
            inputs[index++] = new NativeInput
            {
                Type = InputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput { ScanCode = character, Flags = KeyUnicode | KeyUp }
                }
            };
        }
        if (inputs.Length > 0)
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
    }
}
#endif

/// <summary>Fallback for non-Windows development builds; on Windows the factory selects SendInput.</summary>
public sealed class LegacyInputSimulator : IInputSimulator
{
    public Task MoveToAsync(ScreenPoint point, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Input.MoveTo((short)point.X, (short)point.Y);
        return Task.CompletedTask;
    }

    public Task LeftDownAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Input.SendClickDown(Input.MouseTypes.MouseLeft);
        return Task.CompletedTask;
    }

    public Task LeftUpAsync(CancellationToken cancellationToken)
    {
        Input.SendClickUp(Input.MouseTypes.MouseLeft);
        return Task.CompletedTask;
    }

    public Task ClickAsync(ScreenPoint point, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Input.MoveTo((short)point.X, (short)point.Y);
        Input.SendClick(Input.MouseTypes.MouseLeft);
        return Task.CompletedTask;
    }

    public Task SelectAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Input.SendKeyDown(KeyCode.VcLeftControl);
        Input.SendKeyDown(KeyCode.VcA);
        Input.SendKeyUp(KeyCode.VcA);
        Input.SendKeyUp(KeyCode.VcLeftControl);
        return Task.CompletedTask;
    }

    public Task TypeTextAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Input.SendText(text);
        return Task.CompletedTask;
    }

    public async Task ReplaceTextAsync(string text, CancellationToken cancellationToken)
    {
        await SelectAllAsync(cancellationToken).ConfigureAwait(false);
        await TypeTextAsync(text, cancellationToken).ConfigureAwait(false);
    }

    public Task PressEnterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Input.SendKeyDown(KeyCode.VcEnter);
        Input.SendKeyUp(KeyCode.VcEnter);
        return Task.CompletedTask;
    }
}
