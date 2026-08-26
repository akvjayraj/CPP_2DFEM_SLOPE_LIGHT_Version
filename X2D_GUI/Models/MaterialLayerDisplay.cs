using System.Windows;
using System.Windows.Media;

namespace GeotechSlopeApp.Models;

/// <summary>
/// Minimal layer display for canvas binding. Polygon, Fill, Stroke, StrokeThickness.
/// </summary>
public class MaterialLayerDisplay
{
    public PointCollection Polygon { get; set; } = new();
    public Brush Fill { get; set; } = Brushes.Gray;
    public Brush Stroke { get; set; } = Brushes.Black;
    public double StrokeThickness { get; set; } = 1;
}
