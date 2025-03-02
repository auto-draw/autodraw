using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Newtonsoft.Json;
using SharpHook;
using SharpHook.Native;
using SkiaSharp;

namespace Autodraw;

public partial class MainWindow : Window
{
    public static MainWindow? CurrentMainWindow;

    private readonly Regex _numberRegex = new(@"[^0-9]");
    private OpenAIPrompt? _aiPrompt;
    private int _alphaThresh = 200;
    private DevTest? _devwindow;
    private Bitmap? _displayedBitmap;
    private bool _inChange;

    private long _lastMem;
    private long _lastTime = DateTime.Now.ToFileTime();
    private int _maxBlackThreshold = 127;

    // You are wrong!
    // ReSharper disable once NotAccessedField.Local
    private long _memoryPressure;
    private int _minBlackThreshold;
    private SKBitmap? _preFxBitmap = new(318, 318, true);
    private SKBitmap? _processedBitmap;

    private SKBitmap? _rawBitmap = new(318, 318, true);

    private Settings? _settings;
    public long sessionTime = DateTime.Now.ToFileTime();

    public int widthLock = 0;
    public int heightLock = 0;
    public int widthNumber = 1;
    public int heightNumber = 1;

    public MainWindow()
    {
        InitializeComponent();

        if (Design.IsDesignMode) return;

        this.AttachDevTools();

        // Set language to user-specified language 
        var installedLanguage = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
        Thread.CurrentThread.CurrentCulture = new CultureInfo(Config.GetEntry("userlang") ?? installedLanguage);
        Thread.CurrentThread.CurrentUICulture = new CultureInfo(Config.GetEntry("userlang") ?? installedLanguage);
        Utils.Log(installedLanguage);

        CurrentMainWindow = this;
        // Onboarding
        //if (!File.Exists(Config.ConfigPath)) 
        //new Onboarding(CurrentMainWindow);
        Config.init();

        // Taskbar
        CloseAppButton.Click += (_, _) => Close();
        MinimizeAppButton.Click += MinimizeAppOnClick;
        SettingsButton.Click += OpenSettingsOnClick;
        DevButton.Click += (_, _) => OpenDevWindow();

        // Base
        Closing += (_, _) => Cleanup();
        OpenButton.Click += OpenButtonOnClick;
        ProcessButton.Click += ProcessButtonOnClick;
        RunButton.Click += RunButtonOnClick;

        ImageAIGeneration.Click += ImageAIGenerationOnClick;
        ImageSaveImage.Click += ImageSaveImageOnClick;
        ImageClearImage.Click += ImageClearImageOnClick;

        // Inputs
        SizeSlider.ValueChanged += SizeSliderOnValueChanged;
        WidthInput.TextChanging += WidthInputOnTextChanged;
        HeightInput.TextChanging += HeightInputOnTextChanged;
        
        WidthLock.Click += WidthLockOnClick;
        HeightLock.Click += HeightLockOnClick;
        
        PercentageNumber.TextChanging += PercentageNumberOnTextChanged;

        DrawIntervalElement.TextChanging += DrawIntervalOnTextChanging;
        ClickDelayElement.TextChanging += ClickDelayOnTextChanging;
        minBlackThresholdElement.TextChanging += minBlackThresholdElementOnTextChanging;
        maxBlackThresholdElement.TextChanging += maxBlackThresholdElementOnTextChanging;
        AlphaThresholdElement.TextChanging += AlphaThresholdOnTextChanging;

        FreeDrawCheckbox.Click += FreeDrawCheckboxOnClick;

        EventHandler<TextChangingEventArgs> textChangeEvent = (sender, e) => HandleTextChange(e);
        HorizontalFilterText.TextChanging += textChangeEvent;
        VerticalFilterText.TextChanging += textChangeEvent;
        BorderAdvancedText.TextChanging += textChangeEvent;
        OutlineAdvancedText.TextChanging += textChangeEvent;
        InlineAdvancedText.TextChanging += textChangeEvent;
        InlineBorderAdvancedText.TextChanging += textChangeEvent;
        ErosionAdvancedText.TextChanging += textChangeEvent;

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

    private void ImageAIGenerationOnClick(object? sender, RoutedEventArgs e)
    {
        if (_aiPrompt is not null) return;
        _aiPrompt = new OpenAIPrompt();
        _aiPrompt.Show();
        _aiPrompt.Closed += AiPromptOnClosed;
    }

    private void AiPromptOnClosed(object? sender, EventArgs e)
    {
        _aiPrompt = null;
    }


    // Core Functions

    public void Cleanup()
    {
        _devwindow?.Close();
        _settings?.Close();
        _aiPrompt?.Close();
        if (Utils.LogObject != null) Utils.LogObject.Close();
        Input.Stop();
        Drawing.Halt();
    }
    
    public ImageProcessing.Filters GetSelectFilters() // This has practically become an Update _CurrentFilters if anything, but aight.
    {
        // Generic Filters
        ImageProcessing._currentFilters.MinThreshold = (byte)_minBlackThreshold;
        ImageProcessing._currentFilters.MaxThreshold = (byte)_maxBlackThreshold;
        ImageProcessing._currentFilters.AlphaThreshold = (byte)_alphaThresh;

        // Primary Filters

        //// Generic Filters
        ImageProcessing._currentFilters.Invert = InvertFilterCheck.IsChecked ?? false;
        ImageProcessing._currentFilters.Outline = OutlineFilterCheck.IsChecked ?? false;

        //// Pattern Filters
        ImageProcessing._currentFilters.Crosshatch = CrosshatchFilterCheck.IsChecked ?? false;
        ImageProcessing._currentFilters.DiagCrosshatch = DiagCrossFilterCheck.IsChecked ?? false;
        ImageProcessing._currentFilters.HorizontalLines = int.Parse(HorizontalFilterText.Text ?? "0");
        ImageProcessing._currentFilters.VerticalLines = int.Parse(VerticalFilterText.Text ?? "0");

        //// Experimental Filters
        ImageProcessing._currentFilters.BorderAdvanced = int.Parse(BorderAdvancedText.Text ?? "0");
        ImageProcessing._currentFilters.OutlineAdvanced = int.Parse(OutlineAdvancedText.Text ?? "0");
        ImageProcessing._currentFilters.InlineAdvanced = int.Parse(InlineAdvancedText.Text ?? "0");
        ImageProcessing._currentFilters.InlineBorderAdvanced = int.Parse(InlineBorderAdvancedText.Text ?? "0");
        ImageProcessing._currentFilters.ErosionAdvanced = int.Parse(ErosionAdvancedText.Text ?? "0");

        // Dither Filters
        // **Yet to be implemented**

        return ImageProcessing._currentFilters;
    }

    // External Window Opening/Closing Handles

    private void OpenSettingsOnClick(object? sender, RoutedEventArgs e)
    {
        if (_settings is not null) return;
        _settings = new Settings();
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
    
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.GetPosition(this).Y <= 20)
            BeginMoveDrag(e);
    }


