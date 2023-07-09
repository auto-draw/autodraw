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

        private static Stopwatch stopwatch = new Stopwatch();

        public class Filters
        {
            public byte AlphaThreshold = 127;
            public byte Threshold = 127;
            public bool Invert = true;
        }

        public unsafe static SKBitmap Process(SKBitmap SourceBitmap, Filters FilterSettings)
        {
            stopwatch.Restart();

            // Create an Output Bitmap
            SKBitmap OutputBitmap = SourceBitmap.Copy();

            // Get Memory Pointers
            byte* srcPtr = (byte*)SourceBitmap.GetPixels().ToPointer();
            byte* dstPtr = (byte*)OutputBitmap.GetPixels().ToPointer();

            int width = OutputBitmap.Width;
            int height = OutputBitmap.Height;

            // Get Bitmap Colortypes (Not used despite RGB vs BGR, I really should, just lazy)
            SKColorType typeOrg = SourceBitmap.ColorType;
            SKColorType typeAdj = OutputBitmap.ColorType;

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


                    byte threshColor = 0;
                    if (luminosity > FilterSettings.Threshold) {
                        threshColor = 255;
                    }

                    *dstPtr++ = threshColor;
                    *dstPtr++ = threshColor;
                    *dstPtr++ = threshColor;
                    *dstPtr++ = alphaByte;
                }
            }

            int milliseconds1 = (int)stopwatch.ElapsedMilliseconds;
            System.Diagnostics.Debug.WriteLine(milliseconds1);
            System.Diagnostics.Debug.WriteLine(stopwatch.ElapsedTicks);

            return OutputBitmap;
        }
    }
}
