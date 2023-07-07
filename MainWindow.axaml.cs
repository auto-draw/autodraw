using Avalonia.Controls;
using Avalonia.Themes.Fluent;
using SkiaSharp;
using System;
using System.IO;
using Newtonsoft.Json;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia;
using System.Drawing;
using Avalonia.Shared.PlatformSupport;

namespace Autodraw;

public partial class MainWindow : Window
{
    private Settings? _settings = null;
    public Bitmap loadedBitmap = new Bitmap(1,1);

    public MainWindow()
    {
        InitializeComponent();
        Config.init();
        CloseAppButton.Click += quitApp;
        MinimizeAppButton.Click += minimizeApp;
    }

    // Window Opening Handles

    private void openSettings(object sender, RoutedEventArgs e)
    {
        if (_settings == null) { _settings = new Settings(); } else
        {
            if (!_settings.IsActive)
            {
                Close();
            }
        }
        _settings.Show();
    }

    // Toolbar Handles

    private void minimizeApp(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
    private void quitApp(object? sender, RoutedEventArgs e)
    {
        fullClose();
    }

    // Core Functions

    public void fullClose()
    {
        // Other Cleanup
        if (_settings != null) { _settings.Close(); }

        // Main Cleanup
        Close();
    }
}