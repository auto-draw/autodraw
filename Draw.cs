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
using Avalonia;

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

        public static int pathValue   = 12345678;
        public static int interval    = 100000;
        public static int clickDelay  = 1000; // Milliseconds, please multiply by 10000
        public static bool isDrawing  = false;
        public static bool freeDraw2  = false;

        private static bool StartedHook = false;
        private static int[,]? pixelArray;
        private static int[] path = pathValue.ToString().Select(t => int.Parse(t.ToString())).ToArray();

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

                    pixelArray[x, y] = redByte == 0 ? 1 : 0;
                }
            }
            return;
        }

        private static unsafe void ResetScan(Pos size)
        {
            for (int y = 0; y < size.Y; y++)
            {
                for (int x = 0; x < size.X; x++)
                {
                    pixelArray[x, y] = pixelArray[x, y] == 2 ? 1 : 0;
                }
            }
            return;
        }

        public static void Halt()
        {
            isDrawing = true;
        }

        public static async Task<bool> Draw(SKBitmap bitmap)
        {
            if (isDrawing) return false;

            void keybindHalt(object? sender, KeyboardHookEventArgs e)
            {
                if(e.Data.KeyCode==KeyCode.VcLeftAlt) Halt();
            }
            Input.taskHook.KeyReleased += keybindHalt;

            isDrawing = true;
            path = pathValue.ToString().Select(t => int.Parse(t.ToString())).ToArray();

            Scan(bitmap);

            Pos StartPos = new Pos { X= (int)Input.mousePos.X, Y= (int)Input.mousePos.Y };

            if (pixelArray == null) { System.Diagnostics.Debug.WriteLine("pixelArray was never created."); }

            for (int _y = 0; _y < bitmap.Height; _y++)
            {
                if (!isDrawing) { break; }
                int y = (_y + StartPos.Y);
                for (int _x = 0; _x < bitmap.Width; _x++)
                {
                    if (!isDrawing) { break; }
                    if (pixelArray[_x, _y] == 1)
                    {
                        int x = (_x + StartPos.X);

                        await NOP(clickDelay*10000);

                        Input.MoveTo((short)x, (short)y);
                        Input.SendClickDown(Input.MouseTypes.MouseLeft);
                        bool complete = await DrawArea(_x, _y, StartPos, new Pos { X = bitmap.Width, Y = bitmap.Height });
                        Input.SendClickUp(Input.MouseTypes.MouseLeft);
                    }
                }
            }

            Input.taskHook.KeyReleased -= keybindHalt;

            isDrawing = false;
            ResetScan(new Pos { X = bitmap.Width, Y = bitmap.Height });
            return true;
        }

        private static async Task<bool> DrawArea(int _x, int _y, Pos startPos, Pos size)
        {
            ArrayList stack = new ArrayList();

            bool cont;
            int distanceSinceLastClick = 0;
            while (true)
            {
                if (!isDrawing) { break; }
                short x = (short)(_x + startPos.X);
                short y = (short)(_y + startPos.Y);
                Input.MoveTo((short)x, (short)y);
                if(distanceSinceLastClick > 4096 && freeDraw2)
                {
                    Input.SendClickUp(Input.MouseTypes.MouseLeft);
                    await NOP(interval*3);
                    Input.SendClickDown(Input.MouseTypes.MouseLeft);
                }

                pixelArray[_x,_y] = 2;

                await NOP(interval);

                if (!isDrawing) { break; }

                cont = false;
                foreach (int i in Enumerable.Range(0, 7))
                {
                    switch (path[i])
                    {
                        // Not a fan of this "hard-coding" method but it is easiest.
                        case 1:
                            if (_x <= 0 || _y <= 0) break;
                            if (pixelArray[_x - 1,_y - 1] != 1) break;
                            stack.Add(new Pos { X = _x, Y = _y });
                            _x -= 1;
                            _y -= 1;
                            cont = true;
                            break;
                        case 2:
                            if (_y <= 0) break;
                            if (pixelArray[_x,_y - 1] != 1) break;
                            stack.Add(new Pos { X = _x, Y = _y });
                            _y -= 1;
                            cont = true;
                            break;
                        case 3:
                            if (_y <= 0 || _x >= size.X - 1) break;
                            if (pixelArray[_x + 1,_y - 1] != 1) break;
                            stack.Add(new Pos { X = _x, Y = _y });
                            _x += 1;
                            _y -= 1;
                            cont = true;
                            break;
                        case 4:
                            if (_x <= 0) break;
                            if (pixelArray[_x - 1,_y] != 1) break;
                            stack.Add(new Pos { X = _x, Y = _y });
                            _x -= 1;
                            cont = true;
                            break;
                        case 5:
                            if (_x >= size.X - 1) break;
                            if (pixelArray[_x + 1, _y] != 1) break;
                            stack.Add(new Pos { X = _x, Y = _y });
                            _x += 1;
                            cont = true;
                            break;
                        case 6:
                            if (_y >= size.Y - 1 || _x <= 0) break;
                            if (pixelArray[_x - 1,_y + 1] != 1) break;
                            stack.Add(new Pos { X = _x, Y = _y });
                            _x -= 1;
                            _y += 1;
                            cont = true;
                            break;
                        case 7:
                            if (_y >= size.Y - 1) break;
                            if (pixelArray[_x,_y + 1] != 1) break;
                            stack.Add(new Pos { X = _x, Y = _y });
                            _y += 1;
                            cont = true;
                            break;
                        case 8:
                            if (_y >= size.Y - 1 || _x >= size.X - 1) break;
                            if (pixelArray[_x + 1,_y + 1] != 1) break;
                            stack.Add(new Pos { X = _x, Y = _y });
                            _x += 1;
                            _y += 1;
                            cont = true;
                            break;
                    }
                }
                if (cont) continue;
                if (stack.Count < 1) break;
                Pos backPos = (Pos)stack[^1];
                _x = backPos.X;
                _y = backPos.Y;

                stack.Remove(backPos);
            }
            return true;
        }

        class Pos
        {
            public int X { get; set; }
            public int Y { get; set; }
        }
    }
}