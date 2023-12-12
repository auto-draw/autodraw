#define SKVM_JIT_WHEN_POSSIBLE
#define SK_ENABLE_SKSL_INTERPRETER

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using SkiaSharp;

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
    
    // TODO: Rewrite allat to be image based patterns 🙄

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
    
    public static byte AdjustThresh(byte rByte, byte gByte, byte bByte, byte aByte,  bool doinvert, float maxthresh, float minthresh, byte athresh)
    {
        float lumen = (rByte + gByte + bByte) / 3;
        byte localThresh = (byte)(lumen > maxthresh || lumen < minthresh || aByte < athresh ? 255 : 0);
        localThresh = doinvert == false ? localThresh : (byte)(255 - localThresh);
        return localThresh;
    }

    public static unsafe SKBitmap Process(SKBitmap sourceBitmap, Filters filterSettings)
    {
        if (Process_MemPressure > 0) GC.RemoveMemoryPressure(Process_MemPressure);
        Process_MemPressure = sourceBitmap.BytesPerPixel * sourceBitmap.Width * sourceBitmap.Height;

        SKBitmap outputBitmap = new(sourceBitmap.Width, sourceBitmap.Height);

        var basePtr = (uint*)sourceBitmap.GetPixels().ToPointer();
        var returnPtr = (uint*)outputBitmap.GetPixels().ToPointer();

        var width = outputBitmap.Width;
        var height = outputBitmap.Height;

        var minthresh = filterSettings.minThreshold;
        var maxthresh = filterSettings.maxThreshold;
        var athresh = filterSettings.AlphaThreshold;

        var doinvert = filterSettings.Invert;
        var outline = filterSettings.Outline;
        var sharpoutline = filterSettings.OutlineSharp;

        var crosshatch = filterSettings.Crosshatch;
        var diagcross = filterSettings.DiagCrosshatch;
        var horizontals = filterSettings.HorizontalLines;
        var verticals = filterSettings.VerticalLines;


        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var srcPtr = basePtr + width * y + x;

            GetPixel(srcPtr, out var redByte, out var greenByte, out var blueByte, out var alphaByte);

            float luminosity = (redByte + greenByte + blueByte) / 3;

            var threshByte =
                (byte)(luminosity > maxthresh || luminosity < minthresh || alphaByte < athresh
                    ? 255
                    : 0); // Thresholds Filter

            threshByte = doinvert == false ? threshByte : (byte)(255 - threshByte); // Invert

            if (threshByte == 255)
            {
                *returnPtr++ = 0xffffffff;
                continue;
            }

            byte returnByte = 255;

            if (outline)
            {
                var doOutline = false;
                foreach (var i in Enumerable.Range(0, 8))
                {
                    var offset = i switch
                    {
                        0 => (-1, -1),
                        1 => (0, -1),
                        2 => (1, -1),
                        3 => (-1, 0),
                        4 => (1, 0),
                        5 => (-1, 1),
                        6 => (0, 1),
                        7 => (1, 1),
                        _ => (0, 0)
                    };

                    if (x + offset.Item1 < 0 || x + offset.Item1 >= width || y + offset.Item2 < 0 || y + offset.Item2 >= height)
                    {
                        doOutline = true;
                    }
                    else
                    {
                        uint* pixelAddress = basePtr + width * (y + offset.Item2) + (x + offset.Item1);
                        GetPixel(pixelAddress, out var rByte, out var gByte, out var bByte, out var aByte);
                        byte localThresh = AdjustThresh(rByte, gByte, bByte, aByte, doinvert, maxthresh, minthresh, athresh);
                        if (localThresh == 255) doOutline = true;
                    }    
                    if (doOutline)
                    {
                        returnByte = 0;
                        break;
                    }
                }
            }
            else if (sharpoutline)
            {
                var doOutline = false;
                foreach (var i in Enumerable.Range(0, 4))
                {
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
                        uint* pixelAddress = i switch
                        {
                            0 => basePtr + width * (y - 1) + x,
                            1 => basePtr + width * y + (x - 1),
                            2 => basePtr + width * y + (x + 1),
                            3 => basePtr + width * (y + 1) + x,
                            _ => throw new ArgumentException($"Invalid value {i}")
                        };

                        GetPixel(pixelAddress, out var rByte, out var gByte, out var bByte, out var aByte);
                        byte localThresh= AdjustThresh(rByte, gByte, bByte, aByte, doinvert, maxthresh, minthresh, athresh);
                        if (localThresh == 255) doOutline = true;
                    }
                }
                if (doOutline) returnByte = 0;
            }

            // Pattern Filters
            if (returnByte != 0 && (crosshatch || diagcross || horizontals > 0 || verticals > 0))
            {
                if (horizontals > 0) // Horizontal Stripes
                    if (y % horizontals == 0)
                        returnByte = 0;
                if (verticals > 0) // Vertical Stripes
                    if (x % verticals == 0)
                        returnByte = 0;
                if (diagcross) // Diag Crosshatch
                    foreach (var patPoint in listDiagCross)
                        if (x % patternDiagCross.Width == patPoint[0] && y % patternDiagCross.Height == patPoint[1])
                            returnByte = 0;
                if (crosshatch) // Crosshatch
                    foreach (var patPoint in listCrosshatch)
                        if (x % patternCrosshatch.Width == patPoint[0] && y % patternCrosshatch.Height == patPoint[1])
                            returnByte = 0;
            }
            else if (!outline && !sharpoutline)
            {
                returnByte = threshByte;
            }

            *returnPtr++ = MakePixel(returnByte, returnByte, returnByte, 255);
        }

        GC.AddMemoryPressure(Process_MemPressure);
        return outputBitmap;
    }

    public static unsafe SKBitmap NormalizeColor(this SKBitmap SourceBitmap)
    {
        var srcColor = SourceBitmap.ColorType;
        var srcAlpha = SourceBitmap.AlphaType;

        if (srcColor == SKColorType.Bgra8888) return SourceBitmap;
        // Ensure we don't need to normalize it.

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
        public bool Crosshatch = false;
        public bool DiagCrosshatch = false;
        public decimal HorizontalLines = 0;
        public bool Invert = false;
        public byte maxThreshold = 127;
        public byte minThreshold = 0;
        public bool Outline = false;
        public bool OutlineSharp = false;
        public decimal VerticalLines = 0;
    }

    public class Pattern
    {
        public int Height = 2;
        public string Pat = "0 0\n0 0";
        public int Width = 2;
    }
}