using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Result of graded surface mesh generation for one surface polygon.
/// All coordinates are in model-axis space.
/// </summary>
public sealed class SurfaceMeshResult
{
    public IReadOnlyList<Point> Nodes { get; init; } = Array.Empty<Point>();
    /// <summary>Each element is (n1, n2, n3) — 0-based indices into <see cref="Nodes"/>.</summary>
    public IReadOnlyList<(int a, int b, int c)> Triangles { get; init; } = Array.Empty<(int, int, int)>();
    public int NodeCount  => Nodes.Count;
    public int ElementCount => Triangles.Count;
    /// <summary>Approximate minimum angle across all triangles (degrees).</summary>
    public double MinAngleDeg { get; init; }
    /// <summary>Approximate maximum aspect ratio (longest edge / shortest altitude) across triangles.</summary>
    public double MaxAspectRatio { get; init; }
}

/// <summary>
/// Generates a graded Delaunay triangle mesh inside a closed polygon.
/// Mesh density is finer near the boundary and coarser toward the interior, with a continuous
/// distance-based size field, post-triangulation Laplacian smoothing and Ruppert-style quality
/// refinement (split low-angle triangles by inserting a Steiner point at the circumcenter) so
/// the output matches professional FEM codes (RS2, Plaxis, ANSYS) — no sliver triangles in the
/// transition zone between fine boundary and coarse interior.
/// </summary>
public static class GradedSurfaceMeshGenerator
{
    // ── Quality / smoothing tuning (shared across both entry points) ──────────
    private const double SmoothingMinAngleDeg = 25.0;   // Ruppert refinement target
    private const int    SmoothingPasses1     = 4;      // Laplacian iterations before refinement
    private const int    SmoothingPasses2     = 3;      // Laplacian iterations after refinement
    private const int    RefinementMaxIters   = 4;      // Ruppert split passes
    private const double LaplacianRelaxation  = 0.65;

    /// <summary>
    /// Generate a graded mesh inside <paramref name="polygon"/> (axis-space, CCW or CW).
    /// </summary>
    /// <param name="polygon">Closed polygon vertices (last point need not repeat first).</param>
    /// <param name="nearBoundarySize">Target edge length on and immediately adjacent to the boundary.</param>
    /// <param name="globalSize">Target edge length in the interior far from boundaries.</param>
    /// <param name="boundaryLayers">Minimum number of inward-offset rings; the generator keeps adding rings beyond this until the ring spacing reaches <paramref name="globalSize"/> so the gradation is continuous.</param>
    /// <param name="growthFactor">Size multiplier between successive rings (e.g. 1.3) and per unit-distance basis for the continuous size field.</param>
    /// <param name="maxNodes">Safety cap on total node count to avoid runaway generation.</param>
    /// <param name="holes">
    /// Optional closed polygons fully inside <paramref name="polygon"/> (e.g. trench openings in soil).
    /// Triangles whose centroid falls inside a hole are discarded; hole boundaries are sampled at the same
    /// near-boundary spacing so the soil mesh refines along soil–trench interfaces without overlapping
    /// a separate mesh generated inside the hole region.
    /// </param>
    public static SurfaceMeshResult Generate(
        IReadOnlyList<Point> polygon,
        double nearBoundarySize,
        double globalSize,
        int    boundaryLayers,
        double growthFactor,
        int    maxNodes = 20_000,
        IReadOnlyList<IReadOnlyList<Point>>? holes = null)
    {
        if (polygon == null || polygon.Count < 3)
            return new SurfaceMeshResult();

        nearBoundarySize = Math.Max(nearBoundarySize, 1e-6);
        globalSize       = Math.Max(globalSize, nearBoundarySize);
        boundaryLayers   = Math.Clamp(boundaryLayers, 0, 12);
        growthFactor     = Math.Clamp(growthFactor, 1.05, 3.0);

        // Boundary segments power the continuous size field h(x) and the Steiner-point validity test.
        var boundarySegments = CollectSegments(EnumerateBoundaryPolys(polygon, holes));

        bool IsInsideDomain(Point p) =>
            PointInPolygon(p, polygon) && !CentroidInAnyHole(p, holes);

        double SizeAt(Point p) =>
            SizeFieldAt(p, boundarySegments, nearBoundarySize, globalSize, growthFactor);

        // ── 1. Boundary + ring points (PINNED nodes — never moved by smoothing) ──
        var pinned = new List<Point>(512);
        AddBoundaryPoints(polygon, nearBoundarySize, pinned);
        if (holes != null)
            foreach (var hole in holes)
                if (hole != null && hole.Count >= 3)
                    AddBoundaryPoints(hole, nearBoundarySize, pinned);

        AddInwardRings(polygon, nearBoundarySize, globalSize, boundaryLayers, growthFactor, pinned, maxNodes);
        if (holes != null)
            foreach (var hole in holes)
                if (hole != null && hole.Count >= 3)
                    AddInwardRings(hole, nearBoundarySize, globalSize, boundaryLayers, growthFactor, pinned, maxNodes);

        double mergeEps = nearBoundarySize * 0.1;
        pinned = MergeDuplicates(pinned, mergeEps);
        int boundaryCount = pinned.Count;

        // ── 2. Interior points sampled with a continuous size-field ────────────
        var allPts = new List<Point>(pinned);
        AddSizeFieldInteriorPoints(IsInsideDomain, SizeAt, polygon,
            nearBoundarySize, globalSize, allPts, maxNodes);

        if (allPts.Count < 3)
            return new SurfaceMeshResult();

        // ── 3. Initial Delaunay triangulation ──────────────────────────────────
        var tris = BowyerWatsonTriangulator.Triangulate(allPts);

        // ── 4. Smooth → refine (split low-angle triangles) → smooth ──────────
        LaplacianSmooth(allPts, tris, boundaryCount, IsInsideDomain, SmoothingPasses1, LaplacianRelaxation);
        tris = RefineByMinAngle(allPts, tris, IsInsideDomain, SizeAt,
            boundaryCount, SmoothingMinAngleDeg, RefinementMaxIters, maxNodes);
        LaplacianSmooth(allPts, tris, boundaryCount, IsInsideDomain, SmoothingPasses2, LaplacianRelaxation);

        // ── 5. Filter triangles to the domain (centroid inside polygon, not in holes) ──
        var kept = tris
            .Where(t => IsInsideDomain(Centroid(allPts, t)))
            .ToList();

        ComputeQualityStats(allPts, kept, out double minAngle, out double maxAR);

        return new SurfaceMeshResult
        {
            Nodes          = allPts,
            Triangles      = kept,
            MinAngleDeg    = minAngle,
            MaxAspectRatio = maxAR,
        };
    }

    // ── Conforming multi-region mesh ──────────────────────────────────────────

