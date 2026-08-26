using System.Windows;
using System.Windows.Media;

namespace GeotechSlopeApp.Models;

public enum GroundwaterZoneKind
{
    Saturated,
    Unsaturated
}

public class GroundwaterZoneOverlay
{
    public PointCollection Polygon { get; set; } = new();
    public Brush Fill { get; set; } = Brushes.Transparent;
    public GroundwaterZoneKind Kind { get; init; }
}
