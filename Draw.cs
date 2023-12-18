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

    public static bool NoRescan = false;
    
    public static int PathValue = 12345678;

    public static int Interval = 10000;
    public static int ClickDelay = 1000; // Milliseconds, please multiply by 10000

    public static bool IsDrawing;
    public static bool SkipRescan;
    public static bool IsPaused;
    public static bool IsSkipping;
    public static bool FreeDraw2 = false;

    public static Vector2 LastPos = new(0, 0);
    public static bool UseLastPos = false;

    public static bool ShowPopup =
        Config.getEntry("showPopup") == null || bool.Parse(Config.getEntry("showPopup") ?? "true");


    private static DrawDataDisplay? _dataDisplay;
    private static int[,]? _pixelArray;
    private static int[] _path = PathValue.ToString().Select(t => int.Parse(t.ToString())).ToArray();

    private static int _totalScanSize;
    private static int _completeTotalScan;

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
        _totalScanSize = 0;
        _completeTotalScan = 0;
        _pixelArray = new int[bitmap.Width, bitmap.Height];
        var bitPtr = (byte*)bitmap.GetPixels().ToPointer();


        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var redByte = *bitPtr++;
            var greenByte = *bitPtr++;
            var blueByte = *bitPtr++;
            var alphaByte = *bitPtr++;

            _pixelArray[x, y] = redByte < 127 ? 1 : 0;
            if (redByte < 127) _totalScanSize += 1;
        }
    }

    private static void ResetScan(Pos size)
    {
        for (var y = 0; y < size.Y; y++)
        for (var x = 0; x < size.X; x++)
            _pixelArray[x, y] = _pixelArray[x, y] == 2 ? 1 : 0;
    }

    public static void Halt()
    {
        IsDrawing = false;
    }

    public static async Task<bool> Draw(SKBitmap bitmap)
    {
        if (IsDrawing) return false;

        static void KeybindPress(object? sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode == KeyCode.VcBackspace)
            {
                if (NoRescan) return;
                SkipRescan = true;
            }
        }

        static void KeybindRelease(object? sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode == KeyCode.VcLeftAlt) Halt();
            if (e.Data.KeyCode == KeyCode.VcBackspace)
            {
                if (NoRescan) return;
                SkipRescan = false;
            }

            if (e.Data.KeyCode == KeyCode.VcBackslash) IsPaused = !IsPaused;
        }

        Input.taskHook.KeyPressed += KeybindPress;
        Input.taskHook.KeyReleased += KeybindRelease;

        IsDrawing = true;
        var usedPos = UseLastPos ? LastPos : Input.mousePos;

        Dispatcher.UIThread.Invoke(() =>
        {
            _dataDisplay = new DrawDataDisplay();
            _dataDisplay.Show();
            _dataDisplay.Position =
                new PixelPoint((int)(usedPos.X + bitmap.Width / 2), (int)(usedPos.Y + bitmap.Height / 2));
        });

        _path = PathValue.ToString().Select(t => int.Parse(t.ToString())).ToArray();

        Scan(bitmap);

        LastPos = usedPos;
        Pos startPos = new() { X = (int)usedPos.X - bitmap.Width / 2, Y = (int)usedPos.Y - bitmap.Height / 2 };
        Input.MoveTo((short)startPos.X, (short)startPos.Y);
        Input.SendClick(Input.MouseTypes.MouseLeft);

        await NOP(100000);


        if (_pixelArray == null) Debug.WriteLine("pixelArray was never created.");

        for (var _y = 0; _y < bitmap.Height; _y++)
        {
            if (!IsDrawing) break;
            var y = _y + startPos.Y;
            for (var _x = 0; _x < bitmap.Width; _x++)
            {
                if (!IsDrawing) break;
                if (_pixelArray[_x, _y] == 1)
                {
                    var x = _x + startPos.X;
                    if (IsPaused)
                    {
                        Input.SendClickUp(Input.MouseTypes.MouseLeft);
                        while (IsPaused) await NOP(500000);
                        Input.MoveTo((short)x, (short)y);
                        await NOP(500000);
                        Input.SendClickDown(Input.MouseTypes.MouseLeft);
                    }

                    Input.MoveTo((short)x, (short)(y - 1));
                    await NOP(ClickDelay * 5000);
                    Input.MoveTo((short)x, (short)(y + 1));
                    Input.SendClickDown(Input.MouseTypes.MouseLeft);
                    await DrawArea(_x, _y, startPos, new Pos { X = bitmap.Width, Y = bitmap.Height });
                    Input.SendClickUp(Input.MouseTypes.MouseLeft);
                    await NOP(ClickDelay * 5000);
                }
            }
        }

        Input.taskHook.KeyPressed -= KeybindPress;
        Input.taskHook.KeyReleased -= KeybindRelease;

        IsDrawing = false;
        ResetScan(new Pos { X = bitmap.Width, Y = bitmap.Height });
        Dispatcher.UIThread.Invoke(() =>
        {
            _dataDisplay.Close();
            if (ShowPopup) new MessageBox().ShowMessageBox("Drawing Finished!", "The drawing has finished! Yippee!");
        });
        return true;
    }

    private static async Task<bool> DrawArea(int _x, int _y, Pos startPos, Pos size)
    {
        SkipRescan = NoRescan;
        ArrayList stack = new();

        var distanceSinceLastClick = 0;
        while (true)
        {
            if (!IsDrawing)
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    _dataDisplay.DataDisplayText.Text =
                        $"Total Image Done: {_completeTotalScan}/{_totalScanSize}\nSearching...";
                });
                break;
            }

            var x = (short)(_x + startPos.X);
            var y = (short)(_y + startPos.Y);

            if (IsPaused)
            {
                Input.SendClickUp(Input.MouseTypes.MouseLeft);
                while (IsPaused) await NOP(500000);
                Input.MoveTo(x, y);
                await NOP(500000);
                Input.SendClickDown(Input.MouseTypes.MouseLeft);
            }

            var isPixel = _pixelArray[_x, _y] == 1;

            if (!isPixel && !IsSkipping && SkipRescan) IsSkipping = true;

            if ((IsSkipping && isPixel) || (IsSkipping && !SkipRescan))
            {
                IsSkipping = false;
                Input.SendClickUp(Input.MouseTypes.MouseLeft);
                await NOP(ClickDelay * 5000);
                Input.MoveTo(x, y);
                await NOP(ClickDelay * 5000);
                Input.SendClickDown(Input.MouseTypes.MouseLeft);
            }

            // MOVE MOUSE
            if (!IsSkipping)
            {
                Input.MoveTo(x, y);

                distanceSinceLastClick++;
                if (distanceSinceLastClick > 4000 && FreeDraw2)
                {
                    distanceSinceLastClick = 0;
                    Input.SendClickUp(Input.MouseTypes.MouseLeft);
                    await NOP(Interval * 3);
                    Input.SendClickDown(Input.MouseTypes.MouseLeft);
                }
            }

            if (isPixel) _completeTotalScan += 1;
            _pixelArray[_x, _y] = 2;

            Dispatcher.UIThread.Invoke(() =>
            {
                _dataDisplay.DataDisplayText.Text =
                    $"Total Image Done: {_completeTotalScan}/{_totalScanSize}\nCurrent Stack Size: {stack.Count}";
            });

            if (!(IsSkipping && !isPixel)) await NOP(Interval);

            if (!IsDrawing) break;

            var cont = false;
            foreach (var i in Enumerable.Range(0, 8))
            {
                switch (_path[i])
                {
                    case 1:
                        if (_x <= 0 || _y <= 0) break;
                        if (_pixelArray[_x - 1, _y - 1] != 1) break;
                        stack.Add(new Pos { X = _x, Y = _y });
                        _x -= 1;
                        _y -= 1;
                        cont = true;
                        break;
                    case 2:
                        if (_y <= 0) break;
                        if (_pixelArray[_x, _y - 1] != 1) break;
                        stack.Add(new Pos { X = _x, Y = _y });
                        _y -= 1;
                        cont = true;
                        break;
                    case 3:
                        if (_x >= size.X - 1 || _y <= 0) break;
                        if (_pixelArray[_x + 1, _y - 1] != 1) break;
                        stack.Add(new Pos { X = _x, Y = _y });
                        _x += 1;
                        _y -= 1;
                        cont = true;
                        break;
                    case 4:
                        if (_x <= 0) break;
                        if (_pixelArray[_x - 1, _y] != 1) break;
                        stack.Add(new Pos { X = _x, Y = _y });
                        _x -= 1;
                        cont = true;
                        break;
                    case 5:
                        if (_x >= size.X - 1) break;
                        if (_pixelArray[_x + 1, _y] != 1) break;
                        stack.Add(new Pos { X = _x, Y = _y });
                        _x += 1;
                        cont = true;
                        break;
                    case 6:
                        if (_x <= 0 || _y >= size.Y - 1) break;
                        if (_pixelArray[_x - 1, _y + 1] != 1) break;
                        stack.Add(new Pos { X = _x, Y = _y });
                        _x -= 1;
                        _y += 1;
                        cont = true;
                        break;
                    case 7:
                        if (_y >= size.Y - 1) break;
                        if (_pixelArray[_x, _y + 1] != 1) break;
                        stack.Add(new Pos { X = _x, Y = _y });
                        _y += 1;
                        cont = true;
                        break;
                    case 8:
                        if (_y >= size.Y - 1 || _x >= size.X - 1) break;
                        if (_pixelArray[_x + 1, _y + 1] != 1) break;
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