    // Base UI Handles

    private void ProcessButtonOnClick(object? sender, RoutedEventArgs e)
    {
        if (_preFxBitmap.IsNull) return;
        _processedBitmap?.Dispose();
        _displayedBitmap?.Dispose();

        _processedBitmap = ImageProcessing.Process(_preFxBitmap, GetSelectFilters());
        _displayedBitmap = _processedBitmap.ConvertToAvaloniaBitmap();
        ImagePreview.Image = _displayedBitmap;
    }

    public void ImportImage(string? path, byte[]? img = null)
    {
        _rawBitmap = img is null ? SKBitmap.Decode(path).NormalizeColor() : SKBitmap.Decode(img).NormalizeColor();
        _preFxBitmap = _rawBitmap.Copy();
        _displayedBitmap = _rawBitmap.NormalizeColor().ConvertToAvaloniaBitmap();
        _processedBitmap?.Dispose();
        _processedBitmap = null;
        ImagePreview.Image = _displayedBitmap;

        _inChange = true;
        SizeSlider.Value = 100;

        PercentageNumber.Text = $"{Math.Round(SizeSlider.Value)}%";
        WidthInput.Text = widthLock > 0 ? widthLock.ToString() : _displayedBitmap.Size.Width.ToString();
        HeightInput.Text =  heightLock > 0 ? heightLock.ToString() : _displayedBitmap.Size.Height.ToString();
        _inChange = false;
        
        if (widthLock > 0 || heightLock > 0) ResizeImage(_displayedBitmap.Size.Width, _displayedBitmap.Size.Height);
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
        if (_processedBitmap == null)
        {
            new MessageBox().ShowMessageBox("Error!", "Please select and process an image beforehand.", "error");
            return;
        }

        // Windows doesn't ask for permissions before mouse movement, Linux (wayland) and macOS require it.
        // We just create an empty Mouse Movement to trigger the popup
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var hook = new TaskPoolGlobalHook();
            hook.MouseMoved += (o, args) => { }; // Start listening for input
            hook.RunAsync();
        }
        if (Drawing.IsDrawing) return;
        Drawing.ChosenAlgorithm = (byte)AlgorithmSelection.SelectedIndex;
        
