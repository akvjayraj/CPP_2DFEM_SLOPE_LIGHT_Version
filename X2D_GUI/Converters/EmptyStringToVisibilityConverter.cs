using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GeotechSlopeApp.Converters;

/// <summary>Converts non-empty string to Visible, empty/null to Collapsed.</summary>
public class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return !string.IsNullOrEmpty(value?.ToString()) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
