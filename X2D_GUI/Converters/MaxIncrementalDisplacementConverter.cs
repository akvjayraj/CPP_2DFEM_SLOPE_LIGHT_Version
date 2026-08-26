using System.Globalization;
using System.Windows.Data;

namespace GeotechSlopeApp.Converters;

public class MaxIncrementalDisplacementConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() ?? "0";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
