using System;
using System.Collections.Generic;
using System.Linq;
using Autodraw.Configuration;

namespace Autodraw.ImageInput;

/// <summary>Platform-independent description of one display, expressed in virtual-screen pixels.
/// <see cref="Index"/> is the stable 0-based position inside an ordered <see cref="MonitorCatalog"/>
/// (the primary display is always index 0) so it can be persisted as <c>monitor_index</c>.</summary>
public readonly record struct MonitorInfo(
    int Index,
    int Left,
    int Top,
    int Width,
    int Height,
    double Scaling,
    bool IsPrimary)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;

    public bool ContainsPixel(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    /// <summary>True when the inclusive <see cref="ScreenRect"/> lies completely inside this display.</summary>
    public bool ContainsRegion(ScreenRect region) =>
        region.Left >= Left && region.Top >= Top && region.Right < Right && region.Bottom < Bottom;
}

public static class MonitorCatalog
{
    /// <summary>Orders displays with the primary first and renumbers them from zero. The primary
    /// display therefore always maps to the persisted default <c>monitor_index = 0</c>.</summary>
    public static IReadOnlyList<MonitorInfo> Order(IEnumerable<MonitorInfo> monitors)
    {
        var ordered = monitors
            .OrderBy(monitor => monitor.IsPrimary ? 0 : 1)
            .ThenBy(monitor => monitor.Left)
            .ThenBy(monitor => monitor.Top)
            .ToList();

        for (var index = 0; index < ordered.Count; index++)
            ordered[index] = ordered[index] with { Index = index };

        return ordered;
    }

    /// <summary>Resolves a persisted monitor index. Unknown or disconnected displays fall back to
    /// index 0 (the primary display) and report <paramref name="fellBack"/> so the caller can
    /// surface the non-fatal status message and repair the saved index.</summary>
    public static MonitorInfo? Resolve(IReadOnlyList<MonitorInfo> monitors, int requestedIndex, out bool fellBack)
    {
        fellBack = false;
        if (monitors.Count == 0) return null;
        if (requestedIndex >= 0 && requestedIndex < monitors.Count) return monitors[requestedIndex];
        fellBack = true;
        return monitors[0];
    }
}
