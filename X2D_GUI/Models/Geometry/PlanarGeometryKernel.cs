using System.Collections.Generic;
using System.Linq;

namespace GeotechSlopeApp.Models.Geometry;

/// <summary>
/// Rebuildable planar map used for topology verification, true booleans, geological constraints,
/// and CAD/GIS interchange. The UI still owns the editable collections; this is the solid model.
/// </summary>
public sealed class PlanarGeometryKernel
{
    public List<PlanarMapVertex> Vertices { get; } = new();
    public List<PlanarMapEdge> Edges { get; } = new();
    public List<PlanarMapFace> Faces { get; } = new();
    public List<GeologicalFeature> GeologicalFeatures { get; } = new();
    /// <summary>Boolean history that can recompute face geometry.</summary>
    public List<KernelBooleanOperation> BooleanHistory { get; } = new();

    public PlanarMapVertex? FindVertex(int stableId) =>
        Vertices.FirstOrDefault(v => v.StableId == stableId);

    public IEnumerable<PlanarMapFace> SoilFaces =>
        Faces.Where(f => !f.IsExcavationVoid);

    public IEnumerable<GeologicalFeature> EnabledFeatures(GeologicalFeatureKind kind) =>
        GeologicalFeatures.Where(f => f.IsEnabled && f.Kind == kind && f.Vertices.Count >= 2);
}
