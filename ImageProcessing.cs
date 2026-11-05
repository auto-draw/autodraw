using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media;
using SkiaSharp;
using Autodraw.Configuration;
using Autodraw.Painting;

namespace Autodraw;

public static class ImageProcessing
{
    private static long Process_MemPressure;

    private static readonly Pattern patternCrosshatch = new()
    {
        Pat =
            "0 0 0 1 0 0 0\n0 0 0 1 0 0 0\n0 0 0 1 0 0 0\n1 1 1 1 1 1 1\n0 0 0 1 0 0 0\n0 0 0 1 0 0 0\n0 0 0 1 0 0 0\n",
        Width = 7, Height = 7
    };

    private static readonly Pattern patternDiagCross = new()
    {
        Pat =
            "1 0 0 0 0 0 1\n0 1 0 0 0 1 0\n0 0 1 0 1 0 0\n0 0 0 1 0 0 0\n0 0 1 0 1 0 0\n0 1 0 0 0 1 0\n1 0 0 0 0 0 1\n",
        Width = 6,
        Height = 6
    };

    private static readonly List<int[]> listCrosshatch = ReadPattern(patternCrosshatch.Pat);
    private static readonly List<int[]> listDiagCross = ReadPattern(patternDiagCross.Pat);

    public static Filters _currentFilters = new()
        { Invert = false, MaxThreshold = 127, AlphaThreshold = 200 };

    public static List<int[]> ReadPattern(string pat)
    {
        var lines = pat.Split('\n');

        List<int[]> positions = new();

        for (var i = 0; i < lines.Length; i++)
        {
            var numbers = lines[i].Split(' ');

            for (var j = 0; j < numbers.Length; j++)
                if (numbers[j] == "1")
                    positions.Add(new[] { j, i });
        }

        return positions;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MakePixel(byte red, byte green, byte blue, byte alpha)
    {
        return (uint)((alpha << 24) | (blue << 16) | (green << 8) | red);
    }

    private static unsafe void GetPixel(uint* input, out byte red, out byte green, out byte blue, out byte alpha)
    {
        red = (byte)(*input & 0xFF);
        green = (byte)((*input >> 8) & 0xFF);
        blue = (byte)((*input >> 16) & 0xFF);
        alpha = (byte)((*input >> 24) & 0xFF);
    }

    public static byte AdjustThresh(byte rByte, byte gByte, byte bByte, byte aByte, bool doinvert, float maxthresh, float minthresh, byte athresh)
    {
        float lumen = (rByte + gByte + bByte) / 3;
        byte localThresh = (byte)(lumen > maxthresh || lumen < minthresh || aByte < athresh ? 255 : 0);
        localThresh = doinvert == false ? localThresh : (byte)(255 - localThresh);
        return localThresh;
    }

    private static byte GetThreshold(float luminosity, byte alphaByte, Filters filterSettings)
    {
        byte threshByte =
            (byte)(luminosity > filterSettings.MaxThreshold || luminosity < filterSettings.MinThreshold || alphaByte < filterSettings.AlphaThreshold
                ? 255
                : 0);
        return filterSettings.Invert == false ? threshByte : (byte)(255 - threshByte);
    }

    private static unsafe byte GetOutlineAlpha(int y, int x, uint* basePtr, int width, int height, byte threshByte, Filters filterSettings)
    {
        byte returnByte = 255;
        var doOutline = false;
        foreach (var i in Enumerable.Range(0, 4))
        {
            uint* pixelAddress;
            var outOfBounds = i switch
            {
                0 => y - 1 < 0,
                1 => x - 1 < 0,
                2 => x + 1 >= width,
                3 => y + 1 >= height,
                _ => false
            };
            if (outOfBounds)
            {
                doOutline = true;
            }
            else
            {
                pixelAddress = i switch
                {
                    0 => basePtr + width * (y - 1) + x,
                    1 => basePtr + width * y + (x - 1),
                    2 => basePtr + width * y + (x + 1),
                    3 => basePtr + width * (y + 1) + x,
                    _ => throw new ArgumentException($"Invalid value {i}")
                };
                GetPixel(pixelAddress, out var rByte, out var gByte, out var bByte, out var aByte);
                byte localThresh = AdjustThresh(rByte, gByte, bByte, aByte, filterSettings.Invert, filterSettings.MaxThreshold, filterSettings.MinThreshold, filterSettings.AlphaThreshold);
                if (localThresh == 255) doOutline = true;
            }
        }
        if (doOutline) returnByte = 0;
        return returnByte;
    }

    private static byte GetPatternAlpha(int y, int x, byte returnByte, Filters filterSettings)
    {
        if (returnByte != 0 && (filterSettings.Crosshatch || filterSettings.DiagCrosshatch || filterSettings.HorizontalLines > 0 || filterSettings.VerticalLines > 0))
        {
            if (filterSettings.DiagCrosshatch)
                foreach (var patPoint in listDiagCross)
                    if (x % patternDiagCross.Width == patPoint[0] && y % patternDiagCross.Height == patPoint[1])
                        returnByte = 0;
            if (filterSettings.Crosshatch)
                foreach (var patPoint in listCrosshatch)
                    if (x % patternCrosshatch.Width == patPoint[0] && y % patternCrosshatch.Height == patPoint[1])
                        returnByte = 0;
        }
        return returnByte;
    }

    private static bool IsAnyPatternSet(this Filters filters)
    {
        return filters.Crosshatch || filters.DiagCrosshatch;
    }

    private static unsafe SKBitmap GenerateBorder(int thickness, int width, int height)
    {
        SKBitmap borderBitmap = new SKBitmap(width, height);

        var srcPtr = (byte*)borderBitmap.GetPixels().ToPointer();

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            byte returnByte = 255;
            if (x < thickness || x >= width - thickness ||
                y < thickness || y >= height - thickness) returnByte = 0;

            *srcPtr++ = returnByte;
            *srcPtr++ = returnByte;
            *srcPtr++ = returnByte;
            *srcPtr++ = 255;
        }

        return borderBitmap;
    }

    private static unsafe SKBitmap GeneratePattern(decimal horizontal, decimal vertical, int width, int height)
    {
        SKBitmap patternBitmap = new SKBitmap(width, height);

        var srcPtr = (byte*)patternBitmap.GetPixels().ToPointer();

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            byte returnByte = 255;
            if (horizontal > 0)
                if (y % horizontal == 0)
                    returnByte = 0;
            if (vertical > 0)
                if (x % vertical == 0)
                    returnByte = 0;

            *srcPtr++ = returnByte;
            *srcPtr++ = returnByte;
            *srcPtr++ = returnByte;
            *srcPtr++ = 255;
        }

