using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodraw.Configuration;
using SkiaSharp;

namespace Autodraw.Painting.Recreate;

public enum StrokeOrder
{
    /// <summary>Rows are scanned top-to-bottom and alternate left-to-right / right-to-left.</summary>
    Serpentine = 0,
    /// <summary>Each next run starts at the endpoint nearest the previous run's end.</summary>
    NearestNeighbor = 1
}

public sealed class StrokePlanOptions
{
    public int AlphaThreshold { get; set; } = 200;
    public int BrushSize { get; set; } = 1;
    public StrokeOrder Order { get; set; } = StrokeOrder.Serpentine;
    public ScreenPoint? Start { get; set; }
}

/// <summary>A single horizontal mouse-down/mouse-up run in screen coordinates.</summary>
public readonly record struct PlannedStroke(SKPoint Start, SKPoint End)
{
    public double Length => Math.Sqrt(
        (End.X - Start.X) * (End.X - Start.X) +
        (End.Y - Start.Y) * (End.Y - Start.Y));

    public PlannedStroke Reverse() => new(End, Start);
}

public sealed record LayerStrokePlan(SKColor Color, IReadOnlyList<PlannedStroke> Strokes);

/// <summary>
/// Converts an opaque colour mask into horizontal screen-space runs.  It deliberately keeps one
/// down/up pair per run: a paint program can lose hundreds of milliseconds for every extra click,
/// while a single dragged run paints the same row.  Runs are sampled every <c>BrushSize</c> rows so
/// a larger brush does not redundantly repaint the same band.
/// </summary>
public static class StrokePlanner
{
    public static unsafe List<PlannedStroke> Plan(
        SKBitmap mask,
        CanvasMapper mapper,
        StrokePlanOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(options);

        var result = new List<PlannedStroke>();
        if (mask.Width <= 0 || mask.Height <= 0 || mapper.Canvas.Width <= 0 || mapper.Canvas.Height <= 0)
            return result;

        var alphaThreshold = Math.Clamp(options.AlphaThreshold, 0, 255);
        // BrushSize is a target-application pixel size, while rows are sampled in image space.
        // Account for a downscaled image so a large brush does not repaint the same screen band.
        var rowStep = Math.Max(1, (int)Math.Floor(options.BrushSize / Math.Max(mapper.Scale, 1e-6)));
        var seen = new HashSet<(int, int, int, int)>();

        byte* pixelBase = null;
        var fastPixels = mask.BytesPerPixel == 4 && mask.GetPixels() != IntPtr.Zero;
        if (fastPixels) pixelBase = (byte*)mask.GetPixels().ToPointer();

        bool IsOpaque(int x, int y)
        {
            int alpha;
            if (fastPixels)
                alpha = pixelBase![y * mask.RowBytes + x * 4 + 3];
            else
                alpha = mask.GetPixel(x, y).Alpha;
            return alpha > 0 && alpha >= alphaThreshold;
        }

        var sampledRow = 0;
        for (var y = 0; y < mask.Height; y += rowStep, sampledRow++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowStartIndex = result.Count;

            var runStart = -1;
            for (var x = 0; x <= mask.Width; x++)
            {
                var opaque = x < mask.Width && IsOpaque(x, y);
                if (opaque && runStart < 0) runStart = x;
                if (opaque || runStart < 0) continue;

                var runEnd = x - 1;
                if (runEnd >= runStart)
                {
                    var (start, end) = mapper.MapRun(y, runStart, runEnd);
                    EnsureLeftToRight(ref start, ref end);
                    if (sampledRow % 2 == 1)
                    {
                        // Reverse both the line and (through the run order below) the row traversal.
                        (start, end) = (end, start);
                    }

                    var key = Key(start, end);
                    if (seen.Add(key))
                    {
                        result.Add(new PlannedStroke(
                            new SKPoint(start.X, start.Y),
                            new SKPoint(end.X, end.Y)));
                    }
                }

                runStart = -1;
            }

            if (sampledRow % 2 == 1 && result.Count - rowStartIndex > 1)
                result.Reverse(rowStartIndex, result.Count - rowStartIndex);
        }

        if (options.Order == StrokeOrder.NearestNeighbor && result.Count > 1)
            result = OrderNearest(result, options.Start ?? new ScreenPoint(mapper.Canvas.Left, mapper.Canvas.Top), cancellationToken);

        return result;
    }

    private static void EnsureLeftToRight(ref ScreenPoint start, ref ScreenPoint end)
    {
        if (start.X <= end.X) return;
        (start, end) = (end, start);
    }

    private static (int, int, int, int) Key(ScreenPoint start, ScreenPoint end)
    {
        return start.X <= end.X
            ? (start.X, start.Y, end.X, end.Y)
            : (end.X, end.Y, start.X, start.Y);
    }

