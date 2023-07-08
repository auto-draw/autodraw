using Avalonia.Controls;
using Avalonia.Themes.Fluent;
using SkiaSharp;
using System;
using System.IO;
using Newtonsoft.Json;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using System.Drawing;

namespace Autodraw;

public partial class MainWindow : Window
{
    private Settings? _settings = null;
    private SKBitmap _bitmap = new SKBitmap(315,315,true);

    public MainWindow()
    {
        InitializeComponent();
        Config.init();
        
        ImagePreview.Source = ImageExtensions.ConvertToAvaloniaBitmap(_bitmap);

        CloseAppButton.Click += quitApp;
        MinimizeAppButton.Click += minimizeApp;
        SettingsButton.Click += openSettings;
    }

    // External Window Opening/Closing Handles

    private void openSettings(object? sender, RoutedEventArgs e)
    {
        if (_settings == null) _settings = new Settings();
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

    // User Configuration files

    public void loadConfig(string path)
    {

    }

    public void loadConfigViaDialog()
    {

    }
}