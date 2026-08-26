using System;
using System.Windows.Media;

namespace GeotechSlopeApp.Visualization;

/// <summary>Blue→red colormap shared by 2D and 1D FE contour views.</summary>
public static class FEContourColorMap
{
    public static Brush GetBrushForValue(double value, double minValue, double maxValue)
        => new SolidColorBrush(GetColorForValue(value, minValue, maxValue));

    public static Color GetColorForValue(double value, double minValue, double maxValue)
    {
        if (Math.Abs(maxValue - minValue) < 1e-10)
        {
            minValue -= 1e-6;
            maxValue += 1e-6;
        }

        double normalized = (value - minValue) / (maxValue - minValue);
        normalized = Math.Max(0, Math.Min(1, normalized));
        return GetColorForNormalizedValue(normalized);
    }

    public static Color GetColorForNormalizedValue(double normalized)
    {
        normalized = Math.Max(0, Math.Min(1, normalized));

        if (normalized < 0.25)
        {
            double t = normalized / 0.25;
            return Color.FromRgb(0, (byte)(255 * t), 255);
        }

        if (normalized < 0.5)
        {
            double t = (normalized - 0.25) / 0.25;
            return Color.FromRgb(0, 255, (byte)(255 * (1 - t)));
        }

        if (normalized < 0.75)
        {
            double t = (normalized - 0.5) / 0.25;
            return Color.FromRgb((byte)(255 * t), 255, 0);
        }

        double tHigh = (normalized - 0.75) / 0.25;
        return Color.FromRgb(255, (byte)(255 * (1 - tHigh)), 0);
    }
}
