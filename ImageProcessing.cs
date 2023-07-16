using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Autodraw
{
    public static class ImageProcessing
    {
        private static long MemPressure = 0;

        public class Filters
        {
            public byte AlphaThreshold = 127;
            public byte Threshold = 127;
            public bool Invert = true;
        }

        public unsafe static SKBitmap fixBitmap(SKBitmap bitmap)
        {
            if (bitmap.Info.ColorType == SKColorType.Bgra8888) return bitmap;
            SKBitmap returnBitmap = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

            bitmap.CopyTo(returnBitmap);

            return returnBitmap;
    }

        public unsafe static SKBitmap Process(SKBitmap SourceBitmap, Filters FilterSettings)
        {
            if (MemPressure > 0) GC.RemoveMemoryPressure(MemPressure);
            MemPressure = SourceBitmap.BytesPerPixel * SourceBitmap.Width * SourceBitmap.Height;

            // Create an Output Bitmap
            SKBitmap OutputBitmap = new SKBitmap(SourceBitmap.Width, SourceBitmap.Height);

            // Get Memory Pointers
            byte* srcPtr = (byte*)SourceBitmap.GetPixels().ToPointer();
            byte* dstPtr = (byte*)OutputBitmap.GetPixels().ToPointer();

            int width = OutputBitmap.Width;
            int height = OutputBitmap.Height;

            SKColorType type = SourceBitmap.ColorType;

            // FILTER: Threshold
            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    byte redByte = 0;
                    byte greenByte = 0;
                    byte blueByte = 0;
                    byte alphaByte = 0;
                    if (type != SKColorType.Gray8)
                    {
                        redByte = *srcPtr++;
                        greenByte = *srcPtr++;
                        blueByte = *srcPtr++;
                        alphaByte = *srcPtr++;
                    }
                    else
                    {
                        byte grayByte = *srcPtr++;
                        redByte = grayByte;
                        greenByte = grayByte;
                        blueByte = grayByte;
                        alphaByte = 255;
                    }
                    

                    float luminosity = ( redByte + greenByte + blueByte ) / 3;

                    byte threshColor = (byte)(luminosity > FilterSettings.Threshold || alphaByte < FilterSettings.AlphaThreshold ? 255 : 0);
                    threshColor = FilterSettings.Invert == false ? threshColor : (byte)(255 - threshColor);

                    *dstPtr++ = threshColor;
                    *dstPtr++ = threshColor;
                    *dstPtr++ = threshColor;
                    *dstPtr++ = 255;
                }
            }

            GC.AddMemoryPressure(MemPressure);

            return OutputBitmap;
        }
    }
}
