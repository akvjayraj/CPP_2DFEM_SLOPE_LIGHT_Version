using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace GeotechSlopeApp.Models;

/// <summary>
/// Sibling to <see cref="TunnelExcavationCircle"/> for non-circular tunnels (horseshoe, rectangular,
/// NATM 3-radius arch, custom polyline). Holds a closed CCW polygon in <em>axis</em> space; canvas
/// rendering re-projects through <c>GeometryDefinitionViewModel.ConvertAxisToCanvas</c>.
/// </summary>
public class TunnelExcavationPolygon
{
    /// <summary>Shape family (informational; used by tooltips and labels).</summary>
    public TunnelShapeKind Kind { get; set; } = TunnelShapeKind.Circle;

    /// <summary>Axis-space vertices, closed polygon (last point need not equal first), CCW.</summary>
    public List<Point> AxisPoints { get; set; } = new();

    /// <summary>Canvas-space vertices for overlay rendering. Refreshed when axis changes.</summary>
    public PointCollection CanvasPoints { get; set; } = new();

    /// <summary>Centroid of the bounding box, axis space (used for stage labels).</summary>
    public double CenterX { get; set; }
    public double CenterY { get; set; }

    /// <summary>True after this polygon has been excavated by the staged-construction logic.</summary>
    public bool IsRestored { get; set; }

    /// <summary>Stage number in which excavation activates; null = always.</summary>
    public int? StageActivation { get; set; }
}
