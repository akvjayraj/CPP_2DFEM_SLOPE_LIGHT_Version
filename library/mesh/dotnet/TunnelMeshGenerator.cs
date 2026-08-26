using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// RS2D/Plaxis-style CDT mesh with radial ring refinement.
/// Handles both circular and polygon-shaped excavations (holes):
/// – Circular tunnels: concentric-circle rings at growing radii
/// – Polygon holes: outward-expanded offset rings around the boundary
/// All holes and outer boundary are discretized at nearBoundarySize; gradation
/// increases toward the far field, matching RS2D / Plaxis behaviour.
/// </summary>
public static class TunnelMeshGenerator
{
    public sealed class Result
    {
        /// <summary>Global node list shared by all triangle sets.</summary>
        public IReadOnlyList<Point> Nodes { get; init; } = Array.Empty<Point>();
        /// <summary>Active (soil/rock) triangles — inside outer polygon, outside all holes.</summary>
        public IReadOnlyList<(int a, int b, int c)> ActiveTriangles { get; init; } = Array.Empty<(int, int, int)>();
        /// <summary>Hole-interior triangles (visualisation only, normally deactivated in analysis).</summary>
        public IReadOnlyList<(int a, int b, int c)> TunnelTriangles { get; init; } = Array.Empty<(int, int, int)>();
        public double MinAngleDeg    { get; init; }
        public double MaxAspectRatio { get; init; }
    }

    /// <summary>
    /// Generate a CDT + ring-refinement mesh.
    /// </summary>
    /// <param name="outerPolygon">Closed outer boundary in axis space.</param>
    /// <param name="circleHoles">Circular excavations: (centre, radius) in axis space.</param>
    /// <param name="polygonHoles">Polygon-shaped excavations in axis space (e.g. arch cavern, rectangular trench).</param>
    /// <param name="farFieldEdgeLength">Target edge length in the far field (h₀).</param>
    /// <param name="nearBoundarySize">Target edge length at all hole and outer boundaries (hₜ).</param>
    /// <param name="ringCount">Number of refinement rings around each hole.</param>
    /// <param name="ringGrowthFactor">Spacing multiplier between successive rings (e.g. 1.2).</param>
    /// <param name="firstRingThickness">Thickness of the first ring (axis units).</param>
    /// <param name="maxNodes">Safety cap on total node count.</param>
    public static Result Generate(
        IReadOnlyList<Point>                          outerPolygon,
        IReadOnlyList<(Point Center, double Radius)>  circleHoles,
        IReadOnlyList<IReadOnlyList<Point>>           polygonHoles,
        double farFieldEdgeLength,
        double nearBoundarySize,
        int    ringCount,
        double ringGrowthFactor,
        double firstRingThickness,
        int    maxNodes = 60_000)
    {
        if (outerPolygon == null || outerPolygon.Count < 3)
            return new Result();

        farFieldEdgeLength = Math.Max(farFieldEdgeLength,  1e-6);
        nearBoundarySize   = Math.Max(nearBoundarySize,    1e-6);
        firstRingThickness = Math.Max(firstRingThickness,  nearBoundarySize * 0.5);
        ringCount          = Math.Clamp(ringCount, 0, 20);
        ringGrowthFactor   = Math.Clamp(ringGrowthFactor, 1.05, 3.0);

        circleHoles  ??= Array.Empty<(Point, double)>();
        polygonHoles ??= Array.Empty<IReadOnlyList<Point>>();

        var allPts = new List<Point>(4096);

        // ── 1. Sample outer boundary ───────────────────────────────────────
        SamplePolygonEdges(outerPolygon, nearBoundarySize, allPts);

        // ── 2. Circular holes: boundary + concentric rings outward ─────────
        var circleDefs = circleHoles.Where(c => c.Radius > 1e-9).ToList();
        foreach (var (center, radius) in circleDefs)
        {
            if (allPts.Count >= maxNodes) break;

            // Tunnel wall
            SampleCircle(center, radius, nearBoundarySize, allPts);

            // Outward rings
            double spacing = nearBoundarySize;
            double ringR   = radius + firstRingThickness;
            for (int k = 0; k < ringCount && allPts.Count < maxNodes; k++)
            {
                if (spacing >= farFieldEdgeLength) break;

                // Abort ring if it would escape the outer polygon
                if (!CircleFullyInside(center, ringR, outerPolygon)) break;

                SampleCircle(center, ringR, spacing, allPts);
                ringR   += spacing;
                spacing *= ringGrowthFactor;
            }
        }

        // ── 3. Polygon holes: boundary + outward-expanded rings ────────────
        var validPolyHoles = polygonHoles
            .Where(h => h != null && h.Count >= 3)
            .ToList();

        foreach (var hole in validPolyHoles)
        {
            if (allPts.Count >= maxNodes) break;

            // Hole boundary at near-boundary density
            SamplePolygonEdges(hole, nearBoundarySize, allPts);

            // Outward-expanded rings (moving INTO the outer domain, away from the hole)
            double spacing = nearBoundarySize;
            double offset  = firstRingThickness;
            for (int k = 0; k < ringCount && allPts.Count < maxNodes; k++)
            {
                if (spacing >= farFieldEdgeLength) break;

                var ring = ComputeOutwardOffsetPolygon(hole, offset, outerPolygon);
                if (ring == null || ring.Count < 3) break;

                SamplePolygonEdges(ring, spacing, allPts);
                offset  += spacing;
                spacing *= ringGrowthFactor;
            }
        }

        // ── 4. Steiner grid for the outer domain (excluding all holes) ─────
        AddInteriorGrid(outerPolygon, circleDefs, validPolyHoles,
                        farFieldEdgeLength, allPts, maxNodes);

        // ── 5. Deduplicate ─────────────────────────────────────────────────
        double mergeEps = nearBoundarySize * 0.05;
        allPts = MergeDuplicates(allPts, mergeEps);

        if (allPts.Count < 3) return new Result();

        // ── 6. Bowyer-Watson triangulation ─────────────────────────────────
        var allTris = BowyerWatsonTriangulator.Triangulate(allPts);

        // ── 7. Classify triangles ──────────────────────────────────────────
        var activeTris = new List<(int, int, int)>();
        var tunnelTris = new List<(int, int, int)>();

        foreach (var (ai, bi, ci) in allTris)
        {
            double cx = (allPts[ai].X + allPts[bi].X + allPts[ci].X) / 3;
            double cy = (allPts[ai].Y + allPts[bi].Y + allPts[ci].Y) / 3;
            var c = new Point(cx, cy);

            bool inOuter = GradedSurfaceMeshGenerator.PointInPolygon(c, outerPolygon);
            bool inCircleHole  = circleDefs.Any(t => Dist(c, t.Center) <= t.Radius * 1.001);
            bool inPolyHole    = validPolyHoles.Any(h => GradedSurfaceMeshGenerator.PointInPolygon(c, h));

            if (!inOuter) continue;
            if (inCircleHole || inPolyHole)
                tunnelTris.Add((ai, bi, ci));
            else
                activeTris.Add((ai, bi, ci));
        }

        // ── 8. Quality stats ───────────────────────────────────────────────
        ComputeQualityStats(allPts, activeTris.Concat(tunnelTris).ToList(),
                            out double minAngle, out double maxAR);

        return new Result
        {
            Nodes           = allPts,
            ActiveTriangles = activeTris,
            TunnelTriangles = tunnelTris,
            MinAngleDeg     = minAngle,
            MaxAspectRatio  = maxAR,
        };
    }

