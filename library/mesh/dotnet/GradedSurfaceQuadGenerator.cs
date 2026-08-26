using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Native Q4 surface mesh: structured quadrilateral lattice clipped to polygonal regions.
/// All regions share one axis-aligned node grid so interfaces are conforming.
/// </summary>
public sealed class SurfaceQuadMeshResult
{
    public IReadOnlyList<Point> Nodes { get; init; } = Array.Empty<Point>();
    /// <summary>Each element is (n1, n2, n3, n4) — 0-based corner indices, CCW.</summary>
    public IReadOnlyList<(int a, int b, int c, int d)> Quads { get; init; } = Array.Empty<(int, int, int, int)>();
    /// <summary>Each triangle is (n1, n2, n3) — 0-based corner indices, CCW. Non-empty only at
    /// material-layer wedge tips where the column sweep cannot place a proper quad.</summary>
    public IReadOnlyList<(int a, int b, int c)> Triangles { get; init; } = Array.Empty<(int, int, int)>();
    public int NodeCount => Nodes.Count;
    public int ElementCount => Quads.Count + Triangles.Count;
    public double MinAngleDeg { get; init; }
    public double MaxAspectRatio { get; init; }
    public int MaxRefineDepth { get; init; }
}

public static class GradedSurfaceQuadGenerator
{
    private enum QuadClass { Inside, Outside, Mixed }

    private sealed class DomainContext
    {
        public required IReadOnlyList<Point> Polygon { get; init; }
        public required List<IReadOnlyList<Point>> ChildPolys { get; init; }
        public required List<IReadOnlyList<Point>> HolePolys { get; init; }
        public required IReadOnlyList<(Point a, Point b)> Segments { get; init; }
        /// <summary>Cached outer-polygon edge segments (computed once; reused by the hot per-quad
        /// boundary tests instead of rebuilding the list on every call).</summary>
        public required IReadOnlyList<(Point a, Point b)> OuterSegments { get; init; }
        public required double NearSize { get; init; }
        public required double GlobalSize { get; init; }
        public required double Growth { get; init; }
    }

    /// <summary>
    /// Build a conforming all-Q4 mesh for multiple regions on a shared structured grid with
    /// adaptive boundary refinement so no quad corner lies outside the polygon.
    /// </summary>
    public static IReadOnlyList<(int SurfaceNumber, SurfaceQuadMeshResult Mesh)> GenerateConforming(
        IReadOnlyList<(int SurfaceNumber, IReadOnlyList<Point> Polygon, double NearSize, double GlobalSize, int Layers, double Growth)> regions,
        IReadOnlyList<(int SurfaceNumber, IReadOnlyList<Point> Polygon, double NearSize)>? holeOnlyRegions = null,
        IReadOnlyList<(Point A, Point B)>? materialLayerSegments = null,
        IReadOnlyList<Point>? pinnedBoundaryNodes = null,
        int maxNodes = 40_000)
    {
        if (regions == null || regions.Count == 0)
            return Array.Empty<(int, SurfaceQuadMeshResult)>();

        var safe = regions
            .Where(r => r.Polygon != null && r.Polygon.Count >= 3)
            .Select(r => (
                r.SurfaceNumber,
                r.Polygon,
                NearSize: Math.Max(r.NearSize, 1e-6),
                GlobalSize: Math.Max(r.GlobalSize, Math.Max(r.NearSize, 1e-6)),
                Layers: Math.Clamp(r.Layers, 0, 12),
                Growth: Math.Clamp(r.Growth, 1.05, 3.0)))
            .ToList();
        if (safe.Count == 0)
            return Array.Empty<(int, SurfaceQuadMeshResult)>();

        var holeSafe = (holeOnlyRegions ?? Array.Empty<(int, IReadOnlyList<Point>, double)>())
            .Where(h => h.Item2 != null && h.Item2.Count >= 3)
            .Select(h => (h.Item1, h.Item2, NearSize: Math.Max(h.Item3, 1e-6)))
            .ToList();

        var polyList = safe.Select(r => (r.SurfaceNumber, r.Polygon)).ToList();
        foreach (var h in holeSafe)
            polyList.Add((h.Item1, h.Item2));
        var parentMap = SurfaceMeshHierarchy.BuildParentMap(polyList);

        List<IReadOnlyList<Point>> ChildPolysOf(int parentSn)
        {
            var list = new List<IReadOnlyList<Point>>();
            foreach (var r in safe)
                if (parentMap.TryGetValue(r.SurfaceNumber, out var p) && p == parentSn)
                    list.Add(r.Polygon);
            foreach (var h in holeSafe)
                if (parentMap.TryGetValue(h.Item1, out var ph) && ph == parentSn)
                    list.Add(h.Item2);
            return list;
        }

        double globalCell = safe.Max(r => r.GlobalSize);
        if (holeSafe.Count > 0)
            globalCell = Math.Max(globalCell, holeSafe.Max(h => h.NearSize));
        globalCell = Math.Max(globalCell, 1e-6);

        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        foreach (var (_, poly, _, _, _, _) in safe)
        {
            foreach (var p in poly)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
        }
        foreach (var (_, poly, _) in holeSafe)
        {
            foreach (var p in poly)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
        }

        double mergeEps = safe.Min(r => r.NearSize) * 0.1;
        if (holeSafe.Count > 0)
            mergeEps = Math.Min(mergeEps, holeSafe.Min(h => h.NearSize) * 0.1);
        mergeEps = Math.Max(mergeEps, 1e-8);

        var pinned = BuildPinnedNodes(safe, holeSafe, materialLayerSegments, pinnedBoundaryNodes, mergeEps);

        var xCoords = BuildAxisCoords(minX, maxX, globalCell, CollectAxisValues(safe, holeSafe, axisX: true), mergeEps);
        var yExtras = CollectAxisValues(safe, holeSafe, axisX: false);
        if (materialLayerSegments is { Count: > 0 })
        {
            foreach (var (a, b) in materialLayerSegments)
            {
                yExtras.Add(a.Y);
                yExtras.Add(b.Y);
            }
        }
        var yCoords = BuildAxisCoords(minY, maxY, globalCell, yExtras, mergeEps);

        var regionContexts = safe.Select(r =>
        {
            var childPolys = ChildPolysOf(r.SurfaceNumber);
            var holePolys = holeSafe.Select(h => h.Item2).ToList();
            var segments = CollectSegments(new[] { r.Polygon }.Concat(childPolys).Concat(holePolys));
            return (r.SurfaceNumber, new DomainContext
            {
                Polygon = r.Polygon,
                ChildPolys = childPolys,
                HolePolys = holePolys,
                Segments = segments,
                OuterSegments = CollectSegments(new[] { r.Polygon }),
                NearSize = r.NearSize,
                GlobalSize = r.GlobalSize,
                Growth = r.Growth,
            });
        }).ToList();

        List<(int a, int b, int c, int d)> sharedQuads;
        List<(int a, int b, int c)> sharedTris;
        List<Point> sharedNodes;
        int maxDepth;

        if (TryBuildColumnSweepMesh(regionContexts, xCoords, materialLayerSegments, pinned, mergeEps, maxNodes,
                out sharedQuads, out sharedTris, out sharedNodes))
        {
            maxDepth = 0;
        }
        else if (TryBuildMappedSweepMeshes(regionContexts, xCoords, yCoords, materialLayerSegments, pinned, mergeEps, maxNodes,
                out sharedQuads, out sharedNodes))
        {
            sharedTris = new List<(int, int, int)>();
            maxDepth = 0;
        }
        else
        {
            var amr = BuildAdaptiveSharedQuads(
                regionContexts.Select(rc => rc.Item2).ToList(),
                xCoords, yCoords, pinned, mergeEps, maxNodes);

            var nodePool = new NodePool(mergeEps, pinned);
            foreach (var p in amr.nodes)
                nodePool.Get(p);
            sharedQuads = new List<(int, int, int, int)>(amr.quads);
            sharedTris = new List<(int, int, int)>();
            foreach (var (_, ctx) in regionContexts)
                AddBoundaryStripQuads(ctx, pinned, nodePool, sharedQuads);
            sharedNodes = nodePool.Nodes;
            maxDepth = amr.maxDepth;
        }

        // Drop nodes not referenced by any element (e.g. fine boundary seed points left unused now
        // that they no longer pin a grid column/row). This keeps the node list compact and avoids
        // orphan points sitting on quad edges as hanging nodes, while reindexing all elements.
        PruneUnusedNodes(ref sharedNodes, sharedQuads, sharedTris);

        var results = new List<(int, SurfaceQuadMeshResult)>(safe.Count);
        foreach (var (sn, ctx) in regionContexts)
        {
            // Quads were already accepted at insertion time; partitioning them to their owning
            // region only needs a single centroid-in-region test (far cheaper than re-running the
            // full 4-corner acceptance, which rebuilt segment lists and ran point-in-polygon per quad).
            var regionQuads = sharedQuads
                .Where(q =>
                {
                    double cx = (sharedNodes[q.a].X + sharedNodes[q.b].X + sharedNodes[q.c].X + sharedNodes[q.d].X) * 0.25;
                    double cy = (sharedNodes[q.a].Y + sharedNodes[q.b].Y + sharedNodes[q.c].Y + sharedNodes[q.d].Y) * 0.25;
                    return PointInDomainInteriorOrOnBoundary(new Point(cx, cy), ctx, ctx.NearSize * 0.25);
                })
                .ToList();

            // Wedge-tip triangles partition to their owning region by the same centroid test.
            var regionTris = sharedTris
                .Where(t =>
                {
                    double cx = (sharedNodes[t.a].X + sharedNodes[t.b].X + sharedNodes[t.c].X) / 3.0;
                    double cy = (sharedNodes[t.a].Y + sharedNodes[t.b].Y + sharedNodes[t.c].Y) / 3.0;
                    return PointInDomainInteriorOrOnBoundary(new Point(cx, cy), ctx, ctx.NearSize * 0.25);
                })
                .ToList();

            ComputeQuadQualityStats(sharedNodes, regionQuads, out double minAngle, out double maxAr);
            results.Add((sn, new SurfaceQuadMeshResult
            {
                Nodes = sharedNodes,
                Quads = regionQuads,
                Triangles = regionTris,
                MinAngleDeg = minAngle,
                MaxAspectRatio = maxAr,
                MaxRefineDepth = maxDepth,
            }));
        }

        return results;
    }

    /// <summary>True if any quad corner lies strictly outside <paramref name="polygon"/> (on-boundary is OK).</summary>
    public static bool AnyCornerOutsidePolygon(
        IReadOnlyList<Point> nodes,
        IReadOnlyList<(int a, int b, int c, int d)> quads,
        IReadOnlyList<Point> polygon,
        double nearSize = 0.1)
    {
        double tol = Math.Max(nearSize * 0.12, 1e-5);
        var segs = CollectSegments(new[] { polygon });
        foreach (var (a, b, c, d) in quads)
        {
            if (!CornerInOrOnPolygon(nodes[a], polygon, segs, tol)) return true;
            if (!CornerInOrOnPolygon(nodes[b], polygon, segs, tol)) return true;
            if (!CornerInOrOnPolygon(nodes[c], polygon, segs, tol)) return true;
            if (!CornerInOrOnPolygon(nodes[d], polygon, segs, tol)) return true;
        }
        return false;
    }

