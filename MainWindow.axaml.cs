using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SkiaSharp;

namespace Autodraw;

public partial class MainWindow : Window
{
    public static MainWindow? CurrentMainWindow;

    private readonly ImageProcessing.Filters _currentFilters = new()
        { Invert = false, maxThreshold = 127, AlphaThreshold = 200 };

    private readonly Regex _numberRegex = new(@"[^0-9]");
    private DevTest? _devwindow;

    private Settings? _settings;
    private int _alphaThresh = 200;
    private Bitmap? _displayedBitmap;
    private int _minBlackThreshold;
    private int _maxBlackThreshold = 127;

    private long _lastMem;
    // ReSharper disable once NotAccessedField.Local
    private long _memoryPressure;
    private bool _inChange;
    private long _lastTime = DateTime.Now.ToFileTime();

    private SKBitmap? _rawBitmap = new(318, 318, true);
    private SKBitmap? _preFxBitmap = new(318, 318, true);
    private SKBitmap? _processedBitmap;

    public MainWindow()
    {
        this.AttachDevTools();

        InitializeComponent();

        CurrentMainWindow = this;
        Config.init();

        // Taskbar
        CloseAppButton.Click += QuitAppOnClick;
        MinimizeAppButton.Click += MinimizeAppOnClick;
        SettingsButton.Click += OpenSettingsOnClick;
        DevButton.Click += DevOnClick;

        // Base
        Closing += (sender, e) => { Cleanup(); };
        OpenButton.Click += OpenButtonOnClick;
        ProcessButton.Click += ProcessButtonOnClick;
        RunButton.Click += RunButtonOnClick;
        
        ImageSaveImage.Click += ImageSaveImageOnClick;
        ImageClearImage.Click += ImageClearImageOnClick;

        // Inputs
        SizeSlider.ValueChanged += SizeSliderOnValueChanged;

        WidthInput.TextChanging += WidthInputOnTextChanged;
        HeightInput.TextChanging += HeightInputOnTextChanged;
        PercentageNumber.TextChanging += PercentageNumberOnTextChanged;

        DrawIntervalElement.TextChanging += DrawIntervalOnTextChanging;
        ClickDelayElement.TextChanging += ClickDelayOnTextChanging;
        minBlackThresholdElement.TextChanging += minBlackThresholdElementOnTextChanging;
        maxBlackThresholdElement.TextChanging += maxBlackThresholdElementOnTextChanging;

        AlphaThresholdElement.TextChanging += AlphaThresholdOnTextChanging;

        FreeDrawCheckbox.Click += FreeDrawCheckboxOnClick;

        HorizontalFilterText.TextChanging += HorizontalFilterTextOnTextChanging;
        VerticalFilterText.TextChanging += VerticalFilterTextOnTextChanging;
        OutlineAdvancedText.TextChanging += OutlineAdvancedTextOnTextChanging;
        ErosionAdvancedText.TextChanging += ErosionAdvancedTextOnTextChanging;

        // Config
        RefreshConfigsButton.Click += RefreshConfigList;
        SelectFolderElement.Click += SetConfigFolderViaDialog;
        SaveConfigButton.Click += SaveConfigViaDialog;
        OpenConfigElement.Click += LoadConfigViaDialog;
        LoadSelectButton.Click += LoadSelectedConfig;
        RefreshConfigList(this, null);

        Input.Start();
    }

    // User Configuration Handles

    //*
    public static FilePickerFileType ConfigsFileFilter { get; } = new("AutoDraw Config Files")
    {
        Patterns = new[] { "*.drawcfg" }
    };
    
