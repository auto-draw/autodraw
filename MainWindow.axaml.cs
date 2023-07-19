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
using System.Diagnostics;

namespace Autodraw;

public partial class MainWindow : Window
{
    private Settings? _settings;
    private DevTest? _devwindow;

    private SKBitmap? rawBitmap   = new(318, 318, true);
    private SKBitmap? preFXBitmap = new(318, 318, true);
    private SKBitmap? processedBitmap;
    private Bitmap? displayedBitmap;

    private int MemPressure = 0;
    private long lastMem = 0;
    private long Time = DateTime.Now.ToFileTime();

    private int BlackThresh = 127;
    private int AlphaThresh = 200;

    private ImageProcessing.Filters currentFilters = new ImageProcessing.Filters() { Invert = false, Threshold = (byte)127, AlphaThreshold = (byte)200 };

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
        SizeSlider.ValueChanged += SizeSlider_ValueChanged;

        DrawIntervalElement.TextChanging += DrawInterval_TextChanging;
        ClickDelayElement.TextChanging += ClickDelay_TextChanging;
        BlackThresholdElement.TextChanging += BlackThreshold_TextChanging;

        CustomPatternInput.TextChanging += CustomPatternInput_TextChanging;

        AlphaThresholdElement.TextChanging += AlphaThreshold_TextChanging;

        FreeDrawCheckbox.Click += FreeDrawCheckbox_Click;

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

    private void setPath(int path)
    {
        PatternSelection.SelectedIndex =
            path == 12345678 ? 0 :
            path == 14627358 ? 1 :
            path == 26573481 ? 2 :
            3;
        if (PatternSelection.SelectedIndex == 3)
        {
            CustomPatternInput.Text = path.ToString();
        }
        updatePath();
    }

    private void updatePath()
    {
        int Path = 12345678;
        try
        {
            if (CustomPatternInput.Text.Length == 8)
            {
                Path = int.Parse(CustomPatternInput.Text);
            }
        }
        catch { Path = 12345678; }

        Drawing.pathValue =
            PatternSelection.SelectedIndex == 0 ? 12345678 :
            PatternSelection.SelectedIndex == 1 ? 14627358 :
            PatternSelection.SelectedIndex == 2 ? 26573481 :
            PatternSelection.SelectedIndex == 3 ? Path
            : 12345678;
    }

