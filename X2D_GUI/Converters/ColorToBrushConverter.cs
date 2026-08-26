using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GeotechSlopeApp.Converters;

/// <summary>Binds a <see cref="Color"/> to a <see cref="SolidColorBrush"/> (palette swatches).</summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Color colour ? new SolidColorBrush(colour) : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
