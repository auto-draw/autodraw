using Avalonia.Input;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Autodraw
{
    // Most of this code is going to implement the same methods used for previous versions.
    // The reason I wish to do this, is because simply put, I cannot viably see another way of doing this that is more or less efficient.
    // The previous code was messy, but it's implementation for drawing was more or less, efficient and simple
    // I'm not here to over-engineer something if there's already a very stable method, I don't want to make anything unstable.

    public static class Drawing
    {
        // Variables

        private static int[,]? pixelArray;

        // Functions

        private static unsafe void Scan(SKBitmap bitmap)
        {
            pixelArray = new int[bitmap.Width, bitmap.Height];
            byte* bitPtr = (byte*)bitmap.GetPixels().ToPointer();


            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    byte redByte = *bitPtr++;
                    byte greenByte = *bitPtr++;
                    byte blueByte = *bitPtr++;
                    byte alphaByte = *bitPtr++;

                    pixelArray[x, y] = redByte == 255 ? 1 : 0;
                }
            }
            return;
        }

        private static void Draw(SKBitmap bitmap)
        {
            Scan(bitmap);

        }
    }
}