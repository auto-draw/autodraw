using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Autodraw;

public partial class MessageBox : Window
{
    public MessageBox()
    {
        InitializeComponent();
        CloseAppButton.Click += CloseAppButton_Click;
    }

    public void ShowMessageBox(string title, string description, string icon = "info", string sound = "alert.wav")
    {
        Bitmap bmp = new(AssetLoader.Open(new Uri($"avares://Autodraw/Assets/Message/{icon}.png")));
        MessageIcon.Source = bmp;
        MessageTitle.Text = title;
        MessageContent.Text = description;

        Show();
    }

    private void CloseAppButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}