using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>One region handed to the hybrid driver.</summary>
public readonly record struct HybridRegionInput(int SurfaceNumber, IReadOnlyList<Point> Polygon, bool HasHoles);

/// <summary>One void/excavation region: its boundary is honoured but no elements are emitted for it.</summary>
public readonly record struct HybridHoleInput(int SurfaceNumber, IReadOnlyList<Point> Polygon);

/// <summary>Inputs to a hybrid mesh generation pass.</summary>
public sealed class HybridMeshRequest
{
    /// <summary>Regions to mesh (soil/rock surfaces).</summary>
    public IReadOnlyList<HybridRegionInput> Regions { get; init; } = Array.Empty<HybridRegionInput>();

    /// <summary>Excavations / voids carved out of parent regions.</summary>
    public IReadOnlyList<HybridHoleInput>? HoleOnlyRegions { get; init; }

    /// <summary>Material-layer interface segments the mesh must respect (conforming edges).</summary>
    public IReadOnlyList<(Point A, Point B)>? MaterialLayerSegments { get; init; }

    /// <summary>Additional constraint segments (joint traces) that must become mesh edges.</summary>
    public IReadOnlyList<(Point A, Point B)>? ExtraConstraintSegments { get; init; }

    /// <summary>Optional pre-built sizing field. When null one is built from <see cref="BaseSize"/>
    /// and the supplied geometry features.</summary>
    public MeshSizingField? SizingField { get; init; }

    /// <summary>Interior/global target element size used to build a default sizing field when
    /// <see cref="SizingField"/> is null. If ≤ 0 it is derived from the geometry bounding box.</summary>
    public double BaseSize { get; init; }

    /// <summary>Allow structured transfinite regions; when false everything routes to CDT.</summary>
    public bool AllowStructured { get; init; } = true;

    /// <summary>Allow conformal stitching of axis-aligned rectangular structured blocks into the
    /// surrounding CDT mesh (Phase 2). When false, mixed domains route entirely to CDT.</summary>
    public bool AllowStitching { get; init; } = true;

    /// <summary>Quality-gate thresholds applied to the final mesh (null = defaults).</summary>
    public HybridMeshQualityGates? QualityGates { get; init; }

    /// <summary>Quad-dominant recombination: after CDT, greedily merge adjacent triangle pairs into
    /// convex quads (RS2/Plaxis/Gmsh-style) so arbitrary geometry yields a mostly-quad mesh.</summary>
    public bool RecombineToQuads { get; init; }

    /// <summary>Minimum quad quality (0..1, 1 = square) for a triangle pair to be recombined.</summary>
    public double QuadRecombineMinQuality { get; init; } = 0.30;

    /// <summary>Safety cap on combined node count.</summary>
    public int MaxNodes { get; init; } = 40_000;
}

