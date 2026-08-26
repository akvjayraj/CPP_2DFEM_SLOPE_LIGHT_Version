using System;
using System.Collections.Generic;
using System.Linq;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Structured serendipity Q8 mesh on a logical rectangle, mapped to geometry when a boundary polygon is supplied.
/// </summary>
public static class StructuredRectangularQ8MeshBuilder
{
    private const double BcTol = 1e-9;

    public sealed class BuildOptions
    {
        /// <summary>When set (≥3 vertices), node positions conform to this boundary (4-vertex bilinear map, else clip).</summary>
        public IReadOnlyList<(double x, double y)>? BoundaryPolygon { get; init; }

        /// <summary>When set (odd count, ascending), Q8 node rows use these Y levels instead of uniform spacing.</summary>
        public IReadOnlyList<double>? YNodeLevels { get; init; }
    }

    public static P64EmbankmentMeshBuilder.MeshResult Build(
        double xmin, double xmax, double ymin, double ymax,
        int nxe, int nye,
        BuildOptions? options = null)
        => BuildWithStats(xmin, xmax, ymin, ymax, nxe, nye, options).Mesh;

    public static StructuredMeshBuildResult BuildWithStats(
        double xmin, double xmax, double ymin, double ymax,
        int nxe, int nye,
        BuildOptions? options = null)
    {
        if (nxe < 1 || nye < 1)
            throw new ArgumentException("Element divisions must be at least 1.");
        if (xmax <= xmin || ymax <= ymin)
            throw new ArgumentException("Domain extents must be positive.");

        options ??= new BuildOptions();
        var polygon = options.BoundaryPolygon;

        if (options.YNodeLevels is { Count: >= 3 } yLevels)
        {
            if (polygon is { Count: >= 3 }
                && TryBuildBoundaryFittedMonotonePolygon(polygon, nxe, nye, yLevels, out var fittedMesh))
            {
                return new StructuredMeshBuildResult
                {
                    Mesh = fittedMesh,
                    UsedBoundaryFittedStrategy = true,
                    StrategyName = "x-monotone boundary-fitted q8",
                };
            }

            var mesh = BuildAxisAlignedBoxVariableY(xmin, xmax, yLevels, nxe);
            if (polygon is { Count: >= 3 })
            {
                var clip = ClipToPolygon(mesh, polygon);
                return new StructuredMeshBuildResult
                {
                    Mesh = clip.Mesh,
                    BoundaryFillElementCount = clip.BoundaryFillElementCount,
                    StrategyName = "clipped polygon q8 fallback",
                };
            }
            return new StructuredMeshBuildResult
            {
                Mesh = mesh,
                StrategyName = "axis-aligned q8",
            };
        }

        if (polygon is { Count: 4 })
            return new StructuredMeshBuildResult
            {
                Mesh = BuildBilinearQuad(polygon, nxe, nye),
                UsedBoundaryFittedStrategy = true,
                StrategyName = "bilinear quad map",
            };

        if (polygon is { Count: >= 3 }
            && TryBuildBoundaryFittedMonotonePolygon(polygon, nxe, nye, yNodeLevels: null, out var monotoneMesh))
        {
            return new StructuredMeshBuildResult
            {
                Mesh = monotoneMesh,
                UsedBoundaryFittedStrategy = true,
                StrategyName = "x-monotone boundary-fitted q8",
            };
        }

        var uniformMesh = BuildAxisAlignedBox(xmin, xmax, ymin, ymax, nxe, nye);
        if (polygon is { Count: >= 3 })
        {
            var clip = ClipToPolygon(uniformMesh, polygon);
            return new StructuredMeshBuildResult
            {
                Mesh = clip.Mesh,
                BoundaryFillElementCount = clip.BoundaryFillElementCount,
                StrategyName = "clipped polygon q8 fallback",
            };
        }

        return new StructuredMeshBuildResult
        {
            Mesh = uniformMesh,
            StrategyName = "axis-aligned q8",
        };
    }

