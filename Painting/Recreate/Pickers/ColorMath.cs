using System;
using Autodraw.Configuration;
using SkiaSharp;

namespace Autodraw.Painting.Recreate;

/// <summary>
/// A hue/saturation/value triple with normalised components.  <see cref="H"/> is in degrees,
/// <see cref="S"/> and <see cref="V"/> are in the range 0..1.
/// </summary>
public readonly record struct HsvColor(double H, double S, double V);

/// <summary>
/// Single source of truth for the RGB-to-HSV conversion used by every picker.  It is kept pure
/// (no Skia calls, no I/O) so the picker click-point math can be unit tested exhaustively.
/// </summary>
public static class ColorMath
{
    /// <summary>
    /// Converts an sRGB colour to HSV.  A neutral colour (R == G == B) has no hue; this method
    /// reports H = 0 for it, matching the convention used by every picker description in the
    /// project.  Callers must not rely on the hue of a colour whose saturation is zero.
    /// </summary>
    public static HsvColor ToHsv(SKColor color)
    {
        var r = color.Red / 255d;
        var g = color.Green / 255d;
        var b = color.Blue / 255d;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        var hue = 0d;
        if (delta > 0)
        {
            if (max == r)
                hue = 60d * (((g - b) / delta) % 6d);
            else if (max == g)
                hue = 60d * (((b - r) / delta) + 2d);
            else
                hue = 60d * (((r - g) / delta) + 4d);
        }

        hue = NormalizeHue(hue);
        var saturation = max <= 0 ? 0d : delta / max;
        return new HsvColor(hue, Math.Clamp(saturation, 0d, 1d), Math.Clamp(max, 0d, 1d));
    }

    /// <summary>Wraps an arbitrary hue in degrees into the range [0, 360).</summary>
    public static double NormalizeHue(double degrees)
    {
        if (!double.IsFinite(degrees)) return 0d;
        var wrapped = degrees % 360d;
        if (wrapped < 0) wrapped += 360d;
        return wrapped >= 360d ? 0d : wrapped;
    }

    /// <summary>Rounds a derived floating point coordinate to the integer click position.</summary>
    public static ScreenPoint ToPoint(double x, double y)
        => new((int)Math.Round(x, MidpointRounding.AwayFromZero), (int)Math.Round(y, MidpointRounding.AwayFromZero));
}
