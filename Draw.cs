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
using System.Collections;
using Tmds.DBus.Protocol;
using System.IO.Pipes;

namespace Autodraw
{
    // Most of this code is going to implement the same methods used for previous versions.
    // The reason I wish to do this, is because simply put, I cannot viably see another way of doing this that is more or less efficient.
    // The previous code was messy, but it's implementation for drawing was more or less, efficient and simple
    // I'm not here to over-engineer something if there's already a very stable method, I don't want to make anything unstable.

    // Maybe in the near future I'll program it to use an algorithm to try and go over each pixel without going back over
    // Because the current one loops back thru the stack if it hasn't touched everything, which sucks.

    public static class Drawing
    {
        // Variables

        private static int[,]? pixelArray;
        private static EventSimulator EventSim = new EventSimulator();
        public static TaskPoolGlobalHook InputHook = new TaskPoolGlobalHook();
        public static Vector2 MousePos = new Vector2(0,0);
        private static bool StartedHook = false;
        public static int pathValue = 12358764;
        private static int[] path = pathValue.ToString().Select(t => int.Parse(t.ToString())).ToArray();
        public static bool isDrawing = false;

        // Functions

        public static void Start()
        {
            if (StartedHook) { return; }

            InputHook.MouseMoved += (object? sender, MouseHookEventArgs e) => { MousePos = new Vector2(e.Data.X, e.Data.Y); };

            InputHook.RunAsync();
            StartedHook = true;
        }

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

                    pixelArray[x, y] = redByte == 0 ? 1 : 0;
                }
            }
            return;
        }

        private static unsafe void DrawArea(int _x, int _y, Pos startPos, Pos size)
        {
            ArrayList stack = new ArrayList();

            bool cont;
            while (true)
            {
                short x = (short)(_x + startPos.X);
                short y = (short)(_y + startPos.Y);
                EventSim.SimulateMouseMovement((short)x, (short)y);

                pixelArray[x, y] = 2;
                NOP(10000);
                System.Diagnostics.Debug.WriteLine(x);
                System.Diagnostics.Debug.WriteLine(y);
                System.Diagnostics.Debug.WriteLine(" ");

                cont = false;
                foreach (int i in Enumerable.Range(0, 7))
                {
                    switch (path[i])
                    {
                        // Not a fan of this "hard-coding" method but it is easiest.
                        case 1:
                            if (x <= 0 || y <= 0) break;
                            stack.Add(new Pos { X=_x, Y=_y });
                            _x -= 1;
                            _y -= 1;
                            cont = true;
                            break;
                        case 2:
                            if (y <= 0) break;
                            stack.Add(new Pos { X = _x, Y = _y });
                            _y -= 1;
                            cont = true;
                            break;
                        case 3:
                            if (y <= 0 || x >= size.X) break;
                            stack.Add(new Pos { X = _x, Y = _y });
                            _x += 1;
                            _y -= 1;
                            cont = true;
                            break;
                        case 4:
                            if (x <= 0) break;
                            stack.Add(new Pos { X = _x, Y = _y });
                            _x -= 1;
                            cont = true;
                            break;
                        case 5:
                            if (x >= size.X) break;
                            stack.Add(new Pos { X = _x, Y = _y });
                            _x += 1;
                            cont = true;
                            break;
                        case 6:
                            if (y >= size.Y || x <= 0) break;
                            stack.Add(new Pos { X = _x, Y = _y });
                            _x -= 1;
                            _y += 1;
                            cont = true;
                            break;
                        case 7:
                            if (y >= size.Y) break;
                            stack.Add(new Pos { X = _x, Y = _y });
                            _y += 1;
                            cont = true;
                            break;
                        case 8:
                            if (y >= size.Y || x >= size.X) break;
                            stack.Add(new Pos { X = _x, Y = _y });
                            _x += 1;
                            _y += 1;
                            cont = true;
                            break;
                    }
                }
                if (cont) continue;
                if (stack.Count < 1) { break; } else
                {
                    Pos backPos = (Pos)stack[^1];
                    _x = backPos.X;
                    _y = backPos.Y;

                    stack.Remove(backPos);
                };
            }
        }

        public static async Task<bool> Draw(SKBitmap bitmap)
        {
            isDrawing = true;
            path = pathValue.ToString().Select(t => int.Parse(t.ToString())).ToArray();

            Scan(bitmap);

            Pos StartPos = new Pos { X= (int)MousePos.X, Y= (int)MousePos.Y };

            if (pixelArray == null) { System.Diagnostics.Debug.WriteLine("pixelArray was never created."); }

            for (int _y = 0; _y < bitmap.Height; _y++)
            {
                int y = (_y + StartPos.Y);
                System.Diagnostics.Debug.WriteLine(y);
                for (int _x = 0; _x < bitmap.Width; _x++)
                {
                    if (pixelArray[_x, _y] == 1)
                    {
                        int x = (_x + StartPos.X);

                        await NOP(10000);

                        EventSim.SimulateMouseMovement((short)x, (short)y);

                        DrawArea(_x, _y, StartPos, new Pos { X = bitmap.Width, Y = bitmap.Height });
                    }
                }
            }

            isDrawing = false;
            return true;
        }

        class Pos
        {
            public int X { get; set; }
            public int Y { get; set; }
        }
    }
}