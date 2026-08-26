using System.Globalization;
using System.Windows.Data;

namespace GeotechSlopeApp.Converters;

public class BooleanToDoubleConverter : IValueConverter
{
    public double TrueValue { get; set; } = 1.0;
    public double FalseValue { get; set; } = 0.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? TrueValue : FalseValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
            return Math.Abs(d - TrueValue) < 1e-9;
        return false;
    }
}