        // PURELY TESTING PURPOSES.
        var _drawStack = new List<SKBitmap>();
        var _actionStack = new List<InputAction>
        {
            new(InputAction.ActionType.MoveTo,new Vector2(1670,820)),
            new(InputAction.ActionType.LeftClick),
            new(InputAction.ActionType.MoveTo,new Vector2(1670,824)),
            new(InputAction.ActionType.LeftClick),
            new(InputAction.ActionType.LeftClick),
            new(InputAction.ActionType.LeftClick),
            new(InputAction.ActionType.KeyDown,"VcLeftControl"),
            new(InputAction.ActionType.KeyDown,"VcA"),
            new(InputAction.ActionType.KeyUp,"VcLeftControl"),
            new(InputAction.ActionType.KeyUp,"VcA"),
            new(InputAction.ActionType.WriteString,"{colorHex}")
        };
        
        foreach (var file in
                 Directory.GetFiles(@"C:\Users\Siydge\Pictures\AutodrawImages\interstellar\wormhole\color_layers"))
        {
            SKBitmap bitmap = SKBitmap.Decode(file);
            _drawStack.Add(bitmap);
        }

        //new Preview().ReadyStackDraw(_preFxBitmap, _drawStack, _actionStack);
        new Preview().ReadyDraw(_processedBitmap);
        WindowState = WindowState.Minimized;
    }

    private async void ImageSaveImageOnClick(object? sender, RoutedEventArgs e)
    {
        if (_processedBitmap is null) return;
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
        _rawBitmap = new SKBitmap(318, 318, true);
        _preFxBitmap = new SKBitmap(318, 318, true);
        _processedBitmap = null;
        _displayedBitmap = null;
        ImagePreview.Image = null;
    }

    // Inputs Handles

    private void HandleTextChange(TextChangingEventArgs e)
    {
        var source = (TextBox)e.Source;
        source.Text = _numberRegex.Replace(source.Text, "");
        e.Handled = true;

        if (source.Text.Length < 1) source.Text = "0";
    }

    private void ResizeImage(double width, double height)
    {
        width = widthLock > 0 ? widthLock : Math.Max(1, width);
        height = heightLock > 0 ? heightLock :  Math.Max(1, height);

        if (widthLock == 0) widthNumber = (int)width;
        if (heightLock == 0) heightNumber = (int)height;
        
        if (GC.GetTotalMemory(false) < _lastMem) GC.RemoveMemoryPressure(_lastMem);
        _lastMem = GC.GetTotalMemory(false);

        if (_processedBitmap == null)
        {
            var resizedBitmap = _rawBitmap.Resize(new SKSizeI((int)width, (int)height), SKFilterQuality.High);
            _preFxBitmap.Dispose();
            _preFxBitmap = resizedBitmap;
            _displayedBitmap?.Dispose();
            _displayedBitmap = resizedBitmap.ConvertToAvaloniaBitmap();
            ImagePreview.Image = _displayedBitmap;
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
            ImagePreview.Image = _displayedBitmap;
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
        _inChange = true;
        HeightInput.Text = numberText;
        _inChange = false;
        e.Handled = true;

        if (numberText.Length < 1) return;
        _inChange = true;
        double ratio = (double)_rawBitmap.Width / _rawBitmap.Height; // STUPID STUPID STUPID!!!

        int _heightNumber =  int.Parse(_numberRegex.Replace(HeightInput.Text, ""));
        int _widthNumber = (bool)UnlockAspectRatioCheckBox.IsChecked! ? int.Parse(WidthInput.Text) : (int)(_heightNumber * ratio);

        if(_widthNumber > 4096)
        {
            _heightNumber = (int)(4096 / ratio);
            _widthNumber = 4096;
        }
        
        _widthNumber = Math.Max(Math.Min(_widthNumber, 4096), 1);
        _heightNumber = Math.Max(Math.Min(_heightNumber, 4096), 1);
        
        if (UnlockAspectRatioCheckBox.IsChecked ?? false) ResizeImage(int.Parse(WidthInput.Text), _heightNumber);
        else ResizeImage(_widthNumber, _heightNumber);

        PercentageNumber.Text = $"{Math.Round((decimal)_heightNumber / _rawBitmap.Height * 100)}%";
        WidthInput.Text = _widthNumber.ToString();
        HeightInput.Text = _heightNumber.ToString();
        _inChange = false;
    }

    private void WidthInputOnTextChanged(object? sender, TextChangingEventArgs e)
    {
        if (WidthInput.Text == null) return;
        if (_inChange) return;
        var numberText = _numberRegex.Replace(WidthInput.Text, "");
        _inChange = true;
        WidthInput.Text = numberText;
        _inChange = false;
        e.Handled = true;

        if (numberText.Length < 1) return;
        _inChange = true;
        double ratio = (double)_rawBitmap.Height / _rawBitmap.Width; // STUPID STUPID STUPID!!!

        int _widthNumber = int.Parse(_numberRegex.Replace(WidthInput.Text, ""));
        int _heightNumber = (bool)UnlockAspectRatioCheckBox.IsChecked! ? int.Parse(HeightInput.Text) : (int)(_widthNumber * ratio);
        Utils.Log(_heightNumber);
        Utils.Log(ratio);

        if(_heightNumber > 4096)
        {
            _widthNumber = (int)(4096 / ratio);
            _heightNumber = 4096;
        }
        
        _widthNumber = Math.Max(Math.Min(_widthNumber, 4096), 1);
        _heightNumber = Math.Max(Math.Min(_heightNumber, 4096), 1);

        if (UnlockAspectRatioCheckBox.IsChecked ?? false) ResizeImage(_widthNumber, int.Parse(HeightInput.Text));
        else ResizeImage(_widthNumber, _heightNumber);

        PercentageNumber.Text = $"{Math.Round((decimal)_widthNumber / _rawBitmap.Width * 100)}%";
        WidthInput.Text = _widthNumber.ToString();
        HeightInput.Text = _heightNumber.ToString();
        _inChange = false;
    }

    private void HeightLockOnClick(object? sender, RoutedEventArgs e)
    {
        heightLock = heightLock > 0 ? 0 : heightNumber;
        HeightLockImage.Classes.Clear();
        HeightLockImage.Classes.Add(heightLock > 0 ? "LockedIcon" : "UnlockedIcon");
    }

    private void WidthLockOnClick(object? sender, RoutedEventArgs e)
    {
        widthLock = widthLock > 0 ? 0 : widthNumber;
        WidthLockImage.Classes.Clear();
        WidthLockImage.Classes.Add(widthLock > 0 ? "LockedIcon" : "UnlockedIcon");
    }


    private void DrawIntervalOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        HandleTextChange(e);
        Drawing.Interval = int.TryParse(DrawIntervalElement.Text, out var interval) ? interval : 10000;
    }

    private void ClickDelayOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        HandleTextChange(e);
        Drawing.ClickDelay = int.TryParse(ClickDelayElement.Text, out var clickDelay) ? clickDelay : 1000;
    }

    private void minBlackThresholdElementOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        HandleTextChange(e);
        _minBlackThreshold = int.TryParse(minBlackThresholdElement.Text, out var black) ? black : 127;
    }

    private void maxBlackThresholdElementOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        HandleTextChange(e);
        _maxBlackThreshold = int.TryParse(maxBlackThresholdElement.Text, out var black) ? black : 127;
    }

    private void AlphaThresholdOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        HandleTextChange(e);
        _alphaThresh = int.TryParse(AlphaThresholdElement.Text, out var alpha) ? alpha : 127;
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

    public async void PasteControl()
    {
        var clipboard = Clipboard;
        async void writeDump()
        {
            string dump = JsonConvert.SerializeObject(await clipboard.GetFormatsAsync(), Formatting.Indented);
            Utils.Log(dump);
            dump = JsonConvert.SerializeObject(await clipboard.GetTextAsync(), Formatting.Indented);
            Utils.Log(dump);
            dump = JsonConvert.SerializeObject(await clipboard.GetDataAsync(DataFormats.FileNames), Formatting.Indented);
            Utils.Log(dump);
            dump = JsonConvert.SerializeObject(await clipboard.GetDataAsync(DataFormats.Text), Formatting.Indented);
            Utils.Log(dump);
        }
        try
        {
            var file = await clipboard.GetDataAsync(DataFormats.Files) as IEnumerable<IStorageItem>;
            var img = await clipboard.GetDataAsync("PNG");
            string d = JsonConvert.SerializeObject(await clipboard.GetFormatsAsync(), Formatting.Indented);
            Utils.Log(d);
            if (file is not null) {ImportImage(file.First().Path.LocalPath);}
            else if (img is not null) {ImportImage("",(byte[]?)img);}
            else
            {
                new MessageBox().ShowMessageBox("Error!", "Invalid Image to Paste!", "error");
                Utils.Log("Error with PasteControl(): No image found in clipboard! Dumping clipboard.");
                writeDump();
            }
            
        }
        catch (Exception ex)
        {
            new MessageBox().ShowMessageBox("Error!", "Invalid Image to Paste!", "error");
            Utils.Log("Error with PasteControl(): " + ex);
            writeDump();
        }
    }

    public async void SetConfigFolderViaDialog(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        if (folder.Count != 1) return;
        Config.SetEntry("ConfigFolder", folder[0].TryGetLocalPath());
        RefreshConfigList(this, null);
    }

    public void LoadConfig(string? path)
    {
        // TODO: use the warning box (Not implemented yet) system to make it return a "This config does not exist!"
        if (!path.EndsWith(".drawcfg")) return;
        var lines = File.ReadAllLines(path);
        SelectedConfigLabel.Content =
            $"{Properties.Resources.ConfigSelected} - {Path.GetFileNameWithoutExtension(path)}";

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
        //if (!int.TryParse(lines[5], out var _path)) return;

        //minBlackThresholdElement.Text = lines.Length > 6 ? lines[6] : "0";
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
            await using var stream = await file.OpenWriteAsync();
            await using var streamWriter = new StreamWriter(stream);

            string?[] values =
            {
                DrawIntervalElement.Text,
                ClickDelayElement.Text,
                maxBlackThresholdElement.Text,
                AlphaThresholdElement.Text,
                FreeDrawCheckbox.IsChecked.ToString(),
                "", //We should've used Json for future compatibility and freedom to change and remove config variables @gz9.
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
        var configFolder = Config.GetEntry("ConfigFolder");
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
        LoadConfig($"{Path.Combine(Config.GetEntry("ConfigFolder"), selectedItem)}.drawcfg");
    }
}