    /// <summary>Structured Q8 grid with prescribed Y node levels (count = 2*nye+1).</summary>
    public static P64EmbankmentMeshBuilder.MeshResult BuildAxisAlignedBoxVariableY(
        double xmin, double xmax,
        IReadOnlyList<double> yNodeLevels,
        int nxe)
    {
        if (yNodeLevels.Count % 2 != 1)
            throw new ArgumentException("Y node levels must contain an odd count (2*nye+1).");

        int ny = yNodeLevels.Count;
        int nye = (ny - 1) / 2;
        int nx = 2 * nxe + 1;
        int nn = nx * ny;
        int nels = nxe * nye;

        double dx = (xmax - xmin) / (2 * nxe);
        int NodeId(int gi, int gj) => gi + gj * nx + 1;

        var nodes = new List<(int id, double x, double y)>(nn);
        for (int gj = 0; gj < ny; gj++)
        {
            for (int gi = 0; gi < nx; gi++)
            {
                double x = xmin + gi * dx;
                double y = yNodeLevels[gj];
                nodes.Add((NodeId(gi, gj), x, y));
            }
        }

        var elements = BuildElementConnectivity(nxe, nye, nx, NodeId);
        double ymin = yNodeLevels.Min();
        double ymax = yNodeLevels.Max();
        var bcs = BuildBoxBoundaryConditions(nodes, xmin, xmax, ymin, ymax);

        return new P64EmbankmentMeshBuilder.MeshResult
        {
            Nels = nels,
            Nn = nn,
            Nodes = nodes,
            Elements = elements,
            BoundaryConditions = bcs,
        };
    }

    /// <summary>Axis-aligned structured grid (parameter space matches physical box).</summary>
    public static P64EmbankmentMeshBuilder.MeshResult BuildAxisAlignedBox(
        double xmin, double xmax, double ymin, double ymax,
        int nxe, int nye)
    {
        int nx = 2 * nxe + 1;
        int ny = 2 * nye + 1;
        int nn = nx * ny;
        int nels = nxe * nye;

        double dx = (xmax - xmin) / (2 * nxe);
        double dy = (ymax - ymin) / (2 * nye);

        int NodeId(int gi, int gj) => gi + gj * nx + 1;

        var nodes = new List<(int id, double x, double y)>(nn);
        for (int gj = 0; gj < ny; gj++)
        {
            for (int gi = 0; gi < nx; gi++)
            {
                double x = xmin + gi * dx;
                double y = ymin + gj * dy;
                nodes.Add((NodeId(gi, gj), x, y));
            }
        }

        var elements = BuildElementConnectivity(nxe, nye, nx, NodeId);
        var bcs = BuildBoxBoundaryConditions(nodes, xmin, xmax, ymin, ymax);

        return new P64EmbankmentMeshBuilder.MeshResult
        {
            Nels = nels,
            Nn = nn,
            Nodes = nodes,
            Elements = elements,
            BoundaryConditions = bcs,
        };
    }

    /// <summary>Map a uniform ξ–η grid onto a quadrilateral (geometry boundary with four corners).</summary>
    private static P64EmbankmentMeshBuilder.MeshResult BuildBilinearQuad(
        IReadOnlyList<(double x, double y)> polygon,
        int nxe, int nye)
    {
        OrderQuadCorners(polygon, out var bl, out var br, out var tr, out var tl);

        int nx = 2 * nxe + 1;
        int ny = 2 * nye + 1;
        int nn = nx * ny;
        int nels = nxe * nye;

        int NodeId(int gi, int gj) => gi + gj * nx + 1;

        var nodes = new List<(int id, double x, double y)>(nn);
        for (int gj = 0; gj < ny; gj++)
        {
            double eta = gj / (double)(2 * nye);
            for (int gi = 0; gi < nx; gi++)
            {
                double xi = gi / (double)(2 * nxe);
                double x = Bilinear(xi, eta, bl.x, br.x, tr.x, tl.x);
                double y = Bilinear(xi, eta, bl.y, br.y, tr.y, tl.y);
                nodes.Add((NodeId(gi, gj), x, y));
            }
        }

        var elements = BuildElementConnectivity(nxe, nye, nx, NodeId);
        var poly = nodes.Select(n => (n.x, n.y)).ToList();
        var bcs = BuildPolygonBoundaryConditions(nodes, poly);

        return new P64EmbankmentMeshBuilder.MeshResult
        {
            Nels = nels,
            Nn = nn,
            Nodes = nodes,
            Elements = elements,
            BoundaryConditions = bcs,
        };
    }

