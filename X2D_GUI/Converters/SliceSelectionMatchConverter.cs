using System.Globalization;
using System.Windows.Data;

namespace GeotechSlopeApp.Converters;

public class SliceSelectionMatchConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length < 2)
            return false;

        var indexOrSlice = values[0];
        var selected = values[1];

        if (indexOrSlice == null || selected == null)
            return false;

        var param = parameter?.ToString() ?? "";
        var isZeroBased = param.Equals("ZeroBased", StringComparison.OrdinalIgnoreCase);

        int idx = indexOrSlice is int i ? i : System.Convert.ToInt32(indexOrSlice);
        int sel = selected is int s ? s : System.Convert.ToInt32(selected);

        return isZeroBased ? (idx + 1) == sel : idx == sel;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
