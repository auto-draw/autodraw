using Avalonia.Controls;
using Avalonia.Themes.Fluent;
using SkiaSharp;
using System;
using System.IO;
using Newtonsoft.Json;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Avalonia.Interactivity;

namespace Autodraw;

public partial class MainWindow : Window
{
    private Settings? _settings = null;

    public MainWindow()
    {
        InitializeComponent();
        Config.init();
    }

    public void fullClose()
    {
        // Other Cleanup
        if(_settings != null) { _settings.Close(); }

        // Main Cleanup
        Close();
    }

    private void openSettings(object sender, RoutedEventArgs e)
    {
        if (_settings == null) { _settings = new Settings(); }
        _settings.Show();
    }

    private void quitApp(object sender, RoutedEventArgs e)
    {
        fullClose();
    }
}