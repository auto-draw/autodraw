#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8604 // Possible null reference argument.
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
using System.Threading;

namespace Autodraw;

public partial class MainWindow : Window
{
    private Settings? _settings;
    private DevTest? _devwindow;

    private SKBitmap? rawBitmap = new(318, 318, true);
    private SKBitmap? processedBitmap;
    private Bitmap? displayedBitmap;

    private int BlackThresh = 127;
    private int AlphaThresh = 200;

    public MainWindow()
    {
        InitializeComponent();
        Config.init();


        // Taskbar
        CloseAppButton.Click += QuitApp_Click;
        MinimizeAppButton.Click += MinimizeApp_Click;
        SettingsButton.Click += OpenSettings_Click;
        DevButton.Click += Dev_Click;

        // Base
        this.Closing += (object? sender, WindowClosingEventArgs e) => { Cleanup(); };
        ProcessButton.Click += ProcessButton_Click;
        OpenButton.Click += OpenButton_Click;
        RunButton.Click += RunButton_Click;

        // Inputs
        DrawIntervalElement.TextChanging += DrawInterval_TextChanging;
        ClickDelayElement.TextChanging += ClickDelay_TextChanging;
        BlackThresholdElement.TextChanging += BlackThreshold_TextChanging;
        AlphaThresholdElement.TextChanging += AlphaThreshold_TextChanging;

        // Config
        OpenConfigElement.Click += LoadConfigViaDialog;
        SelectFolderElement.Click += SetFolderViaDialog;
        RefreshConfigsButton.Click += RefreshConfigList;
        SaveConfigButton.Click += SaveConfigViaDialog;
        LoadSelectButton.Click += LoadSelectedConfig;
        RefreshConfigList(this, null);

        Input.Start();
    }



    // Core Functions

    public void Cleanup()
    {
        _devwindow?.Close();
        _settings?.Close();
        Input.Stop();
        Drawing.Halt();
    }



    // External Window Opening/Closing Handles

    private void OpenSettings_Click(object? sender, RoutedEventArgs e)
    {
        _settings ??= new Settings();
        _settings.Show();
        _settings.Closed += Settings_Closed;
    }

    private void Settings_Closed(object? sender, EventArgs e)
    {
        _settings = null;
    }


    private void OpenDevWindow()
    {
        _devwindow ??= new DevTest();
        _devwindow.Show();
        _devwindow.Closed += DevWindow_Closed;
    }

    private void DevWindow_Closed(object? sender, EventArgs e)
    {
        _devwindow = null;
    }



    // Base UI Handles

    private void ProcessButton_Click(object? sender, RoutedEventArgs e)
    {
        processedBitmap?.Dispose();
        displayedBitmap?.Dispose();

        processedBitmap = ImageProcessing.Process(rawBitmap, new ImageProcessing.Filters() { Invert = false, Threshold = (byte)BlackThresh, AlphaThreshold = (byte)AlphaThresh });
        displayedBitmap = processedBitmap.ConvertToAvaloniaBitmap();
        ImagePreview.Source = displayedBitmap;
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
            rawBitmap = SKBitmap.Decode(file[0].TryGetLocalPath());
            Bitmap _tmp = rawBitmap.ConvertToAvaloniaBitmap();
            ImagePreview.Source = _tmp;
        }
    }

    private async void RunButton_Click(Object? sender, RoutedEventArgs e)
    {
        if (processedBitmap == null) { return; }
        if (Drawing.isDrawing) { return; }
        WindowState = WindowState.Minimized;
        new Preview().ReadyDraw(processedBitmap);
    }



    // Inputs Handles
    Regex numberRegex = new(@"[^0-9]");

    private void DrawInterval_TextChanging(object? sender, TextChangingEventArgs e)
    {
        DrawIntervalElement.Text = numberRegex.Replace(DrawIntervalElement.Text, "");
        e.Handled = true;

        if (DrawIntervalElement.Text.Length < 1) { return; }

        try
        {
            Drawing.interval = int.Parse(DrawIntervalElement.Text);
        }
        catch
        {
            Drawing.interval = 10000;
        }
    }
    private void ClickDelay_TextChanging(object? sender, TextChangingEventArgs e)
    {
        ClickDelayElement.Text = numberRegex.Replace(ClickDelayElement.Text, "");
        e.Handled = true;

        if (ClickDelayElement.Text.Length < 1) { return; }

        try
        {
            Drawing.clickDelay = int.Parse(ClickDelayElement.Text);
        }
        catch
        {
            Drawing.clickDelay = 1000;
        }
    }
    private void BlackThreshold_TextChanging(object? sender, TextChangingEventArgs e)
    {
        BlackThresholdElement.Text = numberRegex.Replace(BlackThresholdElement.Text, "");
        e.Handled = true;

        if (BlackThresholdElement.Text.Length < 1) { return; }

        try
        {
            BlackThresh = int.Parse(BlackThresholdElement.Text);
        }
        catch
        {
            BlackThresh = 127;
        }
    }
    private void AlphaThreshold_TextChanging(object? sender, TextChangingEventArgs e)
    {
        AlphaThresholdElement.Text = numberRegex.Replace(AlphaThresholdElement.Text, "");
        e.Handled = true;

        if (AlphaThresholdElement.Text.Length < 1) { return; }

        try
        {
            AlphaThresh = int.Parse(AlphaThresholdElement.Text);
        }
        catch
        {
            AlphaThresh = 127;
        }
    }



    // Toolbar Handles

    private void MinimizeApp_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void QuitApp_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Dev_Click(object? sender, RoutedEventArgs e)
    {
        rawBitmap = DevTest.TestImage(64, 64);
        Bitmap _tmp = rawBitmap.ConvertToAvaloniaBitmap();
        ImagePreview.Source = _tmp;
        OpenDevWindow();
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
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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
        var file = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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