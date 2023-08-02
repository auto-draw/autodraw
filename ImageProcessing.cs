using Avalonia.Controls.Shapes;
using FFmpeg.AutoGen;
using HarfBuzzSharp;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Autodraw
{
    public static class ImageProcessing
    {
        private static long Process_MemPressure = 0;

        private static Pattern patternCrosshatch = new Pattern() { 
            Pat = "0 0 0 1 0 0 0\n0 0 0 1 0 0 0\n0 0 0 1 0 0 0\n1 1 1 1 1 1 1\n0 0 0 1 0 0 0\n0 0 0 1 0 0 0\n0 0 0 1 0 0 0\n",
            Width = 7, Height = 7
        };

        private static Pattern patternDiagCross  = new Pattern() {
            Pat = "1 0 0 0 0 0 1\n0 1 0 0 0 1 0\n0 0 1 0 1 0 0\n0 0 0 1 0 0 0\n0 0 1 0 1 0 0\n0 1 0 0 0 1 0\n1 0 0 0 0 0 1\n",
            Width = 6,
            Height = 6
        };

        private static List<int[]> listCrosshatch = readPattern(patternCrosshatch.Pat);
        private static List<int[]> listDiagCross  = readPattern(patternDiagCross.Pat);

        public class Filters
        {
            public byte AlphaThreshold = 127;
            public byte Threshold = 127;
            public bool Invert = false;
            public bool Outline = false;
            public bool OutlineSharp = false;
            public bool Crosshatch = false;
            public bool DiagCrosshatch = false;
        }
        public class Pattern
        {
            public int Width = 2;
            public int Height = 2;
            public string Pat = "0 0\n0 0";
        }
        public static List<int[]> readPattern(string pat)
        {
            string[] lines = pat.Split('\n');

            List<int[]> positions = new List<int[]>();

            for (int i = 0; i < lines.Length; i++)
            {
                string[] numbers = lines[i].Split(' ');

                for (int j = 0; j < numbers.Length; j++)
                {
                    if (numbers[j] == "1")
                    {
                        positions.Add(new int[] { j, i  });
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
            SKBitmap OutputBitmap = new SKBitmap(SourceBitmap.Width, SourceBitmap.Height);

            uint* basePtr = (uint*)SourceBitmap.GetPixels().ToPointer();
            uint* returnPtr = (uint*)OutputBitmap.GetPixels().ToPointer();

            int width = OutputBitmap.Width;
            int height = OutputBitmap.Height;

            byte thresh = FilterSettings.Threshold;
            byte athresh = FilterSettings.AlphaThreshold;

            bool doinvert = FilterSettings.Invert;
            bool outline = FilterSettings.Outline;
            bool sharpoutline = FilterSettings.OutlineSharp;

            bool crosshatch = FilterSettings.Crosshatch;
            bool diagcross = FilterSettings.DiagCrosshatch;

            for (int y = 0; y < height; y++){
                for (int x = 0; x < width; x++)
                {
                    uint* srcPtr = basePtr + width * y + x;

                    byte redByte, greenByte, blueByte, alphaByte;
                    GetPixel(srcPtr, out redByte, out greenByte, out blueByte, out alphaByte);

                    float luminosity = (redByte + greenByte + blueByte) / 3;

                    byte threshByte = (byte)(luminosity > thresh || alphaByte < athresh ? 255 : 0); // Thresholds Filter

                    threshByte = doinvert == false ? threshByte : (byte)(255 - threshByte); // Invert

                    if (threshByte == 255)
                    {
                        *returnPtr++ = 0xffffffff;
                        continue;
                    }

                    byte returnByte = 255;

                    if (outline)
                    {
                        bool doOutline = false;
                        foreach (int i in Enumerable.Range(0, 8))
                        {
                            switch (i)
                            {
                                case 0:
                                    byte rByte, gByte, bByte, aByte;
                                    float lumen;
                                    byte localThresh;
                                    if (x - 1 < 0 || y - 1 < 0) { doOutline = true; break; };
                                    GetPixel(basePtr + width * (y - 1) + (x - 1), out rByte, out gByte, out bByte, out aByte);
                                    lumen = (rByte + gByte + bByte) / 3;
                                    localThresh = (byte)(lumen > thresh || aByte < athresh ? 255 : 0);
                                    localThresh = doinvert == false ? localThresh : (byte)(255 - localThresh);
                                    if (localThresh == 255) { doOutline = true; }
                                    break;
                                case 1:
                                    if (y - 1 < 0) { doOutline = true; break; };
                                    GetPixel(basePtr + width * (y - 1) + x, out rByte, out gByte, out bByte, out aByte);
                                    lumen = (rByte + gByte + bByte) / 3;
                                    localThresh = (byte)(lumen > thresh || aByte < athresh ? 255 : 0);
                                    localThresh = doinvert == false ? localThresh : (byte)(255 - localThresh);
                                    if (localThresh == 255) { doOutline = true; }
                                    break;
                                case 2:
                                    if (x + 1 >= width || y - 1 < 0) { doOutline = true; break; };
                                    GetPixel(basePtr + width * (y - 1) + (x + 1), out rByte, out gByte, out bByte, out aByte);
                                    lumen = (rByte + gByte + bByte) / 3;
                                    localThresh = (byte)(lumen > thresh || aByte < athresh ? 255 : 0);
                                    localThresh = doinvert == false ? localThresh : (byte)(255 - localThresh);
                                    if (localThresh == 255) { doOutline = true; }
                                    break;
                                case 3:
                                    if (x - 1 < 0) { doOutline = true; break; };
                                    GetPixel(basePtr + width * y + (x - 1), out rByte, out gByte, out bByte, out aByte);
                                    lumen = (rByte + gByte + bByte) / 3;
                                    localThresh = (byte)(lumen > thresh || aByte < athresh ? 255 : 0);
                                    localThresh = doinvert == false ? localThresh : (byte)(255 - localThresh);
                                    if (localThresh == 255) { doOutline = true; }
                                    break;
                                case 4:
                                    if (x + 1 >= width) { doOutline = true; break; };
                                    GetPixel(basePtr + width * y + (x + 1), out rByte, out gByte, out bByte, out aByte);
                                    lumen = (rByte + gByte + bByte) / 3;
                                    localThresh = (byte)(lumen > thresh || aByte < athresh ? 255 : 0);
                                    localThresh = doinvert == false ? localThresh : (byte)(255 - localThresh);
                                    if (localThresh == 255) { doOutline = true; }
                                    break;
                                case 5:
                                    if (x - 1 < 0 || y + 1 >= height) { doOutline = true; break; };
                                    GetPixel(basePtr + width * (y + 1) + (x - 1), out rByte, out gByte, out bByte, out aByte);
                                    lumen = (rByte + gByte + bByte) / 3;
                                    localThresh = (byte)(lumen > thresh || aByte < athresh ? 255 : 0);
                                    localThresh = doinvert == false ? localThresh : (byte)(255 - localThresh);
                                    if (localThresh == 255) { doOutline = true; }
                                    break;
                                case 6:
                                    if (y + 1 >= height) { doOutline = true; break; };
                                    GetPixel(basePtr + width * (y + 1) + x, out rByte, out gByte, out bByte, out aByte);
                                    lumen = (rByte + gByte + bByte) / 3;
                                    localThresh = (byte)(lumen > thresh || aByte < athresh ? 255 : 0);
                                    localThresh = doinvert == false ? localThresh : (byte)(255 - localThresh);
                                    if (localThresh == 255) { doOutline = true; }
                                    break;
                                case 7:
                                    if (x + 1 >= width || y + 1 >= height) { doOutline = true; break; };
                                    GetPixel(basePtr + width * (y + 1) + (x + 1), out rByte, out gByte, out bByte, out aByte);
                                    lumen = (rByte + gByte + bByte) / 3;
                                    localThresh = (byte)(lumen > thresh || aByte < athresh ? 255 : 0);
                                    localThresh = doinvert == false ? localThresh : (byte)(255 - localThresh);
                                    if (localThresh == 255) { doOutline = true; }
                                    break;
                            }
                            if (doOutline)
                            {
                                returnByte = 0;
                                break;
                            }
                        }
                    }else if (sharpoutline)
                    {
                        bool doOutline = false;
                        foreach (int i in Enumerable.Range(0, 4))
                        {
                            switch (i)
                            {
                                case 0:
                                    byte rByte, gByte, bByte, aByte;
                                    float lumen;
                                    byte localThresh;
                                    if (y - 1 < 0) { doOutline = true; break; };
                                    GetPixel(basePtr + width * (y - 1) + x, out rByte, out gByte, out bByte, out aByte);
                                    lumen = (rByte + gByte + bByte) / 3;
                                    localThresh = (byte)(lumen > thresh || aByte < athresh ? 255 : 0);
                                    localThresh = doinvert == false ? localThresh : (byte)(255 - localThresh);
                                    if (localThresh == 255) { doOutline = true; }
                                    break;
                                case 1:
                                    if (x - 1 < 0) { doOutline = true; break; };
                                    GetPixel(basePtr + width * y + (x - 1), out rByte, out gByte, out bByte, out aByte);
                                    lumen = (rByte + gByte + bByte) / 3;
                                    localThresh = (byte)(lumen > thresh || aByte < athresh ? 255 : 0);
                                    localThresh = doinvert == false ? localThresh : (byte)(255 - localThresh);
                                    if (localThresh == 255) { doOutline = true; }
                                    break;
                                case 2:
                                    if (x + 1 >= width) { doOutline = true; break; };
                                    GetPixel(basePtr + width * y + (x + 1), out rByte, out gByte, out bByte, out aByte);
                                    lumen = (rByte + gByte + bByte) / 3;
                                    localThresh = (byte)(lumen > thresh || aByte < athresh ? 255 : 0);
                                    localThresh = doinvert == false ? localThresh : (byte)(255 - localThresh);
                                    if (localThresh == 255) { doOutline = true; }
                                    break;
                                case 3:
                                    if (y + 1 >= height) { doOutline = true; break; };
                                    GetPixel(basePtr + width * (y + 1) + x, out rByte, out gByte, out bByte, out aByte);
                                    lumen = (rByte + gByte + bByte) / 3;
                                    localThresh = (byte)(lumen > thresh || aByte < athresh ? 255 : 0);
                                    localThresh = doinvert == false ? localThresh : (byte)(255 - localThresh);
                                    if (localThresh == 255) { doOutline = true; }
                                    break;
                            }
                            if (doOutline)
                            {
                                returnByte = 0;
                            }
                        }
                    }

                    // Pattern Filters
                    if (returnByte != 0 && (crosshatch || diagcross)) {
                        
                        if (crosshatch) // Crosshatch
                        {
                            foreach (var patPoint in listCrosshatch)
                            {
                                if (x % patternCrosshatch.Width == patPoint[0] && y % patternCrosshatch.Height == patPoint[1])
                                {
                                    returnByte = 0;
                                }
                            }
                        }
                        if (diagcross)  // Diag Crosshatch
                        {
                            foreach (var patPoint in listDiagCross)
                            {
                                if (x % patternDiagCross.Width == patPoint[0] && y % patternDiagCross.Height == patPoint[1])
                                {
                                    returnByte = 0;
                                }
                            }
                        }
                    }
                    else if(!outline && !sharpoutline) returnByte = threshByte;

                    *returnPtr++ = MakePixel(returnByte, returnByte, returnByte, 255);
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

            SKBitmap OutputBitmap = new SKBitmap(SourceBitmap.Width, SourceBitmap.Height);

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
