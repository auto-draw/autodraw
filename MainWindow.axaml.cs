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

namespace Autodraw;

public partial class MainWindow : Window
{
    private Settings? _settings;
    private SKBitmap _bitmap = new SKBitmap(318, 318, true);

    public MainWindow()
    {
        InitializeComponent();
        Config.init();

        CloseAppButton.Click += QuitApp;
        MinimizeAppButton.Click += MinimizeApp;
        SettingsButton.Click += OpenSettings;
        ProcessButton.Click += ProcessButton_Click;
    }

    // External Window Opening/Closing Handles

    private void OpenSettings(object? sender, RoutedEventArgs e)
    {
        if (_settings == null) _settings = new Settings();
        _settings.Show();
        _settings.Closed += ClosedSettings;
    }

    private void ClosedSettings(object? sender, EventArgs e)
    {
        _settings = null;
    }


    private void ProcessButton_Click(object? sender, RoutedEventArgs e)
    {
        
        Bitmap _tmp = ImageExtensions.ConvertToAvaloniaBitmap(_bitmap);
        ImagePreview.Source = _tmp;
    }

    // Toolbar Handles

    private void MinimizeApp(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
    private void QuitApp(object? sender, RoutedEventArgs e)
    {
        fullClose();
    }

    // Core Functions

    public void fullClose()
    {
        // Other Cleanup
        _settings?.Close();

        // Main Cleanup
        Close();
    }
}