using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Autodraw
{
    public static class ImageProcessing
    {
        public class Filters
        {
            public bool Threshold = true;
            public bool Invert = true;
        }

        public static SKBitmap ApplyFilter(SKBitmap Image, Filters reqFilters)
        {
            reqFilters.Threshold = true;
            
            return Image;
        }
    }
}
