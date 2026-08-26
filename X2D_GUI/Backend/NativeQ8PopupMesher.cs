using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Popup-facing native Q8 meshing facade. It preserves the existing popup geometry preparation
/// contract, emits serendipity Q8 connectivity directly in its result model, and rejects any
/// geometry that would fall back to triangles or a mixed mesh inventory.
/// </summary>
public static class NativeQ8PopupMesher
{
    public sealed class Request
    {
        public IReadOnlyList<(int SurfaceNumber, IReadOnlyList<Point> Polygon, double NearSize, double GlobalSize, int Layers, double Growth)> Regions { get; init; }
            = Array.Empty<(int, IReadOnlyList<Point>, double, double, int, double)>();

        public IReadOnlyList<(int SurfaceNumber, IReadOnlyList<Point> Polygon, double NearSize)> HoleOnlyRegions { get; init; }
            = Array.Empty<(int, IReadOnlyList<Point>, double)>();

        public IReadOnlyList<IReadOnlyList<Point>> HolePolygons { get; init; }
            = Array.Empty<IReadOnlyList<Point>>();

        public IReadOnlyList<(Point A, Point B)> MaterialLayerSegments { get; init; }
            = Array.Empty<(Point A, Point B)>();

        public NonHorizontalLayerMeshPlan? NonHorizontalPlan { get; init; }

        public double GlobalSize { get; init; }

        public double NearSize { get; init; }

        public int MaxNodes { get; init; } = 40_000;

        public double CoverageThreshold { get; init; } = 0.98;
    }

    public sealed class NativeQ8RegionMesh
    {
        public int SurfaceNumber { get; init; }

        public string Reason { get; init; } = string.Empty;

        public IReadOnlyList<(int n1, int n2, int n3, int n4, int n5, int n6, int n7, int n8)> Elements { get; init; }
            = Array.Empty<(int, int, int, int, int, int, int, int)>();
    }

    public sealed class NativeQ8MeshResult
    {
        public IReadOnlyList<Point> Nodes { get; init; } = Array.Empty<Point>();

        public IReadOnlyList<NativeQ8RegionMesh> Regions { get; init; } = Array.Empty<NativeQ8RegionMesh>();

        /// <summary>Corner-only quad connectivity, retained for quality/coverage validation.</summary>
        public IReadOnlyList<(int a, int b, int c, int d)> CornerQuads { get; init; }
            = Array.Empty<(int, int, int, int)>();

        public double MinAngleDeg { get; init; }

        public double MaxAspectRatio { get; init; }

        public HybridMeshQuality Quality { get; init; } = HybridMeshQuality.Empty;

        public string Notes { get; init; } = string.Empty;

        public int RegionCount => Regions.Count;

        public int QuadCount => Regions.Sum(r => r.Elements.Count);

        public int StructuredRegionCount => Regions.Count;

        public bool IsEmpty => Nodes.Count == 0 || QuadCount == 0;

        public static NativeQ8MeshResult Empty { get; } = new();
    }

    public sealed class Result
    {
        public bool Success => string.IsNullOrWhiteSpace(ErrorMessage) && !Mesh.IsEmpty;

        public NativeQ8MeshResult Mesh { get; init; } = NativeQ8MeshResult.Empty;

        public string ErrorMessage { get; init; } = string.Empty;

        public string Diagnostics { get; init; } = string.Empty;
    }