    private ImageProcessing.Filters getSelectFilters()
    {
        currentFilters.Threshold = (byte)BlackThresh;
        currentFilters.AlphaThreshold = (byte)AlphaThresh;
        currentFilters.Invert = InvertFilterCheck.IsChecked ?? false;
        currentFilters.Outline = OutlineFilterCheck.IsChecked ?? false;
        currentFilters.OutlineSharp = SharpOutlineFilterCheck.IsChecked ?? false;
        currentFilters.Crosshatch = CrosshatchFilterCheck.IsChecked ?? false;
        currentFilters.DiagCrosshatch = DiagCrossFilterCheck.IsChecked ?? false;

        updatePath();

        return currentFilters;
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

        processedBitmap = ImageProcessing.Process(preFXBitmap, getSelectFilters());
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
            rawBitmap = SKBitmap.Decode(file[0].TryGetLocalPath()).NormalizeColor();
            preFXBitmap = rawBitmap.Copy();
            displayedBitmap = rawBitmap.NormalizeColor().ConvertToAvaloniaBitmap();
            processedBitmap?.Dispose();
            processedBitmap = null;
            ImagePreview.Source = displayedBitmap;

            SizeSlider.Value = 100;

            PercentageNumber.Text = $"{Math.Round(SizeSlider.Value)}%";
            WidthInput.Text = displayedBitmap.Size.Width.ToString();
            HeightInput.Text = displayedBitmap.Size.Height.ToString();
        }
    }

    private async void RunButton_Click(Object? sender, RoutedEventArgs e)
    {
        updatePath();
        if (processedBitmap == null) { new MessageBox().ShowMessageBox("Error!", "Please select and process an image beforehand.", "error"); return; }
        if (Drawing.isDrawing) { return; }
        WindowState = WindowState.Minimized;
        new Preview().ReadyDraw(processedBitmap);
    }



    // Inputs Handles

    private void SizeSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (DateTime.Now.ToFileTime()-Time < 1_000_000) return;
        Time = DateTime.Now.ToFileTime();

        if (GC.GetTotalMemory(false) < lastMem)
        {
            GC.RemoveMemoryPressure(lastMem);
        }
        lastMem = GC.GetTotalMemory(false);
        // Dirty? yes. Works? yes.

        if (processedBitmap == null)
        {
            SKBitmap resizedBitmap = rawBitmap.Resize(new SKSizeI((int)(rawBitmap.Width * SizeSlider.Value / 100), (int)(rawBitmap.Height * SizeSlider.Value / 100)), SKFilterQuality.High);
            preFXBitmap.Dispose();
            preFXBitmap = resizedBitmap;
            displayedBitmap?.Dispose();
            displayedBitmap = resizedBitmap.ConvertToAvaloniaBitmap();
            ImagePreview.Source = displayedBitmap;
            GC.AddMemoryPressure(resizedBitmap.ByteCount);
            MemPressure += resizedBitmap.ByteCount;
        }else if (processedBitmap != null)
        {
            SKBitmap resizedBitmap = rawBitmap.Resize(new SKSizeI((int)(rawBitmap.Width * SizeSlider.Value / 100), (int)(rawBitmap.Height * SizeSlider.Value / 100)), SKFilterQuality.High);
            preFXBitmap.Dispose();
            preFXBitmap = resizedBitmap;
            SKBitmap postProcessBitmap = ImageProcessing.Process(resizedBitmap, getSelectFilters());
            processedBitmap.Dispose();
            processedBitmap = postProcessBitmap;
            displayedBitmap?.Dispose();
            displayedBitmap = postProcessBitmap.ConvertToAvaloniaBitmap();
            ImagePreview.Source = displayedBitmap;
            GC.AddMemoryPressure(resizedBitmap.ByteCount);
            MemPressure += resizedBitmap.ByteCount;
        }

        PercentageNumber.Text = $"{Math.Round(SizeSlider.Value)}%";
        WidthInput.Text = displayedBitmap.Size.Width.ToString();
        HeightInput.Text = displayedBitmap.Size.Height.ToString();
    }


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

    private void CustomPatternInput_TextChanging(object? sender, TextChangingEventArgs e)
    {
        CustomPatternInput.Text = numberRegex.Replace(CustomPatternInput.Text, "");
        e.Handled = true;

        if (CustomPatternInput.Text.Length < 1) { return; }
    }


    private void FreeDrawCheckbox_Click(object? sender, RoutedEventArgs e)
    {
        Drawing.freeDraw2 = FreeDrawCheckbox.IsChecked ?? false;
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
        DrawIntervalElement.Text = lines.Length > 0 ? lines[0] : "10000";
        ClickDelayElement.Text = lines.Length > 1 ? lines[1] : "1000";
        BlackThresholdElement.Text = lines.Length > 2 ? lines[2] : "127";
        AlphaThresholdElement.Text = lines.Length > 3 ? lines[3] : "200";
        if (lines.Length <= 4) return;
        if (!bool.TryParse(lines[4], out bool _fd2)) return;
        FreeDrawCheckbox.IsChecked = _fd2;
        if (lines.Length <= 5) return;
        if (!int.TryParse(lines[5], out int _path)) return;
        setPath(_path);
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
            updatePath();
            await using var stream = await file.OpenWriteAsync();
            using var streamWriter = new StreamWriter(stream);

            string[] values = { DrawIntervalElement.Text,
                            ClickDelayElement.Text,
                            BlackThresholdElement.Text,
                            AlphaThresholdElement.Text,
                            FreeDrawCheckbox.IsChecked.ToString(),
                            Drawing.pathValue.ToString()
            };

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
        if (!Directory.Exists(ConfigFolder)) return;
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
        if (ConfigsListBox.SelectedItem == null) return;
        string SelectedItem = ConfigsListBox.SelectedItem.ToString();
        if (SelectedItem == null) return;
        LoadConfig($"{Path.Combine(Config.getEntry("ConfigFolder"), SelectedItem)}.drawcfg");
    }
}