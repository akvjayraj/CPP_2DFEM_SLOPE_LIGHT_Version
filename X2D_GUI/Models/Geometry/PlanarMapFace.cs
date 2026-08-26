using System.Collections.Generic;

namespace GeotechSlopeApp.Models.Geometry;

/// <summary>Closed face (region) in the planar map, optionally assigned a material.</summary>
public sealed class PlanarMapFace
{
    public required int Id { get; init; }
    public string Name { get; set; } = "";
    public int? MaterialId { get; set; }
    /// <summary>Ordered stable vertex IDs around the face (open or closed ring; closing ID may repeat).</summary>
    public List<int> VertexStableIds { get; } = new();
    /// <summary>True when this face was created by a fill/boolean commit.</summary>
    public bool IsFill { get; set; }
    /// <summary>True when this face is an excavation void (not soil).</summary>
    public bool IsExcavationVoid { get; set; }
}
