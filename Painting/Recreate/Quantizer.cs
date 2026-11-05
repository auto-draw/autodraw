using System;
using System.Collections.Generic;
using System.Linq;
using Autodraw.Configuration;
using SkiaSharp;

namespace Autodraw.Painting.Recreate;

/// <summary>
/// Palette extraction facade.  The quantizer itself lives in <see cref="ImageProcessing"/>; this
/// wrapper pins the colour space used for clustering and layer assignment to the run snapshot so
/// a mid-run settings change cannot alter an in-flight palette.
/// </summary>
public static class Quantizer
{
    public static List<SKColor> ExtractPalette(
        SKBitmap source,
        int paletteSize,
        int alphaThreshold,
        AutoDrawConfig? config = null,
        string? colorSpace = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Width <= 0 || source.Height <= 0) return new List<SKColor>();

        var runConfig = config ?? new AutoDrawConfig();
        runConfig.ColorDistanceMethod = RecreateOptions.NormalizeColorSpace(colorSpace ?? runConfig.ColorDistanceMethod);

        return ImageProcessing.ExtractPalette(
            source,
            Math.Clamp(paletteSize, 1, 64),
            (byte)Math.Clamp(alphaThreshold, 0, 255),
            runConfig)
            .Distinct()
            .ToList();
    }
}
