using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using RestSharp;
using RestSharp.Authenticators.OAuth2;

namespace Autodraw;

public partial class OpenAIPrompt : Window
{
    public static OpenAIPrompt? current;
    
    public OpenAIPrompt()
    {
        InitializeComponent();

        if (Config.GetEntry("OpenAIKey") is null)
        {
            Warning1.Opacity = 1;
            Warning2.Opacity = 1;
        }
        else
        {
            Warning1.Opacity = 0;
            Warning2.Opacity = 0;
        }
        
        Model.SelectedIndex = 0;
        Resolution.Items.Clear();
        Resolution.Items.Add("1024x1024");
        Resolution.Items.Add("512x512");
        Resolution.Items.Add("256x256");
        Resolution.SelectedIndex = 0;
        
        Model.SelectionChanged += ModelOnSelectionChanged;

        CloseAppButton.Click += QuitAppOnClick;
        Generate.Click += GenerateOnClick;
    }

    private void ModelOnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var rawModel = ((ComboBoxItem)this.Model.SelectedItem).Content.ToString();
        var Model = rawModel == "DALL-E 2" ? "dall-e-2" : "dall-e-3";
        
        // For some reason the traditional way, Resolution.Items = new List<string> {} doesn't work. Really annoying.
        if (Model == "dall-e-2")
        {
            Resolution.Items.Clear();
            Resolution.Items.Add("1024x1024");
            Resolution.Items.Add("512x512");
            Resolution.Items.Add("256x256");
            Resolution.SelectedIndex = 0;
        }
        else
        {
            Resolution.Items.Clear();
            Resolution.Items.Add("1024x1024");
            Resolution.Items.Add("1024x1792");
            Resolution.Items.Add("1792x1024");
            Resolution.SelectedIndex = 0;
        }
    }

    private void GenerateOnClick(object? sender, RoutedEventArgs e)
    {
        var OpenAIKey = Config.GetEntry("OpenAIKey");
        if (OpenAIKey is null)
        {
            new MessageBox().ShowMessageBox("Error!", "You have not set up an API key!", "error");
            return;
        }

        var options = new RestClientOptions("https://api.openai.com/v1/images/generations")
        {
            Authenticator = new OAuth2AuthorizationRequestHeaderAuthenticator(OpenAIKey, "Bearer")
        };

        Generate.IsEnabled = false;
        Generate.Content = "Generating...";

        var client = new RestClient(options);

        var request = new RestRequest
        {
            Method = Method.Post,
            RequestFormat = DataFormat.Json
        };

        var rawModel = ((ComboBoxItem)this.Model.SelectedItem).Content.ToString();
        var model = rawModel == "DALL-E 2" ? "dall-e-2" : "dall-e-3";
        var size = Resolution.SelectedItem.ToString();
        var quality = rawModel.EndsWith(" HD") ? "hd" : "standard";
        
        var param = new
        {
            prompt = Prompt.Text,
            model = model,
            size = size,
            n = 1,
            quality = quality
        };
        Task.Run(async () =>
        {
            try
            {
                request.AddJsonBody(param);

                var jsonResponse = JObject.Parse(client.Execute(request).Content);
                if (jsonResponse["error"] is not null)
                {
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        new MessageBox().ShowMessageBox($"Error! ({jsonResponse["error"]["type"]})",
                            jsonResponse["error"]["message"].ToString(), "warn");
                        Generate.IsEnabled = true;
                        Generate.Content = "Generate";
                    });
                    return;
                }

                var url = jsonResponse["data"][0]["url"].ToString();

                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                Dispatcher.UIThread.Invoke(new Action (async() =>
                {
                    Generate.IsEnabled = true;
                    Generate.Content = "Generate";
                    MainWindow.CurrentMainWindow.ImportImage("", await response.Content.ReadAsByteArrayAsync());
                }));
            }
            catch
            {
                Generate.IsEnabled = true;
                Generate.Content = "Generate";
                Utils.Log("Error occured within the AI Generation");
            }
        });
    }
    
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.GetPosition(this).Y <= 20)
            BeginMoveDrag(e);
    }

    private void QuitAppOnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}