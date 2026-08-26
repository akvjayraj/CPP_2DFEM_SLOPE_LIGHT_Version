using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace GeotechSlopeApp.Converters;

/// <summary>
/// Converts a hex color string (e.g. "#FFDC2626") to a <see cref="SolidColorBrush"/>.
/// Returns <see cref="Brushes.Transparent"/> for null/empty/unparseable input.
/// </summary>
public class StringToBrushConverter : IValueConverter
{
    public bool UseWindowForegroundFallback { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch { }
        }
        if (UseWindowForegroundFallback)
        {
            if (Application.Current?.TryFindResource("WindowForegroundBrush") is Brush theme)
                return theme;
            return SystemColors.ControlTextBrush;
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