    public static Result Generate(Request request)
    {
        if (request.Regions.Count == 0)
            return Fail("Native Q8 mesh error: no valid surface polygons found.");

        string? structuredError = null;
        if (request.NonHorizontalPlan?.HasZones != true
            && TryGenerateStructuredSingleRegion(request, out var structuredMesh, out structuredError))
        {
            return ValidateAndFinalize(request, structuredMesh, "structured native Q8 single-region mesh");
        }
        if (!string.IsNullOrWhiteSpace(structuredError))
            return Fail(structuredError);

        if (request.NonHorizontalPlan?.HasZones == true)
        {
            var exactQuadResult = NonHorizontalLayerConformingQuadMeshBuilder.Generate(
                new NonHorizontalLayerConformingQuadMeshBuilder.Request
                {
                    Regions = request.Regions
                        .Select(r => (r.SurfaceNumber, r.Polygon))
                        .ToList(),
                    HolePolygons = request.HolePolygons,
                    LayerPlan = request.NonHorizontalPlan,
                    GlobalSize = request.GlobalSize,
                    MaxNodes = request.MaxNodes,
                });

            if (exactQuadResult.IsEmpty)
            {
                return Fail(
                    "Native Q8 mesh error: exact non-horizontal quad mesher produced no elements. " +
                    "Try a larger Global Mesh Size or simplify the inclined material-layer geometry.");
            }

            return FinalizeFromHybridQuadSource(
                request,
                exactQuadResult,
                "non-horizontal conforming quad source");
        }

        var quadResults = GradedSurfaceQuadGenerator.GenerateConforming(
            request.Regions,
            request.HoleOnlyRegions.Count > 0 ? request.HoleOnlyRegions : null,
            request.MaterialLayerSegments.Count > 0 ? request.MaterialLayerSegments : null,
            pinnedBoundaryNodes: null,
            maxNodes: request.MaxNodes);

        if (quadResults == null || quadResults.Count == 0)
        {
            return Fail(
                "Native Q8 mesh error: quad-native generation produced no elements. " +
                "Check that surfaces are closed polygons sized relative to the mesh settings.");
        }

        IReadOnlyList<Point> sharedNodes = quadResults[0].Mesh.Nodes;
        var regions = new List<HybridRegionMesh>(quadResults.Count);
        foreach (var (surfaceNumber, mesh) in quadResults)
        {
            if (mesh.Quads.Count == 0 && mesh.Triangles.Count == 0)
                continue;

            regions.Add(new HybridRegionMesh
            {
                SurfaceNumber = surfaceNumber,
                Strategy = RegionMeshStrategy.StructuredTransfinite,
                Quads = mesh.Quads,
                Triangles = mesh.Triangles,
                Reason = "popup native Q8 sweep source",
            });
        }

        if (regions.Count == 0)
            return Fail("Native Q8 mesh error: quad-native generation produced no usable regions.");

        var quality = HybridMeshQuality.Evaluate(sharedNodes, regions);
        return FinalizeFromHybridQuadSource(
            request,
            new HybridMeshResult
            {
                Nodes = sharedNodes,
                Regions = regions,
                MinAngleDeg = quality.MinAngleDeg,
                MaxAspectRatio = quality.MaxAspectRatio,
                Quality = quality,
                StitchApplied = false,
                Notes = "popup native Q8 sweep source",
            },
            "shared-grid quad sweep source");
    }

    private static Result FinalizeFromHybridQuadSource(
        Request request,
        HybridMeshResult source,
        string diagnostics)
    {
        if (source.IsEmpty)
            return Fail("Native Q8 mesh error: Q8 source generation produced no elements.");

        if (source.TriangleCount > 0)
        {
            return Fail(
                $"Native Q8 mesh error: source generation still requires {source.TriangleCount} triangle fallback(s). " +
                "Increase mesh size, simplify the local geometry, or use the triangle mesh path.");
        }

        var baseNodes = source.Nodes as List<Point> ?? new List<Point>(source.Nodes);
        var sourceQ4 = source.Regions.SelectMany(r => r.Quads).ToList();
        if (sourceQ4.Count == 0)
            return Fail("Native Q8 mesh error: Q8 source generation produced no quadrilateral elements.");

        double tol = Math.Max(request.NearSize * 0.1, 1e-6);
        if (GradedSurfaceQuadGenerator.HasHangingNode(baseNodes, sourceQ4, tol))
        {
            return Fail(
                "Native Q8 mesh error: the quad source contains hanging corner nodes and is not FE-compatible. " +
                "Adjust the mesh settings or simplify the geometry.");
        }

        var nodes = new List<Point>(baseNodes);
        var q8All = EmitSerendipityQ8(nodes, sourceQ4);
        var q8Regions = new List<NativeQ8RegionMesh>(source.Regions.Count);
        int cursor = 0;
        foreach (var region in source.Regions)
        {
            if (region.Quads.Count == 0)
                continue;

            var elems = new List<(int, int, int, int, int, int, int, int)>(region.Quads.Count);
            for (int i = 0; i < region.Quads.Count; i++)
                elems.Add(q8All[cursor + i]);
            cursor += region.Quads.Count;

            q8Regions.Add(new NativeQ8RegionMesh
            {
                SurfaceNumber = region.SurfaceNumber,
                Reason = region.Reason,
                Elements = elems,
            });
        }

        var q4QualityRegions = source.Regions
            .Where(r => r.Quads.Count > 0)
            .Select(r => new HybridRegionMesh
            {
                SurfaceNumber = r.SurfaceNumber,
                Strategy = r.Strategy,
                Quads = r.Quads,
                Triangles = Array.Empty<(int, int, int)>(),
                Reason = r.Reason,
            })
            .ToList();
        var quality = HybridMeshQuality.Evaluate(baseNodes, q4QualityRegions);

        return ValidateAndFinalize(
            request,
            new NativeQ8MeshResult
            {
                Nodes = nodes,
                Regions = q8Regions,
                CornerQuads = sourceQ4,
                MinAngleDeg = quality.MinAngleDeg,
                MaxAspectRatio = quality.MaxAspectRatio,
                Quality = quality,
                Notes = string.IsNullOrWhiteSpace(source.Notes) ? diagnostics : $"{source.Notes}; {diagnostics}",
            },
            diagnostics);
    }

