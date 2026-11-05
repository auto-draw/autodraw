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
using Autodraw.Configuration;

namespace Autodraw;

// This is solely for Inputs from the DrawStack stuff.
public class InputAction
{
    public enum ActionType
    {
        LeftClick,
        RightClick,
        MoveTo,
        SetColorHex,
        WriteString,
        KeyDown,
        KeyUp
    }

    public ActionType Action { get; set; }
    public Vector2? Position { get; set; }
    
    public int Delay { get; set; }
    
    public int? Speed { get; set; }
    
    public string? Data { get; set; }

    public InputAction(ActionType action, object? data = null, int delay = 0, int? speed = null)
    {
        Action = action;
        Delay = Math.Max(0, delay);
        Speed = speed > 0 ? speed : null;
        switch (action)
        {
            case ActionType.LeftClick:
            case ActionType.RightClick:
            case ActionType.MoveTo:
                if (data is Vector2 pos)
                {
                    Position = pos;
                }
                break;

            case ActionType.SetColorHex:
                if (data is Vector2 colorPosition)
                {
                    Position = colorPosition;
                }
                else
                {
                    Data = data as string;
                }
                break;

            case ActionType.WriteString:
            case ActionType.KeyDown:
            case ActionType.KeyUp:
                Data = data as string;
                break;
        }
    }

    public InputAction(InputAction action)
        : this(action.Action, action.Data is not null ? action.Data : action.Position, action.Delay, action.Speed)
    {
    }

