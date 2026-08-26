using System.Globalization;
using System.Windows.Data;

namespace GeotechSlopeApp.Converters;

public class RadiusResolutionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var param = parameter?.ToString() ?? "";
        var val = value?.ToString() ?? "";
        return val.Equals(param, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return parameter?.ToString() ?? "";
        return Binding.DoNothing;
    }
}