    private sealed class MeshClipResult
    {
        public required P64EmbankmentMeshBuilder.MeshResult Mesh { get; init; }
        public int BoundaryFillElementCount { get; init; }
    }

    private sealed class MeshNodeRegistry
    {
        private readonly List<(double x, double y)> _coords = new();
        private readonly List<int> _ids = new();

        public MeshNodeRegistry(IReadOnlyList<(int id, double x, double y)> seedNodes)
        {
            foreach (var (_, x, y) in seedNodes)
                GetOrAdd(x, y);
        }

        public int GetOrAdd(double x, double y)
        {
            for (int i = 0; i < _coords.Count; i++)
            {
                double dx = _coords[i].x - x;
                double dy = _coords[i].y - y;
                if (dx * dx + dy * dy <= StructuredQuadPolygonClipper.NodeMergeTol * StructuredQuadPolygonClipper.NodeMergeTol)
                    return _ids[i];
            }

            int id = _ids.Count + 1;
            _coords.Add((x, y));
            _ids.Add(id);
            return id;
        }

        public int[] RegisterQ8Coords(IReadOnlyList<(double x, double y)> q8Coords)
        {
            var conn = new int[q8Coords.Count];
            for (int i = 0; i < q8Coords.Count; i++)
                conn[i] = GetOrAdd(q8Coords[i].x, q8Coords[i].y);
            return conn;
        }

        public List<(int id, double x, double y)> ToNodeList()
        {
            var nodes = new List<(int id, double x, double y)>(_coords.Count);
            for (int i = 0; i < _coords.Count; i++)
                nodes.Add((_ids[i], _coords[i].x, _coords[i].y));
            return nodes;
        }
    }

