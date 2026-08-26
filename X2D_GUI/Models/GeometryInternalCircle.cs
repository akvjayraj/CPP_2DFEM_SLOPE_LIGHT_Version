namespace GeotechSlopeApp.Models;

/// <summary>
/// User-drawn circle (Geometry Line Segments → one row: center + radius in axis units).
/// </summary>
public sealed class GeometryInternalCircle
{
    public int Id { get; set; }
    public double CenterAxisX { get; set; }
    public double CenterAxisY { get; set; }
    public double RadiusAxis { get; set; }
}
