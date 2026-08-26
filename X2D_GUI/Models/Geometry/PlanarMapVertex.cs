namespace GeotechSlopeApp.Models.Geometry;

/// <summary>Vertex in the planar geometry kernel (axis coordinates, stable ID).</summary>
public sealed class PlanarMapVertex
{
    public required int StableId { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
}
