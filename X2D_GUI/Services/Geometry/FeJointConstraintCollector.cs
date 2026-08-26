using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GeotechSlopeApp.Models.Geometry;

namespace GeotechSlopeApp.Services.Geometry;

/// <summary>
/// Samples enabled geological joint polylines into short constraint segments at the
/// target mesh size, then clips them to the meshed region polygons.
/// </summary>
public static class FeJointConstraintCollector
{
    public static List<(Point A, Point B)> Collect(
        IEnumerable<GeologicalFeature>? features,
        IReadOnlyList<IReadOnlyList<Point>>? meshPolygons,
        double targetSize)
    {
        targetSize = Math.Max(targetSize, 1e-6);
        var raw = new List<(Point A, Point B)>();
        if (features == null)
            return raw;

        foreach (var feature in features)
        {
            if (feature == null || !feature.IsEnabled || feature.Vertices.Count < 2)
                continue;
            for (int i = 0; i + 1 < feature.Vertices.Count; i++)
            {
                var a = new Point(feature.Vertices[i].X, feature.Vertices[i].Y);
                var b = new Point(feature.Vertices[i + 1].X, feature.Vertices[i + 1].Y);
                double len = Dist(a, b);
                if (len < 1e-12)
                    continue;
                int n = Math.Max(1, (int)Math.Ceiling(len / targetSize));
                n = Math.Min(n, 500);
                for (int k = 0; k < n; k++)
                {
                    double t0 = (double)k / n;
                    double t1 = (double)(k + 1) / n;
                    raw.Add((Lerp(a, b, t0), Lerp(a, b, t1)));
                }
            }
        }

        if (meshPolygons == null || meshPolygons.Count == 0)
            return raw;

        var clipped = new List<(Point A, Point B)>();
        foreach (var (a, b) in raw)
        {
            foreach (var poly in meshPolygons)
            {
                if (poly == null || poly.Count < 3)
                    continue;
                var polyXy = poly.Select(p => (p.X, p.Y)).ToList();
                foreach (var seg in FeJointNetworkGenerator.ClipSegmentToPolygon((a.X, a.Y), (b.X, b.Y), polyXy))
                    clipped.Add((new Point(seg.A.X, seg.A.Y), new Point(seg.B.X, seg.B.Y)));
            }
        }

        return clipped;
    }

    private static Point Lerp(Point a, Point b, double t)
        => new(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));

    private static double Dist(Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
