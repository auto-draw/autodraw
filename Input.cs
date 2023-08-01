using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using SharpHook;
#if WINDOWS 
using SimWinInput;
#endif

namespace Autodraw
{
    public class Input
    {
        //// Variables

        // Private
        private static EventSimulator eventSim = new();

        // Public
        public static TaskPoolGlobalHook taskHook = new();
        public static Vector2 mousePos = new();
        public static event EventHandler? MousePosUpdate;
        public static bool forceUio = false;

        //// Functions

        // Core

        private static bool isUio()
        {
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            return !isWindows || forceUio;
        }

        public static void Start()
        {
            if (taskHook.IsRunning) return;
            if (taskHook.IsDisposed) return; // Avalonia Preview Fix.

            taskHook.MouseMoved += (object? sender, MouseHookEventArgs e) => { mousePos = new Vector2(e.Data.X, e.Data.Y); MousePosUpdate?.Invoke(null, EventArgs.Empty); };

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
                mousePos = new Vector2(xOffset, yOffset);
#endif
            }
        }

        // Click Handling

        public static void SendClick(byte mouseType)
        {
            if (isUio())
            {
                SharpHook.Native.MouseButton button = mouseType == MouseTypes.MouseLeft ? SharpHook.Native.MouseButton.Button1 : SharpHook.Native.MouseButton.Button2;
                eventSim.SimulateMousePress(button);
                eventSim.SimulateMouseRelease(button);
            }
            else
            {
#if WINDOWS
                SimMouse.Action buttonDown = mouseType == MouseTypes.MouseLeft ? SimMouse.Action.LeftButtonDown : SimMouse.Action.RightButtonDown;
                SimMouse.Action buttonUp = mouseType == MouseTypes.MouseLeft ? SimMouse.Action.LeftButtonUp : SimMouse.Action.RightButtonUp;

                SimMouse.Act(buttonDown, (int)mousePos.X, (int)mousePos.Y);
                SimMouse.Act(buttonUp, (int)mousePos.X, (int)mousePos.Y);
#endif
            }
        }

        public static void SendClickDown(byte mouseType)
        {
            if (isUio())
            {
                SharpHook.Native.MouseButton button = mouseType == MouseTypes.MouseLeft ? SharpHook.Native.MouseButton.Button1 : SharpHook.Native.MouseButton.Button2;
                eventSim.SimulateMousePress(button);
            }
            else
            {
#if WINDOWS
                SimMouse.Action buttonDown = mouseType == MouseTypes.MouseLeft ? SimMouse.Action.LeftButtonDown : SimMouse.Action.RightButtonDown;
                SimMouse.Act(buttonDown, (int)mousePos.X, (int)mousePos.Y);
#endif
            }
        }

        public static void SendClickUp(byte mouseType)
        {
            if (isUio())
            {
                SharpHook.Native.MouseButton button = mouseType == MouseTypes.MouseLeft ? SharpHook.Native.MouseButton.Button1 : SharpHook.Native.MouseButton.Button2;
                eventSim.SimulateMouseRelease(button);
            }
            else
            {
#if WINDOWS
                SimMouse.Action buttonUp = mouseType == MouseTypes.MouseLeft ? SimMouse.Action.LeftButtonUp : SimMouse.Action.RightButtonUp;
                SimMouse.Act(buttonUp, (int)mousePos.X, (int)mousePos.Y);
#endif
            }
        }

        public static class MouseTypes
        {
            public static byte MouseLeft = 1;
            public static byte MouseRight = 2;
        }
    }
}
