using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Platform;
using FFmpeg.AutoGen;
using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Autodraw
{
    public static class ImageProcessing
    {
        private static long Process_MemPressure = 0;


        public class Filters
        {
            public byte AlphaThreshold = 127;
            public byte Threshold = 127;
            public bool Invert = false;
            public int AntiOutline = 0;
            public int Outline = 0;
            public bool Crosshatch = false;
            public bool DiagCrosshatch = false;
            public decimal HorizontalLines = 0;
            public decimal VerticalLines = 0;
        }

        public static unsafe List<int[]> ReadPattern(SKBitmap bmp)
        {
            uint* basePtr = (uint*)bmp.GetPixels().ToPointer();

            List<int[]> positions = new List<int[]>();

            for (int x = 0; x < bmp.Width; x++)
            {
                for (int y = 0; y < bmp.Height; y++)
                {
                    uint* srcPtr = basePtr + bmp.Width * y + x;

                    byte pixel = (byte)(*srcPtr & 0xFF);
                    if (pixel == 0)
                    {
                        positions.Add(new int[] {x, y});
                    }
                }
            }

            return positions;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint MakePixel(byte red, byte green, byte blue, byte alpha) =>
                (uint)((alpha << 24) | (blue << 16) | (green << 8) | red);
        private unsafe static void GetPixel(uint* input, out byte red, out byte green, out byte blue, out byte alpha)
        {
            red = (byte)(*input & 0xFF);
            green = (byte)((*input >> 8) & 0xFF);
            blue = (byte)((*input >> 16) & 0xFF);
            alpha = (byte)((*input >> 24) & 0xFF);
        }

        public unsafe static SKBitmap Process(SKBitmap SourceBitmap, Filters FilterSettings)
        {
            if (Process_MemPressure > 0) GC.RemoveMemoryPressure(Process_MemPressure);
            Process_MemPressure = SourceBitmap.BytesPerPixel * SourceBitmap.Width * SourceBitmap.Height;

            // Create an Output Bitmap
            SKBitmap OutputBitmap = new(SourceBitmap.Width, SourceBitmap.Height);

            uint* basePtr = (uint*)SourceBitmap.GetPixels().ToPointer();
            uint* returnPtr = (uint*)OutputBitmap.GetPixels().ToPointer();

            int width = OutputBitmap.Width;
            int height = OutputBitmap.Height;

            byte thresh = FilterSettings.Threshold;
            byte athresh = FilterSettings.AlphaThreshold;

            bool doinvert = FilterSettings.Invert;
            int antioutline = FilterSettings.AntiOutline;
            int outline = FilterSettings.Outline;

            bool crosshatch = FilterSettings.Crosshatch;
            bool diagcross = FilterSettings.DiagCrosshatch;
            decimal horizontals = FilterSettings.HorizontalLines;
            decimal verticals = FilterSettings.VerticalLines;
            
            var invert = new float[20] {
                -1f, 0f,  0f,  0f,  1f,
                0f,  -1f, 0f,  0f,  1f,
                0f,  0f,  -1f, 0f,  1f,
                0f,  0f,  0f,  1f,  0f
            };

            var greyscale = new float[]
            {
                0.21f, 0.72f, 0.07f, 0, 0,
                0.21f, 0.72f, 0.07f, 0, 0,
                0.21f, 0.72f, 0.07f, 0, 0,
                0,     0,     0,     1, 0
            };


            for (int y = 0; y < height; y++){
                for (int x = 0; x < width; x++)
                {
                    uint* srcPtr = basePtr + width * y + x;

                    // Performance is better when embedded directly, refer to function GetPixel()
                    byte red = (byte)(*srcPtr & 0xFF);
                    byte green = (byte)((*srcPtr >> 8) & 0xFF);
                    byte blue = (byte)((*srcPtr >> 16) & 0xFF);
                    byte alpha = (byte)((*srcPtr >> 24) & 0xFF);

                    byte outputByte = (byte)((float)red / 3 + (float)green / 3 + (float)blue / 3);

                    outputByte = outputByte > thresh ? (byte)255 : (byte)0;
                    outputByte = alpha < athresh ? (byte)255 : outputByte;

                    // Tried exclusive OR operator, but it shows worse performance.
                    if (doinvert) { outputByte = outputByte == 255 ? (byte)0 : (byte)255; }

                    *returnPtr++ = (uint)((255 << 24) | (outputByte << 16) | (outputByte << 8) | outputByte);
                }
            }

            SKImage OutImage = SKImage.FromBitmap(OutputBitmap);
            // TODO: Move entire for loop process to graphics here for performance boosts
            //       ↓↓↓↓↓↓↓↓↓↓↓↓↓↓

            //OutImage = OutImage.ApplyImageFilter(SKImageFilter.CreateColorFilter(SKColorFilter.CreateColorMatrix(greyscale)), new SKRectI(0, 0, width, height), new SKRectI(0, 0, width, height), out _, out SKPoint _);


            if (antioutline > 0)
            {
                SKImage OutImageAnti = OutImage.ApplyImageFilter(SKImageFilter.CreateDilate(antioutline, antioutline), new SKRectI(0, 0, width, height), new SKRectI(antioutline, antioutline, width - antioutline, height - antioutline), out _, out SKPoint _);
                OutImage = OutImageAnti;
            }
            if (outline > 0)
            {
                SKImage OutImageAnti = OutImage.ApplyImageFilter(SKImageFilter.CreateDilate(outline, outline), new SKRectI(0, 0, width, height), new SKRectI(0, 0, width - outline, height - outline), out _, out SKPoint _);
                OutImageAnti = OutImageAnti.ApplyImageFilter(SKImageFilter.CreateColorFilter(SKColorFilter.CreateColorMatrix(invert)), new SKRectI(0, 0, width, height), new SKRectI(0, 0, width, height), out _, out SKPoint _);
                SKImage MergeImage = OutImage.ApplyImageFilter(SKImageFilter.CreateBlendMode(SKBlendMode.Lighten, SKImageFilter.CreateImage(OutImageAnti)), new SKRectI(0, 0, width, height), new SKRectI(0, 0, width, height), out _, out SKPoint _);
                OutImage = MergeImage;
            }

            OutputBitmap = SKBitmap.FromImage(OutImage);

            // TODO: Move this to Graphics as a repeating bitmap thing or whatever its called.
            returnPtr = (uint*)OutputBitmap.GetPixels().ToPointer();
            if ((horizontals > 1 || verticals > 1) && outline <= 0)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        uint* srcPtr = returnPtr + width * y + x;

                        byte outputByte = (byte)(*srcPtr & 0xFF);

                        if (outputByte == 0)
                        {
                            outputByte = 255;
                            if (horizontals > 1)
                            {
                                if (y % horizontals == 1)
                                {
                                    outputByte = 0;
                                }
                            }
                            if (verticals > 1)
                            {
                                if (x % verticals == 1)
                                {
                                    outputByte = 0;
                                }
                            }
                        }
                        *srcPtr = (uint)((255 << 24) | (outputByte << 16) | (outputByte << 8) | outputByte);
                    }
                }
            }

            GC.AddMemoryPressure(Process_MemPressure);

            return OutputBitmap;
        }

        public unsafe static SKBitmap NormalizeColor(this SKBitmap SourceBitmap)
        {
            SKColorType srcColor = SourceBitmap.ColorType;
            SKAlphaType srcAlpha = SourceBitmap.AlphaType;

            if (srcColor == SKColorType.Bgra8888) return SourceBitmap;
            // Ensure we don't need to normalize it.

            SKBitmap OutputBitmap = new(SourceBitmap.Width, SourceBitmap.Height);

            byte* srcPtr = (byte*)SourceBitmap.GetPixels().ToPointer();
            byte* dstPtr = (byte*)OutputBitmap.GetPixels().ToPointer();

            int width = OutputBitmap.Width;
            int height = OutputBitmap.Height;

            SKColorType outColor = OutputBitmap.ColorType;

            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    if (srcColor == SKColorType.Gray8 || srcColor == SKColorType.Alpha8)
                    {
                        byte b = *srcPtr++;
                        *dstPtr++ = b;
                        *dstPtr++ = b;
                        *dstPtr++ = b;
                        *dstPtr++ = 255;
                    }
                    else if(srcColor == SKColorType.Rgba8888)
                    {
                        byte r = *srcPtr++;
                        byte g = *srcPtr++;
                        byte b = *srcPtr++;
                        byte a = *srcPtr++;
                        *dstPtr++ = b;
                        *dstPtr++ = g;
                        *dstPtr++ = r;
                        *dstPtr++ = a;
                    }
                    else if (srcColor == SKColorType.Argb4444)
                    {
                        byte r = *srcPtr++;
                        byte g = *srcPtr++;
                        byte b = *srcPtr++;
                        byte a = *srcPtr++;
                        *dstPtr++ = (byte)(b * 2);
                        *dstPtr++ = (byte)(g * 2);
                        *dstPtr++ = (byte)(r * 2);
                        *dstPtr++ = (byte)(a * 2);
                    }
                }
            }
            SourceBitmap.Dispose();
            SourceBitmap = OutputBitmap;

            return SourceBitmap;
        }
    }
}