    public static FilePickerFileType PngFileFilter { get; } = new("Portable Network Graphics")
    {
        Patterns = new[] { "*.png" }
    };


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
            0;
        UpdatePath();
    }

    private void UpdatePath()
    {
        Drawing.PathValue =
            PatternSelection.SelectedIndex == 0 ? 12345678
            : PatternSelection.SelectedIndex == 1 ? 14627358
            : PatternSelection.SelectedIndex == 2 ? 26573481
            : 12345678;
    }

    private ImageProcessing.Filters GetSelectFilters()
    {
        // Generic Filters
        _currentFilters.minThreshold = (byte)_minBlackThreshold;
        _currentFilters.maxThreshold = (byte)_maxBlackThreshold;
        _currentFilters.AlphaThreshold = (byte)_alphaThresh;

        // Primary Filters
        
        //// Generic Filters
        _currentFilters.Invert = InvertFilterCheck.IsChecked ?? false;
        _currentFilters.OutlineSharp = OutlineFilterCheck.IsChecked ?? false;
        
        //// Pattern Filters
        _currentFilters.Crosshatch = CrosshatchFilterCheck.IsChecked ?? false;
        _currentFilters.DiagCrosshatch = DiagCrossFilterCheck.IsChecked ?? false;
        _currentFilters.HorizontalLines = int.Parse(HorizontalFilterText.Text ?? "0");
        _currentFilters.VerticalLines = int.Parse(VerticalFilterText.Text ?? "0");
        
        //// Experimental Filters
        //_currentFilters
        
        // Dither Filters
        // **Yet to be implemented**

        UpdatePath();

        return _currentFilters;
    }

    // External Window Opening/Closing Handles

    private void OpenSettingsOnClick(object? sender, RoutedEventArgs e)
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

    private void ProcessButtonOnClick(object? sender, RoutedEventArgs e)
    {
        if (_preFxBitmap.IsNull) return;
        _processedBitmap?.Dispose();
        _displayedBitmap?.Dispose();

        _processedBitmap = ImageProcessing.Process(_preFxBitmap, GetSelectFilters());
        _displayedBitmap = _processedBitmap.ConvertToAvaloniaBitmap();
        ImagePreview.Source = _displayedBitmap;
    }

    public void ImportImage(string path, byte[]? img = null)
    {
        _rawBitmap = img is null ? SKBitmap.Decode(path).NormalizeColor() : SKBitmap.Decode(img).NormalizeColor();
        _preFxBitmap = _rawBitmap.Copy();
        _displayedBitmap = _rawBitmap.NormalizeColor().ConvertToAvaloniaBitmap();
        _processedBitmap?.Dispose();
        _processedBitmap = null;
        ImagePreview.Source = _displayedBitmap;

        _inChange = true;
        SizeSlider.Value = 100;

        PercentageNumber.Text = $"{Math.Round(SizeSlider.Value)}%";
        WidthInput.Text = _displayedBitmap.Size.Width.ToString();
        HeightInput.Text = _displayedBitmap.Size.Height.ToString();
        _inChange = false;
    }

    private async void OpenButtonOnClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Image",
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll },
            AllowMultiple = false
        });

        if (file.Count == 1) ImportImage(file[0].TryGetLocalPath());
    }

    private void RunButtonOnClick(object? sender, RoutedEventArgs e)
    {
        UpdatePath();
        if (_processedBitmap == null)
        {
            new MessageBox().ShowMessageBox("Error!", "Please select and process an image beforehand.", "error");
            return;
        }

        if (Drawing.IsDrawing) return;
        new Preview().ReadyDraw(_processedBitmap);
        WindowState = WindowState.Minimized;
    }

    private async void ImageSaveImageOnClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Processed Image",
            FileTypeChoices = new[] { PngFileFilter }
        });
        
        if (file is not null)
        {
            var encodedData = _processedBitmap.Encode(SKEncodedImageFormat.Png, 100);
            await using var stream = await file.OpenWriteAsync();

            encodedData.SaveTo(stream);
        }
    }
    
    private void ImageClearImageOnClick(object? sender, RoutedEventArgs e)
    { 
        _rawBitmap = new(318, 318, true);
        _preFxBitmap = new(318, 318, true);
        _processedBitmap = null;
        _displayedBitmap = null;
        ImagePreview.Source = null;
    }

    // Inputs Handles

    private void ResizeImage(double width, double height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (GC.GetTotalMemory(false) < _lastMem) GC.RemoveMemoryPressure(_lastMem);
        _lastMem = GC.GetTotalMemory(false);

        if (_processedBitmap == null)
        {
            var resizedBitmap = _rawBitmap.Resize(new SKSizeI((int)width, (int)height), SKFilterQuality.High);
            _preFxBitmap.Dispose();
            _preFxBitmap = resizedBitmap;
            _displayedBitmap?.Dispose();
            _displayedBitmap = resizedBitmap.ConvertToAvaloniaBitmap();
            ImagePreview.Source = _displayedBitmap;
            GC.AddMemoryPressure(resizedBitmap.ByteCount);
            _memoryPressure += resizedBitmap.ByteCount;
        }
        else if (_processedBitmap != null)
        {
            var resizedBitmap = _rawBitmap.Resize(new SKSizeI((int)width, (int)height), SKFilterQuality.High);
            _preFxBitmap.Dispose();
            _preFxBitmap = resizedBitmap;
            var postProcessBitmap = ImageProcessing.Process(resizedBitmap, GetSelectFilters());
            _processedBitmap.Dispose();
            _processedBitmap = postProcessBitmap;
            _displayedBitmap?.Dispose();
            _displayedBitmap = postProcessBitmap.ConvertToAvaloniaBitmap();
            ImagePreview.Source = _displayedBitmap;
            GC.AddMemoryPressure(resizedBitmap.ByteCount);
            _memoryPressure += resizedBitmap.ByteCount;
        }
    }

    private void SizeSliderOnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_inChange) return;
        if (DateTime.Now.ToFileTime() - _lastTime < 333_333) return;
        _lastTime = DateTime.Now.ToFileTime();

        ResizeImage(_rawBitmap.Width * SizeSlider.Value / 100, _rawBitmap.Height * SizeSlider.Value / 100);

        _inChange = true;
        PercentageNumber.Text = $"{Math.Round(SizeSlider.Value)}%";
        WidthInput.Text = _displayedBitmap.Size.Width.ToString();
        HeightInput.Text = _displayedBitmap.Size.Height.ToString();
        _inChange = false;
    }

    private void PercentageNumberOnTextChanged(object? sender, TextChangingEventArgs e)
    {
        if (PercentageNumber.Text == null) return;
        if (_inChange) return;
        var numberText = _numberRegex.Replace(PercentageNumber.Text, "");
        PercentageNumber.Text = numberText + "%";
        e.Handled = true;

        if (numberText.Length < 1) return;
        var setNumber = int.Parse(numberText);
        if (setNumber < 1) return;
        if (setNumber > 500)
        {
            PercentageNumber.Text = "500%";
            return;
        }

        ResizeImage(_rawBitmap.Width * setNumber / 100, _rawBitmap.Height * setNumber / 100);

        _inChange = true;
        WidthInput.Text = _displayedBitmap.Size.Width.ToString();
        HeightInput.Text = _displayedBitmap.Size.Height.ToString();
        _inChange = false;
    }

    private void HeightInputOnTextChanged(object? sender, TextChangingEventArgs e)
    {
        if (HeightInput.Text == null) return;
        if (_inChange) return;
        var numberText = _numberRegex.Replace(HeightInput.Text, "");
        HeightInput.Text = numberText;
        e.Handled = true;

        if (numberText.Length < 1) return;
        var setNumber = int.Parse(numberText);
        if (setNumber < 1) return;
        if (setNumber > 8096)
        {
            PercentageNumber.Text = "8096";
            return;
        }

        ResizeImage(_rawBitmap.Width / (float)_rawBitmap.Height * setNumber, setNumber);

        _inChange = true;
        PercentageNumber.Text = $"{Math.Round(SizeSlider.Value)}%";
        WidthInput.Text = _displayedBitmap.Size.Width.ToString();
        _inChange = false;
    }

    private void WidthInputOnTextChanged(object? sender, TextChangingEventArgs e)
    {
        if (WidthInput.Text == null) return;
        if (_inChange) return;
        var numberText = _numberRegex.Replace(WidthInput.Text, "");
        WidthInput.Text = numberText;
        e.Handled = true;

        if (numberText.Length < 1) return;
        var setNumber = int.Parse(numberText);
        switch (setNumber)
        {
            case < 1:
                PercentageNumber.Text = "1";
                return;
            case > 4096:
                PercentageNumber.Text = "4096";
                return;
        }

        ResizeImage(setNumber, _rawBitmap.Height / (float)_rawBitmap.Width * setNumber);

        _inChange = true;
        PercentageNumber.Text = $"{Math.Round(SizeSlider.Value)}%";
        HeightInput.Text = _displayedBitmap.Size.Height.ToString();
        _inChange = false;
    }


    private void DrawIntervalOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        if (DrawIntervalElement.Text == null) return;
        DrawIntervalElement.Text = _numberRegex.Replace(DrawIntervalElement.Text, "");
        e.Handled = true;

        if (DrawIntervalElement.Text.Length < 1) return;

        try
        {
            Drawing.Interval = int.Parse(DrawIntervalElement.Text);
        }
        catch
        {
            Drawing.Interval = 10000;
        }
    }

    private void ClickDelayOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        if (ClickDelayElement.Text == null) return;
        ClickDelayElement.Text = _numberRegex.Replace(ClickDelayElement.Text, "");
        e.Handled = true;

        if (ClickDelayElement.Text.Length < 1) return;

        try
        {
            Drawing.ClickDelay = int.Parse(ClickDelayElement.Text);
        }
        catch
        {
            Drawing.ClickDelay = 1000;
        }
    }

    private void minBlackThresholdElementOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        if (minBlackThresholdElement.Text == null) return;
        minBlackThresholdElement.Text = _numberRegex.Replace(minBlackThresholdElement.Text, "");
        e.Handled = true;

        if (minBlackThresholdElement.Text.Length < 1) return;

        try
        {
            _minBlackThreshold = int.Parse(minBlackThresholdElement.Text);
        }
        catch
        {
            _minBlackThreshold = 127;
        }
    }

    private void maxBlackThresholdElementOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        if (maxBlackThresholdElement.Text == null) return;
        maxBlackThresholdElement.Text = _numberRegex.Replace(maxBlackThresholdElement.Text, "");
        e.Handled = true;

        if (maxBlackThresholdElement.Text.Length < 1) return;

        try
        {
            _maxBlackThreshold = int.Parse(maxBlackThresholdElement.Text);
        }
        catch
        {
            _maxBlackThreshold = 127;
        }
    }

    private void AlphaThresholdOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        if (AlphaThresholdElement.Text == null) return;
        AlphaThresholdElement.Text = _numberRegex.Replace(AlphaThresholdElement.Text, "");
        e.Handled = true;

        if (AlphaThresholdElement.Text.Length < 1) return;

        try
        {
            _alphaThresh = int.Parse(AlphaThresholdElement.Text);
        }
        catch
        {
            _alphaThresh = 127;
        }
    }

    private void handleTextChange(TextBox obj, TextChangingEventArgs e)
    {
        if (obj.Text == null) return;
        obj.Text = _numberRegex.Replace(obj.Text, "");
        e.Handled = true;

        if (obj.Text.Length < 1) return;
    }

    private void FreeDrawCheckboxOnClick(object? sender, RoutedEventArgs e)
    {
        Drawing.FreeDraw2 = FreeDrawCheckbox.IsChecked ?? false;
    }

    // Toolbar Handles

    private void MinimizeAppOnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void QuitAppOnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DevOnClick(object? sender, RoutedEventArgs e)
    {
        OpenDevWindow();
    }

    public async void PasteControl()
    {
        try
        {
            var clipboard = Clipboard;
            var fileFormat = (await clipboard.GetFormatsAsync()).ToList()[0];
            var file = await clipboard.GetDataAsync(fileFormat);
            ImportImage("", (byte[])file);
        }
        catch (Exception ex)
        {
            Utils.Log("Error with PasteControl(): " + ex);
        }
    }
    
    private void HorizontalFilterTextOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        handleTextChange(HorizontalFilterText,e);
    }

    private void VerticalFilterTextOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        handleTextChange(VerticalFilterText,e);
    }
    
    private void ErosionAdvancedTextOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        handleTextChange(ErosionAdvancedText, e);
    }

    private void OutlineAdvancedTextOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        handleTextChange(OutlineAdvancedText, e);
    }
    
    public async void SetConfigFolderViaDialog(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        if (folder.Count != 1) return;
        Config.setEntry("ConfigFolder", folder[0].TryGetLocalPath());
        RefreshConfigList(this, null);
    }

    public void LoadConfig(string? path)
    {
        // TODO: use the warning box (Not implemented yet) system to make it return a "This config does not exist!"
        if (!path.EndsWith(".drawcfg")) return;
        var lines = File.ReadAllLines(path);
        SelectedConfigLabel.Content = $"Selected Config: {Path.GetFileNameWithoutExtension(path)}";

        DrawIntervalElement.Text = lines.Length > 0 ? lines[0] : "10000";

        ClickDelayElement.Text = lines.Length > 1 ? lines[1] : "1000";

        //maxBlackThresholdElement.Text = lines.Length > 2 ? lines[2] : "127";
        //AlphaThresholdElement.Text = lines.Length > 3 ? lines[3] : "200";
        // Silly!!

        if (lines.Length <= 4) return;
        if (!bool.TryParse(lines[4], out var _fd2)) return;
        FreeDrawCheckbox.IsChecked = _fd2;
        Drawing.FreeDraw2 = _fd2;

        if (lines.Length <= 5) return;
        if (!int.TryParse(lines[5], out var _path)) return;
        SetPath(_path);

        minBlackThresholdElement.Text = lines.Length > 6 ? lines[6] : "0";
    }

    public async void SaveConfigViaDialog(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Config",
            FileTypeChoices = new[] { ConfigsFileFilter }
        });

        if (file is not null)
        {
            UpdatePath();
            await using var stream = await file.OpenWriteAsync();
            await using var streamWriter = new StreamWriter(stream);

            string?[] values =
            {
                DrawIntervalElement.Text,
                ClickDelayElement.Text,
                maxBlackThresholdElement.Text,
                AlphaThresholdElement.Text,
                FreeDrawCheckbox.IsChecked.ToString(),
                Drawing.PathValue.ToString(),
                minBlackThresholdElement.Text
            };

            await streamWriter.WriteAsync(string.Join("\r\n", values));
        }
    }

    public async void LoadConfigViaDialog(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load Config",
            FileTypeFilter = new[] { ConfigsFileFilter },
            AllowMultiple = false
        });

        if (file.Count == 1) LoadConfig(file[0].TryGetLocalPath());
    }

    public void RefreshConfigList(object? sender, RoutedEventArgs? e)
    {
        var configFolder = Config.getEntry("ConfigFolder");
        if (configFolder == null) return;
        if (!Directory.Exists(configFolder)) return;
        var files = Directory.GetFiles(configFolder, "*.drawcfg");
        var fileNames = files.Select(f => Path.GetFileNameWithoutExtension(f)).ToArray();
        ConfigsListBox.ClearValue(ItemsControl.ItemsSourceProperty);
        ConfigsListBox.Items.Clear();
        ConfigsListBox.ItemsSource = fileNames;
    }

    public void LoadSelectedConfig(object? sender, RoutedEventArgs e)
    {
        if (ConfigsListBox.SelectedItem == null) return;
        var selectedItem = ConfigsListBox.SelectedItem.ToString();
        if (selectedItem == null) return;
        LoadConfig($"{Path.Combine(Config.getEntry("ConfigFolder"), selectedItem)}.drawcfg");
    }
}