    private static bool TryGenerateStructuredSingleRegion(
        Request request,
        out NativeQ8MeshResult mesh,
        out string? error)
    {
        mesh = NativeQ8MeshResult.Empty;
        error = null;

        if (request.Regions.Count != 1 || request.HolePolygons.Count > 0)
            return false;

        var (surfaceNumber, polygon, _, globalSize, _, _) = request.Regions[0];
        if (polygon == null || polygon.Count < 3)
            return false;

        double minX = polygon.Min(p => p.X);
        double maxX = polygon.Max(p => p.X);
        double minY = polygon.Min(p => p.Y);
        double maxY = polygon.Max(p => p.Y);
        double width = maxX - minX;
        double height = maxY - minY;
        if (width <= 1e-9 || height <= 1e-9)
            return false;

        int nxe = Math.Clamp((int)Math.Ceiling(width / Math.Max(globalSize, 1e-6)), 1, 400);
        int nye = Math.Clamp((int)Math.Ceiling(height / Math.Max(globalSize, 1e-6)), 1, 400);

        try
        {
            var stats = StructuredRectangularQ8MeshBuilder.BuildWithStats(
                minX,
                maxX,
                minY,
                maxY,
                nxe,
                nye,
                new StructuredRectangularQ8MeshBuilder.BuildOptions
                {
                    BoundaryPolygon = polygon.Select(p => (p.X, p.Y)).ToList(),
                });

            if (polygon.Count > 4 && !stats.UsedBoundaryFittedStrategy)
            {
                error =
                    "Native Q8 mesh error: this boundary shape is outside the supported slope-edge Q8 fitting class. " +
                    "Use a monotone single-region slope outline for native Q8 edge fitting, or switch to the current Q4/triangle path.";
                mesh = NativeQ8MeshResult.Empty;
                return false;
            }

            var orderedNodes = stats.Mesh.Nodes.OrderBy(n => n.id).ToList();
            var idToIndex = new Dictionary<int, int>(orderedNodes.Count);
            var points = new List<Point>(orderedNodes.Count);
            for (int i = 0; i < orderedNodes.Count; i++)
            {
                var (id, x, y) = orderedNodes[i];
                idToIndex[id] = i;
                points.Add(new Point(x, y));
            }

            var regionElems = new List<(int, int, int, int, int, int, int, int)>(stats.Mesh.Elements.Count);
            var cornerQuads = new List<(int, int, int, int)>(stats.Mesh.Elements.Count);
            foreach (var (_, conn, _) in stats.Mesh.Elements)
            {
                if (conn.Length < 8)
                    continue;
                if (!TryMapQ8(conn, idToIndex, out var mapped))
                    continue;

                regionElems.Add(mapped);
                cornerQuads.Add((mapped.n1, mapped.n2, mapped.n3, mapped.n4));
            }

            if (regionElems.Count == 0)
                return false;

            var qualityRegions = new[]
            {
                new HybridRegionMesh
                {
                    SurfaceNumber = surfaceNumber,
                    Strategy = RegionMeshStrategy.StructuredTransfinite,
                    Quads = cornerQuads,
                    Triangles = Array.Empty<(int, int, int)>(),
                    Reason = "structured native Q8 single-region mesh",
                }
            };
            var quality = HybridMeshQuality.Evaluate(points, qualityRegions);
            mesh = new NativeQ8MeshResult
            {
                Nodes = points,
                Regions = new[]
                {
                    new NativeQ8RegionMesh
                    {
                        SurfaceNumber = surfaceNumber,
                        Reason = string.IsNullOrWhiteSpace(stats.StrategyName)
                            ? "structured native Q8 single-region mesh"
                            : stats.StrategyName,
                        Elements = regionElems,
                    }
                },
                CornerQuads = cornerQuads,
                MinAngleDeg = quality.MinAngleDeg,
                MaxAspectRatio = quality.MaxAspectRatio,
                Quality = quality,
                Notes = string.IsNullOrWhiteSpace(stats.StrategyName)
                    ? "structured native Q8 single-region mesh"
                    : stats.StrategyName,
            };
            return true;
        }
        catch
        {
            mesh = NativeQ8MeshResult.Empty;
            error = null;
            return false;
        }
    }

