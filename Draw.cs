using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using SharpHook;
using SharpHook.Native;
using SkiaSharp;

namespace Autodraw;
// Most of this code is going to implement the same methods used for previous versions.
// The reason I wish to do this, is because simply put, I cannot viably see another way of doing this that is more or less efficient.
// The previous code was messy, but it's implementation for drawing was more or less, efficient and simple
// I'm not here to over-engineer something if there's already a very stable method, I don't want to make anything unstable.

// Maybe in the near future I'll program it to use an algorithm to try and go over each pixel without going back over
// Because the current one loops back thru the stack if it hasn't touched everything, which sucks.

public static class Drawing
{
    
    // Variables

    public static int pathValue = 12345678;

    public static int interval = 10000;
    public static int clickDelay = 1000; // Milliseconds, please multiply by 10000

    public static bool isDrawing;
    public static bool skipRescan;
    public static bool isPaused;
    public static bool isSkipping;
    public static bool freeDraw2 = false;

    public static Vector2 lastPos = new(0, 0);
    public static bool useLastPos = false;

    public static bool ShowPopup =
        Config.getEntry("showPopup") == null || bool.Parse(Config.getEntry("showPopup") ?? "true");


    private static DrawDataDisplay? dataDisplay;
    private static int[,]? pixelArray;
    private static int[] path = pathValue.ToString().Select(t => int.Parse(t.ToString())).ToArray();

    private static int totalScanSize;
    private static int completeTotalScan;

    // Functions

