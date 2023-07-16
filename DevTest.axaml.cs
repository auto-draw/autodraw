using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SkiaSharp;
using System;
using System.Diagnostics;

namespace Autodraw;

public partial class DevTest : Window
{
    public DevTest()
    {
        InitializeComponent();
        TestBenchmarking.Click += (object? sender, RoutedEventArgs e) => Benchmark();
        TestPopup.Click += TestPopup_Click;
        new MessageBox().ShowMessageBox("Drawing Finished!", "The drawing has finished! Yippee!", "nerd", "nerd.wav");
    }

    private void TestPopup_Click(object? sender, RoutedEventArgs e)
    {
        new MessageBox().ShowMessageBox("Hi", "Loser");
    }

    public unsafe static SKBitmap TestImage(int width, int height)
    {
        SKBitmap returnbtmp = new(width, height, false);

        byte* srcPtr = (byte*)returnbtmp.GetPixels().ToPointer();

        Random rng = new();

        // FILTER: Threshold
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                *srcPtr++ = (byte)rng.Next(0,255);
                *srcPtr++ = (byte)rng.Next(0, 255);
                *srcPtr++ = (byte)rng.Next(0, 255);
                *srcPtr++ = (byte)rng.Next(0, 255);
            }
        }

        return returnbtmp;
    }

    private void Benchmark()
    {
        Stopwatch sw = Stopwatch.StartNew();

        SKBitmap small = new(64, 64, false);
        sw.Restart();
        ImageProcessing.Process(small, new ImageProcessing.Filters() { Invert = true });
        long TimeTookSmall = sw.ElapsedMilliseconds;

        BenchmarkResults.Text = "Results:\n64x64: " + TimeTookSmall.ToString();

        SKBitmap avg = new(384, 384, false);
        sw.Restart();
        ImageProcessing.Process(avg, new ImageProcessing.Filters() { Invert = true });
        long TimeTookAvg = sw.ElapsedMilliseconds;

        BenchmarkResults.Text = "Results:\n64x64: " + TimeTookSmall.ToString() + "\n384x384: " + TimeTookAvg.ToString();

        SKBitmap med = new(1024, 1024, false);
        sw.Restart();
        ImageProcessing.Process(med, new ImageProcessing.Filters() { Invert = true });
        long TimeTookMed = sw.ElapsedMilliseconds;

        BenchmarkResults.Text = "Results:\n64x64: " + TimeTookSmall.ToString() + "\n384x384: " + TimeTookAvg.ToString() + "\n1024x1024: " + TimeTookMed.ToString();

        SKBitmap large = new(3072, 3072, false);
        sw.Restart();
        ImageProcessing.Process(large, new ImageProcessing.Filters() { Invert = true });
        long TimeTookLarge = sw.ElapsedMilliseconds;

        sw.Reset();

        BenchmarkResults.Text = "Results:\n64x64: "+TimeTookSmall.ToString()+"\n384x384: "+TimeTookAvg.ToString() + "\n1024x1024: " + TimeTookMed.ToString() + "\n4096x4096: " + TimeTookLarge.ToString();

        small.Dispose();
        avg.Dispose();
        med.Dispose();
        large.Dispose();
    }
}