namespace GeotechSlopeApp.Models.Geometry;

/// <summary>Undirected edge between two kernel vertices.</summary>
public sealed class PlanarMapEdge
{
    public required int Id { get; init; }
    public required int StartStableId { get; init; }
    public required int EndStableId { get; init; }
    public GeometryEdgeRole Role { get; set; } = GeometryEdgeRole.Boundary;
    public int? MaterialAId { get; set; }
    public int? MaterialBId { get; set; }
    /// <summary>Optional named geological feature this edge belongs to.</summary>
    public string? FeatureId { get; set; }
}
