using System.Windows;
using System.Windows.Media;

namespace GeotechSlopeApp.Models;

/// <summary>Canvas overlay for free water above the ground (ponded region).</summary>
public class PondedWaterOverlay
{
    public PointCollection Polygon { get; set; } = new();
    public Brush Fill { get; set; } = Brushes.Transparent;
}

public class PondedWaterIsobarSegment
{
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
}

public class PondedWaterCallout
{
    public double X { get; init; }
    public double Y { get; init; }
    public string Text { get; init; } = "";
}

public class TransientFixedPressureNodeOverlay
{
    public double X { get; init; }
    public double Y { get; init; }
    public string Text { get; init; } = "";
    public bool IsSelected { get; init; }
}