    /// <summary>
    /// FE-validity check: returns true if any node lies strictly on the interior of a quad edge
    /// without being one of that edge's two endpoints (a hanging node, which breaks element
    /// compatibility). Uses a spatial hash on <paramref name="tol"/>-sized cells for speed.
    /// </summary>
    public static bool HasHangingNode(
        IReadOnlyList<Point> nodes,
        IReadOnlyList<(int a, int b, int c, int d)> quads,
        double tol)
    {
        if (nodes == null || nodes.Count == 0 || quads == null || quads.Count == 0)
            return false;

        double cell = Math.Max(tol, 1e-9);
        var grid = new Dictionary<(long, long), List<int>>();
        (long, long) Cell(Point p) => ((long)Math.Floor(p.X / cell), (long)Math.Floor(p.Y / cell));
        for (int i = 0; i < nodes.Count; i++)
        {
            var key = Cell(nodes[i]);
            if (!grid.TryGetValue(key, out var list)) { list = new List<int>(); grid[key] = list; }
            list.Add(i);
        }

        double endTol = tol * 2.5; // ignore points effectively coincident with an endpoint

        bool EdgeHasInteriorNode(int ia, int ib)
        {
            Point a = nodes[ia], b = nodes[ib];
            double abx = b.X - a.X, aby = b.Y - a.Y;
            double len2 = abx * abx + aby * aby;
            if (len2 < 1e-18) return false;

            long minCx = (long)Math.Floor(Math.Min(a.X, b.X) / cell) - 1;
            long maxCx = (long)Math.Floor(Math.Max(a.X, b.X) / cell) + 1;
            long minCy = (long)Math.Floor(Math.Min(a.Y, b.Y) / cell) - 1;
            long maxCy = (long)Math.Floor(Math.Max(a.Y, b.Y) / cell) + 1;

            for (long cx = minCx; cx <= maxCx; cx++)
                for (long cy = minCy; cy <= maxCy; cy++)
                {
                    if (!grid.TryGetValue((cx, cy), out var bucket)) continue;
                    foreach (int k in bucket)
                    {
                        if (k == ia || k == ib) continue;
                        Point p = nodes[k];
                        double t = ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2;
                        if (t <= 0 || t >= 1) continue;
                        double projX = a.X + t * abx, projY = a.Y + t * aby;
                        double perp = Math.Sqrt((p.X - projX) * (p.X - projX) + (p.Y - projY) * (p.Y - projY));
                        if (perp > tol) continue;
                        if (Dist(p, a) > endTol && Dist(p, b) > endTol)
                            return true;
                    }
                }
            return false;
        }

        foreach (var (a, b, c, d) in quads)
        {
            if (EdgeHasInteriorNode(a, b)) return true;
            if (EdgeHasInteriorNode(b, c)) return true;
            if (EdgeHasInteriorNode(c, d)) return true;
            if (EdgeHasInteriorNode(d, a)) return true;
        }
        return false;
    }

    /// <summary>
    /// Coverage diagnostic: total quad area divided by the net polygon area (outer minus holes).
    /// 1.0 means the quads fill the region exactly; values &lt; 1 indicate unmeshed gaps/slivers.
    /// </summary>
    public static double MeshedAreaFraction(
        IReadOnlyList<Point> nodes,
        IReadOnlyList<(int a, int b, int c, int d)> quads,
        IReadOnlyList<Point> polygon,
        IReadOnlyList<IReadOnlyList<Point>>? holes = null)
    {
        if (polygon == null || polygon.Count < 3) return 0;

        double net = Math.Abs(SignedArea2(polygon)) * 0.5;
        if (holes != null)
            foreach (var h in holes)
                if (h is { Count: >= 3 })
                    net -= Math.Abs(SignedArea2(h)) * 0.5;
        if (net <= 1e-12) return 0;

        double meshed = 0;
        if (quads != null)
            foreach (var (a, b, c, d) in quads)
                meshed += QuadArea(nodes[a], nodes[b], nodes[c], nodes[d]);

        return meshed / net;
    }