    private static MeshClipResult ClipToPolygon(
        P64EmbankmentMeshBuilder.MeshResult mesh,
        IReadOnlyList<(double x, double y)> polygon)
    {
        var nodeMap = mesh.Nodes.ToDictionary(n => n.id, n => (n.x, n.y));
        var registry = new MeshNodeRegistry(mesh.Nodes);
        var outputElements = new List<(int[] conn, int etype)>();
        int boundaryFillCount = 0;

        foreach (var (_, conn, etype) in mesh.Elements)
        {
            if (conn.Length < 8) continue;

            var corners = new (double x, double y)[]
            {
                nodeMap[conn[0]],
                nodeMap[conn[1]],
                nodeMap[conn[2]],
                nodeMap[conn[3]],
            };

            if (StructuredQuadPolygonClipper.IsFullyInside(corners, polygon))
            {
                var conn8 = new int[8];
                for (int k = 0; k < 8; k++)
                {
                    var (x, y) = nodeMap[conn[k]];
                    conn8[k] = registry.GetOrAdd(x, y);
                }
                outputElements.Add((conn8, etype));
                continue;
            }

            if (StructuredQuadPolygonClipper.IsFullyOutside(corners, polygon))
                continue;

            bool emittedTrapezium = false;
            if (StructuredQuadPolygonClipper.TryEmitBoundaryTrapezium(corners, polygon, out var trapCorners))
            {
                var q8Coords = StructuredQuadPolygonClipper.BuildStructuredQ8NodeCoordsFromCorners(trapCorners);
                outputElements.Add((registry.RegisterQ8Coords(q8Coords), etype));
                boundaryFillCount++;
                emittedTrapezium = true;
            }

            if (emittedTrapezium)
                continue;

            // Coverage fallback: multi-piece clip when trapezium build fails.
            foreach (var piece in StructuredQuadPolygonClipper.ClipQuadToPolygon(corners, polygon))
            {
                var q8Coords = StructuredQuadPolygonClipper.BuildStructuredQ8NodeCoordsFromCorners(piece);
                outputElements.Add((registry.RegisterQ8Coords(q8Coords), etype));
                boundaryFillCount++;
            }
        }

        if (outputElements.Count == 0)
            throw new InvalidOperationException("No Q8 elements lie inside the geometry boundary. Check the surface polygon or element counts.");

        var newNodes = registry.ToNodeList();
        var newElems = new List<(int id, int[] conn, int etype)>(outputElements.Count);
        for (int i = 0; i < outputElements.Count; i++)
        {
            var (conn, etype) = outputElements[i];
            newElems.Add((i + 1, conn, etype));
        }

        var clipMesh = new P64EmbankmentMeshBuilder.MeshResult
        {
            Nels = newElems.Count,
            Nn = newNodes.Count,
            Nodes = newNodes,
            Elements = newElems,
            BoundaryConditions = BuildPolygonBoundaryConditions(newNodes, polygon),
        };

        if (!StructuredQuadPolygonClipper.ValidateMeshInsidePolygon(clipMesh, polygon, out var validationError))
        {
            // Relax midside validation: corners-only pass for boundary trapeziums.
            if (!StructuredQuadPolygonClipper.ValidateMeshCornersInsidePolygon(clipMesh, polygon, out validationError))
                throw new InvalidOperationException(validationError ?? "Clipped mesh extends outside the geometry polygon.");
        }

        return new MeshClipResult
        {
            Mesh = clipMesh,
            BoundaryFillElementCount = boundaryFillCount,
        };
    }

    private static bool TryBuildBoundaryFittedMonotonePolygon(
        IReadOnlyList<(double x, double y)> polygon,
        int nxe,
        int nye,
        IReadOnlyList<double>? yNodeLevels,
        out P64EmbankmentMeshBuilder.MeshResult mesh)
    {
        mesh = default!;
        var poly = NormalizePolygon(polygon);
        if (poly.Count < 4)
            return false;

        int nx = 2 * nxe + 1;
        int ny = yNodeLevels?.Count ?? (2 * nye + 1);
        if (ny < 3 || ny % 2 == 0)
            return false;
        if (yNodeLevels != null && (ny - 1) / 2 != nye)
            return false;

        double minX = poly.Min(p => p.x);
        double maxX = poly.Max(p => p.x);
        if (maxX <= minX)
            return false;

        double minY = poly.Min(p => p.y);
        double maxY = poly.Max(p => p.y);
        if (maxY <= minY)
            return false;

        var etaLevels = BuildEtaLevels(ny, minY, maxY, yNodeLevels);
        if (etaLevels == null)
            return false;

        double tolX = Math.Max((maxX - minX) * 1e-8, 1e-8);
        var lower = new double[nx];
        var upper = new double[nx];
        for (int gi = 0; gi < nx; gi++)
        {
            double xi = gi / (double)(nx - 1);
            double x = minX + (maxX - minX) * xi;
            var ys = VerticalCrossings(poly, x);
            if (ys.Count != 2)
                return false;
            lower[gi] = ys[0];
            upper[gi] = ys[1];
            if (upper[gi] - lower[gi] <= 1e-8)
                return false;
        }

        if (Math.Abs(upper[0] - lower[0]) <= tolX || Math.Abs(upper[^1] - lower[^1]) <= tolX)
            return false;

        int NodeId(int gi, int gj) => gi + gj * nx + 1;
        var nodes = new List<(int id, double x, double y)>(nx * ny);
        for (int gj = 0; gj < ny; gj++)
        {
            double eta = etaLevels[gj];
            for (int gi = 0; gi < nx; gi++)
            {
                double xi = gi / (double)(nx - 1);
                double x = minX + (maxX - minX) * xi;
                double y = lower[gi] + (upper[gi] - lower[gi]) * eta;
                nodes.Add((NodeId(gi, gj), x, y));
            }
        }

        var elements = BuildElementConnectivity(nxe, nye, nx, NodeId);
        mesh = new P64EmbankmentMeshBuilder.MeshResult
        {
            Nels = nxe * nye,
            Nn = nodes.Count,
            Nodes = nodes,
            Elements = elements,
            BoundaryConditions = BuildPolygonBoundaryConditions(nodes, poly),
        };

        if (!StructuredQuadPolygonClipper.ValidateMeshInsidePolygon(mesh, poly, out var validationError)
            && !StructuredQuadPolygonClipper.ValidateMeshCornersInsidePolygon(mesh, poly, out validationError))
        {
            mesh = default!;
            return false;
        }

        return true;
    }

