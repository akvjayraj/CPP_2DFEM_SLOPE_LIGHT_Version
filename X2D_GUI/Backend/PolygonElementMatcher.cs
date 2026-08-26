using System;
using System.Collections.Generic;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Shared point-in-polygon helpers used by:
/// <list type="bullet">
///   <item><see cref="P610T3DatContentBuilder"/> when emitting the <c>Layers</c> block</item>
///   <item><c>StagedConstructionViewModel.CommitExcavation</c> when deriving element IDs from a freshly-clicked polygon</item>
///   <item>Future importers/exporters that need to map polygons to mesh elements</item>
/// </list>
/// All methods operate in axis (model) space.
/// </summary>
public static class PolygonElementMatcher
{
    /// <summary>Standard ray-cast point-in-polygon test (CCW or CW polygons both fine).</summary>
    public static bool PointInPolygon(double px, double py, IReadOnlyList<(double X, double Y)> polygon)
    {
        if (polygon == null) return false;
        int n = polygon.Count;
        if (n < 3) return false;
        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = polygon[i].X, yi = polygon[i].Y;
            double xj = polygon[j].X, yj = polygon[j].Y;
            if (((yi > py) != (yj > py)) &&
                (px < (xj - xi) * (py - yi) / (yj - yi) + xi))
                inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// Returns the IDs of the mesh elements whose centroid lies inside <paramref name="polygon"/>.
    /// Sorted ascending. Empty when no rows or polygon has &lt; 3 vertices.
    /// Centroid = arithmetic mean of corner vertices (3 for triangles, 4 for quads), matching
    /// the convention used by <see cref="P610T3DatContentBuilder"/>.
    /// </summary>
    public static List<int> ElementIdsInsidePolygon(
        IReadOnlyList<MeshInformationRow> rows,
        IReadOnlyList<(double X, double Y)> polygon)
    {
        var hits = new List<int>();
        if (rows == null || polygon == null || polygon.Count < 3) return hits;

        foreach (var r in rows)
        {
            (double cx, double cy) = ElementCentroid(r);
            if (PointInPolygon(cx, cy, polygon))
                hits.Add(r.ElementId);
        }
        hits.Sort();
        return hits;
    }

    /// <summary>Centroid (arithmetic mean of corner vertices) for a mesh row.</summary>
    public static (double X, double Y) ElementCentroid(MeshInformationRow r)
    {
        if (r.Node4 > 0 && r.IsQuad)
        {
            return ((r.N1X + r.N2X + r.N3X + r.N4X) * 0.25,
                    (r.N1Y + r.N2Y + r.N3Y + r.N4Y) * 0.25);
        }
        return ((r.N1X + r.N2X + r.N3X) / 3.0,
                (r.N1Y + r.N2Y + r.N3Y) / 3.0);
    }
}
