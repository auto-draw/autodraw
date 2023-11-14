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
using Avalonia.Input;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using RestSharp.Authenticators.OAuth2;
using RestSharp;
using System.Net;
using System.Net.Http;

namespace Autodraw;

public partial class MainWindow : Window
{
    public static MainWindow? CurrentMainWindow;

    private Settings? _settings;
    private DevTest? _devwindow;

    private SKBitmap? rawBitmap   = new(318, 318, true);
    private SKBitmap? preFXBitmap = new(318, 318, true);
    private SKBitmap? processedBitmap;
    private Bitmap? displayedBitmap;

#pragma warning disable IDE0052 // Such lies.
    private long MemPressure;
#pragma warning restore IDE0052
    private long lastMem = 0;
    private long Time = DateTime.Now.ToFileTime();

    private int minBlackThreshold = 0;
    private int maxBlackThreshold = 127;
    private int AlphaThresh = 200;
    private bool MidChange = false;

    private readonly ImageProcessing.Filters currentFilters = new() { Invert = false, maxThreshold = (byte)127, AlphaThreshold = (byte)200 };

    public MainWindow()
    {
        DevToolsExtensions.AttachDevTools(this);

        InitializeComponent();

        CurrentMainWindow = this;

        Config.init();
        keybinds.Text = "Keybinds:\nStart: Shift\nStop: Alt\nLock to Last/Current: Ctrl\nSkip Backtrace: Backspace\nPause/Resume: Backslash";

        // Taskbar
        CloseAppButton.Click += QuitApp_Click;
        MinimizeAppButton.Click += MinimizeApp_Click;
        SettingsButton.Click += OpenSettings_Click;
        DevButton.Click += Dev_Click;

        // Base
        Closing += (object? sender, WindowClosingEventArgs e) => { Cleanup(); };
        ProcessButton.Click += ProcessButton_Click;
        OpenButton.Click += OpenButton_Click;
        RunButton.Click += RunButton_Click;

        // Inputs
        SizeSlider.ValueChanged += SizeSlider_ValueChanged;

        WidthInput.TextChanging += WidthInput_TextChanged;
        HeightInput.TextChanging += HeightInput_TextChanged;
        PercentageNumber.TextChanging += PercentageNumber_TextChanged;

        DrawIntervalElement.TextChanging += DrawInterval_TextChanging;
        ClickDelayElement.TextChanging += ClickDelay_TextChanging;
        maxBlackThresholdElement.TextChanging += maxBlackThresholdElement_TextChanging;
        minBlackThresholdElement.TextChanging += minBlackThresholdElement_TextChanging;

        HorizontalFilterCheck.TextChanging += HorizontalFilterCheck_TextChanging;
        VerticalFilterCheck.TextChanging += VerticalFilterCheck_TextChanging;

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

    private void SetPath(int path)
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
        UpdatePath();
    }

    private void UpdatePath()
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

    private ImageProcessing.Filters GetSelectFilters()
    {
        // Generic Filters
        currentFilters.minThreshold = (byte)minBlackThreshold;
        currentFilters.maxThreshold = (byte)maxBlackThreshold;
        currentFilters.AlphaThreshold = (byte)AlphaThresh;

        // Primary Filters
        currentFilters.Invert = InvertFilterCheck.IsChecked ?? false;
        currentFilters.Outline = OutlineFilterCheck.IsChecked ?? false;
        currentFilters.OutlineSharp = SharpOutlineFilterCheck.IsChecked ?? false;
        currentFilters.HorizontalLines = int.Parse(HorizontalFilterCheck.Text ?? "0");
        currentFilters.VerticalLines = int.Parse(VerticalFilterCheck.Text ?? "0");
        currentFilters.Crosshatch = CrosshatchFilterCheck.IsChecked ?? false;
        currentFilters.DiagCrosshatch = DiagCrossFilterCheck.IsChecked ?? false;

        // Dither Filters

        UpdatePath();

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
        if (preFXBitmap.IsNull) return;
        processedBitmap?.Dispose();
        displayedBitmap?.Dispose();

        processedBitmap = ImageProcessing.Process(preFXBitmap, GetSelectFilters());
        displayedBitmap = processedBitmap.ConvertToAvaloniaBitmap();
        ImagePreview.Source = displayedBitmap;
    }
    
    public void ImportImage(string path)
    {
        rawBitmap = SKBitmap.Decode(path).NormalizeColor();
        preFXBitmap = rawBitmap.Copy();
        displayedBitmap = rawBitmap.NormalizeColor().ConvertToAvaloniaBitmap();
        processedBitmap?.Dispose();
        processedBitmap = null;
        ImagePreview.Source = displayedBitmap;

        MidChange = true;
        SizeSlider.Value = 100;

        PercentageNumber.Text = $"{Math.Round(SizeSlider.Value)}%";
        WidthInput.Text = displayedBitmap.Size.Width.ToString();
        HeightInput.Text = displayedBitmap.Size.Height.ToString();
        MidChange = false;
    }

