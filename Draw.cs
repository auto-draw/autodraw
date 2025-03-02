using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Converters;
using Avalonia.Media;
using Avalonia.Threading;
using SharpHook;
using SharpHook.Native;
using SkiaSharp;

namespace Autodraw;


// This is solely for Inputs from the DrawStack stuff.
public class InputAction
{
    public enum ActionType
    {
        LeftClick,
        RightClick,
        MoveTo,
        WriteString,
        KeyDown,
        KeyUp
    }

    public ActionType Action { get; set; }
    public Vector2? Position { get; set; }
    public string? Data { get; set; }

    public InputAction(ActionType action, object? data = null)
    {
        Action = action;
        switch (action)
        {
            case ActionType.MoveTo:
                if (data is Vector2 pos)
                {
                    Position = pos;
                }
                break;

            case ActionType.WriteString:
            case ActionType.KeyDown:
            case ActionType.KeyUp:
                Data = data as string;
                break;
        }
    }

    public void PerformAction()
    {
        switch (Action)
        {
            case ActionType.MoveTo:
                Input.MoveTo((short)Position.Value.X, (short)Position.Value.Y);
                break;

            case ActionType.LeftClick:
                Input.SendClick(Input.MouseTypes.MouseLeft);
                break;

            case ActionType.RightClick:
                Input.SendClick(Input.MouseTypes.MouseRight);
                break;

            case ActionType.WriteString:
                Input.SendText(Data);
                break;

            case ActionType.KeyDown:
                if (Enum.TryParse(typeof(KeyCode), Data, true, out var kc1))
                {
                    Input.SendKeyDown((KeyCode)kc1);
                }
                break;

            case ActionType.KeyUp:
                if (Enum.TryParse(typeof(KeyCode), Data, true, out var kc2))
                {
                    Input.SendKeyUp((KeyCode)kc2);
                }
                break;
        }
    }
}


public static class Drawing
{
    
    // Variables

    public static int Interval = 10000;
    public static int ClickDelay = 1000; // Milliseconds, please multiply by 10000
    
    /// <summary>
    ///  0 indicates DFS, 1 indicates Edge-Following
    /// </summary>
    public static byte ChosenAlgorithm = 0;
    
    public static bool NoRescan = false;
    public static bool IsDrawing;
    public static bool SkipRescan;
    public static bool IsPaused;
    public static bool IsSkipping;
    public static bool FreeDraw2 = false;

    public static Vector2 LastPos = Config.Preview_LastLockPos;

    public static bool ShowPopup =
        Config.GetEntry("showPopup") == null || bool.Parse(Config.GetEntry("showPopup") ?? "true");


    private static DrawDataDisplay? _dataDisplay;

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

    private static unsafe byte[,] Scan(SKBitmap bitmap)
    {
        _totalScanSize = 0;
        _completeTotalScan = 0;
        var _pixelArray = new byte[bitmap.Width, bitmap.Height];
        var bitPtr = (byte*)bitmap.GetPixels().ToPointer();


        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var redByte = *bitPtr++;
            bitPtr++;
            bitPtr++;
            bitPtr++;

            _pixelArray[x, y] = redByte < 127 ? (byte)1 : (byte)0;
            if (redByte < 127) _totalScanSize += 1;
        }

