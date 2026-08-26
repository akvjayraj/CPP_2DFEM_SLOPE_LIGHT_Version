using System.Collections.Generic;

namespace GeotechSlopeApp.Models;

/// <summary>One line segment imported from Geometry Line Segments; lists mesh node IDs on the matching path edge (if any).</summary>
public class OuterBoundaryInfo
{
    /// <summary>Matches <see cref="GeometryLineSegment.LineNumber"/>; used to highlight the line on the BC / mesh canvas.</summary>
    public int LineNumber { get; set; }

    public string BoundaryTag { get; set; } = "";
    public int NodeCount => NodeIds?.Count ?? 0;
    public IReadOnlyList<int> NodeIds { get; set; } = new List<int>();
}
