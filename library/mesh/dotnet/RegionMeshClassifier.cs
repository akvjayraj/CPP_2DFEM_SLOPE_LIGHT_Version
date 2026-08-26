using System;
using System.Collections.Generic;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>How a region should be meshed by the hybrid driver.</summary>
public enum RegionMeshStrategy
{
    /// <summary>Map a clean 4-corner region with a structured transfinite quad grid (best element
    /// quality, aligned with stratigraphy — preferred by Plaxis/RS2 for embankments and soil columns).</summary>
    StructuredTransfinite,

    /// <summary>Fill an arbitrary / non-mappable region with a graded conforming Delaunay (CDT)
    /// triangle mesh (handles holes, re-entrant corners, curved boundaries).</summary>
    UnstructuredCdt
}

/// <summary>Outcome of classifying one region polygon.</summary>
public sealed class RegionClassification
{
    public int SurfaceNumber { get; init; }
    public RegionMeshStrategy Strategy { get; init; }

    /// <summary>For <see cref="RegionMeshStrategy.StructuredTransfinite"/>: the four ordered corners
    /// (CCW) used as the transfinite map's logical box. Null otherwise.</summary>
    public IReadOnlyList<Point>? Corners { get; init; }

    /// <summary>True when the structured quad is an axis-aligned rectangle. Only such blocks can be
    /// conformally stitched into a surrounding CDT mesh with a guarantee of no hanging nodes (their
    /// perimeter spacing matches the engine's uniform edge sampling on every edge).</summary>
    public bool IsAxisAlignedRectangle { get; init; }

    /// <summary>Human-readable reason the strategy was chosen (for diagnostics / mesh report).</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Classifies geometry regions into the hybrid meshing strategy. The policy matches industry
/// practice: regions that reduce to a well-shaped quadrilateral (4 dominant corners, convex,
/// no interior holes) are mapped structurally; everything else falls back to the proven graded
/// CDT engine. Keeping this pure and side-effect-free makes the routing testable and lets the
/// hybrid driver, Phase 2 stitching, and Phase 3 adaptivity all share one decision function.
/// </summary>
public static class RegionMeshClassifier
{
    /// <summary>Max deviation (radians) from a straight line for a vertex to be treated as collinear
    /// (and thus folded away when counting the region's dominant corners). ~5°.</summary>
    private const double CollinearAngleTol = 0.087266;

    /// <summary>Classify a single region polygon.</summary>
    /// <param name="surfaceNumber">Region/surface id.</param>
    /// <param name="polygon">Closed boundary (axis space; last point need not repeat first).</param>
    /// <param name="hasHoles">True if the region contains excavations/voids/nested surfaces.</param>
    /// <param name="allowStructured">Global toggle; when false everything routes to CDT.</param>
    public static RegionClassification Classify(
        int surfaceNumber,
        IReadOnlyList<Point> polygon,
        bool hasHoles,
        bool allowStructured = true)
    {
        if (!allowStructured)
            return Cdt(surfaceNumber, "structured routing disabled");

        if (polygon == null || polygon.Count < 3)
            return Cdt(surfaceNumber, "degenerate polygon");

        if (hasHoles)
            return Cdt(surfaceNumber, "region contains holes/excavations");

        var corners = DominantCorners(polygon);
        if (corners.Count != 4)
            return Cdt(surfaceNumber, $"{corners.Count} dominant corners (need 4)");

        if (!IsConvex(corners))
            return Cdt(surfaceNumber, "quad corners are non-convex");

        if (!IsWellShapedQuad(corners))
            return Cdt(surfaceNumber, "quad too skewed for transfinite map");

        bool isRect = IsAxisAlignedRectangle(corners);
        return new RegionClassification
        {
            SurfaceNumber = surfaceNumber,
            Strategy = RegionMeshStrategy.StructuredTransfinite,
            Corners = corners,
            IsAxisAlignedRectangle = isRect,
            Reason = isRect ? "axis-aligned rectangle → transfinite (stitchable)"
                            : "clean convex quad → transfinite"
        };
    }

    /// <summary>True when the four corners form a rectangle with edges parallel to the X and Y axes.</summary>
    public static bool IsAxisAlignedRectangle(IReadOnlyList<Point> corners, double relTol = 1e-3)
    {
        if (corners == null || corners.Count != 4) return false;

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in corners)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        double w = maxX - minX, h = maxY - minY;
        if (w <= 1e-9 || h <= 1e-9) return false;
        double tol = Math.Max(w, h) * relTol;