    /// <summary>
    /// Generate a single conforming Delaunay mesh for multiple adjacent regions simultaneously.
    /// A single Bowyer-Watson triangulation is performed over the combined point cloud from all
    /// regions, guaranteeing that interface boundaries are shared — no overlap, no gaps, and
    /// smooth mesh transition between domains (e.g. rockmass + arch cavern).
    /// </summary>
    /// <param name="regions">
    /// Ordered list of (SurfaceNumber, Polygon, NearBoundarySize, GlobalSize, BoundaryLayers, GrowthFactor).
    /// Nesting (which polygon is inside which) is determined automatically via <see cref="SurfaceMeshHierarchy"/>.
    /// </param>
    /// <param name="holeOnlyRegions">
    /// Excavated (or other void) surfaces: their boundary is discretized and their interior is carved out of
    /// parent regions, but no mesh elements are returned for these surface numbers.
    /// </param>
    /// <param name="maxNodes">Safety cap on combined node count.</param>
    /// <returns>
    /// Per-region mesh results. All share the same <see cref="SurfaceMeshResult.Nodes"/> list (the global
    /// combined node set). Triangle indices are 0-based into that shared list.
    /// </returns>
    public static IReadOnlyList<(int SurfaceNumber, SurfaceMeshResult Mesh)> GenerateConforming(
        IReadOnlyList<(int SurfaceNumber, IReadOnlyList<Point> Polygon, double NearSize, double GlobalSize, int Layers, double Growth)> regions,
        IReadOnlyList<(int SurfaceNumber, IReadOnlyList<Point> Polygon, double NearSize)>? holeOnlyRegions = null,
        IReadOnlyList<(Point A, Point B)>? materialLayerSegments = null,
        IReadOnlyList<Point>? pinnedBoundaryNodes = null,
        int maxNodes = 40_000,
        IReadOnlyList<(int SurfaceNumber, IReadOnlyList<Point> BoundaryNodes)>? fixedBoundaryHoles = null,
        IReadOnlyList<(Point A, Point B)>? extraConstraintSegments = null)
    {
        if (regions == null || regions.Count == 0)
            return Array.Empty<(int, SurfaceMeshResult)>();

        // Clamp / sanitise per-region parameters
        var safe = regions.Select(r => (
            r.SurfaceNumber,
            r.Polygon,
            NearSize:   Math.Max(r.NearSize,   1e-6),
            GlobalSize: Math.Max(r.GlobalSize,  Math.Max(r.NearSize, 1e-6)),
            Layers:     Math.Clamp(r.Layers, 0, 12),
            Growth:     Math.Clamp(r.Growth,  1.05, 3.0)
        )).ToList();

        var holeSafe = (holeOnlyRegions ?? Array.Empty<(int, IReadOnlyList<Point>, double)>())
            .Where(h => h.Polygon != null && h.Polygon.Count >= 3)
            .Select(h => (h.SurfaceNumber, h.Polygon, NearSize: Math.Max(h.NearSize, 1e-6)))
            .ToList();

        // Fixed-boundary holes (e.g. structured/transfinite blocks): their boundary is given by an
        // explicit node loop honoured exactly (no re-sampling) so the surrounding CDT conforms
        // node-for-node to the block perimeter. Treated as holes for all exclusion / carving logic.
        var fixedHoles = (fixedBoundaryHoles ?? Array.Empty<(int, IReadOnlyList<Point>)>())
            .Where(h => h.BoundaryNodes != null && h.BoundaryNodes.Count >= 3)
            .Select(h => (h.SurfaceNumber, Polygon: (IReadOnlyList<Point>)h.BoundaryNodes))
            .ToList();
        var allHoles = holeSafe
            .Select(h => (h.SurfaceNumber, h.Polygon))
            .Concat(fixedHoles)
            .ToList();

        // Build parent map so inner regions are treated as holes in their parent
        var polyList = safe.Select(r => (r.SurfaceNumber, r.Polygon)).ToList();
        foreach (var h in allHoles)
            polyList.Add((h.SurfaceNumber, h.Polygon));
        var parentMap = SurfaceMeshHierarchy.BuildParentMap(polyList);

        List<IReadOnlyList<Point>> ChildPolysOf(int parentSn)
        {
            var list = new List<IReadOnlyList<Point>>();
            foreach (var r in safe)
            {
                if (parentMap.TryGetValue(r.SurfaceNumber, out var p) && p == parentSn)
                    list.Add(r.Polygon);
            }

            foreach (var h in allHoles)
            {
                if (parentMap.TryGetValue(h.SurfaceNumber, out var ph) && ph == parentSn)
                    list.Add(h.Polygon);
            }

            return list;
        }

        // Domain predicate: inside ANY meshed region polygon, NOT inside any excavation hole. Used by
        // the size-field interior sampler, the Laplacian post-smoothing (so smoothed nodes stay inside
        // the real geometry) and Ruppert refinement (so Steiner circumcenters land in valid space).
        bool IsInsideDomain(Point p)
        {
            foreach (var (_, poly, _, _, _, _) in safe)
                if (PointInPolygon(p, poly))
                {
                    foreach (var (_, holePoly) in allHoles)
                        if (PointInPolygon(p, holePoly)) return false;
                    return true;
                }
            return false;
        }

        // Build a combined boundary-segment list once, used for the size field. Includes EVERY region
        // outer polygon and EVERY excavation polygon, so the field is fine on every interface.
        var allBoundaryPolys = safe.Select(r => r.Polygon)
            .Concat(allHoles.Select(h => h.Polygon))
            .ToList();
        var boundarySegments = CollectSegments(allBoundaryPolys);
        if (materialLayerSegments is { Count: > 0 })
            boundarySegments.AddRange(materialLayerSegments);
        if (extraConstraintSegments is { Count: > 0 })
            boundarySegments.AddRange(extraConstraintSegments);

        // Region-aware size field: pick the (nearSize, globalSize, growth) of whichever safe region the
        // candidate point sits in (default to the finest if not strictly inside any). This keeps interior
        // density tied to the per-surface settings the user picked.
        double SizeAt(Point p)
        {
            (double n, double g, double w) chosen = (safe.Min(r => r.NearSize),
                                                     safe.Min(r => r.GlobalSize),
                                                     safe.Max(r => r.Growth));
            foreach (var r in safe)
            {
                if (PointInPolygon(p, r.Polygon))
                {
                    chosen = (r.NearSize, r.GlobalSize, r.Growth);
                    break;
                }
            }
            return SizeFieldAt(p, boundarySegments, chosen.n, chosen.g, chosen.w);
        }

        // ── 1. Pinned (boundary + ring) point cloud across all regions ───────
        var pinned = new List<Point>(1024);
        var boundaryPolys = safe.Select(r => r.Polygon)
            .Concat(allHoles.Select(h => h.Polygon))
            .Where(p => p != null && p.Count >= 3)
            .ToList();

        IReadOnlyList<double>? layerCutYs = null;
        if (materialLayerSegments is { Count: > 0 })
            layerCutYs = CollectLayerCutElevations(materialLayerSegments);

        foreach (var (_, poly, nearSz, globalSz, layers, growth) in safe)
        {
            AddBoundaryPointsWithLayerCuts(poly, nearSz, layerCutYs, pinned);
            AddInwardRings(poly, nearSz, globalSz, layers, growth, pinned, maxNodes, layerCutYs);
        }
        foreach (var (_, poly, nearSz) in holeSafe)
            AddBoundaryPointsWithLayerCuts(poly, nearSz, layerCutYs, pinned);

        // Fixed-boundary holes: pin their exact perimeter nodes (no re-sampling) so the surrounding
        // triangulation uses these exact nodes on the shared interface (node-for-node conforming).
        foreach (var (_, poly) in fixedHoles)
            foreach (var fp in poly)
                pinned.Add(fp);

        double layerNear = safe.Min(r => r.NearSize);
        if (holeSafe.Count > 0)
            layerNear = Math.Min(layerNear, holeSafe.Min(h => h.NearSize));

        List<Point> layerJunctions = new();
        if (materialLayerSegments is { Count: > 0 })
        {
            layerJunctions = CollectLayerBoundaryJunctions(materialLayerSegments, boundaryPolys);
            foreach (var j in layerJunctions)
                pinned.Add(j);

            double endpointSnap = layerNear * 0.35;
            foreach (var (a, b) in materialLayerSegments)
            {
                var aa = SnapEndpointToJunction(a, layerJunctions, endpointSnap);
                var bb = SnapEndpointToJunction(b, layerJunctions, endpointSnap);
                AddSegmentPoints(aa, bb, layerNear, pinned);
            }
        }

        if (extraConstraintSegments is { Count: > 0 })
        {
            foreach (var (a, b) in extraConstraintSegments)
                AddSegmentPoints(a, b, layerNear, pinned);
        }

        // RS2D Discretize preview nodes (outer boundary + material layer chords) — pinned so Mesh
        // honors the same constraint spacing the user saw before clicking Mesh.
        if (pinnedBoundaryNodes is { Count: > 0 })
        {
            foreach (var p in pinnedBoundaryNodes)
                pinned.Add(p);
        }

        double mergeEps = safe.Min(r => r.NearSize) * (layerJunctions.Count > 0 ? 0.15 : 0.1);
        if (holeSafe.Count > 0)
            mergeEps = Math.Min(mergeEps, holeSafe.Min(h => h.NearSize) * (layerJunctions.Count > 0 ? 0.15 : 0.1));
        if (layerJunctions.Count > 0)
            SnapPinnedPointsToLayerJunctions(pinned, layerJunctions, mergeEps * 2.5);
        pinned = MergeDuplicates(pinned, mergeEps);
        int boundaryCount = pinned.Count;

        // ── 2. Interior points (per region, size-field-aware Poisson rejection) ──
        var allPts = new List<Point>(pinned);
        foreach (var (sn, poly, nearSz, globalSz, layers, growth) in safe)
        {
            var childPolys = ChildPolysOf(sn);
            // Region-local domain predicate excludes children so the interior sampler doesn't seed
            // points inside a nested cavern (the cavern is meshed by its own region).
            bool IsInsideRegion(Point p)
            {
                if (!PointInPolygon(p, poly)) return false;
                foreach (var cp in childPolys)
                    if (PointInPolygon(p, cp)) return false;
                return true;
            }

            AddSizeFieldInteriorPoints(IsInsideRegion, SizeAt, poly,
                nearSz, globalSz, allPts, maxNodes);
        }

        if (allPts.Count < 3)
            return Array.Empty<(int, SurfaceMeshResult)>();

        // ── 3. Initial Delaunay triangulation on the full cloud ──────────────
        var allTris = BowyerWatsonTriangulator.Triangulate(allPts);

        // ── 3b. Enforce horizontal material layer cuts as mesh edges ─────────
        if (layerCutYs is { Count: > 0 })
        {
            allTris = EnforceHorizontalLayerCuts(allPts, allTris, layerCutYs, mergeEps);
        }
        if (extraConstraintSegments is { Count: > 0 })
            allTris = EnforceSegmentCuts(allPts, allTris, extraConstraintSegments, mergeEps);

        // ── 4. Smooth → refine (Ruppert) → smooth: removes the sliver triangles
        // that previously formed at the boundary ↔ interior gradation transition.
        LaplacianSmooth(allPts, allTris, boundaryCount, IsInsideDomain, SmoothingPasses1, LaplacianRelaxation);
        allTris = RefineByMinAngle(allPts, allTris, IsInsideDomain, SizeAt,
            boundaryCount, SmoothingMinAngleDeg, RefinementMaxIters, maxNodes, layerCutYs);
        if (layerCutYs is { Count: > 0 })
            allTris = EnforceHorizontalLayerCuts(allPts, allTris, layerCutYs, mergeEps);
        if (extraConstraintSegments is { Count: > 0 })
            allTris = EnforceSegmentCuts(allPts, allTris, extraConstraintSegments, mergeEps);
        LaplacianSmooth(allPts, allTris, boundaryCount, IsInsideDomain, SmoothingPasses2, LaplacianRelaxation);

        // ── 5. Assign triangles to each region by centroid ───────────────────
        var results = new List<(int, SurfaceMeshResult)>(safe.Count);

        foreach (var (sn, poly, _, _, _, _) in safe)
        {
            var childPolys = ChildPolysOf(sn);

            var regionTris = allTris
                .Where(t =>
                {
                    var centroid = Centroid(allPts, t);
                    if (!PointInPolygon(centroid, poly)) return false;
                    foreach (var cp in childPolys)
                        if (PointInPolygon(centroid, cp)) return false;
                    return true;
                })
                .ToList();

            ComputeQualityStats(allPts, regionTris, out double minAngle, out double maxAR);

            results.Add((sn, new SurfaceMeshResult
            {
                Nodes          = allPts,       // shared global node list — this is what makes the mesh conforming
                Triangles      = regionTris,
                MinAngleDeg    = minAngle,
                MaxAspectRatio = maxAR,
            }));
        }

        return results;
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>Sample polygon edges uniformly at <paramref name="spacing"/>.</summary>
    private static void AddBoundaryPoints(IReadOnlyList<Point> poly, double spacing, List<Point> output)
        => AddBoundaryPointsWithLayerCuts(poly, spacing, null, output);

    /// <summary>
    /// Sample polygon edges at <paramref name="spacing"/>, splitting each edge at material layer cut
    /// elevations so vertical boundaries receive an exact node at every horizontal interface (T-junction).
    /// </summary>
    private static void AddBoundaryPointsWithLayerCuts(
        IReadOnlyList<Point> poly,
        double spacing,
        IReadOnlyList<double>? layerCutYs,
        List<Point> output)
    {
        if (poly == null || poly.Count < 2) return;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            var tParams = new List<double> { 0.0, 1.0 };
            if (layerCutYs != null)
            {
                foreach (var cutY in layerCutYs)
                {
                    if (TryEdgeParameterAtHorizontalCut(a, b, cutY, out double t))
                        tParams.Add(t);
                }
            }

            tParams.Sort();
            var merged = new List<double> { tParams[0] };
            for (int k = 1; k < tParams.Count; k++)
            {
                if (tParams[k] - merged[^1] > 1e-10)
                    merged.Add(tParams[k]);
            }

            for (int k = 0; k < merged.Count - 1; k++)
            {
                double t0 = merged[k], t1 = merged[k + 1];
                if (t1 - t0 < 1e-12) continue;
                AddSegmentPoints(Lerp(a, b, t0), Lerp(a, b, t1), spacing, output);
            }
        }
    }

