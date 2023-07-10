#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.

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
using Avalonia.Platform.Storage;
using System.Text.RegularExpressions;

namespace Autodraw;

public partial class MainWindow : Window
{
    //
    private Settings? _settings;

    //
    private SKBitmap _rawBitmap = new SKBitmap(318, 318, true);
    private Bitmap? _displayedBitmap; // For cleanup so I don't spam GC.Collect

    public MainWindow()
    {
        InitializeComponent();
        Config.init();

        // Taskbar
        CloseAppButton.Click += QuitApp_Click;
        MinimizeAppButton.Click += MinimizeApp_Click;
        SettingsButton.Click += OpenSettings_Click;

        // Base
        ProcessButton.Click += ProcessButton_Click;
        OpenButton.Click += OpenButton_Click;

        // Inputs
        DrawIntervalElement.TextChanging += DrawInterval_TextChanging;
        ClickDelayElement.TextChanging += ClickDelay_TextChanging;

        // Config
        OpenConfigElement.Click += LoadConfigViaDialog;
        SelectFolderElement.Click += SetFolderViaDialog;
        RefreshConfigsButton.Click += RefreshConfigList;
        SaveConfigButton.Click += SaveConfigViaDialog;
        LoadSelectButton.Click += LoadSelectedConfig;
        RefreshConfigList(this, null);
    }



    // Core Functions

    public void fullClose()
    {
        // Other Cleanup
        _settings?.Close();

        // Main Cleanup
        Close();
    }



    // External Window Opening/Closing Handles

    private void OpenSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings == null) _settings = new Settings();
        _settings.Show();
        _settings.Closed += Settings_Closed;
    }

    private void Settings_Closed(object? sender, EventArgs e)
    {
        _settings = null;
    }



    // Base UI Handles

    private void ProcessButton_Click(object? sender, RoutedEventArgs e)
    {
        SKBitmap processedBitmap = ImageProcessing.Process(_rawBitmap, new ImageProcessing.Filters() { Invert = false, Threshold = 127 });

        Bitmap _tmp = processedBitmap.ConvertToAvaloniaBitmap();
        ImagePreview.Source = _tmp;
    }

    private async void OpenButton_Click(object? sender, RoutedEventArgs e)
    {
        var file = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Save Config",
            FileTypeFilter = new FilePickerFileType[] { FilePickerFileTypes.ImageAll },
            AllowMultiple = false
        });

        if (file.Count == 1)
        {
            _rawBitmap = SKBitmap.Decode(file[0].TryGetLocalPath());
            Bitmap _tmp = _rawBitmap.ConvertToAvaloniaBitmap();
            ImagePreview.Source = _tmp;
        }
    }



    // Inputs Handles
    Regex numberRegex = new Regex(@"[^0-9]");

    private void DrawInterval_TextChanging(object? sender, TextChangingEventArgs e)
    {
        DrawIntervalElement.Text = numberRegex.Replace(DrawIntervalElement.Text, "");
        e.Handled = true;
    }
    private void ClickDelay_TextChanging(object? sender, TextChangingEventArgs e)
    {
        ClickDelayElement.Text = numberRegex.Replace(ClickDelayElement.Text, "");
        e.Handled = true;
    }
    private void BlackThreshold_TextChanging(object? sender, TextChangingEventArgs e)
    {
        BlackThresholdElement.Text = numberRegex.Replace(BlackThresholdElement.Text, "");
        e.Handled = true;
    }
    private void AlphaThreshold_TextChanging(object? sender, TextChangingEventArgs e)
    {
        AlphaThresholdElement.Text = numberRegex.Replace(AlphaThresholdElement.Text, "");
        e.Handled = true;
    }



    // Toolbar Handles

    private void MinimizeApp_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void QuitApp_Click(object? sender, RoutedEventArgs e)
    {
        fullClose();
    }



    // User Configuration Handles

    public static FilePickerFileType configsFileFilter { get; } = new("Autodraw Config Files")
    {
        Patterns = new[] { "*.drawcfg" }
    };

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
        var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Config",
            FileTypeChoices = new FilePickerFileType[] { configsFileFilter }
        });

        if (file is not null)
        {
            await using var stream = await file.OpenWriteAsync();
            using var streamWriter = new StreamWriter(stream);

            string[] values = { DrawIntervalElement.Text,
                            ClickDelayElement.Text,
                            BlackThresholdElement.Text,
                            AlphaThresholdElement.Text };

            streamWriter.Write(string.Join("\r\n", values));
        }
    }

    public async void LoadConfigViaDialog(object? sender, RoutedEventArgs e)
    {
        var file = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Save Config",
            FileTypeFilter = new FilePickerFileType[] { configsFileFilter },
            AllowMultiple = false
        });

        if (file.Count == 1)
        {
            LoadConfig(file[0].TryGetLocalPath());
        }
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
        var folder = await this.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions() { AllowMultiple = false });
        if (folder.Count != 1) return;
        Config.setEntry("ConfigFolder", folder[0].TryGetLocalPath());
        RefreshConfigList(this, null);
    }

    public void LoadSelectedConfig(object? sender, RoutedEventArgs e)
    {
        string SelectedItem = ConfigsListBox.SelectedItem.ToString();
        if (SelectedItem == null) return;
        LoadConfig($"{Path.Combine(Config.getEntry("ConfigFolder"), SelectedItem)}.drawcfg");
    }
}