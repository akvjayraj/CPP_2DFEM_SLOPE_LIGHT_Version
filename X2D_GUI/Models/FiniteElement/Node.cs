namespace GeotechSlopeApp.Models.FiniteElement;

/// <summary>
/// FEM node with coordinates and displacements.
/// </summary>
public record Node
{
    public int Id { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double[] Displacements { get; init; } = { 0, 0 };
}
