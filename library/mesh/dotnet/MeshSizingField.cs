using System;
using System.Collections.Generic;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// A continuous background element-sizing field h(x) — the single source of truth for mesh
/// density used by the hybrid mesh driver. This mirrors how the leading geotech meshers
/// (Plaxis, RS2, OptumG2) drive density: a coarse interior "base" size that refines toward
/// geometric features (domain boundaries, excavation/hole boundaries, material-layer
/// interfaces) and toward explicitly requested refinement points/circles. The same field is
/// reused for solution-adaptive refinement (Phase 3): an error estimator simply injects extra
/// refinement sources and the field is rebuilt.
///
/// The field is feature-distance based: at a query point p, the target size is the smallest of
/// the base size and, for every feature, <c>featureNearSize * growth^(distance/featureNearSize)</c>,
/// clamped to <see cref="MinSize"/>. Larger growth ⇒ faster coarsening away from features.
/// </summary>
public sealed class MeshSizingField
{
    /// <summary>Target element size far from every feature (interior global size).</summary>
    public double BaseSize { get; }

    /// <summary>Hard floor on element size anywhere in the domain.</summary>
    public double MinSize { get; }

    /// <summary>Geometric growth ratio of size per characteristic-length step away from a feature.</summary>
    public double GrowthFactor { get; }

    // Feature sources. Segments cover boundaries / holes / layer interfaces; points and circles
    // cover explicit local refinement (e.g. load application points, tunnel springlines, or
    // adaptive error hotspots fed in by Phase 3).
    private readonly List<(Point A, Point B, double NearSize)> _segments = new();
    private readonly List<(Point C, double NearSize, double Radius)> _points = new();

    /// <param name="baseSize">Interior/global target size (must be &gt; 0).</param>
    /// <param name="minSize">Lower clamp on size (defaults to baseSize/64, floored at 1e-6).</param>
    /// <param name="growthFactor">Per-step coarsening ratio away from features (clamped 1.05–3.0).</param>
    public MeshSizingField(double baseSize, double minSize = 0.0, double growthFactor = 1.3)
    {
        BaseSize = Math.Max(baseSize, 1e-6);
        MinSize = minSize > 0 ? Math.Min(minSize, BaseSize) : Math.Max(BaseSize / 64.0, 1e-6);
        GrowthFactor = Math.Clamp(growthFactor, 1.05, 3.0);
    }

    /// <summary>Number of feature sources currently registered (segments + point/circle sources).</summary>
    public int FeatureCount => _segments.Count + _points.Count;

    /// <summary>Add one refinement segment (a boundary/interface edge) with a local near size.</summary>
    public void AddSegment(Point a, Point b, double nearSize)
    {
        if (double.IsNaN(nearSize) || nearSize <= 0) return;
        _segments.Add((a, b, Math.Max(nearSize, MinSize)));
    }

    /// <summary>Add every edge of a closed polyline/polygon as refinement segments.</summary>
    /// <param name="closed">When true, the last→first edge is also added.</param>
    public void AddPolyline(IReadOnlyList<Point> pts, double nearSize, bool closed)
    {
        if (pts == null || pts.Count < 2) return;
        for (int i = 0; i < pts.Count - 1; i++)
            AddSegment(pts[i], pts[i + 1], nearSize);
        if (closed && pts.Count >= 3)
            AddSegment(pts[^1], pts[0], nearSize);
    }

    /// <summary>Add a local refinement source at a point, optionally with an influence radius
    /// inside which the near size is held constant before growth begins.</summary>
    public void AddPointRefinement(Point center, double nearSize, double radius = 0.0)
    {
        if (double.IsNaN(nearSize) || nearSize <= 0) return;
        _points.Add((center, Math.Max(nearSize, MinSize), Math.Max(radius, 0.0)));
    }

    /// <summary>Add a circular refinement (e.g. a circular excavation) by sampling its perimeter
    /// as refinement segments at roughly the near size.</summary>
    public void AddCircle(Point center, double radius, double nearSize)
    {
        if (radius <= 0 || nearSize <= 0) return;
        int n = Math.Clamp((int)Math.Ceiling(2 * Math.PI * radius / Math.Max(nearSize, MinSize)), 8, 720);
        Point Prev = new(center.X + radius, center.Y);
        for (int i = 1; i <= n; i++)
        {
            double t = 2 * Math.PI * i / n;
            Point cur = new(center.X + radius * Math.Cos(t), center.Y + radius * Math.Sin(t));
            AddSegment(Prev, cur, nearSize);
            Prev = cur;
        }
    }

