using System;
using System.Collections.Generic;
using System.Linq;

namespace GeotechSlopeApp.Services.Geometry;

/// <summary>Merges near-coincident vertices (CAD "heal" / close gaps).</summary>
public static class GeometryHealService
{
    public static List<(int StableId, double X, double Y)> HealVertices(
        IReadOnlyList<(int StableId, double X, double Y)> vertices,
        double tolerance)
    {
        if (vertices == null || vertices.Count == 0)
            return new List<(int, double, double)>();

        var tol2 = Math.Max(1e-18, tolerance * tolerance);
        var result = vertices.ToList();
        var clusters = new List<List<int>>();
        var assigned = new bool[result.Count];

        for (var i = 0; i < result.Count; i++)
        {
            if (assigned[i]) continue;
            var cluster = new List<int> { i };
            assigned[i] = true;
            for (var j = i + 1; j < result.Count; j++)
            {
                if (assigned[j]) continue;
                var dx = result[i].X - result[j].X;
                var dy = result[i].Y - result[j].Y;
                if (dx * dx + dy * dy <= tol2)
                {
                    cluster.Add(j);
                    assigned[j] = true;
                }
            }

            clusters.Add(cluster);
        }

        var healed = new List<(int StableId, double X, double Y)>(result.Count);
        foreach (var cluster in clusters)
        {
            var ax = cluster.Average(i => result[i].X);
            var ay = cluster.Average(i => result[i].Y);
            foreach (var i in cluster)
                healed.Add((result[i].StableId, ax, ay));
        }

        return healed.OrderBy(v => v.StableId).ToList();
    }

    /// <summary>Splits an edge at a point projection; returns the split point if within tolerance of the segment.</summary>
    public static bool TrySplitSegment(
        (double X, double Y) a,
        (double X, double Y) b,
        (double X, double Y) p,
        out (double X, double Y) split,
        double tolerance)
    {
        split = default;
        var vx = b.X - a.X;
        var vy = b.Y - a.Y;
        var len2 = vx * vx + vy * vy;
        if (len2 < 1e-18) return false;
        var t = ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / len2;
        if (t <= 1e-9 || t >= 1 - 1e-9) return false;
        var qx = a.X + t * vx;
        var qy = a.Y + t * vy;
        var dx = p.X - qx;
        var dy = p.Y - qy;
        if (dx * dx + dy * dy > tolerance * tolerance) return false;
        split = (qx, qy);
        return true;
    }

    /// <summary>Extends endpoint of AB toward CD intersection when segments nearly meet.</summary>
    public static bool TryExtendToIntersection(
        (double X, double Y) a,
        (double X, double Y) b,
        (double X, double Y) c,
        (double X, double Y) d,
        out (double X, double Y) intersection)
    {
        intersection = default;
        var den = (a.X - b.X) * (c.Y - d.Y) - (a.Y - b.Y) * (c.X - d.X);
        if (Math.Abs(den) < 1e-18) return false;
        var t = ((a.X - c.X) * (c.Y - d.Y) - (a.Y - c.Y) * (c.X - d.X)) / den;
        intersection = (a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
        return true;
    }
}
