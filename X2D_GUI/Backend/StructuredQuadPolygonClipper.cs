using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Clips structured-mesh corner quads against an arbitrary (possibly concave) geometry polygon
/// and decomposes the overlap into Q4 pieces for boundary fill along sloped faces.
/// </summary>
public static class StructuredQuadPolygonClipper
{
    public const double Tol = 1e-9;
    public const double NodeMergeTol = 1e-6;

    /// <summary>
    /// Returns one or more convex polygons (3–4 vertices) representing quad ∩ geometryPolygon.
    /// Each piece is CCW and suitable for Q4/Q8 boundary fill.
    /// </summary>
    public static List<IReadOnlyList<(double x, double y)>> ClipQuadToPolygon(
        IReadOnlyList<(double x, double y)> quad,
        IReadOnlyList<(double x, double y)> geometryPolygon)
    {
        var result = new List<IReadOnlyList<(double x, double y)>>();
        if (quad.Count < 3 || geometryPolygon.Count < 3)
            return result;

        var subject = EnsureCounterClockwise(quad.ToList());
        if (subject.Count < 3)
            return result;

        if (!PolygonEarTriangulation.TryTriangulate(
                geometryPolygon.Select(p => new Point(p.x, p.y)).ToList(),
                out var triIndices))
        {
            // Fallback: treat clip region as convex hull edges via sequential half-planes (works for convex polygons).
            var clipped = ClipConvexAgainstHalfPlanes(subject, geometryPolygon);
            if (clipped.Count >= 3 && PolygonArea(clipped) > Tol)
                result.AddRange(DecomposeToQuads(clipped));
            return result;
        }

        var seen = new HashSet<string>();
        foreach (var (i0, i1, i2) in triIndices)
        {
            var tri = new List<(double x, double y)>
            {
                geometryPolygon[i0],
                geometryPolygon[i1],
                geometryPolygon[i2],
            };
            var piece = ClipConvexAgainstConvex(subject, tri);
            if (piece.Count < 3 || PolygonArea(piece) <= Tol)
                continue;

            string key = PolygonKey(piece);
            if (!seen.Add(key))
                continue;

            result.AddRange(DecomposeToQuads(piece));
        }

        return result;
    }

    /// <summary>True when all four corners lie inside the polygon (boundary points count as inside).</summary>
    public static bool IsFullyInside(
        IReadOnlyList<(double x, double y)> corners,
        IReadOnlyList<(double x, double y)> polygon)
    {
        foreach (var c in corners)
        {
            if (PolygonElementMatcher.PointInPolygon(c.x, c.y, polygon))
                continue;
            if (IsOnPolygonBoundary(c.x, c.y, polygon, NodeMergeTol))
                continue;
            return false;
        }
        return true;
    }

    /// <summary>Checks corners and midside nodes are inside or on the polygon boundary.</summary>
    public static bool IsTrapeziumInsidePolygon(
        IReadOnlyList<(double x, double y)> corners,
        IReadOnlyList<(double x, double y)> polygon)
    {
        if (corners.Count < 4)
            return false;

        const double boundaryTol = NodeMergeTol * 10;

        foreach (var c in corners.Take(4))
        {
            if (!IsInsideOrOnBoundary(c.x, c.y, polygon, boundaryTol))
                return false;
        }

        var q8 = BuildStructuredQ8NodeCoordsFromCorners(corners);
        foreach (var (x, y) in q8)
        {
            if (!IsInsideOrOnBoundary(x, y, polygon, boundaryTol))
                return false;
        }

        return true;
    }

    /// <summary>True when the point lies inside the polygon or on its boundary.</summary>
    public static bool IsInsideOrOnBoundary(
        double px, double py,
        IReadOnlyList<(double x, double y)> polygon,
        double boundaryMargin = NodeMergeTol)
        => PolygonElementMatcher.PointInPolygon(px, py, polygon)
           || IsOnPolygonBoundary(px, py, polygon, boundaryMargin);