    /// <summary>Evaluate the target element size at <paramref name="p"/> (axis space).</summary>
    public double SizeAt(Point p)
    {
        double size = BaseSize;

        for (int i = 0; i < _segments.Count; i++)
        {
            var s = _segments[i];
            double d = DistancePointToSegment(p, s.A, s.B);
            double h = GrownSize(s.NearSize, d);
            if (h < size) size = h;
            if (size <= MinSize) return MinSize;
        }

        for (int i = 0; i < _points.Count; i++)
        {
            var pt = _points[i];
            double d = Math.Max(0.0, Distance(p, pt.C) - pt.Radius);
            double h = GrownSize(pt.NearSize, d);
            if (h < size) size = h;
            if (size <= MinSize) return MinSize;
        }

        return Math.Clamp(size, MinSize, BaseSize);
    }

    /// <summary>Sample the field over a polygon and report representative near/global sizes plus a
    /// suggested boundary-layer count and growth — convenient adapter for engines (like
    /// <c>GradedSurfaceMeshGenerator.GenerateConforming</c>) that take scalar per-region params.</summary>
    public RegionSizing SampleRegion(IReadOnlyList<Point> polygon)
    {
        if (polygon == null || polygon.Count < 3)
            return new RegionSizing(MinSize, BaseSize, 2, GrowthFactor);

        // Near size: finest size found along the polygon boundary (where features bite hardest).
        double near = BaseSize;
        for (int i = 0; i < polygon.Count; i++)
        {
            Point a = polygon[i];
            Point b = polygon[(i + 1) % polygon.Count];
            near = Math.Min(near, SizeAt(a));
            near = Math.Min(near, SizeAt(new Point((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5)));
        }
        near = Math.Clamp(near, MinSize, BaseSize);

        // Global size: size at the polygon centroid (interior, far from features).
        double global = Math.Clamp(SizeAt(Centroid(polygon)), near, BaseSize);

        // Boundary layers: enough inward rings for the size to grow from near→global.
        int layers = 2;
        if (global > near * 1.0001)
            layers = Math.Clamp((int)Math.Ceiling(Math.Log(global / near) / Math.Log(GrowthFactor)), 2, 12);

        return new RegionSizing(near, global, layers, GrowthFactor);
    }

    private double GrownSize(double nearSize, double distance)
    {
        if (distance <= 0) return nearSize;
        // Continuous geometric growth: number of characteristic steps = distance / nearSize.
        double steps = distance / nearSize;
        double h = nearSize * Math.Pow(GrowthFactor, steps);
        return h > BaseSize ? BaseSize : h;
    }

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double DistancePointToSegment(Point p, Point a, Point b)
    {
        double vx = b.X - a.X, vy = b.Y - a.Y;
        double wx = p.X - a.X, wy = p.Y - a.Y;
        double len2 = vx * vx + vy * vy;
        if (len2 <= 1e-18)
            return Distance(p, a);
        double t = (wx * vx + wy * vy) / len2;
        t = Math.Clamp(t, 0.0, 1.0);
        double cx = a.X + t * vx, cy = a.Y + t * vy;
        double dx = p.X - cx, dy = p.Y - cy;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static Point Centroid(IReadOnlyList<Point> poly)
    {
        // Area-weighted polygon centroid (falls back to vertex average for degenerate area).
        double a2 = 0, cx = 0, cy = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            Point p0 = poly[i];
            Point p1 = poly[(i + 1) % poly.Count];
            double cross = p0.X * p1.Y - p1.X * p0.Y;
            a2 += cross;
            cx += (p0.X + p1.X) * cross;
            cy += (p0.Y + p1.Y) * cross;
        }
        if (Math.Abs(a2) < 1e-12)
        {
            double sx = 0, sy = 0;
            foreach (var p in poly) { sx += p.X; sy += p.Y; }
            return new Point(sx / poly.Count, sy / poly.Count);
        }
        return new Point(cx / (3 * a2), cy / (3 * a2));
    }
}

/// <summary>Scalar per-region sizing parameters derived from a <see cref="MeshSizingField"/>,
/// shaped to feed existing engines that take (nearSize, globalSize, layers, growth).</summary>
public readonly record struct RegionSizing(double NearSize, double GlobalSize, int BoundaryLayers, double GrowthFactor);
