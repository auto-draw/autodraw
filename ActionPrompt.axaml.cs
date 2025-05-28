using System;
using System.ComponentModel;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SharpHook;
using MouseButton = SharpHook.Native.MouseButton;

namespace Autodraw;


public partial class ActionPrompt : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string actionData;
    public string ActionData
    {
        get => actionData;
        set
        {
            if (actionData != value)
            {
                actionData = value;
                OnPropertyChanged(nameof(ActionData));
            }
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
    public ActionPrompt()
    {
        DataContext = this; // Still stupid.
        
        InitializeComponent();
        CloseAppButton.Click += CloseAppButton_Click;
        ActionType.SelectionChanged += ActionTypeOnSelectionChanged;
    }

    public String Speed { get; set; }
    public String Delay { get; set; }
    public String InputData { get; set; }
    public int Selection { get; set; }
    public Action Callback { get; set; }
    public InputAction? Action { get; set; }
    private object? _rawActionData;

    private void CloseAppButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private bool isTracking = false;
    public void StartTracking()
    {
        if (isTracking)
        {
            // Stop tracking if it was already active
            Input.taskHook.MouseClicked -= OnGlobalMouseClick;
            Input.taskHook.KeyPressed -= OnGlobalKeyPress;
            isTracking = false;
            return;
        }

        // Start global input hook
        Input.Start();
        isTracking = true;

        switch (Selection)
        {
            // Left Click
            case 0:
            // Right Click
            case 1:
            // General Position
            case 2:
                Input.taskHook.MouseClicked += OnGlobalMouseClick;
                ActionData = "Awaiting Mouse Input";
                break;
            // Key Down
            case 4:
                Input.taskHook.KeyPressed += OnGlobalKeyPress;
                ActionData = "Awaiting Key Down...";
                break;
            // Key Up
            case 5:
                Input.taskHook.KeyReleased += OnGlobalKeyPress;
                ActionData = "Awaiting Key Up...";
                break;
            default:
                Console.WriteLine($"Selection type '{Selection}' is not supported for global tracking.");
                break;
        }
    }

    // Handle global mouse click events
    private void OnGlobalMouseClick(object? sender, MouseHookEventArgs e)
    {
        if (e.Data.Button is MouseButton.Button1 or MouseButton.Button2)
        {
            var x = e.Data.X;
            var y = e.Data.Y;
            Console.WriteLine($"Global Mouse Click: Button {e.Data.Button}, Position: ({x}, {y})");

            ActionData = $"Click at X:{x}, Y:{y}";
            _rawActionData = new Vector2(x, y);

            // Stop tracking after the action
            Input.taskHook.MouseClicked -= OnGlobalMouseClick;
            isTracking = false;
        }
    }

    // Handle global key press events
    private void OnGlobalKeyPress(object? sender, KeyboardHookEventArgs e)
    {
        var keyEvent = Selection == 4 ? "Key Down" : "Key Up";

        Console.WriteLine($"{keyEvent}: {e.Data.KeyCode}");

        ActionData = $"{keyEvent}: {e.Data.KeyCode}";
        _rawActionData = e.Data.KeyCode.ToString();

        // Stop tracking after the action
        if (Selection == 4)
        {
            Input.taskHook.KeyPressed -= OnGlobalKeyPress;
        }
        else if (Selection == 5)
        {
            Input.taskHook.KeyReleased -= OnGlobalKeyPress;
        }

        isTracking = false;
    }

    private void ActionTypeOnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _rawActionData = null;
        ActionData = "";
        if (Selection == 3)
        {
            TrackAction.IsVisible = false;
            InputBox.IsVisible = true;
        }
        else
        {
            TrackAction.IsVisible = true;
            InputBox.IsVisible = false;
        }
    }

    private void InputBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (Selection != 3) return;
        Console.WriteLine(InputData);
        _rawActionData = InputData;
    }
    
    public void Confirm()
    {
        if (_rawActionData is null) return;
        // Generate a Gift for Mothership!
        var _ActionType = Selection switch
        {
            1 => InputAction.ActionType.RightClick,
            2 => InputAction.ActionType.MoveTo,
            3 => InputAction.ActionType.WriteString,
            4 => InputAction.ActionType.KeyDown,
            5 => InputAction.ActionType.KeyUp,
            _ => InputAction.ActionType.LeftClick
        };
        var _ActionData = _rawActionData;
        
        Action = new InputAction(_ActionType, _ActionData);

        Callback(); // Alien Cat: "Hello? Mothership, can you beam me up?"
    }

    public void Cancel()
    {
        Close();
    }
}