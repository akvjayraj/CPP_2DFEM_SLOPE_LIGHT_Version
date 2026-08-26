using System;
using System.Collections.Generic;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Quad-dominant recombination: greedily merges adjacent CDT triangle pairs that share an edge into
/// convex Q4 quads, leaving the rest as triangles. This is the standard "recombine" approach used by
/// RS2 / Plaxis / Gmsh to obtain quad(-dominant) meshes on arbitrary geometry. It operates purely on
/// connectivity over a fixed, shared node pool — no nodes are moved or added — so a conforming input
/// stays conforming (interface edges are region-boundary edges shared by a single triangle and are
/// never consumed). Each triangle participates in at most one merge; pairs are accepted in descending
/// quality order so the best quads form first.
/// </summary>
public static class TriangleToQuadRecombiner
{
    /// <summary>Recombine <paramref name="tris"/> into quads + leftover triangles (indices into
    /// <paramref name="nodes"/>). A candidate quad is only accepted when it is simple, convex and its
    /// quality (Gmsh-style, 1 = perfect square) is at least <paramref name="minQuality"/>.</summary>
    public static (List<(int a, int b, int c, int d)> Quads, List<(int a, int b, int c)> Triangles) Recombine(
        IReadOnlyList<Point> nodes,
        IReadOnlyList<(int a, int b, int c)> tris,
        double minQuality = 0.25)
    {
        var quads = new List<(int, int, int, int)>();
        var leftover = new List<(int, int, int)>();
        int n = tris?.Count ?? 0;
        if (n == 0) return (quads, leftover);

        // Map each undirected edge to the triangles that use it.
        var edgeMap = new Dictionary<(int, int), List<int>>(n * 2);
        static (int, int) Key(int i, int j) => i < j ? (i, j) : (j, i);
        void Register(int t, int i, int j)
        {
            var k = Key(i, j);
            if (!edgeMap.TryGetValue(k, out var list)) { list = new List<int>(2); edgeMap[k] = list; }
            list.Add(t);
        }
        for (int t = 0; t < n; t++)
        {
            var (a, b, c) = tris[t];
            Register(t, a, b);
            Register(t, b, c);
            Register(t, c, a);
        }

        // Build merge candidates over interior edges shared by exactly two triangles.
        var candidates = new List<(double Quality, int T1, int T2, (int, int, int, int) Quad)>();
        foreach (var kv in edgeMap)
        {
            if (kv.Value.Count != 2) continue;
            int t1 = kv.Value[0], t2 = kv.Value[1];
            var (p, q) = kv.Key;
            int apex1 = ThirdVertex(tris[t1], p, q);
            int apex2 = ThirdVertex(tris[t2], p, q);
            if (apex1 < 0 || apex2 < 0 || apex1 == apex2) continue;

            if (TryMakeConvexQuad(nodes, apex1, p, apex2, q, out var quad, out double quality)
                && quality >= minQuality)
                candidates.Add((quality, t1, t2, quad));
        }

        candidates.Sort((x, y) => y.Quality.CompareTo(x.Quality));

        var used = new bool[n];
        foreach (var cand in candidates)
        {
            if (used[cand.T1] || used[cand.T2]) continue;
            used[cand.T1] = true;
            used[cand.T2] = true;
            quads.Add(cand.Quad);
        }

        for (int t = 0; t < n; t++)
            if (!used[t]) leftover.Add(tris[t]);

        return (quads, leftover);
    }

    private static int ThirdVertex((int a, int b, int c) tri, int p, int q)
    {
        if (tri.a != p && tri.a != q) return tri.a;
        if (tri.b != p && tri.b != q) return tri.b;
        if (tri.c != p && tri.c != q) return tri.c;
        return -1;
    }

    /// <summary>Build the quad [apex1, p, apex2, q] (pq is the shared diagonal), normalise to CCW and
    /// accept only if it is convex and non-degenerate. Quality = 1 - (2/π)·max|θ−90°| over the four
    /// corner angles (1 = perfect rectangle, 0 = degenerate).</summary>
    private static bool TryMakeConvexQuad(
        IReadOnlyList<Point> nodes, int apex1, int p, int apex2, int q,
        out (int a, int b, int c, int d) quad, out double quality)
    {
        quad = default;
        quality = 0;

        int[] order = { apex1, p, apex2, q };
        Point[] pts = { nodes[apex1], nodes[p], nodes[apex2], nodes[q] };

        double area = SignedArea(pts);
        if (Math.Abs(area) < 1e-12) return false;

        if (area < 0)
        {
            Array.Reverse(order);
            Array.Reverse(pts);
        }

        // Convexity: every consecutive cross product must be strictly positive (CCW, no reflex corner).
        for (int i = 0; i < 4; i++)
        {
            Point a = pts[i], b = pts[(i + 1) % 4], c = pts[(i + 2) % 4];
            double cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
            if (cross <= 1e-12) return false;
        }

        double maxDev = 0;
        for (int i = 0; i < 4; i++)
        {
            Point prev = pts[(i + 3) % 4], cur = pts[i], next = pts[(i + 1) % 4];
            double ux = prev.X - cur.X, uy = prev.Y - cur.Y;
            double vx = next.X - cur.X, vy = next.Y - cur.Y;
            double lu = Math.Sqrt(ux * ux + uy * uy), lv = Math.Sqrt(vx * vx + vy * vy);
            if (lu < 1e-12 || lv < 1e-12) return false;
            double ang = Math.Acos(Math.Clamp((ux * vx + uy * vy) / (lu * lv), -1.0, 1.0));
            maxDev = Math.Max(maxDev, Math.Abs(ang - Math.PI / 2));
        }

        quality = Math.Max(0.0, 1.0 - (2.0 / Math.PI) * maxDev);
        quad = (order[0], order[1], order[2], order[3]);
        return true;
    }

    private static double SignedArea(Point[] p)
    {
        double a = 0;
        for (int i = 0; i < p.Length; i++)
        {
            Point p0 = p[i], p1 = p[(i + 1) % p.Length];
            a += p0.X * p1.Y - p1.X * p0.Y;
        }
        return a * 0.5;
    }
}
