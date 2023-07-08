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
using System.Linq;

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
        OpenConfigElement.Click += loadConfigViaDialog;
        SelectFolderElement.Click += setFolderViaDialog;
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
        // TODO: use the warning box (Not implemented yet) system to make it return a "This config does not exist!"
        if (!path.EndsWith(".drawcfg")) { return; }
        string[] lines = File.ReadAllLines(path);
        DrawIntervalElement.Text = lines[0];
        ClickDelayElement.Text = lines[1];
        BlackThresholdElement.Text = lines[2];
        AlphaThresholdElement.Text = lines[3];
    }

    public async void loadConfigViaDialog(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog();
        dialog.Filters.Add(new FileDialogFilter() { Name = "Draw Configuration Files", Extensions = { "drawcfg" } });
        dialog.AllowMultiple = false;
        var result = await dialog.ShowAsync(this);
        if (result != null && result.Length != 0) loadConfig(result[0]);
    }

    public void refreshConfigList()
    {

    }

    public async void setFolderViaDialog(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        var result = await dialog.ShowAsync(this);
        if (result == null || result.Length == 0) return;
        Config.setEntry("ConfigFolder", result);
        refreshConfigList();
    }
}