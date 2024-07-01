using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SharpHook;
using SharpHook.Native;
using SkiaSharp;

namespace Autodraw;

public partial class Preview : Window
{
    public bool hasStarted = false;
    public SKBitmap? inputBitmap;
    public long lastMovement;
    public Bitmap? renderedBitmap;
    private double scale = 1;

    public Preview()
    {
        InitializeComponent();
        scale = Screens.ScreenFromWindow(this).Scaling;
        Closing += OnClosing;
        Input.MousePosUpdate += UpdateMousePosition;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        renderedBitmap.Dispose();
        Closing -= OnClosing;
        Input.MousePosUpdate -= UpdateMousePosition;
    }

    private void UpdateMousePosition(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var currUnix = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds();
            if (currUnix < lastMovement + 10) return;
            lastMovement = currUnix;
            var usedPos = Drawing.UseLastPos ? Drawing.LastPos : Input.mousePos;
            var x = usedPos.X - ((Width / 2) * scale);
            var y = usedPos.Y - ((Height / 2) * scale);
            Position = new PixelPoint((int)x, (int)y);
        });
    }

    private void Keybind(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode == Config.Keybind_StartDrawing)
        {
            if (inputBitmap.IsNull) return;
            Thread drawThread = new(async () =>
            {
                await Drawing.Draw(inputBitmap);
            });
            drawThread.Start();
            Dispatcher.UIThread.Invoke(Close);

            Input.taskHook.KeyReleased -= Keybind;
        }

        if (e.Data.KeyCode == Config.Keybind_StopDrawing)
        {
            Dispatcher.UIThread.Invoke(Close);
            Dispatcher.UIThread.Invoke(() =>
            {
                if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.MainWindow.WindowState = WindowState.Normal;
                    desktop.MainWindow.BringIntoView();
                }
            });
            Input.taskHook.KeyReleased -= Keybind;
        }

        if (e.Data.KeyCode == Config.Keybind_LockPreview)
        {
            if (Drawing.LastPos.X == 0 && Drawing.LastPos.Y == 0) Drawing.LastPos = Input.mousePos;
            Config.SetEntry("Preview_LastLockedX", Drawing.LastPos.X.ToString());
            Config.SetEntry("Preview_LastLockedY", Drawing.LastPos.Y.ToString());
            Drawing.UseLastPos = !Drawing.UseLastPos;
        }

        if (e.Data.KeyCode == Config.Keybind_ClearLock)
        {
            Drawing.LastPos = new Vector2(0,0);
            Config.SetEntry("Preview_LastLockedX", "0");
            Config.SetEntry("Preview_LastLockedY", "0");
            Drawing.UseLastPos = false;
        }
    }

    public void ReadyDraw(SKBitmap bitmap)
    {
        renderedBitmap?.Dispose();
        renderedBitmap = bitmap.ConvertToAvaloniaBitmap();
        PreviewImage.Source = renderedBitmap;

        Width = (bitmap.Width) / scale;
        Height = (bitmap.Height) / scale;

        Show();

        inputBitmap = bitmap;
        Input.taskHook.KeyReleased += Keybind;
    }
}