    /// <summary>
    /// Builds one slope-conforming trapezium for a partial grid cell. Never returns true for fully outside cells.
    /// </summary>
    public static bool TryEmitBoundaryTrapezium(
        IReadOnlyList<(double x, double y)> quad,
        IReadOnlyList<(double x, double y)> polygon,
        out (double x, double y)[] corners4)
    {
        corners4 = Array.Empty<(double, double)>();
        if (quad.Count < 4 || polygon.Count < 3)
            return false;

        var grid = quad.Take(4).ToArray();
        if (IsFullyOutside(grid, polygon))
            return false;

        var loop = ClipQuadIntersectionPolygon(grid, polygon);
        if (loop == null || loop.Count < 3 || Math.Abs(PolygonArea(loop)) <= Tol)
            return false;

        // Single trapezium only when the clip intersection is already a triangle or quad.
        if (loop.Count > 4)
            return false;

        List<(double x, double y)> trap = loop.Count switch
        {
            4 => OrderStructuredCorners(loop),
            3 => ExpandTriangleToTrapezium(loop, grid, polygon)
                  ?? new List<(double x, double y)> { loop[0], loop[1], loop[2], loop[2] },
            _ => OrderStructuredCorners(loop),
        };

        trap = trap.Select(p => SnapToBoundary(p, polygon)).ToList();
        if (Math.Abs(PolygonArea(trap)) <= Tol)
            return false;
        if (!AreCornersInsideOrOnBoundary(trap, polygon))
            return false;

        corners4 = trap.ToArray();
        return true;
    }

    public static bool ValidateMeshCornersInsidePolygon(
        P64EmbankmentMeshBuilder.MeshResult mesh,
        IReadOnlyList<(double x, double y)> polygon,
        out string? error)
    {
        error = null;
        var nodeMap = mesh.Nodes.ToDictionary(n => n.id, n => (n.x, n.y));
        const double boundaryTol = NodeMergeTol * 10;

        foreach (var (id, conn, _) in mesh.Elements)
        {
            if (conn.Length < 4)
                continue;
            for (int k = 0; k < 4; k++)
            {
                var (x, y) = nodeMap[conn[k]];
                if (IsInsideOrOnBoundary(x, y, polygon, boundaryTol))
                    continue;
                error = $"Element {id} corner index {k} at ({x:G6}, {y:G6}) lies outside the geometry polygon.";
                return false;
            }
        }

        return true;
    }

    /// <summary>True when all supplied corners lie inside or on the polygon boundary.</summary>
    public static bool AreCornersInsideOrOnBoundary(
        IReadOnlyList<(double x, double y)> corners,
        IReadOnlyList<(double x, double y)> polygon)
    {
        const double boundaryTol = NodeMergeTol * 10;
        foreach (var c in corners)
        {
            if (!IsInsideOrOnBoundary(c.x, c.y, polygon, boundaryTol))
                return false;
        }
        return true;
    }

    internal static List<(double x, double y)>? ClipQuadIntersectionPolygonForTest(
        IReadOnlyList<(double x, double y)> quad,
        IReadOnlyList<(double x, double y)> geometryPolygon)
        => ClipQuadIntersectionPolygon(quad, geometryPolygon);

    internal static List<(double x, double y)> OrderStructuredCornersPublic(
        IReadOnlyList<(double x, double y)> vertices)
        => OrderStructuredCorners(vertices);

    /// <summary>
    /// Builds a single slope-conforming trapezium for a partial grid cell (centroid typically outside).
    /// Returns four corners in structured order BL, BR, TR, TL.
    /// </summary>
    public static bool TryBuildSlopeTrapezium(
        IReadOnlyList<(double x, double y)> quad,
        IReadOnlyList<(double x, double y)> polygon,
        out (double x, double y)[] corners4)
        => TryEmitBoundaryTrapezium(quad, polygon, out corners4);

    /// <summary>
    /// Relaxed trapezium pick used when strict slope trapezium build fails.
    /// </summary>
    public static bool TryPickBoundaryTrapezium(
        IReadOnlyList<(double x, double y)> quad,
        IReadOnlyList<(double x, double y)> polygon,
        out (double x, double y)[] corners4)
        => TryEmitBoundaryTrapezium(quad, polygon, out corners4);

