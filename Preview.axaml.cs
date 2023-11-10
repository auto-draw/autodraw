using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SharpHook;
using SkiaSharp;
using System;
using System.Numerics;
using System.Threading;

namespace Autodraw;

public partial class Preview : Window
{
    public Bitmap? renderedBitmap;
    public SKBitmap? inputBitmap;
    public bool hasStarted = false;
    public long lastMovement = 0;

    public Preview()
    {
        InitializeComponent();
        this.Closing += OnClosing;
        Input.MousePosUpdate += UpdateMousePosition;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        renderedBitmap.Dispose();
    }

    private void UpdateMousePosition(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Invoke(new Action(() =>
        {
            long currUnix = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds();
            if(currUnix < lastMovement + 16) { return; }
            lastMovement = currUnix;
            Vector2 usedPos = Drawing.useLastPos ? Drawing.lastPos : Input.mousePos;
            double x = usedPos.X - (Width/2);
            double y = usedPos.Y - (Height/2)-20;
            Position = new PixelPoint((int)x, (int)y);
        }));
    }

    void Keybind(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode == SharpHook.Native.KeyCode.VcLeftShift || e.Data.KeyCode == SharpHook.Native.KeyCode.VcRightShift)
        {
            if (inputBitmap.IsNull) return;
            Thread drawThread = new(async () =>
            {
                await Drawing.Draw(inputBitmap);
            });
            drawThread.Start();
            Dispatcher.UIThread.Invoke(new Action(() => Close()));

            Input.taskHook.KeyReleased -= Keybind;
        }
        if (e.Data.KeyCode == SharpHook.Native.KeyCode.VcLeftAlt || e.Data.KeyCode == SharpHook.Native.KeyCode.VcRightAlt)
        {
            Dispatcher.UIThread.Invoke(new Action(() => Close() ));
            Input.taskHook.KeyReleased -= Keybind;
            Dispatcher.UIThread.Invoke(new Action(() =>
            {
                if (Avalonia.Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.MainWindow.WindowState = WindowState.Normal;
                }
            }));
        }
        if (e.Data.KeyCode == SharpHook.Native.KeyCode.VcLeftControl || e.Data.KeyCode == SharpHook.Native.KeyCode.VcRightControl)
        {
            if (Drawing.lastPos.X == 0 && Drawing.lastPos.Y == 0) Drawing.lastPos = Input.mousePos;
            Drawing.useLastPos = !Drawing.useLastPos;
        }
    }

    public void ReadyDraw(SKBitmap bitmap)
    {
        renderedBitmap?.Dispose();
        renderedBitmap = bitmap.ConvertToAvaloniaBitmap();
        previewImage.Source = renderedBitmap;

        Width = bitmap.Width;
        Height = bitmap.Height;

        Show();

        inputBitmap = bitmap;
        Input.taskHook.KeyReleased += Keybind;
    }
}