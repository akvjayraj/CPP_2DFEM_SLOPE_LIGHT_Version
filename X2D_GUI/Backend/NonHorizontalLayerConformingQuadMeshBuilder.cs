using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Quad-only mesh path for non-horizontal material layers. It clips a shared background
/// lattice to each layer-zone polygon, so adjacent zones share the exact inclined interface
/// nodes while still emitting Q4-compatible connectivity only.
/// </summary>
public static class NonHorizontalLayerConformingQuadMeshBuilder
{
    private const double AreaTol = 1e-10;

    public sealed class Request
    {
        public IReadOnlyList<(int SurfaceNumber, IReadOnlyList<Point> Polygon)> Regions { get; init; } =
            Array.Empty<(int, IReadOnlyList<Point>)>();
        public IReadOnlyList<IReadOnlyList<Point>> HolePolygons { get; init; } = Array.Empty<IReadOnlyList<Point>>();
        public required NonHorizontalLayerMeshPlan LayerPlan { get; init; }
        public double GlobalSize { get; init; }
        public int MaxNodes { get; init; } = 40_000;
    }

    public static HybridMeshResult Generate(Request request)
    {
        var regions = request.Regions
            .Where(r => r.Polygon is { Count: >= 3 })
            .ToList();
        var zones = request.LayerPlan.Zones
            .Where(z => z.Polygon.Count >= 3)
            .ToList();
        if (regions.Count == 0 || zones.Count == 0)
            return HybridMeshResult.Empty;

        var holes = request.HolePolygons
            .Where(h => h is { Count: >= 3 })
            .Select(h => h.Select(p => (p.X, p.Y)).ToList())
            .ToList();

        double target = Math.Max(request.GlobalSize, 1e-6);
        var xCoords = BuildAxisCoords(regions, zones, target, axisX: true);
        var yCoords = BuildAxisCoords(regions, zones, target, axisX: false);
        if (xCoords.Count < 2 || yCoords.Count < 2)
            return HybridMeshResult.Empty;

        var nodePool = new NodeRegistry(Math.Max(target * 1e-7, 1e-7));
        var regionQuads = regions.ToDictionary(
            r => r.SurfaceNumber,
            _ => new List<(int a, int b, int c, int d)>());

        foreach (var region in regions)
        {
            var regionPoly = ToTuples(region.Polygon);
            var acceptedKeys = new HashSet<string>();
            for (int ix = 0; ix + 1 < xCoords.Count; ix++)
            {
                for (int iy = 0; iy + 1 < yCoords.Count; iy++)
                {
                    var cell = new[]
                    {
                        (x: xCoords[ix], y: yCoords[iy]),
                        (x: xCoords[ix + 1], y: yCoords[iy]),
                        (x: xCoords[ix + 1], y: yCoords[iy + 1]),
                        (x: xCoords[ix], y: yCoords[iy + 1]),
                    };

                    var regionPieces = StructuredQuadPolygonClipper.ClipQuadToPolygon(cell, regionPoly);
                    if (regionPieces.Count == 0)
                        continue;

                    foreach (var zone in zones)
                    {
                        foreach (var regionPiece in regionPieces)
                        {
                            var zonePieces = StructuredQuadPolygonClipper.ClipQuadToPolygon(regionPiece, zone.Polygon);
                            foreach (var piece in zonePieces)
                            {
                                var quad = NormalizeQuad(piece);
                                if (quad.Count != 4 || Math.Abs(PolygonArea(quad)) <= AreaTol)
                                    continue;

                                var centroid = Centroid(quad);
                                if (IsInsideAnyHole(centroid, holes))
                                    continue;

                                var key = QuadKey(quad);
                                if (!acceptedKeys.Add(key))
                                    continue;

                                int a = nodePool.Get(quad[0]);
                                int b = nodePool.Get(quad[1]);
                                int c = nodePool.Get(quad[2]);
                                int d = nodePool.Get(quad[3]);
                                if (HasDuplicateNode(a, b, c, d))
                                    continue;
                                regionQuads[region.SurfaceNumber].Add((a, b, c, d));
                                if (nodePool.Nodes.Count > request.MaxNodes)
                                    return HybridMeshResult.Empty;
                            }
                        }
                    }
                }
            }
        }

        var meshes = new List<HybridRegionMesh>();
        foreach (var region in regions)
        {
            var quads = regionQuads[region.SurfaceNumber];
            if (quads.Count == 0)
                continue;
            meshes.Add(new HybridRegionMesh
            {
                SurfaceNumber = region.SurfaceNumber,
                Strategy = RegionMeshStrategy.StructuredTransfinite,
                Quads = quads,
                Triangles = Array.Empty<(int, int, int)>(),
                Reason = "quad-only lattice clipped to non-horizontal material layer zones",
            });
        }

        if (meshes.Count == 0)
            return HybridMeshResult.Empty;

        return new HybridMeshResult
        {
            Nodes = nodePool.Nodes,
            Regions = meshes,
            Notes = "quad-only non-horizontal layer conforming mesh",
        };
    }

