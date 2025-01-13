using System;
using System.Numerics;
using System.Runtime.InteropServices;
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

        taskHook.MouseMoved += (sender, e) =>
        {
            mousePos = new Vector2(e.Data.X, e.Data.Y);
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
        if (isUio())
        {
            eventSim.SimulateMouseMovement(x, y);
        }
        else
        {
#if WINDOWS
            SimMouse.Act(SimMouse.Action.MoveOnly, x, y);
            mousePos = new Vector2(x, y);
#else
            // FALLBACK INCASE I MESS UP BUILD
            eventSim.SimulateMouseMovement(x, y);
#endif
        }
    }

    public static void MoveBy(short xOffset, short yOffset)
    {
        if (isUio())
        {
            eventSim.SimulateMouseMovementRelative(xOffset, yOffset);
        }
        else
        {
#if WINDOWS
            SimMouse.Act(SimMouse.Action.MoveOnly, xOffset + (short)mousePos.X, yOffset + (short)mousePos.Y);
            mousePos = new Vector2(xOffset + (short)mousePos.X, yOffset + (short)mousePos.Y);
#else
            // FALLBACK INCASE I MESS UP BUILD
            eventSim.SimulateMouseMovementRelative(xOffset, yOffset);

#endif
        }
    }

    // Click Handling

    public static void SendClick(byte mouseType)
    {
        if (isUio())
        {
            var button = mouseType == MouseTypes.MouseLeft ? MouseButton.Button1 : MouseButton.Button2;
            eventSim.SimulateMousePress(button);
            eventSim.SimulateMouseRelease(button);
        }
        else
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
            // FALLBACK INCASE I MESS UP BUILD
            SharpHook.Native.MouseButton button =
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        mouseType == MouseTypes.MouseLeft ? SharpHook.Native.MouseButton.Button1 : SharpHook.Native.MouseButton.Button2;
            eventSim.SimulateMousePress(button);
            eventSim.SimulateMouseRelease(button);
#endif
        }
    }

    public static void SendClickDown(byte mouseType)
    {
        if (isUio())
        {
            var button = mouseType == MouseTypes.MouseLeft ? MouseButton.Button1 : MouseButton.Button2;
            eventSim.SimulateMousePress(button);
        }
        else
        {
#if WINDOWS
            var buttonDown = mouseType == MouseTypes.MouseLeft
                ? SimMouse.Action.LeftButtonDown
                : SimMouse.Action.RightButtonDown;
            SimMouse.Act(buttonDown, (int)mousePos.X, (int)mousePos.Y);
#else
            // FALLBACK INCASE I MESS UP BUILD
            SharpHook.Native.MouseButton button =
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       mouseType == MouseTypes.MouseLeft ? SharpHook.Native.MouseButton.Button1 : SharpHook.Native.MouseButton.Button2;
            eventSim.SimulateMousePress(button);
#endif
        }
    }

    public static void SendClickUp(byte mouseType)
    {
        if (isUio())
        {
            var button = mouseType == MouseTypes.MouseLeft ? MouseButton.Button1 : MouseButton.Button2;
            eventSim.SimulateMouseRelease(button);
        }
        else
        {
#if WINDOWS
            var buttonUp = mouseType == MouseTypes.MouseLeft
                ? SimMouse.Action.LeftButtonUp
                : SimMouse.Action.RightButtonUp;
            SimMouse.Act(buttonUp, (int)mousePos.X, (int)mousePos.Y);
#else
                // FALLBACK INCASE I MESS UP BUILD
                SharpHook.Native.MouseButton button = 
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           mouseType == MouseTypes.MouseLeft ? SharpHook.Native.MouseButton.Button1 : SharpHook.Native.MouseButton.Button2;
                eventSim.SimulateMouseRelease(button);
#endif
        }
    }

    public static class MouseTypes
    {
        public static byte MouseLeft = 1;
        public static byte MouseRight = 2;
    }
}