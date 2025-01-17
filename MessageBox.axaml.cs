using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
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

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.GetPosition(this).Y <= 20)
            BeginMoveDrag(e);
    }
    
    private void CloseAppButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}