    private static List<(int id, int[] conn, int etype)> BuildElementConnectivity(
        int nxe, int nye, int nx, Func<int, int, int> nodeId)
    {
        var elements = new List<(int id, int[] conn, int etype)>(nxe * nye);
        int eid = 1;
        for (int ey = 0; ey < nye; ey++)
        {
            for (int ex = 0; ex < nxe; ex++)
            {
                int gi0 = 2 * ex;
                int gj0 = 2 * ey;
                var conn = new[]
                {
                    nodeId(gi0,     gj0),
                    nodeId(gi0 + 2, gj0),
                    nodeId(gi0 + 2, gj0 + 2),
                    nodeId(gi0,     gj0 + 2),
                    nodeId(gi0 + 1, gj0),
                    nodeId(gi0 + 2, gj0 + 1),
                    nodeId(gi0 + 1, gj0 + 2),
                    nodeId(gi0,     gj0 + 1),
                };
                elements.Add((eid++, conn, 1));
            }
        }
        return elements;
    }

    private static List<(double x, double y)> NormalizePolygon(IReadOnlyList<(double x, double y)> polygon)
    {
        var result = new List<(double x, double y)>(polygon.Count);
        foreach (var p in polygon)
        {
            if (result.Count == 0
                || Math.Abs(result[^1].x - p.x) > 1e-9
                || Math.Abs(result[^1].y - p.y) > 1e-9)
            {
                result.Add(p);
            }
        }

        if (result.Count > 1
            && Math.Abs(result[0].x - result[^1].x) <= 1e-9
            && Math.Abs(result[0].y - result[^1].y) <= 1e-9)
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    private static double[]? BuildEtaLevels(
        int nodeRowCount,
        double minY,
        double maxY,
        IReadOnlyList<double>? yNodeLevels)
    {
        if (nodeRowCount < 3 || maxY <= minY)
            return null;

        if (yNodeLevels == null)
        {
            var eta = new double[nodeRowCount];
            for (int i = 0; i < nodeRowCount; i++)
                eta[i] = i / (double)(nodeRowCount - 1);
            return eta;
        }

        if (yNodeLevels.Count != nodeRowCount)
            return null;

        double span = maxY - minY;
        var normalized = new double[nodeRowCount];
        double prev = double.NegativeInfinity;
        for (int i = 0; i < nodeRowCount; i++)
        {
            double eta = (yNodeLevels[i] - minY) / span;
            if (!double.IsFinite(eta))
                return null;
            eta = Math.Clamp(eta, 0.0, 1.0);
            if (eta + 1e-9 < prev)
                return null;
            normalized[i] = eta;
            prev = eta;
        }

        return normalized;
    }

    private static List<double> VerticalCrossings(IReadOnlyList<(double x, double y)> poly, double x)
    {
        var ys = new List<double>();
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % n];
            if (Math.Abs(a.x - b.x) < 1e-12)
            {
                if (Math.Abs(a.x - x) < 1e-10)
                {
                    ys.Add(a.y);
                    ys.Add(b.y);
                }
                continue;
            }

            double xMin = Math.Min(a.x, b.x);
            double xMax = Math.Max(a.x, b.x);
            if (x < xMin - 1e-10 || x > xMax + 1e-10)
                continue;

            double t = (x - a.x) / (b.x - a.x);
            if (t < -1e-10 || t > 1 + 1e-10)
                continue;

            ys.Add(a.y + t * (b.y - a.y));
        }

