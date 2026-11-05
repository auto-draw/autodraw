using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace Autodraw.Painting.Recreate;

/// <summary>
/// One quantized colour layer.  The engine owns the mask for the duration of a run and disposes
/// it when the run completes, fails, or is cancelled (unless <see cref="RecreateOptions.DisposeLayers"/>
/// is explicitly disabled by a caller that wants to keep the masks).
/// </summary>
public sealed class Layer : IDisposable
{
    private bool _disposed;

    public Layer(SKColor color, SKBitmap mask, int order, int pixelCount)
    {
        Color = color;
        Mask = mask ?? throw new ArgumentNullException(nameof(mask));
        Order = order;
        PixelCount = Math.Max(0, pixelCount);
    }

    public SKColor Color { get; }
    public SKBitmap Mask { get; }
    public int Order { get; set; }
    public int PixelCount { get; }
    public bool IsDisposed => _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Mask.Dispose();
    }
}

/// <summary>Assigns the sequential draw order used by the recreate engine.</summary>
public static class LayerOrdering
{
    public static readonly string[] ValidValues =
    {
        "coverage-desc", "coverage-asc", "luminance-desc", "luminance-asc", "manual"
    };

    public static string Normalize(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "largest" or "largest-area-first" or "largest area first" or "coverage" or "coverage-desc" => "coverage-desc",
            "smallest" or "smallest-area-first" or "coverage-asc" => "coverage-asc",
            "bright" or "brightness" or "brightness-to-darkness" or "luminance-desc" => "luminance-desc",
            "dark" or "darkness" or "darkness-to-brightness" or "luminance-asc" => "luminance-asc",
            "manual" or "user" or "as-is" or "source" => "manual",
            _ => "coverage-desc"
        };
    }

    /// <summary>Stable ordering: ties keep the palette order produced by the quantizer.</summary>
    public static List<Layer> Apply(IEnumerable<Layer> layers, string? order)
    {
        var list = layers.ToList();
        var normalized = Normalize(order);
        IEnumerable<Layer> ordered = normalized switch
        {
            "coverage-asc" => list.OrderBy(layer => layer.PixelCount),
            "luminance-desc" => list.OrderByDescending(Luminance),
            "luminance-asc" => list.OrderBy(Luminance),
            "manual" => list,
            _ => list.OrderByDescending(layer => layer.PixelCount)
        };

        var result = ordered.ToList();
        for (var index = 0; index < result.Count; index++) result[index].Order = index;
        return result;
    }

    private static double Luminance(Layer layer)
        => layer.Color.Red * .2126 + layer.Color.Green * .7152 + layer.Color.Blue * .0722;
}