    private static List<double> BuildAxisCoords(
        IReadOnlyList<(int SurfaceNumber, IReadOnlyList<Point> Polygon)> regions,
        IReadOnlyList<NonHorizontalLayerMeshPlan.Zone> zones,
        double step,
        bool axisX)
    {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        var extras = new List<double>();

        void AddPoint(Point p)
        {
            double v = axisX ? p.X : p.Y;
            min = Math.Min(min, v);
            max = Math.Max(max, v);
            extras.Add(v);
        }

        foreach (var (_, poly) in regions)
            foreach (var p in poly)
                AddPoint(p);

        foreach (var zone in zones)
        {
            foreach (var (x, y) in zone.Polygon)
                extras.Add(axisX ? x : y);
        }

        if (!double.IsFinite(min) || !double.IsFinite(max) || max <= min)
            return new List<double>();

        var coords = new List<double>();
        int n = Math.Clamp((int)Math.Ceiling((max - min) / step), 1, 600);
        double dx = (max - min) / n;
        for (int i = 0; i <= n; i++)
            coords.Add(min + i * dx);
        coords.AddRange(extras.Where(double.IsFinite));
        return MergeSorted(coords, Math.Max(step * 1e-7, 1e-7));
    }

    private static List<double> MergeSorted(IEnumerable<double> values, double tol)
    {
        var ordered = values.OrderBy(v => v).ToList();
        var merged = new List<double>();
        foreach (double v in ordered)
        {
            if (merged.Count == 0 || Math.Abs(v - merged[^1]) > tol)
                merged.Add(v);
        }
        return merged;
    }

    private static List<(double x, double y)> NormalizeQuad(IReadOnlyList<(double x, double y)> piece)
    {
        var pts = piece
            .Where(p => double.IsFinite(p.x) && double.IsFinite(p.y))
            .ToList();
        pts = RemoveDuplicateConsecutive(pts);
        if (pts.Count < 3)
            return new List<(double, double)>();
        if (PolygonArea(pts) < 0)
            pts.Reverse();
        if (pts.Count == 3)
            pts = ExpandTriangleToQuad(pts);
        if (pts.Count > 4)
            pts = pts.Take(4).ToList();
        return pts;
    }

