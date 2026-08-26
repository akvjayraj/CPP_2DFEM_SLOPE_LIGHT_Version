using System;
using System.Collections.Generic;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>Simple polygon ear-clipping triangulation (3-node triangles) for a simple closed polygon.</summary>
public static class PolygonEarTriangulation
{
    /// <summary>Triangulate a simple polygon (no self-intersections). Vertices may be CW or CCW.</summary>
    public static bool TryTriangulate(IReadOnlyList<Point> vertices, out List<(int i, int j, int k)> triangles)
    {
        triangles = new List<(int, int, int)>();
        if (vertices == null || vertices.Count < 3)
            return false;

        int n = vertices.Count;
        var idx = new List<int>(n);
        for (int i = 0; i < n; i++)
            idx.Add(i);

        if (PolygonAreaSigned(vertices) < 0)
            idx.Reverse();

        int guard = n * n * 2;
        while (idx.Count > 3 && guard-- > 0)
        {
            bool removed = false;
            for (int k = 0; k < idx.Count; k++)
            {
                int iPrev = idx[(k + idx.Count - 1) % idx.Count];
                int iCurr = idx[k];
                int iNext = idx[(k + 1) % idx.Count];
                var a = vertices[iPrev];
                var b = vertices[iCurr];
                var c = vertices[iNext];
                if (!IsConvex(a, b, c))
                    continue;
                if (!IsEar(vertices, idx, k, iPrev, iCurr, iNext))
                    continue;
                triangles.Add((iPrev, iCurr, iNext));
                idx.RemoveAt(k);
                removed = true;
                break;
            }
            if (!removed)
                return false;
        }

        if (idx.Count == 3)
            triangles.Add((idx[0], idx[1], idx[2]));
        return triangles.Count > 0;
    }

    private static double PolygonAreaSigned(IReadOnlyList<Point> v)
    {
        double a = 0;
        int n = v.Count;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            a += v[i].X * v[j].Y - v[j].X * v[i].Y;
        }
        return a * 0.5;
    }

    private static bool IsConvex(Point a, Point b, Point c) =>
        Cross(a, b, c) > 1e-18;

    private static double Cross(Point a, Point b, Point c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool IsEar(IReadOnlyList<Point> vertices, List<int> ring, int earK, int iPrev, int iCurr, int iNext)
    {
        var a = vertices[iPrev];
        var b = vertices[iCurr];
        var c = vertices[iNext];
        for (int t = 0; t < ring.Count; t++)
        {
            int vi = ring[t];
            if (vi == iPrev || vi == iCurr || vi == iNext)
                continue;
            var p = vertices[vi];
            if (PointInTriangle(p, a, b, c))
                return false;
        }
        return true;
    }

    private static bool PointInTriangle(Point p, Point a, Point b, Point c)
    {
        double s1 = Cross(a, b, p);
        double s2 = Cross(b, c, p);
        double s3 = Cross(c, a, p);
        bool hasNeg = s1 < -1e-12 || s2 < -1e-12 || s3 < -1e-12;
        bool hasPos = s1 > 1e-12 || s2 > 1e-12 || s3 > 1e-12;
        return !(hasNeg && hasPos);
    }
}
