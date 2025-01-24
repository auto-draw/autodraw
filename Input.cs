using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using SharpHook;
using SharpHook.Native;

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
        taskHook.Dispose();
    }

    // Movement

    public static void MoveTo(short x, short y)
    {
        eventSim.SimulateMouseMovement(x, y);
    }

    public static void MoveBy(short xOffset, short yOffset)
    {
        eventSim.SimulateMouseMovementRelative(xOffset, yOffset);
    }

    // Click Handling

    public static void SendClick(byte mouseType)
    {
        var button = mouseType == MouseTypes.MouseLeft ? MouseButton.Button1 : MouseButton.Button2;
        eventSim.SimulateMousePress(button);
        eventSim.SimulateMouseRelease(button);
    }

    public static void SendClickDown(byte mouseType)
    {
        var button = mouseType == MouseTypes.MouseLeft ? MouseButton.Button1 : MouseButton.Button2;
        eventSim.SimulateMousePress(button);
    }

    public static void SendClickUp(byte mouseType)
    {
        var button = mouseType == MouseTypes.MouseLeft ? MouseButton.Button1 : MouseButton.Button2;
        eventSim.SimulateMouseRelease(button);
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
        eventSim.SimulateTextEntry(text);
    }

    public static class MouseTypes
    {
        public static byte MouseLeft = 1;
        public static byte MouseRight = 2;
    }
}