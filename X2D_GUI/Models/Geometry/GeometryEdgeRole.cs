namespace GeotechSlopeApp.Models.Geometry;

/// <summary>Semantic role of a planar-map edge for editing, import mapping, and LEM constraints.</summary>
public enum GeometryEdgeRole
{
    Boundary = 0,
    MaterialContact = 1,
    Excavation = 2,
    WeakPlane = 3,
    TensionCrack = 4,
    WaterTable = 5,
    Bedding = 6,
    Annotation = 7
}