/// <summary>
/// The single entry point for hybrid meshing. It (1) builds/uses a shared <see cref="MeshSizingField"/>,
/// (2) classifies every region with <see cref="RegionMeshClassifier"/>, and (3) executes the plan,
/// reusing the proven backend engines: a transfinite map for clean single mappable regions and the
/// graded conforming Delaunay engine (<see cref="GradedSurfaceMeshGenerator"/>) for everything else.
///
/// Phase 1: a unified, sizing-field-driven driver with a structured fast-path for the common
/// single-quad domain and conforming CDT for multi-region / holed / layered domains.
/// Phase 2 (this revision): conformal structured+unstructured stitching within one domain —
/// axis-aligned rectangular mappable blocks are meshed with transfinite quads and stitched into the
/// surrounding CDT by pinning their exact perimeter nodes (fixed-boundary holes), guarded by a strict
/// hanging-node self-check with an all-CDT fallback; plus mesh-quality gates on the final result.
/// Phase 3 (future) re-runs this driver with extra refinement sources injected into the sizing field.
/// </summary>
public static class HybridMeshDriver
{
    /// <summary>Generate a hybrid mesh for <paramref name="request"/>.</summary>
    public static HybridMeshResult Generate(HybridMeshRequest request)
    {
        if (request == null || request.Regions == null || request.Regions.Count == 0)
            return HybridMeshResult.Empty;

        var field = request.SizingField ?? BuildDefaultSizingField(request);

        // Classify every region up front (shared decision used by every downstream phase).
        var classifications = request.Regions
            .Select(r => RegionMeshClassifier.Classify(r.SurfaceNumber, r.Polygon, r.HasHoles, request.AllowStructured))
            .ToList();

        bool hasHoles = request.HoleOnlyRegions is { Count: > 0 };
        bool hasLayers = request.MaterialLayerSegments is { Count: > 0 };

        HybridMeshResult raw;

        // Structured fast-path: a single, clean, mappable region with no voids/interfaces needs no
        // stitching, so we emit a high-quality transfinite quad grid directly.
        if (request.Regions.Count == 1 && !hasHoles && !hasLayers &&
            classifications[0].Strategy == RegionMeshStrategy.StructuredTransfinite &&
            classifications[0].Corners is { Count: 4 } corners)
        {
            raw = GenerateStructuredSingle(request.Regions[0].SurfaceNumber, corners, field, request.MaxNodes);
        }
        else
        {
            // Phase 2 — conformal stitch: when the domain contains axis-aligned rectangular mappable
            // blocks, mesh those structurally and stitch the surrounding space with conforming CDT
            // (the block perimeter nodes are pinned exactly so there are no hanging nodes). The stitch
            // is guarded by a strict self-check; on any non-conformance it returns null and we fall
            // back to the proven all-CDT path so a broken mesh can never be produced.
            bool stitchable = request.AllowStitching && request.AllowStructured &&
                classifications.Any(c => c.Strategy == RegionMeshStrategy.StructuredTransfinite
                                         && c.IsAxisAlignedRectangle && c.Corners is { Count: 4 });

            raw = (stitchable ? TryGenerateStitched(request, field, classifications) : null)
                  ?? GenerateConformingCdt(request, field, classifications);
        }

        // Quad-dominant recombination (Option 2): turn CDT triangles into convex quad pairs where
        // quality allows, leaving the rest as triangles. Structured regions (quads only) are untouched.
        if (request.RecombineToQuads && raw.Regions.Any(r => r.Triangles.Count > 0))
            raw = ApplyRecombination(raw, request.QuadRecombineMinQuality);

        return WithQuality(raw, request.QualityGates);
    }

    /// <summary>Recombine every region's triangles into quad-dominant element sets over the same node
    /// pool, then refresh the aggregate min-angle / max-AR. Conformance is preserved because no nodes
    /// move and only interior (twice-used) edges between two triangles of the same region are merged.</summary>
    private static HybridMeshResult ApplyRecombination(HybridMeshResult raw, double minQuality)
    {
        var newRegions = new List<HybridRegionMesh>(raw.Regions.Count);
        int recombined = 0;

        foreach (var region in raw.Regions)
        {
            if (region.Triangles.Count == 0)
            {
                newRegions.Add(region);
                continue;
            }

            var (newQuads, leftoverTris) = TriangleToQuadRecombiner.Recombine(raw.Nodes, region.Triangles, minQuality);
            recombined += newQuads.Count;

            var mergedQuads = new List<(int a, int b, int c, int d)>(region.Quads.Count + newQuads.Count);
            mergedQuads.AddRange(region.Quads);
            mergedQuads.AddRange(newQuads);

            newRegions.Add(new HybridRegionMesh
            {
                SurfaceNumber = region.SurfaceNumber,
                Strategy = region.Strategy,
                Quads = mergedQuads,
                Triangles = leftoverTris,
                Reason = region.Reason + " + quad recombination"
            });
        }

        var quality = HybridMeshQuality.Evaluate(raw.Nodes, newRegions, HybridMeshQualityGates.Default);
        string note = string.IsNullOrWhiteSpace(raw.Notes)
            ? $"quad-dominant recombination ({recombined} quad pairs)"
            : $"{raw.Notes}; quad-dominant recombination ({recombined} quad pairs)";

        return new HybridMeshResult
        {
            Nodes = raw.Nodes,
            Regions = newRegions,
            MinAngleDeg = quality.MinAngleDeg,
            MaxAspectRatio = quality.MaxAspectRatio,
            StitchApplied = raw.StitchApplied,
            Notes = note
        };
    }

