using System;
using Autodraw.Configuration;

namespace Autodraw.ImageInput;

/// <summary>Pure coordinate math for F1 region capture. All values are physical screen pixels;
/// logical drag points coming from an Avalonia overlay are multiplied by the target monitor's
/// scale exactly once.</summary>
public static class RegionCaptureMath
{
    /// <summary>Drags smaller than this many pixels are treated as an accidental click, not a capture.</summary>
    public const int MinimumSide = 2;

    /// <summary>Converts a logical drag rectangle inside <paramref name="monitor"/> into an
    /// inclusive physical <see cref="ScreenRect"/>. Returns null for a zero-area drag.</summary>
    public static ScreenRect? FromLogicalDrag(
        MonitorInfo monitor,
        double startX,
        double startY,
        double endX,
        double endY)
    {
        var scale = NormalizeScale(monitor.Scaling);
        var left = monitor.Left + (int)Math.Round(Math.Min(startX, endX) * scale);
        var top = monitor.Top + (int)Math.Round(Math.Min(startY, endY) * scale);
        var rightExclusive = monitor.Left + (int)Math.Round(Math.Max(startX, endX) * scale);
        var bottomExclusive = monitor.Top + (int)Math.Round(Math.Max(startY, endY) * scale);

        var rect = new ScreenRect(
            left,
            top,
            Math.Max(left, rightExclusive - 1),
            Math.Max(top, bottomExclusive - 1));

        return rect.Width < MinimumSide || rect.Height < MinimumSide ? null : rect;
    }

    /// <summary>Guards against a stale <see cref="Avalonia.Platform.Screen"/> reference that was
    /// unplugged between resolving it and completing the drag.</summary>
    public static bool IsRegionInside(ScreenRect region, MonitorInfo monitor) => monitor.ContainsRegion(region);

    /// <summary>Import never crops: oversized images are reported so the resize/fit step can scale
    /// them instead of silently losing pixels.</summary>
    public static bool ExceedsSurface(int imageWidth, int imageHeight, ScreenRect surface) =>
        imageWidth > surface.Width || imageHeight > surface.Height;

    private static double NormalizeScale(double scale) =>
        scale > 0 && !double.IsNaN(scale) && !double.IsInfinity(scale) ? scale : 1d;
}