    /// <summary>
    /// Greedy nearest-run ordering accelerated with a coarse spatial grid.  Each run is registered
    /// at both endpoints, so the next stroke can be entered from whichever end is closer.
    /// </summary>
    private static List<PlannedStroke> OrderNearest(
        List<PlannedStroke> strokes,
        ScreenPoint start,
        CancellationToken cancellationToken)
    {
        const int cellSize = 32;
        var buckets = new Dictionary<(int X, int Y), List<int>>();
        var located = new bool[strokes.Count];
        for (var i = 0; i < strokes.Count; i++)
        {
            located[i] = true;
            AddToBucket(buckets, strokes[i].Start, i, cellSize);
            AddToBucket(buckets, strokes[i].End, i, cellSize);
        }

        var result = new List<PlannedStroke>(strokes.Count);
        var current = new SKPoint(start.X, start.Y);

        while (result.Count < strokes.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentCell = Cell(current.X, current.Y, cellSize);
            var bestIndex = -1;
            var bestDistance = double.MaxValue;
            var reverse = false;
            var foundRing = -1;

            for (var ring = 0; ring < 4096; ring++)
            {
                for (var cx = currentCell.X - ring; cx <= currentCell.X + ring; cx++)
                {
                    for (var cy = currentCell.Y - ring; cy <= currentCell.Y + ring; cy++)
                    {
                        // Only the perimeter is new for this ring.
                        if (ring > 0 &&
                            cx != currentCell.X - ring && cx != currentCell.X + ring &&
                            cy != currentCell.Y - ring && cy != currentCell.Y + ring)
                            continue;
                        if (!buckets.TryGetValue((cx, cy), out var indices)) continue;

                        foreach (var index in indices)
                        {
                            if (!located[index]) continue;
                            var candidate = strokes[index];
                            var startDistance = DistanceSquared(current, candidate.Start);
                            var endDistance = DistanceSquared(current, candidate.End);
                            if (startDistance <= endDistance && startDistance < bestDistance)
                            {
                                bestDistance = startDistance;
                                bestIndex = index;
                                reverse = false;
                            }
                            else if (endDistance < bestDistance)
                            {
                                bestDistance = endDistance;
                                bestIndex = index;
                                reverse = true;
                            }
                        }
                    }
                }

                if (bestIndex >= 0)
                {
                    if (foundRing < 0) foundRing = ring;
                    // One extra ring protects against a near edge of the next cell being closer
                    // than a far corner of the ring that produced the candidate.
                    if (ring >= foundRing + 2) break;
                }
            }

            if (bestIndex < 0)
            {
                // Defensive fallback: never drop a run because of an unexpected spatial edge case.
                bestIndex = Array.IndexOf(located, true);
                if (bestIndex < 0) break;
            }

            var chosen = reverse ? strokes[bestIndex].Reverse() : strokes[bestIndex];
            result.Add(chosen);
            located[bestIndex] = false;
            current = chosen.End;
        }

        return result;
    }

    private static void AddToBucket(
        Dictionary<(int X, int Y), List<int>> buckets,
        SKPoint point,
        int index,
        int cellSize)
    {
        var key = Cell(point.X, point.Y, cellSize);
        if (!buckets.TryGetValue(key, out var list))
        {
            list = new List<int>();
            buckets[key] = list;
        }
        if (!list.Contains(index)) list.Add(index);
    }

    private static (int X, int Y) Cell(float x, float y, int cellSize)
        => (FloorDiv((int)Math.Round(x), cellSize), FloorDiv((int)Math.Round(y), cellSize));

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        if (value < 0 && value % divisor != 0) quotient--;
        return quotient;
    }

    private static double DistanceSquared(SKPoint a, SKPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    /// <summary>Renders a diagnostic bitmap of the whole plan.  The caller owns the result.</summary>
    public static SKBitmap RenderPreview(
        IEnumerable<LayerStrokePlan> plans,
        ScreenRect canvas,
        int brushSize)
    {
        var bitmap = new SKBitmap(Math.Max(1, canvas.Width), Math.Max(1, canvas.Height),
            SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var surface = new SKCanvas(bitmap);
        surface.Clear(new SKColor(232, 235, 239));

        using (var border = new SKPaint
               {
                   Style = SKPaintStyle.Stroke,
                   StrokeWidth = 1,
                   IsAntialias = false,
                   Color = new SKColor(90, 98, 108)
               })
        {
            surface.DrawRect(0.5f, 0.5f, canvas.Width - 1, canvas.Height - 1, border);
        }

        foreach (var plan in plans)
        {
            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, brushSize),
                StrokeCap = SKStrokeCap.Round,
                IsAntialias = false,
                Color = plan.Color
            };
            foreach (var stroke in plan.Strokes)
            {
                surface.DrawLine(
                    stroke.Start.X - canvas.Left,
                    stroke.Start.Y - canvas.Top,
                    stroke.End.X - canvas.Left,
                    stroke.End.Y - canvas.Top,
                    paint);
            }
        }

        return bitmap;
    }

    public static SKBitmap RenderPreview(
        IReadOnlyList<PlannedStroke> strokes,
        ScreenRect canvas,
        int brushSize,
        SKColor color)
        => RenderPreview(new[] { new LayerStrokePlan(color, strokes) }, canvas, brushSize);
}
