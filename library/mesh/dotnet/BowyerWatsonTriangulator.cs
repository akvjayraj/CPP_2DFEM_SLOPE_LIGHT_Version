using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Incremental Bowyer-Watson Delaunay triangulation.
/// Produces a Delaunay triangulation of a set of 2-D points.
/// Index triples (a, b, c) reference positions in the original input list.
/// </summary>
public static class BowyerWatsonTriangulator
{
    private struct Tri { public int A, B, C; }

    /// <summary>
    /// Triangulate <paramref name="inputPoints"/> and return a list of index triples.
    /// Each triple (a,b,c) is a triangle whose vertices are inputPoints[a], [b], [c].
    /// </summary>
    public static List<(int a, int b, int c)> Triangulate(IReadOnlyList<Point> inputPoints)
    {
        if (inputPoints.Count < 3) return new();

        // Extend with 3 super-triangle vertices so every input point lies inside
        var pts = new List<Point>(inputPoints.Count + 3);
        pts.AddRange(inputPoints);
        int n = inputPoints.Count;

        double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
        double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        double dx   = maxX - minX,       dy   = maxY - minY;
        double d    = Math.Max(dx, dy) * 12 + 1;
        double mx   = (minX + maxX) * 0.5, my = (minY + maxY) * 0.5;

        pts.Add(new Point(mx - 4 * d, my - d));      // index n
        pts.Add(new Point(mx,         my + 4 * d));  // index n+1
        pts.Add(new Point(mx + 4 * d, my - d));      // index n+2

        var triangulation = new List<Tri>(inputPoints.Count * 3)
        {
            new() { A = n, B = n + 1, C = n + 2 }
        };

        var badTris    = new List<Tri>();
        var polygon    = new List<(int, int)>();

        for (int pi = 0; pi < n; pi++)
        {
            var p = pts[pi];
            badTris.Clear();
            polygon.Clear();

            // --- Find all triangles whose circumcircle contains p ---
            foreach (var t in triangulation)
                if (InCircumcircle(pts[t.A], pts[t.B], pts[t.C], p))
                    badTris.Add(t);

            // --- Find the boundary polygon of the hole ---
            foreach (var t in badTris)
            {
                TryAddEdge(polygon, t.A, t.B);
                TryAddEdge(polygon, t.B, t.C);
                TryAddEdge(polygon, t.C, t.A);
            }

            // --- Remove bad triangles ---
            foreach (var t in badTris)
                triangulation.Remove(t);

            // --- Retriangulate from hole boundary to p ---
            foreach (var (ea, eb) in polygon)
                triangulation.Add(new Tri { A = ea, B = eb, C = pi });
        }

        // --- Remove triangles sharing a vertex with the super-triangle ---
        var result = new List<(int, int, int)>(triangulation.Count);
        foreach (var t in triangulation)
        {
            if (t.A >= n || t.B >= n || t.C >= n) continue;
            result.Add((t.A, t.B, t.C));
        }
        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Add edge (a,b) to <paramref name="boundary"/>. If the reversed edge (b,a) already exists,
    /// remove it instead (shared edge between two bad triangles — not part of the hole boundary).
    /// </summary>
    private static void TryAddEdge(List<(int, int)> boundary, int a, int b)
    {
        for (int i = 0; i < boundary.Count; i++)
        {
            var (ea, eb) = boundary[i];
            if ((ea == b && eb == a) || (ea == a && eb == b))
            {
                boundary.RemoveAt(i);
                return;
            }
        }
        boundary.Add((a, b));
    }

    /// <summary>
    /// Returns true if point <paramref name="p"/> lies strictly inside the circumcircle of
    /// triangle (<paramref name="a"/>, <paramref name="b"/>, <paramref name="c"/>).
    /// Uses the exact determinant test (works for CCW and CW orientation).
    /// </summary>
    private static bool InCircumcircle(Point a, Point b, Point c, Point p)
    {
        double ax = a.X - p.X, ay = a.Y - p.Y;
        double bx = b.X - p.X, by = b.Y - p.Y;
        double cx = c.X - p.X, cy = c.Y - p.Y;

        double det =
              ax * (by * (cx * cx + cy * cy) - cy * (bx * bx + by * by))
            - ay * (bx * (cx * cx + cy * cy) - cx * (bx * bx + by * by))
            + (ax * ax + ay * ay) * (bx * cy - by * cx);

        // For CCW triangles det > 0 means inside; for CW triangles det < 0.
        // We don't know orientation, so check the signed area to decide sign convention.
        double area2 = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        return area2 >= 0 ? det > 1e-10 : det < -1e-10;
    }
}
