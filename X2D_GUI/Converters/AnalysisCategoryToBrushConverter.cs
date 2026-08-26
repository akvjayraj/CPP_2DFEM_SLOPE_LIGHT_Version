using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Converters;

/// <summary>Background brush for Analysis column: Structural = slate, Seepage = cyan.</summary>
public sealed class AnalysisCategoryToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush StructuralBrush = Create(0xE2, 0xE8, 0xF0);
    private static readonly SolidColorBrush SeepageBrush = Create(0xCF, 0xEA, 0xFE);
    private static readonly SolidColorBrush DefaultBrush = Create(0xF8, 0xFA, 0xFC);

    private static SolidColorBrush Create(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var cat = value as string;
        if (string.Equals(cat, BoundaryConditionAnalysisCategories.Seepage, StringComparison.Ordinal))
            return SeepageBrush;
        if (string.Equals(cat, BoundaryConditionAnalysisCategories.Structural, StringComparison.Ordinal))
            return StructuralBrush;
        return DefaultBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
