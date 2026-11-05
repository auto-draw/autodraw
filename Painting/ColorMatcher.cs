using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Autodraw.Painting;

public enum ColorSpace { Rgb, Lab }

/// <summary>
/// Single source of truth for perceptual color difference in AutoDraw.
/// All palette extraction, layer assignment, and background-removal code should route through
/// this class so that every part of the pipeline agrees on what "close in color" means.
/// </summary>
public static class ColorMatcher
{
    public static int ClosestIndex(SKColor c, IReadOnlyList<SKColor> palette, ColorSpace space)
    {
        if (palette.Count == 0)
            throw new ArgumentException("Palette must not be empty.", nameof(palette));

        int best = 0;
        double bestDist = double.MaxValue;
        for (int i = 0; i < palette.Count; i++)
        {
            double d = space == ColorSpace.Lab
                ? DeltaE2000(c, palette[i])
                : RgbDistance(c, palette[i]);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    /// <summary>Full CIE ΔE2000 color difference between two sRGB colors.</summary>
    public static double DeltaE2000(SKColor a, SKColor b)
    {
        ToLab(a, out double L1, out double a1, out double b1);
        ToLab(b, out double L2, out double a2, out double b2);
        return DeltaE2000Lab(L1, a1, b1, L2, a2, b2);
    }

    /// <summary>
    /// CIE ΔE2000 between two pre-converted CIELAB triples.  Exposed separately so callers that
    /// iterate over many colors can convert the source pixel to Lab exactly once.
    /// </summary>
    public static double DeltaE2000Lab(double L1, double a1, double b1, double L2, double a2, double b2)
    {
        const double kL = 1.0, kC = 1.0, kH = 1.0;
        const double deg2rad = Math.PI / 180.0;
        const double pow25_7 = 6103515625.0; // 25^7

        double C1 = Math.Sqrt(a1 * a1 + b1 * b1);
        double C2 = Math.Sqrt(a2 * a2 + b2 * b2);
        double Cbar = (C1 + C2) / 2.0;
        double Cbar7 = Math.Pow(Cbar, 7);
        double G = 0.5 * (1.0 - Math.Sqrt(Cbar7 / (Cbar7 + pow25_7)));

        double a1p = (1 + G) * a1;
        double a2p = (1 + G) * a2;
        double C1p = Math.Sqrt(a1p * a1p + b1 * b1);
        double C2p = Math.Sqrt(a2p * a2p + b2 * b2);

        double h1p = Math.Atan2(b1, a1p) / deg2rad;
        if (h1p < 0) h1p += 360.0;
        double h2p = Math.Atan2(b2, a2p) / deg2rad;
        if (h2p < 0) h2p += 360.0;

        double dLp = L2 - L1;
        double dCp = C2p - C1p;

        double dhp;
        if (C1p * C2p == 0)
            dhp = 0;
        else if (Math.Abs(h2p - h1p) <= 180.0)
            dhp = h2p - h1p;
        else if (h2p - h1p > 180.0)
            dhp = h2p - h1p - 360.0;
        else
            dhp = h2p - h1p + 360.0;

        double dHp = 2.0 * Math.Sqrt(C1p * C2p) * Math.Sin(dhp * deg2rad / 2.0);

        double Lbarp = (L1 + L2) / 2.0;
        double Cbarp = (C1p + C2p) / 2.0;

        double hbarp;
        if (C1p * C2p == 0)
            hbarp = h1p + h2p;
        else if (Math.Abs(h1p - h2p) <= 180.0)
            hbarp = (h1p + h2p) / 2.0;
        else if (h1p + h2p < 360.0)
            hbarp = (h1p + h2p + 360.0) / 2.0;
        else
            hbarp = (h1p + h2p - 360.0) / 2.0;

        double T = 1.0
            - 0.17 * Math.Cos((hbarp - 30.0) * deg2rad)
            + 0.24 * Math.Cos(2.0 * hbarp * deg2rad)
            + 0.32 * Math.Cos((3.0 * hbarp + 6.0) * deg2rad)
            - 0.20 * Math.Cos((4.0 * hbarp - 63.0) * deg2rad);

        double dTheta = 30.0 * Math.Exp(-Math.Pow((hbarp - 275.0) / 25.0, 2.0));
        double Cbarp7 = Math.Pow(Cbarp, 7);
        double RC = 2.0 * Math.Sqrt(Cbarp7 / (Cbarp7 + pow25_7));
        double SL = 1.0 + (0.015 * Math.Pow(Lbarp - 50.0, 2.0)) / Math.Sqrt(20.0 + Math.Pow(Lbarp - 50.0, 2.0));
        double SC = 1.0 + 0.045 * Cbarp;
        double SH = 1.0 + 0.015 * Cbarp * T;
        double RT = -Math.Sin(2.0 * dTheta * deg2rad) * RC;

        double dLterm = dLp / (kL * SL);
        double dCterm = dCp / (kC * SC);
        double dHterm = dHp / (kH * SH);

        return Math.Sqrt(dLterm * dLterm + dCterm * dCterm + dHterm * dHterm + RT * dCterm * dHterm);
    }

    private static double RgbDistance(SKColor a, SKColor b)
    {
        int dr = a.Red - b.Red, dg = a.Green - b.Green, dbl = a.Blue - b.Blue;
        return Math.Sqrt(dr * dr + dg * dg + dbl * dbl);
    }

    public static void ToLab(SKColor c, out double L, out double a, out double b)
    {
        double r = PivotRgb(c.Red / 255.0);
        double g = PivotRgb(c.Green / 255.0);
        double bl = PivotRgb(c.Blue / 255.0);

        double x = PivotXyz((r * 0.4124564 + g * 0.3575761 + bl * 0.1804375) / 0.95047);
        double y = PivotXyz(r * 0.2126729 + g * 0.7151522 + bl * 0.0721750);
        double z = PivotXyz((r * 0.0193339 + g * 0.1191920 + bl * 0.9503041) / 1.08883);

        L = 116.0 * y - 16.0;
        a = 500.0 * (x - y);
        b = 200.0 * (y - z);
    }

    private static double PivotRgb(double n)
        => n > 0.04045 ? Math.Pow((n + 0.055) / 1.055, 2.4) : n / 12.92;

    private static double PivotXyz(double n)
        => n > 0.008856 ? Math.Cbrt(n) : 7.787 * n + 16.0 / 116.0;
}
