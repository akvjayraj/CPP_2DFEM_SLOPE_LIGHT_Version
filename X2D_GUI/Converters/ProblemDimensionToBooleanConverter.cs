using System;
using System.Globalization;
using System.Windows.Data;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Converters;

public class ProblemDimensionToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ProblemDimension dim || parameter is not string paramStr)
            return false;
        if (Enum.TryParse<ProblemDimension>(paramStr, true, out var paramDim))
            return dim == paramDim;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true || parameter is not string paramStr)
            return Binding.DoNothing;
        if (Enum.TryParse<ProblemDimension>(paramStr, true, out var paramDim))
            return paramDim;
        return Binding.DoNothing;
    }
}
