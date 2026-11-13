using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ClevoControlPanel.App.Converters;

public sealed class IndexToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index &&
            parameter is string text &&
            int.TryParse(text, out int expected) &&
            index == expected)
        {
            return Visibility.Visible;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