    /// <summary>Attach quality-gate metrics to a freshly built result.</summary>
    private static HybridMeshResult WithQuality(HybridMeshResult raw, HybridMeshQualityGates? gates)
    {
        if (raw == null || raw.Nodes.Count == 0 || raw.Regions.Count == 0)
            return raw ?? HybridMeshResult.Empty;

        var quality = HybridMeshQuality.Evaluate(raw.Nodes, raw.Regions, gates);
        return new HybridMeshResult
        {
            Nodes = raw.Nodes,
            Regions = raw.Regions,
            MinAngleDeg = raw.MinAngleDeg,
            MaxAspectRatio = raw.MaxAspectRatio,
            Quality = quality,
            StitchApplied = raw.StitchApplied,
            Notes = raw.Notes,
        };
    }

    // ── Structured transfinite fast-path ──────────────────────────────────────

    private static HybridMeshResult GenerateStructuredSingle(
        int surfaceNumber, IReadOnlyList<Point> corners, MeshSizingField field, int maxNodes)
    {
        // Logical box corners (CCW): c0=(0,0) c1=(1,0) c2=(1,1) c3=(0,1).
        Point c0 = corners[0], c1 = corners[1], c2 = corners[2], c3 = corners[3];

        double widthBottom = Dist(c0, c1), widthTop = Dist(c3, c2);
        double heightLeft = Dist(c0, c3), heightRight = Dist(c1, c2);
        double avgWidth = 0.5 * (widthBottom + widthTop);
        double avgHeight = 0.5 * (heightLeft + heightRight);

        var sizing = field.SampleRegion(corners);
        double target = Math.Max(sizing.GlobalSize, field.MinSize);

        int nxe = Math.Clamp((int)Math.Round(avgWidth / target), 1, 400);
        int nye = Math.Clamp((int)Math.Round(avgHeight / target), 1, 400);

        // Honour the node cap.
        while ((long)(nxe + 1) * (nye + 1) > maxNodes && (nxe > 1 || nye > 1))
        {
            if (nxe >= nye && nxe > 1) nxe--;
            else if (nye > 1) nye--;
            else break;
        }

        var nodes = new List<Point>((nxe + 1) * (nye + 1));
        var idx = new int[nxe + 1, nye + 1];
        for (int j = 0; j <= nye; j++)
        {
            double v = (double)j / nye;
            for (int i = 0; i <= nxe; i++)
            {
                double u = (double)i / nxe;
                idx[i, j] = nodes.Count;
                nodes.Add(Bilinear(c0, c1, c2, c3, u, v));
            }
        }

        var quads = new List<(int a, int b, int c, int d)>(nxe * nye);
        for (int j = 0; j < nye; j++)
            for (int i = 0; i < nxe; i++)
                quads.Add((idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], idx[i, j + 1]));

        ComputeQuadQuality(nodes, quads, out double minAngle, out double maxAr);

        var region = new HybridRegionMesh
        {
            SurfaceNumber = surfaceNumber,
            Strategy = RegionMeshStrategy.StructuredTransfinite,
            Quads = quads,
            Reason = $"structured transfinite {nxe}×{nye}"
        };