    /// <summary>
    /// True when all four corners and sampled edge/midside points lie inside or on the polygon boundary.
    /// </summary>
    public static bool IsStrictlyInsideQuad(
        IReadOnlyList<(double x, double y)> corners,
        IReadOnlyList<(double x, double y)> polygon)
    {
        if (corners.Count < 4)
            return false;

        if (!IsTrapeziumInsidePolygon(corners, polygon))
            return false;

        for (int i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            for (int s = 1; s < 4; s++)
            {
                double t = s / 4.0;
                double px = a.x + t * (b.x - a.x);
                double py = a.y + t * (b.y - a.y);
                if (!IsInsideOrOnBoundary(px, py, polygon, NodeMergeTol * 10))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates that every element corner and midside node lies inside or on the polygon boundary.
    /// </summary>
    public static bool ValidateMeshInsidePolygon(
        P64EmbankmentMeshBuilder.MeshResult mesh,
        IReadOnlyList<(double x, double y)> polygon,
        out string? error)
    {
        error = null;
        var nodeMap = mesh.Nodes.ToDictionary(n => n.id, n => (n.x, n.y));

        foreach (var (id, conn, _) in mesh.Elements)
        {
            if (conn.Length < 8)
                continue;

            for (int k = 0; k < 8; k++)
            {
                var (x, y) = nodeMap[conn[k]];
                if (IsInsideOrOnBoundary(x, y, polygon, NodeMergeTol * 10))
                    continue;
                error = $"Element {id} node index {k} at ({x:G6}, {y:G6}) lies outside the geometry polygon.";
                return false;
            }
        }

        return true;
    }

    /// <summary>True when no corner is inside/on boundary and no edge intersects the polygon boundary.</summary>
    public static bool IsFullyOutside(
        IReadOnlyList<(double x, double y)> corners,
        IReadOnlyList<(double x, double y)> polygon)
    {
        bool anyInside = corners.Any(c =>
            PolygonElementMatcher.PointInPolygon(c.x, c.y, polygon) ||
            IsOnPolygonBoundary(c.x, c.y, polygon, NodeMergeTol));
        if (anyInside)
            return false;

        for (int i = 0; i < corners.Count; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % corners.Count];
            if (SegmentIntersectsPolygon(a, b, polygon))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Build Q8 node coordinates from four CCW corners using structured-mesh local order:
    /// corners 0–3, then midsides 4–7 (bottom, right, top, left).
    /// </summary>
    public static (double x, double y)[] BuildStructuredQ8NodeCoordsFromCorners(
        IReadOnlyList<(double x, double y)> corners)
    {
        if (corners.Count < 3)
            throw new ArgumentException("At least three corners required for Q8 fill.");

        var c = NormalizeQuadCorners(corners);
        static (double x, double y) Mid((double x, double y) a, (double x, double y) b)
            => (0.5 * (a.x + b.x), 0.5 * (a.y + b.y));

        return new[]
        {
            c[0],
            c[1],
            c[2],
            c[3],
            Mid(c[0], c[1]),
            Mid(c[1], c[2]),
            Mid(c[2], c[3]),
            Mid(c[3], c[0]),
        };
    }

    private static List<(double x, double y)> NormalizeQuadCorners(IReadOnlyList<(double x, double y)> corners)
    {
        if (corners.Count == 3)
            return new List<(double x, double y)> { corners[0], corners[1], corners[2], corners[2] };

        if (corners.Count >= 4)
            return corners.Take(4).ToList();

        throw new ArgumentException("Invalid corner count.");
    }

    private static List<IReadOnlyList<(double x, double y)>> DecomposeToQuads(List<(double x, double y)> polygon)
    {
        var poly = EnsureCounterClockwise(SimplifyCollinear(polygon));
        if (poly.Count < 3)
            return new List<IReadOnlyList<(double x, double y)>>();

        if (poly.Count == 3)
            return new List<IReadOnlyList<(double x, double y)>> { new[] { poly[0], poly[1], poly[2], poly[2] } };

        if (poly.Count == 4)
            return new List<IReadOnlyList<(double x, double y)>> { poly };

        // Fan from first vertex: each triangle becomes a degenerate Q4 (repeat last corner).
        var quads = new List<IReadOnlyList<(double x, double y)>>();
        for (int i = 1; i < poly.Count - 1; i++)
            quads.Add(new[] { poly[0], poly[i], poly[i + 1], poly[i + 1] });
        return quads;
    }

    private static List<(double x, double y)> ClipConvexAgainstHalfPlanes(
        List<(double x, double y)> subject,
        IReadOnlyList<(double x, double y)> clipPolygon)
    {
        var output = subject;
        int n = clipPolygon.Count;
        for (int i = 0; i < n; i++)
        {
            var edgeStart = clipPolygon[i];
            var edgeEnd = clipPolygon[(i + 1) % n];
            output = ClipAgainstEdge(output, edgeStart, edgeEnd);
            if (output.Count == 0)
                break;
        }
        return output;
    }

    private static List<(double x, double y)> ClipConvexAgainstConvex(
        List<(double x, double y)> subject,
        IReadOnlyList<(double x, double y)> convexClip)
        => ClipConvexAgainstHalfPlanes(subject, convexClip);

    private static List<(double x, double y)> ClipAgainstEdge(
        IReadOnlyList<(double x, double y)> subject,
        (double x, double y) edgeStart,
        (double x, double y) edgeEnd)
    {
        var output = new List<(double x, double y)>();
        if (subject.Count == 0)
            return output;

        var s = subject[^1];
        for (int i = 0; i < subject.Count; i++)
        {
            var e = subject[i];
            bool sInside = IsInsideHalfPlane(s, edgeStart, edgeEnd);
            bool eInside = IsInsideHalfPlane(e, edgeStart, edgeEnd);

            if (eInside)
            {
                if (!sInside)
                    output.Add(LineIntersection(s, e, edgeStart, edgeEnd));
                output.Add(e);
            }
            else if (sInside)
            {
                output.Add(LineIntersection(s, e, edgeStart, edgeEnd));
            }

            s = e;
        }

        return output;
    }

    private static bool IsInsideHalfPlane(
        (double x, double y) p,
        (double x, double y) edgeStart,
        (double x, double y) edgeEnd)
    {
        double cross = (edgeEnd.x - edgeStart.x) * (p.y - edgeStart.y)
                         - (edgeEnd.y - edgeStart.y) * (p.x - edgeStart.x);
        return cross >= -Tol;
    }

    private static (double x, double y) LineIntersection(
        (double x, double y) a,
        (double x, double y) b,
        (double x, double y) c,
        (double x, double y) d)
    {
        double denom = (a.x - b.x) * (c.y - d.y) - (a.y - b.y) * (c.x - d.x);
        if (Math.Abs(denom) < Tol)
            return a;

        double t = ((a.x - c.x) * (c.y - d.y) - (a.y - c.y) * (c.x - d.x)) / denom;
        return (a.x + t * (b.x - a.x), a.y + t * (b.y - a.y));
    }

    private static bool IsOnPolygonBoundary(
        double px, double py,
        IReadOnlyList<(double x, double y)> polygon,
        double margin)
    {
        int n = polygon.Count;
        for (int i = 0; i < n; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % n];
            double dx = b.x - a.x;
            double dy = b.y - a.y;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < Tol * Tol) continue;
            double t = ((px - a.x) * dx + (py - a.y) * dy) / lenSq;
            if (t < -Tol || t > 1 + Tol) continue;
            double cx = a.x + t * dx;
            double cy = a.y + t * dy;
            double distSq = (px - cx) * (px - cx) + (py - cy) * (py - cy);
            if (distSq <= margin * margin)
                return true;
        }
        return false;
    }

    private static bool SegmentIntersectsPolygon(
        (double x, double y) a,
        (double x, double y) b,
        IReadOnlyList<(double x, double y)> polygon)
    {
        for (int i = 0; i < polygon.Count; i++)
        {
            var c = polygon[i];
            var d = polygon[(i + 1) % polygon.Count];
            if (SegmentsIntersect(a, b, c, d))
                return true;
        }
        return false;
    }

    private static bool SegmentsIntersect(
        (double x, double y) a,
        (double x, double y) b,
        (double x, double y) c,
        (double x, double y) d)
    {
        double o1 = Orientation(a, b, c);
        double o2 = Orientation(a, b, d);
        double o3 = Orientation(c, d, a);
        double o4 = Orientation(c, d, b);
        if (o1 * o2 < 0 && o3 * o4 < 0)
            return true;
        return false;
    }

    private static double Orientation(
        (double x, double y) a,
        (double x, double y) b,
        (double x, double y) c)
        => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

    private static List<(double x, double y)> EnsureCounterClockwise(List<(double x, double y)> poly)
    {
        if (PolygonArea(poly) < 0)
            poly.Reverse();
        return poly;
    }

    private static List<(double x, double y)> SimplifyCollinear(List<(double x, double y)> poly)
    {
        if (poly.Count <= 3)
            return poly;

        var result = new List<(double x, double y)>();
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            var prev = poly[(i + n - 1) % n];
            var curr = poly[i];
            var next = poly[(i + 1) % n];
            if (Math.Abs(Orientation(prev, curr, next)) > Tol)
                result.Add(curr);
        }
        return result.Count >= 3 ? result : poly;
    }

    private static double PolygonArea(IReadOnlyList<(double x, double y)> poly)
    {
        double area = 0;
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            area += poly[i].x * poly[j].y - poly[j].x * poly[i].y;
        }
        return area * 0.5;
    }

