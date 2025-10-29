using System;
using System.Globalization;
using System.Windows.Data;

namespace DockerBuildBoxSystem.App.Converters;

/// <summary>
/// Converter that inverts a boolean value.
/// https://stackoverflow.com/questions/46562252/inversebooleanconverter-is-missing
/// </summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Convert(value, targetType, parameter, culture);
    }
}
