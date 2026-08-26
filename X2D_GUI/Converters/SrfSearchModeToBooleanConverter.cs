using System;
using System.Globalization;
using System.Windows.Data;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Converters;

/// <summary>Binds a RadioButton's IsChecked to a specific <see cref="SrfSearchMode"/> value (ConverterParameter).</summary>
public class SrfSearchModeToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not SrfSearchMode mode || parameter is not string paramStr)
            return false;
        return Enum.TryParse<SrfSearchMode>(paramStr, true, out var paramMode) && mode == paramMode;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true || parameter is not string paramStr)
            return Binding.DoNothing;
        return Enum.TryParse<SrfSearchMode>(paramStr, true, out var paramMode) ? paramMode : Binding.DoNothing;
    }
}