    private async Task MoveToPosition()
    {
        if (Position is null) return;

        var targetPosition = Position.Value;
        if (Speed is null)
        {
            Input.MoveTo((short)targetPosition.X, (short)targetPosition.Y);
            return;
        }

        var currentPosition = Input.mousePos;
        var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(currentPosition, targetPosition)));

        for (var i = 1; i <= steps; i++)
        {
            var position = Vector2.Lerp(currentPosition, targetPosition, i / (float)steps);
            Input.MoveTo((short)position.X, (short)position.Y);
            await Drawing.NOP(Speed.Value);
        }
    }

    public async Task PerformAction()
    {
        if (Delay > 0)
        {
            await Drawing.NOP(Delay * 10_000L);
        }

        switch (Action)
        {
            case ActionType.MoveTo:
                await MoveToPosition();
                break;

            case ActionType.SetColorHex:
                // SelectColor dispatches on PickerType (Wheel / Box).  Never click blind
                // coordinates: when the configured picker is not calibrated, stop the draw and
                // tell the user where to configure it.
                if (!ColorPicker.CanSelectCurrently)
                {
                    Drawing.Halt();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        new MessageBox().ShowMessageBox("Color picker not set up",
                            LocalizationManager.T("status_error_no_picker_setup"), "warn"));
                    break;
                }
                await ColorPicker.SelectColor(Data ?? string.Empty);
                break;

            case ActionType.LeftClick:
                await MoveToPosition();
                Input.SendClick(Input.MouseTypes.MouseLeft);
                break;

            case ActionType.RightClick:
                await MoveToPosition();
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
    public static bool Painting_Legacy;

    // Variables

    public static int Interval = 10000;
    public static int ClickDelay = 1000; // Milliseconds, please multiply by 10000
    /// <summary>Immutable configuration captured for the current drawing run.</summary>
    public static AutoDrawConfig? ActiveConfiguration { get; private set; }
    
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

    // HexedDrawer-compatible run settings and progress.
    public static string DfsMode = "8-Directional";
    public static string DrawerModule = "New (A* Bridge)";
    public static int BrushSize = 1;
    public static bool ClipToRegion;
    public static long ProgressDrawn { get; private set; }
    public static long ProgressTotal { get; private set; }
    public static double ProgressPercent => ProgressTotal <= 0 ? 0 : Math.Clamp(ProgressDrawn * 100d / ProgressTotal, 0d, 100d);
    public static event EventHandler? ProgressChanged;

    private static long _lastProgressTicks;
    private static bool _progressMuted;

    private static void ReportProgress(long drawn, long total, bool force = false)
    {
        ProgressDrawn = Math.Max(0, drawn);
        ProgressTotal = Math.Max(0, total);
        var now = Environment.TickCount64;
        if (!force && now - _lastProgressTicks < 60) return;
        _lastProgressTicks = now;
        ProgressChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void ResetProgress()
    {
        ReportProgress(0, 0, true);
    }

    /// <summary>Lets the colour-layer engine drive the shared progress UI without exposing the
    /// private throttled reporter.  Safe to call from a background thread.</summary>
    public static void ReportExternalProgress(long drawn, long total, bool force = false)
        => ReportProgress(drawn, total, force || drawn >= total);

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

    public static Task DelaySeconds(double seconds) => NOP((long)Math.Max(0, seconds) * 10_000_000L);

    private static unsafe byte[,] Scan(SKBitmap bitmap)
    {
        _totalScanSize = 0;
        _completeTotalScan = 0;
        var width = bitmap.Width;
        var height = bitmap.Height;
        var source = new byte[width, height];
        var bitPtr = (byte*)bitmap.GetPixels().ToPointer();

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var redByte = *bitPtr++;
            bitPtr++;
            bitPtr++;
            bitPtr++;
            source[x, y] = redByte < 127 ? (byte)1 : (byte)0;
        }

        if (BrushSize <= 1)
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (source[x, y] == 1) _totalScanSize++;
            return source;
        }

        // A larger brush stamps a square tip.  The expanded mask is what gets traversed, so
        // the generated path covers the requested stroke width instead of only its centreline.
        var radius = Math.Clamp(BrushSize / 2, 1, 32);
        var expanded = new byte[width, height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (source[x, y] != 1) continue;
            for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                expanded[nx, ny] = 1;
            }
        }
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            if (expanded[x, y] == 1) _totalScanSize++;
        return expanded;
    }

    public static void Halt()
    {
        IsDrawing = false;
        IsPaused = false;
        StackHalted = true;
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

    /// <summary>
    /// Builds one continuous pen path for every connected painted component.  Every move is to an
    /// eight-connected neighbour, so the target application never receives a long accidental
    /// stroke between two unrelated pixels.  Returning to a parent is intentional: it lets DFS
    /// cover branches without lifting the mouse.
    /// </summary>
    private static List<List<Vector2>> GenerateActions(byte[,] data)
    {
        var relativeDirections = GetDirections();
        var actions = new List<List<Vector2>>();
        for (var y = 0; y < data.GetLength(1); y++)
        for (var x = 0; x < data.GetLength(0); x++)
        {
            if (data[x, y] != 1) continue;
            actions.Add(ChosenFunction(new Vector2(x, y), data, relativeDirections));
        }
        return actions;
    }

    private static Vector2[] GetDirections()
    {
        var four = DfsMode.Contains("4", StringComparison.OrdinalIgnoreCase);
        return four
            ? new[]
            {
                new Vector2(0, -1), new Vector2(1, 0), new Vector2(0, 1), new Vector2(-1, 0)
            }
            : new[]
            {
                new Vector2(0, -1), new Vector2(1, 0), new Vector2(0, 1), new Vector2(-1, 0),
                new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1)
            };
    }

    private static List<Vector2> ChosenFunction(Vector2 start, byte[,] data, Vector2[] relativeDirections)
    {
        return ChosenAlgorithm switch
        {
            1 => EdgeTraversal(start, data, relativeDirections),
            2 => Concentric(start, data),
            _ => DFS(start, data, relativeDirections)
        };
    }
    
    private static List<Vector2> EdgeTraversal(Vector2 start, byte[,] data, Vector2[] directions)
    {
        // Prefer the outside of a component, but retain the same complete traversal guarantee
        // as DFS.  The former edge follower stopped after one contour and silently dropped
        // interiors, branches, and isolated pixels.
        return TraverseComponent(start, data, directions, preferEdges: true);
    }

    private static List<Vector2> DFS(Vector2 start, byte[,] data, Vector2[] directions)
    {
        return TraverseComponent(start, data, directions, preferEdges: false);
    }

    /// <summary>
    /// Concentric traversal: paints one connected component ring by ring, expanding from its
    /// centroid.  Each ring is swept in one angular direction beginning where the pen arrived,
    /// and any gap between consecutive points is bridged through other pixels of the same
    /// component, so every emitted step stays 8-adjacent and the pen never paints a line
    /// across empty space.  Repainting an already-drawn bridge pixel is intentional: it keeps
    /// the path contiguous, the same way DFS backtracking re-visits parents.
    /// </summary>
    private static List<Vector2> Concentric(Vector2 start, byte[,] data)
    {
        var pixels = CollectComponent(start, data); // also marks every collected pixel visited
        var component = new HashSet<Vector2>(pixels);
        var path = new List<Vector2> { start };
        if (pixels.Count <= 1) return path;

        var cx = pixels.Average(p => p.X);
        var cy = pixels.Average(p => p.Y);

        // Bucket pixels into rings by integer distance from the centroid, then walk outward.
        var rings = pixels
            .Select(p => (Pixel: p, Ring: (int)MathF.Floor(Vector2.Distance(p, new Vector2((float)cx, (float)cy)))))
            .GroupBy(entry => entry.Ring)
            .OrderBy(group => group.Key)
            .Select(group => group.Select(entry => entry.Pixel).ToList());

        var painted = new HashSet<Vector2> { start };
        var current = start;
        var arrivalAngle = Math.Atan2(start.Y - cy, start.X - cx);

        foreach (var ring in rings)
        {
            // Sweep the ring around the circle from the arrival angle so consecutive points
            // are neighbours instead of alternating across the ring.
            var entryAngle = arrivalAngle;
            var ordered = ring
                .Where(p => !painted.Contains(p))
                .OrderBy(p => NormalizeAngle(Math.Atan2(p.Y - cy, p.X - cx) - entryAngle))
                .ToList();

            foreach (var target in ordered)
            {
                if (painted.Contains(target)) continue; // an earlier bridge already painted it
                if (!AreAdjacent(current, target))
                    foreach (var step in BridgePath(current, target, component))
                    {
                        path.Add(step); // repaint keeps the pen path contiguous
                        painted.Add(step);
                        current = step;
                    }
                path.Add(target);
                painted.Add(target);
                current = target;
            }
            if (ordered.Count > 0) arrivalAngle = Math.Atan2(current.Y - cy, current.X - cx);
        }

        // Safety net for pixels skipped because a bridge painted them early, plus any left by
        // floating-point ring bucketing: append them via a component-internal bridge.
        foreach (var leftover in pixels)
        {
            if (painted.Contains(leftover)) continue;
            if (!AreAdjacent(current, leftover))
                foreach (var step in BridgePath(current, leftover, component))
                {
                    path.Add(step); // repaint keeps the pen path contiguous
                    painted.Add(step);
                    current = step;
                }
            path.Add(leftover);
            painted.Add(leftover);
            current = leftover;
        }

        return path;
    }

    /// <summary>Returns every pixel of the 8-connected component containing <paramref name="start"/>,
    /// marking each as visited so the caller's scan does not restart on the same shape.</summary>
    private static List<Vector2> CollectComponent(Vector2 start, byte[,] data)
    {
        var pixels = new List<Vector2> { start };
        var pending = new Stack<Vector2>();
        pending.Push(start);
        data[(int)start.X, (int)start.Y] = 2;
        while (pending.Count > 0)
        {
            var point = pending.Pop();
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var next = point + new Vector2(dx, dy);
                if (!IsValidMove(next, data)) continue;
                data[(int)next.X, (int)next.Y] = 2;
                pixels.Add(next);
                pending.Push(next);
            }
        }
        return pixels;
    }

    /// <summary>Shortest 8-connected route inside the component between two of its pixels,
    /// excluding both endpoints.  Empty when the points are already adjacent.  The "New"
    /// drawer module uses A*; "Old" keeps the legacy breadth-first bridge.</summary>
    private static List<Vector2> BridgePath(Vector2 from, Vector2 to, HashSet<Vector2> component)
    {
        if (from == to || AreAdjacent(from, to)) return new();
        if (DrawerModule.Contains("new", StringComparison.OrdinalIgnoreCase))
            return AStarBridgePath(from, to, component);

        var previous = new Dictionary<Vector2, Vector2> { [from] = from };
        var pending = new Queue<Vector2>();
        pending.Enqueue(from);
        while (pending.Count > 0 && !previous.ContainsKey(to))
        {
            var point = pending.Dequeue();
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var next = point + new Vector2(dx, dy);
                if (!component.Contains(next) || previous.ContainsKey(next)) continue;
                previous[next] = point;
                pending.Enqueue(next);
            }
        }
        if (!previous.ContainsKey(to)) return new(); // unreachable while the component is intact

        var reversed = new List<Vector2>();
        for (var step = to; step != from; step = previous[step]) reversed.Add(step);
        reversed.RemoveAt(0); // the caller appends the target itself
        reversed.Reverse();
        return reversed;
    }

    private static List<Vector2> AStarBridgePath(Vector2 from, Vector2 to, HashSet<Vector2> component)
    {
        var open = new PriorityQueue<Vector2, float>();
        var closed = new HashSet<Vector2>();
        var cameFrom = new Dictionary<Vector2, Vector2>();
        var cost = new Dictionary<Vector2, float> { [from] = 0 };
        open.Enqueue(from, 0);
        while (open.Count > 0)
        {
            var current = open.Dequeue();
            if (current == to) break;
            if (!closed.Add(current)) continue;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var next = current + new Vector2(dx, dy);
                if (!component.Contains(next)) continue;
                var nextCost = cost[current] + (dx != 0 && dy != 0 ? 1.4142135f : 1f);
                if (cost.TryGetValue(next, out var known) && nextCost >= known) continue;
                cost[next] = nextCost;
                cameFrom[next] = current;
                open.Enqueue(next, nextCost + Math.Abs(next.X - to.X) + Math.Abs(next.Y - to.Y));
            }
        }
        if (!cameFrom.ContainsKey(to)) return new();
        var result = new List<Vector2>();
        for (var step = to; step != from; step = cameFrom[step]) result.Add(step);
        result.RemoveAt(0);
        result.Reverse();
        return result;
    }

    private static bool AreAdjacent(Vector2 a, Vector2 b) =>
        Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1;

    private static double NormalizeAngle(double angle)
    {
        var turns = angle % (2 * Math.PI);
        return turns < 0 ? turns + 2 * Math.PI : turns;
    }

    private static List<Vector2> TraverseComponent(Vector2 start, byte[,] data, Vector2[] directions, bool preferEdges)
    {
        var path = new List<Vector2> { start };
        var stack = new Stack<(Vector2 Position, int NextDirection)>();
        stack.Push((start, 0));
        data[(int)start.X, (int)start.Y] = 2;

        while (stack.Count > 0)
        {
            var frame = stack.Pop();
            var orderedDirections = preferEdges
                ? Enumerable.Range(0, directions.Length)
                    .OrderByDescending(i => IsBoundary(frame.Position + directions[i], data))
                    .ThenBy(i => i)
                    .ToArray()
                : Enumerable.Range(0, directions.Length).ToArray();

            var moved = false;
            for (var orderIndex = frame.NextDirection; orderIndex < orderedDirections.Length; orderIndex++)
            {
                var neighbour = frame.Position + directions[orderedDirections[orderIndex]];
                if (!IsValidMove(neighbour, data)) continue;
                stack.Push((frame.Position, orderIndex + 1));
                data[(int)neighbour.X, (int)neighbour.Y] = 2;
                path.Add(neighbour);
                stack.Push((neighbour, 0));
                moved = true;
                break;
            }
            if (!moved && stack.Count > 0)
            {
                // The next frame is this node's parent, therefore the replayed position is
                // adjacent and painting it again is safe.
                path.Add(stack.Peek().Position);
            }
        }
        return path;
    }

    private static bool IsBoundary(Vector2 position, byte[,] data)
    {
        if (!IsWithinBounds(position, data) || data[(int)position.X, (int)position.Y] == 0) return true;
        foreach (var direction in new[] { new Vector2(0, -1), new Vector2(1, 0), new Vector2(0, 1), new Vector2(-1, 0) })
        {
            var neighbour = position + direction;
            if (!IsWithinBounds(neighbour, data) || data[(int)neighbour.X, (int)neighbour.Y] == 0)
                return true;
        }
        return false;
    }

    /* The A* helpers below remain available for legacy callers; path generation uses a complete
       contiguous walk now, which is both cheaper and correct for branching components. */
    private static bool IsAdjacent(Vector2 position1, Vector2 position2, Vector2[] directions)
    {
        foreach (Vector2 direction in directions)
            if (position1 + direction == position2) return true;
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

    private static void ApplyRuntimeConfiguration(AutoDrawConfig config)
    {
        Interval = (int)Math.Clamp(config.Interval, 0, int.MaxValue);
        ClickDelay = (int)Math.Clamp(Math.Round(config.ClickDelay * 1000), 0, int.MaxValue);
        DfsMode = config.DfsMode;
        DrawerModule = config.DrawerModule;
        BrushSize = Math.Clamp(config.BrushSize, 1, 64);
    }

    private static bool StackHalted;
    public static async Task<bool> DrawStack(List<ColorLayerResult> stack, List<InputAction> actions, Vector2 position)
    {
        ActiveConfiguration = DdConfigService.Current;
        ApplyRuntimeConfiguration(ActiveConfiguration);
        var config = ActiveConfiguration;
        stack = config.LayerOrder.Contains("largest", StringComparison.OrdinalIgnoreCase)
            ? stack.OrderByDescending(layer => layer.PixelCount).ToList()
            : config.LayerOrder.Contains("darkness", StringComparison.OrdinalIgnoreCase) ||
              config.LayerOrder.Contains("dark to bright", StringComparison.OrdinalIgnoreCase)
                ? stack.OrderBy(layer => layer.Color.Red * .2126 + layer.Color.Green * .7152 + layer.Color.Blue * .0722).ToList()
                : stack.OrderByDescending(layer => layer.Color.Red * .2126 + layer.Color.Green * .7152 + layer.Color.Blue * .0722).ToList();
        StackHalted = false;
        _progressMuted = true;
        ReportProgress(0, stack.Sum(layer => (long)Math.Max(0, layer.PixelCount)), true);
        static void KeybindRelease(object? sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode == Config.Keybind_StopDrawing) { StackHalted = true; }
        }
        Input.taskHook.KeyReleased += KeybindRelease;

        foreach (ColorLayerResult layer in stack)
        {
            List<InputAction> actionsCopy = new(actions.Select(act => new InputAction(act)));
            if (StackHalted)
            {
                break;
            }
            
            // Pre-Process Actions:
            string hex = $"{layer.Color.Red:X2}{layer.Color.Green:X2}{layer.Color.Blue:X2}";
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
                else if (act.Action == InputAction.ActionType.SetColorHex)
                {
                    // A dedicated color action always sends a conventional CSS-style
                    // hex value, regardless of any text actions in the stack.
                    act.Data = $"#{hex}";
                }
            }
            
            // Use the Actions :D
            foreach (var act in actionsCopy)
            {
                await act.PerformAction();
                await NOP(1000000);
            }
            
            if (StackHalted)
            {
                break;
            }
            
            using SKBitmap processedBitmap = ImageProcessing.ProcessLayerMask(layer.Bitmap, ImageProcessing._currentFilters);
            await NOP(1000000);
            await Draw(processedBitmap, position, false);
            ReportProgress(ProgressDrawn, ProgressTotal, true);
            if (StackHalted) break;
        }

        Input.taskHook.KeyReleased -= KeybindRelease;
        _progressMuted = false;
        ActiveConfiguration = null;
        ReportProgress(StackHalted ? ProgressDrawn : ProgressTotal, ProgressTotal, true);

        if (ShowPopup && !StackHalted)
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                new MessageBox().ShowMessageBox("Drawing Finished!", "The drawing has finished! Yippee!");
            });
        }

        return true;
    }

    public static async Task<bool> Draw(SKBitmap bitmap, Vector2 position, bool showFinishedPopup = true)
    {
        if (IsDrawing) return false;
        ActiveConfiguration ??= DdConfigService.Current;
        var config = ActiveConfiguration;
        ApplyRuntimeConfiguration(config);

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
        if (!_progressMuted) ReportProgress(0, 0, true);
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
        Dispatcher.UIThread.Invoke(() =>
        {
            _dataDisplay.DataDisplayText.Text =
                $"Generating Action Path...";
        });
        List<List<Vector2>> Actions = GenerateActions(dataArray);
        if (!_progressMuted) ReportProgress(0, Actions.Sum(action => (long)action.Count), true);

        int ActionsComplete = 0;
        foreach (List<Vector2> Action in Actions)
        {
            DateTime TimeStarted = DateTime.Now;
            
            ActionsComplete++;
            bool isDown = false;
            int ActionComplete = 0;
            foreach (Vector2 p in Action)
            {
                ActionComplete++;
                if (!IsDrawing) break;
                short x = (short)(p.X * config.PixelSize + startPos.X);
                short y = (short)(p.Y * config.PixelSize + startPos.Y);
                if (ClipToRegion && (x < config.DrawRegion.Left || y < config.DrawRegion.Top ||
                    x > config.DrawRegion.Right || y > config.DrawRegion.Bottom)) continue;
                ReportProgress(ProgressDrawn + 1, ProgressTotal);
                Dispatcher.UIThread.Invoke(() => // Note, this may be slowing down the top-speed, need further testing.
                {
                    _dataDisplay.DataDisplayText.Text =
                        $"ActionSet Completed: {ActionComplete}/{Action.Count}\n" +
                        $"ActionSet's Remaining: {ActionsComplete}/{Actions.Count}\n" +
                        $"Progress: {ProgressPercent:0.0}%  Pixels: {ProgressDrawn}/{ProgressTotal}";
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
                }
                else
                {
                    if (FreeDraw2 && ActionComplete % 10000 == 0)
                    { // Free Draw Mass Draw Protection
                        Utils.Log("Free Draw Click");
                        Input.SendClickUp(Input.MouseTypes.MouseLeft);
                        Input.SendClickDown(Input.MouseTypes.MouseLeft);
                    }
                }
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

            var timeComp = (DateTime.Now - TimeStarted);
            
            Utils.Log($"Time per Action: {timeComp.TotalMilliseconds/Action.Count}");
            Utils.Log($"Action Count: {Action.Count}");
            
            if (FreeDraw2)
            {
                var timeLim = 1000 - timeComp.TotalMilliseconds;
                if (timeLim > 0)
                {
                    Console.WriteLine(timeLim);
                    await NOP((long)timeLim * 10_000);
                }
            }

            await NOP(ClickDelay * 2500);
            if (!IsDrawing) break;
        }

        Input.taskHook.KeyPressed -= KeybindPress;
        Input.taskHook.KeyReleased -= KeybindRelease;

        var completed = IsDrawing;
        IsDrawing = false;
        if (!_progressMuted)
        {
            ActiveConfiguration = null;
            ReportProgress(completed ? ProgressTotal : ProgressDrawn, ProgressTotal, true);
        }
        Dispatcher.UIThread.Invoke(() =>
        {
            _dataDisplay.Close();
            if (showFinishedPopup && ShowPopup) new MessageBox().ShowMessageBox("Drawing Finished!", "The drawing has finished! Yippee!");
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