        return _pixelArray;
    }

    public static void Halt()
    {
        IsDrawing = false;
    }

    private static List<Dictionary<Vector2, int>> GetChunks(SKBitmap srcBitmap)
    {
        List<List<byte>> data = new List<List<byte>>();
        for (int x = 0; x < srcBitmap.Width; x++)
        {
            List<byte> column = new List<byte>();
            for (int y = 0; y < srcBitmap.Height; y++)
            {
                var Color = srcBitmap.GetPixel(x, y);
                Color.ToHsv(out _, out _, out float v);
                bool B = v < 50;
                column.Add(B ? (byte)1 : (byte)0);
            }
            data.Add(column);
        }
        
        List<Dictionary<Vector2, int>> chunks = new(); // ah yes, list dictionary tuple-array vector2.
        void Search(int x, int y)
        {
            var stack = new Stack<(int, int)>(); 
            stack.Push((x, y));
            data[x][y] = 2; // Mark as visited

            var chunk = new Dictionary<Vector2, int>();

            // This is practically the same as the AutoDraw code lol.
            while (stack.Count > 0)
            {
                (x, y) = stack.Pop(); 

                // Explore neighbors (sides and corners)

                // Left
                if (x > 0 && data[x - 1][y] == 1) 
                {
                    data[x - 1][y] = 2;
                    stack.Push((x - 1, y));
                    chunk[new Vector2(x - 1, y)] = 1;
                }
                else if(x > 0 && data[x - 1][y] == 0) chunk[new Vector2(x , y)] = 2;

                // Right
                if (x < srcBitmap.Width - 1 && data[x + 1][y] == 1) 
                {
                    data[x + 1][y] = 2;
                    stack.Push((x + 1, y));
                    chunk[new Vector2(x + 1, y)] = 1;
                }
                else if(x < srcBitmap.Width - 1 && data[x + 1][y] == 0) chunk[new Vector2(x , y)] = 2;

                // Up
                if (y > 0 && data[x][y - 1] == 1) 
                {
                    data[x][y - 1] = 2;
                    stack.Push((x, y - 1));
                    chunk[new Vector2(x, y - 1)] = 1;
                }
                else if(y > 0 && data[x][y - 1] == 0) chunk[new Vector2(x , y)] = 2;

                // Down
                if (y < srcBitmap.Height - 1 && data[x][y + 1] == 1) 
                {
                    data[x][y + 1] = 2;
                    stack.Push((x, y + 1));
                    chunk[new Vector2(x, y + 1)] = 1;
                }
                else if(y < srcBitmap.Height - 1 && data[x][y + 1] == 0) chunk[new Vector2(x , y)] = 2;

                // Top-Left
                if (x > 0 && y > 0 && data[x - 1][y - 1] == 1)
                {
                    data[x - 1][y - 1] = 2;
                    stack.Push((x - 1, y - 1));
                    chunk[new Vector2(x - 1, y - 1)] = 1;
                }
                else if (x > 0 && y > 0 && data[x - 1][y - 1] == 0) chunk[new Vector2(x, y)] = 2;

                // Top-Right
                if (x < srcBitmap.Width - 1 && y > 0 && data[x + 1][y - 1] == 1)
                {
                    data[x + 1][y - 1] = 2;
                    stack.Push((x + 1, y - 1));
                    chunk[new Vector2(x + 1, y - 1)] = 1;
                }
                else if (x < srcBitmap.Width - 1 && y > 0 && data[x + 1][y - 1] == 0) chunk[new Vector2(x, y)] = 2;

                // Bottom-Left
                if (x > 0 && y < srcBitmap.Height - 1 && data[x - 1][y + 1] == 1)
                {
                    data[x - 1][y + 1] = 2;
                    stack.Push((x - 1, y + 1));
                    chunk[new Vector2(x - 1, y + 1)] = 1;
                }
                else if (x > 0 && y < srcBitmap.Height - 1 && data[x - 1][y + 1] == 0) chunk[new Vector2(x, y)] = 2;

                // Bottom-Right
                if (x < srcBitmap.Width - 1 && y < srcBitmap.Height - 1 && data[x + 1][y + 1] == 1)
                {
                    data[x + 1][y + 1] = 2;
                    stack.Push((x + 1, y + 1));
                    chunk[new Vector2(x + 1, y + 1)] = 1;
                }
                else if (x < srcBitmap.Width - 1 && y < srcBitmap.Height - 1 && data[x + 1][y + 1] == 0) chunk[new Vector2(x, y)] = 2;
            }
            chunks.Add(chunk);
        }
        for (int y = 0; y < srcBitmap.Height; y++)
            for (int x = 0; x < srcBitmap.Width; x++)
            {
                if (data[x][y] == 1)
                {
                    Search(x,y);
                }
            }
        
        chunks.Sort(delegate(Dictionary<Vector2, int> x, Dictionary<Vector2, int> y)
        {
            return y.Count.CompareTo(x.Count);
        });
        
        return chunks;
    }

    private static List<List<Vector2>> GenerateActions(List<Dictionary<Vector2, int>> chunks, byte[,] data)
    {
        Vector2[] relativeDirections =
        {
            new(0, -1),    // Up
            new(1, 0),     // Right
            new(0, 1),     // Down
            new(-1, 0),    // Left
            new(-1, -1),   // Top-Left (Diagonal)
            new(1, -1),    // Top-Right (Diagonal)
            new(1, 1),     // Bottom-Right (Diagonal)
            new(-1, 1)     // Bottom-Left (Diagonal)
        };

        List<List<Vector2>> actions = new();

        // Traverse each chunk
        foreach (Dictionary<Vector2, int> chunk in chunks)
        {
            foreach (KeyValuePair<Vector2, int> startPoint in chunk)
            {
                if (data[(int)startPoint.Key.X, (int)startPoint.Key.Y] != 1) continue;

                // Perform DFS to find connected components
                actions.Add(ChosenFunction(startPoint.Key, data, relativeDirections,chunk));
            }
        }

        return actions;
    }

    private static List<Vector2> ChosenFunction(Vector2 start, byte[,] data, Vector2[] relativeDirections, Dictionary<Vector2, int> chunk)
    {
        if (ChosenAlgorithm == 0)
        {
            return DFS(start, data, relativeDirections);
        }
        if (ChosenAlgorithm == 1)
        {
            return EdgeTraversal(start, data, relativeDirections);
        }

        return DFS(start, data, relativeDirections); // This really shouldn't happen.
    }
    
    private static List<Vector2> EdgeTraversal(Vector2 start, byte[,] data, Vector2[] directions)
    {
        List<Vector2> path = new();
        Vector2 currentPosition = start;
        int currentDirection = 1;

        while (true)
        {
            bool moved = false;

            foreach (int directionIndex in GetDirectionOrder(currentDirection))
            {
                Vector2 newPosition = currentPosition + directions[directionIndex];
                if (IsValidMove(newPosition, data))
                {
                    path.Add(newPosition);
                    currentPosition = newPosition;
                    currentDirection = directionIndex;
                    data[(int)newPosition.X, (int)newPosition.Y] = 2; // Mark as traveled
                    moved = true;
                    break;
                }
            }

            if (!moved)
                break;
        }
        return path;
    }

    private static List<Vector2> DFS(Vector2 start, byte[,] data, Vector2[] directions)
    {
        Stack<Vector2> stack = new();
        List<Vector2> path = new();

        Vector2? previousPosition = null;

        stack.Push(start);
        data[(int)start.X, (int)start.Y] = 2;

        while (stack.Count > 0)
        {
            Vector2 currentPosition = stack.Pop();

            if (previousPosition.HasValue && !IsAdjacent(previousPosition.Value, currentPosition, directions))
            {
                List<Vector2> aStarPath = AStar(previousPosition.Value, currentPosition, data);
                path.AddRange(aStarPath);

                foreach (var position in aStarPath)
                {
                    data[(int)position.X, (int)position.Y] = 2;
                }
            }

            path.Add(currentPosition);
            previousPosition = currentPosition;

            foreach (Vector2 direction in directions)
            {
                Vector2 neighbor = currentPosition + direction;
                if (IsValidMove(neighbor, data))
                {
                    data[(int)neighbor.X, (int)neighbor.Y] = 2;
                    stack.Push(neighbor);
                }
            }
        }

        return path;
    }

    private static bool IsAdjacent(Vector2 position1, Vector2 position2, Vector2[] directions)
    {
        foreach (Vector2 direction in directions)
        {
            if (position1 + direction == position2)
            {
                return true;
            }
        }
        return false;
    }

    private static List<Vector2> AStar(Vector2 start, Vector2 goal, byte[,] data)
    {
        PriorityQueue<Vector2, float> openSet = new();
        HashSet<Vector2> closedSet = new();
        Dictionary<Vector2, Vector2?> cameFrom = new();
        Dictionary<Vector2, float> gScore = new();
        Dictionary<Vector2, float> fScore = new();

        openSet.Enqueue(start, 0);
        gScore[start] = 0;
        fScore[start] = Heuristic(start, goal);

        while (openSet.Count > 0)
        {
            Vector2 current = openSet.Dequeue();

            if (current == goal)
            {
                return ReconstructPath(cameFrom, current);
            }

            closedSet.Add(current);

            for (int i = 0; i < 8; i++)
            {
                Vector2 neighbor = current + GetRelativeDirection(i);
                if (!IsWithinBounds(neighbor, data) || data[(int)neighbor.X, (int)neighbor.Y] == 0 || closedSet.Contains(neighbor))
                {
                    continue;
                }

                float tentativeGScore = gScore[current] + 1;

                if (!gScore.ContainsKey(neighbor) || tentativeGScore < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeGScore;
                    fScore[neighbor] = gScore[neighbor] + Heuristic(neighbor, goal);

                    if (!openSet.UnorderedItems.Any(item => item.Element == neighbor))
                    {
                        openSet.Enqueue(neighbor, fScore[neighbor]);
                    }
                }
            }
        }

        return new();
    }

    private static float Heuristic(Vector2 a, Vector2 b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    private static List<Vector2> ReconstructPath(Dictionary<Vector2, Vector2?> cameFrom, Vector2 current)
    {
        List<Vector2> path = new();
        while (cameFrom.ContainsKey(current) && cameFrom[current].HasValue)
        {
            path.Add(current);
            current = cameFrom[current].Value;
        }

        path.Reverse();
        return path;
    }
    
    private static IEnumerable<int> GetDirectionOrder(int currentDirection)
    {
        return new[]
        {
            (currentDirection + 3) % 4,  // Left
            currentDirection,            // Forward
            (currentDirection + 1) % 4,  // Right
            (currentDirection + 2) % 4,  // Backward
            4, 5, 6, 7                   // Diagonals
        };
    }

    private static Vector2 GetRelativeDirection(int directionIndex)
    {
        return directionIndex switch
        {
            0 => new Vector2(0, -1),  // Up
            1 => new Vector2(1, 0),   // Right
            2 => new Vector2(0, 1),   // Down
            3 => new Vector2(-1, 0),  // Left
            4 => new Vector2(-1, -1), // Top-Left
            5 => new Vector2(1, -1),  // Top-Right
            6 => new Vector2(1, 1),   // Bottom-Right
            7 => new Vector2(-1, 1),  // Bottom-Left
            _ => throw new ArgumentOutOfRangeException(nameof(directionIndex), "Invalid direction index.")
        };
    }

    private static bool IsWithinBounds(Vector2 position, byte[,] data)
    {
        return position.X >= 0 && position.Y >= 0 &&
               position.X < data.GetLength(0) && position.Y < data.GetLength(1);
    }
    
    

    private static bool IsValidMove(Vector2 position, byte[,] data)
    {
        return position.X >= 0 && position.Y >= 0 &&
               position.X < data.GetLength(0) && position.Y < data.GetLength(1) &&
               data[(int)position.X, (int)position.Y] == 1;
    }

    private static bool StackHalted;
    public static async Task<bool> DrawStack(List<SKBitmap> stack, List<InputAction> actions, Vector2 position)
    {
        StackHalted = false;
        static void KeybindRelease(object? sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode == Config.Keybind_StopDrawing) { StackHalted = true; }
        }
        Input.taskHook.KeyReleased += KeybindRelease;

        foreach (SKBitmap bitmap in stack)
        {
            List<InputAction> actionsCopy = new(actions.Select(act => new InputAction(act.Action, act.Data is not null ? act.Data : act.Position)));
            if (StackHalted)
            {
                break;
            }
            
            // Pre-Process Actions:
            Color color = ImageProcessing.GetColor(bitmap);
            string hex = ColorToHexConverter.ToHexString(color, AlphaComponentPosition.Trailing); // Why yes I AM feeling lazy today! Thanks avalonia for this lol
            hex = hex.Substring(0, 6);
            Console.WriteLine(hex);
            
            foreach (var act in actionsCopy)
            {
                if (act.Action == InputAction.ActionType.WriteString && !string.IsNullOrEmpty(act.Data))
                {
                    // Replace all occurrences of "{colorHex}" in the Data property
                    act.Data = act.Data.Replace("{colorHex}", hex);
                    Console.WriteLine(act.Data);
                    Console.WriteLine(hex);
                }
            }
            
            // Use the Actions :D
            foreach (var act in actionsCopy)
            {
                act.PerformAction();
                await NOP(1000000);
            }
            
            if (StackHalted)
            {
                break;
            }
            
            SKBitmap processedBitmap = ImageProcessing.Process(bitmap, ImageProcessing._currentFilters);
            await NOP(1000000);
            await Draw(processedBitmap,position);
        }

        return true;
    }

    public static async Task<bool> Draw(SKBitmap bitmap,Vector2 position)
    {
        if (IsDrawing) return false;

        static void KeybindPress(object? sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode == Config.Keybind_SkipRescan)
            {
                if (NoRescan) return;
                SkipRescan = true;
            }
        }

        static void KeybindRelease(object? sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode == Config.Keybind_StopDrawing) Halt();
            if (e.Data.KeyCode == Config.Keybind_SkipRescan)
            {
                if (NoRescan) return;
                SkipRescan = false;
            }

            if (e.Data.KeyCode == Config.Keybind_PauseDrawing) IsPaused = !IsPaused;
        }

        Input.taskHook.KeyPressed += KeybindPress;
        Input.taskHook.KeyReleased += KeybindRelease;

        IsDrawing = true;
        var usedPos = position;

        Dispatcher.UIThread.Invoke(() =>
        {
            _dataDisplay = new DrawDataDisplay();
            _dataDisplay.Show();
            _dataDisplay.Position =
                new PixelPoint((int)(usedPos.X + bitmap.Width), (int)(usedPos.Y + bitmap.Height));
        });

        LastPos = usedPos;
        Pos startPos = new() { X = (int)usedPos.X, Y = (int)usedPos.Y };
        Input.MoveTo((short)startPos.X, (short)startPos.Y);
        await NOP(50000);
        Input.SendClick(Input.MouseTypes.MouseLeft);
        await NOP(50000);

        byte[,] dataArray = Scan(bitmap);

        Dispatcher.UIThread.Invoke(() =>
        {
            _dataDisplay.DataDisplayText.Text =
                $"Getting Chunks...";
        });
        List<Dictionary<Vector2, int>> Chunks = GetChunks(bitmap);
        Dispatcher.UIThread.Invoke(() =>
        {
            _dataDisplay.DataDisplayText.Text =
                $"Generating Action Path...";
        });
        List<List<Vector2>> Actions = GenerateActions(Chunks,dataArray);

        int ActionsComplete = 0;
        foreach (List<Vector2> Action in Actions)
        {
            ActionsComplete++;
            bool isDown = false;
            int ActionComplete = 0;
            foreach (Vector2 p in Action)
            {
                ActionComplete++;
                if (!IsDrawing) break;
                short x = (short)(p.X + startPos.X);
                short y = (short)(p.Y + startPos.Y);
                Dispatcher.UIThread.Invoke(() =>
                {
                    _dataDisplay.DataDisplayText.Text =
                        $"ActionSet Completed: {ActionComplete}/{Action.Count}\n" +
                        $"ActionSet's Remaining: {ActionsComplete}/{Actions.Count}";
                });
                if (!isDown)
                {
                    isDown = true;
                    Vector2 currentPosition = Input.mousePos;
                    Vector2 targetPosition = new Vector2(x, y);
                    int steps = 100;
                    float stepDelay = ClickDelay * 2500f / steps;

                    for (int i = 1; i <= steps; i++)
                    {
                        var interpP = Vector2.Lerp(currentPosition, targetPosition, i / (float)steps);
                        short interpX = (short)interpP.X;
                        short interpY = (short)interpP.Y;

                        Input.MoveTo(interpX, interpY);
                        await NOP((long)stepDelay);
                    }

                    for (int i = 0; i < 10; i++)
                    {
                        Input.MoveTo((short)(x-1), y);
                        await NOP(ClickDelay * 500);
                        Input.MoveTo(x, y);
                    }
                    Input.SendClickDown(Input.MouseTypes.MouseLeft);
                } // Just initializes the Mouse Down
                if (IsPaused)
                {
                    Input.SendClickUp(Input.MouseTypes.MouseLeft);
                    while (IsPaused) await NOP(500000);
                    Input.MoveTo(x, y);
                    await NOP(500000);
                    Input.SendClickDown(Input.MouseTypes.MouseLeft);
                }
                
                Input.MoveTo(x, y);
                await NOP(Interval);
            }
            Input.SendClickUp(Input.MouseTypes.MouseLeft);
            await NOP(ClickDelay * 2500);
            if (!IsDrawing) break;
        }

        Input.taskHook.KeyPressed -= KeybindPress;
        Input.taskHook.KeyReleased -= KeybindRelease;

        IsDrawing = false;
        Dispatcher.UIThread.Invoke(() =>
        {
            _dataDisplay.Close();
            if (ShowPopup) new MessageBox().ShowMessageBox("Drawing Finished!", "The drawing has finished! Yippee!");
        });
        
        return true;
        /*
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
        */
    }

    private static async Task<bool> DrawArea(int _x, int _y, Pos startPos, Pos size)
    {
        return false;
        /*
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
        */
    }

    private class Pos
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
}