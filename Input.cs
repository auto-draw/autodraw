using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using SharpHook;
using SimWinInput;

namespace Autodraw
{
    public class Input
    {
        //// Variables

        // Private
        private static EventSimulator eventSim = new EventSimulator();

        // Public
        public static TaskPoolGlobalHook taskHook = new TaskPoolGlobalHook();
        public static Vector2 mousePos = new Vector2();
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

            taskHook.MouseMoved += (object? sender, MouseHookEventArgs e) => { mousePos = new Vector2(e.Data.X, e.Data.Y); };

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
                SimMouse.Act(SimMouse.Action.MoveOnly, x, y);
                mousePos = new Vector2(x, y);
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
                SimMouse.Act(SimMouse.Action.MoveOnly, xOffset + (short)mousePos.X, yOffset + (short)mousePos.Y);
                mousePos = new Vector2(xOffset, yOffset);
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
                SimMouse.Action buttonDown = mouseType == MouseTypes.MouseLeft ? SimMouse.Action.LeftButtonDown : SimMouse.Action.RightButtonDown;
                SimMouse.Action buttonUp = mouseType == MouseTypes.MouseLeft ? SimMouse.Action.LeftButtonUp : SimMouse.Action.RightButtonUp;

                SimMouse.Act(buttonDown, (int)mousePos.X, (int)mousePos.Y);
                SimMouse.Act(buttonUp, (int)mousePos.X, (int)mousePos.Y);

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
                SimMouse.Action buttonDown = mouseType == MouseTypes.MouseLeft ? SimMouse.Action.LeftButtonDown : SimMouse.Action.RightButtonDown;
                SimMouse.Act(buttonDown, (int)mousePos.X, (int)mousePos.Y);
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
                SimMouse.Action buttonUp = mouseType == MouseTypes.MouseLeft ? SimMouse.Action.LeftButtonUp : SimMouse.Action.RightButtonUp;
                SimMouse.Act(buttonUp, (int)mousePos.X, (int)mousePos.Y);
            }
        }

        public static class MouseTypes
        {
            public static byte MouseLeft = 1;
            public static byte MouseRight = 2;
        }
    }
}