    private static Point Lerp(Point a, Point b, double t) =>
        new(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));

    private static bool TryEdgeParameterAtHorizontalCut(Point a, Point b, double cutY, out double t)
    {
        t = 0;
        double dy = b.Y - a.Y;
        if (Math.Abs(dy) < 1e-12)
            return false;
        t = (cutY - a.Y) / dy;
        return t > 1e-9 && t < 1.0 - 1e-9;
    }

    private static List<double> CollectLayerCutElevations(IReadOnlyList<(Point A, Point B)> layerSegments)
    {
        var ys = new List<double>();
        foreach (var (a, b) in layerSegments)
        {
            ys.Add(a.Y);
            ys.Add(b.Y);
        }

        ys.Sort();
        var merged = new List<double>();
        const double eps = 1e-9;
        foreach (var y in ys)
        {
            if (merged.Count == 0 || Math.Abs(y - merged[^1]) > eps)
                merged.Add(y);
        }

        return merged;
    }

    /// <summary>
    /// Exact coordinates where horizontal material layer cuts meet polygon boundaries (corner T-junctions).
    /// </summary>
    private static List<Point> CollectLayerBoundaryJunctions(
        IReadOnlyList<(Point A, Point B)> layerSegments,
        IReadOnlyList<IReadOnlyList<Point>> boundaryPolys)
    {
        var junctions = new List<Point>();
        var cutYs = CollectLayerCutElevations(layerSegments);
        const double yTol = 1e-9;

        foreach (var cutY in cutYs)
        {
            foreach (var poly in boundaryPolys)
            {
                if (poly == null || poly.Count < 2) continue;
                foreach (var v in poly)
                {
                    if (Math.Abs(v.Y - cutY) <= yTol)
                        junctions.Add(new Point(v.X, cutY));
                }

                for (int i = 0; i < poly.Count; i++)
                {
                    var p0 = poly[i];
                    var p1 = poly[(i + 1) % poly.Count];
                    if (TryEdgeParameterAtHorizontalCut(p0, p1, cutY, out double t))
                        junctions.Add(Lerp(p0, p1, t));
                }
            }

            foreach (var (a, b) in layerSegments)
            {
                if (Math.Abs(a.Y - cutY) <= yTol)
                    junctions.Add(a);
                if (Math.Abs(b.Y - cutY) <= yTol)
                    junctions.Add(b);
            }
        }

        return MergeDuplicates(junctions, 1e-9);
    }

    private static Point SnapEndpointToJunction(Point p, IReadOnlyList<Point> junctions, double snapTol)
    {
        if (junctions.Count == 0)
            return p;

        double snap2 = snapTol * snapTol;
        var best = p;
        double bestD2 = snap2;
        foreach (var j in junctions)
        {
            double dx = p.X - j.X, dy = p.Y - j.Y;
            double d2 = dx * dx + dy * dy;
            if (d2 <= bestD2)
            {
                bestD2 = d2;
                best = j;
            }
        }

        return best;
    }

    /// <summary>
    /// Collapse near-duplicate pinned nodes onto canonical layer/boundary junction coordinates.
    /// </summary>
    private static void SnapPinnedPointsToLayerJunctions(
        List<Point> pinned,
        IReadOnlyList<Point> junctions,
        double snapTol)
    {
        if (junctions.Count == 0) return;
        double snap2 = snapTol * snapTol;
        for (int i = 0; i < pinned.Count; i++)
        {
            var p = pinned[i];
            Point? best = null;
            double bestD2 = snap2;
            foreach (var j in junctions)
            {
                double dx = p.X - j.X, dy = p.Y - j.Y;
                if (Math.Abs(dx) > snapTol && Math.Abs(dy) > snapTol)
                    continue;
                double d2 = dx * dx + dy * dy;
                if (d2 <= bestD2)
                {
                    bestD2 = d2;
                    best = j;
                }
            }

            if (best.HasValue)
                pinned[i] = best.Value;
        }
    }

    /// <summary>Sample a single segment uniformly at <paramref name="spacing"/>.</summary>
    private static void AddSegmentPoints(Point a, Point b, double spacing, List<Point> output)
    {
        double len = Distance(a, b);
        if (len < 1e-10) return;
        spacing = Math.Max(spacing, 1e-6);
        int segs = Math.Max(1, (int)Math.Ceiling(len / spacing));
        for (int j = 0; j < segs; j++)
        {
            double t = (double)j / segs;
            output.Add(new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
        }
    }

    /// <summary>
    /// Add inward-offset rings until ring spacing reaches <paramref name="globalSize"/>. The user-supplied
    /// <paramref name="minLayers"/> is treated as a minimum count; the loop keeps going beyond it so the
    /// transition between fine boundary spacing and coarse interior is continuous (no abrupt jump that
    /// produces sliver triangles).
    /// </summary>
    private static void AddInwardRings(
        IReadOnlyList<Point> poly,
        double nearSize,
        double globalSize,
        int    minLayers,
        double growth,
        List<Point> output,
        int    maxNodes,
        IReadOnlyList<double>? layerCutYs = null)
    {
        if (growth <= 1.0001) return;
        double ringSize = nearSize;
        double offset   = 0;
        int    layer    = 0;
        const int MaxLayersHardCap = 60; // safety: at growth=1.3 reaches ~10000× nearSize in 60 layers

        while (output.Count < maxNodes && layer < MaxLayersHardCap)
        {
            offset   += ringSize;
            ringSize *= growth;
            var ring  = ComputeInwardOffsetPolygon(poly, offset);
            if (ring == null || ring.Count < 3) break;
            AddBoundaryPointsWithLayerCuts(ring, ringSize, layerCutYs, output);
            layer++;

            // Stop once we've placed at least the user-requested minimum AND the next ring would
            // already be at the global interior size — interior sampler takes over from here.
            if (layer >= minLayers && ringSize >= globalSize) break;
        }
    }

    /// <summary>
    /// Approximate inward Minkowski offset of <paramref name="polygon"/> by <paramref name="dist"/>.
    /// Uses vertex-normal averaging (fast but not exact for non-convex polygons).
    /// </summary>
    private static List<Point>? ComputeInwardOffsetPolygon(IReadOnlyList<Point> poly, double dist)
    {
        int n   = poly.Count;
        var res = new List<Point>(n);

        for (int i = 0; i < n; i++)
        {
            var prev = poly[(i + n - 1) % n];
            var cur  = poly[i];
            var next = poly[(i + 1) % n];

            // Inward normal of edge prev→cur
            var d1 = Normalize(new Vector(cur.X - prev.X, cur.Y - prev.Y));
            var n1 = new Vector(d1.Y, -d1.X);   // right-perpendicular (inward for CCW)

            // Inward normal of edge cur→next
            var d2 = Normalize(new Vector(next.X - cur.X, next.Y - cur.Y));
            var n2 = new Vector(d2.Y, -d2.X);

            var bisector = n1 + n2;
            double bLen  = bisector.Length;
            if (bLen < 1e-8)
            {
                res.Add(new Point(cur.X + n1.X * dist, cur.Y + n1.Y * dist));
            }
            else
            {
                bisector /= bLen;
                // Scale by 1/sin(θ/2) so offset is consistent along both edges
                double cosHalf = (n1.X * n2.X + n1.Y * n2.Y);
                double sinHalf = Math.Sqrt(Math.Max(0, (1 - cosHalf) / 2));
                double scale   = sinHalf < 0.15 ? dist : dist / sinHalf;
                scale = Math.Min(scale, dist * 3); // cap for very sharp angles
                res.Add(new Point(cur.X + bisector.X * scale, cur.Y + bisector.Y * scale));
            }
        }

        // Validate: all offset points must be inside the original polygon
        if (res.Any(p => !PointInPolygon(p, poly)))
            return null; // offset collapsed — stop adding layers

        return res;
    }

    /// <summary>
    /// Sample interior points using a continuous size field
    /// h(x) = clamp(nearSize · growth^(d/nearSize), nearSize, globalSize)
    /// where <c>d</c> is the distance from <c>x</c> to the nearest boundary segment. Each candidate
    /// passes only if its distance to all already-placed nodes is at least <c>0.85 · h(x)</c>
    /// (Bridson-style Poisson rejection, accelerated by a uniform spatial hash). Removes the abrupt
    /// fine→coarse jump that the previous uniform-grid filler produced.
    /// </summary>
    private static void AddSizeFieldInteriorPoints(
        Func<Point, bool> isInsideRegion,
        Func<Point, double> sizeAt,
        IReadOnlyList<Point> polygon,
        double nearSize,
        double globalSize,
        List<Point> allPts,
        int maxNodes)
    {
        double minX = polygon.Min(p => p.X), maxX = polygon.Max(p => p.X);
        double minY = polygon.Min(p => p.Y), maxY = polygon.Max(p => p.Y);

        // Hash bucket = globalSize so a 3×3 neighbourhood around any candidate covers every
        // possible rejection radius (max rejection radius = 0.85·globalSize).
        double bucket = Math.Max(globalSize, nearSize * 2);
        var hash = new Dictionary<(int gx, int gy), List<int>>();
        (int, int) Cell(Point p) => ((int)Math.Floor(p.X / bucket), (int)Math.Floor(p.Y / bucket));

        for (int i = 0; i < allPts.Count; i++)
        {
            var key = Cell(allPts[i]);
            if (!hash.TryGetValue(key, out var bucketList))
            {
                bucketList = new List<int>(4);
                hash[key] = bucketList;
            }
            bucketList.Add(i);
        }

        bool TooClose(Point p, double minDist)
        {
            double minDist2 = minDist * minDist;
            var (gx, gy) = Cell(p);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!hash.TryGetValue((gx + dx, gy + dy), out var bucketList)) continue;
                foreach (var idx in bucketList)
                {
                    double ex = allPts[idx].X - p.X, ey = allPts[idx].Y - p.Y;
                    if (ex * ex + ey * ey < minDist2) return true;
                }
            }
            return false;
        }

        // Walk a fine sampling grid (cell = nearSize) so the candidate density is enough to fill
        // even high-density regions; the Poisson check thins it out where the size field grows.
        double step = Math.Max(nearSize * 0.6, 1e-6);
        double offX = step * 0.5, offY = step * 0.5;
        for (double y = minY + offY; y < maxY && allPts.Count < maxNodes; y += step)
        for (double x = minX + offX; x < maxX && allPts.Count < maxNodes; x += step)
        {
            var p = new Point(x, y);
            if (!isInsideRegion(p)) continue;
            double h = sizeAt(p);
            double minDist = h * 0.85;
            if (TooClose(p, minDist)) continue;

            allPts.Add(p);
            var key = Cell(p);
            if (!hash.TryGetValue(key, out var bucketList))
            {
                bucketList = new List<int>(4);
                hash[key] = bucketList;
            }
            bucketList.Add(allPts.Count - 1);
        }
    }

    /// <summary>
    /// Laplacian (umbrella) smoothing: each interior node moves toward the average position of its
    /// triangle-edge neighbours, scaled by <paramref name="relaxation"/>. Boundary / pinned nodes
    /// (indices 0..<paramref name="boundaryCount"/>) never move. Movements that would push a node
    /// outside the domain are rejected so concave boundaries don't get crossed.
    /// </summary>
    private static void LaplacianSmooth(
        List<Point> nodes,
        IReadOnlyList<(int a, int b, int c)> tris,
        int boundaryCount,
        Func<Point, bool> isInsideDomain,
        int iterations,
        double relaxation)
    {
        if (iterations <= 0 || nodes.Count == 0 || tris.Count == 0) return;

        var adj = new HashSet<int>[nodes.Count];
        for (int i = 0; i < nodes.Count; i++) adj[i] = new HashSet<int>();
        foreach (var (a, b, c) in tris)
        {
            if (a >= nodes.Count || b >= nodes.Count || c >= nodes.Count) continue;
            adj[a].Add(b); adj[a].Add(c);
            adj[b].Add(a); adj[b].Add(c);
            adj[c].Add(a); adj[c].Add(b);
        }

        for (int it = 0; it < iterations; it++)
        {
            for (int i = boundaryCount; i < nodes.Count; i++)
            {
                if (adj[i].Count == 0) continue;
                double sx = 0, sy = 0;
                foreach (var j in adj[i]) { sx += nodes[j].X; sy += nodes[j].Y; }
                double cx = sx / adj[i].Count, cy = sy / adj[i].Count;
                var moved = new Point(
                    nodes[i].X + (cx - nodes[i].X) * relaxation,
                    nodes[i].Y + (cy - nodes[i].Y) * relaxation);
                if (isInsideDomain(moved))
                    nodes[i] = moved;
            }
        }
    }

    /// <summary>
    /// Ruppert-style quality refinement: triangles whose minimum angle is below
    /// <paramref name="minAngleDeg"/> get their circumcenter inserted as a Steiner point if it lands
    /// inside the domain and is at least <c>0.45·sizeAt(circumcenter)</c> from any existing node.
    /// Runs at most <paramref name="maxIter"/> passes; bails out as soon as no new points are added or
    /// the global node cap is reached. Returns the refined triangulation (re-run of Bowyer-Watson on
    /// the augmented point cloud).
    /// </summary>
    private static List<(int a, int b, int c)> RefineByMinAngle(
        List<Point> nodes,
        IReadOnlyList<(int a, int b, int c)> tris,
        Func<Point, bool> isInsideDomain,
        Func<Point, double> sizeAt,
        int boundaryCount,
        double minAngleDeg,
        int maxIter,
        int maxNodes,
        IReadOnlyList<double>? layerCutYs = null)
    {
        const double cutTol = 1e-6;
        var current = new List<(int, int, int)>(tris);
        for (int iter = 0; iter < maxIter; iter++)
        {
            if (nodes.Count >= maxNodes) break;

            // Spatial hash of existing nodes for O(1) proximity check during this pass.
            double bucket = sizeAt(nodes[boundaryCount > 0 ? 0 : 0]); // any reasonable size
            // Use the median-ish size by sampling a few points so the bucket isn't too tiny.
            bucket = Math.Max(bucket, sizeAt(new Point(
                nodes.Average(p => p.X), nodes.Average(p => p.Y))));
            bucket = Math.Max(bucket, 1e-6);

            var hash = new Dictionary<(int, int), List<int>>();
            (int, int) Cell(Point p) => ((int)Math.Floor(p.X / bucket), (int)Math.Floor(p.Y / bucket));
            for (int i = 0; i < nodes.Count; i++)
            {
                var key = Cell(nodes[i]);
                if (!hash.TryGetValue(key, out var bl)) { bl = new List<int>(4); hash[key] = bl; }
                bl.Add(i);
            }

            bool TooClose(Point p, double minDist)
            {
                double m2 = minDist * minDist;
                var (gx, gy) = Cell(p);
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!hash.TryGetValue((gx + dx, gy + dy), out var bl)) continue;
                    foreach (var idx in bl)
                    {
                        double ex = nodes[idx].X - p.X, ey = nodes[idx].Y - p.Y;
                        if (ex * ex + ey * ey < m2) return true;
                    }
                }
                return false;
            }

            int addedThisPass = 0;
            foreach (var (ai, bi, ci) in current)
            {
                if (nodes.Count >= maxNodes) break;
                var pa = nodes[ai]; var pb = nodes[bi]; var pc = nodes[ci];
                double minAngle = MinAngleDeg3(pa, pb, pc);
                if (minAngle >= minAngleDeg) continue;

                if (!TryComputeCircumcenter(pa, pb, pc, out var cc)) continue;
                if (!isInsideDomain(cc)) continue;

                double h = sizeAt(cc);
                if (layerCutYs is { Count: > 0 })
                {
                    cc = SnapCircumcenterToLayerCut(cc, layerCutYs, h, cutTol);
                    if (!isInsideDomain(cc)) continue;
                    if (WouldTriangleStraddleAnyCut(pa, pb, cc, layerCutYs, cutTol) ||
                        WouldTriangleStraddleAnyCut(pb, pc, cc, layerCutYs, cutTol) ||
                        WouldTriangleStraddleAnyCut(pc, pa, cc, layerCutYs, cutTol))
                        continue;
                }

                if (TooClose(cc, h * 0.45)) continue;

                nodes.Add(cc);
                var key = Cell(cc);
                if (!hash.TryGetValue(key, out var bucketList)) { bucketList = new List<int>(4); hash[key] = bucketList; }
                bucketList.Add(nodes.Count - 1);
                addedThisPass++;
            }

            if (addedThisPass == 0) break;
            current = BowyerWatsonTriangulator.Triangulate(nodes);
        }

        return current;
    }

    /// <summary>
    /// Split triangles that straddle horizontal material layer cuts by inserting intersection nodes
    /// and retriangulating until no triangle spans a cut (or iteration cap reached).
    /// </summary>
    private static List<(int a, int b, int c)> EnforceHorizontalLayerCuts(
        List<Point> nodes,
        IReadOnlyList<(int a, int b, int c)> tris,
        IReadOnlyList<double> cutYs,
        double mergeEps,
        int maxIter = 20)
    {
        const double tol = 1e-6;
        var current = tris.ToList();

        for (int iter = 0; iter < maxIter; iter++)
        {
            bool anySplit = false;
            foreach (var cutY in cutYs)
            {
                for (int ti = current.Count - 1; ti >= 0; ti--)
                {
                    var (ai, bi, ci) = current[ti];
                    var pa = nodes[ai]; var pb = nodes[bi]; var pc = nodes[ci];
                    if (!TriangleStraddlesHorizontalCut(pa, pb, pc, cutY, tol))
                        continue;

                    if (TrySplitTriangleAtHorizontalCut(nodes, pa, pb, pc, ai, bi, ci, cutY, tol, mergeEps,
                            out var replacements))
                    {
                        current.RemoveAt(ti);
                        current.AddRange(replacements);
                        anySplit = true;
                    }
                }
            }

            if (!anySplit)
                break;
        }

        return current;
    }

    /// <summary>
    /// Split triangles crossed by arbitrary constraint segments so each segment becomes mesh edges.
    /// </summary>
    private static List<(int a, int b, int c)> EnforceSegmentCuts(
        List<Point> nodes,
        IReadOnlyList<(int a, int b, int c)> tris,
        IReadOnlyList<(Point A, Point B)> segments,
        double mergeEps,
        int maxIter = 40)
    {
        const double tol = 1e-6;
        var current = tris.ToList();

        for (int iter = 0; iter < maxIter; iter++)
        {
            bool anySplit = false;
            foreach (var (sa, sb) in segments)
            {
                if (Distance(sa, sb) < tol)
                    continue;
                for (int ti = current.Count - 1; ti >= 0; ti--)
                {
                    var (ai, bi, ci) = current[ti];
                    var pa = nodes[ai];
                    var pb = nodes[bi];
                    var pc = nodes[ci];
                    if (!TrySplitTriangleAtSegment(nodes, pa, pb, pc, ai, bi, ci, sa, sb, tol, mergeEps,
                            out var replacements))
                        continue;
                    current.RemoveAt(ti);
                    current.AddRange(replacements);
                    anySplit = true;
                }
            }

            if (!anySplit)
                break;
        }

        return current;
    }

    private static bool TrySplitTriangleAtSegment(
        List<Point> nodes,
        Point pa, Point pb, Point pc,
        int ai, int bi, int ci,
        Point sa, Point sb,
        double tol, double mergeEps,
        out List<(int a, int b, int c)> replacements)
    {
        replacements = new List<(int, int, int)>();
        bool onA = PointOnSegment(pa, sa, sb, tol);
        bool onB = PointOnSegment(pb, sa, sb, tol);
        bool onC = PointOnSegment(pc, sa, sb, tol);
        if (onA && onB || onB && onC || onC && onA)
            return false;

        bool hitAB = TryProperSegmentIntersection(pa, pb, sa, sb, tol, out var pAB);
        bool hitBC = TryProperSegmentIntersection(pb, pc, sa, sb, tol, out var pBC);
        bool hitCA = TryProperSegmentIntersection(pc, pa, sa, sb, tol, out var pCA);
        int hits = (hitAB ? 1 : 0) + (hitBC ? 1 : 0) + (hitCA ? 1 : 0);
        int onCount = (onA ? 1 : 0) + (onB ? 1 : 0) + (onC ? 1 : 0);

        if (onCount == 1 && hits == 1)
        {
            int onIdx = onA ? ai : onB ? bi : ci;
            Point hit;
            int oppA, oppB;
            if (hitAB) { hit = pAB; oppA = ai; oppB = bi; }
            else if (hitBC) { hit = pBC; oppA = bi; oppB = ci; }
            else { hit = pCA; oppA = ci; oppB = ai; }
            if (onIdx == oppA || onIdx == oppB)
                return false;
            int pi = FindOrAddNode(nodes, hit, mergeEps);
            replacements.Add((onIdx, oppA, pi));
            replacements.Add((onIdx, pi, oppB));
            return replacements[0] != replacements[1] && pi != onIdx;
        }

        if (hits == 2)
        {
            int i1, i2, aloneIdx, sideAIdx, sideBIdx;
            if (hitAB && hitBC)
            {
                i1 = FindOrAddNode(nodes, pAB, mergeEps);
                i2 = FindOrAddNode(nodes, pBC, mergeEps);
                aloneIdx = bi; sideAIdx = ai; sideBIdx = ci;
            }
            else if (hitBC && hitCA)
            {
                i1 = FindOrAddNode(nodes, pBC, mergeEps);
                i2 = FindOrAddNode(nodes, pCA, mergeEps);
                aloneIdx = ci; sideAIdx = bi; sideBIdx = ai;
            }
            else
            {
                i1 = FindOrAddNode(nodes, pCA, mergeEps);
                i2 = FindOrAddNode(nodes, pAB, mergeEps);
                aloneIdx = ai; sideAIdx = ci; sideBIdx = bi;
            }

            if (i1 == i2)
                return false;
            replacements.Add((aloneIdx, i1, i2));
            replacements.Add((i1, sideAIdx, sideBIdx));
            replacements.Add((i1, sideBIdx, i2));
            return true;
        }

        return false;
    }

    private static bool PointOnSegment(Point p, Point a, Point b, double tol)
        => DistanceToSegment(p, a, b) <= tol;

    private static bool TryProperSegmentIntersection(Point a, Point b, Point c, Point d, double tol, out Point hit)
    {
        hit = default;
        double dx1 = b.X - a.X, dy1 = b.Y - a.Y;
        double dx2 = d.X - c.X, dy2 = d.Y - c.Y;
        double den = dx1 * dy2 - dy1 * dx2;
        if (Math.Abs(den) < 1e-16)
            return false;
        double t = ((c.X - a.X) * dy2 - (c.Y - a.Y) * dx2) / den;
        double u = ((c.X - a.X) * dy1 - (c.Y - a.Y) * dx1) / den;
        if (t <= 1e-8 || t >= 1.0 - 1e-8 || u < -1e-8 || u > 1.0 + 1e-8)
            return false;
        hit = new Point(a.X + t * dx1, a.Y + t * dy1);
        if (Distance(hit, a) <= tol || Distance(hit, b) <= tol)
            return false;
        return true;
    }

    private static bool TriangleStraddlesHorizontalCut(Point pa, Point pb, Point pc, double cutY, double tol)
    {
        double minY = Math.Min(Math.Min(pa.Y, pb.Y), pc.Y);
        double maxY = Math.Max(Math.Max(pa.Y, pb.Y), pc.Y);
        return minY < cutY - tol && maxY > cutY + tol;
    }

    private static bool WouldTriangleStraddleAnyCut(
        Point pa, Point pb, Point pc,
        IReadOnlyList<double> cutYs, double tol)
    {
        foreach (var cutY in cutYs)
            if (TriangleStraddlesHorizontalCut(pa, pb, pc, cutY, tol))
                return true;
        return false;
    }

    private static bool WouldTriangleStraddleAnyCut(
        IReadOnlyList<Point> nodes, int i, int j, int k,
        IReadOnlyList<double> cutYs, double tol)
    {
        return WouldTriangleStraddleAnyCut(nodes[i], nodes[j], nodes[k], cutYs, tol);
    }

    private static Point SnapCircumcenterToLayerCut(Point cc, IReadOnlyList<double> cutYs, double sizeAtCc, double tol)
    {
        double snapBand = Math.Max(sizeAtCc * 0.5, tol * 10);
        foreach (var cutY in cutYs)
        {
            if (Math.Abs(cc.Y - cutY) <= snapBand)
                return new Point(cc.X, cutY);
        }
        return cc;
    }

    private static bool TryEdgeHorizontalCut(Point a, Point b, double cutY, double tol, out Point hit)
    {
        hit = default;
        double da = a.Y - cutY;
        double db = b.Y - cutY;
        if (Math.Abs(da) <= tol && Math.Abs(db) <= tol)
            return false;
        if (da * db > tol * tol)
            return false;
        if (Math.Abs(b.Y - a.Y) < 1e-12)
            return false;
        double t = (cutY - a.Y) / (b.Y - a.Y);
        if (t < -1e-9 || t > 1.0 + 1e-9)
            return false;
        t = Math.Clamp(t, 0.0, 1.0);
        hit = new Point(a.X + t * (b.X - a.X), cutY);
        return true;
    }

    private static int FindOrAddNode(List<Point> nodes, Point p, double mergeEps)
    {
        double merge2 = mergeEps * mergeEps;
        for (int i = 0; i < nodes.Count; i++)
        {
            double dx = nodes[i].X - p.X, dy = nodes[i].Y - p.Y;
            if (dx * dx + dy * dy <= merge2)
                return i;
        }
        nodes.Add(p);
        return nodes.Count - 1;
    }

    /// <summary>Split one triangle that straddles y = cutY into 2–3 sub-triangles with nodes on the cut.</summary>
    private static bool TrySplitTriangleAtHorizontalCut(
        List<Point> nodes,
        Point pa, Point pb, Point pc,
        int ai, int bi, int ci,
        double cutY, double tol, double mergeEps,
        out List<(int a, int b, int c)> replacements)
    {
        replacements = new List<(int, int, int)>();
        int Side(double y) => y < cutY - tol ? -1 : y > cutY + tol ? 1 : 0;

        int sa = Side(pa.Y), sb = Side(pb.Y), sc = Side(pc.Y);
        if (sa == 0 && sb == 0 && sc == 0) return false;

        // One vertex on the cut, two on opposite sides — crossing on the edge between them.
        if (sa == 0 && sb != sc)
        {
            if (!TryEdgeHorizontalCut(pb, pc, cutY, tol, out var hit))
                return false;
            int pi = FindOrAddNode(nodes, hit, mergeEps);
            replacements.Add((ai, bi, pi));
            replacements.Add((ai, pi, ci));
            return true;
        }
        if (sb == 0 && sa != sc)
        {
            if (!TryEdgeHorizontalCut(pa, pc, cutY, tol, out var hit))
                return false;
            int pi = FindOrAddNode(nodes, hit, mergeEps);
            replacements.Add((ai, bi, pi));
            replacements.Add((bi, pi, ci));
            return true;
        }
        if (sc == 0 && sa != sb)
        {
            if (!TryEdgeHorizontalCut(pa, pb, cutY, tol, out var hit))
                return false;
            int pi = FindOrAddNode(nodes, hit, mergeEps);
            replacements.Add((ai, pi, ci));
            replacements.Add((pi, bi, ci));
            return true;
        }

        // Two vertices on one side, one on the other — two edge crossings.
        int aloneIdx, sideAIdx, sideBIdx;
        Point alone, sideA, sideB;
        if (sa != sb && sa != sc)
        {
            aloneIdx = ai; sideAIdx = bi; sideBIdx = ci;
            alone = pa; sideA = pb; sideB = pc;
        }
        else if (sb != sa && sb != sc)
        {
            aloneIdx = bi; sideAIdx = ci; sideBIdx = ai;
            alone = pb; sideA = pc; sideB = pa;
        }
        else if (sc != sa && sc != sb)
        {
            aloneIdx = ci; sideAIdx = ai; sideBIdx = bi;
            alone = pc; sideA = pa; sideB = pb;
        }
        else
            return false;

        if (!TryEdgeHorizontalCut(alone, sideA, cutY, tol, out var p1)) return false;
        if (!TryEdgeHorizontalCut(alone, sideB, cutY, tol, out var p2)) return false;
        int i1 = FindOrAddNode(nodes, p1, mergeEps);
        int i2 = FindOrAddNode(nodes, p2, mergeEps);
        if (i1 == i2)
            return false;

        replacements.Add((aloneIdx, i1, i2));
        replacements.Add((i1, sideAIdx, sideBIdx));
        replacements.Add((i1, sideBIdx, i2));
        return true;
    }

    private static bool TryComputeCircumcenter(Point a, Point b, Point c, out Point center)
    {
        double d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        if (Math.Abs(d) < 1e-14) { center = default; return false; }
        double ux = ((a.X * a.X + a.Y * a.Y) * (b.Y - c.Y)
                   + (b.X * b.X + b.Y * b.Y) * (c.Y - a.Y)
                   + (c.X * c.X + c.Y * c.Y) * (a.Y - b.Y)) / d;
        double uy = ((a.X * a.X + a.Y * a.Y) * (c.X - b.X)
                   + (b.X * b.X + b.Y * b.Y) * (a.X - c.X)
                   + (c.X * c.X + c.Y * c.Y) * (b.X - a.X)) / d;
        center = new Point(ux, uy);
        return true;
    }

    private static double MinAngleDeg3(Point a, Point b, Point c)
    {
        double ab = Distance(a, b), bc = Distance(b, c), ca = Distance(c, a);
        if (ab < 1e-12 || bc < 1e-12 || ca < 1e-12) return 0;
        double angA = Math.Acos(Math.Clamp((ab * ab + ca * ca - bc * bc) / (2 * ab * ca), -1, 1)) * 180 / Math.PI;
        double angB = Math.Acos(Math.Clamp((ab * ab + bc * bc - ca * ca) / (2 * ab * bc), -1, 1)) * 180 / Math.PI;
        double angC = 180 - angA - angB;
        return Math.Min(angA, Math.Min(angB, angC));
    }

    private static Point Centroid(IReadOnlyList<Point> pts, (int a, int b, int c) t)
    {
        var pa = pts[t.a]; var pb = pts[t.b]; var pc = pts[t.c];
        return new Point((pa.X + pb.X + pc.X) / 3.0, (pa.Y + pb.Y + pc.Y) / 3.0);
    }

    private static IEnumerable<IReadOnlyList<Point>> EnumerateBoundaryPolys(
        IReadOnlyList<Point> outer, IReadOnlyList<IReadOnlyList<Point>>? holes)
    {
        yield return outer;
        if (holes == null) yield break;
        foreach (var h in holes)
            if (h != null && h.Count >= 3) yield return h;
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

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double L2 = dx * dx + dy * dy;
        if (L2 < 1e-20) return Distance(p, a);
        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / L2;
        t = Math.Clamp(t, 0, 1);
        double cx = a.X + t * dx, cy = a.Y + t * dy;
        double ex = p.X - cx, ey = p.Y - cy;
        return Math.Sqrt(ex * ex + ey * ey);
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

        // h(d) = nearSize · growth^(d / nearSize), clamped to [nearSize, globalSize]
        double k = dMin / Math.Max(nearSize, 1e-12);
        double h = nearSize * Math.Pow(growth, k);
        return Math.Clamp(h, nearSize, globalSize);
    }

    private static bool CentroidInAnyHole(Point c, IReadOnlyList<IReadOnlyList<Point>>? holes)
    {
        if (holes == null) return false;
        foreach (var h in holes)
        {
            if (h != null && h.Count >= 3 && PointInPolygon(c, h))
                return true;
        }
        return false;
    }

    /// <summary>Merge points closer than <paramref name="eps"/> to each other.</summary>
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

    /// <summary>Ray-casting point-in-polygon (skips horizontal edges).</summary>
    public static bool PointInPolygon(Point p, IReadOnlyList<Point> poly)
    {
        int n = poly.Count;
        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = poly[i].X, yi = poly[i].Y;
            double xj = poly[j].X, yj = poly[j].Y;
            if (Math.Abs(yj - yi) < 1e-12) continue;
            if ((yi > p.Y) != (yj > p.Y))
            {
                double xInt = (xj - xi) * (p.Y - yi) / (yj - yi) + xi;
                if (p.X < xInt) inside = !inside;
            }
        }
        return inside;
    }

    private static void ComputeQualityStats(
        IReadOnlyList<Point> pts,
        IReadOnlyList<(int a, int b, int c)> tris,
        out double minAngle,
        out double maxAspectRatio)
    {
        minAngle       = 180;
        maxAspectRatio = 0;

        foreach (var (ai, bi, ci) in tris)
        {
            var a = pts[ai]; var b = pts[bi]; var c = pts[ci];

            double ab = Distance(a, b), bc = Distance(b, c), ca = Distance(c, a);
            if (ab < 1e-12 || bc < 1e-12 || ca < 1e-12) continue;

            // Angles via law of cosines
            double angA = Math.Acos(Math.Clamp((ab*ab + ca*ca - bc*bc) / (2*ab*ca), -1, 1)) * 180 / Math.PI;
            double angB = Math.Acos(Math.Clamp((ab*ab + bc*bc - ca*ca) / (2*ab*bc), -1, 1)) * 180 / Math.PI;
            double angC = 180 - angA - angB;

            minAngle = Math.Min(minAngle, Math.Min(angA, Math.Min(angB, angC)));

            // Aspect ratio ≈ longest edge / (2 * inradius)
            double s    = (ab + bc + ca) / 2;
            double area = Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) / 2;
            if (area > 1e-12)
            {
                double inRadius = area / s;
                double ar = Math.Max(ab, Math.Max(bc, ca)) / (2 * inRadius);
                maxAspectRatio = Math.Max(maxAspectRatio, ar);
            }
        }

        if (tris.Count == 0) { minAngle = 0; maxAspectRatio = 0; }
    }

    private static double Distance(Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static Vector Normalize(Vector v)
    {
        double len = v.Length;
        return len < 1e-10 ? new Vector(0, 0) : v / len;
    }
}