        // Every corner must coincide with a bounding-box corner, and all four box corners must be used.
        int cMinMin = 0, cMinMax = 0, cMaxMin = 0, cMaxMax = 0;
        foreach (var p in corners)
        {
            bool atMinX = Math.Abs(p.X - minX) <= tol, atMaxX = Math.Abs(p.X - maxX) <= tol;
            bool atMinY = Math.Abs(p.Y - minY) <= tol, atMaxY = Math.Abs(p.Y - maxY) <= tol;
            if (!((atMinX || atMaxX) && (atMinY || atMaxY))) return false;
            if (atMinX && atMinY) cMinMin++;
            else if (atMinX && atMaxY) cMinMax++;
            else if (atMaxX && atMinY) cMaxMin++;
            else if (atMaxX && atMaxY) cMaxMax++;
        }
        return cMinMin == 1 && cMinMax == 1 && cMaxMin == 1 && cMaxMax == 1;
    }

    private static RegionClassification Cdt(int surface, string reason) => new()
    {
        SurfaceNumber = surface,
        Strategy = RegionMeshStrategy.UnstructuredCdt,
        Corners = null,
        Reason = reason
    };

    /// <summary>Reduce a polygon to its dominant corners by dropping near-collinear vertices.</summary>
    public static IReadOnlyList<Point> DominantCorners(IReadOnlyList<Point> polygon)
    {
        var pts = Dedup(polygon, 1e-9);
        int n = pts.Count;
        if (n < 3) return pts;

        var corners = new List<Point>(n);
        for (int i = 0; i < n; i++)
        {
            Point prev = pts[(i - 1 + n) % n];
            Point cur = pts[i];
            Point next = pts[(i + 1) % n];
            if (TurnAngle(prev, cur, next) > CollinearAngleTol)
                corners.Add(cur);
        }
        return corners.Count >= 3 ? corners : pts;
    }

    private static List<Point> Dedup(IReadOnlyList<Point> pts, double eps)
    {
        var outp = new List<Point>(pts.Count);
        foreach (var p in pts)
        {
            if (outp.Count > 0)
            {
                var q = outp[^1];
                if (Math.Abs(p.X - q.X) < eps && Math.Abs(p.Y - q.Y) < eps) continue;
            }
            outp.Add(p);
        }
        if (outp.Count >= 2)
        {
            var f = outp[0]; var l = outp[^1];
            if (Math.Abs(f.X - l.X) < eps && Math.Abs(f.Y - l.Y) < eps)
                outp.RemoveAt(outp.Count - 1);
        }
        return outp;
    }

    private static double TurnAngle(Point a, Point b, Point c)
    {
        double v1x = b.X - a.X, v1y = b.Y - a.Y;
        double v2x = c.X - b.X, v2y = c.Y - b.Y;
        double l1 = Math.Sqrt(v1x * v1x + v1y * v1y);
        double l2 = Math.Sqrt(v2x * v2x + v2y * v2y);
        if (l1 < 1e-12 || l2 < 1e-12) return 0.0;
        double cos = (v1x * v2x + v1y * v2y) / (l1 * l2);
        return Math.Acos(Math.Clamp(cos, -1.0, 1.0));
    }

    private static bool IsConvex(IReadOnlyList<Point> poly)
    {
        int n = poly.Count;
        if (n < 3) return false;
        bool gotNeg = false, gotPos = false;
        for (int i = 0; i < n; i++)
        {
            Point a = poly[i];
            Point b = poly[(i + 1) % n];
            Point c = poly[(i + 2) % n];
            double cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
            if (cross < -1e-9) gotNeg = true;
            else if (cross > 1e-9) gotPos = true;
            if (gotNeg && gotPos) return false;
        }
        return true;
    }

    /// <summary>Reject quads whose opposite-side length ratio is extreme (degenerate transfinite map).</summary>
    private static bool IsWellShapedQuad(IReadOnlyList<Point> q)
    {
        if (q.Count != 4) return false;
        double s0 = Len(q[0], q[1]);
        double s1 = Len(q[1], q[2]);
        double s2 = Len(q[2], q[3]);
        double s3 = Len(q[3], q[0]);
        double minSide = Math.Min(Math.Min(s0, s1), Math.Min(s2, s3));
        if (minSide < 1e-9) return false;

        // Opposite sides should be comparable for a clean structured map.
        double r0 = Ratio(s0, s2);
        double r1 = Ratio(s1, s3);
        const double maxOppRatio = 6.0;
        return r0 <= maxOppRatio && r1 <= maxOppRatio;
    }

    private static double Ratio(double a, double b)
    {
        double lo = Math.Min(a, b), hi = Math.Max(a, b);
        return lo < 1e-12 ? double.MaxValue : hi / lo;
    }

    private static double Len(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