    private static List<(double x, double y)>? ClipQuadIntersectionPolygon(
        IReadOnlyList<(double x, double y)> quad,
        IReadOnlyList<(double x, double y)> geometryPolygon)
    {
        var subject = EnsureCounterClockwise(quad.ToList());
        if (subject.Count < 3)
            return null;

        List<(double x, double y)>? best = null;
        double bestArea = 0;

        if (!PolygonEarTriangulation.TryTriangulate(
                geometryPolygon.Select(p => new Point(p.x, p.y)).ToList(),
                out var triIndices))
        {
            var clipped = ClipConvexAgainstHalfPlanes(subject, geometryPolygon);
            if (clipped.Count >= 3 && Math.Abs(PolygonArea(clipped)) > Tol)
                return SimplifyCollinear(clipped);
            return null;
        }

        foreach (var (i0, i1, i2) in triIndices)
        {
            var tri = new List<(double x, double y)>
            {
                geometryPolygon[i0],
                geometryPolygon[i1],
                geometryPolygon[i2],
            };
            var piece = ClipConvexAgainstConvex(subject, tri);
            if (piece.Count < 3)
                continue;

            double area = Math.Abs(PolygonArea(piece));
            if (area <= Tol)
                continue;

            if (best == null || area > bestArea)
            {
                bestArea = area;
                best = SimplifyCollinear(piece);
            }
        }

        // Merge all triangle clips when overlap spans multiple triangulation pieces.
        var allPieces = new List<List<(double x, double y)>>();
        foreach (var (i0, i1, i2) in triIndices)
        {
            var tri = new List<(double x, double y)>
            {
                geometryPolygon[i0],
                geometryPolygon[i1],
                geometryPolygon[i2],
            };
            var piece = ClipConvexAgainstConvex(subject, tri);
            if (piece.Count >= 3 && Math.Abs(PolygonArea(piece)) > Tol)
                allPieces.Add(SimplifyCollinear(piece));
        }

        if (allPieces.Count > 1)
        {
            var merged = MergeConvexClipPieces(allPieces);
            if (merged.Count >= 3 && Math.Abs(PolygonArea(merged)) > Tol)
                return merged;
        }

        return best;
    }