        ys.Sort();
        return MergeScalarDuplicates(ys, 1e-8);
    }

    private static List<double> MergeScalarDuplicates(IReadOnlyList<double> values, double tol)
    {
        var merged = new List<double>(values.Count);
        foreach (double value in values)
        {
            if (merged.Count == 0 || Math.Abs(value - merged[^1]) > tol)
                merged.Add(value);
        }
        return merged;
    }

    private static void OrderQuadCorners(
        IReadOnlyList<(double x, double y)> poly,
        out (double x, double y) bl, out (double x, double y) br,
        out (double x, double y) tr, out (double x, double y) tl)
    {
        var sorted = poly.OrderBy(p => p.y).ThenBy(p => p.x).ToArray();
        var bottom = sorted.Take(2).OrderBy(p => p.x).ToArray();
        var top = sorted.Skip(2).OrderBy(p => p.x).ToArray();
        bl = bottom[0];
        br = bottom[1];
        tl = top[0];
        tr = top[1];
    }

    private static double Bilinear(double xi, double eta,
        double v00, double v10, double v11, double v01) =>
        (1 - xi) * (1 - eta) * v00
        + xi * (1 - eta) * v10
        + xi * eta * v11
        + (1 - xi) * eta * v01;

    private static List<(int node, int fx, int fy)> BuildBoxBoundaryConditions(
        IReadOnlyList<(int id, double x, double y)> nodes,
        double xmin, double xmax, double ymin, double ymax) =>
        BuildPolygonBoundaryConditions(nodes, new[]
        {
            (xmin, ymin), (xmax, ymin), (xmax, ymax), (xmin, ymax),
        });

    private static List<(int node, int fx, int fy)> BuildPolygonBoundaryConditions(
        IReadOnlyList<(int id, double x, double y)> nodes,
        IReadOnlyList<(double x, double y)> polygon)
    {
        if (polygon.Count < 3)
            return new List<(int node, int fx, int fy)>();

        double minX = polygon.Min(p => p.x);
        double maxX = polygon.Max(p => p.x);
        double minY = polygon.Min(p => p.y);
        double maxY = polygon.Max(p => p.y);
        double tolX = Math.Max((maxX - minX) * 1e-6, BcTol);
        double tolY = Math.Max((maxY - minY) * 1e-6, BcTol);

        var bcs = new List<(int node, int fx, int fy)>();
        foreach (var (id, x, y) in nodes)
        {
            bool onBottom = Math.Abs(y - minY) <= tolY;
            bool onLeft = Math.Abs(x - minX) <= tolX;
            bool onRight = Math.Abs(x - maxX) <= tolX;

            if (onBottom)
                bcs.Add((id, 0, 0));
            else if (onLeft || onRight)
                bcs.Add((id, 0, 1));
        }
        return bcs;
    }

    public static (int nodes, int elements) PreviewCounts(int nxe, int nye)
    {
        if (nxe < 1 || nye < 1) return (0, 0);
        return ((2 * nxe + 1) * (2 * nye + 1), nxe * nye);
    }
}
