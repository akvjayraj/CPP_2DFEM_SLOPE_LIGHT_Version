using System;
using System.Collections.Generic;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>Shape-quality metrics for unstructured RS2D meshes (T3 and Q4).</summary>
public static class MeshElementQualityMetrics
{
    public sealed class MeshQualityStats
    {
        public double MinAngleDeg { get; init; }
        public double MaxAspectRatio { get; init; }
        public double MaxSkew { get; init; }
        public int TriangleCount { get; init; }
        public int QuadCount { get; init; }
        public int TotalElements => TriangleCount + QuadCount;
    }

    public static MeshQualityStats ComputeWholeMesh(
        IReadOnlyList<Point> nodes,
        IReadOnlyList<Rs2dQuadConverter.QuadResult> quads,
        IReadOnlyList<Rs2dQuadConverter.UnmatchedTri> unmatched)
    {
        double minAngle = 180.0;
        double maxAr = 0.0;
        double maxSkew = 0.0;

        foreach (var q in quads)
        {
            if (!TryQuadMetrics(nodes, (q.Q1, q.Q2, q.Q3, q.Q4),
                    out double minA, out double ar, out double skew))
                continue;
            minAngle = Math.Min(minAngle, minA);
            maxAr = Math.Max(maxAr, ar);
            maxSkew = Math.Max(maxSkew, skew);
        }

        foreach (var t in unmatched)
        {
            if (!TryTriangleMetrics(nodes, t.A, t.B, t.C, out double minA, out double ar))
                continue;
            minAngle = Math.Min(minAngle, minA);
            maxAr = Math.Max(maxAr, ar);
        }

        int triCount = unmatched.Count;
        int quadCount = quads.Count;
        if (triCount + quadCount == 0)
            return new MeshQualityStats();

        return new MeshQualityStats
        {
            MinAngleDeg = minAngle > 179.0 ? 0.0 : minAngle,
            MaxAspectRatio = maxAr,
            MaxSkew = maxSkew,
            TriangleCount = triCount,
            QuadCount = quadCount,
        };
    }

    public static bool TryTriangleMetrics(
        IReadOnlyList<Point> nodes, int a, int b, int c,
        out double minAngleDeg, out double maxAspectRatio)
    {
        minAngleDeg = 0;
        maxAspectRatio = 0;
        if (a < 0 || b < 0 || c < 0 || a >= nodes.Count || b >= nodes.Count || c >= nodes.Count)
            return false;

        var pa = nodes[a]; var pb = nodes[b]; var pc = nodes[c];
        double ab = Dist(pa, pb), bc = Dist(pb, pc), ca = Dist(pc, pa);
        if (ab < 1e-12 || bc < 1e-12 || ca < 1e-12) return false;

        double angA = AngleAt(pb, pa, pc);
        double angB = AngleAt(pa, pb, pc);
        double angC = 180.0 - angA - angB;
        minAngleDeg = Math.Min(angA, Math.Min(angB, angC));

        double s = (ab + bc + ca) / 2.0;
        double area = Math.Abs(Cross(pa, pb, pc)) * 0.5;
        if (area <= 1e-14) return false;
        double inRadius = area / s;
        maxAspectRatio = Math.Max(ab, Math.Max(bc, ca)) / (2.0 * inRadius);
        return true;
    }

    public static bool TryQuadMetrics(
        IReadOnlyList<Point> nodes,
        (int q1, int q2, int q3, int q4) quad,
        out double minAngleDeg,
        out double aspectRatio,
        out double skew)
        => Rs2dQuadConverter.TryQuadMetrics(nodes, quad, out minAngleDeg, out aspectRatio, out skew);

    private static double Dist(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Cross(Point a, Point b, Point c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static double AngleAt(Point prev, Point pivot, Point next)
    {
        double v1x = prev.X - pivot.X, v1y = prev.Y - pivot.Y;
        double v2x = next.X - pivot.X, v2y = next.Y - pivot.Y;
        double dot = v1x * v2x + v1y * v2y;
        double cross = v1x * v2y - v1y * v2x;
        return Math.Atan2(Math.Abs(cross), dot) * 180.0 / Math.PI;
    }
}