    // ── Private helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Compute an outward-expanded polygon around <paramref name="hole"/> at <paramref name="dist"/>.
    /// Moves each vertex outward (away from the hole centroid) using edge-bisector normals.
    /// Returns null if any expanded vertex falls outside <paramref name="outerBound"/>.
    /// </summary>
    private static List<Point>? ComputeOutwardOffsetPolygon(
        IReadOnlyList<Point> hole,
        double               dist,
        IReadOnlyList<Point> outerBound)
    {
        int n   = hole.Count;
        var res = new List<Point>(n);

        // Determine winding: for a CCW hole the outward normal is the LEFT normal of each edge.
        // We negate the inward normals of GradedSurfaceMeshGenerator.ComputeInwardOffsetPolygon.
        for (int i = 0; i < n; i++)
        {
            var prev = hole[(i + n - 1) % n];
            var cur  = hole[i];
            var next = hole[(i + 1) % n];

            // Edge direction normals (inward for CCW = right-perpendicular)
            var d1 = Normalize(cur.X - prev.X, cur.Y - prev.Y);
            var n1x =  d1.y; var n1y = -d1.x;   // right-perp = inward for CCW
            // Outward = negate
            n1x = -n1x; n1y = -n1y;

            var d2 = Normalize(next.X - cur.X, next.Y - cur.Y);
            var n2x =  d2.y; var n2y = -d2.x;
            n2x = -n2x; n2y = -n2y;

            double bx = n1x + n2x, by = n1y + n2y;
            double bLen = Math.Sqrt(bx * bx + by * by);

            double px, py;
            if (bLen < 1e-8)
            {
                px = cur.X + n1x * dist;
                py = cur.Y + n1y * dist;
            }
            else
            {
                bx /= bLen; by /= bLen;
                double cosHalf = n1x * n2x + n1y * n2y;
                double sinHalf = Math.Sqrt(Math.Max(0, (1 - cosHalf) / 2));
                double scale   = sinHalf < 0.15 ? dist : dist / sinHalf;
                scale = Math.Min(scale, dist * 4); // cap at sharp corners
                px = cur.X + bx * scale;
                py = cur.Y + by * scale;
            }
            res.Add(new Point(px, py));
        }

        // Validate: all expanded vertices must be inside the outer boundary
        // AND outside the original hole (i.e. actually expanded outward)
        if (res.Any(p => !GradedSurfaceMeshGenerator.PointInPolygon(p, outerBound)))
            return null;

        return res;
    }