    private static List<(double x, double y)> MergeConvexClipPieces(
        IReadOnlyList<List<(double x, double y)>> pieces)
    {
        var pts = DeduplicatePoints(pieces.SelectMany(p => p).ToList());
        if (pts.Count <= 4)
            return EnsureCounterClockwise(pts);

        double cx = pts.Average(p => p.x);
        double cy = pts.Average(p => p.y);
        return EnsureCounterClockwise(pts
            .OrderBy(p => Math.Atan2(p.y - cy, p.x - cx))
            .ToList());
    }

    private static List<(double x, double y)>? ReduceToTrapeziumCorners(
        List<(double x, double y)> loop,
        IReadOnlyList<(double x, double y)> gridCorners,
        IReadOnlyList<(double x, double y)> polygon)
    {
        var poly = EnsureCounterClockwise(SimplifyCollinear(loop));
        if (poly.Count == 4)
            return OrderStructuredCorners(poly);

        if (poly.Count == 3)
        {
            var expanded = ExpandTriangleToTrapezium(poly, gridCorners, polygon);
            if (expanded != null)
                return expanded;
        }

        var fromGrid = PickTrapeziumFromGrid(gridCorners, polygon);
        if (fromGrid != null && Math.Abs(PolygonArea(fromGrid)) > Tol)
            return fromGrid;

        if (poly.Count >= 4)
            return OrderStructuredCorners(SelectFourExtremeVertices(poly, gridCorners, polygon));

        return null;
    }

