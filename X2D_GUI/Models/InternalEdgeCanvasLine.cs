using System.Windows.Media;

namespace GeotechSlopeApp.Models;

/// <summary>Canvas-space polyline for drawing shared/internal edges (and arcs) on the geometry canvas.</summary>
public sealed class InternalEdgeCanvasLine
{
    /// <summary>Sampled canvas points. Two points = straight line; many points = arc.</summary>
    public PointCollection Points { get; init; } = new();

    public bool IsArc { get; init; }

    // Kept for backwards compat with any straight-line callers that use individual coords.
    public double X1 => Points.Count > 0 ? Points[0].X : 0;
    public double Y1 => Points.Count > 0 ? Points[0].Y : 0;
    public double X2 => Points.Count > 1 ? Points[^1].X : 0;
    public double Y2 => Points.Count > 1 ? Points[^1].Y : 0;
}
