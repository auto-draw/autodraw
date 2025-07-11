using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Autodraw;

public class NumericValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Convert int or byte (nullable or not) to string
        if (value is int or byte)
        {
            return value.ToString();
        }
        if (value == null)
        {
            return string.Empty; // Show empty string for null
        }
        return string.Empty; // Fallback
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string input || string.IsNullOrWhiteSpace(input))
        {
            return Avalonia.Data.BindingOperations.DoNothing; // Do nothing on invalid input
        }

        if (int.TryParse(input, out int intValue))
        {
            // Clamp value for byte range if target type is byte
            if (targetType == typeof(byte) || targetType == typeof(byte?))
            {
                intValue = Math.Clamp(intValue, 0, 255); // Clamp to valid range
                // This is just a QOL, the Behaviors catch this anyway's :P
                // Just keeps things clean and pretty
                return (byte)intValue;
            }
            // Return as integer for any other type
            return intValue;
        }

        return Avalonia.Data.BindingOperations.DoNothing; // Invalid conversion
    }
}


public class PercentageValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Convert number (int, byte, etc.) to a string with a '%' appended
        if (value is int or byte or double)
        {
            return $"{value}%";
        }

        return string.Empty; // Fallback for non-numeric values
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string input || string.IsNullOrWhiteSpace(input))
        {
            // Return DoNothing if input is null or empty
            return Avalonia.Data.BindingOperations.DoNothing;
        }

        input = input.Replace("%", "").Trim();
        if (int.TryParse(input, out var intValue))
        {
            // Clamp the value if the target type is `byte` or `byte?`
            if (targetType == typeof(byte) || targetType == typeof(byte?))
            {
                intValue = Math.Clamp(intValue, 0, 255); // Clamp to valid range
                // This is just a QOL, the Behaviors catch this anyway's :P
                // Just keeps things clean and pretty
                return (byte)intValue;
            }

            return intValue;
        }

        // Return DoNothing on invalid conversion
        return Avalonia.Data.BindingOperations.DoNothing;
    }
}