    private static List<(double x, double y)> OrderStructuredCorners(
        IReadOnlyList<(double x, double y)> vertices)
    {
        var sorted = vertices.OrderBy(p => p.y).ThenBy(p => p.x).ToArray();
        var bottom = sorted.Take(2).OrderBy(p => p.x).ToArray();
        var top = sorted.Skip(Math.Max(0, sorted.Length - 2)).OrderBy(p => p.x).ToArray();
        if (top.Length < 2)
            top = sorted.Skip(Math.Max(0, sorted.Length - 2)).OrderBy(p => p.x).ToArray();
        return new List<(double x, double y)> { bottom[0], bottom[1], top[1], top[0] };
    }

    private static List<(double x, double y)>? ExpandTriangleToTrapezium(
        IReadOnlyList<(double x, double y)> triangle,
        IReadOnlyList<(double x, double y)> gridCorners,
        IReadOnlyList<(double x, double y)> polygon)
    {
        var fromGrid = PickTrapeziumFromGrid(gridCorners, polygon);
        if (fromGrid == null)
            return null;

        int unique = CountUniquePoints(fromGrid);
        if (unique >= 4)
            return fromGrid;

        var triSet = triangle.ToList();
        var extra = CollectEdgePolygonIntersections(gridCorners, polygon)
            .Where(p => !triSet.Any(t =>
                Math.Abs(t.x - p.x) <= NodeMergeTol && Math.Abs(t.y - p.y) <= NodeMergeTol))
            .OrderByDescending(p => p.y)
            .ThenBy(p => p.x)
            .ToList();

        if (extra.Count == 0)
            return fromGrid;

        var merged = new List<(double x, double y)>(fromGrid);
        foreach (var p in extra)
        {
            if (CountUniquePoints(merged) >= 4)
                break;
            if (!merged.Any(q => Math.Abs(q.x - p.x) <= NodeMergeTol && Math.Abs(q.y - p.y) <= NodeMergeTol))
                merged.Add(p);
        }

        return OrderStructuredCorners(merged.Take(4).ToList());
    }

    private static List<(double x, double y)> SelectFourExtremeVertices(
        IReadOnlyList<(double x, double y)> poly,
        IReadOnlyList<(double x, double y)> gridCorners,
        IReadOnlyList<(double x, double y)> geometryPolygon)
    {
        var points = new List<(double x, double y)>();
        foreach (var g in gridCorners)
        {
            if (IsInsideOrOnBoundary(g.x, g.y, geometryPolygon))
                points.Add(g);
        }

        foreach (var p in poly)
        {
            if (!points.Any(q => Math.Abs(q.x - p.x) <= NodeMergeTol && Math.Abs(q.y - p.y) <= NodeMergeTol))
                points.Add(p);
        }

        if (points.Count >= 4)
            return OrderStructuredCorners(points);
        return OrderStructuredCorners(poly);
    }