    /// <summary>Max distance from sampled polygon boundary points to the nearest mesh node.</summary>
    public static double MaxExteriorBoundaryGap(
        IReadOnlyList<Point> nodes,
        IReadOnlyList<Point> polygon,
        double nearSize,
        double? yMin = null,
        double? yMax = null)
    {
        double sampleSpacing = Math.Max(nearSize * 0.5, 1e-4);
        double maxGap = 0;
        int n = polygon.Count;
        for (int i = 0; i < n; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % n];
            double len = Dist(a, b);
            int segs = Math.Max(1, (int)Math.Ceiling(len / sampleSpacing));
            for (int j = 0; j <= segs; j++)
            {
                double t = (double)j / segs;
                var s = new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                if (yMin.HasValue && s.Y < yMin.Value - 1e-8) continue;
                if (yMax.HasValue && s.Y > yMax.Value + 1e-8) continue;
                double dMin = double.MaxValue;
                foreach (var nd in nodes)
                {
                    double d = Dist(s, nd);
                    if (d < dMin) dMin = d;
                }
                if (dMin < double.MaxValue && dMin > maxGap) maxGap = dMin;
            }
        }
        return maxGap;
    }

    /// <summary>
    /// True when (cx, cy) lies in a horizontal material layer band suitable for native structured Q4
    /// (not the top/slope polygon-scan band where unstructured graded pairing is required).
    /// When no layer cuts exist, returns false so the entire domain uses pairing-only quads.
    /// </summary>
    public static bool IsCentroidInInternalMaterialBand(
        double cx,
        double cy,
        IReadOnlyList<(Point A, Point B)>? materialLayerSegments,
        double yMin,
        double yMax,
        double eps)
    {
        if (materialLayerSegments == null || materialLayerSegments.Count == 0)
            return false;

        var bands = BuildLayerBands(materialLayerSegments, yMin, yMax, eps);
        foreach (var b in bands)
        {
            if (b.UsePolygonScan) continue;
            if (cy >= b.YBottom - eps && cy <= b.YTop + eps
                && cx >= b.XLo - eps && cx <= b.XHi + eps)
                return true;
        }
        return false;
    }

    private static bool CornerInOrOnPolygon(Point p, IReadOnlyList<Point> polygon, IReadOnlyList<(Point a, Point b)> segs, double tol)
    {
        if (GradedSurfaceMeshGenerator.PointInPolygon(p, polygon)) return true;
        return CornerOnPolygonBoundary(p, segs, tol);
    }

    private static List<Point> BuildPinnedNodes(
        List<(int SurfaceNumber, IReadOnlyList<Point> Polygon, double NearSize, double GlobalSize, int Layers, double Growth)> safe,
        List<(int Item1, IReadOnlyList<Point> Item2, double NearSize)> holeSafe,
        IReadOnlyList<(Point A, Point B)>? materialLayerSegments,
        IReadOnlyList<Point>? pinnedBoundaryNodes,
        double mergeEps)
    {
        var pinned = new List<Point>(256);
        foreach (var (_, poly, nearSz, _, _, _) in safe)
            AddBoundaryPoints(poly, nearSz, pinned);
        foreach (var (_, poly, nearSz) in holeSafe)
            AddBoundaryPoints(poly, nearSz, pinned);

        double layerNear = safe.Min(r => r.NearSize);
        if (holeSafe.Count > 0)
            layerNear = Math.Min(layerNear, holeSafe.Min(h => h.NearSize));
        if (materialLayerSegments is { Count: > 0 })
        {
            foreach (var (a, b) in materialLayerSegments)
                AddSegmentPoints(a, b, layerNear, pinned);
        }

        if (pinnedBoundaryNodes is { Count: > 0 })
            pinned.AddRange(pinnedBoundaryNodes);

        return MergeDuplicates(pinned, mergeEps);
    }

    private sealed class LayerBand
    {
        public required double YBottom { get; init; }
        public required double YTop { get; init; }
        public required double XLo { get; init; }
        public required double XHi { get; init; }
        public required bool UsePolygonScan { get; init; }
    }

    private static List<LayerBand> BuildLayerBands(
        IReadOnlyList<(Point A, Point B)>? segments,
        double yMin,
        double yMax,
        double eps)
    {
        if (segments == null || segments.Count == 0)
            return new List<LayerBand> { new() { YBottom = yMin, YTop = yMax, XLo = 0, XHi = 0, UsePolygonScan = true } };

        var cuts = segments
            .Where(s => Math.Abs(s.A.Y - s.B.Y) <= eps)
            .Select(s => (Y: (s.A.Y + s.B.Y) * 0.5, XLo: Math.Min(s.A.X, s.B.X), XHi: Math.Max(s.A.X, s.B.X)))
            .OrderBy(c => c.Y)
            .ToList();

        if (cuts.Count == 0)
            return new List<LayerBand> { new() { YBottom = yMin, YTop = yMax, XLo = 0, XHi = 0, UsePolygonScan = true } };

        var bands = new List<LayerBand>();
        double yPrev = yMin;
        (double XLo, double XHi) prevSpan = (0, 0);
        bool hasPrevSpan = false;

        foreach (var cut in cuts)
        {
            if (cut.Y > yPrev + eps)
            {
                double xLo = cut.XLo;
                double xHi = cut.XHi;
                if (hasPrevSpan)
                {
                    xLo = Math.Min(xLo, prevSpan.XLo);
                    xHi = Math.Max(xHi, prevSpan.XHi);
                }

                bands.Add(new LayerBand
                {
                    YBottom = yPrev,
                    YTop = cut.Y,
                    XLo = xLo,
                    XHi = xHi,
                    UsePolygonScan = false,
                });
            }
            prevSpan = (cut.XLo, cut.XHi);
            hasPrevSpan = true;
            yPrev = cut.Y;
        }

        if (yMax > cuts[^1].Y + eps)
        {
            bands.Add(new LayerBand
            {
                YBottom = cuts[^1].Y,
                YTop = yMax,
                XLo = 0,
                XHi = 0,
                UsePolygonScan = true,
            });
        }

        return bands;
    }

    private static List<(double xL, double xR)> ResolveRowIntervals(
        DomainContext ctx,
        double y,
        IReadOnlyList<LayerBand> layerBands,
        double mergeEps)
    {
        double eps = Math.Max(mergeEps, 1e-8);
        foreach (var band in layerBands)
        {
            if (y < band.YBottom - eps || y > band.YTop + eps) continue;
            if (band.UsePolygonScan)
                return RowInsideIntervals(ctx, y);

            double xLo = band.XLo;
            double xHi = band.XHi;
            foreach (var (scanL, scanR) in RowInsideIntervals(ctx, y))
            {
                xLo = Math.Min(xLo, scanL);
                xHi = Math.Max(xHi, scanR);
            }

            double width = xHi - xLo;
            if (width <= mergeEps) break;
            return new List<(double, double)> { (xLo, xHi) };
        }

        return RowInsideIntervals(ctx, y);
    }

    /// <summary>
    /// Boundary-fitted column sweep. For each vertical grid line the region's true lower and upper
    /// boundary Y are sampled, and the column is divided into a fixed number of rows by interpolating
    /// between them. Every quad therefore follows the actual sloped top/bottom boundary instead of
    /// being clipped out of a global horizontal grid, which eliminates the shear (diagonal striping)
    /// and toe-sliver "fan" artefacts the legacy horizontal sweep produced on inclined boundaries.
    /// Material interfaces (horizontal OR inclined) are honoured by splitting each column into bands
    /// separated by the interface levels evaluated at that column; the bottom and top bands follow the
    /// (possibly inclined) actual boundaries, so layered benched slopes are meshed with boundary-fitted
    /// quads instead of skewed strips. Overhang / re-entrant columns (more than two boundary crossings)
    /// are meshed span-by-span over their disjoint vertical spans. Only regions without holes or child
    /// surfaces are handled here; anything else returns false so the caller falls back to the legacy
    /// sweep (which remains responsible for holed/nested domains).
    /// </summary>
    private static bool TryBuildColumnSweepMesh(
        IReadOnlyList<(int SurfaceNumber, DomainContext Ctx)> regions,
        IReadOnlyList<double> xCoords,
        IReadOnlyList<(Point A, Point B)>? materialLayerSegments,
        List<Point> pinned,
        double mergeEps,
        int maxNodes,
        out List<(int a, int b, int c, int d)> quads,
        out List<(int a, int b, int c)> tris,
        out List<Point> nodes)
    {
        quads = new List<(int, int, int, int)>();
        tris = new List<(int, int, int)>();
        nodes = new List<Point>();
        if (xCoords.Count < 2) return false;

        var interfaces = BuildInterfaceLevels(materialLayerSegments, mergeEps);
        var nodePool = new NodePool(mergeEps, pinned);
        var outQuads = new List<(int, int, int, int)>();
        var outTris = new List<(int, int, int)>();

        foreach (var (_, ctx) in regions)
        {
            // Column sweep only supports simple regions; defer holed/nested ones to the legacy path.
            if (ctx.HolePolys.Count > 0 || ctx.ChildPolys.Count > 0) return false;

            double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity;
            foreach (var p in ctx.Polygon) { yMin = Math.Min(yMin, p.Y); yMax = Math.Max(yMax, p.Y); }
            if (yMax - yMin <= mergeEps) return false;

            double cell = Math.Max(ctx.GlobalSize, mergeEps);
            double eps = Math.Max(mergeEps, 1e-8);
            int m = xCoords.Count;
            double areaTol = cell * cell * 1e-4;

            // Material interfaces (horizontal OR inclined) passing through this region become band
            // boundaries, ordered low -> high. Each is evaluated per column so inclined interfaces are
            // followed exactly. Bands run [bottom, L_1(x), ..., L_K(x), top].
            double cutEps = Math.Max(mergeEps, (yMax - yMin) * 1e-4);
            var levels = new List<InterfaceLevel>();
            foreach (var lv in interfaces)
                if (lv.MeanY > yMin + cutEps && lv.MeanY < yMax - cutEps)
                    levels.Add(lv);
            int K = levels.Count;
            int bandCount = K + 1;

            // Per column: all disjoint vertical spans (pairs of boundary crossings). A vertically-simple
            // column owns one span; an overhang / re-entrant column owns two or more, meshed separately.
            var colSpans = new List<(double lo, double hi)>[m];
            int maxSpans = 0;
            for (int i = 0; i < m; i++)
            {
                colSpans[i] = PairSpans(VerticalCrossings(ctx.Polygon, xCoords[i]), eps);
                maxSpans = Math.Max(maxSpans, colSpans[i].Count);
            }
            if (maxSpans == 0) return false;

            for (int s = 0; s < maxSpans; s++)
            {
                // Region-wide row count per band for this span index (max band thickness over columns).
                // A constant per-band count keeps neighbouring columns' node strings (and the shared
                // interface rows) identical, guaranteeing a conforming (hanging-node free) mesh.
                var maxBandH = new double[bandCount];
                bool anyColumn = false;
                for (int i = 0; i < m; i++)
                {
                    if (colSpans[i].Count <= s) continue;
                    anyColumn = true;
                    var (lo, hi) = colSpans[i][s];
                    double prev = lo;
                    for (int b = 0; b < bandCount; b++)
                    {
                        double topRaw = b < K ? levels[b].Eval(xCoords[i]) : hi;
                        double loC = Math.Clamp(prev, lo, hi);
                        double hiC = Math.Clamp(topRaw, lo, hi);
                        if (hiC < loC) hiC = loC;
                        maxBandH[b] = Math.Max(maxBandH[b], hiC - loC);
                        prev = topRaw;
                    }
                }
                if (!anyColumn) continue;

                var nBand = new int[bandCount];
                int totalRows = 0;
                for (int b = 0; b < bandCount; b++)
                {
                    nBand[b] = Math.Clamp((int)Math.Round(maxBandH[b] / cell), 1, 400);
                    totalRows += nBand[b];
                }
                if (totalRows < 1) continue;

                // Per-column node strings for this span. Each owning column has exactly totalRows+1
                // nodes placed band-by-band (the inter-band boundary node is shared). Where a band has
                // pinched out it collapses onto the boundary node and dedupes to one index.
                var cols = new int[m][];
                for (int i = 0; i < m; i++)
                {
                    if (colSpans[i].Count <= s) continue;
                    var (lo, hi) = colSpans[i][s];
                    var col = new int[totalRows + 1];
                    int idx = 0;
                    col[idx++] = nodePool.Get(new Point(xCoords[i], lo));
                    double prev = lo;
                    for (int b = 0; b < bandCount; b++)
                    {
                        double topRaw = b < K ? levels[b].Eval(xCoords[i]) : hi;
                        double loC = Math.Clamp(prev, lo, hi);
                        double hiC = Math.Clamp(topRaw, lo, hi);
                        if (hiC < loC) hiC = loC;
                        int nb = nBand[b];
                        for (int k = 1; k <= nb; k++)
                        {
                            double t = (double)k / nb;
                            col[idx++] = nodePool.Get(new Point(xCoords[i], loC + t * (hiC - loC)));
                        }
                        prev = topRaw;
                    }
                    cols[i] = col;
                    if (nodePool.Nodes.Count > maxNodes) return false;
                }

                for (int i = 0; i + 1 < m; i++)
                {
                    if (cols[i] == null || cols[i + 1] == null) continue;

                    // The column mid-line must own a matching span s that overlaps both columns' span s,
                    // so we never bridge a concavity/gap or stitch the wrong sheet of an overhang.
                    var midSpans = PairSpans(VerticalCrossings(ctx.Polygon, (xCoords[i] + xCoords[i + 1]) * 0.5), eps);
                    if (midSpans.Count <= s) continue;
                    var (midLo, midHi) = midSpans[s];
                    if (midHi - midLo <= eps) continue;
                    double overlapLo = Math.Max(colSpans[i][s].lo, colSpans[i + 1][s].lo);
                    double overlapHi = Math.Min(colSpans[i][s].hi, colSpans[i + 1][s].hi);
                    if (midHi < overlapLo - eps || midLo > overlapHi + eps) continue;

                    int[] left = cols[i], right = cols[i + 1];
                    for (int k = 0; k < totalRows; k++)
                    {
                        int a = left[k], b = right[k], c = right[k + 1], d = left[k + 1];
                        // Bow-tie (diagonal) collapse is never a valid cell.
                        if (a == c || b == d) continue;
                        if (QuadArea(nodePool.Nodes[a], nodePool.Nodes[b], nodePool.Nodes[c], nodePool.Nodes[d]) < areaTol) continue;
                        // Count distinct corners. Four => proper quad. Three (one collapsed edge at a
                        // band pinch-out) => emit a clean T3 at the wedge tip, taking the distinct
                        // corners in their existing CCW order. Fewer than three => degenerate, skip.
                        int distinct = 4;
                        if (a == b) distinct--;
                        if (b == c) distinct--;
                        if (c == d) distinct--;
                        if (d == a) distinct--;
                        if (distinct == 4)
                        {
                            outQuads.Add((a, b, c, d));
                        }
                        else if (distinct == 3)
                        {
                            // Walk the ordered corners, dropping a vertex equal to its predecessor
                            // (a==d folds the wrap-around), leaving 3 indices in CCW order.
                            var loop = new[] { a, b, c, d };
                            int t1 = -1, t2 = -1, t3 = -1, cnt = 0;
                            for (int v = 0; v < 4; v++)
                            {
                                int cur = loop[v];
                                int prev = loop[(v + 3) % 4];
                                if (cur == prev) continue;
                                if (cnt == 0) t1 = cur;
                                else if (cnt == 1) t2 = cur;
                                else t3 = cur;
                                cnt++;
                            }
                            if (cnt == 3) outTris.Add((t1, t2, t3));
                        }
                    }
                }
            }
        }

        if (outQuads.Count == 0 && outTris.Count == 0) return false;
        quads = outQuads;
        tris = outTris;
        nodes = nodePool.Nodes;
        return true;
    }

    // Vertical analogue of HorizontalCrossings: Y values where the vertical line x = <paramref name="x"/>
    // crosses the polygon boundary, sorted and de-duplicated. min/max of a clean 2-crossing result give
    // the region's lower/upper boundary at that column.
    private static List<double> VerticalCrossings(IReadOnlyList<Point> poly, double x)
    {
        var ys = new List<double>();
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % n];
            if (Math.Abs(a.X - b.X) < 1e-12)
            {
                if (Math.Abs(a.X - x) < 1e-10) { ys.Add(a.Y); ys.Add(b.Y); }
                continue;
            }
            double xMin = Math.Min(a.X, b.X);
            double xMax = Math.Max(a.X, b.X);
            if (x < xMin - 1e-10 || x > xMax + 1e-10) continue;
            double t = (x - a.X) / (b.X - a.X);
            if (t < -1e-10 || t > 1 + 1e-10) continue;
            ys.Add(a.Y + t * (b.Y - a.Y));
        }
        ys.Sort();
        return MergeScalarDuplicates(ys, 1e-8);
    }

    /// <summary>
    /// A single material-layer interface (horizontal or inclined) assembled from connected segments.
    /// <see cref="Eval"/> returns the interface Y at a given X (clamped to the interface X-extent), so
    /// the column sweep can place a conforming node row exactly on the interface in every column.
    /// </summary>
    private sealed class InterfaceLevel
    {
        public required List<(Point A, Point B)> Segments { get; init; }
        public required double MinX { get; init; }
        public required double MaxX { get; init; }
        public required double MeanY { get; init; }

        public double Eval(double x)
        {
            double xx = Math.Clamp(x, MinX, MaxX);
            foreach (var (a, b) in Segments)
            {
                double lo = Math.Min(a.X, b.X), hi = Math.Max(a.X, b.X);
                if (xx < lo - 1e-9 || xx > hi + 1e-9) continue;
                if (Math.Abs(b.X - a.X) < 1e-12) return (a.Y + b.Y) * 0.5;
                double t = Math.Clamp((xx - a.X) / (b.X - a.X), 0.0, 1.0);
                return a.Y + t * (b.Y - a.Y);
            }
            // X falls in a gap between segments of a broken interface: snap to nearest endpoint by X.
            double bestY = MeanY, bestDx = double.PositiveInfinity;
            foreach (var (a, b) in Segments)
            {
                double da = Math.Abs(a.X - xx);
                if (da < bestDx) { bestDx = da; bestY = a.Y; }
                double db = Math.Abs(b.X - xx);
                if (db < bestDx) { bestDx = db; bestY = b.Y; }
            }
            return bestY;
        }
    }

    /// <summary>
    /// Group material-layer segments into distinct interfaces (connected polylines) ordered low -> high
    /// by mean Y. Handles horizontal cuts and inclined interfaces uniformly; an interface that only
    /// spans part of the width is evaluated by clamping to its X-extent (see <see cref="InterfaceLevel.Eval"/>).
    /// </summary>
    private static List<InterfaceLevel> BuildInterfaceLevels(IReadOnlyList<(Point A, Point B)>? segments, double eps)
    {
        var result = new List<InterfaceLevel>();
        if (segments == null || segments.Count == 0) return result;

        double tol = Math.Max(eps, 1e-6);
        int n = segments.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        bool Touch(Point p, Point q) => Math.Abs(p.X - q.X) <= tol && Math.Abs(p.Y - q.Y) <= tol;

        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                var si = segments[i];
                var sj = segments[j];
                if (Touch(si.A, sj.A) || Touch(si.A, sj.B) || Touch(si.B, sj.A) || Touch(si.B, sj.B))
                    parent[Find(i)] = Find(j);
            }

        var groups = new Dictionary<int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            int r = Find(i);
            if (!groups.TryGetValue(r, out var lst)) { lst = new List<int>(); groups[r] = lst; }
            lst.Add(i);
        }

        foreach (var grp in groups.Values)
        {
            var segs = new List<(Point A, Point B)>(grp.Count);
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, sumY = 0;
            int cnt = 0;
            foreach (int idx in grp)
            {
                var (a, b) = segments[idx];
                segs.Add((a, b));
                minX = Math.Min(minX, Math.Min(a.X, b.X));
                maxX = Math.Max(maxX, Math.Max(a.X, b.X));
                sumY += a.Y + b.Y;
                cnt += 2;
            }
            segs.Sort((p, q) => Math.Min(p.A.X, p.B.X).CompareTo(Math.Min(q.A.X, q.B.X)));
            result.Add(new InterfaceLevel
            {
                Segments = segs,
                MinX = minX,
                MaxX = maxX,
                MeanY = cnt > 0 ? sumY / cnt : 0,
            });
        }

        result.Sort((p, q) => p.MeanY.CompareTo(q.MeanY));
        return result;
    }

    /// <summary>
    /// Pair sorted vertical boundary crossings into disjoint spans (lo, hi). One span for a
    /// vertically-simple column; two or more for an overhang / re-entrant column.
    /// </summary>
    private static List<(double lo, double hi)> PairSpans(IReadOnlyList<double> crossings, double eps)
    {
        var spans = new List<(double lo, double hi)>();
        for (int j = 0; j + 1 < crossings.Count; j += 2)
            if (crossings[j + 1] - crossings[j] > eps)
                spans.Add((crossings[j], crossings[j + 1]));
        return spans;
    }

    /// <summary>
    /// Mapped Q4 mesh: one shared column topology for the full height. Each row places nodes
    /// from left boundary to right boundary so columns lean with sloped faces (reference embankment style).
    /// Interior material-layer bands use full horizontal cut spans (Material 2 fills completely).
    /// </summary>
    private static bool TryBuildMappedSweepMeshes(
        IReadOnlyList<(int SurfaceNumber, DomainContext Ctx)> regions,
        IReadOnlyList<double> xCoords,
        IReadOnlyList<double> yCoords,
        IReadOnlyList<(Point A, Point B)>? materialLayerSegments,
        List<Point> pinned,
        double mergeEps,
        int maxNodes,
        out List<(int a, int b, int c, int d)> quads,
        out List<Point> nodes)
    {
        quads = new List<(int, int, int, int)>();
        if (yCoords.Count < 2) { nodes = new List<Point>(); return false; }

        var nodePool = new NodePool(mergeEps, pinned);
        int totalQuads = 0;
        double yMin = yCoords[0];
        double yMax = yCoords[^1];
        var layerBands = BuildLayerBands(materialLayerSegments, yMin, yMax, mergeEps);

        foreach (var (_, ctx) in regions)
        {
            if (!TrySweepMappedRegion(ctx, yCoords, xCoords, layerBands, materialLayerSegments, nodePool, mergeEps, pinned, quads, ref totalQuads, maxNodes))
            {
                nodes = new List<Point>();
                quads.Clear();
                return false;
            }
        }

        nodes = nodePool.Nodes;
        return quads.Count > 0;
    }

    private static int ResolveMappedColumnCount(
        DomainContext ctx,
        IReadOnlyList<double> yCoords,
        IReadOnlyList<double> xCoords,
        double mergeEps)
    {
        if (xCoords.Count >= 2)
            return Math.Max(1, xCoords.Count - 1);

        int nDiv = 1;
        foreach (double y in yCoords)
        {
            var intervals = RowInsideIntervals(ctx, y);
            if (intervals.Count != 1) continue;
            var (xL, xR) = intervals[0];
            double width = xR - xL;
            if (width <= mergeEps) continue;
            double target = SizeFieldAt(new Point((xL + xR) * 0.5, y), ctx.Segments, ctx.NearSize, ctx.GlobalSize, ctx.Growth);
            nDiv = Math.Max(nDiv, Math.Clamp((int)Math.Round(width / target), 1, 500));
        }
        return nDiv;
    }

    private sealed class RowStrip
    {
        public required double XL { get; init; }
        public required double XR { get; init; }
        public required int[] NodeIds { get; init; }
        public bool IsStructured { get; init; }
        public double MidX => (XL + XR) * 0.5;
    }

    private static List<double> ExtractCutYs(IReadOnlyList<(Point A, Point B)>? segments, double eps)
    {
        if (segments == null || segments.Count == 0) return new List<double>();
        return segments
            .Where(s => Math.Abs(s.A.Y - s.B.Y) <= eps)
            .Select(s => (s.A.Y + s.B.Y) * 0.5)
            .Distinct()
            .OrderBy(y => y)
            .ToList();
    }

    private static bool IsCutRow(double y, IReadOnlyList<double> cutYs, double eps)
    {
        foreach (var cy in cutYs)
            if (Math.Abs(y - cy) <= Math.Max(eps, 1e-8)) return true;
        return false;
    }

    private static bool TryGetInternalBandAtY(
        double y,
        IReadOnlyList<LayerBand> bands,
        double eps,
        out LayerBand band)
    {
        foreach (var b in bands)
        {
            if (b.UsePolygonScan) continue;
            if (y >= b.YBottom - eps && y <= b.YTop + eps)
            {
                band = b;
                return true;
            }
        }
        band = null!;
        return false;
    }

    private static (double xLo, double xHi) SpanAtCut(double cutY, IReadOnlyList<LayerBand> bands, double eps)
    {
        double xLo = double.PositiveInfinity, xHi = double.NegativeInfinity;
        foreach (var b in bands)
        {
            if (Math.Abs(b.YTop - cutY) > eps && Math.Abs(b.YBottom - cutY) > eps) continue;
            xLo = Math.Min(xLo, b.XLo);
            xHi = Math.Max(xHi, b.XHi);
        }
        if (xLo > xHi) return (0, 0);
        return (xLo, xHi);
    }

    private static int[] BuildStructuredRow(
        double y,
        IReadOnlyList<double> xCoords,
        double xLo,
        double xHi,
        DomainContext ctx,
        NodePool nodePool,
        double mergeEps)
    {
        double snapTol = Math.Max(ctx.NearSize * 0.15, mergeEps);
        var ids = new int[xCoords.Count];
        for (int k = 0; k < xCoords.Count; k++)
        {
            double x = Math.Clamp(xCoords[k], xLo, xHi);
            var pt = new Point(x, y);
            if (k == 0 || k == xCoords.Count - 1)
                pt = SnapRowEndpointToBoundary(pt, ctx, snapTol);
            ids[k] = nodePool.Get(pt);
        }
        return ids;
    }

    private static List<RowStrip> BuildSlopeStrips(
        double y,
        DomainContext ctx,
        IReadOnlyList<LayerBand> layerBands,
        int nDiv,
        NodePool nodePool,
        double mergeEps)
    {
        var intervals = ResolveRowIntervals(ctx, y, layerBands, mergeEps);
        if (intervals.Count == 0) return new List<RowStrip>();

        double maxW = 0;
        foreach (var (xL, xR) in intervals)
            maxW = Math.Max(maxW, xR - xL);
        if (maxW <= mergeEps) return new List<RowStrip>();

        var strips = new List<RowStrip>();
        double snapTol = Math.Max(ctx.NearSize * 0.15, mergeEps);

        foreach (var (xL, xR) in intervals)
        {
            double width = xR - xL;
            if (width <= mergeEps) continue;

            int stripDiv = Math.Max(1, (int)Math.Round(nDiv * width / maxW));
            int minDiv = Math.Max(1, (int)Math.Ceiling(width / Math.Max(ctx.GlobalSize, ctx.NearSize)));
            stripDiv = Math.Clamp(Math.Max(stripDiv, minDiv), 1, nDiv);

            var ids = new int[stripDiv + 1];
            for (int k = 0; k <= stripDiv; k++)
            {
                double t = (double)k / stripDiv;
                double x = xL + t * (xR - xL);
                var pt = new Point(x, y);
                if (k == 0 || k == stripDiv)
                    pt = SnapRowEndpointToBoundary(pt, ctx, snapTol);
                ids[k] = nodePool.Get(pt);
            }
            strips.Add(new RowStrip { XL = xL, XR = xR, NodeIds = ids, IsStructured = false });
        }

        return strips;
    }

    private static bool TrySweepMappedRegion(
        DomainContext ctx,
        IReadOnlyList<double> yCoords,
        IReadOnlyList<double> xCoords,
        IReadOnlyList<LayerBand> layerBands,
        IReadOnlyList<(Point A, Point B)>? materialLayerSegments,
        NodePool nodePool,
        double mergeEps,
        List<Point> pinned,
        List<(int a, int b, int c, int d)> quads,
        ref int totalQuads,
        int maxNodes)
    {
        if (xCoords.Count < 2) return false;

        int nDiv = ResolveMappedColumnCount(ctx, yCoords, xCoords, mergeEps);
        var cutYs = ExtractCutYs(materialLayerSegments, mergeEps);
        var rowStrips = new List<List<RowStrip>>();
        var interfaceRowCache = new Dictionary<int, int[]>();

        for (int j = 0; j < yCoords.Count; j++)
        {
            double y = yCoords[j];

            if (cutYs.Count > 0 && IsCutRow(y, cutYs, mergeEps))
            {
                if (!interfaceRowCache.TryGetValue(j, out var ifaceIds))
                {
                    var (xLo, xHi) = SpanAtCut(y, layerBands, mergeEps);
                    if (xHi - xLo <= mergeEps && TryGetInternalBandAtY(y, layerBands, mergeEps, out var cutBand))
                    {
                        xLo = cutBand.XLo;
                        xHi = cutBand.XHi;
                    }
                    ifaceIds = BuildStructuredRow(y, xCoords, xLo, xHi, ctx, nodePool, mergeEps);
                    interfaceRowCache[j] = ifaceIds;
                }
                rowStrips.Add(new List<RowStrip>
                {
                    new RowStrip
                    {
                        XL = xCoords[0],
                        XR = xCoords[^1],
                        NodeIds = ifaceIds,
                        IsStructured = true,
                    },
                });
                continue;
            }

            if (TryGetInternalBandAtY(y, layerBands, mergeEps, out var band) && !IsCutRow(y, cutYs, mergeEps))
            {
                var ids = BuildStructuredRow(y, xCoords, band.XLo, band.XHi, ctx, nodePool, mergeEps);
                rowStrips.Add(new List<RowStrip>
                {
                    new RowStrip { XL = band.XLo, XR = band.XHi, NodeIds = ids, IsStructured = true },
                });
                continue;
            }

            var slopeStrips = BuildSlopeStrips(y, ctx, layerBands, nDiv, nodePool, mergeEps);
            if (slopeStrips.Count == 0) return false;
            rowStrips.Add(slopeStrips);
        }

        for (int j = 0; j < rowStrips.Count - 1; j++)
            ConnectRowPair(rowStrips[j], rowStrips[j + 1], ctx, nodePool, quads, ref totalQuads, maxNodes, mergeEps);

        FillHorizontalShelfQuadsGapOnly(ctx, yCoords, rowStrips, nodePool, pinned, mergeEps, quads, ref totalQuads, maxNodes);
        DedupeGeometricQuads(quads, nodePool.Nodes, mergeEps);

        return true;
    }

    private static void ConnectRowPair(
        List<RowStrip> lower,
        List<RowStrip> upper,
        DomainContext ctx,
        NodePool nodePool,
        List<(int a, int b, int c, int d)> quads,
        ref int totalQuads,
        int maxNodes,
        double mergeEps)
    {
        bool lowerStruct = lower.Count == 1 && lower[0].IsStructured;
        bool upperStruct = upper.Count == 1 && upper[0].IsStructured;

        if (lowerStruct && upperStruct)
        {
            ConnectStructuredRows(lower[0].NodeIds, upper[0].NodeIds, ctx, nodePool, quads, ref totalQuads, maxNodes);
            return;
        }

        if (lowerStruct && !upperStruct)
        {
            foreach (var strip in upper)
                ConnectStructuredToStrip(lower[0].NodeIds, strip, structuredIsLower: true, ctx, nodePool, quads, ref totalQuads, maxNodes, mergeEps);
            return;
        }

        if (!lowerStruct && upperStruct)
        {
            foreach (var strip in lower)
                ConnectStructuredToStrip(upper[0].NodeIds, strip, structuredIsLower: false, ctx, nodePool, quads, ref totalQuads, maxNodes, mergeEps);
            return;
        }

        foreach (var stripUpper in upper)
        {
            var stripLower = FindBestMatchingStrip(stripUpper, lower, mergeEps);
            if (stripLower == null) continue;
            ConnectStripQuads(stripLower, stripUpper, ctx, nodePool, quads, ref totalQuads, maxNodes);
            if (nodePool.Nodes.Count > maxNodes || totalQuads > maxNodes * 2) return;
        }
    }

    private static void ConnectStructuredRows(
        int[] rowLower,
        int[] rowUpper,
        DomainContext ctx,
        NodePool nodePool,
        List<(int a, int b, int c, int d)> quads,
        ref int totalQuads,
        int maxNodes)
    {
        if (rowLower.Length != rowUpper.Length || rowLower.Length < 2) return;

        for (int k = 0; k < rowLower.Length - 1; k++)
        {
            int n1 = rowLower[k];
            int n2 = rowLower[k + 1];
            int n3 = rowUpper[k + 1];
            int n4 = rowUpper[k];
            if (n1 == n2 || n2 == n3 || n3 == n4 || n4 == n1) continue;

            var p1 = nodePool.Nodes[n1]; var p2 = nodePool.Nodes[n2];
            var p3 = nodePool.Nodes[n3]; var p4 = nodePool.Nodes[n4];
            TryAddSweepQuad(n1, n2, n3, n4, p1, p2, p3, p4, ctx, nodePool, quads, ref totalQuads, maxNodes);
        }
    }

    private static void ConnectStructuredToStrip(
        int[] structuredRow,
        RowStrip strip,
        bool structuredIsLower,
        DomainContext ctx,
        NodePool nodePool,
        List<(int a, int b, int c, int d)> quads,
        ref int totalQuads,
        int maxNodes,
        double mergeEps)
    {
        var stripIds = strip.NodeIds;
        if (stripIds.Length < 2 || structuredRow.Length < 2) return;

        double tol = Math.Max(ctx.NearSize * 0.08, mergeEps);

        for (int k = 0; k < stripIds.Length - 1; k++)
        {
            var pStrip0 = nodePool.Nodes[stripIds[k]];
            var pStrip1 = nodePool.Nodes[stripIds[k + 1]];
            int i0 = NearestColumnForX(structuredRow, nodePool, pStrip0.X, tol);
            int i1 = NearestColumnForX(structuredRow, nodePool, pStrip1.X, tol);
            if (i0 < 0 || i1 < 0) continue;
            if (i0 == i1)
            {
                i1 = Math.Min(i0 + 1, structuredRow.Length - 1);
                if (i0 == i1) continue;
            }

            int ns0 = structuredRow[Math.Min(i0, i1)];
            int ns1 = structuredRow[Math.Max(i0, i1)];
            int ss0 = stripIds[k];
            int ss1 = stripIds[k + 1];

            if (structuredIsLower)
            {
                var p1 = nodePool.Nodes[ns0]; var p2 = nodePool.Nodes[ns1];
                var p3 = nodePool.Nodes[ss1]; var p4 = nodePool.Nodes[ss0];
                TryAddSweepQuad(ns0, ns1, ss1, ss0, p1, p2, p3, p4, ctx, nodePool, quads, ref totalQuads, maxNodes);
            }
            else
            {
                var p1 = nodePool.Nodes[ss0]; var p2 = nodePool.Nodes[ss1];
                var p3 = nodePool.Nodes[ns1]; var p4 = nodePool.Nodes[ns0];
                TryAddSweepQuad(ss0, ss1, ns1, ns0, p1, p2, p3, p4, ctx, nodePool, quads, ref totalQuads, maxNodes);
            }
        }
    }

    /// <summary>Remove quads that duplicate the same four corner locations (stacked/overdrawn elements).</summary>
    private static void DedupeGeometricQuads(
        List<(int a, int b, int c, int d)> quads,
        IReadOnlyList<Point> nodes,
        double mergeEps)
    {
        double cell = Math.Max(mergeEps, 1e-6);
        var seen = new HashSet<string>();
        var kept = new List<(int, int, int, int)>(quads.Count);

        foreach (var q in quads)
        {
            var pts = new[] { nodes[q.a], nodes[q.b], nodes[q.c], nodes[q.d] }
                .Select(p => ((long)Math.Round(p.X / cell), (long)Math.Round(p.Y / cell)))
                .OrderBy(t => t.Item1).ThenBy(t => t.Item2)
                .ToList();
            var key = string.Join(":", pts.Select(t => $"{t.Item1},{t.Item2}"));
            if (seen.Add(key))
                kept.Add(q);
        }

        quads.Clear();
        quads.AddRange(kept);
    }

    private static bool HasQuadCoveringPoint(
        Point p,
        IReadOnlyList<(int a, int b, int c, int d)> quads,
        IReadOnlyList<Point> nodes,
        double tol)
    {
        foreach (var q in quads)
        {
            var c1 = nodes[q.a]; var c2 = nodes[q.b]; var c3 = nodes[q.c]; var c4 = nodes[q.d];
            if (PointInQuad(p, c1, c2, c3, c4, tol))
                return true;
        }
        return false;
    }

    private static bool PointInQuad(Point p, Point a, Point b, Point c, Point d, double tol)
    {
        static double Cross(Point o, Point u, Point v) =>
            (u.X - o.X) * (v.Y - o.Y) - (u.Y - o.Y) * (v.X - o.X);

        double c1 = Cross(a, b, p);
        double c2 = Cross(b, c, p);
        double c3 = Cross(c, d, p);
        double c4 = Cross(d, a, p);
        bool hasNeg = c1 < -tol || c2 < -tol || c3 < -tol || c4 < -tol;
        bool hasPos = c1 > tol || c2 > tol || c3 > tol || c4 > tol;
        return !(hasNeg && hasPos);
    }

    /// <summary>Nodes lying on a material layer cut elevation (for interface conformity tests).</summary>
    public static int CountNodesAtCutY(IReadOnlyList<Point> nodes, double cutY, double tol = 1e-4)
    {
        int count = 0;
        foreach (var p in nodes)
            if (Math.Abs(p.Y - cutY) <= tol) count++;
        return count;
    }

    /// <summary>Quads whose bottom and top edges are horizontal within tolerance.</summary>
    public static int CountAxisAlignedQuads(
        IReadOnlyList<Point> nodes,
        IReadOnlyList<(int a, int b, int c, int d)> quads,
        double yMin,
        double yMax,
        double tol = 1e-4)
    {
        int count = 0;
        foreach (var q in quads)
        {
            var p1 = nodes[q.a]; var p2 = nodes[q.b]; var p3 = nodes[q.c]; var p4 = nodes[q.d];
            double cy = (p1.Y + p2.Y + p3.Y + p4.Y) * 0.25;
            if (cy < yMin || cy > yMax) continue;
            if (Math.Abs(p1.Y - p2.Y) <= tol && Math.Abs(p4.Y - p3.Y) <= tol)
                count++;
        }
        return count;
    }

    /// <summary>Geometric duplicate quads whose centroid lies near a layer cut.</summary>
    public static int CountGeometricDuplicateQuadsNearCut(
        IReadOnlyList<Point> nodes,
        IReadOnlyList<(int a, int b, int c, int d)> quads,
        double cutY,
        double bandHalfWidth,
        double tol = 1e-4)
    {
        var nearCut = quads.Where(q =>
        {
            double cy = (nodes[q.a].Y + nodes[q.b].Y + nodes[q.c].Y + nodes[q.d].Y) * 0.25;
            return Math.Abs(cy - cutY) <= bandHalfWidth;
        }).ToList();
        return CountGeometricDuplicateQuads(nodes, nearCut, tol);
    }

    /// <summary>Count geometrically duplicate quads (for regression tests).</summary>
    public static int CountGeometricDuplicateQuads(
        IReadOnlyList<Point> nodes,
        IReadOnlyList<(int a, int b, int c, int d)> quads,
        double tol = 1e-4)
    {
        double cell = Math.Max(tol, 1e-6);
        var seen = new HashSet<string>();
        int dup = 0;
        foreach (var q in quads)
        {
            var key = string.Join(":", new[] { nodes[q.a], nodes[q.b], nodes[q.c], nodes[q.d] }
                .Select(p => ((long)Math.Round(p.X / cell), (long)Math.Round(p.Y / cell)))
                .OrderBy(t => t.Item1).ThenBy(t => t.Item2)
                .Select(t => $"{t.Item1},{t.Item2}"));
            if (!seen.Add(key)) dup++;
        }
        return dup;
    }

    private static RowStrip? FindBestMatchingStrip(RowStrip a, List<RowStrip> nextRow, double mergeEps)
    {
        RowStrip? best = null;
        double bestOverlap = mergeEps;
        foreach (var b in nextRow)
        {
            double overlap = Math.Min(a.XR, b.XR) - Math.Max(a.XL, b.XL);
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                best = b;
            }
        }
        return best;
    }

    private static void ConnectStripQuads(
        RowStrip stripA,
        RowStrip stripB,
        DomainContext ctx,
        NodePool nodePool,
        List<(int a, int b, int c, int d)> quads,
        ref int totalQuads,
        int maxNodes)
    {
        var rowA = stripA.NodeIds;
        var rowB = stripB.NodeIds;
        int nConnect = Math.Min(rowA.Length, rowB.Length);
        if (nConnect < 2) return;

        for (int k = 0; k < nConnect - 1; k++)
        {
            int ia0 = MapStripColumn(rowA.Length, k, nConnect - 1);
            int ia1 = MapStripColumn(rowA.Length, k + 1, nConnect - 1);
            int ib1 = MapStripColumn(rowB.Length, k + 1, nConnect - 1);
            int ib0 = MapStripColumn(rowB.Length, k, nConnect - 1);

            int n1 = rowA[ia0];
            int n2 = rowA[ia1];
            int n3 = rowB[ib1];
            int n4 = rowB[ib0];
            if (n1 == n2 || n2 == n3 || n3 == n4 || n4 == n1) continue;

            var p1 = nodePool.Nodes[n1]; var p2 = nodePool.Nodes[n2];
            var p3 = nodePool.Nodes[n3]; var p4 = nodePool.Nodes[n4];
            if (!TryAddSweepQuad(n1, n2, n3, n4, p1, p2, p3, p4, ctx, nodePool, quads, ref totalQuads, maxNodes))
            {
                if (nodePool.Nodes.Count > maxNodes || totalQuads > maxNodes * 2) return;
            }
        }
    }

    private static int MapStripColumn(int rowLen, int k, int nConnect)
    {
        if (rowLen <= 1) return 0;
        if (nConnect <= 0) return Math.Clamp(k, 0, rowLen - 1);
        return Math.Clamp((int)Math.Round((double)k / nConnect * (rowLen - 1)), 0, rowLen - 1);
    }

    private static bool TryAddSweepQuad(
        int n1, int n2, int n3, int n4,
        Point p1, Point p2, Point p3, Point p4,
        DomainContext ctx,
        NodePool nodePool,
        List<(int a, int b, int c, int d)> quads,
        ref int totalQuads,
        int maxNodes)
    {
        if (!IsAcceptedBoundaryQuad(p1, p2, p3, p4, ctx, ctx.NearSize))
            return true;

        quads.Add((n1, n2, n3, n4));
        totalQuads++;
        return nodePool.Nodes.Count <= maxNodes && totalQuads <= maxNodes * 2;
    }

    /// <summary>
    /// Fill wedge gaps under horizontal boundary shelves only where the main sweep left a hole.
    /// </summary>
    private static void FillHorizontalShelfQuadsGapOnly(
        DomainContext ctx,
        IReadOnlyList<double> yCoords,
        IReadOnlyList<List<RowStrip>> rowStrips,
        NodePool nodePool,
        List<Point> pinned,
        double mergeEps,
        List<(int a, int b, int c, int d)> quads,
        ref int totalQuads,
        int maxNodes)
    {
        if (rowStrips.Count < 2) return;

        var poly = ctx.Polygon;
        int n = poly.Count;
        double tol = Math.Max(ctx.NearSize * 0.08, mergeEps);
        var existing = new HashSet<(int, int, int, int)>(
            quads.Select(q => NormalizeQuadKey(q.a, q.b, q.c, q.d)));

        for (int i = 0; i < n; i++)
        {
            var va = poly[i];
            var vb = poly[(i + 1) % n];
            if (Math.Abs(va.Y - vb.Y) > tol) continue;

            double edgeY = (va.Y + vb.Y) * 0.5;
            double edgeXLo = Math.Min(va.X, vb.X);
            double edgeXHi = Math.Max(va.X, vb.X);
            int jTop = FindYIndex(yCoords, edgeY, tol);
            if (jTop <= 0 || jTop >= rowStrips.Count) continue;

            var stripTop = FindStripCoveringX(rowStrips[jTop], edgeXLo, edgeXHi, tol);
            var stripBot = FindStripCoveringX(rowStrips[jTop - 1], edgeXLo, edgeXHi, tol);
            if (stripTop == null || stripBot == null) continue;

            var rowTop = stripTop.NodeIds;
            var rowBot = stripBot.NodeIds;
            var chain = CollectNodesOnSegment(va, vb, pinned, nodePool, tol);
            if (chain.Count < 2) continue;

            for (int k = 0; k < chain.Count - 1; k++)
            {
                var b0 = chain[k];
                var b1 = chain[k + 1];
                if (Dist(b0, b1) < tol) continue;

                int i0 = NearestColumnForX(rowTop, nodePool, b0.X, tol);
                int i1 = NearestColumnForX(rowTop, nodePool, b1.X, tol);
                if (i0 < 0 || i1 < 0) continue;

                int nTop0 = nodePool.Get(b0);
                int nTop1 = nodePool.Get(b1);
                int nBot0 = rowBot[Math.Clamp(i0, 0, rowBot.Length - 1)];
                int nBot1 = rowBot[Math.Clamp(i1, 0, rowBot.Length - 1)];
                if (nTop0 == nTop1 || nBot0 == nBot1 || nTop0 == nBot0 || nTop1 == nBot1) continue;

                var p1 = nodePool.Nodes[nTop0]; var p2 = nodePool.Nodes[nTop1];
                var p3 = nodePool.Nodes[nBot1]; var p4 = nodePool.Nodes[nBot0];
                if (!IsAcceptedBoundaryQuad(p1, p2, p3, p4, ctx, ctx.NearSize)) continue;

                var mid = new Point((b0.X + b1.X) * 0.5, (b0.Y + b1.Y) * 0.5);
                if (HasQuadCoveringPoint(mid, quads, nodePool.Nodes, tol)) continue;

                var key = NormalizeQuadKey(nTop0, nTop1, nBot1, nBot0);
                if (!existing.Add(key)) continue;
                quads.Add((nTop0, nTop1, nBot1, nBot0));
                totalQuads++;
                if (nodePool.Nodes.Count > maxNodes || totalQuads > maxNodes * 2) return;
            }
        }
    }

    private static RowStrip? FindStripCoveringX(List<RowStrip> strips, double xLo, double xHi, double tol)
    {
        double mid = (xLo + xHi) * 0.5;
        RowStrip? best = null;
        double bestOverlap = -1;
        foreach (var s in strips)
        {
            double overlap = Math.Min(s.XR, xHi) - Math.Max(s.XL, xLo);
            if (overlap > bestOverlap && mid >= s.XL - tol && mid <= s.XR + tol)
            {
                bestOverlap = overlap;
                best = s;
            }
        }
        return best;
    }

    private static int FindYIndex(IReadOnlyList<double> yCoords, double y, double eps)
    {
        int best = -1;
        double bestD = double.MaxValue;
        for (int i = 0; i < yCoords.Count; i++)
        {
            double d = Math.Abs(yCoords[i] - y);
            if (d <= Math.Max(eps, 1e-8) && d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    private static int NearestColumnForX(int[] row, NodePool nodePool, double x, double tol)
    {
        int best = -1;
        double bestD = double.MaxValue;
        for (int i = 0; i < row.Length; i++)
        {
            double dx = Math.Abs(nodePool.Nodes[row[i]].X - x);
            if (dx < bestD) { bestD = dx; best = i; }
        }
        return bestD <= Math.Max(tol * 4, 1e-4) ? best : -1;
    }

    private static List<Point> CollectNodesOnSegment(
        Point a, Point b, List<Point> pinned, NodePool nodePool, double tol)
    {
        var hits = new List<(double t, Point p)>();
        hits.Add((0, a));
        hits.Add((1, b));
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 < 1e-20) return new List<Point> { a };

        void TryAddPoint(Point p)
        {
            var proj = ProjectToSegment(p, a, b);
            if (Dist(p, proj) > tol && Dist(proj, a) > tol && Dist(proj, b) > tol) return;
            double t = len2 < 1e-20 ? 0 : ((proj.X - a.X) * dx + (proj.Y - a.Y) * dy) / len2;
            if (t < -tol || t > 1 + tol) return;
            t = Math.Clamp(t, 0, 1);
            hits.Add((t, new Point(a.X + t * dx, a.Y + t * dy)));
        }

        foreach (var p in pinned) TryAddPoint(p);
        foreach (var p in nodePool.Nodes) TryAddPoint(p);

        hits.Sort((x, y) => x.t.CompareTo(y.t));
        var chain = new List<Point>();
        foreach (var (_, p) in hits)
        {
            if (chain.Count == 0 || Dist(chain[^1], p) > tol)
                chain.Add(p);
        }
        return chain;
    }

    private static Point SnapRowEndpointToBoundary(Point p, DomainContext ctx, double snapTol)
    {
        double tol = Math.Max(snapTol * 0.05, 1e-6);
        if (IsValidBoundaryPoint(p, ctx, tol)) return p;

        var (dist, proj) = NearestExteriorBoundaryPoint(p, ctx);
        if (dist <= snapTol && IsValidBoundaryPoint(proj, ctx, tol))
            return proj;

        return p;
    }

    private static List<(double xL, double xR)> RowInsideIntervals(DomainContext ctx, double y)
    {
        var intervals = IntervalsFromCrossings(HorizontalCrossings(ctx.Polygon, y));
        foreach (var cutPoly in ctx.ChildPolys.Concat(ctx.HolePolys))
            intervals = SubtractIntervals(intervals, IntervalsFromCrossings(HorizontalCrossings(cutPoly, y)));
        return MergeTouchingIntervals(intervals, 1e-8);
    }

    private static List<double> HorizontalCrossings(IReadOnlyList<Point> poly, double y)
    {
        var xs = new List<double>();
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % n];
            if (Math.Abs(a.Y - b.Y) < 1e-12)
            {
                if (Math.Abs(a.Y - y) < 1e-10) { xs.Add(a.X); xs.Add(b.X); }
                continue;
            }
            double yMin = Math.Min(a.Y, b.Y);
            double yMax = Math.Max(a.Y, b.Y);
            if (y < yMin - 1e-10 || y > yMax + 1e-10) continue;
            double t = (y - a.Y) / (b.Y - a.Y);
            if (t < -1e-10 || t > 1 + 1e-10) continue;
            xs.Add(a.X + t * (b.X - a.X));
        }
        xs.Sort();
        return MergeScalarDuplicates(xs, 1e-8);
    }

    private static List<(double xL, double xR)> IntervalsFromCrossings(IReadOnlyList<double> xs)
    {
        var list = new List<(double, double)>();
        for (int i = 0; i + 1 < xs.Count; i += 2)
            list.Add((xs[i], xs[i + 1]));
        return list;
    }

    private static List<(double xL, double xR)> SubtractIntervals(
        List<(double xL, double xR)> baseIntervals,
        List<(double xL, double xR)> cuts)
    {
        var result = baseIntervals;
        foreach (var (cL, cR) in cuts)
        {
            var next = new List<(double, double)>();
            foreach (var (bL, bR) in result)
            {
                if (cR <= bL || cL >= bR) { next.Add((bL, bR)); continue; }
                if (bL < cL) next.Add((bL, cL));
                if (cR < bR) next.Add((cR, bR));
            }
            result = next;
            if (result.Count == 0) break;
        }
        return result;
    }

    private static List<(double xL, double xR)> MergeTouchingIntervals(List<(double xL, double xR)> intervals, double eps)
    {
        if (intervals.Count == 0) return intervals;
        intervals.Sort((a, b) => a.xL.CompareTo(b.xL));
        var merged = new List<(double xL, double xR)> { intervals[0] };
        for (int i = 1; i < intervals.Count; i++)
        {
            var last = merged[^1];
            var cur = intervals[i];
            if (cur.xL <= last.xR + eps)
                merged[^1] = (last.xL, Math.Max(last.xR, cur.xR));
            else
                merged.Add(cur);
        }
        return merged;
    }

    // Removes nodes that are not referenced by any quad and reindexes the quads in place so the
    // shared node list stays compact (all region partitions index into the same pruned list).
    private static void PruneUnusedNodes(
        ref List<Point> nodes,
        List<(int a, int b, int c, int d)> quads,
        List<(int a, int b, int c)>? tris = null)
    {
        if (nodes.Count == 0 || (quads.Count == 0 && (tris == null || tris.Count == 0))) return;

        var used = new bool[nodes.Count];
        foreach (var (a, b, c, d) in quads)
        {
            used[a] = true; used[b] = true; used[c] = true; used[d] = true;
        }
        if (tris != null)
            foreach (var (a, b, c) in tris)
            {
                used[a] = true; used[b] = true; used[c] = true;
            }

        var remap = new int[nodes.Count];
        var pruned = new List<Point>(nodes.Count);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (used[i]) { remap[i] = pruned.Count; pruned.Add(nodes[i]); }
            else remap[i] = -1;
        }

        if (pruned.Count == nodes.Count) return; // nothing to prune

        for (int i = 0; i < quads.Count; i++)
        {
            var q = quads[i];
            quads[i] = (remap[q.a], remap[q.b], remap[q.c], remap[q.d]);
        }
        if (tris != null)
            for (int i = 0; i < tris.Count; i++)
            {
                var t = tris[i];
                tris[i] = (remap[t.a], remap[t.b], remap[t.c]);
            }
        nodes = pruned;
    }

    // Collects geometry-feature axis coordinates (polygon and hole vertices) that the structured
    // column/row grid must align to. Boundary sample points (pinned) are deliberately NOT added
    // here: feeding the fine near-size boundary samples into the global grid forced the entire
    // interior to be near-size-fine in both directions (~25x more quads than needed), which made
    // generation and canvas population extremely slow. Interior density is supplied by the uniform
    // global-size subdivision in BuildAxisCoords; boundary conformity is handled by row-endpoint
    // snapping and the boundary-strip fill, with any unused boundary seeds pruned afterwards.
    private static List<double> CollectAxisValues(
        List<(int SurfaceNumber, IReadOnlyList<Point> Polygon, double NearSize, double GlobalSize, int Layers, double Growth)> safe,
        List<(int Item1, IReadOnlyList<Point> Item2, double NearSize)> holeSafe,
        bool axisX)
    {
        var vals = new List<double>(64);
        foreach (var (_, poly, _, _, _, _) in safe)
            foreach (var p in poly)
                vals.Add(axisX ? p.X : p.Y);
        foreach (var (_, poly, _) in holeSafe)
            foreach (var p in poly)
                vals.Add(axisX ? p.X : p.Y);
        return vals;
    }

    private static List<double> BuildAxisCoords(double minV, double maxV, double cellSize, List<double> extras, double mergeEps)
    {
        var vals = new List<double> { minV, maxV };
        vals.AddRange(extras);
        int n = Math.Clamp((int)Math.Ceiling(Math.Max(maxV - minV, cellSize) / cellSize), 1, 400);
        for (int i = 0; i <= n; i++)
            vals.Add(minV + i * (maxV - minV) / n);
        vals.Sort();
        return MergeScalarDuplicates(vals, mergeEps);
    }

    private static List<double> MergeScalarDuplicates(List<double> vals, double eps)
    {
        var result = new List<double>(vals.Count);
        foreach (var v in vals)
        {
            if (result.Count == 0 || Math.Abs(result[^1] - v) > eps)
                result.Add(v);
        }
        return result;
    }

    private static (List<(int a, int b, int c, int d)> quads, List<Point> nodes, int maxDepth) BuildAdaptiveSharedQuads(
        IReadOnlyList<DomainContext> contexts,
        IReadOnlyList<double> xCoords,
        IReadOnlyList<double> yCoords,
        List<Point> pinned,
        double mergeEps,
        int maxNodes)
    {
        int nxe = Math.Max(1, xCoords.Count - 1);
        int nye = Math.Max(1, yCoords.Count - 1);
        var levels = new int[nxe, nye];
        const int maxDepthCap = 12;
        int maxDepth = 0;

        for (int pass = 0; pass < maxDepthCap + 4; pass++)
        {
            bool anyMixed = false;
            for (int j = 0; j < nye; j++)
            {
                for (int i = 0; i < nxe; i++)
                {
                    int L = levels[i, j];
                    if (L >= maxDepthCap) continue;

                    foreach (var ctx in contexts)
                    {
                        if (!HasMixedLeaf(i, j, L, xCoords, yCoords, ctx, out double leafSize))
                            continue;

                        var center = LeafCenter(i, j, xCoords, yCoords);
                        double target = SizeFieldAt(center, ctx.Segments, ctx.NearSize, ctx.GlobalSize, ctx.Growth);
                        if (leafSize > target * 1.02)
                        {
                            levels[i, j]++;
                            maxDepth = Math.Max(maxDepth, levels[i, j]);
                            anyMixed = true;
                            break;
                        }
                    }
                }
            }

            SynchronizeFaceLevels(levels, nxe, nye);

            if (!anyMixed)
                break;

            if (EstimateNodeCount(levels, nxe, nye) > maxNodes)
                break;
        }

        var nodeIndex = new NodePool(mergeEps, pinned);
        var quads = new List<(int, int, int, int)>(nxe * nye * 4);

        for (int j = 0; j < nye; j++)
        {
            for (int i = 0; i < nxe; i++)
            {
                int L = levels[i, j];
                int subdiv = 1 << L;
                double x0 = xCoords[i];
                double y0 = yCoords[j];
                double x1 = xCoords[i + 1];
                double y1 = yCoords[j + 1];
                double hdx = (x1 - x0) / subdiv;
                double hdy = (y1 - y0) / subdiv;
                double snapTol = Math.Min(Math.Abs(hdx), Math.Abs(hdy)) * 1.05;

                for (int sj = 0; sj < subdiv; sj++)
                {
                    for (int si = 0; si < subdiv; si++)
                    {
                        var p1 = new Point(x0 + si * hdx, y0 + sj * hdy);
                        var p2 = new Point(x0 + (si + 1) * hdx, y0 + sj * hdy);
                        var p3 = new Point(x0 + (si + 1) * hdx, y0 + (sj + 1) * hdy);
                        var p4 = new Point(x0 + si * hdx, y0 + (sj + 1) * hdy);

                        foreach (var ctx in contexts)
                        {
                            if (!TryBuildBoundarySnappedQuad(p1, p2, p3, p4, ctx, snapTol,
                                    out var s1, out var s2, out var s3, out var s4))
                                continue;

                            int n1 = nodeIndex.Get(s1);
                            int n2 = nodeIndex.Get(s2);
                            int n3 = nodeIndex.Get(s3);
                            int n4 = nodeIndex.Get(s4);
                            if (n1 == n2 || n2 == n3 || n3 == n4 || n4 == n1) continue;
                            quads.Add((n1, n2, n3, n4));
                            break;
                        }
                    }
                }
            }
        }

        return (quads, nodeIndex.Nodes, maxDepth);
    }

    private static bool HasMixedLeaf(
        int i, int j, int L,
        IReadOnlyList<double> xCoords, IReadOnlyList<double> yCoords,
        DomainContext ctx,
        out double leafSize)
    {
        int subdiv = 1 << L;
        double x0 = xCoords[i];
        double y0 = yCoords[j];
        double x1 = xCoords[i + 1];
        double y1 = yCoords[j + 1];
        double hdx = (x1 - x0) / subdiv;
        double hdy = (y1 - y0) / subdiv;
        leafSize = Math.Min(Math.Abs(hdx), Math.Abs(hdy));

        for (int sj = 0; sj < subdiv; sj++)
        {
            for (int si = 0; si < subdiv; si++)
            {
                var p1 = new Point(x0 + si * hdx, y0 + sj * hdy);
                var p2 = new Point(x0 + (si + 1) * hdx, y0 + sj * hdy);
                var p3 = new Point(x0 + (si + 1) * hdx, y0 + (sj + 1) * hdy);
                var p4 = new Point(x0 + si * hdx, y0 + (sj + 1) * hdy);
                if (ClassifyQuad(p1, p2, p3, p4, ctx) == QuadClass.Mixed)
                    return true;
            }
        }
        return false;
    }

    private static Point LeafCenter(int i, int j, IReadOnlyList<double> xCoords, IReadOnlyList<double> yCoords)
        => new((xCoords[i] + xCoords[i + 1]) * 0.5, (yCoords[j] + yCoords[j + 1]) * 0.5);

    private static void SynchronizeFaceLevels(int[,] levels, int nxe, int nye)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int j = 0; j < nye; j++)
            {
                for (int i = 0; i < nxe; i++)
                {
                    if (i + 1 < nxe)
                    {
                        int m = Math.Max(levels[i, j], levels[i + 1, j]);
                        if (levels[i, j] != m) { levels[i, j] = m; changed = true; }
                        if (levels[i + 1, j] != m) { levels[i + 1, j] = m; changed = true; }
                    }
                    if (j + 1 < nye)
                    {
                        int m = Math.Max(levels[i, j], levels[i, j + 1]);
                        if (levels[i, j] != m) { levels[i, j] = m; changed = true; }
                        if (levels[i, j + 1] != m) { levels[i, j + 1] = m; changed = true; }
                    }
                }
            }
        }
    }

    private static int EstimateNodeCount(int[,] levels, int nxe, int nye)
    {
        int sum = 0;
        for (int j = 0; j < nye; j++)
            for (int i = 0; i < nxe; i++)
                sum += (1 << levels[i, j]) * (1 << levels[i, j]);
        return Math.Min(sum, 500_000);
    }

    private static QuadClass ClassifyQuad(Point p1, Point p2, Point p3, Point p4, DomainContext ctx)
    {
        double tol = Math.Max(ctx.NearSize * 0.05, 1e-6);
        bool i1 = PointInDomainInteriorOrOnBoundary(p1, ctx, tol);
        bool i2 = PointInDomainInteriorOrOnBoundary(p2, ctx, tol);
        bool i3 = PointInDomainInteriorOrOnBoundary(p3, ctx, tol);
        bool i4 = PointInDomainInteriorOrOnBoundary(p4, ctx, tol);
        int inside = (i1 ? 1 : 0) + (i2 ? 1 : 0) + (i3 ? 1 : 0) + (i4 ? 1 : 0);
        if (inside == 4) return QuadClass.Inside;
        if (inside == 0) return QuadClass.Outside;
        return QuadClass.Mixed;
    }

    private static bool IsAcceptedBoundaryQuad(Point p1, Point p2, Point p3, Point p4, DomainContext ctx, double nearSize)
    {
        double tol = Math.Max(nearSize * 0.12, 1e-5);
        var segs = ctx.OuterSegments;
        if (!CornerInOrOnForRegion(p1, ctx, segs, tol)) return false;
        if (!CornerInOrOnForRegion(p2, ctx, segs, tol)) return false;
        if (!CornerInOrOnForRegion(p3, ctx, segs, tol)) return false;
        if (!CornerInOrOnForRegion(p4, ctx, segs, tol)) return false;

        var cls = ClassifyQuad(p1, p2, p3, p4, ctx);
        if (cls == QuadClass.Outside) return false;

        var c = new Point((p1.X + p2.X + p3.X + p4.X) * 0.25, (p1.Y + p2.Y + p3.Y + p4.Y) * 0.25);
        if (PointInDomainInteriorOrOnBoundary(c, ctx, tol)) return true;

        // Accept wedge/corner quads at re-entrant boundaries (slope toe shelf) when corners are valid.
        if (cls == QuadClass.Mixed && QuadArea(p1, p2, p3, p4) >= tol * tol * 0.01)
            return CountBoundaryCorners(p1, p2, p3, p4, segs, tol) >= 1;

        return cls == QuadClass.Inside;
    }

    private static int CountBoundaryCorners(Point p1, Point p2, Point p3, Point p4, IReadOnlyList<(Point a, Point b)> segs, double tol)
    {
        int n = 0;
        if (CornerOnPolygonBoundary(p1, segs, tol)) n++;
        if (CornerOnPolygonBoundary(p2, segs, tol)) n++;
        if (CornerOnPolygonBoundary(p3, segs, tol)) n++;
        if (CornerOnPolygonBoundary(p4, segs, tol)) n++;
        return n;
    }

    private static bool CornerInOrOnForRegion(Point p, DomainContext ctx, IReadOnlyList<(Point a, Point b)> segs, double tol)
    {
        if (GradedSurfaceMeshGenerator.PointInPolygon(p, ctx.Polygon))
        {
            foreach (var cp in ctx.ChildPolys)
                if (GradedSurfaceMeshGenerator.PointInPolygon(p, cp)) return false;
            foreach (var hp in ctx.HolePolys)
                if (GradedSurfaceMeshGenerator.PointInPolygon(p, hp)) return false;
            return true;
        }
        return IsValidBoundaryPoint(p, ctx, tol);
    }

    private static bool TryBuildBoundarySnappedQuad(
        Point p1, Point p2, Point p3, Point p4,
        DomainContext ctx, double snapTol,
        out Point s1, out Point s2, out Point s3, out Point s4)
    {
        s1 = SnapCorner(p1, ctx, snapTol);
        s2 = SnapCorner(p2, ctx, snapTol);
        s3 = SnapCorner(p3, ctx, snapTol);
        s4 = SnapCorner(p4, ctx, snapTol);

        double tol = Math.Max(snapTol * 0.05, 1e-6);
        if (!CornerInDomain(s1, ctx, tol)) return false;
        if (!CornerInDomain(s2, ctx, tol)) return false;
        if (!CornerInDomain(s3, ctx, tol)) return false;
        if (!CornerInDomain(s4, ctx, tol)) return false;

        if (GradedSurfaceMeshGenerator.PointInPolygon(s1, ctx.Polygon) == false &&
            !IsValidBoundaryPoint(s1, ctx, tol)) return false;
        if (GradedSurfaceMeshGenerator.PointInPolygon(s2, ctx.Polygon) == false &&
            !IsValidBoundaryPoint(s2, ctx, tol)) return false;
        if (GradedSurfaceMeshGenerator.PointInPolygon(s3, ctx.Polygon) == false &&
            !IsValidBoundaryPoint(s3, ctx, tol)) return false;
        if (GradedSurfaceMeshGenerator.PointInPolygon(s4, ctx.Polygon) == false &&
            !IsValidBoundaryPoint(s4, ctx, tol)) return false;

        var cls = ClassifyQuad(s1, s2, s3, s4, ctx);
        if (cls == QuadClass.Outside) return false;

        var c = new Point((s1.X + s2.X + s3.X + s4.X) * 0.25, (s1.Y + s2.Y + s3.Y + s4.Y) * 0.25);
        if (!PointInDomainInteriorOrOnBoundary(c, ctx, tol)) return false;

        if (QuadArea(s1, s2, s3, s4) < tol * tol * 0.01) return false;
        return true;
    }

    private static Point SnapCorner(Point p, DomainContext ctx, double snapTol)
    {
        double tol = Math.Max(snapTol * 0.05, 1e-6);
        if (IsValidBoundaryPoint(p, ctx, tol))
            return p;

        var (dist, proj) = NearestExteriorBoundaryPoint(p, ctx);
        if (dist <= snapTol && IsValidBoundaryPoint(proj, ctx, tol))
            return proj;

        if (PointInDomainInteriorOrOnBoundary(p, ctx, tol))
            return p;

        return p;
    }

    private static bool IsValidBoundaryPoint(Point p, DomainContext ctx, double tol)
    {
        if (!CornerOnPolygonBoundary(p, ctx.OuterSegments, tol))
            return false;
        foreach (var cp in ctx.ChildPolys)
            if (GradedSurfaceMeshGenerator.PointInPolygon(p, cp)) return false;
        foreach (var hp in ctx.HolePolys)
            if (GradedSurfaceMeshGenerator.PointInPolygon(p, hp)) return false;
        return true;
    }

    private static (double dist, Point proj) NearestExteriorBoundaryPoint(Point p, DomainContext ctx)
    {
        double best = double.MaxValue;
        var bestPt = p;
        foreach (var (a, b) in ctx.OuterSegments)
        {
            var proj = ProjectToSegment(p, a, b);
            double d = Dist(p, proj);
            if (d < best) { best = d; bestPt = proj; }
        }
        return (best, bestPt);
    }

    private static Point ProjectToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double l2 = dx * dx + dy * dy;
        if (l2 < 1e-20) return a;
        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2;
        t = Math.Clamp(t, 0, 1);
        return new Point(a.X + t * dx, a.Y + t * dy);
    }

    private static bool PointOnBoundarySegment(Point p, IReadOnlyList<Point> polygon, double tol)
    {
        var segs = CollectSegments(new[] { polygon });
        return CornerOnPolygonBoundary(p, segs, tol);
    }

    private static bool CornerOnPolygonBoundary(Point p, IReadOnlyList<(Point a, Point b)> segs, double tol)
    {
        foreach (var (a, b) in segs)
        {
            var proj = ProjectToSegment(p, a, b);
            if (Dist(p, proj) <= tol) return true;
        }
        return false;
    }

    private static bool CornerInDomain(Point p, DomainContext ctx, double tol)
    {
        if (PointInDomainInteriorOrOnBoundary(p, ctx, tol)) return true;
        return false;
    }

    private static bool PointInDomainInteriorOrOnBoundary(Point p, DomainContext ctx, double tol)
    {
        if (GradedSurfaceMeshGenerator.PointInPolygon(p, ctx.Polygon)) return true;
        if (CornerOnPolygonBoundary(p, ctx.OuterSegments, tol))
        {
            foreach (var cp in ctx.ChildPolys)
                if (GradedSurfaceMeshGenerator.PointInPolygon(p, cp)) return false;
            foreach (var hp in ctx.HolePolys)
                if (GradedSurfaceMeshGenerator.PointInPolygon(p, hp)) return false;
            return true;
        }
        return false;
    }

    private static double QuadArea(Point p1, Point p2, Point p3, Point p4)
    {
        double area = 0;
        area += p1.X * p2.Y - p2.X * p1.Y;
        area += p2.X * p3.Y - p3.X * p2.Y;
        area += p3.X * p4.Y - p4.X * p3.Y;
        area += p4.X * p1.Y - p1.X * p4.Y;
        return Math.Abs(area) * 0.5;
    }

    private static void AddBoundaryStripQuads(
        DomainContext ctx,
        List<Point> pinned,
        NodePool nodeIndex,
        List<(int a, int b, int c, int d)> quads)
    {
        var poly = ctx.Polygon;
        int n = poly.Count;
        double depth = ctx.NearSize * 0.35;
        double tol = Math.Max(ctx.NearSize * 0.08, 1e-6);
        var existing = new HashSet<(int, int, int, int)>(quads);

        for (int i = 0; i < n; i++)
        {
            var va = poly[i];
            var vb = poly[(i + 1) % n];
            var chain = CollectNodesOnSegment(va, vb, pinned, tol);
            if (chain.Count < 2) continue;

            for (int k = 0; k < chain.Count - 1; k++)
            {
                var b0 = chain[k];
                var b1 = chain[k + 1];
                if (Dist(b0, b1) < tol) continue;

                var inward = InwardNormalUnit(poly, i);
                var i0 = new Point(b0.X + inward.X * depth, b0.Y + inward.Y * depth);
                var i1 = new Point(b1.X + inward.X * depth, b1.Y + inward.Y * depth);

                if (!PointInDomainInteriorOrOnBoundary(i0, ctx, tol)) continue;
                if (!PointInDomainInteriorOrOnBoundary(i1, ctx, tol)) continue;

                if (!TryBuildBoundarySnappedQuad(b0, b1, i1, i0, ctx, tol,
                        out var s1, out var s2, out var s3, out var s4))
                    continue;

                int n1 = nodeIndex.Get(s1);
                int n2 = nodeIndex.Get(s2);
                int n3 = nodeIndex.Get(s3);
                int n4 = nodeIndex.Get(s4);
                if (n1 == n2 || n2 == n3 || n3 == n4 || n4 == n1) continue;
                var key = NormalizeQuadKey(n1, n2, n3, n4);
                if (existing.Add(key))
                    quads.Add((n1, n2, n3, n4));
            }
        }
    }

    private static (int, int, int, int) NormalizeQuadKey(int a, int b, int c, int d)
    {
        var arr = new[] { a, b, c, d }.OrderBy(x => x).ToArray();
        return (arr[0], arr[1], arr[2], arr[3]);
    }

    private static List<Point> CollectNodesOnSegment(Point a, Point b, List<Point> pinned, double tol)
    {
        var hits = new List<(double t, Point p)>();
        hits.Add((0, a));
        hits.Add((1, b));
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 < 1e-20) return new List<Point> { a };

        foreach (var p in pinned)
        {
            var proj = ProjectToSegment(p, a, b);
            if (Dist(p, proj) > tol) continue;
            double t = ((proj.X - a.X) * dx + (proj.Y - a.Y) * dy) / len2;
            if (t < -tol || t > 1 + tol) continue;
            t = Math.Clamp(t, 0, 1);
            hits.Add((t, proj));
        }

        hits.Sort((x, y) => x.t.CompareTo(y.t));
        var chain = new List<Point>();
        foreach (var (_, p) in hits)
        {
            if (chain.Count == 0 || Dist(chain[^1], p) > tol)
                chain.Add(p);
        }
        return chain;
    }

    private static Vector InwardNormalUnit(IReadOnlyList<Point> poly, int edgeStart)
    {
        int n = poly.Count;
        var a = poly[edgeStart];
        var b = poly[(edgeStart + 1) % n];
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-12) return new Vector(0, -1);
        double area2 = SignedArea2(poly);
        // CCW outer polygon: inward is left of edge; CW: flip.
        if (area2 >= 0)
            return new Vector(-dy / len, dx / len);
        return new Vector(dy / len, -dx / len);
    }

    private static double SignedArea2(IReadOnlyList<Point> poly)
    {
        double sum = 0;
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % n];
            sum += p.X * q.Y - q.X * p.Y;
        }
        return sum;
    }

    private static bool PointInDomain(Point p, DomainContext ctx)
    {
        if (!GradedSurfaceMeshGenerator.PointInPolygon(p, ctx.Polygon)) return false;
        foreach (var cp in ctx.ChildPolys)
            if (GradedSurfaceMeshGenerator.PointInPolygon(p, cp)) return false;
        foreach (var hp in ctx.HolePolys)
            if (GradedSurfaceMeshGenerator.PointInPolygon(p, hp)) return false;
        return true;
    }

    private sealed class NodePool
    {
        private readonly double _eps;
        private readonly Dictionary<(long, long), int> _map = new();
        public List<Point> Nodes { get; } = new();

        public NodePool(double eps, IReadOnlyList<Point> pinned)
        {
            _eps = eps;
            foreach (var p in pinned)
                Get(p);
        }

        public int Get(Point p)
        {
            long qx = (long)Math.Round(p.X / _eps);
            long qy = (long)Math.Round(p.Y / _eps);
            var key = (qx, qy);
            if (_map.TryGetValue(key, out int idx))
                return idx;
            idx = Nodes.Count;
            Nodes.Add(p);
            _map[key] = idx;
            return idx;
        }
    }

    private static void AddBoundaryPoints(IReadOnlyList<Point> poly, double spacing, List<Point> output)
    {
        for (int i = 0; i < poly.Count; i++)
            AddSegmentPoints(poly[i], poly[(i + 1) % poly.Count], spacing, output);
    }

    private static void AddSegmentPoints(Point a, Point b, double spacing, List<Point> output)
    {
        double len = Dist(a, b);
        if (len < 1e-10) return;
        spacing = Math.Max(spacing, 1e-6);
        int segs = Math.Max(1, (int)Math.Ceiling(len / spacing));
        for (int j = 0; j < segs; j++)
        {
            double t = (double)j / segs;
            output.Add(new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
        }
    }

    private static List<(Point a, Point b)> CollectSegments(IEnumerable<IReadOnlyList<Point>> polygons)
    {
        var segs = new List<(Point, Point)>();
        foreach (var poly in polygons)
        {
            if (poly == null || poly.Count < 2) continue;
            for (int i = 0; i < poly.Count; i++)
                segs.Add((poly[i], poly[(i + 1) % poly.Count]));
        }
        return segs;
    }

    private static double SizeFieldAt(
        Point p, IReadOnlyList<(Point a, Point b)> segs,
        double nearSize, double globalSize, double growth)
    {
        double dMin = double.MaxValue;
        foreach (var (a, b) in segs)
        {
            double d = DistanceToSegment(p, a, b);
            if (d < dMin) dMin = d;
        }
        if (dMin == double.MaxValue) return globalSize;
        double k = dMin / Math.Max(nearSize, 1e-12);
        double h = nearSize * Math.Pow(growth, k);
        return Math.Clamp(h, nearSize, globalSize);
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double l2 = dx * dx + dy * dy;
        if (l2 < 1e-20) return Dist(p, a);
        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2;
        t = Math.Clamp(t, 0, 1);
        double cx = a.X + t * dx, cy = a.Y + t * dy;
        return Dist(p, new Point(cx, cy));
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

    private static void ComputeQuadQualityStats(
        IReadOnlyList<Point> nodes,
        IReadOnlyList<(int a, int b, int c, int d)> quads,
        out double minAngleDeg,
        out double maxAspectRatio)
    {
        minAngleDeg = 180;
        maxAspectRatio = 0;
        if (quads.Count == 0)
        {
            minAngleDeg = 0;
            return;
        }

        foreach (var q in quads)
        {
            var p1 = nodes[q.a]; var p2 = nodes[q.b]; var p3 = nodes[q.c]; var p4 = nodes[q.d];
            double a1 = InteriorAngle(p4, p1, p2);
            double a2 = InteriorAngle(p1, p2, p3);
            double a3 = InteriorAngle(p2, p3, p4);
            double a4 = InteriorAngle(p3, p4, p1);
            double minA = Math.Min(Math.Min(a1, a2), Math.Min(a3, a4));
            if (minA < minAngleDeg) minAngleDeg = minA;

            double e12 = Dist(p1, p2), e23 = Dist(p2, p3), e34 = Dist(p3, p4), e41 = Dist(p4, p1);
            double eMin = Math.Min(Math.Min(e12, e23), Math.Min(e34, e41));
            double eMax = Math.Max(Math.Max(e12, e23), Math.Max(e34, e41));
            if (eMin > 1e-12)
            {
                double ar = eMax / eMin;
                if (ar > maxAspectRatio) maxAspectRatio = ar;
            }
        }
    }

    private static double InteriorAngle(Point prev, Point pivot, Point next)
    {
        double v1x = prev.X - pivot.X, v1y = prev.Y - pivot.Y;
        double v2x = next.X - pivot.X, v2y = next.Y - pivot.Y;
        double dot = v1x * v2x + v1y * v2y;
        double cross = v1x * v2y - v1y * v2x;
        return Math.Atan2(Math.Abs(cross), dot) * 180.0 / Math.PI;
    }

    private static double Dist(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
