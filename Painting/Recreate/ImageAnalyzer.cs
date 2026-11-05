using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodraw.Configuration;
using SkiaSharp;

namespace Autodraw.Painting.Recreate;

/// <summary>Owns every mask produced by one image analysis pass.</summary>
public sealed class ImageAnalysisResult : IDisposable
{
    private bool _disposed;

    public ImageAnalysisResult(int width, int height, IReadOnlyList<SKColor> palette, IReadOnlyList<Layer> layers)
    {
        Width = width;
        Height = height;
        Palette = palette;
        Layers = layers;
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<SKColor> Palette { get; }
    public IReadOnlyList<Layer> Layers { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var layer in Layers) layer.Dispose();
    }
}

/// <summary>
/// The load-image -> quantize -> split-into-masks stage.  The resulting <see cref="ImageAnalysisResult"/>
/// owns the masks and can be handed directly to <see cref="RecreateEngine"/>.
/// </summary>
public static class ImageAnalyzer
{
    public static ImageAnalysisResult Analyze(
        SKBitmap source,
        RecreateOptions options,
        AutoDrawConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (source.Width <= 0 || source.Height <= 0)
            throw new ArgumentException("The source image has no pixels.", nameof(source));

        config ??= new AutoDrawConfig();
        // The run snapshot owns the palette colour space.  Assignment below uses the same config
        // instance, so clustering and layer masks can never disagree about the space.
        config.ColorDistanceMethod = RecreateOptions.NormalizeColorSpace(options.PaletteColorSpace);

        using var prepared = ImageProcessing.PrepareColorImage(source, config);
        cancellationToken.ThrowIfCancellationRequested();

        var palette = Quantizer.ExtractPalette(
            prepared,
            options.PaletteSize,
            options.AlphaThreshold,
            config,
            options.PaletteColorSpace);

        if (palette.Count == 0)
            return new ImageAnalysisResult(prepared.Width, prepared.Height, Array.Empty<SKColor>(), Array.Empty<Layer>());

        SKBitmap splitSource = prepared;
        SKBitmap? grayscale = null;
        if (config.Mode?.Equals("bw", StringComparison.OrdinalIgnoreCase) == true)
        {
            grayscale = ImageProcessing.ToGrayscale(prepared);
            splitSource = grayscale;
            palette = palette
                .Select(color =>
                {
                    var luma = (byte)Math.Clamp(
                        (int)Math.Round(color.Red * .2126 + color.Green * .7152 + color.Blue * .0722), 0, 255);
                    return new SKColor(luma, luma, luma);
                })
                .Distinct()
                .ToList();
        }

        var layers = new List<Layer>();
        try
        {
            var split = ImageProcessing.SplitImageByColors(
                splitSource, palette, (byte)Math.Clamp(options.AlphaThreshold, 0, 255), config);
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var result in split)
            {
                if (result.PixelCount <= 0)
                {
                    result.Bitmap.Dispose();
                    continue;
                }
                layers.Add(new Layer(result.Color, result.Bitmap, layers.Count, result.PixelCount));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var ordered = LayerOrdering.Apply(layers, options.LayerOrder);
            palette = ordered.Select(layer => layer.Color).ToList();
            return new ImageAnalysisResult(prepared.Width, prepared.Height, palette, ordered);
        }
        catch
        {
            foreach (var layer in layers) layer.Dispose();
            throw;
        }
        finally
        {
            grayscale?.Dispose();
        }
    }
}