    /// <summary>
    /// Converts a triangular clipped sliver into a four-node quad by splitting its longest edge.
    /// The added point lies on the same boundary, preserving exact geometry while avoiding duplicate
    /// node ids in the downstream Q4/Q8 connectivity.
    /// </summary>
    private static List<(double x, double y)> ExpandTriangleToQuad(List<(double x, double y)> tri)
    {
        int splitStart = 0;
        double best = -1;
        for (int i = 0; i < 3; i++)
        {
            double d = DistSq(tri[i], tri[(i + 1) % 3]);
            if (d > best)
            {
                best = d;
                splitStart = i;
            }
        }

        int splitEnd = (splitStart + 1) % 3;
        var mid = (
            x: 0.5 * (tri[splitStart].x + tri[splitEnd].x),
            y: 0.5 * (tri[splitStart].y + tri[splitEnd].y));

        var quad = new List<(double x, double y)>(4);
        for (int i = 0; i < 3; i++)
        {
            quad.Add(tri[i]);
            if (i == splitStart)
                quad.Add(mid);
        }
        return quad;
    }

    private static List<(double x, double y)> RemoveDuplicateConsecutive(List<(double x, double y)> pts)
    {
        var result = new List<(double x, double y)>();
        foreach (var p in pts)
        {
            if (result.Count == 0 || DistSq(result[^1], p) > 1e-18)
                result.Add(p);
        }
        if (result.Count > 1 && DistSq(result[0], result[^1]) <= 1e-18)
            result.RemoveAt(result.Count - 1);
        return result;
    }

    private static List<(double x, double y)> ToTuples(IReadOnlyList<Point> points) =>
        points.Select(p => (p.X, p.Y)).ToList();

    private static bool IsInsideAnyHole((double x, double y) p, IReadOnlyList<IReadOnlyList<(double x, double y)>> holes)
    {
        foreach (var hole in holes)
            if (PolygonElementMatcher.PointInPolygon(p.x, p.y, hole))
                return true;
        return false;
    }

    private static (double x, double y) Centroid(IReadOnlyList<(double x, double y)> pts)
    {
        double sx = 0, sy = 0;
        foreach (var p in pts)
        {
            sx += p.x;
            sy += p.y;
        }
        return (sx / pts.Count, sy / pts.Count);
    }

    private static double PolygonArea(IReadOnlyList<(double x, double y)> pts)
    {
        double a = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p0 = pts[i];
            var p1 = pts[(i + 1) % pts.Count];
            a += p0.x * p1.y - p1.x * p0.y;
        }
        return 0.5 * a;
    }

    private static bool HasDuplicateNode(int a, int b, int c, int d) =>
        a == b || a == c || a == d || b == c || b == d || c == d;

    private static double DistSq((double x, double y) a, (double x, double y) b)
    {
        double dx = a.x - b.x;
        double dy = a.y - b.y;
        return dx * dx + dy * dy;
    }

    private static string QuadKey(IReadOnlyList<(double x, double y)> quad)
    {
        var c = Centroid(quad);
        return string.Join("|", quad
            .OrderBy(p => Math.Atan2(p.y - c.y, p.x - c.x))
            .Select(p => $"{Math.Round(p.x, 8):G17},{Math.Round(p.y, 8):G17}"));
    }

    private sealed class NodeRegistry
    {
        private readonly double _tol;
        private readonly Dictionary<(long x, long y), int> _map = new();
        private readonly List<Point> _nodes = new();

        public NodeRegistry(double tol) => _tol = Math.Max(tol, 1e-9);

        public IReadOnlyList<Point> Nodes => _nodes;

        public int Get((double x, double y) p)
        {
            var key = Key(p.x, p.y);
            for (long ix = key.x - 1; ix <= key.x + 1; ix++)
            {
                for (long iy = key.y - 1; iy <= key.y + 1; iy++)
                {
                    if (!_map.TryGetValue((ix, iy), out int id))
                        continue;
                    var existing = _nodes[id];
                    double dx = existing.X - p.x;
                    double dy = existing.Y - p.y;
                    if (dx * dx + dy * dy <= _tol * _tol)
                        return id;
                }
            }

            int next = _nodes.Count;
            _nodes.Add(new Point(p.x, p.y));
            _map[key] = next;
            return next;
        }

        private (long x, long y) Key(double x, double y) =>
            ((long)Math.Round(x / _tol), (long)Math.Round(y / _tol));
    }
}