        return patternBitmap;
    }

    public static unsafe SKBitmap Process(SKBitmap sourceBitmap, Filters filterSettings)
    {
        if (Process_MemPressure > 0) GC.RemoveMemoryPressure(Process_MemPressure);
        Process_MemPressure = sourceBitmap.BytesPerPixel * sourceBitmap.Width * sourceBitmap.Height;
        var height = sourceBitmap.Height;
        var width = sourceBitmap.Width;

        SKBitmap outputBitmap = new(width, height);

        var basePtr = (uint*)sourceBitmap.GetPixels().ToPointer();
        var returnPtr = (uint*)outputBitmap.GetPixels().ToPointer();
        var isPatSet = filterSettings.IsAnyPatternSet();
        var outline = filterSettings.Outline;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var srcPtr = basePtr + width * y + x;
            GetPixel(srcPtr, out var redByte, out var greenByte, out var blueByte, out var alphaByte);
            var luminosity = (redByte + greenByte + blueByte) / 3;
            byte threshByte = GetThreshold(luminosity, alphaByte, filterSettings);

            if (threshByte == 255)
            {
                *returnPtr++ = 0xffffffff;
                continue;
            }

            byte returnByte = 255;
            if (outline)
            {
                returnByte = GetOutlineAlpha(y, x, basePtr, width, height, threshByte, filterSettings);
            }

            if (isPatSet)
            {
                returnByte = GetPatternAlpha(y, x, returnByte, filterSettings);
            }
            else if (!outline)
            {
                returnByte = threshByte;
            }
            *returnPtr++ = MakePixel(returnByte, returnByte, returnByte, 255);
        }

        var invert = new float[20] {
            -1f, 0f,  0f,  0f,  1f,
            0f,  -1f, 0f,  0f,  1f,
            0f,  0f,  -1f, 0f,  1f,
            0f,  0f,  0f,  1f,  0f
        };

        SKImage InvertImage(SKImage image)
        {
            return image.ApplyImageFilter(SKImageFilter.CreateColorFilter(SKColorFilter.CreateColorMatrix(invert)), new SKRectI(0, 0, width, height), new SKRectI(0, 0, width, height), out _, out SKPoint _);
        }

        var OutImage = SKImage.FromBitmap(outputBitmap);
        if (filterSettings.ErosionAdvanced > 0)
        {
            var OutImageAnti = OutImage.ApplyImageFilter(
                SKImageFilter.CreateDilate(filterSettings.ErosionAdvanced, filterSettings.ErosionAdvanced),
                new SKRectI(0, 0, width, height),
                new SKRectI(filterSettings.ErosionAdvanced, filterSettings.ErosionAdvanced, width - filterSettings.ErosionAdvanced, height - filterSettings.ErosionAdvanced),
                out _,
                out SKPoint _
            );
            OutImage = OutImageAnti;
        }

        if (filterSettings.InlineAdvanced > 0 || filterSettings.InlineBorderAdvanced > 0 || filterSettings.OutlineAdvanced > 0 ||
            filterSettings.HorizontalLines > 0 || filterSettings.VerticalLines > 0 || filterSettings.BorderAdvanced > 0)
        {
            SKImageInfo imageInfo = new SKImageInfo(OutImage.Width, OutImage.Height);
            using SKSurface primarySurface = SKSurface.Create(imageInfo);

            SKPaint lightenPaint = new SKPaint();
            lightenPaint.BlendMode = SKBlendMode.Lighten;
            SKPaint darkenPaint = new SKPaint();
            darkenPaint.BlendMode = SKBlendMode.Darken;

            bool hasAppliedAnother = false;

            if (filterSettings.InlineAdvanced > 0)
            {
                using SKPaint inlinePaint = new SKPaint();
                inlinePaint.ImageFilter = SKImageFilter.CreateDilate(filterSettings.InlineAdvanced, filterSettings.InlineAdvanced);
                primarySurface.Canvas.DrawImage(OutImage, 0, 0, inlinePaint);
                primarySurface.Canvas.DrawImage(InvertImage(primarySurface.Snapshot()), 0, 0);
                primarySurface.Canvas.DrawImage(OutImage, 0, 0, lightenPaint);
                hasAppliedAnother = true;
            }

            if (filterSettings.OutlineAdvanced > 0)
            {
                using SKPaint outlinePaint = new SKPaint();
                outlinePaint.ImageFilter = SKImageFilter.CreateDilate(filterSettings.OutlineAdvanced, filterSettings.OutlineAdvanced);
                using SKSurface secondarySurface = SKSurface.Create(imageInfo);
                secondarySurface.Canvas.DrawImage(InvertImage(OutImage), 0, 0, outlinePaint);
                secondarySurface.Canvas.DrawImage(InvertImage(secondarySurface.Snapshot()), 0, 0);
                secondarySurface.Canvas.DrawImage(InvertImage(OutImage), 0, 0, lightenPaint);
                primarySurface.Canvas.DrawImage(secondarySurface.Snapshot(), 0, 0, darkenPaint);
                hasAppliedAnother = true;
            }

            if (filterSettings.InlineBorderAdvanced > 0)
            {
                using SKSurface secondarySurface = SKSurface.Create(imageInfo);
                secondarySurface.Canvas.DrawImage(OutImage, 0, 0);
                secondarySurface.Canvas.DrawImage(SKImage.FromBitmap(GenerateBorder(filterSettings.InlineBorderAdvanced, width, height)), 0, 0, lightenPaint);
                primarySurface.Canvas.DrawImage(secondarySurface.Snapshot(), 0, 0, darkenPaint);
                hasAppliedAnother = true;
            }

            if (filterSettings.HorizontalLines > 0 || filterSettings.VerticalLines > 0)
            {
                filterSettings.HorizontalLines = Math.Min(4096, filterSettings.HorizontalLines);
                filterSettings.VerticalLines = Math.Min(4096, filterSettings.VerticalLines);

                SKBitmap patternBitmap = GeneratePattern(filterSettings.HorizontalLines, filterSettings.VerticalLines, width, height);
                using SKSurface secondarySurface = SKSurface.Create(imageInfo);

                secondarySurface.Canvas.DrawBitmap(patternBitmap, 0, 0);
                secondarySurface.Canvas.DrawImage(OutImage, 0, 0, lightenPaint);

                primarySurface.Canvas.DrawImage(secondarySurface.Snapshot(), 0, 0, darkenPaint);
                hasAppliedAnother = true;
            }

            if (filterSettings.BorderAdvanced > 0)
            {
                if (!hasAppliedAnother)
                {
                    primarySurface.Canvas.DrawImage(OutImage, 0, 0);
                }
                primarySurface.Canvas.DrawImage(SKImage.FromBitmap(GenerateBorder(filterSettings.BorderAdvanced, width, height)), 0, 0, darkenPaint);
            }

            SKImage MergeImage = primarySurface.Snapshot();
            OutImage = MergeImage;
        }

        outputBitmap = SKBitmap.FromImage(OutImage);

        GC.AddMemoryPressure(Process_MemPressure);
        return outputBitmap;
    }

    /// <summary>
    /// Converts an RGBA source bitmap into a 0/255 black/white mask bitmap suitable for the
    /// existing drawing path.  This is intentionally a single pass - the previous implementation
    /// re-thresholded an already binary mask through the full filter pipeline.
    /// </summary>
    public static unsafe SKBitmap ProcessLayerMask(SKBitmap sourceBitmap, Filters filterSettings)
    {
        var width = sourceBitmap.Width;
        var height = sourceBitmap.Height;
        var maskBitmap = new SKBitmap(width, height);
        var srcPtr = (uint*)sourceBitmap.GetPixels().ToPointer();
        var dstPtr = (uint*)maskBitmap.GetPixels().ToPointer();

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            GetPixel(srcPtr++, out _, out _, out _, out var alphaByte);
            *dstPtr++ = alphaByte >= filterSettings.AlphaThreshold
                ? MakePixel(0, 0, 0, 255)
                : MakePixel(255, 255, 255, 255);
        }

        // Apply any user-configured outline / pattern / advanced filters on top of the binary mask.
        var maskFilters = new Filters
        {
            AlphaThreshold = 0,
            MaxThreshold = 127,
            MinThreshold = 0,
            Crosshatch = filterSettings.Crosshatch,
            DiagCrosshatch = filterSettings.DiagCrosshatch,
            HorizontalLines = filterSettings.HorizontalLines,
            VerticalLines = filterSettings.VerticalLines,
            Invert = false,
            Outline = filterSettings.Outline,
            BorderAdvanced = filterSettings.BorderAdvanced,
            OutlineAdvanced = filterSettings.OutlineAdvanced,
            InlineAdvanced = filterSettings.InlineAdvanced,
            InlineBorderAdvanced = filterSettings.InlineBorderAdvanced,
            ErosionAdvanced = filterSettings.ErosionAdvanced
        };

        var processedBitmap = Process(maskBitmap, maskFilters);
        maskBitmap.Dispose();
        return processedBitmap;
    }

    public static unsafe Color GetColor(SKBitmap sourceBitmap)
    {
        var width = sourceBitmap.Width;
        var height = sourceBitmap.Height;
        var srcPtr = (byte*)sourceBitmap.GetPixels().ToPointer();

        for (var row = 0; row < height; row++)
        for (var col = 0; col < width; col++)
        {
            var b = *srcPtr++;
            var g = *srcPtr++;
            var r = *srcPtr++;
            var a = *srcPtr++;

            if (a == 255)
            {
                return Color.FromArgb(a, r, g, b);
            }
        }
        return Color.FromArgb(0, 0, 0, 0);
    }

    public static unsafe SKBitmap NormalizeColor(this SKBitmap SourceBitmap)
    {
        var srcColor = SourceBitmap.ColorType;
        var srcAlpha = SourceBitmap.AlphaType;

        if (srcColor == SKColorType.Bgra8888) return SourceBitmap;

        SKBitmap OutputBitmap = new(SourceBitmap.Width, SourceBitmap.Height);

        var srcPtr = (byte*)SourceBitmap.GetPixels().ToPointer();
        var dstPtr = (byte*)OutputBitmap.GetPixels().ToPointer();

        var width = OutputBitmap.Width;
        var height = OutputBitmap.Height;

        var outColor = OutputBitmap.ColorType;

        for (var row = 0; row < height; row++)
        for (var col = 0; col < width; col++)
            if (srcColor == SKColorType.Gray8 || srcColor == SKColorType.Alpha8)
            {
                var b = *srcPtr++;
                *dstPtr++ = b;
                *dstPtr++ = b;
                *dstPtr++ = b;
                *dstPtr++ = 255;
            }
            else if (srcColor == SKColorType.Rgba8888)
            {
                var r = *srcPtr++;
                var g = *srcPtr++;
                var b = *srcPtr++;
                var a = *srcPtr++;
                *dstPtr++ = b;
                *dstPtr++ = g;
                *dstPtr++ = r;
                *dstPtr++ = a;
            }
            else if (srcColor == SKColorType.Argb4444)
            {
                var r = *srcPtr++;
                var g = *srcPtr++;
                var b = *srcPtr++;
                var a = *srcPtr++;
                *dstPtr++ = (byte)(b * 2);
                *dstPtr++ = (byte)(g * 2);
                *dstPtr++ = (byte)(r * 2);
                *dstPtr++ = (byte)(a * 2);
            }

        SourceBitmap.Dispose();
        SourceBitmap = OutputBitmap;

        return SourceBitmap;
    }

    public class Filters
    {
        public byte AlphaThreshold = 127;
        public byte MaxThreshold = 127;
        public byte MinThreshold = 0;

        public bool Crosshatch = false;
        public bool DiagCrosshatch = false;
        public decimal HorizontalLines = 0;
        public decimal VerticalLines = 0;

        public bool Invert = false;
        public bool Outline = false;

        public int BorderAdvanced = 5;
        public int OutlineAdvanced = 0;
        public int InlineAdvanced = 0;
        public int InlineBorderAdvanced = 0;
        public int ErosionAdvanced = 0;
    }

    public class Pattern
    {
        public int Height = 2;
        public string Pat = "0 0\n0 0";
        public int Width = 2;
    }

    // ---------------------------------------------------------------------
    // Palette extraction
    // ---------------------------------------------------------------------

    /// <summary>
    /// Produces the palette used by the colour-layer workflow.
    /// Histogram -> (median cut | KMeans++) -> merge-to-target-size.  All colour comparisons
    /// go through <see cref="Autodraw.Painting.ColorMatcher.DeltaE2000Lab"/> so this matches the
    /// layer assignment pass exactly.
    /// </summary>
    public static unsafe List<SKColor> ExtractPalette(SKBitmap sourceBitmap, int paletteSize, byte alphaThreshold, AutoDrawConfig? config = null)
    {
        paletteSize = Math.Clamp(paletteSize, 1, 64);

        var histogram = BuildHistogram(sourceBitmap, alphaThreshold, config);
        if (histogram.Count == 0) return new();

        // Scale candidate count with actual unique-color diversity, but never exceed the histogram.
        var candidateBudget = Math.Max(paletteSize * 8, Math.Min(histogram.Count, paletteSize * 64));
        var candidates = histogram
            .OrderByDescending(entry => entry.Value)
            .Take(candidateBudget)
            .Select(entry =>
            {
                var red = ((entry.Key >> 10) & 31) * 8 + 4;
                var green = ((entry.Key >> 5) & 31) * 8 + 4;
                var blue = (entry.Key & 31) * 8 + 4;
                return (Color: new SKColor((byte)red, (byte)green, (byte)blue), Weight: entry.Value);
            })
            .ToList();

        // If the image genuinely has fewer distinct colours than the requested palette, return
        // exactly those colours rather than inventing centroids from noise.
        if (candidates.Count <= paletteSize)
            return candidates.Select(c => c.Color).Distinct().ToList();

        var quantize = config?.QuantizeMethod ?? string.Empty;
        if (quantize.Contains("div", StringComparison.OrdinalIgnoreCase) ||
            quantize.Contains("wu", StringComparison.OrdinalIgnoreCase))
            return DivQuantizer(candidates, paletteSize);
        if (quantize.Contains("median", StringComparison.OrdinalIgnoreCase) ||
            quantize.Contains("lab color", StringComparison.OrdinalIgnoreCase))
            return MedianCut(candidates, paletteSize);

        // Default path: KMeans++.  The legacy "NeuQuant Neural-Net", MiniBatch and NeuQuant labels are
        // treated as the KMeans path, matching the historical (silent) behaviour.
        return KMeans(candidates, paletteSize, config);
    }

    private static unsafe Dictionary<int, int> BuildHistogram(SKBitmap sourceBitmap, byte alphaThreshold, AutoDrawConfig? config)
    {
        var histogram = new Dictionary<int, int>();
        var sourcePtr = (uint*)sourceBitmap.GetPixels().ToPointer();
        var width = sourceBitmap.Width;
        var height = sourceBitmap.Height;
        var pixelCount = width * height;

        // SeedsCount controls how many pixels vote.  Each sampled pixel votes with its *own*
        // quantized colour - never with a cell average - so thin edges and saturated accents
        // remain represented.
        var step = 1;
        if (config?.UseSeeds == true && config.SeedsCount > 0)
            step = Math.Max(1, pixelCount / config.SeedsCount);

        for (var i = 0; i < pixelCount; i += step)
        {
            GetPixel(sourcePtr + i, out var red, out var green, out var blue, out var alpha);
            if (alpha < alphaThreshold) continue;
            var redBin = red >> 3;
            var greenBin = green >> 3;
            var blueBin = blue >> 3;
            var key = (redBin << 10) | (greenBin << 5) | blueBin;
            histogram[key] = histogram.GetValueOrDefault(key) + 1;
        }
        return histogram;
    }

    private static List<SKColor> KMeans(List<(SKColor Color, int Weight)> candidates, int paletteSize, AutoDrawConfig? config)
    {
        var candidateCoords = new (double X, double Y, double Z)[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
            candidateCoords[i] = ToColorSpace(candidates[i].Color, config);

        var usePlusPlus = !string.Equals(config?.KmeansAlgorithm, "KMeans (Random)", StringComparison.OrdinalIgnoreCase);
        var random = new Random(0);

        var centroids = new List<(double X, double Y, double Z)>();

        if (usePlusPlus)
        {
            var totalWeight = candidates.Sum(c => (long)c.Weight);
            if (totalWeight <= 0) totalWeight = 1;
            var pick = random.NextInt64(totalWeight);
            long cumulative = 0;
            for (var i = 0; i < candidates.Count; i++)
            {
                cumulative += candidates[i].Weight;
                if (cumulative > pick)
                {
                    centroids.Add(candidateCoords[i]);
                    break;
                }
            }
        }
        if (centroids.Count == 0)
            centroids.Add(candidateCoords[0]);

        while (centroids.Count < paletteSize && centroids.Count < candidates.Count)
        {
            var distances = new double[candidates.Count];
            var totalDist = 0.0;
            for (var i = 0; i < candidates.Count; i++)
            {
                double minDist = double.MaxValue;
                foreach (var centroid in centroids)
                {
                    var d = WeightedSpaceDistance(candidateCoords[i].X, candidateCoords[i].Y, candidateCoords[i].Z, centroid.X, centroid.Y, centroid.Z, config);
                    if (d < minDist) minDist = d;
                }
                distances[i] = candidates[i].Weight * Math.Max(minDist, 0.0);
                totalDist += distances[i];
            }

            if (totalDist <= 0) break;

            if (usePlusPlus)
            {
                var pick = random.NextDouble() * totalDist;
                double cumulative = 0;
                var added = false;
                for (var i = 0; i < candidates.Count; i++)
                {
                    cumulative += distances[i];
                    if (cumulative >= pick)
                    {
                        centroids.Add(candidateCoords[i]);
                        added = true;
                        break;
                    }
                }
                if (!added)
                    centroids.Add(candidateCoords[^1]);
            }
            else
            {
                centroids.Add(candidateCoords[random.Next(candidates.Count)]);
            }
        }

        const int maxIterations = 64;
        const double convergenceThreshold = 0.5;
        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var xTotals = new double[centroids.Count];
            var yTotals = new double[centroids.Count];
            var zTotals = new double[centroids.Count];
            var weights = new long[centroids.Count];

            for (var i = 0; i < candidates.Count; i++)
            {
                var closest = ClosestSpaceCentroid(candidateCoords[i], centroids, config);
                var weight = candidates[i].Weight;
                xTotals[closest] += candidateCoords[i].X * weight;
                yTotals[closest] += candidateCoords[i].Y * weight;
                zTotals[closest] += candidateCoords[i].Z * weight;
                weights[closest] += weight;
            }

            double maxMovement = 0;
            for (var i = 0; i < centroids.Count; i++)
            {
                if (weights[i] <= 0) continue;
                var newX = xTotals[i] / weights[i];
                var newY = yTotals[i] / weights[i];
                var newZ = zTotals[i] / weights[i];
                var movement = Math.Sqrt(
                    (newX - centroids[i].X) * (newX - centroids[i].X) +
                    (newY - centroids[i].Y) * (newY - centroids[i].Y) +
                    (newZ - centroids[i].Z) * (newZ - centroids[i].Z));
                if (movement > maxMovement) maxMovement = movement;
                centroids[i] = (newX, newY, newZ);
            }

            if (maxMovement < convergenceThreshold) break;
        }

        var colors = centroids.Select(c => FromColorSpace(c.X, c.Y, c.Z, config)).ToList();
        return MergeToTargetSize(colors, candidates, paletteSize, config);
    }

    private static int ClosestSpaceCentroid((double X, double Y, double Z) coord, List<(double X, double Y, double Z)> centroids, AutoDrawConfig? config)
    {
        var closest = 0;
        var distance = double.MaxValue;
        for (var i = 0; i < centroids.Count; i++)
        {
            var d = WeightedSpaceDistance(coord.X, coord.Y, coord.Z, centroids[i].X, centroids[i].Y, centroids[i].Z, config);
            if (d < distance)
            {
                distance = d;
                closest = i;
            }
        }
        return closest;
    }

    private static List<SKColor> MergeToTargetSize(List<SKColor> colors, List<(SKColor Color, int Weight)> candidates, int targetSize, AutoDrawConfig? config)
    {
        var result = new List<SKColor>(targetSize);
        var seen = new HashSet<int>();

        // 1. Keep distinct existing centroids.
        foreach (var color in colors)
        {
            var key = (color.Red << 16) | (color.Green << 8) | color.Blue;
            if (seen.Add(key)) result.Add(color);
            if (result.Count >= targetSize) return result;
        }

        // 2. If we lost any to rounding, replace with the most-popular candidates that are not
        //    already represented by an existing centroid.  The 4.0 minimum ΔE2000 keeps them
        //    visually meaningful instead of near-duplicates.
        foreach (var candidate in candidates.OrderByDescending(c => c.Weight))
        {
            if (result.Count >= targetSize) break;
            var key = (candidate.Color.Red << 16) | (candidate.Color.Green << 8) | candidate.Color.Blue;
            if (seen.Contains(key)) continue;

            ColorMatcher.ToLab(candidate.Color, out var cL, out var cA, out var cB);
            var tooClose = false;
            foreach (var existing in result)
            {
                ColorMatcher.ToLab(existing, out var eL, out var eA, out var eB);
                if (ColorMatcher.DeltaE2000Lab(cL, cA, cB, eL, eA, eB) < 4.0) { tooClose = true; break; }
            }
            if (tooClose) continue;

            seen.Add(key);
            result.Add(candidate.Color);
        }

        return result;
    }

    private static List<SKColor> MedianCut(List<(SKColor Color, int Weight)> candidates, int paletteSize)
    {
        var boxes = new List<List<(SKColor Color, int Weight)>> { candidates };
        while (boxes.Count < paletteSize)
        {
            var box = boxes.Where(b => b.Count > 1).OrderByDescending(ColourRange).FirstOrDefault();
            if (box is null) break;
            boxes.Remove(box);
            var channel = WidestChannel(box);
            var ordered = box.OrderBy(c => Channel(c.Color, channel)).ToList();
            var half = ordered.Sum(c => c.Weight) / 2;
            var accumulated = 0; var split = 1;
            for (; split < ordered.Count; split++) { accumulated += ordered[split - 1].Weight; if (accumulated >= half) break; }
            if (split <= 0) split = 1;
            if (split >= ordered.Count) split = Math.Max(1, ordered.Count - 1);
            boxes.Add(ordered.Take(split).ToList()); boxes.Add(ordered.Skip(split).ToList());
        }
        return boxes.Select(box =>
        {
            if (box.Count == 0) return new SKColor(0, 0, 0);
            var totalWeight = box.Sum(c => c.Weight);
            if (totalWeight <= 0) totalWeight = 1;
            return new SKColor(
                (byte)Math.Clamp((int)Math.Round(box.Sum(c => c.Color.Red * c.Weight) / (double)totalWeight), 0, 255),
                (byte)Math.Clamp((int)Math.Round(box.Sum(c => c.Color.Green * c.Weight) / (double)totalWeight), 0, 255),
                (byte)Math.Clamp((int)Math.Round(box.Sum(c => c.Color.Blue * c.Weight) / (double)totalWeight), 0, 255));
        }).ToList();
    }

    private static List<SKColor> DivQuantizer(List<(SKColor Color, int Weight)> candidates, int paletteSize)
    {
        var boxes = new List<List<(SKColor Color, int Weight)>> { candidates };
        while (boxes.Count < paletteSize)
        {
            int bestIndex = -1;
            int bestChannel = -1;
            double bestScore = -1;
            for (var bi = 0; bi < boxes.Count; bi++)
            {
                var box = boxes[bi];
                if (box.Count < 2) continue;
                var meanR = box.Sum(c => c.Color.Red * c.Weight) / (double)box.Sum(c => c.Weight);
                var meanG = box.Sum(c => c.Color.Green * c.Weight) / (double)box.Sum(c => c.Weight);
                var meanB = box.Sum(c => c.Color.Blue * c.Weight) / (double)box.Sum(c => c.Weight);
                var variances = new double[3];
                var weights = box.Sum(c => c.Weight);
                foreach (var c in box)
                {
                    var wr = c.Weight / (double)weights;
                    variances[0] += wr * Math.Pow(c.Color.Red - meanR, 2);
                    variances[1] += wr * Math.Pow(c.Color.Green - meanG, 2);
                    variances[2] += wr * Math.Pow(c.Color.Blue - meanB, 2);
                }
                var score = variances.Max();
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = bi;
                    bestChannel = Array.IndexOf(variances, variances.Max());
                }
            }

            if (bestIndex < 0 || bestScore <= 0) break;

            var target = boxes[bestIndex];
            boxes.RemoveAt(bestIndex);
            var ordered = target.OrderBy(c => bestChannel == 0 ? c.Color.Red : bestChannel == 1 ? c.Color.Green : c.Color.Blue).ToList();
            var totalWeight = ordered.Sum(c => c.Weight);
            var half = totalWeight / 2;
            var accumulated = 0;
            var split = 1;
            for (; split < ordered.Count; split++)
            {
                accumulated += ordered[split - 1].Weight;
                if (accumulated >= half) break;
            }
            if (split <= 0) split = 1;
            if (split >= ordered.Count) split = Math.Max(1, ordered.Count - 1);
            boxes.Add(ordered.Take(split).ToList());
            boxes.Add(ordered.Skip(split).ToList());
        }

        return boxes.Select(box =>
        {
            if (box.Count == 0) return new SKColor(0, 0, 0);
            var totalWeight = box.Sum(c => c.Weight);
            if (totalWeight <= 0) totalWeight = 1;
            return new SKColor(
                (byte)Math.Clamp((int)Math.Round(box.Sum(c => c.Color.Red * c.Weight) / (double)totalWeight), 0, 255),
                (byte)Math.Clamp((int)Math.Round(box.Sum(c => c.Color.Green * c.Weight) / (double)totalWeight), 0, 255),
                (byte)Math.Clamp((int)Math.Round(box.Sum(c => c.Color.Blue * c.Weight) / (double)totalWeight), 0, 255));
        }).ToList();
    }

    private static int Channel(SKColor color, int channel) => channel == 0 ? color.Red : channel == 1 ? color.Green : color.Blue;
    private static int WidestChannel(List<(SKColor Color, int Weight)> box)
    {
        var ranges = Enumerable.Range(0, 3).Select(channel => box.Max(c => Channel(c.Color, channel)) - box.Min(c => Channel(c.Color, channel))).ToArray();
        return Array.IndexOf(ranges, ranges.Max());
    }
    private static int ColourRange(List<(SKColor Color, int Weight)> box) => Enumerable.Range(0, 3).Max(channel => box.Max(c => Channel(c.Color, channel)) - box.Min(c => Channel(c.Color, channel)));

    private static double ColorDistance(SKColor color, (float Red, float Green, float Blue) centroid, AutoDrawConfig? config = null)
    {
        var centroidColor = new SKColor((byte)centroid.Red, (byte)centroid.Green, (byte)centroid.Blue);
        var a = ToColorSpace(color, config);
        var b = ToColorSpace(centroidColor, config);
        return WeightedSpaceDistance(a.X, a.Y, a.Z, b.X, b.Y, b.Z, config);
    }

    private static (double X, double Y, double Z) ToColorSpace(SKColor color, AutoDrawConfig? config)
    {
        var method = (config?.ColorDistanceMethod ?? "CIELAB").ToUpperInvariant();
        switch (method)
        {
            case "RGB":
            case "SRGB":
                return (color.Red, color.Green, color.Blue);
            case "OKLAB":
                return RgbToOkLab(color);
            default:
                ColorMatcher.ToLab(color, out var l, out var a, out var b);
                return (l, a, b);
        }
    }

    private static SKColor FromColorSpace(double x, double y, double z, AutoDrawConfig? config)
    {
        var method = (config?.ColorDistanceMethod ?? "CIELAB").ToUpperInvariant();
        switch (method)
        {
            case "RGB":
            case "SRGB":
                return new SKColor((byte)Math.Clamp(x, 0, 255), (byte)Math.Clamp(y, 0, 255), (byte)Math.Clamp(z, 0, 255));
            case "OKLAB":
                return OkLabToRgb(x, y, z);
            default:
                return LabToRgb(x, y, z);
        }
    }

    private static double WeightedSpaceDistance(double x1, double y1, double z1, double x2, double y2, double z2, AutoDrawConfig? config)
    {
        if (config is null)
        {
            var dx = x1 - x2;
            var dy = y1 - y2;
            var dz = z1 - z2;
            return dx * dx + dy * dy + dz * dz;
        }

        var method = (config.ColorDistanceMethod ?? "CIELAB").ToUpperInvariant();
        if (method == "CIELAB")
        {
            var dl = (x1 - x2) * config.LabLumWeight;
            var da = y1 - y2;
            var db = z1 - z2;
            var chroma = Math.Sqrt(da * da + db * db) * config.LabChromaWeight;
            var chroma1 = Math.Sqrt(y1 * y1 + z1 * z1);
            var chroma2 = Math.Sqrt(y2 * y2 + z2 * z2);
            var meanChroma = Math.Sqrt(chroma1 * chroma2);
            var hueDiff = Math.Atan2(z1, y1) - Math.Atan2(z2, y2);
            while (hueDiff > Math.PI) hueDiff -= 2 * Math.PI;
            while (hueDiff < -Math.PI) hueDiff += 2 * Math.PI;
            var hue = Math.Abs(hueDiff) * meanChroma * config.LabHueWeight;
            return dl * dl + chroma * chroma + hue * hue;
        }

        var wx = (x1 - x2) * config.LabLumWeight;
        var wy = (y1 - y2) * config.LabChromaWeight;
        var wz = (z1 - z2) * config.LabHueWeight;
        return wx * wx + wy * wy + wz * wz;
    }

    private static (double X, double Y, double Z) RgbToOkLab(SKColor color)
    {
        static double Linear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        var r = Linear(color.Red / 255.0);
        var g = Linear(color.Green / 255.0);
        var b = Linear(color.Blue / 255.0);
        var l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b;
        var m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b;
        var s = 0.0883024619 * r + 0.2817188376 * g + 0.6299796995 * b;
        var l_ = Math.Cbrt(l);
        var m_ = Math.Cbrt(m);
        var s_ = Math.Cbrt(s);
        var L = 0.2104542553 * l_ + 0.7936177850 * m_ - 0.0040720468 * s_;
        var a = 1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_;
        var bb = 0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_;
        return (L, a, bb);
    }

    private static SKColor OkLabToRgb(double L, double a, double b)
    {
        var l_ = L + 0.3963377774 * a + 0.2158037573 * b;
        var m_ = L - 0.1055613458 * a - 0.0638541728 * b;
        var s_ = L - 0.0894841775 * a - 1.2914855480 * b;
        var l = l_ * l_ * l_;
        var m = m_ * m_ * m_;
        var s = s_ * s_ * s_;
        var r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
        var g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
        var bb = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;

        static double Encode(double v) => v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1.0 / 2.4) - 0.055;
        return new SKColor(
            (byte)Math.Clamp(Encode(r) * 255.0, 0, 255),
            (byte)Math.Clamp(Encode(g) * 255.0, 0, 255),
            (byte)Math.Clamp(Encode(bb) * 255.0, 0, 255));
    }

    private static int ClosestLabCentroid((double L, double A, double B) lab, List<(double L, double A, double B)> centroids, AutoDrawConfig? config)
    {
        var closest = 0;
        var distance = double.MaxValue;
        for (var i = 0; i < centroids.Count; i++)
        {
            var d = WeightedLabDistanceSquared(lab.L, lab.A, lab.B, centroids[i].L, centroids[i].A, centroids[i].B, config);
            if (d < distance)
            {
                distance = d;
                closest = i;
            }
        }
        return closest;
    }

    private static double WeightedLabDistanceSquared(double l1, double a1, double b1, double l2, double a2, double b2, AutoDrawConfig? config)
    {
        if (config is null)
        {
            var dl = l1 - l2;
            var da = a1 - a2;
            var db = b1 - b2;
            return dl * dl + da * da + db * db;
        }

        var dlw = (l1 - l2) * config.LabLumWeight;
        var da2 = a1 - a2;
        var db2 = b1 - b2;
        var chroma = Math.Sqrt(da2 * da2 + db2 * db2) * config.LabChromaWeight;

        var chroma1 = Math.Sqrt(a1 * a1 + b1 * b1);
        var chroma2 = Math.Sqrt(a2 * a2 + b2 * b2);
        var meanChroma = Math.Sqrt(chroma1 * chroma2);

        var hueDiff = Math.Atan2(b1, a1) - Math.Atan2(b2, a2);
        while (hueDiff > Math.PI) hueDiff -= 2 * Math.PI;
        while (hueDiff < -Math.PI) hueDiff += 2 * Math.PI;
        var hue = Math.Abs(hueDiff) * meanChroma * config.LabHueWeight;

        return dlw * dlw + chroma * chroma + hue * hue;
    }

    private static SKColor LabToRgb(double L, double a, double b)
    {
        static double InvF(double t) => t > 0.20689655172413793 ? t * t * t : (t - 16.0 / 116.0) / 7.787;
        var fy = (L + 16.0) / 116.0;
        var fx = fy + a / 500.0;
        var fz = fy - b / 200.0;
        var x = InvF(fx) * 0.95047;
        var y = InvF(fy) * 1.00000;
        var z = InvF(fz) * 1.08883;
        var r = x * 3.2406 + y * -1.5372 + z * -0.4986;
        var g = x * -0.9689 + y * 1.8758 + z * 0.0415;
        var blue = x * 0.0557 + y * -0.2040 + z * 1.0570;
        static double Encode(double v) => v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1.0 / 2.4) - 0.055;
        return new SKColor(
            (byte)Math.Clamp(Encode(r) * 255.0, 0, 255),
            (byte)Math.Clamp(Encode(g) * 255.0, 0, 255),
            (byte)Math.Clamp(Encode(blue) * 255.0, 0, 255));
    }

    private static int ClosestCentroid(SKColor color, List<(float Red, float Green, float Blue)> centroids, AutoDrawConfig? config = null)
    {
        var closest = 0;
        var distance = double.MaxValue;
        for (var i = 0; i < centroids.Count; i++)
        {
            var candidateDistance = ColorDistance(color, centroids[i], config);
            if (candidateDistance >= distance) continue;
            distance = candidateDistance;
            closest = i;
        }
        return closest;
    }

    public static unsafe List<ColorLayerResult> SplitImageByColors(SKBitmap sourceBitmap, List<SKColor> targetColors, byte alphaThreshold, AutoDrawConfig? config = null)
    {
        var width = sourceBitmap.Width;
        var height = sourceBitmap.Height;

        var results = new List<ColorLayerResult>();
        var layerPtrs = new uint*[targetColors.Count];

        for (int i = 0; i < targetColors.Count; i++)
        {
            var color = targetColors[i];
            var layerBmp = new SKBitmap(width, height);
            // Clearing once is much cheaper than writing a transparent pixel into every layer
            // for every source pixel (O(layers * pixels) writes).
            layerBmp.Erase(SKColors.Transparent);
            results.Add(new ColorLayerResult { Bitmap = layerBmp, Color = color, PixelCount = 0 });
            layerPtrs[i] = (uint*)layerBmp.GetPixels().ToPointer();
        }

        var targetCoords = targetColors.Select(tc => ToColorSpace(tc, config)).ToArray();

        var basePtr = (uint*)sourceBitmap.GetPixels().ToPointer();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var srcPtr = basePtr + width * y + x;
                GetPixel(srcPtr, out var r, out var g, out var b, out var a);

                if (a < alphaThreshold) continue;

                var pixelCoord = ToColorSpace(new SKColor(r, g, b), config);

                int closestIndex = 0;
                double minDistance = double.MaxValue;

                for (int i = 0; i < targetColors.Count; i++)
                {
                    var dist = WeightedSpaceDistance(pixelCoord.X, pixelCoord.Y, pixelCoord.Z, targetCoords[i].X, targetCoords[i].Y, targetCoords[i].Z, config);
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        closestIndex = i;
                    }
                }

                var tc = targetColors[closestIndex];
                *(layerPtrs[closestIndex] + width * y + x) = MakePixel(tc.Red, tc.Green, tc.Blue, 255);
                results[closestIndex].PixelCount++;
            }
        }

        if (config?.RemoveStray == true) MergeStrayPixels(results, width, height);
        if (config?.QuantizeStyle?.Contains("vector", StringComparison.OrdinalIgnoreCase) == true)
            SmoothLayerAssignments(results, width, height);
        return results;
    }

    /// <summary>Combines the transparent colour masks into a single preview bitmap.</summary>
    public static SKBitmap BuildComposite(IReadOnlyList<ColorLayerResult> layers, int width, int height)
    {
        var composite = new SKBitmap(width, height);
        using var canvas = new SKCanvas(composite);
        canvas.Clear(SKColors.Transparent);
        foreach (var layer in layers)
        {
            if (layer.PixelCount <= 0) continue;
            canvas.DrawBitmap(layer.Bitmap, 0, 0);
        }
        return composite;
    }

    /// <summary>Produces a grayscale copy used by HexedDrawer's Black &amp; White mode.</summary>
    public static SKBitmap ToGrayscale(SKBitmap source)
    {
        var result = source.Copy();
        for (var y = 0; y < result.Height; y++)
        for (var x = 0; x < result.Width; x++)
        {
            var color = result.GetPixel(x, y);
            var luma = (byte)Math.Clamp((int)Math.Round(color.Red * .2126 + color.Green * .7152 + color.Blue * .0722), 0, 255);
            result.SetPixel(x, y, new SKColor(luma, luma, luma, color.Alpha));
        }
        return result;
    }

    /// <summary>Moves isolated single pixels into the dominant neighbouring colour layer instead of
    /// deleting them. This is HexedDrawer's Smart Merge (V5) behaviour.</summary>
    private static unsafe void MergeStrayPixels(IReadOnlyList<ColorLayerResult> layers, int width, int height)
    {
        var labels = BuildLabelMap(layers, width, height);
        var moves = new List<(int X, int Y, int From, int To)>();
        var neighbours = new int[layers.Count];
        var pointers = new uint*[layers.Count];
        for (var i = 0; i < layers.Count; i++) pointers[i] = (uint*)layers[i].Bitmap.GetPixels().ToPointer();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var from = labels[y * width + x];
            if (from < 0) continue;
            Array.Clear(neighbours);
            var same = 0;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                var label = labels[ny * width + nx];
                if (label < 0) continue;
                if (label == from) same++;
                else neighbours[label]++;
            }
            if (same > 0) continue;
            var best = -1;
            var bestCount = 0;
            for (var i = 0; i < neighbours.Length; i++)
            {
                if (neighbours[i] <= bestCount) continue;
                bestCount = neighbours[i];
                best = i;
            }
            if (best >= 0) moves.Add((x, y, from, best));
        }

        foreach (var (x, y, from, to) in moves)
        {
            pointers[from][y * width + x] = 0;
            var color = layers[to].Color;
            pointers[to][y * width + x] = MakePixel(color.Red, color.Green, color.Blue, 255);
            layers[from].PixelCount = Math.Max(0, layers[from].PixelCount - 1);
            layers[to].PixelCount++;
        }
    }

    /// <summary>Majority-vote cleanup used by the Vectorized (Smooth) quantize style.</summary>
    private static unsafe void SmoothLayerAssignments(IReadOnlyList<ColorLayerResult> layers, int width, int height)
    {
        var pointers = new uint*[layers.Count];
        for (var i = 0; i < layers.Count; i++) pointers[i] = (uint*)layers[i].Bitmap.GetPixels().ToPointer();
        for (var pass = 0; pass < 2; pass++)
        {
            var labels = BuildLabelMap(layers, width, height);
            var moves = new List<(int X, int Y, int From, int To)>();
            var counts = new int[layers.Count];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var from = labels[y * width + x];
                if (from < 0) continue;
                Array.Clear(counts);
                var total = 0;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    var label = labels[ny * width + nx];
                    if (label < 0) continue;
                    counts[label]++;
                    total++;
                }
                if (total < 4) continue;
                var best = from;
                for (var i = 0; i < counts.Length; i++)
                    if (counts[i] > counts[best]) best = i;
                if (best != from && counts[best] >= 4) moves.Add((x, y, from, best));
            }

            foreach (var (x, y, from, to) in moves)
            {
                pointers[from][y * width + x] = 0;
                var color = layers[to].Color;
                pointers[to][y * width + x] = MakePixel(color.Red, color.Green, color.Blue, 255);
                layers[from].PixelCount = Math.Max(0, layers[from].PixelCount - 1);
                layers[to].PixelCount++;
            }
        }
    }

    private static unsafe int[] BuildLabelMap(IReadOnlyList<ColorLayerResult> layers, int width, int height)
    {
        var labels = Enumerable.Repeat(-1, width * height).ToArray();
        for (var i = 0; i < layers.Count; i++)
        {
            var bitmap = layers[i].Bitmap;
            var pixels = (byte*)bitmap.GetPixels().ToPointer();
            if (pixels == null || bitmap.BytesPerPixel != 4)
            {
                // Fallback for exotic bitmap formats; layer bitmaps are normally 4-byte BGRA.
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    if (bitmap.GetPixel(x, y).Alpha != 0) labels[y * width + x] = i;
                }
                continue;
            }

            var rowBytes = bitmap.RowBytes;
            for (var y = 0; y < height; y++)
            {
                var row = pixels + y * rowBytes;
                for (var x = 0; x < width; x++)
                {
                    if (row[x * 4 + 3] != 0) labels[y * width + x] = i;
                }
            }
        }
        return labels;
    }

    /// <summary>Copies a bitmap and makes configured background colours transparent before colour quantization.</summary>
    public static SKBitmap PrepareColorImage(SKBitmap source, AutoDrawConfig config)
    {
        var output = source.Copy();
        var background = config.BackgroundColorsToRemove.Select(SKColor.Parse).ToArray();
        if (background.Length == 0 && !config.UseBilateral) return output;
        if (background.Length > 0)
        {
            for (var y = 0; y < output.Height; y++)
            for (var x = 0; x < output.Width; x++)
            {
                var c = output.GetPixel(x, y);
                if (background.Any(bg => ColorMatcher.DeltaE2000(bg, c) <= config.LabContrastThreshold))
                    output.SetPixel(x, y, new SKColor(0, 0, 0, 0));
            }
        }
        if (!config.UseBilateral) return output;
        var filtered = ApplyBilateral(output, config);
        output.Dispose();
        return filtered;
    }

    // Deliberately bounded radius: a configuration import must not turn a large image into an unbounded operation.
    private static unsafe SKBitmap ApplyBilateral(SKBitmap source, AutoDrawConfig config)
    {
        var result = source.Copy();
        var radius = Math.Clamp(config.BilateralDiameter / 2, 1, 8);
        var space = Math.Max(config.BilateralSpaceSigma, .001);
        var range = Math.Max(config.BilateralColorSigma, .001);
        var diameter = radius * 2 + 1;
        var spatialWeights = new double[diameter * diameter];
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
            spatialWeights[(dy + radius) * diameter + dx + radius] = Math.Exp(-(dx * dx + dy * dy) / (2 * space * space));

        // Range weights use a bounded lookup table: the exact value is an exponential of squared
        // channel distance, and evaluating it for every neighbour of every pixel is too slow for
        // live auto-quantization on typical images.
        const int rangeBins = 1024;
        const double maxDifference = 3d * 255d * 255d;
        var rangeWeights = new double[rangeBins];
        for (var i = 0; i < rangeBins; i++)
            rangeWeights[i] = Math.Exp(-(maxDifference * i / (rangeBins - 1)) / (2 * range * range));

        if (source.BytesPerPixel == 4 && result.BytesPerPixel == 4)
        {
            var src = (byte*)source.GetPixels().ToPointer();
            var dst = (byte*)result.GetPixels().ToPointer();
            var rowBytes = source.RowBytes;
            var rowBytesOut = result.RowBytes;
            var rgba = source.ColorType == SKColorType.Rgba8888;
            var redOffset = rgba ? 0 : 2;
            var blueOffset = rgba ? 2 : 0;

            for (var y = 0; y < source.Height; y++)
            {
                var dstRow = dst + y * rowBytesOut;
                for (var x = 0; x < source.Width; x++)
                {
                    var center = src + y * rowBytes + x * 4;
                    var centerAlpha = center[3];
                    if (centerAlpha == 0)
                    {
                        *(uint*)(dstRow + x * 4) = 0;
                        continue;
                    }

                    double red = 0, green = 0, blue = 0, alpha = 0, total = 0;
                    for (var dy = -radius; dy <= radius; dy++)
                    {
                        var ny = y + dy;
                        if (ny < 0 || ny >= source.Height) continue;
                        var sampleRow = src + ny * rowBytes;
                        for (var dx = -radius; dx <= radius; dx++)
                        {
                            var nx = x + dx;
                            if (nx < 0 || nx >= source.Width) continue;
                            var sample = sampleRow + nx * 4;
                            if (sample[3] == 0) continue;

                            var dr = sample[redOffset] - center[redOffset];
                            var dg = sample[1] - center[1];
                            var db = sample[blueOffset] - center[blueOffset];
                            var difference = dr * dr + dg * dg + db * db;
                            var rangeIndex = (int)(difference * (rangeBins - 1) / maxDifference);
                            var weight = spatialWeights[(dy + radius) * diameter + dx + radius]
                                         * rangeWeights[Math.Clamp(rangeIndex, 0, rangeBins - 1)];
                            red += sample[redOffset] * weight;
                            green += sample[1] * weight;
                            blue += sample[blueOffset] * weight;
                            alpha += sample[3] * weight;
                            total += weight;
                        }
                    }

                    var target = dstRow + x * 4;
                    if (total <= 0)
                    {
                        target[0] = target[1] = target[2] = target[3] = 0;
                    }
                    else
                    {
                        target[redOffset] = (byte)Math.Clamp(red / total, 0, 255);
                        target[1] = (byte)Math.Clamp(green / total, 0, 255);
                        target[blueOffset] = (byte)Math.Clamp(blue / total, 0, 255);
                        target[3] = (byte)Math.Clamp(alpha / total, 0, 255);
                    }
                }
            }
            return result;
        }

        for (var y = 0; y < source.Height; y++) for (var x = 0; x < source.Width; x++)
        {
            var center = source.GetPixel(x, y); double red = 0, green = 0, blue = 0, alpha = 0, total = 0;
            for (var dy = -radius; dy <= radius; dy++) for (var dx = -radius; dx <= radius; dx++)
            {
                var nx = x + dx; var ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= source.Width || ny >= source.Height) continue;
                var sample = source.GetPixel(nx, ny);
                var spatial = spatialWeights[(dy + radius) * diameter + dx + radius];
                var difference = Math.Pow(sample.Red - center.Red, 2) + Math.Pow(sample.Green - center.Green, 2) + Math.Pow(sample.Blue - center.Blue, 2);
                var rangeIndex = (int)(difference * (rangeBins - 1) / maxDifference);
                var weight = spatial * rangeWeights[Math.Clamp(rangeIndex, 0, rangeBins - 1)];
                red += sample.Red * weight; green += sample.Green * weight; blue += sample.Blue * weight; alpha += sample.Alpha * weight; total += weight;
            }
            result.SetPixel(x, y, new SKColor((byte)(red / total), (byte)(green / total), (byte)(blue / total), (byte)(alpha / total)));
        }
        return result;
    }

    private static void RemoveSinglePixelComponents(List<ColorLayerResult> layers, int width, int height)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var layer in layers)
            {
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    if (layer.Bitmap.GetPixel(x, y).Alpha == 0) continue;
                    var neighbours = 0;
                    for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                        if ((dx != 0 || dy != 0) && x + dx >= 0 && y + dy >= 0 && x + dx < width && y + dy < height && layer.Bitmap.GetPixel(x + dx, y + dy).Alpha != 0) neighbours++;
                    if (neighbours == 0)
                    {
                        layer.Bitmap.SetPixel(x, y, new SKColor(0, 0, 0, 0));
                        layer.PixelCount = Math.Max(0, layer.PixelCount - 1);
                        changed = true;
                    }
                }
            }
        }
    }

    private readonly record struct Lab(double L, double A, double B);
    private static Lab ToLab(SKColor c)
    {
        static double Linear(double v) => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);
        var r = Linear(c.Red / 255d); var g = Linear(c.Green / 255d); var b = Linear(c.Blue / 255d);
        var x = (r * .4124 + g * .3576 + b * .1805) / .95047; var y = r * .2126 + g * .7152 + b * .0722; var z = (r * .0193 + g * .1192 + b * .9505) / 1.08883;
        static double F(double v) => v > .008856 ? Math.Pow(v, 1d / 3d) : 7.787 * v + 16d / 116d;
        var fx = F(x); var fy = F(y); var fz = F(z); return new Lab(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }
}

public class ColorLayerResult
{
    public SKBitmap Bitmap { get; set; } = null!;
    public SKColor Color { get; set; }
    public int PixelCount { get; set; }
}