    private static void SamplePolygonEdges(IReadOnlyList<Point> poly, double spacing, List<Point> output)
    {
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            double len = Dist(a, b);
            if (len < 1e-10) continue;
            int segs = Math.Max(1, (int)Math.Ceiling(len / spacing));
            for (int j = 0; j < segs; j++)
            {
                double t = (double)j / segs;
                output.Add(new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
            }
        }
    }

    private static void SampleCircle(Point center, double radius, double spacing, List<Point> output)
    {
        int n = Math.Max(8, (int)Math.Ceiling(2.0 * Math.PI * radius / spacing));
        for (int i = 0; i < n; i++)
        {
            double theta = i * 2.0 * Math.PI / n;
            output.Add(new Point(center.X + radius * Math.Cos(theta),
                                 center.Y + radius * Math.Sin(theta)));
        }
    }

    private static void AddInteriorGrid(
        IReadOnlyList<Point>                         outer,
        IReadOnlyList<(Point Center, double Radius)> circles,
        IReadOnlyList<IReadOnlyList<Point>>          polyHoles,
        double                                       spacing,
        List<Point>                                  output,
        int                                          maxNodes)
    {
        double minX = outer.Min(p => p.X), maxX = outer.Max(p => p.X);
        double minY = outer.Min(p => p.Y), maxY = outer.Max(p => p.Y);

        for (double y = minY + spacing * 0.5; y < maxY && output.Count < maxNodes; y += spacing)
        for (double x = minX + spacing * 0.5; x < maxX && output.Count < maxNodes; x += spacing)
        {
            var pt = new Point(x, y);
            if (!GradedSurfaceMeshGenerator.PointInPolygon(pt, outer)) continue;
            if (circles.Any(c => Dist(pt, c.Center) <= c.Radius)) continue;
            if (polyHoles.Any(h => GradedSurfaceMeshGenerator.PointInPolygon(pt, h))) continue;
            output.Add(pt);
        }
    }

    private static bool CircleFullyInside(Point center, double r, IReadOnlyList<Point> poly)
    {
        int samples = Math.Max(8, (int)Math.Ceiling(2 * Math.PI * r / r)); // ~8 checks
        for (int i = 0; i < 8; i++)
        {
            double theta = i * Math.PI / 4;
            var p = new Point(center.X + r * Math.Cos(theta), center.Y + r * Math.Sin(theta));
            if (!GradedSurfaceMeshGenerator.PointInPolygon(p, poly)) return false;
        }
        return true;
    }

    private static List<Point> MergeDuplicates(List<Point> pts, double eps)
    {
        var result = new List<Point>(pts.Count);
        double eps2 = eps * eps;
        foreach (var p in pts)
        {
            bool dup = false;
            foreach (var r in result)
            {
                double dx = p.X - r.X, dy = p.Y - r.Y;
                if (dx * dx + dy * dy < eps2) { dup = true; break; }
            }
            if (!dup) result.Add(p);
        }
        return result;
    }

    private static double Dist(Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static (double x, double y) Normalize(double dx, double dy)
    {
        double len = Math.Sqrt(dx * dx + dy * dy);
        return len < 1e-10 ? (0, 0) : (dx / len, dy / len);
    }

    private static void ComputeQualityStats(
        IReadOnlyList<Point> pts,
        IReadOnlyList<(int a, int b, int c)> tris,
        out double minAngle, out double maxAR)
    {
        minAngle = 180; maxAR = 0;
        foreach (var (ai, bi, ci) in tris)
        {
            var a = pts[ai]; var b = pts[bi]; var c = pts[ci];
            double ab = Dist(a, b), bc = Dist(b, c), ca = Dist(c, a);
            if (ab < 1e-12 || bc < 1e-12 || ca < 1e-12) continue;
            double angA = Math.Acos(Math.Clamp((ab*ab + ca*ca - bc*bc) / (2*ab*ca), -1, 1)) * 180 / Math.PI;
            double angB = Math.Acos(Math.Clamp((ab*ab + bc*bc - ca*ca) / (2*ab*bc), -1, 1)) * 180 / Math.PI;
            minAngle = Math.Min(minAngle, Math.Min(angA, Math.Min(angB, 180 - angA - angB)));
            double s = (ab + bc + ca) / 2;
            double area = Math.Abs((b.X - a.X)*(c.Y - a.Y) - (c.X - a.X)*(b.Y - a.Y)) / 2;
            if (area > 1e-12) maxAR = Math.Max(maxAR, Math.Max(ab, Math.Max(bc, ca)) / (2 * area / s));
        }
        if (tris.Count == 0) { minAngle = 0; maxAR = 0; }
    }
}
