using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GeotechSlopeApp.Converters;

public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool nonZero = false;
        if (value is int i) nonZero = i > 0;
        else if (value is System.Collections.ICollection c) nonZero = c.Count > 0;
        if (parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
            nonZero = !nonZero;
        return nonZero ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