    private static List<(double x, double y)>? PickTrapeziumFromGrid(
        IReadOnlyList<(double x, double y)> grid,
        IReadOnlyList<(double x, double y)> polygon)
    {
        if (grid.Count < 4)
            return null;

        var bl = ResolveOnEdge(grid[0], grid[3], grid[0], grid[1], polygon);
        var br = ResolveOnEdge(grid[1], grid[0], grid[1], grid[2], polygon);

        var topHits = DeduplicatePoints(GetSegmentPolygonIntersections(grid[3], grid[2], polygon))
            .OrderBy(p => p.x)
            .ToList();
        var leftHits = DeduplicatePoints(GetSegmentPolygonIntersections(grid[3], grid[0], polygon));
        var rightHits = DeduplicatePoints(GetSegmentPolygonIntersections(grid[1], grid[2], polygon));

        (double x, double y) tl;
        (double x, double y) tr;

        if (topHits.Count >= 2)
        {
            tl = topHits[0];
            tr = topHits[^1];
        }
        else if (topHits.Count == 1)
        {
            var hit = topHits[0];
            tl = leftHits.Count > 0
                ? leftHits.OrderByDescending(p => p.y).First()
                : hit;
            tr = rightHits.Count > 0
                ? rightHits.OrderByDescending(p => p.y).First()
                : hit;
            if (Math.Abs(tl.x - tr.x) <= NodeMergeTol && Math.Abs(tl.y - tr.y) <= NodeMergeTol)
            {
                if (leftHits.Count > 0) tl = leftHits.OrderBy(p => p.x).First();
                if (rightHits.Count > 0) tr = rightHits.OrderByDescending(p => p.x).First();
            }
        }
        else
        {
            tl = ResolveOnEdge(grid[3], grid[2], grid[3], grid[0], polygon);
            tr = ResolveOnEdge(grid[2], grid[1], grid[1], grid[2], polygon);
        }

        return new List<(double x, double y)> { bl, br, tr, tl };
    }

    private static (double x, double y) ResolveOnEdge(
        (double x, double y) corner,
        (double x, double y) edgePrev,
        (double x, double y) edgeStart,
        (double x, double y) edgeEnd,
        IReadOnlyList<(double x, double y)> polygon)
    {
        if (IsInsideOrOnBoundary(corner.x, corner.y, polygon))
            return corner;

        var hits = DeduplicatePoints(
            GetSegmentPolygonIntersections(edgePrev, edgeStart, polygon)
                .Concat(GetSegmentPolygonIntersections(edgeStart, edgeEnd, polygon))
                .ToList());

        if (hits.Count == 0)
            return SnapToBoundary(corner, polygon);

        static double DistSq((double x, double y) a, (double x, double y) b)
        {
            double dx = a.x - b.x;
            double dy = a.y - b.y;
            return dx * dx + dy * dy;
        }

        return hits
            .Where(h => IsInsideOrOnBoundary(h.x, h.y, polygon))
            .OrderBy(h => DistSq(h, corner))
            .First();
    }

    private static List<(double x, double y)>? ExpandDuplicateTrapezium(
        IReadOnlyList<(double x, double y)> trap,
        List<(double x, double y)>? loop,
        IReadOnlyList<(double x, double y)> grid,
        IReadOnlyList<(double x, double y)> polygon)
    {
        if (loop != null && loop.Count >= 3)
        {
            var reduced = ReduceToTrapeziumCorners(loop, grid, polygon);
            if (reduced != null && CountUniquePoints(reduced) >= 4)
                return reduced;
        }

        var allHits = CollectEdgePolygonIntersections(grid, polygon);
        var merged = DeduplicatePoints(trap.Concat(allHits).ToList());
        if (merged.Count >= 4)
            return OrderStructuredCorners(merged);

        return null;
    }

    private static List<(double x, double y)> CollectEdgePolygonIntersections(
        IReadOnlyList<(double x, double y)> grid,
        IReadOnlyList<(double x, double y)> polygon)
    {
        var hits = new List<(double x, double y)>();
        for (int i = 0; i < 4; i++)
        {
            var a = grid[i];
            var b = grid[(i + 1) % 4];
            hits.AddRange(GetSegmentPolygonIntersections(a, b, polygon));
        }
        return DeduplicatePoints(hits);
    }

