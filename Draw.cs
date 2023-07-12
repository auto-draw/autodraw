using Avalonia.Input;
using SkiaSharp;
using SharpHook;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpHook.Native;

namespace Autodraw
{
    // Most of this code is going to implement the same methods used for previous versions.
    // The reason I wish to do this, is because simply put, I cannot viably see another way of doing this that is more or less efficient.
    // The previous code was messy, but it's implementation for drawing was more or less, efficient and simple
    // I'm not here to over-engineer something if there's already a very stable method, I don't want to make anything unstable.

    public static class Drawing
    {
        // Variables

        private static int[,]? pixelArray;
        public static TaskPoolGlobalHook InputHook = new TaskPoolGlobalHook();
        public static Vector2 MousePos = new Vector2(0,0);
        public static bool Started = false;

        public static void Start()
        {
            if (Started) { return; }

            InputHook.MouseMoved += (object? sender, MouseHookEventArgs e) => { MousePos = new Vector2(e.Data.X, e.Data.Y); };

            InputHook.RunAsync();
            Started = true;
        }

        // Functions

        public async static Task NOP(long durationTicks)
        {
            var sw = Stopwatch.StartNew();

            while(sw.ElapsedTicks < durationTicks) { if (durationTicks - sw.ElapsedTicks > 150000) { await Task.Delay(1); } }
        }

        private static unsafe void Scan(SKBitmap bitmap)
        {
            pixelArray = new int[bitmap.Width, bitmap.Height];
            byte* bitPtr = (byte*)bitmap.GetPixels().ToPointer();


            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    byte redByte = *bitPtr++;
                    byte greenByte = *bitPtr++;
                    byte blueByte = *bitPtr++;
                    byte alphaByte = *bitPtr++;

                    pixelArray[x, y] = redByte == 255 ? 1 : 0;
                }
            }
            return;
        }

        public static async Task<bool> Draw(SKBitmap bitmap)
        {

            Scan(bitmap);

            EventSimulator EventSim = new EventSimulator();
            Vector2 StartPos = MousePos;

            System.Diagnostics.Debug.WriteLine(MousePos);

            if (pixelArray == null) { Debug.Fail("pixelArray was never created."); }

            for (int _y = 0; _y < bitmap.Height; _y++)
            {
                for (int _x = 0; _x < bitmap.Width; _x++)
                {
                    if (pixelArray[_x, _y] == 1)
                    {
                        int x = (int)(_x + StartPos.X);
                        int y = (int)(_y + StartPos.Y);

                        await NOP(1);

                        EventSim.SimulateMouseMovement((short)x, (short)y);
                    }
                }
            }

            return true;
        }
    }
}