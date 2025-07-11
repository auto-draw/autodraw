using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Xaml.Interactivity;

namespace Autodraw;

public class NumericBoxFilterBehavior : Behavior<TextBox>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.TextInput += OnTextInput;
            AssociatedObject.KeyDown += OnKeyDown;
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (AssociatedObject != null)
        {
            AssociatedObject.TextInput -= OnTextInput;
            AssociatedObject.KeyDown -= OnKeyDown;
        }
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Text) && !int.TryParse(e.Text, out _))
        {
            e.Handled = true;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Back or Key.Delete or Key.Left or Key.Right)
        {
            return;
        }

        if (!int.TryParse(e.Key.ToString().Replace("D", "").Replace("NumPad", ""), out _))
        {
            e.Handled = true;
        }
    }
}

public class RangeFilterBehavior : Behavior<TextBox>
{
    private const int Min = 0;
    private const int Max = 255;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.KeyDown += OnKeyDown;
            AssociatedObject.LostFocus += OnLostFocus;
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (AssociatedObject != null)
        {
            AssociatedObject.KeyDown -= OnKeyDown;
            AssociatedObject.LostFocus -= OnLostFocus;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Back or Key.Delete or Key.Left or Key.Right or Key.Tab)
        {
            return;
        }

        var keyIsDigit = e.Key >= Key.D0 && e.Key <= Key.D9 || e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9;
        if (!keyIsDigit)
        {
            e.Handled = true;
        }
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (string.IsNullOrWhiteSpace(textBox.Text)) return;

        if (int.TryParse(textBox.Text, out var value))
        {
            if (value < Min)
            {
                textBox.Text = Min.ToString();
            }
            else if (value > Max)
            {
                textBox.Text = Max.ToString();
            }
        }
        else
        {
            textBox.Text = Min.ToString();
        }
    }
}

public class PercentageBoxFilterBehavior : Behavior<TextBox> // Could cut this down and require combine with NumericBox to have that handle numbers :P
{
    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.TextInput += OnTextInput;
            AssociatedObject.LostFocus += OnLostFocus;
            AssociatedObject.KeyDown += OnKeyDown;
            EnsurePercentageSuffix();
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (AssociatedObject != null)
        {
            AssociatedObject.TextInput -= OnTextInput;
            AssociatedObject.LostFocus -= OnLostFocus;
            AssociatedObject.KeyDown -= OnKeyDown;
        }
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        // Allow only digits in the text input, disallowing other characters
        if (!string.IsNullOrEmpty(e.Text) && !int.TryParse(e.Text, out _))
        {
            e.Handled = true;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Allow Back, Delete, Left, Right, and Tab for navigation
        if (e.Key is Key.Back or Key.Delete or Key.Left or Key.Right or Key.Tab)
        {
            return;
        }

        // Prevent non-digit key presses
        var keyIsDigit = e.Key >= Key.D0 && e.Key <= Key.D9 || e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9;
        if (!keyIsDigit)
        {
            e.Handled = true;
        }
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        EnsurePercentageSuffix();
    }

    private void EnsurePercentageSuffix()
    {
        if (AssociatedObject == null) return;

        var text = AssociatedObject.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            AssociatedObject.Text = "0%"; // Default to 0% if empty
            return;
        }

        if (text.EndsWith("%"))
        {
            // Remove the '%' temporarily, validate, then add '%' back
            if (int.TryParse(text.TrimEnd('%'), out var number))
            {
                AssociatedObject.Text = $"{number}%";
            }
            else
            {
                // Reset to valid default if parsing fails
                AssociatedObject.Text = "0%";
            }
        }
        else
        {
            // Append '%' if missing
            if (int.TryParse(text, out var number))
            {
                AssociatedObject.Text = $"{number}%";
            }
            else
            {
                AssociatedObject.Text = "0%"; // Reset to valid default
            }
        }
    }
}
