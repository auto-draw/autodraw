using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using RestSharp;
using RestSharp.Authenticators.OAuth2;
using SkiaSharp;

namespace Autodraw;

public partial class DevTest : Window
{
    public DevTest()
    {
        InitializeComponent();
        TestBenchmarking.Click += (sender, e) => Benchmark();
        TestPopup.Click += TestPopup_Click;
        GenerateImage.Click += GenerateImage_ClickAsync;
    }

    private void GenerateImage_ClickAsync(object? sender, RoutedEventArgs e)
    {
        new MessageBox().ShowMessageBox("Depreciated!","This is depreciated, please use the context menu prompt instead.\nRight click the image box and select AI Generation");
    }

    private void TestPopup_Click(object? sender, RoutedEventArgs e)
    {
        new MessageBox().ShowMessageBox("Hi", "Loser");
    }

    public static unsafe SKBitmap TestImage(int width, int height)
    {
        SKBitmap returnbtmp = new(width, height);

        var srcPtr = (byte*)returnbtmp.GetPixels().ToPointer();

        Random rng = new();

        for (var row = 0; row < height; row++)
        for (var col = 0; col < width; col++)
        {
            *srcPtr++ = (byte)rng.Next(0, 255);
            *srcPtr++ = (byte)rng.Next(0, 255);
            *srcPtr++ = (byte)rng.Next(0, 255);
            *srcPtr++ = (byte)rng.Next(0, 255);
        }

        return returnbtmp;
    }

    private void Benchmark()
    {
        var sw = Stopwatch.StartNew();

        SKBitmap small = new(64, 64);
        sw.Restart();
        ImageProcessing.Process(small, new ImageProcessing.Filters { Invert = true });
        var TimeTookSmall = sw.ElapsedMilliseconds;

        BenchmarkResults.Text = "Results:\n64x64: " + TimeTookSmall;

        SKBitmap avg = new(384, 384);
        sw.Restart();
        ImageProcessing.Process(avg, new ImageProcessing.Filters { Invert = true });
        var TimeTookAvg = sw.ElapsedMilliseconds;

        BenchmarkResults.Text = "Results:\n64x64: " + TimeTookSmall + "\n384x384: " + TimeTookAvg;

        SKBitmap med = new(1024, 1024);
        sw.Restart();
        ImageProcessing.Process(med, new ImageProcessing.Filters { Invert = true });
        var TimeTookMed = sw.ElapsedMilliseconds;

        BenchmarkResults.Text = "Results:\n64x64: " + TimeTookSmall + "\n384x384: " + TimeTookAvg + "\n1024x1024: " +
                                TimeTookMed;

        SKBitmap large = new(3072, 3072);
        sw.Restart();
        ImageProcessing.Process(large, new ImageProcessing.Filters { Invert = true });
        var TimeTookLarge = sw.ElapsedMilliseconds;

        sw.Reset();

        BenchmarkResults.Text = "Results:\n64x64: " + TimeTookSmall + "\n384x384: " + TimeTookAvg + "\n1024x1024: " +
                                TimeTookMed + "\n4096x4096: " + TimeTookLarge;

        small.Dispose();
        avg.Dispose();
        med.Dispose();
        large.Dispose();
    }
}