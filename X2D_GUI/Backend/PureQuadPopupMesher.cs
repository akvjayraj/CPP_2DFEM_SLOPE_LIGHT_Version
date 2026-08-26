using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Popup-facing pure-quad meshing facade. It reuses the existing quad-native builders and
/// rejects any result that still requires triangle fallbacks or leaves obvious FE-invalid gaps.
/// </summary>
public static class PureQuadPopupMesher
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

    public sealed class Result
    {
        public bool Success => string.IsNullOrWhiteSpace(ErrorMessage) && !Mesh.IsEmpty;

        public HybridMeshResult Mesh { get; init; } = HybridMeshResult.Empty;

        public string ErrorMessage { get; init; } = string.Empty;

        public string Diagnostics { get; init; } = string.Empty;
    }

    public static Result Generate(Request request)
    {
        if (request.Regions.Count == 0)
            return Fail("Pure quad mesh error: no valid surface polygons found.");

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
                    "Pure quad mesh error: exact non-horizontal quad mesher produced no elements. " +
                    "Try a larger Global Mesh Size or simplify the inclined material-layer geometry.");
            }

            return ValidateAndFinalize(
                request,
                exactQuadResult,
                "quad-only non-horizontal conforming mesh");
        }

        if (TryGenerateStructuredSingleRegion(request, out var structuredMesh))
        {
            var structured = ValidateAndFinalize(
                request,
                structuredMesh,
                "structured polygon-clipped quad mesh");
            if (structured.Success)
                return structured;
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
                "Pure quad mesh error: quad-native generation produced no elements. " +
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
                Reason = "popup pure quad sweep",
            });
        }

        if (regions.Count == 0)
            return Fail("Pure quad mesh error: quad-native generation produced no usable regions.");

        var quality = HybridMeshQuality.Evaluate(sharedNodes, regions);
        return ValidateAndFinalize(
            request,
            new HybridMeshResult
            {
                Nodes = sharedNodes,
                Regions = regions,
                MinAngleDeg = quality.MinAngleDeg,
                MaxAspectRatio = quality.MaxAspectRatio,
                Quality = quality,
                StitchApplied = false,
                Notes = "popup pure quad sweep",
            },
            "shared-grid quad sweep");
    }

    private static bool TryGenerateStructuredSingleRegion(Request request, out HybridMeshResult mesh)
    {
        mesh = HybridMeshResult.Empty;

        if (request.Regions.Count != 1 || request.HolePolygons.Count > 0)
            return false;

        var (_, polygon, _, globalSize, _, _) = request.Regions[0];
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

            mesh = ToHybridResult(
                stats.Mesh,
                request.Regions[0].SurfaceNumber,
                "structured single-region fallback");
            return !mesh.IsEmpty;
        }
        catch
        {
            mesh = HybridMeshResult.Empty;
            return false;
        }
    }

    private static HybridMeshResult ToHybridResult(
        P64EmbankmentMeshBuilder.MeshResult mesh,
        int surfaceNumber,
        string note)
    {
        if (mesh.Nodes.Count == 0 || mesh.Elements.Count == 0)
            return HybridMeshResult.Empty;

        var orderedNodes = mesh.Nodes.OrderBy(n => n.id).ToList();
        var idToIndex = new Dictionary<int, int>(orderedNodes.Count);
        var points = new List<Point>(orderedNodes.Count);
        for (int i = 0; i < orderedNodes.Count; i++)
        {
            var (id, x, y) = orderedNodes[i];
            idToIndex[id] = i;
            points.Add(new Point(x, y));
        }

        var quads = new List<(int a, int b, int c, int d)>(mesh.Elements.Count);
        foreach (var (_, conn, _) in mesh.Elements)
        {
            if (conn.Length < 4)
                continue;
            if (!idToIndex.TryGetValue(conn[0], out var a)
                || !idToIndex.TryGetValue(conn[1], out var b)
                || !idToIndex.TryGetValue(conn[2], out var c)
                || !idToIndex.TryGetValue(conn[3], out var d))
            {
                continue;
            }
            quads.Add((a, b, c, d));
        }

        if (quads.Count == 0)
            return HybridMeshResult.Empty;

        var region = new HybridRegionMesh
        {
            SurfaceNumber = surfaceNumber,
            Strategy = RegionMeshStrategy.StructuredTransfinite,
            Quads = quads,
            Triangles = Array.Empty<(int a, int b, int c)>(),
            Reason = note,
        };
        var quality = HybridMeshQuality.Evaluate(points, new[] { region });
        return new HybridMeshResult
        {
            Nodes = points,
            Regions = new[] { region },
            MinAngleDeg = quality.MinAngleDeg,
            MaxAspectRatio = quality.MaxAspectRatio,
            Quality = quality,
            StitchApplied = false,
            Notes = note,
        };
    }

    private static Result ValidateAndFinalize(Request request, HybridMeshResult mesh, string diagnostics)
    {
        if (mesh.IsEmpty)
            return Fail("Pure quad mesh error: quad-native generation produced no elements.");

        if (mesh.TriangleCount > 0)
        {
            return Fail(
                $"Pure quad mesh error: quad-native generation still requires {mesh.TriangleCount} triangle fallback(s). " +
                "Increase mesh size, simplify the local geometry, or use the triangle mesh path for this case.");
        }

        var allQuads = mesh.Regions.SelectMany(r => r.Quads).ToList();
        double tol = Math.Max(request.NearSize * 0.1, 1e-6);
        if (GradedSurfaceQuadGenerator.HasHangingNode(mesh.Nodes, allQuads, tol))
        {
            return Fail(
                "Pure quad mesh error: the generated mesh contains hanging nodes and is not FE-compatible. " +
                "Adjust the mesh settings or simplify the geometry.");
        }

        double netArea = 0.0;
        foreach (var (_, poly, _, _, _, _) in request.Regions)
            netArea += PolygonArea(poly);
        foreach (var hole in request.HolePolygons)
            netArea -= PolygonArea(hole);

        double meshedArea = 0.0;
        foreach (var (a, b, c, d) in allQuads)
            meshedArea += QuadAreaAbs(mesh.Nodes[a], mesh.Nodes[b], mesh.Nodes[c], mesh.Nodes[d]);

        double coverage = netArea > 1e-9 ? meshedArea / netArea : 0.0;
        if (coverage < request.CoverageThreshold)
        {
            return Fail(
                $"Pure quad mesh error: coverage is only {coverage:P0} for the selected geometry. " +
                "Increase the element size, reduce local complexity, or use the triangle mesh path.");
        }

        var quality = mesh.Quality.EvaluatedElements > 0
            ? mesh.Quality
            : HybridMeshQuality.Evaluate(mesh.Nodes, mesh.Regions);

        return new Result
        {
            Mesh = new HybridMeshResult
            {
                Nodes = mesh.Nodes,
                Regions = mesh.Regions,
                MinAngleDeg = quality.MinAngleDeg,
                MaxAspectRatio = quality.MaxAspectRatio,
                Quality = quality,
                StitchApplied = mesh.StitchApplied,
                Notes = string.IsNullOrWhiteSpace(mesh.Notes)
                    ? diagnostics
                    : $"{mesh.Notes}; {diagnostics}",
            },
            Diagnostics = $"Pure quad path: {diagnostics}. Coverage {coverage:P0}.",
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