        return new HybridMeshResult
        {
            Nodes = nodes,
            Regions = new[] { region },
            MinAngleDeg = minAngle,
            MaxAspectRatio = maxAr,
            Notes = "single-region transfinite fast-path"
        };
    }

    // ── Phase 2: conformal structured + CDT stitch ─────────────────────────────

    /// <summary>
    /// Mesh axis-aligned rectangular mappable regions as transfinite quad blocks and stitch the
    /// remaining domain with conforming CDT. Block perimeter nodes are pinned into the CDT as
    /// fixed-boundary holes so the interface is node-for-node conforming. A strict conformance
    /// self-check rejects any hanging node; on failure (or empty CDT) returns null so the caller
    /// can fall back to all-CDT. Returns null when nothing is gained (e.g. all regions are blocks
    /// and there is no surrounding space, which the fast-path / CDT path already handle well).
    /// </summary>
    private static HybridMeshResult? TryGenerateStitched(
        HybridMeshRequest request, MeshSizingField field, IReadOnlyList<RegionClassification> classifications)
    {
        var byNum = classifications.ToDictionary(c => c.SurfaceNumber, c => c);

        var blockRegions = new List<(int Sn, IReadOnlyList<Point> Corners)>();
        var cdtRegions = new List<HybridRegionInput>();
        foreach (var r in request.Regions)
        {
            if (byNum.TryGetValue(r.SurfaceNumber, out var cls)
                && cls.Strategy == RegionMeshStrategy.StructuredTransfinite
                && cls.IsAxisAlignedRectangle
                && cls.Corners is { Count: 4 } corners
                && !r.HasHoles)
                blockRegions.Add((r.SurfaceNumber, corners));
            else
                cdtRegions.Add(r);
        }

        if (blockRegions.Count == 0)
            return null;

        // A single uniform near-size shared by blocks and surrounding CDT guarantees the engine's
        // uniform edge sampling on a shared edge lands on the same positions as the block perimeter.
        double stitchNear = double.MaxValue;
        foreach (var b in blockRegions)
            stitchNear = Math.Min(stitchNear, field.SampleRegion(b.Corners).NearSize);
        foreach (var r in cdtRegions)
            stitchNear = Math.Min(stitchNear, field.SampleRegion(r.Polygon).NearSize);
        if (request.HoleOnlyRegions != null)
            foreach (var h in request.HoleOnlyRegions)
                stitchNear = Math.Min(stitchNear, field.SampleRegion(h.Polygon).NearSize);
        if (!(stitchNear > 0) || double.IsInfinity(stitchNear))
            return null;

        double dedupEps = stitchNear * 0.05;
        var pool = new NodePool(dedupEps);
        var regions = new List<HybridRegionMesh>();
        var fixedHoles = new List<(int SurfaceNumber, IReadOnlyList<Point> BoundaryNodes)>();

        // 1) Structured blocks: build grids, register nodes in the shared pool, record perimeter loops.
        foreach (var (sn, corners) in blockRegions)
        {
            BuildStructuredRectGrid(corners, stitchNear, request.MaxNodes,
                out var blockNodes, out var blockQuads, out var perimeter);
            if (blockNodes.Count == 0 || blockQuads.Count == 0)
                return null;

            var map = new int[blockNodes.Count];
            for (int i = 0; i < blockNodes.Count; i++)
                map[i] = pool.AddOrGet(blockNodes[i]);

            var remappedQuads = blockQuads
                .Select(q => (map[q.a], map[q.b], map[q.c], map[q.d]))
                .ToList();

            regions.Add(new HybridRegionMesh
            {
                SurfaceNumber = sn,
                Strategy = RegionMeshStrategy.StructuredTransfinite,
                Quads = remappedQuads,
                Reason = "axis-aligned transfinite block (stitched)"
            });

            fixedHoles.Add((sn, perimeter));
        }

        // 2) Surrounding CDT: only run if there is non-block space to fill.
        var cdtWithPolys = cdtRegions.Where(r => r.Polygon is { Count: >= 3 }).ToList();
        if (cdtWithPolys.Count > 0)
        {
            var regionParams = cdtWithPolys.Select(r =>
            {
                var s = field.SampleRegion(r.Polygon);
                // Pin the boundary at the shared near-size so shared edges match block perimeter spacing.
                return (r.SurfaceNumber, r.Polygon, stitchNear, s.GlobalSize, s.BoundaryLayers, s.GrowthFactor);
            }).ToList();

            var holeParams = request.HoleOnlyRegions?
                .Select(h => (h.SurfaceNumber, h.Polygon, stitchNear))
                .ToList();

            var conforming = GradedSurfaceMeshGenerator.GenerateConforming(
                regionParams,
                holeParams,
                request.MaterialLayerSegments,
                pinnedBoundaryNodes: null,
                maxNodes: request.MaxNodes,
                fixedBoundaryHoles: fixedHoles,
                extraConstraintSegments: request.ExtraConstraintSegments);

            if (conforming == null || conforming.Count == 0)
                return null;

            IReadOnlyList<Point> cdtNodes = conforming[0].Mesh.Nodes;
            var cdtMap = new int[cdtNodes.Count];
            for (int i = 0; i < cdtNodes.Count; i++)
                cdtMap[i] = pool.AddOrGet(cdtNodes[i]);

            foreach (var (sn, mesh) in conforming)
            {
                if (mesh.Triangles.Count == 0) continue;
                regions.Add(new HybridRegionMesh
                {
                    SurfaceNumber = sn,
                    Strategy = RegionMeshStrategy.UnstructuredCdt,
                    Triangles = mesh.Triangles.Select(t => (cdtMap[t.a], cdtMap[t.b], cdtMap[t.c])).ToList(),
                    Reason = "conforming CDT (stitched around structured blocks)"
                });
            }
        }
        else if (request.HoleOnlyRegions is { Count: > 0 } || cdtRegions.Count > 0)
        {
            // There is non-block geometry we could not turn into CDT regions → don't risk a partial mesh.
            return null;
        }

        var nodes = pool.Nodes;
        if (nodes.Count == 0 || regions.Count == 0)
            return null;

        // 3) Strict conformance self-check: no node may sit on the interior of a structured-block
        // boundary edge. If it does, the interface is non-conforming → reject (caller falls back).
        if (!IsConforming(nodes, regions, stitchNear))
            return null;

        ComputeMixedQuality(nodes, regions, out double minAngle, out double maxAr);

        int structuredCount = regions.Count(r => r.Strategy == RegionMeshStrategy.StructuredTransfinite);
        return new HybridMeshResult
        {
            Nodes = nodes,
            Regions = regions,
            MinAngleDeg = minAngle,
            MaxAspectRatio = maxAr,
            StitchApplied = true,
            Notes = $"conformal stitch: {structuredCount} structured block(s) + CDT"
        };
    }

    /// <summary>Build a transfinite quad grid for an axis-aligned rectangle, with per-edge subdivisions
    /// = ceil(edge/nearSize) so the perimeter spacing matches the CDT engine's uniform edge sampling.
    /// Emits the grid nodes, quad connectivity (indices into <paramref name="nodes"/>) and the ordered
    /// CCW perimeter node loop used to pin the block into the surrounding triangulation.</summary>
    private static void BuildStructuredRectGrid(
        IReadOnlyList<Point> corners, double nearSize, int maxNodes,
        out List<Point> nodes, out List<(int a, int b, int c, int d)> quads, out List<Point> perimeter)
    {
        Point c0 = corners[0], c1 = corners[1], c2 = corners[2], c3 = corners[3];
        int nxe = Math.Clamp((int)Math.Ceiling(Dist(c0, c1) / nearSize), 1, 600);
        int nye = Math.Clamp((int)Math.Ceiling(Dist(c1, c2) / nearSize), 1, 600);
        while ((long)(nxe + 1) * (nye + 1) > maxNodes && (nxe > 1 || nye > 1))
        {
            if (nxe >= nye && nxe > 1) nxe--;
            else if (nye > 1) nye--;
            else break;
        }

        nodes = new List<Point>((nxe + 1) * (nye + 1));
        var idx = new int[nxe + 1, nye + 1];
        for (int j = 0; j <= nye; j++)
        {
            double v = (double)j / nye;
            for (int i = 0; i <= nxe; i++)
            {
                double u = (double)i / nxe;
                idx[i, j] = nodes.Count;
                nodes.Add(Bilinear(c0, c1, c2, c3, u, v));
            }
        }

        quads = new List<(int a, int b, int c, int d)>(nxe * nye);
        for (int j = 0; j < nye; j++)
            for (int i = 0; i < nxe; i++)
                quads.Add((idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], idx[i, j + 1]));

        // CCW perimeter loop, corners once: bottom → right → top → left.
        perimeter = new List<Point>(2 * (nxe + nye));
        for (int i = 0; i <= nxe; i++) perimeter.Add(nodes[idx[i, 0]]);
        for (int j = 1; j <= nye; j++) perimeter.Add(nodes[idx[nxe, j]]);
        for (int i = nxe - 1; i >= 0; i--) perimeter.Add(nodes[idx[i, nye]]);
        for (int j = nye - 1; j >= 1; j--) perimeter.Add(nodes[idx[0, j]]);
    }

    // ── Conforming CDT general path ────────────────────────────────────────────

    private static HybridMeshResult GenerateConformingCdt(
        HybridMeshRequest request, MeshSizingField field, IReadOnlyList<RegionClassification> classifications)
    {
        var regionParams = request.Regions.Select(r =>
        {
            var s = field.SampleRegion(r.Polygon);
            return (r.SurfaceNumber, r.Polygon, s.NearSize, s.GlobalSize, s.BoundaryLayers, s.GrowthFactor);
        }).ToList();

        var holeParams = request.HoleOnlyRegions?
            .Select(h => (h.SurfaceNumber, h.Polygon, field.SampleRegion(h.Polygon).NearSize))
            .ToList();

        var conforming = GradedSurfaceMeshGenerator.GenerateConforming(
            regionParams,
            holeParams,
            request.MaterialLayerSegments,
            pinnedBoundaryNodes: null,
            maxNodes: request.MaxNodes,
            extraConstraintSegments: request.ExtraConstraintSegments);

        if (conforming == null || conforming.Count == 0)
            return HybridMeshResult.Empty;

        // All per-region results share one global Nodes list.
        IReadOnlyList<Point> sharedNodes = conforming[0].Mesh.Nodes;

        var byStrategy = classifications.ToDictionary(c => c.SurfaceNumber, c => c);
        var regions = new List<HybridRegionMesh>(conforming.Count);
        double minAngle = double.MaxValue;
        double maxAr = 0;

        foreach (var (surfaceNumber, mesh) in conforming)
        {
            if (mesh.Triangles.Count == 0) continue;
            byStrategy.TryGetValue(surfaceNumber, out var cls);

            // Density (CDT) is what was actually built here; record the original classification intent
            // in the reason so a region that *could* be structured is visible for Phase 2 stitching.
            string reason = cls is { Strategy: RegionMeshStrategy.StructuredTransfinite }
                ? "mappable region meshed as CDT (structured stitching deferred to Phase 2)"
                : (cls?.Reason ?? "conforming CDT");

            regions.Add(new HybridRegionMesh
            {
                SurfaceNumber = surfaceNumber,
                Strategy = RegionMeshStrategy.UnstructuredCdt,
                Triangles = mesh.Triangles,
                Reason = reason
            });

            if (mesh.MinAngleDeg > 0) minAngle = Math.Min(minAngle, mesh.MinAngleDeg);
            maxAr = Math.Max(maxAr, mesh.MaxAspectRatio);
        }

        if (regions.Count == 0)
            return HybridMeshResult.Empty;

        // Aggregate the distinct routing reasons so the UI can explain *why* the mesh is all-triangle.
        var reasons = classifications
            .Where(c => c.Strategy == RegionMeshStrategy.UnstructuredCdt && !string.IsNullOrWhiteSpace(c.Reason))
            .Select(c => c.Reason)
            .Distinct()
            .ToList();
        string note = reasons.Count > 0
            ? "all-CDT: " + string.Join("; ", reasons)
            : "conforming CDT";

        return new HybridMeshResult
        {
            Nodes = sharedNodes,
            Regions = regions,
            MinAngleDeg = minAngle == double.MaxValue ? 0 : minAngle,
            MaxAspectRatio = maxAr,
            Notes = note
        };
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Spatial-hash node pool that deduplicates coincident points within <c>eps</c> while
    /// assigning stable indices — used to merge structured-block grids and the CDT cloud into one
    /// shared node list (the basis of a conforming mixed mesh).</summary>
    private sealed class NodePool
    {
        private readonly List<Point> _nodes = new();
        private readonly Dictionary<(long, long), List<int>> _cells = new();
        private readonly double _eps;
        private readonly double _eps2;

        public NodePool(double eps)
        {
            _eps = Math.Max(eps, 1e-9);
            _eps2 = _eps * _eps;
        }

        public List<Point> Nodes => _nodes;

        private (long, long) Cell(Point p) => ((long)Math.Floor(p.X / _eps), (long)Math.Floor(p.Y / _eps));

        public int AddOrGet(Point p)
        {
            var (cx, cy) = Cell(p);
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    if (_cells.TryGetValue((cx + dx, cy + dy), out var bucket))
                        foreach (int id in bucket)
                        {
                            double ddx = _nodes[id].X - p.X, ddy = _nodes[id].Y - p.Y;
                            if (ddx * ddx + ddy * ddy <= _eps2) return id;
                        }

            int newId = _nodes.Count;
            _nodes.Add(p);
            var key = (cx, cy);
            if (!_cells.TryGetValue(key, out var list)) { list = new List<int>(); _cells[key] = list; }
            list.Add(newId);
            return newId;
        }
    }

    /// <summary>Strict interface conformance check: no node may lie on the interior of a structured
    /// block's boundary edge (a hanging node). Only the once-used quad edges (block perimeter) can be
    /// shared with CDT triangles, so those are the only edges that can develop hanging nodes.</summary>
    private static bool IsConforming(IReadOnlyList<Point> nodes, IReadOnlyList<HybridRegionMesh> regions, double stitchNear)
    {
        // Count quad edge usage to isolate block-perimeter (once-used) edges.
        var edgeUse = new Dictionary<(int, int), int>();
        void Bump(int a, int b)
        {
            var k = a < b ? (a, b) : (b, a);
            edgeUse[k] = edgeUse.TryGetValue(k, out int n) ? n + 1 : 1;
        }
        foreach (var r in regions)
            foreach (var (a, b, c, d) in r.Quads)
            {
                Bump(a, b); Bump(b, c); Bump(c, d); Bump(d, a);
            }

        var perimeterEdges = edgeUse.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
        if (perimeterEdges.Count == 0)
            return true; // no quads → nothing to stitch against

        // Spatial hash of all nodes for fast edge-neighbourhood queries.
        double cell = Math.Max(stitchNear, 1e-9);
        var grid = new Dictionary<(long, long), List<int>>();
        (long, long) Cell(Point p) => ((long)Math.Floor(p.X / cell), (long)Math.Floor(p.Y / cell));
        for (int i = 0; i < nodes.Count; i++)
        {
            var key = Cell(nodes[i]);
            if (!grid.TryGetValue(key, out var list)) { list = new List<int>(); grid[key] = list; }
            list.Add(i);
        }

        double perpTol = stitchNear * 0.1;
        double endTol = stitchNear * 0.25; // ignore points essentially at an endpoint

        foreach (var (ia, ib) in perimeterEdges)
        {
            Point a = nodes[ia], b = nodes[ib];
            double abx = b.X - a.X, aby = b.Y - a.Y;
            double len2 = abx * abx + aby * aby;
            if (len2 < 1e-18) continue;

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
                        if (perp > perpTol) continue;
                        // On the edge line and strictly interior → hanging node unless near an endpoint.
                        double dA = Dist(p, a), dB = Dist(p, b);
                        if (dA > endTol && dB > endTol)
                            return false;
                    }
                }
        }

        return true;
    }

    private static void ComputeMixedQuality(
        IReadOnlyList<Point> nodes, IReadOnlyList<HybridRegionMesh> regions,
        out double minAngleDeg, out double maxAspectRatio)
    {
        var q = HybridMeshQuality.Evaluate(nodes, regions, HybridMeshQualityGates.Default);
        minAngleDeg = q.MinAngleDeg;
        maxAspectRatio = q.MaxAspectRatio;
    }

    private static MeshSizingField BuildDefaultSizingField(HybridMeshRequest request)
    {
        double baseSize = request.BaseSize;
        if (baseSize <= 0)
        {
            // Derive a sensible base size from the overall geometry extent (~1/25 of the diagonal).
            var bbox = BoundingBox(request);
            double diag = Math.Sqrt(bbox.W * bbox.W + bbox.H * bbox.H);
            baseSize = diag > 0 ? diag / 25.0 : 1.0;
        }

        double boundaryNear = baseSize / 3.0;
        double featureNear = baseSize / 4.0;

        var field = new MeshSizingField(baseSize, minSize: baseSize / 64.0, growthFactor: 1.3);

        foreach (var r in request.Regions)
            field.AddPolyline(r.Polygon, boundaryNear, closed: true);

        if (request.HoleOnlyRegions != null)
            foreach (var h in request.HoleOnlyRegions)
                field.AddPolyline(h.Polygon, featureNear, closed: true);

        if (request.MaterialLayerSegments != null)
            foreach (var (a, b) in request.MaterialLayerSegments)
                field.AddSegment(a, b, featureNear);

        if (request.ExtraConstraintSegments != null)
            foreach (var (a, b) in request.ExtraConstraintSegments)
                field.AddSegment(a, b, featureNear);

        return field;
    }

    private static (double X, double Y, double W, double H) BoundingBox(HybridMeshRequest request)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Acc(IReadOnlyList<Point> poly)
        {
            if (poly == null) return;
            foreach (var p in poly)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
        }
        foreach (var r in request.Regions) Acc(r.Polygon);
        if (request.HoleOnlyRegions != null)
            foreach (var h in request.HoleOnlyRegions) Acc(h.Polygon);
        if (minX > maxX) return (0, 0, 0, 0);
        return (minX, minY, maxX - minX, maxY - minY);
    }

    private static Point Bilinear(Point c0, Point c1, Point c2, Point c3, double u, double v)
    {
        // Transfinite (Coons) map reduces to bilinear for straight-sided quads.
        double x = (1 - u) * (1 - v) * c0.X + u * (1 - v) * c1.X + u * v * c2.X + (1 - u) * v * c3.X;
        double y = (1 - u) * (1 - v) * c0.Y + u * (1 - v) * c1.Y + u * v * c2.Y + (1 - u) * v * c3.Y;
        return new Point(x, y);
    }

    private static double Dist(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static void ComputeQuadQuality(
        IReadOnlyList<Point> nodes, IReadOnlyList<(int a, int b, int c, int d)> quads,
        out double minAngleDeg, out double maxAspectRatio)
    {
        minAngleDeg = 180.0;
        maxAspectRatio = 1.0;
        foreach (var q in quads)
        {
            Point[] p = { nodes[q.a], nodes[q.b], nodes[q.c], nodes[q.d] };
            double minEdge = double.MaxValue, maxEdge = 0;
            for (int i = 0; i < 4; i++)
            {
                Point a = p[i], b = p[(i + 1) % 4], prev = p[(i + 3) % 4];
                double e = Dist(a, b);
                if (e < minEdge) minEdge = e;
                if (e > maxEdge) maxEdge = e;

                double v1x = prev.X - a.X, v1y = prev.Y - a.Y;
                double v2x = b.X - a.X, v2y = b.Y - a.Y;
                double l1 = Math.Sqrt(v1x * v1x + v1y * v1y);
                double l2 = Math.Sqrt(v2x * v2x + v2y * v2y);
                if (l1 < 1e-12 || l2 < 1e-12) continue;
                double cos = (v1x * v2x + v1y * v2y) / (l1 * l2);
                double ang = Math.Acos(Math.Clamp(cos, -1.0, 1.0)) * 180.0 / Math.PI;
                if (ang < minAngleDeg) minAngleDeg = ang;
            }
            if (minEdge > 1e-12)
                maxAspectRatio = Math.Max(maxAspectRatio, maxEdge / minEdge);
        }
        if (quads.Count == 0) minAngleDeg = 0;
    }
}