    private async void OpenButton_Click(object? sender, RoutedEventArgs e)
    {
        var file = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Image",
            FileTypeFilter = new FilePickerFileType[] { FilePickerFileTypes.ImageAll },
            AllowMultiple = false
        });

        if (file.Count == 1)
        {
            ImportImage(file[0].TryGetLocalPath());
        }
    }

    private void RunButton_Click(Object? sender, RoutedEventArgs e)
    {
        UpdatePath();
        if (processedBitmap == null) { new MessageBox().ShowMessageBox("Error!", "Please select and process an image beforehand.", "error"); return; }
        if (Drawing.isDrawing) { return; }
        WindowState = WindowState.Minimized;
        new Preview().ReadyDraw(processedBitmap);
    }

    // Inputs Handles

    private void ResizeImage(double width, double height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (GC.GetTotalMemory(false) < lastMem)
        {
            GC.RemoveMemoryPressure(lastMem);
        }
        lastMem = GC.GetTotalMemory(false);

        if (processedBitmap == null)
        {
            SKBitmap resizedBitmap = rawBitmap.Resize(new SKSizeI((int)width, (int)height), SKFilterQuality.High);
            preFXBitmap.Dispose();
            preFXBitmap = resizedBitmap;
            displayedBitmap?.Dispose();
            displayedBitmap = resizedBitmap.ConvertToAvaloniaBitmap();
            ImagePreview.Source = displayedBitmap;
            GC.AddMemoryPressure(resizedBitmap.ByteCount);
            MemPressure += resizedBitmap.ByteCount;
        }
        else if (processedBitmap != null)
        {
            SKBitmap resizedBitmap = rawBitmap.Resize(new SKSizeI((int)width, (int)height), SKFilterQuality.High);
            preFXBitmap.Dispose();
            preFXBitmap = resizedBitmap;
            SKBitmap postProcessBitmap = ImageProcessing.Process(resizedBitmap, GetSelectFilters());
            processedBitmap.Dispose();
            processedBitmap = postProcessBitmap;
            displayedBitmap?.Dispose();
            displayedBitmap = postProcessBitmap.ConvertToAvaloniaBitmap();
            ImagePreview.Source = displayedBitmap;
            GC.AddMemoryPressure(resizedBitmap.ByteCount);
            MemPressure += resizedBitmap.ByteCount;
        }
    }

    private void SizeSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (MidChange) return;
        if (DateTime.Now.ToFileTime()-Time < 333_333) return;
        Time = DateTime.Now.ToFileTime();

        ResizeImage(rawBitmap.Width * SizeSlider.Value / 100, rawBitmap.Height * SizeSlider.Value / 100);

        MidChange = true;
        PercentageNumber.Text = $"{Math.Round(SizeSlider.Value)}%";
        WidthInput.Text = displayedBitmap.Size.Width.ToString();
        HeightInput.Text = displayedBitmap.Size.Height.ToString();
        MidChange = false;
    }

    readonly Regex numberRegex = new(@"[^0-9]");

    private void PercentageNumber_TextChanged(object? sender, TextChangingEventArgs e)
    {
        if (PercentageNumber.Text == null) return;
        if (MidChange) return;
        string numberText = numberRegex.Replace(PercentageNumber.Text, "");
        PercentageNumber.Text = numberText+"%";
        e.Handled = true;

        if (numberText.Length < 1) { return; }
        int setNumber = int.Parse(numberText);
        if (setNumber < 1) { return; }
        if (setNumber > 500) { PercentageNumber.Text = "500%"; return; }

        ResizeImage(rawBitmap.Width * setNumber / 100, rawBitmap.Height * setNumber / 100);

        MidChange = true;
        WidthInput.Text = displayedBitmap.Size.Width.ToString();
        HeightInput.Text = displayedBitmap.Size.Height.ToString();
        MidChange = false;
    }

    private void HeightInput_TextChanged(object? sender, TextChangingEventArgs e)
    {
        if (HeightInput.Text == null) return;
        if (MidChange) return;
        string numberText = numberRegex.Replace(HeightInput.Text, "");
        HeightInput.Text = numberText;
        e.Handled = true;

        if (numberText.Length < 1) { return; }
        int setNumber = int.Parse(numberText);
        if (setNumber < 1) { return; }
        if (setNumber > 8096) { PercentageNumber.Text = "8096"; return; }

        // Cast is redundant is a lie. It rounds it if its not explicitly cast as a float.
        ResizeImage(((float)rawBitmap.Width / (float)rawBitmap.Height)*setNumber, setNumber);

        MidChange = true;
        PercentageNumber.Text = $"{Math.Round(SizeSlider.Value)}%";
        WidthInput.Text = displayedBitmap.Size.Width.ToString();
        MidChange = false;
    }

    private void WidthInput_TextChanged(object? sender, TextChangingEventArgs e)
    {
        if (WidthInput.Text == null) return;
        if (MidChange) return;
        string numberText = numberRegex.Replace(WidthInput.Text, "");
        WidthInput.Text = numberText;
        e.Handled = true;

        if (numberText.Length < 1) { return; }
        int setNumber = int.Parse(numberText);
        if (setNumber < 1) { return; }
        if (setNumber > 8096) { PercentageNumber.Text = "8096"; return; }

        // Cast is redundant is a lie. It rounds it if its not explicitly cast as a float.
        ResizeImage(setNumber, ((float)rawBitmap.Height/ (float)rawBitmap.Width)*setNumber);

        MidChange = true;
        PercentageNumber.Text = $"{Math.Round(SizeSlider.Value)}%";
        HeightInput.Text = displayedBitmap.Size.Height.ToString();
        MidChange = false;
    }



    private void DrawInterval_TextChanging(object? sender, TextChangingEventArgs e)
    {
        if (DrawIntervalElement.Text == null) return;
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
        if (ClickDelayElement.Text == null) return;
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
    private void minBlackThresholdElement_TextChanging(object? sender, TextChangingEventArgs e)
    {
        if (minBlackThresholdElement.Text == null) return;
        minBlackThresholdElement.Text = numberRegex.Replace(minBlackThresholdElement.Text, "");
        e.Handled = true;

        if (minBlackThresholdElement.Text.Length < 1) { return; }

        try
        {
            minBlackThreshold = int.Parse(minBlackThresholdElement.Text);
        }
        catch
        {
            minBlackThreshold = 127;
        }
    }
    private void maxBlackThresholdElement_TextChanging(object? sender, TextChangingEventArgs e)
    {
        if (maxBlackThresholdElement.Text == null) return;
        maxBlackThresholdElement.Text = numberRegex.Replace(maxBlackThresholdElement.Text, "");
        e.Handled = true;

        if (maxBlackThresholdElement.Text.Length < 1) { return; }

        try
        {
            maxBlackThreshold = int.Parse(maxBlackThresholdElement.Text);
        }
        catch
        {
            maxBlackThreshold = 127;
        }
    }
    private void AlphaThreshold_TextChanging(object? sender, TextChangingEventArgs e)
    {
        if (AlphaThresholdElement.Text == null) return;
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
        if (CustomPatternInput.Text == null) return;
        CustomPatternInput.Text = numberRegex.Replace(CustomPatternInput.Text, "");
        e.Handled = true;

        if (CustomPatternInput.Text.Length < 1) { return; }
    }

    private void HorizontalFilterCheck_TextChanging(object? sender, TextChangingEventArgs e)
    {
        if (HorizontalFilterCheck.Text == null) return;
        HorizontalFilterCheck.Text = numberRegex.Replace(HorizontalFilterCheck.Text, "");
        e.Handled = true;

        if (HorizontalFilterCheck.Text.Length < 1) { HorizontalFilterCheck.Text = "0"; return; }
    }

    private void VerticalFilterCheck_TextChanging(object? sender, TextChangingEventArgs e)
    {
        if (VerticalFilterCheck.Text == null) return;
        VerticalFilterCheck.Text = numberRegex.Replace(VerticalFilterCheck.Text, "");
        e.Handled = true;

        if (VerticalFilterCheck.Text.Length < 1) { VerticalFilterCheck.Text = "0"; return; }
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
        OpenDevWindow();
    }

    // User Configuration Handles

    public static FilePickerFileType ConfigsFileFilter { get; } = new("Autodraw Config Files")
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

        maxBlackThresholdElement.Text = lines.Length > 2 ? lines[2] : "127";
          // Silly!!

        AlphaThresholdElement.Text = lines.Length > 3 ? lines[3] : "200";
          // Silly!!

        if (lines.Length <= 4) return;
        if (!bool.TryParse(lines[4], out bool _fd2)) return;
        FreeDrawCheckbox.IsChecked = _fd2;

        if (lines.Length <= 5) return;
        if (!int.TryParse(lines[5], out int _path)) return;
        SetPath(_path);

        minBlackThresholdElement.Text = lines.Length > 6 ? lines[6] : "0";
    }

    public async void SaveConfigViaDialog(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Config",
            FileTypeChoices = new FilePickerFileType[] { ConfigsFileFilter }
        });

        if (file is not null)
        {
            UpdatePath();
            await using var stream = await file.OpenWriteAsync();
            using var streamWriter = new StreamWriter(stream);

            string[] values = { DrawIntervalElement.Text,
                            ClickDelayElement.Text,
                            maxBlackThresholdElement.Text,
                            AlphaThresholdElement.Text,
                            FreeDrawCheckbox.IsChecked.ToString(),
                            Drawing.pathValue.ToString(),
                            minBlackThresholdElement.Text
            };

            streamWriter.Write(string.Join("\r\n", values));
        }
    }

    public async void LoadConfigViaDialog(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Save Config",
            FileTypeFilter = new FilePickerFileType[] { ConfigsFileFilter },
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