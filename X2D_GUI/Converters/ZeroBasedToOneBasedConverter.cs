using System.Globalization;
using System.Windows.Data;

namespace GeotechSlopeApp.Converters;

public class ZeroBasedToOneBasedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int i)
            return i + 1;
        return value ?? 1;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int i)
            return Math.Max(0, i - 1);
        return value ?? 0;
    }
}
