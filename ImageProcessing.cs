using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media;
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
    
    private static byte GetThreshold(float luminosity, byte alphaByte, Filters filterSettings)
    {
        byte threshByte =
            (byte)(luminosity > filterSettings.MaxThreshold || luminosity < filterSettings.MinThreshold || alphaByte < filterSettings.AlphaThreshold
                ? 255
                : 0); // Thresholds Filter
        return filterSettings.Invert == false ? threshByte : (byte)(255 - threshByte); // Invert
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
            if (filterSettings.DiagCrosshatch) // Diag Crosshatch
                foreach (var patPoint in listDiagCross)
                    if (x % patternDiagCross.Width == patPoint[0] && y % patternDiagCross.Height == patPoint[1])
                        returnByte = 0;
            if (filterSettings.Crosshatch) // Crosshatch
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
        // Could move this to SkiaSharp SKPaint stuff but nahhh.
        
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
        Process_MemPressure = sourceBitmap.BytesPerPixel * sourceBitmap.Width * sourceBitmap.Height;
        var height = sourceBitmap.Height;
        var width = sourceBitmap.Width;
        
        SKBitmap outputBitmap = new(width, height);
        var basePtr = (uint*)sourceBitmap.GetPixels().ToPointer();
        var returnPtr = (uint*)outputBitmap.GetPixels().ToPointer();

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
            if (filterSettings.Outline)
            {
                returnByte = GetOutlineAlpha(y, x, basePtr, width, height, threshByte, filterSettings);
            }
        
            if (filterSettings.IsAnyPatternSet())
            {
                returnByte = GetPatternAlpha(y, x, returnByte, filterSettings);
            }
            else if (!filterSettings.Outline)
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
                secondarySurface.Canvas.DrawImage(InvertImage(secondarySurface.Snapshot()),0,0);
                secondarySurface.Canvas.DrawImage(InvertImage(OutImage),0,0,lightenPaint);
                primarySurface.Canvas.DrawImage(secondarySurface.Snapshot(),0,0,darkenPaint);
                hasAppliedAnother = true;
            }

            if (filterSettings.InlineBorderAdvanced > 0)
            {
                using SKSurface secondarySurface = SKSurface.Create(imageInfo);
                secondarySurface.Canvas.DrawImage(OutImage, 0, 0);
                secondarySurface.Canvas.DrawImage(SKImage.FromBitmap(GenerateBorder(filterSettings.InlineBorderAdvanced, width, height)),0,0,lightenPaint);
                primarySurface.Canvas.DrawImage(secondarySurface.Snapshot(),0,0,darkenPaint);
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
                
                primarySurface.Canvas.DrawImage(secondarySurface.Snapshot(),0,0,darkenPaint);
                hasAppliedAnother = true;
            }

            if (filterSettings.BorderAdvanced > 0)
            {
                if(!hasAppliedAnother)
                {
                    primarySurface.Canvas.DrawImage(OutImage,0,0);
                }
                primarySurface.Canvas.DrawImage(SKImage.FromBitmap(GenerateBorder(filterSettings.BorderAdvanced, width, height)),0,0,darkenPaint);
            }

            SKImage MergeImage = primarySurface.Snapshot();
            OutImage = MergeImage;
        }
        
        outputBitmap = SKBitmap.FromImage(OutImage);
        
        GC.AddMemoryPressure(Process_MemPressure);
        return outputBitmap;
    }

    public static unsafe Color GetColor(SKBitmap sourceBitmap)
    {
        // Assume its Bgra8888
        var width = sourceBitmap.Width;
        var height = sourceBitmap.Height;
        // I've always found it so weird how specifying this as a variable, instead of just chucking it into a for loop, improves performance so much.
        // Guess the width/height variable could change in the middle of the function technically, so it has to re-read. Silly tho.
        var srcPtr = (byte*)sourceBitmap.GetPixels().ToPointer();
        
        for (var row = 0; row < height; row++)
        for (var col = 0; col < width; col++)
        {
            var b = *srcPtr++;
            var g = *srcPtr++;
            var r = *srcPtr++;
            var a = *srcPtr++;

            if (a == 255) // Look for only opaque pixels, transparent pixels sometimes are black or pink?! No clue why pink, but it happens often.
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
}