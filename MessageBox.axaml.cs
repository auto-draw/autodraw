using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;

using SharpAudio;
using SharpAudio.Codec;
using System.Runtime.InteropServices;

namespace Autodraw;

public partial class MessageBox : Window
{
    public void ShowMessageBox(string title, string description, string icon = "info", string sound = "alert.wav")
    {
        Bitmap bmp = new(AssetLoader.Open(new Uri($"avares://Autodraw/Assets/Message/{icon}.png")));
        MessageIcon.Source = bmp;
        MessageTitle.Text = title;
        MessageContent.Text = description;

        Audio.PlaySound($"avares://Autodraw/Assets/Sounds/{sound}");

        Show();
    }

    public MessageBox()
    {
        InitializeComponent();
        CloseAppButton.Click += CloseAppButton_Click;
    }

    private void CloseAppButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}