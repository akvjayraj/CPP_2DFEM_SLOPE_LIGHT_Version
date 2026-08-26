using System.Globalization;
using System.Windows.Data;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Converters;

public class LineNumberMatchToBooleanConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not GeometryLineSegment segment)
            return false;
        var highlighted = values[1] as int?;
        return highlighted.HasValue && segment.LineNumber == highlighted.Value;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
