using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GeotechSlopeApp.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value?.ToString()?.ToLowerInvariant() ?? "";
        return s switch
        {
            "complete" or "success" or "done" => Brushes.Green,
            "error" or "failed" => Brushes.Red,
            "warning" or "pending" => Brushes.Orange,
            "running" or "in progress" => Brushes.Blue,
            _ => Brushes.Black
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
