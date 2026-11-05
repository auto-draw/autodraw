using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using SharpHook;
using SharpHook.Native;
#if WINDOWS
using SimWinInput;
#endif

namespace Autodraw;

public class Input
{
    //// Variables

    // Private
    private static readonly EventSimulator eventSim = new();

    // Public
    public static TaskPoolGlobalHook taskHook = new();
    public static Vector2 mousePos;
    public static bool forceUio = false;
    public static event EventHandler? MousePosUpdate;
    public static PixelPoint primaryScreenBounds;

    //// Functions

    // Core

    private static bool isUio()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        return !isWindows || forceUio;
    }

    public static void Start()
    {
        if (taskHook.IsRunning) return;
        if (taskHook.IsDisposed) return; // Avalonia Preview Fix.
        primaryScreenBounds = MainWindow.CurrentMainWindow.Screens.Primary.Bounds.TopLeft; // updates if main screen orientation changes

        taskHook.MouseMoved += (sender, e) =>
        {
            mousePos = new Vector2(e.Data.X, e.Data.Y);
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) // || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                mousePos = new Vector2(mousePos.X + primaryScreenBounds.X, mousePos.Y + primaryScreenBounds.Y);
            MousePosUpdate?.Invoke(null, EventArgs.Empty);
        };

        taskHook.RunAsync();
    }

    public static void Stop()
    {
        // Never really need to call this UNLESS, we are closing the software.
        taskHook.Dispose();
    }

    // Movement

    public static void MoveTo(short x, short y)
    {
#if WINDOWS
            SimMouse.Act(SimMouse.Action.MoveOnly, x, y);
            mousePos = new Vector2(x, y);
#else
            eventSim.SimulateMouseMovement(x, y);
#endif
    }

    public static void MoveBy(short xOffset, short yOffset)
    {
#if WINDOWS
        SimMouse.Act(SimMouse.Action.MoveOnly, xOffset + (short)mousePos.X, yOffset + (short)mousePos.Y);
        mousePos = new Vector2(xOffset + (short)mousePos.X, yOffset + (short)mousePos.Y);
#else
        eventSim.SimulateMouseMovementRelative(xOffset, yOffset);
#endif
    }

    // Click Handling

    public static void SendClick(byte mouseType)
    {
#if WINDOWS
        var buttonDown = mouseType == MouseTypes.MouseLeft
            ? SimMouse.Action.LeftButtonDown
            : SimMouse.Action.RightButtonDown;
        var buttonUp = mouseType == MouseTypes.MouseLeft
            ? SimMouse.Action.LeftButtonUp
            : SimMouse.Action.RightButtonUp;
        SimMouse.Act(buttonDown, (int)mousePos.X, (int)mousePos.Y);
        SimMouse.Act(buttonUp, (int)mousePos.X, (int)mousePos.Y);
#else
        var button = mouseType == MouseTypes.MouseLeft ? MouseButton.Button1 : MouseButton.Button2;
        eventSim.SimulateMousePress(button);
        eventSim.SimulateMouseRelease(button);
#endif
    }

    public static void SendClickDown(byte mouseType)
    {
#if WINDOWS
        var buttonDown = mouseType == MouseTypes.MouseLeft
            ? SimMouse.Action.LeftButtonDown
            : SimMouse.Action.RightButtonDown;
        SimMouse.Act(buttonDown, (int)mousePos.X, (int)mousePos.Y);
#else
        var button = mouseType == MouseTypes.MouseLeft ? MouseButton.Button1 : MouseButton.Button2;
        eventSim.SimulateMousePress(button);
#endif
    }

    public static void SendClickUp(byte mouseType)
    {
#if WINDOWS
        var buttonUp = mouseType == MouseTypes.MouseLeft
            ? SimMouse.Action.LeftButtonUp
            : SimMouse.Action.RightButtonUp;
        SimMouse.Act(buttonUp, (int)mousePos.X, (int)mousePos.Y);
#else
        var button = mouseType == MouseTypes.MouseLeft ? MouseButton.Button1 : MouseButton.Button2;
        eventSim.SimulateMouseRelease(button);
#endif
    }

    public static void SendKeyDown(KeyCode keyCode)
    {
        eventSim.SimulateKeyPress(keyCode);
    }
    public static void SendKeyUp(KeyCode keyCode)
    {
        eventSim.SimulateKeyRelease(keyCode);
    }
    public static void SendText(string text)
    {
#if WINDOWS
        SendUnicodeText(text);
#else
        eventSim.SimulateTextEntry(text);
#endif
    }

    /// <summary>Replaces the contents of the currently focused native text control.</summary>
    public static void ReplaceFocusedText(string text)
    {
#if WINDOWS
        SendVirtualKey(0x11, false); // Ctrl
        SendVirtualKey(0x41, false); // A
        SendVirtualKey(0x41, true);
        SendVirtualKey(0x11, true);
        SendUnicodeText(text);
        SendVirtualKey(0x0D, false); // Enter
        SendVirtualKey(0x0D, true);
#else
        eventSim.SimulateKeyPress(KeyCode.VcLeftControl);
        eventSim.SimulateKeyPress(KeyCode.VcA);
        eventSim.SimulateKeyRelease(KeyCode.VcA);
        eventSim.SimulateKeyRelease(KeyCode.VcLeftControl);
        eventSim.SimulateTextEntry(text);
        eventSim.SimulateKeyPress(KeyCode.VcEnter);
        eventSim.SimulateKeyRelease(KeyCode.VcEnter);
#endif
    }

#if WINDOWS
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeInputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)] public NativeKeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, NativeInput[] inputs, int inputSize);

    private static void SendVirtualKey(ushort virtualKey, bool keyUp)
    {
        var input = new NativeInput
        {
            Type = InputKeyboard,
            Data = new NativeInputUnion
            {
                Keyboard = new NativeKeyboardInput { VirtualKey = virtualKey, Flags = keyUp ? KeyEventKeyUp : 0 }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<NativeInput>());
    }

    private static void SendUnicodeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var inputs = new NativeInput[text.Length * 2];
        for (var i = 0; i < text.Length; i++)
        {
            inputs[i * 2] = new NativeInput
            {
                Type = InputKeyboard,
                Data = new NativeInputUnion
                {
                    Keyboard = new NativeKeyboardInput { ScanCode = text[i], Flags = KeyEventUnicode }
                }
            };
            inputs[i * 2 + 1] = new NativeInput
            {
                Type = InputKeyboard,
                Data = new NativeInputUnion
                {
                    Keyboard = new NativeKeyboardInput { ScanCode = text[i], Flags = KeyEventUnicode | KeyEventKeyUp }
                }
            };
        }
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
    }
#endif

    public static class MouseTypes
    {
        public static byte MouseLeft = 1;
        public static byte MouseRight = 2;
    }
}