    public static async Task NOP(long durationTicks)
    {
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedTicks < durationTicks)
            if (durationTicks - sw.ElapsedTicks > 150000)
                await Task.Delay(1);
    }

    private static unsafe void Scan(SKBitmap bitmap)
    {
        totalScanSize = 0;
        completeTotalScan = 0;
        pixelArray = new int[bitmap.Width, bitmap.Height];
        var bitPtr = (byte*)bitmap.GetPixels().ToPointer();


        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var redByte = *bitPtr++;
            var greenByte = *bitPtr++;
            var blueByte = *bitPtr++;
            var alphaByte = *bitPtr++;

            pixelArray[x, y] = redByte < 127 ? 1 : 0;
            if (redByte < 127) totalScanSize += 1;
        }
    }

    private static void ResetScan(Pos size)
    {
        for (var y = 0; y < size.Y; y++)
        for (var x = 0; x < size.X; x++)
            pixelArray[x, y] = pixelArray[x, y] == 2 ? 1 : 0;
    }

    public static void Halt()
    {
        isDrawing = false;
    }

    public static async Task<bool> Draw(SKBitmap bitmap)
    {
        if (isDrawing) return false;

        static void keybindPress(object? sender, KeyboardHookEventArgs e)
        {
            throw new NotImplementedException();
            if (e.Data.KeyCode == KeyCode.VcBackspace) skipRescan = true;
        }

        static void keybindRelease(object? sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode == KeyCode.VcLeftAlt) Halt();
            if (e.Data.KeyCode == KeyCode.VcBackspace) skipRescan = false;
            if (e.Data.KeyCode == KeyCode.VcBackslash) isPaused = !isPaused;
        }

        Input.taskHook.KeyPressed += keybindPress;
        Input.taskHook.KeyReleased += keybindRelease;

        isDrawing = true;
        var usedPos = useLastPos ? lastPos : Input.mousePos;

        Dispatcher.UIThread.Invoke(() =>
        {
            dataDisplay = new DrawDataDisplay();
            dataDisplay.Show();
            dataDisplay.Position =
                new PixelPoint((int)(usedPos.X + bitmap.Width / 2), (int)(usedPos.Y + bitmap.Height / 2));
        });

        path = pathValue.ToString().Select(t => int.Parse(t.ToString())).ToArray();

        Scan(bitmap);

        lastPos = usedPos;
        Pos StartPos = new() { X = (int)usedPos.X - bitmap.Width / 2, Y = (int)usedPos.Y - bitmap.Height / 2 };
        Input.MoveTo((short)StartPos.X, (short)StartPos.Y);
        Input.SendClick(Input.MouseTypes.MouseLeft);

        await NOP(100000);


        if (pixelArray == null) Debug.WriteLine("pixelArray was never created.");

        for (var _y = 0; _y < bitmap.Height; _y++)
        {
            if (!isDrawing) break;
            var y = _y + StartPos.Y;
            for (var _x = 0; _x < bitmap.Width; _x++)
            {
                if (!isDrawing) break;
                if (pixelArray[_x, _y] == 1)
                {
                    var x = _x + StartPos.X;
                    if (isPaused)
                    {
                        Input.SendClickUp(Input.MouseTypes.MouseLeft);
                        while (isPaused) await NOP(500000);
                        Input.MoveTo((short)x, (short)y);
                        await NOP(500000);
                        Input.SendClickDown(Input.MouseTypes.MouseLeft);
                    }

                    Input.MoveTo((short)x, (short)(y - 1));
                    await NOP(clickDelay * 5000);
                    Input.MoveTo((short)x, (short)(y + 1));
                    Input.SendClickDown(Input.MouseTypes.MouseLeft);
                    var complete = await DrawArea(_x, _y, StartPos, new Pos { X = bitmap.Width, Y = bitmap.Height });
                    Input.SendClickUp(Input.MouseTypes.MouseLeft);
                    await NOP(clickDelay * 5000);
                }
            }
        }

        Input.taskHook.KeyPressed -= keybindPress;
        Input.taskHook.KeyReleased -= keybindRelease;

        isDrawing = false;
        ResetScan(new Pos { X = bitmap.Width, Y = bitmap.Height });
        Dispatcher.UIThread.Invoke(() =>
        {
            dataDisplay.Close();
            if (ShowPopup) new MessageBox().ShowMessageBox("Drawing Finished!", "The drawing has finished! Yippee!");
            ;
        });
        return true;
    }

    private static async Task<bool> DrawArea(int _x, int _y, Pos startPos, Pos size)
    {
        ArrayList stack = new();

        bool cont;
        var distanceSinceLastClick = 0;
        while (true)
        {
            if (!isDrawing)
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    dataDisplay.DataDisplayText.Text =
                        $"Total Image Done: {completeTotalScan}/{totalScanSize}\nSearching...";
                });
                break;
            }

            var x = (short)(_x + startPos.X);
            var y = (short)(_y + startPos.Y);

            if (isPaused)
            {
                Input.SendClickUp(Input.MouseTypes.MouseLeft);
                while (isPaused) await NOP(500000);
                Input.MoveTo(x, y);
                await NOP(500000);
                Input.SendClickDown(Input.MouseTypes.MouseLeft);
            }

            var isPixel = pixelArray[_x, _y] == 1;

            if (!isPixel && !isSkipping && skipRescan) isSkipping = true;

            if ((isSkipping && isPixel) || (isSkipping && !skipRescan))
            {
                isSkipping = false;
                Input.SendClickUp(Input.MouseTypes.MouseLeft);
                await NOP(interval * 3);
                Input.MoveTo(x, y);
                await NOP(interval * 3);
                Input.SendClickDown(Input.MouseTypes.MouseLeft);
            }

            // MOVE MOUSE
            if (!isSkipping)
            {
                Input.MoveTo(x, y);

                distanceSinceLastClick++;
                if (distanceSinceLastClick > 4000 && freeDraw2)
                {
                    distanceSinceLastClick = 0;
                    Input.SendClickUp(Input.MouseTypes.MouseLeft);
                    await NOP(interval * 3);
                    Input.SendClickDown(Input.MouseTypes.MouseLeft);
                }
            }

            if (isPixel) completeTotalScan += 1;
            pixelArray[_x, _y] = 2;

            Dispatcher.UIThread.Invoke(() =>
            {
                dataDisplay.DataDisplayText.Text =
                    $"Total Image Done: {completeTotalScan}/{totalScanSize}\nCurrent Stack Size: {stack.Count}";
            });

            if (!(isSkipping && !isPixel)) await NOP(interval);

            if (!isDrawing) break;

            cont = false;
            foreach (var i in Enumerable.Range(0, 8))
            {
                switch (path[i])
                {
                    // Not a fan of this "hard-coding" method but it is easiest.
                    case 1:
                        if (_x <= 0 || _y <= 0) break;
                        if (pixelArray[_x - 1, _y - 1] != 1) break;
                        stack.Add(new Pos { X = _x, Y = _y });
                        _x -= 1;
                        _y -= 1;
                        cont = true;
                        break;
                    case 2:
                        if (_y <= 0) break;
                        if (pixelArray[_x, _y - 1] != 1) break;
                        stack.Add(new Pos { X = _x, Y = _y });
                        _y -= 1;
                        cont = true;
                        break;
                    case 3:
                        if (_x >= size.X - 1 || _y <= 0) break;
                        if (pixelArray[_x + 1, _y - 1] != 1) break;
                        stack.Add(new Pos { X = _x, Y = _y });
                        _x += 1;
                        _y -= 1;
                        cont = true;
                        break;
                    case 4:
                        if (_x <= 0) break;
                        if (pixelArray[_x - 1, _y] != 1) break;
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
                        if (_x <= 0 || _y >= size.Y - 1) break;
                        if (pixelArray[_x - 1, _y + 1] != 1) break;
                        stack.Add(new Pos { X = _x, Y = _y });
                        _x -= 1;
                        _y += 1;
                        cont = true;
                        break;
                    case 7:
                        if (_y >= size.Y - 1) break;
                        if (pixelArray[_x, _y + 1] != 1) break;
                        stack.Add(new Pos { X = _x, Y = _y });
                        _y += 1;
                        cont = true;
                        break;
                    case 8:
                        if (_y >= size.Y - 1 || _x >= size.X - 1) break;
                        if (pixelArray[_x + 1, _y + 1] != 1) break;
                        stack.Add(new Pos { X = _x, Y = _y });
                        _x += 1;
                        _y += 1;
                        cont = true;
                        break;
                }

                if (cont) break;
            }

            if (cont) continue;
            if (stack.Count < 1) break;
            var backPos = (Pos)stack[^1];
            _x = backPos.X;
            _y = backPos.Y;

            stack.Remove(backPos);
        }

        return true;
    }

    private class Pos
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
}