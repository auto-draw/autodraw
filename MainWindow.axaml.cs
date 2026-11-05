using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SharpHook;
using SharpHook.Native;
using SkiaSharp;
using Autodraw.Configuration;
using Autodraw.ImageInput;
using Autodraw.Painting.Recreate;

namespace Autodraw;

public class ActionDisp
{
    public string Text { get; set; }
    public InputAction boundAction { get; set; }
    public int Speed { get; set; }
    public int Delay { get; set; }
}

public class LayerDisp
{
    public string HexColor { get; set; }
    public IBrush HexColorBrush { get; set; }
    public Bitmap Thumbnail { get; set; }
    public SKBitmap RawBitmap { get; set; }
    public SKColor Color { get; set; }
    public int PixelCount { get; set; }
    public string PixelCountText => $"{PixelCount} pixels";
    public bool Enabled { get; set; } = true;
}

public partial class MainWindow : Window
{
    public static MainWindow? CurrentMainWindow;
    private OpenAIPrompt? _aiPrompt;
    private DevTest? _devwindow;
    private Bitmap? _displayedBitmap;
    private bool _inChange;

    private readonly Regex _numberRegex = new(@"[^0-9]");

    // Automation
    public ObservableCollection<ActionDisp> ActionsContext { get; set; } = new();
    public ObservableCollection<LayerDisp> LayersContext { get; set; } = new();
    private List<InputAction> _actionStack = new();
    List<ColorLayerResult> _layersStack = new();

    // HexedDrawer integration
    private HexedDrawWindow? _hexedDrawWindow;
    private bool _hexedDrawRunning;
    private bool _recreateRunning;
    private CancellationTokenSource? _recreateCts;
    private RecreatePauseController? _recreatePause;
    private RecreateEngine? _recreateEngine;
    private RecreatePreviewWindow? _recreatePreviewWindow;
    private bool _quantizing;
    private int _quantizeGeneration;
    private bool _imageEnhanced;
    private bool _enhanceFailed;
    private bool _hasImportedImage;
    private bool _capturingRegion;
    private bool _syncingMonitorList;

    private readonly List<MonitorInfo> _monitors = new();
    private readonly List<Screen> _monitorScreens = new();

    public bool HasImportedImage => _hasImportedImage && _preFxBitmap is { Width: > 1, Height: > 1 };
    public string? ImportedImagePath { get; private set; }
    public Bitmap? QuantizedPreview { get; private set; }
    public bool IsCapturingColorPicker => _capturingColorPicker;
    public bool IsCapturingRegion => _capturingRegion;
    public event EventHandler? QuantizedPreviewChanged;
    public event EventHandler? QuantizedPreviewInvalidated;

    private long _lastMem;
    private long _lastTime = DateTime.Now.ToFileTime();
    private int _maxBlackThreshold = 127;
    private int _alphaThresh = 200;

    private int _minBlackThreshold;
    private SKBitmap? _preFxBitmap = new(318, 318, true);
    private SKBitmap? _processedBitmap;

    private SKBitmap? _rawBitmap = new(318, 318, true);

    private Settings? _settings;
    private readonly List<Vector2> _colorPickerCalibrationPoints = new();
    private bool _capturingColorPicker;
    private PickerCalibrationOverlay? _calibrationOverlay;
    // Screen rect of the guide overlay, cached on the UI thread. The mouse hook runs on
    // a background thread and must never read Avalonia visual properties itself.
    private int _overlayLeft = int.MinValue;
    private int _overlayTop = int.MinValue;
    private int _overlayRight = int.MinValue;
    private int _overlayBottom = int.MinValue;

    /// <summary>
    /// Which calibration scheme the next captured clicks belong to.  The two picker values map to
    /// <see cref="PickerRegistry"/> ids; the current-colour indicator is an engine setting, not a
    /// picker, so it has its own mode.
    /// </summary>
    private enum ColorPickerCalibrationMode
    {
        Wheel,
        BoxAll,
        RecreateCurrentColorPoint
    }

    private ColorPickerCalibrationMode _calibrationMode = ColorPickerCalibrationMode.Wheel;

    public int widthLock = 0;
    public int heightLock = 0;
    public int widthNumber = 1;
    public int heightNumber = 1;

    public MainWindow()
    {
        DataContext = this; // This stupid piece of shit is required, unlike normal Xaml, fuck it.
        
        InitializeComponent();
        UpdateColorPickerModeStatus();

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
        LocalizationManager.Load(Config.GetEntry("userlang") ?? installedLanguage);
        DdConfigService.AutoLoad();
        DdConfigService.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(ApplyDdConfigToUi);
        ApplyDdConfigToUi();

        UpdateActionsContext();

        // Taskbar
        CloseAppButton.Click += (_, _) => Close();
        MinimizeAppButton.Click += MinimizeAppOnClick;
        SettingsButton.Click += OpenSettingsOnClick;
        DevButton.Click += (_, _) => OpenDevWindow();
        ColorPickerHelpButton.Click += ShowColorPickerHelp;
        HexedDrawButton.Click += (_, _) => OpenHexedDrawWindow();
        UpdateColorPickerModeStatus();
        InitializeImageInputUi();

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
        AlgorithmSelection.SelectionChanged += AlgorithmSelectionOnSelectionChanged;

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
        Input.taskHook.KeyReleased += ConfigureColorPicker;
        Input.taskHook.KeyPressed += HexedDrawHotkeys;
    }

    /// <summary>Global HexedDrawer hotkeys: F1 area, F2 start/pause, F3 stop, F4 reset, F6 quantize.</summary>
    private void HexedDrawHotkeys(object? sender, KeyboardHookEventArgs e)
    {
        if (_capturingColorPicker || _capturingRegion) return;
        switch (e.Data.KeyCode)
        {
            case KeyCode.VcF1:
                Dispatcher.UIThread.Post(async () => await CaptureRegionToImageAsync());
                break;
            case KeyCode.VcF2:
                Dispatcher.UIThread.Post(async () => await StartPauseHexedDrawAsync());
                break;
            case KeyCode.VcF3:
                Dispatcher.UIThread.Post(() =>
                {
                    var wasDrawing = Drawing.IsDrawing || _hexedDrawRunning || _recreateRunning;
                    StopHexedDraw();
                    if (!wasDrawing) _hexedDrawWindow?.Close();
                });
                break;
            case KeyCode.VcF4:
                Dispatcher.UIThread.Post(ResetHexedDraw);
                break;
            case KeyCode.VcF6:
                Dispatcher.UIThread.Post(async () => await QuantizeForHexedDrawAsync());
                break;
        }
    }

    // User Configuration Handles

