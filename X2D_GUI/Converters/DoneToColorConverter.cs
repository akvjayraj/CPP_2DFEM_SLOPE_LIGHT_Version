using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GeotechSlopeApp.Converters;

/// <summary>Converts a bool "done" flag to an emerald/grey brush for wizard status chips.</summary>
public class DoneToColorConverter : IValueConverter
{
    private static readonly Brush DoneBrush = new SolidColorBrush(
        (Color)ColorConverter.ConvertFromString("#10B981")!); // emerald

    private static readonly Brush PendingBrush = new SolidColorBrush(
        (Color)ColorConverter.ConvertFromString("#9CA3AF")!); // grey

    static DoneToColorConverter()
    {
        DoneBrush.Freeze();
        PendingBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is bool b && b) ? DoneBrush : PendingBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