    private static List<(double x, double y)> GetSegmentPolygonIntersections(
        (double x, double y) a,
        (double x, double y) b,
        IReadOnlyList<(double x, double y)> polygon)
    {
        var hits = new List<(double x, double y)>();
        for (int i = 0; i < polygon.Count; i++)
        {
            var c = polygon[i];
            var d = polygon[(i + 1) % polygon.Count];
            if (!TrySegmentIntersection(a, b, c, d, out var p))
                continue;
            if (IsPointOnSegment(p, a, b) && IsPointOnSegment(p, c, d))
                hits.Add(p);
        }
        return hits;
    }

    private static bool TrySegmentIntersection(
        (double x, double y) a,
        (double x, double y) b,
        (double x, double y) c,
        (double x, double y) d,
        out (double x, double y) point)
    {
        point = default;
        double denom = (a.x - b.x) * (c.y - d.y) - (a.y - b.y) * (c.x - d.x);
        if (Math.Abs(denom) < Tol)
            return false;

        point = LineIntersection(a, b, c, d);
        return true;
    }

    private static bool IsPointOnSegment(
        (double x, double y) p,
        (double x, double y) a,
        (double x, double y) b)
    {
        double minX = Math.Min(a.x, b.x) - NodeMergeTol;
        double maxX = Math.Max(a.x, b.x) + NodeMergeTol;
        double minY = Math.Min(a.y, b.y) - NodeMergeTol;
        double maxY = Math.Max(a.y, b.y) + NodeMergeTol;
        if (p.x < minX || p.x > maxX || p.y < minY || p.y > maxY)
            return false;

        double dx = b.x - a.x;
        double dy = b.y - a.y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < Tol * Tol)
            return Math.Abs(p.x - a.x) <= NodeMergeTol && Math.Abs(p.y - a.y) <= NodeMergeTol;

        double cross = Math.Abs(dx * (p.y - a.y) - dy * (p.x - a.x));
        return cross <= NodeMergeTol * Math.Sqrt(lenSq);
    }

    private static (double x, double y) SnapToBoundary(
        (double x, double y) p,
        IReadOnlyList<(double x, double y)> polygon)
    {
        int n = polygon.Count;
        (double x, double y) best = p;
        double bestDistSq = NodeMergeTol * NodeMergeTol;

        for (int i = 0; i < n; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % n];
            double dx = b.x - a.x;
            double dy = b.y - a.y;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < Tol * Tol)
                continue;

            double t = ((p.x - a.x) * dx + (p.y - a.y) * dy) / lenSq;
            t = Math.Clamp(t, 0, 1);
            var q = (x: a.x + t * dx, y: a.y + t * dy);
            double distSq = (p.x - q.x) * (p.x - q.x) + (p.y - q.y) * (p.y - q.y);
            if (distSq <= bestDistSq)
            {
                bestDistSq = distSq;
                best = q;
            }
        }

        return best;
    }

    private static List<(double x, double y)> DeduplicatePoints(IReadOnlyList<(double x, double y)> points)
    {
        var result = new List<(double x, double y)>();
        foreach (var p in points)
        {
            if (!result.Any(q => Math.Abs(q.x - p.x) <= NodeMergeTol && Math.Abs(q.y - p.y) <= NodeMergeTol))
                result.Add(p);
        }
        return result;
    }

    private static int CountUniquePoints(IReadOnlyList<(double x, double y)> points)
        => DeduplicatePoints(points).Count;

    private static string PolygonKey(IReadOnlyList<(double x, double y)> poly)
    {
        var pts = poly
            .Select(p => ((long)Math.Round(p.x / NodeMergeTol), (long)Math.Round(p.y / NodeMergeTol)))
            .OrderBy(t => t.Item1)
            .ThenBy(t => t.Item2);
        return string.Join("|", pts.Select(t => $"{t.Item1},{t.Item2}"));
    }
}
