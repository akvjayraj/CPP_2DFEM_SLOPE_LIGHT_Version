using System.Globalization;
using System.Windows.Data;

namespace GeotechSlopeApp.Converters;

/// <summary>
/// Converts a string value to bool by comparing it to ConverterParameter.
/// Used with RadioButton.IsChecked to bind a group of radio buttons to a single string property.
/// Convert: returns true when value == parameter.
/// ConvertBack: returns parameter when value is true, otherwise returns Binding.DoNothing.
/// </summary>
public class StringEqualConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string, StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return parameter ?? "";
        return System.Windows.Data.Binding.DoNothing;
    }
}