    //*
    public static FilePickerFileType ConfigsFileFilter { get; } = new("AutoDraw Config Files")
    {
        Patterns = new[] { "*.drawcfg" }
    };
    public static FilePickerFileType DdConfigFileFilter { get; } = new("dd.json Configuration") { Patterns = new[] { "*.json" } };

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
        _recreatePreviewWindow?.Close();
        if (Utils.LogObject != null) Utils.LogObject.Close();
        _recreateCts?.Cancel();
        Input.Stop();
        Drawing.Halt();
    }

    /// <summary>
    /// Listens for the calibration hotkey on the global hook.  F5 calibrates whichever picker is
    /// selected in <c>dd.json</c>; the guide window derives its steps from that picker's
    /// <see cref="IPickerDefinition.Points"/>, so a new picker needs no changes here.
    /// </summary>
    private void ConfigureColorPicker(object? sender, KeyboardHookEventArgs e)
    {
        if (_capturingColorPicker) return;
        if (e.Data.KeyCode == KeyCode.VcF5)
            BeginSelectedPickerCalibration();
    }

    /// <summary>Runs the calibration flow for the picker type currently selected in dd.json.
    /// The Settings tab's Calibrate button and the F5 hotkey both call this method.</summary>
    public void BeginSelectedPickerCalibration()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(BeginSelectedPickerCalibration);
            return;
        }
        if (_capturingColorPicker) return;
        _hexedDrawWindow?.HideForCalibration();
        StartColorPickerCalibration(SelectedPickerCalibrationMode());
    }

    /// <summary>Registry id for a picker calibration mode, or null for the current-colour point.</summary>
    private static string? PickerModeId(ColorPickerCalibrationMode mode) => mode switch
    {
        ColorPickerCalibrationMode.Wheel => "Wheel",
        ColorPickerCalibrationMode.BoxAll => "Box",
        _ => null
    };

    private static ColorPickerCalibrationMode SelectedPickerCalibrationMode()
        => string.Equals(DdConfigService.Current.PickerType, "Box", StringComparison.OrdinalIgnoreCase)
            ? ColorPickerCalibrationMode.BoxAll
            : ColorPickerCalibrationMode.Wheel;

    public void BeginRecreateCurrentColorCalibration()
    {
        if (_capturingColorPicker) return;
        StartColorPickerCalibration(ColorPickerCalibrationMode.RecreateCurrentColorPoint);
    }

    /// <summary>The click-by-click guidance for each calibration scheme, in capture order.</summary>
    private static string[] CalibrationSteps(ColorPickerCalibrationMode mode)
    {
        if (PickerModeId(mode) is { } pickerId && PickerRegistry.Find(pickerId) is { } definition)
            return definition.Points
                .Select(point => LocalizationManager.T(point.PromptKey))
                .ToArray();

        return new[]
        {
            CalibText("calib_step_recreate_current", "Click the target application's current-colour indicator (the swatch showing the selected colour).")
        };
    }

    private static string CalibText(string key, string fallback)
    {
        var value = LocalizationManager.T(key);
        return value == key ? fallback : value;
    }

    private void StartColorPickerCalibration(ColorPickerCalibrationMode mode)
    {
        _capturingColorPicker = true;
        _calibrationMode = mode;
        _colorPickerCalibrationPoints.Clear();
        Input.taskHook.MouseClicked += CaptureColorPickerPoint;

        Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
        {
            _calibrationOverlay = new PickerCalibrationOverlay();
            _calibrationOverlay.CancelRequested += CancelColorPickerCalibration;
            _calibrationOverlay.Closed += (_, _) =>
            {
                _calibrationOverlay = null;
                InvalidateOverlayBounds();
            };
            _calibrationOverlay.PositionChanged += (_, _) => CacheOverlayBounds();
            _calibrationOverlay.PropertyChanged += (_, e) =>
            {
                if (e.Property == BoundsProperty) CacheOverlayBounds();
            };
            // Show the first guidance step, then minimize so the target picker is visible.
            _calibrationOverlay.ShowFor(0, CalibrationSteps(mode).Length, CalibrationStepText(0), LocalizationManager.T("calib_title"));
            CacheOverlayBounds();
            WindowState = WindowState.Minimized;
            UpdateGuidanceStep(0);
        });
    }

    private string CalibrationStepText(int completed)
    {
        var steps = CalibrationSteps(_calibrationMode);
        return steps[Math.Min(completed, steps.Length - 1)];
    }

    /// <summary>True when the click happened inside the guide window itself (e.g. its Cancel button)
    /// and therefore must not be counted as a calibration point. Reads only cached ints so it is
    /// safe to call from the mouse-hook thread.</summary>
    private bool IsClickInsideOverlay(Vector2 point)
    {
        if (_overlayRight <= _overlayLeft || _overlayBottom <= _overlayTop) return false;
        return point.X >= _overlayLeft - 10 && point.Y >= _overlayTop - 10
            && point.X <= _overlayRight + 10 && point.Y <= _overlayBottom + 10;
    }

    /// <summary>Refresh the cached overlay rect. UI thread only.</summary>
    private void CacheOverlayBounds()
    {
        var overlay = _calibrationOverlay;
        if (overlay is null)
        {
            InvalidateOverlayBounds();
            return;
        }

        var position = overlay.Position;
        var width = (int)Math.Ceiling(overlay.Bounds.Width);
        var height = (int)Math.Ceiling(overlay.Bounds.Height);
        if (width <= 0 || height <= 0)
        {
            // Layout has not completed yet; refresh once it has.
            Dispatcher.UIThread.Post(CacheOverlayBounds, DispatcherPriority.Loaded);
            return;
        }

        _overlayLeft = position.X;
        _overlayTop = position.Y;
        _overlayRight = position.X + width;
        _overlayBottom = position.Y + height;
    }

    private void InvalidateOverlayBounds()
    {
        _overlayLeft = int.MinValue;
        _overlayTop = int.MinValue;
        _overlayRight = int.MinValue;
        _overlayBottom = int.MinValue;
    }

    private static int RequiredCalibrationPoints(ColorPickerCalibrationMode mode)
        => PickerModeId(mode) is { } pickerId && PickerRegistry.Find(pickerId) is { } definition
            ? definition.Points.Count
            : 1;

    private void UpdateGuidanceStep(int completed)
    {
        var overlay = _calibrationOverlay;
        if (overlay is null) return;
        var total = RequiredCalibrationPoints(_calibrationMode);
        var next = completed >= total ? "..." : CalibrationStepText(completed);
        overlay.SetStep(completed, total, next, LocalizationManager.T("calib_title"));
        CacheOverlayBounds();
    }

    /// <summary>User pressed Cancel / ESC on the guide window: abort the capture without saving.</summary>
    private void CancelColorPickerCalibration(object? sender, EventArgs e)
    {
        if (!_capturingColorPicker) return;
        Input.taskHook.MouseClicked -= CaptureColorPickerPoint;
        _capturingColorPicker = false;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        });
        _calibrationOverlay?.Close();
        _calibrationOverlay = null;
        Utils.Log("Color picker calibration cancelled.");
    }

    private void CaptureColorPickerPoint(object? sender, MouseHookEventArgs e)
    {
        if (e.Data.Button != SharpHook.Native.MouseButton.Button1) return;
        var clickPoint = new Vector2(e.Data.X, e.Data.Y);
        if (IsClickInsideOverlay(clickPoint)) return;

        _colorPickerCalibrationPoints.Add(clickPoint);
        var completed = _colorPickerCalibrationPoints.Count;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateGuidanceStep(completed));

        var requiredPoints = RequiredCalibrationPoints(_calibrationMode);
        if (_colorPickerCalibrationPoints.Count < requiredPoints) return;

        var points = _colorPickerCalibrationPoints.ToArray();
        var saved = true;
        string? error = null;

        if (PickerModeId(_calibrationMode) is { } pickerId && PickerRegistry.Find(pickerId) is { } definition)
        {
            // The capture order is the picker's own declared order, so a new picker definition
            // needs zero changes to this method.
            var calibration = new PickerCalibration(definition.Id);
            for (var index = 0; index < definition.Points.Count; index++)
                calibration.Set(definition.Points[index].Key, ToScreenPoint(points[index]));

            saved = ColorPicker.TrySaveCalibration(calibration, out error);
        }
        else
        {
            SaveRecreatePoint(config => config.RecreateCurrentColorPoint = ToScreenPoint(points[0]));
        }

        FinishColorPickerCalibration(saved, error);
    }

    private static ScreenPoint ToScreenPoint(Vector2 point)
        => new((int)Math.Round(point.X), (int)Math.Round(point.Y));

    private static void SaveRecreatePoint(Action<AutoDrawConfig> update)
    {
        var config = DdConfigService.Current;
        update(config);
        if (!DdConfigService.TryApply(config, out var error))
            Utils.Log("Recreate calibration rejected: " + error);
    }

    /// <summary>
    /// Closes the guide window and reports the result.  A rejected calibration (a collapsed
    /// wheel, a zero-length hue strip, an off-screen point) keeps the previous one intact and
    /// tells the user exactly what to re-capture.
    /// </summary>
    private void FinishColorPickerCalibration(bool saved, string? error)
    {
        Input.taskHook.MouseClicked -= CaptureColorPickerPoint;
        _capturingColorPicker = false;
        Dispatcher.UIThread.Invoke(() =>
        {
            _calibrationOverlay?.Close();
            _calibrationOverlay = null;
            WindowState = WindowState.Normal;
            UpdateColorPickerModeStatus();

            if (!saved)
            {
                new MessageBox().ShowMessageBox(
                    LocalizationManager.T("calib_rejected_title"),
                    LocalizationManager.F(
                        "calib_rejected_body",
                        error ?? LocalizationManager.T("calib_rejected_unknown")));
                return;
            }

            var message = _calibrationMode switch
            {
                ColorPickerCalibrationMode.Wheel => LocalizationManager.T("calib_saved_wheel"),
                ColorPickerCalibrationMode.BoxAll => LocalizationManager.T("calib_saved_box"),
                _ => LocalizationManager.T("calib_saved_current")
            };
            new MessageBox().ShowMessageBox(LocalizationManager.T("calib_saved_title"), message);
        });
    }

    private void UpdateColorPickerModeStatus()
    {
        var config = DdConfigService.Current;
        var definition = PickerRegistry.Find(config.PickerType);
        var label = definition is null
            ? LocalizationManager.T("picker_status_unknown")
            : LocalizationManager.F("picker_status", LocalizationManager.T(definition.DisplayNameKey));

        if (!ColorPicker.ConfiguredTypeIsReady())
            label += " " + LocalizationManager.T("picker_status_needs_setup");

        ColorPickerModeLabel.Content = label;
    }

    private void ShowColorPickerHelp(object? sender, RoutedEventArgs e)
    {
        new MessageBox().ShowMessageBox("Color Picker Setup", ColorPicker.GetUsageInstructions());
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.V && (e.KeyModifiers & KeyModifiers.Control) != 0 && !IsTextInputFocused())
        {
            e.Handled = true;
            _ = PasteImageFromClipboardAsync();
        }

        base.OnKeyDown(e);
    }

    private bool IsTextInputFocused()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return focused is TextBox;
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
        if (Drawing.IsDrawing)
        {
            ReportImageInputStatus("status_error_drawing_active");
            return;
        }

        var decoded = DecodeImage(path, img);
        if (decoded is null)
        {
            ReportImageInputStatus("status_import_failed", Path.GetFileName(path ?? LocalizationManager.T("clipboard_image")));
            return;
        }

        ApplySourceBitmap(PrepareSource(decoded), string.IsNullOrWhiteSpace(path) ? null : path);
    }

    /// <summary>Async import used by picker, clipboard and drop paths. Decoding, normalizing and
    /// the preview PNG encode all run on the thread pool so large images never stall the UI.</summary>
    public async Task<bool> ImportImageAsync(string? path, byte[]? img = null, CancellationToken cancellationToken = default)
    {
        if (Drawing.IsDrawing)
        {
            ReportImageInputStatus("status_error_drawing_active");
            return false;
        }

        PreparedSource? prepared;
        try
        {
            prepared = await Task.Run(
                () => DecodeImage(path, img) is { } decoded ? PrepareSource(decoded) : null,
                cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Utils.Log("Image preparation failed: " + ex.Message);
            prepared = null;
        }

        if (prepared is null)
        {
            ReportImageInputStatus("status_import_failed", Path.GetFileName(path ?? LocalizationManager.T("clipboard_image")));
            return false;
        }

        ApplySourceBitmap(prepared, string.IsNullOrWhiteSpace(path) ? null : path);
        return true;
    }

    private static SKBitmap? DecodeImage(string? path, byte[]? img)
    {
        try
        {
            if (img is { Length: > 0 }) return SKBitmap.Decode(img);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            return SKBitmap.Decode(stream);
        }
        catch (Exception ex)
        {
            Utils.Log("Image decode failed: " + ex.Message);
            return null;
        }
    }

    /// <summary>Source bitmaps prepared off the UI thread. <see cref="PrepareSource"/> takes
    /// ownership of the decoded bitmap.</summary>
    private sealed record PreparedSource(SKBitmap Raw, SKBitmap PreFx, Bitmap Display);

    private static PreparedSource PrepareSource(SKBitmap decoded)
    {
        var normalized = decoded.NormalizeColor();
        if (!ReferenceEquals(normalized, decoded)) decoded.Dispose();

        SKBitmap? preFx = null;
        Bitmap? display = null;
        try
        {
            preFx = normalized.Copy();
            display = normalized.ConvertToAvaloniaBitmap();
            return new PreparedSource(normalized, preFx, display);
        }
        catch
        {
            preFx?.Dispose();
            display?.Dispose();
            normalized.Dispose();
            throw;
        }
    }

    /// <summary>Replaces the current source image for every input path. The existing image is only
    /// disposed once the replacement decoded successfully, so a bad paste/drop never blanks the
    /// preview.</summary>
    private void ApplySourceBitmap(PreparedSource prepared, string? path)
    {
        foreach (var layer in LayersContext)
        {
            layer.RawBitmap?.Dispose();
        }
        LayersContext.Clear();
        _layersStack.Clear();

        _rawBitmap?.Dispose();
        _rawBitmap = prepared.Raw;
        _preFxBitmap?.Dispose();
        _preFxBitmap = prepared.PreFx;
        _displayedBitmap?.Dispose();
        _displayedBitmap = prepared.Display;
        _processedBitmap?.Dispose();
        _processedBitmap = null;
        ImagePreview.Image = _displayedBitmap;

        _hasImportedImage = true;
        ImportedImagePath = path;
        _imageEnhanced = false;
        _enhanceFailed = false;
        _quantizeGeneration++;
        QuantizedPreviewInvalidated?.Invoke(this, EventArgs.Empty);
        QuantizedPreview?.Dispose();
        QuantizedPreview = null;
        QuantizedPreviewChanged?.Invoke(this, EventArgs.Empty);
        Drawing.ResetProgress();

        _inChange = true;
        SizeSlider.Value = 100;

        PercentageNumber.Text = $"{Math.Round(SizeSlider.Value)}%";
        WidthInput.Text = widthLock > 0 ? widthLock.ToString() : _displayedBitmap.Size.Width.ToString();
        HeightInput.Text =  heightLock > 0 ? heightLock.ToString() : _displayedBitmap.Size.Height.ToString();
        _inChange = false;
        
        if (widthLock > 0 || heightLock > 0) ResizeImage(_displayedBitmap.Size.Width, _displayedBitmap.Size.Height);

        ReportOversizedImageIfNeeded();
    }

    /// <summary>Imports never crop. When the selected drawing area is smaller than the image, say
    /// so and leave scaling to the resize/fit controls.</summary>
    private void ReportOversizedImageIfNeeded()
    {
        if (_rawBitmap is null) return;
        var config = DdConfigService.Current;
        if (!config.AreaSelected) return;
        if (RegionCaptureMath.ExceedsSurface(_rawBitmap.Width, _rawBitmap.Height, config.DrawRegion))
            ReportImageInputStatus("status_image_larger_than_region");
    }

    private async void OpenButtonOnClick(object? sender, RoutedEventArgs e)
    {
        // Replacing the source bitmaps while the drawing engine still holds them (even paused)
        // would dispose memory it is about to reuse when the run resumes.
        if (Drawing.IsDrawing)
        {
            ReportImageInputStatus("status_error_drawing_active");
            return;
        }

        var file = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.T("btn_select_image"),
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll },
            AllowMultiple = false
        });

        if (file.Count == 1)
        {
            var path = file[0].TryGetLocalPath();
            if (await ImportImageAsync(path))
                ReportImageInputStatus("status_import_loaded", Path.GetFileName(path ?? string.Empty));
        }
    }

    private void RunButtonOnClick(object? sender, RoutedEventArgs e)
    {
        _layersStack.Clear();
        if (LayersContext != null && LayersContext.Count > 0)
        {
            foreach (var layer in LayersContext)
            {
                if (layer.Enabled)
                {
                    _layersStack.Add(new ColorLayerResult
                    {
                        Bitmap = layer.RawBitmap,
                        Color = layer.Color,
                        PixelCount = layer.PixelCount
                    });
                }
            }
        }

        if (_processedBitmap == null && _layersStack.Count == 0)
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
        Drawing.ClipToRegion = false;

        if (_layersStack.Count > 0)
        {
            new Preview().ReadyStackDraw(_preFxBitmap, _layersStack, _actionStack);
        }
        else
        {
            new Preview().ReadyDraw(_processedBitmap);
        }
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
        foreach (var layer in LayersContext)
        {
            layer.RawBitmap?.Dispose();
        }
        LayersContext.Clear();
        _layersStack.Clear();

        _rawBitmap?.Dispose();
        _preFxBitmap?.Dispose();
        _processedBitmap?.Dispose();
        _displayedBitmap?.Dispose();
        _rawBitmap = new SKBitmap(318, 318, true);
        _preFxBitmap = new SKBitmap(318, 318, true);
        _processedBitmap = null;
        _displayedBitmap = null;
        _hasImportedImage = false;
        ImportedImagePath = null;
        _imageEnhanced = false;
        _enhanceFailed = false;
        _quantizeGeneration++;
        QuantizedPreviewInvalidated?.Invoke(this, EventArgs.Empty);
        QuantizedPreview?.Dispose();
        QuantizedPreview = null;
        QuantizedPreviewChanged?.Invoke(this, EventArgs.Empty);
        Drawing.ResetProgress();
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
        foreach (var layer in LayersContext)
        {
            layer.RawBitmap?.Dispose();
        }
        LayersContext.Clear();
        _layersStack.Clear();

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

    private void InitializeImageInputUi()
    {
        ImageInputTab.Header = LocalizationManager.T("actions");
        ActionsHeaderText.Content = LocalizationManager.T("actions");
        ActionImportButton.Content = LocalizationManager.T("action_import");
        ActionPasteButton.Content = LocalizationManager.T("action_paste");
        ActionCaptureButton.Content = LocalizationManager.T("action_capture_region");
        MonitorSelectLabel.Content = LocalizationManager.T("monitor_select");
        DropHintText.Text = LocalizationManager.T("drop_hint");
        DropHighlightText.Text = LocalizationManager.T("drop_hint");

        ActionImportButton.Click += OpenButtonOnClick;
        ActionPasteButton.Click += PasteImage_OnClick;
        ActionCaptureButton.Click += async (_, _) => await CaptureRegionToImageAsync();
        MonitorCombo.SelectionChanged += MonitorCombo_OnSelectionChanged;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnImageDragOver);
        AddHandler(DragDrop.DragOverEvent, OnImageDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnImageDragLeave);
        AddHandler(DragDrop.DropEvent, OnImageDrop);

        Screens.Changed += (_, _) => Dispatcher.UIThread.Post(RefreshMonitorList);
        RefreshMonitorList();
        ReportImageInputStatus("ready");
    }

    // ---------------------------------------------------------------- image input actions

    private async void PasteImage_OnClick(object? sender, RoutedEventArgs e) => await PasteImageFromClipboardAsync();

    /// <summary>Ctrl+V / Paste menu entry. Accepts a clipboard file reference or raw clipboard
    /// pixels; failures are reported through the non-modal status label instead of a dialog.</summary>
    public async Task PasteImageFromClipboardAsync()
    {
        if (Drawing.IsDrawing)
        {
            ReportImageInputStatus("status_error_drawing_active");
            return;
        }

        var payload = await ClipboardImageReader.ReadAsync(Clipboard);
        await ImportPayloadAsync(payload, fromClipboard: true);
    }

    private async Task<bool> ImportPayloadAsync(ClipboardPayload payload, bool fromClipboard)
    {
        switch (payload.Kind)
        {
            case ClipboardPayloadKind.FileReference:
                if (!ClipboardPayloadClassifier.IsDroppableFile(payload.Path))
                {
                    ReportImageInputStatus(fromClipboard ? "status_clipboard_no_image_file" : "status_drop_invalid");
                    return false;
                }
                if (!await ImportImageAsync(payload.Path)) return false;
                ReportImageInputStatus(fromClipboard ? "status_pasted_image" : "status_import_loaded", Path.GetFileName(payload.Path));
                return true;

            case ClipboardPayloadKind.EncodedImage:
                if (!await ImportImageAsync(null, payload.EncodedBytes)) return false;
                ReportImageInputStatus(fromClipboard ? "status_pasted_image" : "status_import_loaded", LocalizationManager.T("clipboard_image"));
                return true;

            default:
                ReportImageInputStatus(fromClipboard ? "status_clipboard_no_image" : "status_drop_invalid");
                return false;
        }
    }

    private void OnImageDragOver(object? sender, DragEventArgs e)
    {
        var valid = HasDroppableImage(e.Data);
        e.DragEffects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        DropHighlight.IsVisible = valid;
        e.Handled = true;
    }

    private void OnImageDragLeave(object? sender, DragEventArgs e)
    {
        DropHighlight.IsVisible = false;
        e.Handled = true;
    }

    private async void OnImageDrop(object? sender, DragEventArgs e)
    {
        DropHighlight.IsVisible = false;
        e.Handled = true;

        if (Drawing.IsDrawing)
        {
            ReportImageInputStatus("status_error_drawing_active");
            return;
        }

        // Read everything before awaiting: drop data becomes invalid once this handler returns.
        var payload = ReadDropPayload(e.Data);
        await ImportPayloadAsync(payload, fromClipboard: false);
    }

    private static bool HasDroppableImage(IDataObject data)
    {
        if (data.Contains("PNG") || data.Contains("image/png") || data.Contains("Bitmap")) return true;
        return FirstSupportedDropPath(data) is not null;
    }

    private static string? FirstSupportedDropPath(IDataObject data)
    {
        var files = data.GetFiles();
        if (files is not null)
        {
            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();
                if (ClipboardPayloadClassifier.IsDroppableFile(path)) return path;
            }
        }

        var names = GetFileNamesCompat(data);
        if (names is not null)
        {
            foreach (var name in names)
                if (ClipboardPayloadClassifier.IsDroppableFile(name)) return name;
        }

        return null;
    }

    private static IEnumerable<string>? GetFileNamesCompat(IDataObject data)
    {
        // FileNames is desktop-only and deprecated in Avalonia 11, but some drag sources only
        // expose it. GetFiles is preferred above.
#pragma warning disable CS0618
        return data.GetFileNames();
#pragma warning restore CS0618
    }

    private static ClipboardPayload ReadDropPayload(IDataObject data)
    {
        var path = FirstSupportedDropPath(data);
        if (path is not null) return ClipboardPayload.FromFile(path);

        foreach (var format in new[] { "PNG", "image/png", "Bitmap" })
        {
            var bytes = ClipboardImageReader.TryExtractEncodedBytes(data.Get(format));
            if (bytes is { Length: > 0 }) return ClipboardPayload.FromBytes(bytes);
        }

        return ClipboardPayload.None;
    }

    /// <summary>F1 region capture: dim the selected monitor, drag a rectangle, store it as
    /// <c>draw_region</c>, and use the captured pixels as the source image. Esc and zero-area
    /// drags leave the previous image untouched.</summary>
    public async Task CaptureRegionToImageAsync()
    {
        if (_capturingRegion || _capturingColorPicker) return;
        if (Drawing.IsDrawing && !Drawing.IsPaused)
        {
            ReportImageInputStatus("status_error_drawing_active");
            return;
        }

        _capturingRegion = true;
        try
        {
            RefreshMonitorList();
            var config = DdConfigService.Current;
            var monitor = MonitorCatalog.Resolve(_monitors, config.MonitorIndex, out var fellBack);
            if (monitor is null)
            {
                ReportImageInputStatus("monitor_none");
                return;
            }

            if (fellBack)
            {
                config.MonitorIndex = monitor.Value.Index;
                if (!DdConfigService.TryApply(config, out var fallbackError, allowWhilePaused: true))
                    Utils.Log("Monitor fallback could not be saved: " + fallbackError);
                ReportImageInputStatus("status_monitor_missing", monitor.Value.Index);
            }

            if (monitor.Value.Index < 0 || monitor.Value.Index >= _monitorScreens.Count)
            {
                ReportImageInputStatus("monitor_none");
                return;
            }

            var screen = _monitorScreens[monitor.Value.Index];
            var rect = await AreaSelector.SelectAsync(this, screen);
            if (rect is null)
            {
                ReportImageInputStatus("status_area_selection_cancel");
                return;
            }

            if (!RegionCaptureMath.IsRegionInside(rect.Value, monitor.Value))
            {
                ReportImageInputStatus("status_region_failed");
                return;
            }

            if (!ApplyCapturedRegion(rect.Value, monitor.Value.Index)) return;

            // Replacing the layer bitmaps while the drawing engine still holds them would crash
            // the paused run, so a paused capture only updates the region.
            if (Drawing.IsDrawing)
            {
                ReportImageInputStatus("status_region_not_replaced");
                return;
            }

            var prepared = await Task.Run(() =>
                ScreenCapture.Capture(rect.Value) is { } capture ? PrepareSource(capture) : null);
            if (prepared is null)
            {
                ReportImageInputStatus("status_region_failed");
                return;
            }

            ApplySourceBitmap(prepared, null);
            ReportImageInputStatus("status_region_captured", rect.Value.Left, rect.Value.Top, rect.Value.Width, rect.Value.Height);
        }
        catch (Exception ex)
        {
            Utils.Log("Region capture failed: " + ex);
            ReportImageInputStatus("status_region_failed");
        }
        finally
        {
            _capturingRegion = false;
        }
    }

    private bool ApplyCapturedRegion(ScreenRect region, int monitorIndex)
    {
        var config = DdConfigService.Current;
        config.DrawRegion = region;
        config.MonitorIndex = monitorIndex;
        config.AreaSelected = true;
        if (!DdConfigService.TryApply(config, out var error, allowWhilePaused: true))
        {
            ReportImageInputStatus("status_area_error", error);
            return false;
        }

        Drawing.LastPos = new Vector2(region.Left, region.Top);
        Drawing.ClipToRegion = true;
        return true;
    }

    private void RefreshMonitorList()
    {
        var previous = DdConfigService.Current.MonitorIndex;
        var pairs = Screens.All
            .Select(screen => (Screen: screen, Info: new MonitorInfo(
                0,
                screen.Bounds.X,
                screen.Bounds.Y,
                screen.Bounds.Width,
                screen.Bounds.Height,
                screen.Scaling,
                screen.IsPrimary)))
            .ToList();

        var ordered = MonitorCatalog.Order(pairs.Select(pair => pair.Info));
        _monitors.Clear();
        _monitorScreens.Clear();
        foreach (var info in ordered)
        {
            var match = pairs.FirstOrDefault(pair =>
                pair.Info.Left == info.Left && pair.Info.Top == info.Top &&
                pair.Info.Width == info.Width && pair.Info.Height == info.Height);
            if (match.Screen is null) continue;
            _monitors.Add(info);
            _monitorScreens.Add(match.Screen);
        }

        _syncingMonitorList = true;
        MonitorCombo.ItemsSource = _monitors.Select(FormatMonitorLabel).ToList();
        var resolved = MonitorCatalog.Resolve(_monitors, previous, out _);
        MonitorCombo.SelectedIndex = resolved?.Index ?? -1;
        _syncingMonitorList = false;
    }

    private static string FormatMonitorLabel(MonitorInfo monitor) =>
        LocalizationManager.F(
            "monitor_label",
            monitor.Index,
            monitor.Width,
            monitor.Height,
            Math.Round(monitor.Scaling * 100));

    private void SyncMonitorSelection(int monitorIndex)
    {
        if (_monitors.Count == 0) return;
        var resolved = MonitorCatalog.Resolve(_monitors, monitorIndex, out _);
        _syncingMonitorList = true;
        MonitorCombo.SelectedIndex = resolved?.Index ?? -1;
        _syncingMonitorList = false;
    }

    private void MonitorCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingMonitorList || _inChange || Design.IsDesignMode) return;
        var index = MonitorCombo.SelectedIndex;
        if (index < 0 || index >= _monitors.Count) return;

        var config = DdConfigService.Current;
        if (config.MonitorIndex == index) return;
        config.MonitorIndex = index;
        if (!DdConfigService.TryApply(config, out var error))
        {
            SyncMonitorSelection(config.MonitorIndex);
            ReportImageInputStatus("status_error_drawing_active");
            Utils.Log("Monitor change rejected: " + error);
            return;
        }

        var monitor = _monitors[index];
        ReportImageInputStatus("status_monitor_selected", index, monitor.Width, monitor.Height);
    }

    /// <summary>Single place that pushes image-input feedback to the Actions panel (and the
    /// HexedDrawer status bar when it is open) so every input path is localized and non-fatal.</summary>
    private void ReportImageInputStatus(string key, params object?[] args)
    {
        var text = args.Length == 0 ? LocalizationManager.T(key) : LocalizationManager.F(key, args);
        Utils.Log(text);

        void Apply()
        {
            ImageInputStatusText.Text = text;
            _hexedDrawWindow?.SetStatus(text);
        }

        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }

    // Legacy entry point kept for the Automation menu / external callers.
    public async void PasteControl() => await PasteImageFromClipboardAsync();

    public async void SetConfigFolderViaDialog(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        if (folder.Count != 1) return;
        Config.SetEntry("ConfigFolder", folder[0].TryGetLocalPath());
        RefreshConfigList(this, null);
    }

    public void LoadConfig(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            if (!DdConfigService.TryLoad(path, out var ddConfig, out var error) || !DdConfigService.TryApply(ddConfig!, out error))
                new MessageBox().ShowMessageBox("Invalid dd.json", error, "error");
            else { SelectedConfigLabel.Content = $"dd.json - {Path.GetFileName(path)}"; ApplyDdConfigToUi(); }
            return;
        }
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
            FileTypeChoices = new[] { ConfigsFileFilter, DdConfigFileFilter }
        });

        if (file is not null)
        {
            if (Path.GetExtension(file.TryGetLocalPath()).Equals(".json", StringComparison.OrdinalIgnoreCase)) { DdConfigService.Export(file.TryGetLocalPath()); return; }
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
            FileTypeFilter = new[] { ConfigsFileFilter, DdConfigFileFilter },
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

    private void ApplyDdConfigToUi()
    {
        var config = DdConfigService.Current;
        _inChange = true;
        DrawIntervalElement.Text = config.Interval.ToString(CultureInfo.InvariantCulture);
        ClickDelayElement.Text = Math.Round(config.ClickDelay * 1000).ToString(CultureInfo.InvariantCulture);
        AlgorithmSelection.SelectedIndex = AlgorithmIndexFromName(config.Algorithm);
        PaletteSizeInput.Text = config.KClusters.ToString(CultureInfo.InvariantCulture);
        _inChange = false;
        if (config.LockShape) { widthLock = config.CustomWidth; heightLock = config.CustomHeight; WidthInput.Text = config.CustomWidth.ToString(); HeightInput.Text = config.CustomHeight.ToString(); }
        Drawing.LastPos = new Vector2(config.DrawRegion.Left, config.DrawRegion.Top);
        SyncMonitorSelection(config.MonitorIndex);
        UpdateColorPickerModeStatus();
    }

    /// <summary>Maps a persisted algorithm name onto the AlgorithmSelection ComboBox index.
    /// Display labels differ from config names ("Edge-Following" vs "Edge Traversal"), so the
    /// config name is the single source of truth here.</summary>
    private static int AlgorithmIndexFromName(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "edge traversal" or "edge-following" => 1,
        "concentric" => 2,
        _ => 0
    };

    /// <summary>Writes the algorithm choice back into the shared config so it persists and
    /// round-trips through exported dd.json files.</summary>
    private void AlgorithmSelectionOnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_inChange || Design.IsDesignMode) return;
        var name = AlgorithmSelection.SelectedIndex switch
        {
            1 => "Edge Traversal",
            2 => "Concentric",
            _ => "DFS (Default)"
        };
        var config = DdConfigService.Current;
        if (config.Algorithm.Equals(name, StringComparison.OrdinalIgnoreCase)) return;
        config.Algorithm = name;
        if (!DdConfigService.TryApply(config, out var error))
            Utils.Log("Algorithm config write-back rejected: " + error);
    }
    
    // Actions
    ActionPrompt _actionPrompt = new();
    
    public void ClickActionObject(InputAction Action)
    {
        // TODO: add action prompt stuff lol
        Console.WriteLine("Yellow");
    }

    public void AddAction()
    {
        if (!_actionPrompt.IsActive) _actionPrompt = new();
        _actionPrompt.Show();
        _actionPrompt.Callback = _addActionCallback;
        Console.WriteLine("Black");
    }

    public void _addActionCallback()
    {
        Console.WriteLine("Recv Message!");
        if (_actionPrompt.Action is not null) // Empty the compartments of your pantaloons.
        {
            Console.WriteLine("+1 Crime Level");
            Console.WriteLine(_actionPrompt.Action);
            _actionStack.Add(_actionPrompt.Action);
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(UpdateActionsContext);
        }
        // Mothership: "Fuck you." *obliterates your ass*
        _actionPrompt.Close();
    }

    private void UpdateActionsContext()
    {
        if (ActionsContext is null) return;

        // Clear the existing items in ActionsContext
        ActionsContext.Clear();

        // Populate ActionsContext with relevant data from _actionStack
        foreach (var action in _actionStack)
        {
            var _ActionType = "";
            if (action.Action == InputAction.ActionType.KeyDown) _ActionType = "Key Down";
            else if (action.Action == InputAction.ActionType.KeyUp) _ActionType = "Key Up";
            else if (action.Action == InputAction.ActionType.LeftClick) _ActionType = "Left Click";
            else if (action.Action == InputAction.ActionType.RightClick) _ActionType = "Right Click";
            else if (action.Action == InputAction.ActionType.WriteString) _ActionType = "Write String";
            else if (action.Action == InputAction.ActionType.MoveTo) _ActionType = "Move to";
            else if (action.Action == InputAction.ActionType.SetColorHex) _ActionType = "Set color hex";
            
            var _ActionData = action.Data is null ? $" @ x={action.Position.Value.X}, y={action.Position.Value.Y}" : $" - {action.Data}";
            
            var actionDisp = new ActionDisp
            { 
                Text = $"{_ActionType}{_ActionData}",
                boundAction = action,
            };

            ActionsContext.Add(actionDisp);
        }
    }

    public void GenerateLayersButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_preFxBitmap == null || _preFxBitmap.Width <= 1 || _preFxBitmap.Height <= 1)
        {
            new MessageBox().ShowMessageBox("Error!", "Please import an image first.", "error");
            return;
        }

        var matches = Regex.Matches(HexColorsInput.Text ?? "", @"#?([a-fA-F0-9]{6})");
        var targetColors = new List<SKColor>();
        var seenColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in matches)
        {
            var hex = match.Groups[1].Value.ToUpper();
            if (seenColors.Contains(hex)) continue;
            seenColors.Add(hex);
            
            try
            {
                byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                targetColors.Add(new SKColor(r, g, b));
            }
            catch { }
        }

        if (targetColors.Count == 0)
        {
            new MessageBox().ShowMessageBox("Error!", "Please enter at least one valid hex color.", "error");
            return;
        }

        using var prepared = ImageProcessing.PrepareColorImage(_preFxBitmap, DdConfigService.Current);
        PopulateLayers(ImageProcessing.SplitImageByColors(prepared, targetColors, (byte)_alphaThresh, DdConfigService.Current));
    }

    public void AutoGenerateLayersButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_preFxBitmap == null || _preFxBitmap.Width <= 1 || _preFxBitmap.Height <= 1)
        {
            new MessageBox().ShowMessageBox("Error!", "Please import an image first.", "error");
            return;
        }

        if (!int.TryParse(PaletteSizeInput.Text, out var paletteSize) || paletteSize is < 1 or > 64)
        {
            new MessageBox().ShowMessageBox("Error!", "Palette size must be a number from 1 to 64.", "error");
            return;
        }

        var config = DdConfigService.Current;
        using var prepared = ImageProcessing.PrepareColorImage(_preFxBitmap, config);
        var targetColors = ImageProcessing.ExtractPalette(prepared, paletteSize, (byte)_alphaThresh, config);
        if (targetColors.Count == 0)
        {
            new MessageBox().ShowMessageBox("Warning", "No opaque pixels were found in the image.", "warning");
            return;
        }

        PopulateLayers(ImageProcessing.SplitImageByColors(prepared, targetColors, (byte)_alphaThresh, config));
    }

    private void PopulateLayers(List<ColorLayerResult> splitResults, bool showWarnings = true)
    {
        foreach (var layer in LayersContext)
        {
            layer.RawBitmap?.Dispose();
        }
        LayersContext.Clear();
        _layersStack.Clear();

        foreach (var res in splitResults)
        {
            if (res.PixelCount == 0)
            {
                // Discard empty layers
                res.Bitmap.Dispose();
                continue;
            }

            var hexStr = $"#{res.Color.Red:X2}{res.Color.Green:X2}{res.Color.Blue:X2}";
            var color = Color.FromRgb(res.Color.Red, res.Color.Green, res.Color.Blue);
            var avaloniaBrush = new SolidColorBrush(color);
            var thumbnail = res.Bitmap.ConvertToAvaloniaBitmap();

            LayersContext.Add(new LayerDisp
            {
                HexColor = hexStr,
                HexColorBrush = avaloniaBrush,
                Thumbnail = thumbnail,
                RawBitmap = res.Bitmap,
                Color = res.Color,
                PixelCount = res.PixelCount
            });
        }

        if (LayersContext.Count == 0 && showWarnings)
        {
            new MessageBox().ShowMessageBox("Warning", "No pixels matched the specified hex colors.", "warning");
        }
    }

    // ------------------------------------------------------------------
    // HexedDrawer integration API used by HexedDrawWindow and global hotkeys.
    // ------------------------------------------------------------------

    private void HexedDrawMenu_OnClick(object? sender, RoutedEventArgs e) => OpenHexedDrawWindow();

    public void OpenHexedDrawWindow()
    {        if (_hexedDrawWindow is { IsVisible: true })
        {
            _hexedDrawWindow.Activate();
            return;
        }
        _hexedDrawWindow = new HexedDrawWindow();
        _hexedDrawWindow.Closed += (_, _) => _hexedDrawWindow = null;
        _hexedDrawWindow.Show(this);
    }

    private void HexedStatus(string key, params object?[] args)
    {
        var text = args.Length == 0 ? LocalizationManager.T(key) : LocalizationManager.F(key, args);
        Utils.Log(text);
        Dispatcher.UIThread.Post(() => _hexedDrawWindow?.SetStatus(text));
    }

    public async Task SelectHexedDrawImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.T("dialog_select_image"),
            AllowMultiple = false,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
        });
        if (files.Count != 1) return;
        var path = files[0].TryGetLocalPath();
        if (path is null) return;
        ImportImage(path);
        HexedStatus("status_loaded_image", Path.GetFileName(path));
    }

    public async Task SelectDrawAreaAsync()
    {
        try
        {
            var rect = await AreaSelector.SelectAsync(this);
            if (rect is null)
            {
                HexedStatus("status_area_selection_cancel");
                return;
            }
            var config = DdConfigService.Current;
            config.DrawRegion = rect.Value;
            config.AreaSelected = true;
            if (!DdConfigService.TryApply(config, out var error))
            {
                HexedStatus("status_area_error", error);
                return;
            }
            Drawing.LastPos = new Vector2(rect.Value.Left, rect.Value.Top);
            Drawing.ClipToRegion = true;
            HexedStatus("status_area_selected", rect.Value.Left, rect.Value.Top, rect.Value.Width, rect.Value.Height);
        }
        catch (Exception ex)
        {
            HexedStatus("status_area_selection_error", ex.Message);
        }
    }

    public async Task QuantizeForHexedDrawAsync()
    {
        if (!HasImportedImage)
        {
            HexedStatus("status_error_no_area_image");
            return;
        }
        if (Drawing.IsDrawing)
        {
            HexedStatus("status_error_drawing_active");
            return;
        }
        if (_quantizing) return;
        _quantizing = true;
        var generation = ++_quantizeGeneration;
        try
        {
            var config = DdConfigService.Current;
            HexedStatus("status_processing_image");

            if (config.EnhanceImage && !_imageEnhanced && !_enhanceFailed)
            {
                HexedStatus("status_enhancing_image");
                try
                {
                    var enhanced = await ImageEnhancer.EnhanceAsync(_preFxBitmap!);
                    if (generation != _quantizeGeneration)
                    {
                        enhanced.Dispose();
                        return;
                    }
                    _preFxBitmap?.Dispose();
                    _preFxBitmap = enhanced;
                    _displayedBitmap?.Dispose();
                    _displayedBitmap = enhanced.ConvertToAvaloniaBitmap();
                    ImagePreview.Image = _displayedBitmap;
                    _imageEnhanced = true;
                }
                catch (Exception ex)
                {
                    _enhanceFailed = true;
                    Utils.Log("Image enhancement failed: " + ex.Message);
                    HexedStatus("status_esrgan_fail");
                }
            }

            var paletteSize = Math.Clamp(config.KClusters, 1, 256);
            List<ColorLayerResult>? layers = null;
            SKBitmap? composite = null;
            using var source = _preFxBitmap!.Copy();
            await Task.Run(() =>
            {
                using var prepared = ImageProcessing.PrepareColorImage(source, config);
                var colors = ImageProcessing.ExtractPalette(prepared, paletteSize, (byte)_alphaThresh, config);
                if (colors.Count == 0) throw new InvalidOperationException(LocalizationManager.T("status_image_transparent"));

                if (config.Mode.Equals("bw", StringComparison.OrdinalIgnoreCase))
                {
                    using var gray = ImageProcessing.ToGrayscale(prepared);
                    colors = colors.Select(color =>
                    {
                        var luma = (byte)Math.Clamp((int)Math.Round(color.Red * .2126 + color.Green * .7152 + color.Blue * .0722), 0, 255);
                        return new SKColor(luma, luma, luma);
                    }).Distinct().ToList();
                    layers = ImageProcessing.SplitImageByColors(gray, colors, (byte)_alphaThresh, config);
                    composite = ImageProcessing.BuildComposite(layers, gray.Width, gray.Height);
                }
                else
                {
                    layers = ImageProcessing.SplitImageByColors(prepared, colors, (byte)_alphaThresh, config);
                    composite = ImageProcessing.BuildComposite(layers, prepared.Width, prepared.Height);
                }
            });

            if (generation != _quantizeGeneration)
            {
                if (layers is not null) foreach (var layer in layers) layer.Bitmap.Dispose();
                composite?.Dispose();
                return;
            }

            if (layers is null || composite is null) return;
            PopulateLayers(layers, showWarnings: false);
            if (LayersContext.Count == 0)
            {
                composite.Dispose();
                HexedStatus("status_image_transparent");
                return;
            }
            QuantizedPreviewInvalidated?.Invoke(this, EventArgs.Empty);
            QuantizedPreview?.Dispose();
            QuantizedPreview = composite.ConvertToAvaloniaBitmap();
            composite.Dispose();
            QuantizedPreviewChanged?.Invoke(this, EventArgs.Empty);
            HexedStatus("status_image_processed");
        }
        catch (Exception ex)
        {
            Utils.Log("Quantize failed: " + ex);
            HexedStatus("status_processing_error", ex.Message);
        }
        finally
        {
            _quantizing = false;
        }
    }

    public async Task StartPauseHexedDrawAsync()
    {
        if (_recreateRunning)
        {
            if (_recreatePause is { } pause)
            {
                pause.IsPaused = !pause.IsPaused;
                Drawing.IsPaused = pause.IsPaused;
            }
            HexedStatus(Drawing.IsPaused ? "status_paused" : "status_drawing");
            return;
        }
        if (_hexedDrawRunning)
        {
            if (Drawing.IsDrawing)
            {
                Drawing.IsPaused = !Drawing.IsPaused;
                HexedStatus(Drawing.IsPaused ? "status_paused" : "status_drawing");
            }
            return;
        }
        if (Drawing.IsDrawing)
        {
            Drawing.IsPaused = !Drawing.IsPaused;
            HexedStatus(Drawing.IsPaused ? "status_paused" : "status_drawing");
            return;
        }
        if (!HasImportedImage)
        {
            HexedStatus("status_error_no_area_image");
            return;
        }
        if (!DdConfigService.Current.AreaSelected)
        {
            HexedStatus("status_select_area_f1");
            return;
        }

        var config = DdConfigService.Current;
        if (config.MaskMode == LayerMaskMode.ColorLayers)
        {
            await StartColorLayerRecreateAsync(config);
            return;
        }

        if (LayersContext.Count == 0) await QuantizeForHexedDrawAsync();
        if (LayersContext.Count == 0) return;

        _layersStack.Clear();
        foreach (var layer in LayersContext)
        {
            if (!layer.Enabled || layer.RawBitmap is null) continue;
            _layersStack.Add(new ColorLayerResult
            {
                Bitmap = layer.RawBitmap,
                Color = layer.Color,
                PixelCount = layer.PixelCount
            });
        }
        if (_layersStack.Count == 0) return;

        var region = config.DrawRegion;
        var origin = new Vector2(region.Left, region.Top);
        Drawing.LastPos = origin;
        Drawing.ClipToRegion = true;
        Drawing.ChosenAlgorithm = (byte)AlgorithmIndexFromName(config.Algorithm);
        var hexedWasVisible = _hexedDrawWindow is { IsVisible: true };
        var showPopup = Drawing.ShowPopup;
        Drawing.ShowPopup = false;
        _hexedDrawRunning = true;
        if (hexedWasVisible) _hexedDrawWindow!.WindowState = WindowState.Minimized;
        WindowState = WindowState.Minimized;
        HexedStatus("status_drawing");
        try
        {
            var layers = _layersStack.ToList();
            var actions = _actionStack.ToList();
            await Task.Run(() => Drawing.DrawStack(layers, actions, origin));
        }
        catch (Exception ex)
        {
            Utils.Log("Draw failed: " + ex);
        }
        finally
        {
            Drawing.ShowPopup = showPopup;
            _hexedDrawRunning = false;
            Dispatcher.UIThread.Post(() =>
            {
                WindowState = WindowState.Normal;
                Activate();
                if (hexedWasVisible && _hexedDrawWindow is { } window)
                {
                    window.WindowState = WindowState.Normal;
                    window.Activate();
                }
                HexedStatus(Drawing.ProgressPercent >= 99.5 ? "status_drawing_complete" : "status_drawing_stopped");
                _hexedDrawWindow?.RefreshFromConfig();
            });
        }
    }

    /// <summary>
    /// Colour-layer workflow: analyse the imported image, build one mask per palette colour, then
    /// run the cancellable stroke engine.  Authentication/calibration errors are reported before
    /// the mouse is touched.  All masks are owned by <see cref="ImageAnalysisResult"/> and released
    /// in the finally block; the engine is told not to dispose them a second time.
    /// </summary>
    private async Task StartColorLayerRecreateAsync(AutoDrawConfig config)
    {
        if (_preFxBitmap is not { Width: > 1, Height: > 1 })
        {
            HexedStatus("status_error_no_area_image");
            return;
        }

        var options = RecreateOptions.FromConfig(config);
        options.Canvas = config.DrawRegion;
        options.DisposeLayers = false;

        IInputSimulator input;
        IColorPicker picker;
        IColorVerifier? verifier = null;
        try
        {
            input = InputSimulatorFactory.Create();
            if (options.DryRun)
            {
                // A preview must not require a calibrated picker; it never clicks the target app.
                picker = new NoOpColorPicker();
            }
            else
            {
                var definition = PickerRegistry.Require(options.PickerType);
                var calibration = RecreateOptions.BuildCalibration(config, definition.Id);
                var validation = definition.Validate(calibration, ScreenGeometry.TryGetVirtualScreenBounds());
                if (validation is not null)
                    throw new ColorSelectionException("Color picker is not calibrated: " + validation + ".");

                picker = definition.Create(input, calibration, options, Utils.Log);
                if (options.Verify)
                {
                    if (options.CurrentColorPoint is null)
                        throw new ColorSelectionException(LocalizationManager.T("status_recreate_verify_missing"));
                    verifier = new ColorVerifier(new GdiScreenColorSampler(), options.CurrentColorPoint.Value, options.VerifyTolerance);
                }
            }
        }
        catch (Exception ex)
        {
            HexedStatus("status_recreate_error", ex.Message);
            return;
        }

        var hexedWasVisible = _hexedDrawWindow is { IsVisible: true };
        var showPopup = Drawing.ShowPopup;
        _recreateCts = new CancellationTokenSource();
        var token = _recreateCts.Token;
        _recreatePause = new RecreatePauseController();
        _recreateRunning = true;
        if (!options.DryRun)
        {
            // Reserve the shared drawing state during analysis too, so the legacy Run button
            // cannot start a second engine against the same imported bitmap.
            Drawing.IsDrawing = true;
            Drawing.IsPaused = false;
            Drawing.ResetProgress();
        }
        var minimized = false;
        ImageAnalysisResult? analysis = null;

        try
        {
            HexedStatus("status_recreate_analyzing");
            using (var source = _preFxBitmap!.Copy())
            {
                analysis = await Task.Run(
                    () => ImageAnalyzer.Analyze(source, options, config, token),
                    token);
            }

            if (analysis.Layers.Count == 0)
            {
                HexedStatus("status_image_transparent");
                return;
            }
            HexedStatus("status_recreate_planned", analysis.Layers.Count);

            // The fingerprint covers the pixels, the canvas, the palette and the stroke plan.
            // A checkpoint is only ever resumed when this identical run produced it.
            ICheckpointStore? checkpointStore = null;
            if (!options.DryRun && options.Resume)
            {
                checkpointStore = new FileCheckpointStore(log: Utils.Log);
                options.Fingerprint = RecreateFingerprint.Compute(
                    _preFxBitmap!, options.Canvas, analysis.Palette, options);
                options.ResumeCheckpoint = checkpointStore.Load(options.Fingerprint);
                if (options.ResumeCheckpoint is { } checkpoint)
                    HexedStatus("status_recreate_resume_found", checkpoint.StrokeIndex);
            }
            else
            {
                options.Resume = false;
            }

            IFocusGuard? focusGuard = null;
            if (!options.DryRun)
            {
                Drawing.LastPos = new Vector2(config.DrawRegion.Left, config.DrawRegion.Top);
                Drawing.ClipToRegion = true;
                Drawing.ShowPopup = false;
                Drawing.IsDrawing = true;
                Drawing.IsPaused = _recreatePause?.IsPaused ?? false;
                if (hexedWasVisible) _hexedDrawWindow!.WindowState = WindowState.Minimized;
                WindowState = WindowState.Minimized;
                minimized = true;
                HexedStatus("status_drawing");

                // Arm only after AutoDraw is out of the way; the guard ignores this process so it
                // can never latch onto the window that is about to be minimised.
                if (options.FocusGuard)
                {
                    var guard = new ForegroundWindowFocusGuard();
                    if (!await guard.ArmAsync(token))
                    {
                        HexedStatus("status_recreate_focus_arm_failed");
                        return;
                    }

                    focusGuard = guard;
                    Utils.Log("Focus guard armed on " + guard.Description + ".");
                }
            }

            var engine = new RecreateEngine(
                input, picker, verifier, options, _recreatePause, Utils.Log, focusGuard, checkpointStore);
            _recreateEngine = engine;
            var lastStatusTicks = 0L;
            engine.ProgressChanged += (_, progress) =>
            {
                Drawing.ReportExternalProgress(
                    progress.CompletedStrokes,
                    progress.TotalStrokes,
                    progress.IsLayerBoundary || progress.Phase is "complete" or "dry-run");

                var now = Environment.TickCount64;
                if (!progress.IsLayerBoundary &&
                    progress.Phase is not ("complete" or "dry-run") &&
                    now - lastStatusTicks < 250)
                    return;
                lastStatusTicks = now;

                Dispatcher.UIThread.Post(() =>
                {
                    if (progress.Phase is "complete" or "dry-run") return;
                    _hexedDrawWindow?.SetStatus(LocalizationManager.F(
                        "status_recreate_progress",
                        progress.LayerText,
                        progress.Percent.ToString("0.0", CultureInfo.InvariantCulture),
                        progress.ColorHex));
                });
            };

            if (options.DryRun)
            {
                var dryRun = await Task.Run(() => engine.RunAsync(analysis.Layers, token), token);
                Drawing.ReportExternalProgress(dryRun.TotalStrokes, dryRun.TotalStrokes, true);
                if (dryRun.PreviewBitmap is { } preview)
                {
                    Bitmap avaloniaPreview;
                    try
                    {
                        avaloniaPreview = preview.ConvertToAvaloniaBitmap();
                    }
                    finally
                    {
                        preview.Dispose();
                    }

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _recreatePreviewWindow?.Close();
                        var window = new RecreatePreviewWindow(
                            avaloniaPreview,
                            LocalizationManager.T("recreate_preview_title"));
                        _recreatePreviewWindow = window;
                        window.Closed += (_, _) =>
                        {
                            if (ReferenceEquals(_recreatePreviewWindow, window))
                                _recreatePreviewWindow = null;
                        };
                        window.Show(this);
                    });
                }
                HexedStatus("status_recreate_dry_run", dryRun.LayerCount, dryRun.TotalStrokes);
                return;
            }

            var result = await Task.Run(() => engine.RunAsync(analysis.Layers, token), token);
            HexedStatus("status_recreate_complete", result.LayerCount, result.DrawnStrokes);
        }
        catch (OperationCanceledException)
        {
            HexedStatus("status_recreate_cancelled");
        }
        catch (RecreateFocusLostException ex)
        {
            Utils.Log("Recreate stopped: " + ex.Message);
            HexedStatus("status_recreate_focus_lost");
        }
        catch (Exception ex)
        {
            Utils.Log("Recreate failed: " + ex);
            HexedStatus("status_recreate_error", ex.Message);
        }
        finally
        {
            _recreateEngine = null;
            Drawing.ShowPopup = showPopup;
            Drawing.IsDrawing = false;
            Drawing.IsPaused = false;
            _recreatePause = null;
            _recreateRunning = false;
            _recreateCts?.Dispose();
            _recreateCts = null;
            analysis?.Dispose();

            if (minimized)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    WindowState = WindowState.Normal;
                    Activate();
                    if (hexedWasVisible && _hexedDrawWindow is { } window)
                    {
                        window.WindowState = WindowState.Normal;
                        window.Activate();
                    }
                });
            }

            if (Dispatcher.UIThread.CheckAccess()) _hexedDrawWindow?.RefreshFromConfig();
            else Dispatcher.UIThread.Post(() => _hexedDrawWindow?.RefreshFromConfig());
        }
    }

    public void ResetHexedDraw()
    {
        _recreateCts?.Cancel();
        if (_recreatePause is { } pause) pause.IsPaused = false;
        Drawing.Halt();
        Drawing.ResetProgress();
        Drawing.ClipToRegion = false;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        HexedStatus("status_reset_progress");
    }

    public void StopHexedDraw()
    {
        _recreateEngine?.Abort();
        _recreateCts?.Cancel();
        if (_recreatePause is { } pause) pause.IsPaused = false;
        Drawing.Halt();
        Drawing.IsPaused = false;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        HexedStatus("status_drawing_stopped");
    }

    public void ClearLayersButton_OnClick(object? sender, RoutedEventArgs e)
    {
        foreach (var layer in LayersContext)
        {
            layer.RawBitmap?.Dispose();
        }
        LayersContext.Clear();
        _layersStack.Clear();
    }
}
