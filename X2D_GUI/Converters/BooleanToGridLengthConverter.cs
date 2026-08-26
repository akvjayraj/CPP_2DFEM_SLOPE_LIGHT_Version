using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GeotechSlopeApp.Converters;

public class BooleanToGridLengthConverter : IValueConverter
{
    public string TrueValue { get; set; } = "1*";
    public string FalseValue { get; set; } = "0";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value is bool b && b ? TrueValue : FalseValue;
        return (GridLength)new GridLengthConverter().ConvertFrom(s)!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is GridLength gl)
        {
            var trueLen = (GridLength)new GridLengthConverter().ConvertFrom(TrueValue)!;
            return Math.Abs(gl.Value - trueLen.Value) < 1e-9 && gl.GridUnitType == trueLen.GridUnitType;
        }
        return false;
    }
}
