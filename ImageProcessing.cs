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

        public unsafe static SKBitmap Process(SKBitmap SourceBitmap, Filters FilterSettings)
        {
            if (MemPressure > 0) GC.RemoveMemoryPressure(MemPressure);
            MemPressure = SourceBitmap.BytesPerPixel * SourceBitmap.Width * SourceBitmap.Height;

            // Create an Output Bitmap
            SKBitmap OutputBitmap = SourceBitmap.Copy();

            // Get Memory Pointers
            byte* srcPtr = (byte*)SourceBitmap.GetPixels().ToPointer();
            byte* dstPtr = (byte*)OutputBitmap.GetPixels().ToPointer();

            int width = OutputBitmap.Width;
            int height = OutputBitmap.Height;

            // FILTER: Threshold
            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    byte redByte = *srcPtr++;
                    byte greenByte = *srcPtr++;
                    byte blueByte = *srcPtr++;
                    byte alphaByte = *srcPtr++;
                    
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
