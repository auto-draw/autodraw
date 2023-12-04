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
        var OpenAIKey = Config.getEntry("OpenAIKey");
        if (OpenAIKey == null)
        {
            new MessageBox().ShowMessageBox("Error!", "You have not set up an API key!", "error");
            return;
        }

        var options = new RestClientOptions("https://api.openai.com/v1/images/generations")
        {
            Authenticator = new OAuth2AuthorizationRequestHeaderAuthenticator(OpenAIKey, "Bearer")
        };

        GenerateImage.IsEnabled = false;

        var client = new RestClient(options);

        var request = new RestRequest
        {
            Method = Method.Post,
            RequestFormat = DataFormat.Json
        };

        var param = new
        {
            prompt = AIPrompt.Text,
            model = AIModel.Text,
            size = AISize.Text,
            n = 1,
            quality = "hd"
        };
        Task.Run(async () =>
        {
            try
            {
                request.AddJsonBody(param);
                Debug.WriteLine(param);

                var jsonResponse = JObject.Parse(client.Execute(request).Content);
                if (jsonResponse["error"] is not null)
                {
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        new MessageBox().ShowMessageBox($"Error! ({jsonResponse["error"]["type"]})",
                            jsonResponse["error"]["message"].ToString(), "warn");
                    });
                    Utils.Log("Error with Prompt: " + jsonResponse["error"]);
                    GenerateImage.IsEnabled = true;
                    return;
                }

                var URL = jsonResponse["data"][0]["url"].ToString();

                using (var httpClient = new HttpClient())
                {
                    var response = await httpClient.GetAsync(URL);
                    response.EnsureSuccessStatusCode();

                    Dispatcher.UIThread.Invoke(new Action(async () =>
                    {
                        GenerateImage.IsEnabled = true;
                        MainWindow.CurrentMainWindow.ImportImage("", await response.Content.ReadAsByteArrayAsync());
                    }));
                }
            }
            catch
            {
                GenerateImage.IsEnabled = true;
                Utils.Log("Error occured within the AI Debug");
            }
        });
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