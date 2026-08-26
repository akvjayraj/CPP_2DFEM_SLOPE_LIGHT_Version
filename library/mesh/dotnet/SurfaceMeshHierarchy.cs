using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Determines which surface regions are geometrically nested (smallest enclosing region by area)
/// so the outer mesh can be generated with holes and inner regions meshed separately — avoiding
/// overlapping triangles between soil and trench (or similar) domains.
/// </summary>
public static class SurfaceMeshHierarchy
{
    /// <summary>Absolute polygon area (axis units²).</summary>
    public static double PolygonArea(IReadOnlyList<Point> poly)
    {
        int n = poly.Count;
        if (n < 3) return 0;
        double a = 0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            a += poly[i].X * poly[j].Y - poly[j].X * poly[i].Y;
        }
        return Math.Abs(a * 0.5);
    }

    public static Point PolygonCentroid(IReadOnlyList<Point> poly)
    {
        if (poly == null || poly.Count == 0) return new Point(0, 0);
        double sx = 0, sy = 0;
        foreach (var p in poly) { sx += p.X; sy += p.Y; }
        return new Point(sx / poly.Count, sy / poly.Count);
    }

    /// <summary>
    /// True if every vertex of <paramref name="inner"/> lies strictly inside or on <paramref name="outer"/>
    /// (standard nesting test for closed axis polygons).
    /// </summary>
    public static bool PolygonFullyInside(IReadOnlyList<Point> inner, IReadOnlyList<Point> outer)
    {
        if (inner == null || outer == null || inner.Count < 1 || outer.Count < 3) return false;
        foreach (var p in inner)
        {
            if (!GradedSurfaceMeshGenerator.PointInPolygon(p, outer)) return false;
        }
        return true;
    }

    /// <summary>
    /// For each surface, the parent is the <b>smallest-area</b> other surface that <b>fully contains</b>
    /// this surface's polygon (all vertices inside the candidate). That avoids inverting nested regions
    /// when the outer polygon's centroid falls inside an inner void (e.g. rockmass around a cavern).
    /// Null if none (outermost).
    /// </summary>
    public static Dictionary<int, int?> BuildParentMap(
        IReadOnlyList<(int SurfaceNumber, IReadOnlyList<Point> Polygon)> surfaces)
    {
        var parents = new Dictionary<int, int?>();
        var withArea = surfaces
            .Select(s => (s.SurfaceNumber, s.Polygon, Area: PolygonArea(s.Polygon)))
            .ToList();

        foreach (var (sn, poly, area) in withArea)
        {
            int? parent = null;
            double bestParentArea = double.MaxValue;
            foreach (var (osn, opoly, oarea) in withArea)
            {
                if (osn == sn) continue;
                // Parent must be a strictly larger region that wraps this one.
                if (oarea <= area + 1e-12) continue;
                if (!PolygonFullyInside(poly, opoly)) continue;
                if (oarea < bestParentArea)
                {
                    bestParentArea = oarea;
                    parent = osn;
                }
            }
            parents[sn] = parent;
        }

        return parents;
    }
}
