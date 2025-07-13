using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Autodraw.Models.Utils;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualBasic.CompilerServices;
using SharpHook;
using SkiaSharp;

namespace Autodraw.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private Window _main;
    
    // General Properties
    [ObservableProperty] private byte mode = 0; // BW, Color, Trace
    
    [ObservableProperty] private int widthProperty = 100;
    [ObservableProperty] private int heightProperty = 100;
    [ObservableProperty] private int scaleProperty = 100;
    
    [ObservableProperty] private int interval = 10_000;
    [ObservableProperty] private double speed = 10;     // Calculation is speed=100,000/interval
    [ObservableProperty] private int clickDelay = 100;  // Milliseconds, please multiply by 10,000 when using in Operations
    
    [ObservableProperty] private bool showInterval = false;
    
    // Black and White Properties
    [ObservableProperty] private byte minLight = 0;
    [ObservableProperty] private byte maxLight = 200;
    // Color Properties
    [ObservableProperty] private byte colors = 12;
    
    // Other Properties
    [ObservableProperty] private byte alphaThreshold = 127;
    [ObservableProperty] private byte drawAlgorithm = 0;
    
    // Filter Properties
    [ObservableProperty] private bool invert = false;
    [ObservableProperty] private bool outline = false;
    [ObservableProperty] private bool crosshatch = false;
    [ObservableProperty] private bool diagCrosshatch = false;
    [ObservableProperty] private int horizontalLines = 0;
    [ObservableProperty] private int verticalLines = 0;
    [ObservableProperty] private int borderAdvanced = 0;
    [ObservableProperty] private int outlineAdvanced = 0;
    [ObservableProperty] private int inlineAdvanced = 0;
    [ObservableProperty] private int inlineBorderAdvanced = 0;
    [ObservableProperty] private int erosionAdvanced = 0;
    
    
    [ObservableProperty]
    private Bitmap? renderBitmap; // Modifications ONLY to RenderBitmap!! Notice the Capital R! "notice my tone! 😼😼" - seffy.sef
    
    private SKBitmap? RawBitmap = new(100, 100);
    private SKBitmap? ScaledBitmap;
    private SKBitmap? ProcessBitmap;

    public MainWindowViewModel(Window main)
    {
        _main = main;
        Config.Initialize();
        
        Console.WriteLine(@"Welcome to MVVM!");
        ScaledBitmap = RawBitmap.NormalizeColor().Copy();
        RenderBitmap = ScaledBitmap.ConvertToAvaloniaBitmap();
        
        Input.Start();
    }

    private void LoadImage(string path)
    {
        RawBitmap = SKBitmap.Decode(path).NormalizeColor();
        ScaledBitmap = RawBitmap.Copy();
        RenderBitmap = ScaledBitmap.ConvertToAvaloniaBitmap();
        ProcessBitmap?.Dispose();
        ProcessBitmap = null;
        Console.WriteLine(@"Image Loaded!");
        // Reset Parameters

        _isUpdating = true;
        
        (WidthProperty, HeightProperty) = ClampDimensionsWithAspectRatio(ScaledBitmap.Width, ScaledBitmap.Height);
        ScaleProperty = ClampScale((int)((ScaledBitmap.Width * 100.0) / ScaledBitmap.Width));
        
        _isUpdating = false;
        
        DoResizeImageFull();
    }
    
    private CancellationTokenSource? _resizeDelayTokenSource;

    private async void DoResizeImageFast()
    {
        try
        {
            SKBitmap anyBitmap = ProcessBitmap is null ? RawBitmap : ProcessBitmap;
            Console.WriteLine(anyBitmap is null);
            if (anyBitmap is null) return;
            RenderBitmap = anyBitmap.Resize(new SKSizeI(WidthProperty,HeightProperty), SKFilterQuality.Low)
                .NormalizeColor()
                .ConvertToAvaloniaBitmap();
        
            _resizeDelayTokenSource?.Cancel();
            _resizeDelayTokenSource = new CancellationTokenSource();
    
            try
            {
                // Wait for 50ms, if not cancelled, do full resize
                await Task.Delay(50, _resizeDelayTokenSource.Token);
                DoResizeImageFull();
            }
            catch (TaskCanceledException)
            {
                // Ignore cancellation
            }
        }
        catch (Exception e)
        {
            throw; // TODO handle exception
        }
    }
    private void DoResizeImageFull()
    {
        if (RawBitmap is null) return;
        ScaledBitmap = RawBitmap.NormalizeColor().Resize(new SKSizeI(WidthProperty,HeightProperty), SKFilterQuality.High);
        Process();
    }
    
    

    [RelayCommand]
    private async void OpenImage()
    {
        Console.WriteLine(@"Open Image received!");
        var file = await _main.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Image",
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
            AllowMultiple = false
        });
        
        if(file.Count == 1) LoadImage(file[0].Path.LocalPath);
    }
    
    [RelayCommand]
    private async Task Process()
    {
        ProcessBitmap?.Dispose();
        RenderBitmap?.Dispose();
        if (ScaledBitmap == null) return;

        ProcessBitmap = ImageProcessing.Process(ScaledBitmap, GetFilters());
        RenderBitmap = ProcessBitmap.NormalizeColor().ConvertToAvaloniaBitmap();
    }

    [RelayCommand]
    private void Draw()
    {
        if (ProcessBitmap == null) // Yes, this can still happen despite auto-processing, due to the Clear function.
        {
            var msg = new MessageBox();
            msg.ShowMessageBox("Error!", "Please load and process an image first!");
            return;
        };
        
        // We send a control request, particularly to non-windows platforms like macOS and Linux on Wayland
        // This ensures the user receives a popup to permit mouse control.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // I haven't been able to test if just Input.MoveBy(0,5) would be easier, but this code is already proven functional.
            var hook = new TaskPoolGlobalHook();
            void foo (object? _, MouseHookEventArgs __) { };
            hook.MouseMoved += foo; // Start listening for input
            hook.RunAsync();
            hook.MouseMoved -= foo;
            hook.Dispose();
        }
        
        if(Drawing.IsDrawing) return;
        Drawing.ChosenAlgorithm = DrawAlgorithm;
        Drawing.Interval = Interval;
        Drawing.ClickDelay = ClickDelay;
        
        new Preview().ReadyDraw(ProcessBitmap);
        _main.WindowState = WindowState.Minimized;
    }

    [RelayCommand]
    private void ToggleInterval()
    {
        ShowInterval = !ShowInterval;
    }

    [RelayCommand]
    private void Paste()
    {
        Console.WriteLine(WidthProperty);
        Console.WriteLine(HeightProperty);
        Console.WriteLine(ScaleProperty);
        Console.WriteLine(@"Paste received!");
    }
    
    private bool _isUpdating;
    private const int MIN_SCALE = 1;
    private const int MAX_SCALE = 800;
    private const int MAX_DIMENSION = 4096;

    private int ClampScale(int scale) => Math.Clamp(scale, MIN_SCALE, MAX_SCALE);

    private (int width, int height) ClampDimensionsWithAspectRatio(int targetWidth, int targetHeight)
    {
        if (RawBitmap == null) return (targetWidth, targetHeight);
        
        double aspectRatio = (double)RawBitmap.Width / RawBitmap.Height;
        
        if (targetWidth > MAX_DIMENSION)
        {
            targetWidth = MAX_DIMENSION;
            targetHeight = (int)(targetWidth / aspectRatio);
        }
        
        if (targetHeight > MAX_DIMENSION)
        {
            targetHeight = MAX_DIMENSION;
            targetWidth = (int)(targetHeight * aspectRatio);
        }
        
        int minWidth = (int)(RawBitmap.Width * (MIN_SCALE / 100.0));
        int minHeight = (int)(RawBitmap.Height * (MIN_SCALE / 100.0));
        
        targetWidth = Math.Max(targetWidth, minWidth);
        targetHeight = Math.Max(targetHeight, minHeight);
        
        return (targetWidth, targetHeight);
    }

    partial void OnScalePropertyChanged(int value)
    {
        if (_isUpdating || RawBitmap == null) return;
        
        try
        {
            _isUpdating = true;
            
            int clampedScale = ClampScale(value);
            if (clampedScale != value)
            {
                ScaleProperty = clampedScale;
                return;
            }
            
            var newWidth = (int)(RawBitmap.Width * (clampedScale / 100.0));
            var newHeight = (int)(RawBitmap.Height * (clampedScale / 100.0));
            
            (newWidth, newHeight) = ClampDimensionsWithAspectRatio(newWidth, newHeight);
            
            WidthProperty = newWidth;
            HeightProperty = newHeight;
            
            DoResizeImageFast();
        }
        finally
        {
            _isUpdating = false;
        }
    }
    
    partial void OnWidthPropertyChanged(int value)
    {
        if (_isUpdating || RawBitmap == null) return;
        
        try
        {
            _isUpdating = true;
            
            var scale = (value * 100.0) / RawBitmap.Width;
            var newHeight = (int)(RawBitmap.Height * (scale / 100.0));
            
            (var newWidth, newHeight) = ClampDimensionsWithAspectRatio(value, newHeight);
            
            if (newWidth != value)
            {
                WidthProperty = newWidth;
                return;
            }
            
            scale = (newWidth * 100.0) / RawBitmap.Width;
            ScaleProperty = ClampScale((int)scale);
            HeightProperty = newHeight;
            
            DoResizeImageFast();
        }
        finally
        {
            _isUpdating = false;
        }
    }

    partial void OnHeightPropertyChanged(int value)
    {
        if (_isUpdating || RawBitmap == null) return;
        
        try
        {
            _isUpdating = true;
            
            var scale = (value * 100.0) / RawBitmap.Height;
            var newWidth = (int)(RawBitmap.Width * (scale / 100.0));
            
            (newWidth, var newHeight) = ClampDimensionsWithAspectRatio(newWidth, value);
            
            if (newHeight != value)
            {
                HeightProperty = newHeight;
                return;
            }
            
            scale = (newHeight * 100.0) / RawBitmap.Height;
            ScaleProperty = ClampScale((int)scale);
            WidthProperty = newWidth;
            
            DoResizeImageFast();
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private ImageProcessing.Filters GetFilters()
    {
        return new ImageProcessing.Filters(
            minThreshold: MinLight,
            maxThreshold: MaxLight,
            alphaThreshold: AlphaThreshold,
            invert: Invert,
            outline: Outline,
            crosshatch: Crosshatch,
            diagCrosshatch: DiagCrosshatch,
            horizontalLines: HorizontalLines,
            verticalLines: VerticalLines,
            borderAdvanced: BorderAdvanced,
            outlineAdvanced: OutlineAdvanced,
            inlineAdvanced: InlineAdvanced,
            inlineBorderAdvanced: InlineBorderAdvanced,
            erosionAdvanced: ErosionAdvanced
        );
    }
}