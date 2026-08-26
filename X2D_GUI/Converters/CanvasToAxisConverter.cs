using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GeotechSlopeApp.Converters;

/// <summary>
/// Converts between canvas coordinates and axis coordinates for MultiBinding.
/// Expects values: [canvasValue, canvasSize, axisMin, axisMax].
/// </summary>
public class CanvasToAxisConverter : IMultiValueConverter
{
    public string Dimension { get; set; } = "X";

    private double _lastSize, _lastMin, _lastMax;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length < 4) return 0.0;
        var canvasVal = System.Convert.ToDouble(values[0]);
        _lastSize = System.Convert.ToDouble(values[1]);
        _lastMin = System.Convert.ToDouble(values[2]);
        _lastMax = System.Convert.ToDouble(values[3]);
        if (_lastSize <= 0 || Math.Abs(_lastMax - _lastMin) < 1e-10) return _lastMin;
        if (string.Equals(Dimension, "Y", StringComparison.OrdinalIgnoreCase))
            return _lastMax - (canvasVal / _lastSize) * (_lastMax - _lastMin);
        return _lastMin + (canvasVal / _lastSize) * (_lastMax - _lastMin);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        var axisVal = System.Convert.ToDouble(value);
        double canvasVal;
        if (_lastSize <= 0 || Math.Abs(_lastMax - _lastMin) < 1e-10)
            canvasVal = 0;
        else if (string.Equals(Dimension, "Y", StringComparison.OrdinalIgnoreCase))
            canvasVal = (_lastMax - axisVal) / (_lastMax - _lastMin) * _lastSize;
        else
            canvasVal = (axisVal - _lastMin) / (_lastMax - _lastMin) * _lastSize;
        return new object[] { canvasVal, Binding.DoNothing, Binding.DoNothing, Binding.DoNothing };
    }
}