    private static bool TryMapQ8(
        IReadOnlyList<int> conn,
        IReadOnlyDictionary<int, int> idToIndex,
        out (int n1, int n2, int n3, int n4, int n5, int n6, int n7, int n8) mapped)
    {
        mapped = default;
        if (conn.Count < 8)
            return false;

        if (!idToIndex.TryGetValue(conn[0], out var n1)
            || !idToIndex.TryGetValue(conn[1], out var n2)
            || !idToIndex.TryGetValue(conn[2], out var n3)
            || !idToIndex.TryGetValue(conn[3], out var n4)
            || !idToIndex.TryGetValue(conn[4], out var n5)
            || !idToIndex.TryGetValue(conn[5], out var n6)
            || !idToIndex.TryGetValue(conn[6], out var n7)
            || !idToIndex.TryGetValue(conn[7], out var n8))
        {
            return false;
        }

        mapped = (n1, n2, n3, n4, n5, n6, n7, n8);
        return true;
    }

    private static List<(int n1, int n2, int n3, int n4, int n5, int n6, int n7, int n8)> EmitSerendipityQ8(
        List<Point> nodes,
        IReadOnlyList<(int a, int b, int c, int d)> q4Quads)
    {
        var edgeToMid = new Dictionary<(int lo, int hi), int>(q4Quads.Count * 4);
        var q8 = new List<(int, int, int, int, int, int, int, int)>(q4Quads.Count);

        int GetOrAddMid(int i, int j)
        {
            var key = i < j ? (i, j) : (j, i);
            if (edgeToMid.TryGetValue(key, out var mid))
                return mid;

            mid = nodes.Count;
            nodes.Add(new Point((nodes[i].X + nodes[j].X) * 0.5, (nodes[i].Y + nodes[j].Y) * 0.5));
            edgeToMid[key] = mid;
            return mid;
        }

        foreach (var (a, b, c, d) in q4Quads)
        {
            int n5 = GetOrAddMid(a, b);
            int n6 = GetOrAddMid(b, c);
            int n7 = GetOrAddMid(c, d);
            int n8 = GetOrAddMid(d, a);
            q8.Add((a, b, c, d, n5, n6, n7, n8));
        }

        return q8;
    }

    private static Result ValidateAndFinalize(
        Request request,
        NativeQ8MeshResult mesh,
        string diagnostics)
    {
        if (mesh.IsEmpty)
            return Fail("Native Q8 mesh error: native Q8 generation produced no elements.");

        double netArea = 0.0;
        foreach (var (_, poly, _, _, _, _) in request.Regions)
            netArea += PolygonArea(poly);
        foreach (var hole in request.HolePolygons)
            netArea -= PolygonArea(hole);

        double meshedArea = 0.0;
        foreach (var (a, b, c, d) in mesh.CornerQuads)
            meshedArea += QuadAreaAbs(mesh.Nodes[a], mesh.Nodes[b], mesh.Nodes[c], mesh.Nodes[d]);

        double coverage = netArea > 1e-9 ? meshedArea / netArea : 0.0;
        if (coverage < request.CoverageThreshold)
        {
            return Fail(
                $"Native Q8 mesh error: coverage is only {coverage:P0} for the selected geometry. " +
                "Increase the element size, reduce local complexity, or use the triangle mesh path.");
        }

        return new Result
        {
            Mesh = new NativeQ8MeshResult
            {
                Nodes = mesh.Nodes,
                Regions = mesh.Regions,
                CornerQuads = mesh.CornerQuads,
                MinAngleDeg = mesh.MinAngleDeg,
                MaxAspectRatio = mesh.MaxAspectRatio,
                Quality = mesh.Quality,
                Notes = string.IsNullOrWhiteSpace(mesh.Notes) ? diagnostics : mesh.Notes,
            },
            Diagnostics = $"Native Q8 path: {diagnostics}. Coverage {coverage:P0}.",
        };
    }

    private static Result Fail(string message) =>
        new()
        {
            ErrorMessage = message,
        };

    private static double PolygonArea(IReadOnlyList<Point> poly)
    {
        if (poly == null || poly.Count < 3)
            return 0.0;
        double area = 0.0;
        for (int i = 0; i < poly.Count; i++)
        {
            var p0 = poly[i];
            var p1 = poly[(i + 1) % poly.Count];
            area += p0.X * p1.Y - p1.X * p0.Y;
        }
        return Math.Abs(area) * 0.5;
    }

    private static double QuadAreaAbs(Point p1, Point p2, Point p3, Point p4)
    {
        double area = 0.0;
        area += p1.X * p2.Y - p2.X * p1.Y;
        area += p2.X * p3.Y - p3.X * p2.Y;
        area += p3.X * p4.Y - p4.X * p3.Y;
        area += p4.X * p1.Y - p1.X * p4.Y;
        return Math.Abs(area) * 0.5;
    }
}
