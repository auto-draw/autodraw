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
using System.Linq;

namespace Autodraw;

public partial class MainWindow : Window
{
    private Settings? _settings;
    private SKBitmap _bitmap = new SKBitmap(318,318,true);

    public MainWindow()
    {
        InitializeComponent();
        Config.init();

        CloseAppButton.Click += QuitApp;
        MinimizeAppButton.Click += MinimizeApp;
        SettingsButton.Click += OpenSettings;
        ProcessButton.Click += ProcessButton_Click;
        OpenConfigElement.Click += LoadConfigViaDialog;
        SelectFolderElement.Click += SetFolderViaDialog;
        RefreshConfigsButton.Click += RefreshConfigList;
        SaveConfigButton.Click += SaveConfigViaDialog;
        LoadSelectButton.Click += LoadSelectedConfig;
        RefreshConfigList(this, null);
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

    // User Configuration files

    public void LoadConfig(string path)
    {
        // TODO: use the warning box (Not implemented yet) system to make it return a "This config does not exist!"
        if (!path.EndsWith(".drawcfg")) { return; }
        string[] lines = File.ReadAllLines(path);
        SelectedConfigLabel.Content = $"Selected Config: {Path.GetFileNameWithoutExtension(path)}";
        DrawIntervalElement.Text = lines[0];
        ClickDelayElement.Text = lines[1];
        BlackThresholdElement.Text = lines[2];
        AlphaThresholdElement.Text = lines[3];
    }

    public async void SaveConfigViaDialog(object? sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog();
        dialog.Filters.Add(new FileDialogFilter() { Name = "Draw Configuration Files", Extensions = { "drawcfg" } });
        var result = await dialog.ShowAsync(this);
        if (result == null || result.Length == 0) return;
        string[] values = { DrawIntervalElement.Text, 
                            ClickDelayElement.Text, 
                            BlackThresholdElement.Text, 
                            AlphaThresholdElement.Text };
        File.WriteAllLines(result, Array.ConvertAll(values, x => x.ToString()));
    }

    public async void LoadConfigViaDialog(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog();
        dialog.Filters.Add(new FileDialogFilter() { Name = "Draw Configuration Files", Extensions = { "drawcfg" } });
        dialog.AllowMultiple = false;
        var result = await dialog.ShowAsync(this);
        if (result != null && result.Length != 0) LoadConfig(result[0]);
    }

    public void RefreshConfigList(object? sender, RoutedEventArgs? e)
    {
        string ConfigFolder = Config.getEntry("ConfigFolder");
        if (ConfigFolder == null) return;
        string[] files = Directory.GetFiles(ConfigFolder, "*.drawcfg");
        string[] fileNames = files.Select(f => Path.GetFileNameWithoutExtension(f)).ToArray();
        ConfigsListBox.ClearValue(ItemsControl.ItemsSourceProperty);
        ConfigsListBox.Items.Clear();
        ConfigsListBox.ItemsSource = fileNames;
    }

    public async void SetFolderViaDialog(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        var result = await dialog.ShowAsync(this);
        if (result == null || result.Length == 0) return;
        Config.setEntry("ConfigFolder", result);
        RefreshConfigList(sender, e);
    }

    public async void LoadSelectedConfig(object? sender, RoutedEventArgs e)
    {
        string SelectedItem = ConfigsListBox.SelectedItem.ToString();
        if (SelectedItem == null) return;
        LoadConfig($"{Path.Combine(Config.getEntry("ConfigFolder"), SelectedItem)}.drawcfg");
    }
}