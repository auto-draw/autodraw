using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SharpHook;
using System;

namespace Autodraw;

public partial class Preview : Window
{
    void Keybind(object? sender, KeyboardHookEventArgs e)
    {

    }

    public Preview()
    {
        InitializeComponent();
        Input.mousePosUpdate += (object? sender, EventArgs e) =>
        {
            Position = new PixelPoint((int)Input.mousePos.X, (int)Input.mousePos.Y);
        };

        Input.taskHook.KeyReleased += Keybind;
    }
}