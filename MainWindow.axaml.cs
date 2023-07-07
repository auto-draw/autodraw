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
using System.ComponentModel;

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
        SettingsButton.Click += openSettings;
    }

    // External Window Opening/Closing Handles

    private void openSettings(object? sender, RoutedEventArgs e)
    {
        if (_settings == null) { _settings = new Settings(); }
        _settings.Show();
        _settings.Closed += closedSettings;
    }

    private void closedSettings(object? sender, EventArgs e)
    {
        _settings = null;
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