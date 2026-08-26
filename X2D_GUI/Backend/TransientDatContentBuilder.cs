using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Builds Chapter-8 transient consolidation .dat decks:
///   p84 — plane/axisymmetric structured Q4 (geom_rect),
///   p88 — general 2D explicit mesh (homogeneous T3 / T6 / Q4 / Q8).
/// When "Transient (plane/axisymmetric)" is selected but the mesh is not a structured
/// rectangular Q4 grid, the builder automatically emits a p88 deck so all mesh types run.
/// </summary>
public static class TransientDatContentBuilder
{
    public static string? ResolveProgramId(string? detailedType) => detailedType switch
    {
        MaterialPropertiesViewModel.GroundwaterTransientPlaneAxisymType => "p84",
        MaterialPropertiesViewModel.GroundwaterTransientGeneral2DType => "p88",
        _ => null
    };

    /// <summary>
    /// Chooses the runtime solver from the selected type and the actual mesh:
    /// structured Q4 + p84 type → p84; otherwise any supported homogeneous mesh → p88.
    /// </summary>
    public static string ResolveRuntimeProgramId(
        string detailedType,
        MeshViewModel meshVM,
        TransientAnalysisSettings settings)
    {
        var preferred = ResolveProgramId(detailedType)
            ?? throw new InvalidOperationException($"'{detailedType}' is not a transient consolidation analysis type.");

        if (preferred == "p88")
            return "p88";

        if (TryDetectStructuredQ4(meshVM, out _))
            return "p84";

        if (settings.Axisymmetric)
        {
            throw new InvalidOperationException(
                "Axisymmetric transient consolidation (p84) requires a structured rectangular Q4 mesh. " +
                "Turn off Axisymmetric and use General 2D consolidation (p88) for T3/T6/Q4/Q8 meshes, " +
                "or regenerate a structured rectangular Q4 mesh.");
        }

        // Non-structured mesh under the p84 UI option → run general p88.
        return "p88";
    }

    public static string Build(
        string detailedType,
        MeshViewModel meshVM,
        MaterialPropertiesViewModel materialsVM,
        TransientAnalysisSettings settings,
        IReadOnlyCollection<TransientFixedPressureNode>? explicitFixedNodes = null)
        => BuildWithProgramId(
            detailedType, meshVM, materialsVM, settings, explicitFixedNodes).DatContent;

    public static (string ProgramId, string DatContent) BuildWithProgramId(
        string detailedType,
        MeshViewModel meshVM,
        MaterialPropertiesViewModel materialsVM,
        TransientAnalysisSettings settings,
        IReadOnlyCollection<TransientFixedPressureNode>? explicitFixedNodes = null)
    {
        var programId = ResolveRuntimeProgramId(detailedType, meshVM, settings);
        var dat = programId == "p84"
            ? BuildP84(meshVM, materialsVM, settings, explicitFixedNodes)
            : BuildP88(meshVM, materialsVM, settings, explicitFixedNodes);
        return (programId, dat);
    }

    private static string BuildP84(
        MeshViewModel meshVM,
        MaterialPropertiesViewModel materialsVM,
        TransientAnalysisSettings settings,
        IReadOnlyCollection<TransientFixedPressureNode>? explicitFixedNodes)
    {
        if (!TryDetectStructuredQ4(meshVM, out var grid) || grid == null)
        {
            throw new InvalidOperationException(
                "Transient (plane/axisymmetric) (p84) requires a structured rectangular Q4 mesh.");
        }

        int nxe = grid.Xs.Count - 1, nye = grid.Ys.Count - 1;
        int cols = nxe + 1, rows = nye + 1, nn = cols * rows;
        var (kx, ky, npTypes) = CollectPermeabilities(materialsVM);

        // geom_rect dir='x': node = row*(nxe+1) + col + 1, row 0 = first y in the deck (top).
        var nodeCoords = new Dictionary<int, (double X, double Y)>();
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
            nodeCoords[r * cols + c + 1] = (grid.Xs[c], grid.YsTopDown[r]);

        var fixedNodes = CollectFixedPressures(nodeCoords, settings, explicitFixedNodes);
        var loads = BuildInitialLoads(nn, nodeCoords, settings, fixedNodes);

        var sb = new StringBuilder();
        sb.AppendLine("p84");
        sb.AppendLine("! Transient consolidation — plane/axisymmetric (S&G Program 8.4, Q4 structured grid)");
        sb.AppendLine($"'{(settings.Axisymmetric ? "axisymmetric" : "plane")}' '{(settings.Axisymmetric ? "r" : "x")}'");
        sb.AppendLine($"{nxe} {nye} {npTypes}");
        AppendPermeabilityRows(sb, kx, ky, npTypes);
        if (npTypes > 1)
            AppendStructuredEtypes(sb, grid, meshVM, npTypes);
        sb.AppendLine("! x_coords then y_coords (top -> bottom)");
        sb.AppendLine(string.Join("  ", grid.Xs.Select(F)));
        sb.AppendLine(string.Join("  ", grid.YsTopDown.Select(F)));
        sb.AppendLine("! dtim nstep theta npri nres ntime");
        sb.AppendLine($"{F(settings.TimeStep)} {settings.NumberOfSteps} {F(settings.Theta)} " +
                      $"{settings.PrintInterval} {ClampMonitor(settings.MonitorNode, nn)} {settings.ContourStep}");
        sb.AppendLine("! initial nodal pressures");
        foreach (var chunk in Chunk(loads, cols))
            sb.AppendLine(string.Join("  ", chunk.Select(F)));
        sb.AppendLine("! fixed freedoms");
        sb.AppendLine(fixedNodes.Count.ToString(CultureInfo.InvariantCulture));
        if (fixedNodes.Count > 0)
        {
            foreach (var chunk in Chunk(fixedNodes.OrderBy(kv => kv.Key).ToList(), 6))
                sb.AppendLine(string.Join("   ", chunk.Select(kv => $"{kv.Key} {F(kv.Value)}")));
        }
        sb.AppendLine("! contour intervals (read at ntime)");
        sb.AppendLine(settings.ContourIntervals.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static string BuildP88(
        MeshViewModel meshVM,
        MaterialPropertiesViewModel materialsVM,
        TransientAnalysisSettings settings,
        IReadOnlyCollection<TransientFixedPressureNode>? explicitFixedNodes)
    {
        var rows = RequireMeshRows(meshVM);
        var nodeCoords = CollectNodeCoords(rows);
        int nn = nodeCoords.Keys.Max();
        var kind = DetectHomogeneousMesh(rows);
        var (element, nod, nip) = kind switch
        {
            MeshKind.T3 => ("triangle", 3, 1),
            MeshKind.T6 => ("triangle", 6, 3),
            MeshKind.Q4 => ("quadrilateral", 4, 4),
            MeshKind.Q8 => ("quadrilateral", 8, 4),
            _ => throw new InvalidOperationException(
                "Transient consolidation requires a homogeneous T3, T6, Q4, or Q8 mesh.")
        };
        var (kx, ky, npTypes) = CollectPermeabilities(materialsVM);
        var fixedNodes = CollectFixedPressures(nodeCoords, settings, explicitFixedNodes);
        var loads = BuildInitialLoads(nn, nodeCoords, settings, fixedNodes);
        bool writeContourMeta = kind == MeshKind.Q4;

        var sb = new StringBuilder();
        sb.AppendLine("p88");
        sb.AppendLine($"! Transient consolidation — General 2D (S&G Program 8.8, explicit {kind} mesh)");
        // Fortran p88 header: element, nels, nn, nip, nod, ndim, np_types
        sb.AppendLine($"'{element}' {rows.Count} {nn} {nip} {nod} 2 {npTypes}");
        AppendPermeabilityRows(sb, kx, ky, npTypes);
        if (npTypes > 1)
        {
            sb.AppendLine("! etype");
            int col = 0;
            foreach (var r in rows)
            {
                sb.Append($"{Math.Clamp(r.Etype < 1 ? 1 : r.Etype, 1, npTypes),3}");
                if (++col % 20 == 0) sb.AppendLine();
            }
            if (col % 20 != 0) sb.AppendLine();
        }
        sb.AppendLine("! nodal coordinates x y");
        for (int id = 1; id <= nn; id++)
        {
            var (x, y) = nodeCoords.TryGetValue(id, out var pt) ? pt : (0.0, 0.0);
            sb.AppendLine($"{F(x)}  {F(y)}");
        }
        sb.AppendLine("! connectivity (oriented for positive Jacobian)");
        foreach (var r in rows)
            sb.AppendLine(string.Join("  ", FormatConnectivity(r, kind, nodeCoords)));
        sb.AppendLine("! dtim nstep theta npri nres");
        sb.AppendLine($"{F(settings.TimeStep)} {settings.NumberOfSteps} {F(settings.Theta)} " +
                      $"{settings.PrintInterval} {ClampMonitor(settings.MonitorNode, nn)}");
        if (writeContourMeta)
        {
            sb.AppendLine("! ntime nci (2D Q4 only)");
            sb.AppendLine($"{settings.ContourStep} {settings.ContourIntervals}");
        }
        sb.AppendLine("! initial nodal pressures");
        foreach (var chunk in Chunk(loads, 8))
            sb.AppendLine(string.Join("  ", chunk.Select(F)));
        sb.AppendLine("! fixed freedoms");
        sb.AppendLine(fixedNodes.Count.ToString(CultureInfo.InvariantCulture));
        if (fixedNodes.Count > 0)
        {
            foreach (var chunk in Chunk(fixedNodes.OrderBy(kv => kv.Key).ToList(), 6))
                sb.AppendLine(string.Join("   ", chunk.Select(kv => $"{kv.Key} {F(kv.Value)}")));
        }
        return sb.ToString();
    }

    private static List<double> BuildInitialLoads(
        int nn,
        IReadOnlyDictionary<int, (double X, double Y)> nodeCoords,
        TransientAnalysisSettings settings,
        IReadOnlyDictionary<int, double> fixedNodes)
    {
        var loads = new List<double>(nn);
        for (int id = 1; id <= nn; id++)
        {
            if (!nodeCoords.ContainsKey(id))
            {
                loads.Add(settings.InitialPressure);
                continue;
            }
            loads.Add(fixedNodes.TryGetValue(id, out var fixedPressure)
                ? fixedPressure
                : settings.InitialPressure);
        }
        return loads;
    }

    private static HashSet<int> CollectFixedFaceNodes(
        IReadOnlyDictionary<int, (double X, double Y)> nodeCoords,
        TransientAnalysisSettings settings)
    {
        if (nodeCoords.Count == 0) return new HashSet<int>();
        double minX = nodeCoords.Values.Min(p => p.X);
        double maxX = nodeCoords.Values.Max(p => p.X);
        double minY = nodeCoords.Values.Min(p => p.Y);
        double maxY = nodeCoords.Values.Max(p => p.Y);
        double tolX = Math.Max(1e-9, (maxX - minX) * 1e-6);
        double tolY = Math.Max(1e-9, (maxY - minY) * 1e-6);
        var ids = new HashSet<int>();
        foreach (var (id, pt) in nodeCoords)
        {
            if (settings.FixMinXFace && Math.Abs(pt.X - minX) <= tolX) ids.Add(id);
            if (settings.FixMaxXFace && Math.Abs(pt.X - maxX) <= tolX) ids.Add(id);
            if (settings.FixBottomFace && Math.Abs(pt.Y - minY) <= tolY) ids.Add(id);
            if (settings.FixTopFace && Math.Abs(pt.Y - maxY) <= tolY) ids.Add(id);
        }
        return ids;
    }

    private static Dictionary<int, double> CollectFixedPressures(
        IReadOnlyDictionary<int, (double X, double Y)> nodeCoords,
        TransientAnalysisSettings settings,
        IReadOnlyCollection<TransientFixedPressureNode>? explicitFixedNodes)
    {
        var result = CollectFixedFaceNodes(nodeCoords, settings)
            .ToDictionary(id => id, _ => settings.FixedPressure);
        if (explicitFixedNodes == null || nodeCoords.Count == 0)
            return result;

        double minX = nodeCoords.Values.Min(p => p.X);
        double maxX = nodeCoords.Values.Max(p => p.X);
        double minY = nodeCoords.Values.Min(p => p.Y);
        double maxY = nodeCoords.Values.Max(p => p.Y);
        double tolX = Math.Max(1e-9, (maxX - minX) * 1e-6);
        double tolY = Math.Max(1e-9, (maxY - minY) * 1e-6);
        double baseTol = Math.Max(tolX, tolY);

        void Assign(int id, double pressure)
        {
            if (id > 0 && nodeCoords.ContainsKey(id))
                result[id] = pressure;
        }

        foreach (var row in explicitFixedNodes.Where(n => n.IsEnabled))
        {
            if (row.IsUserDefined && !row.HasPlacement)
            {
                foreach (var (id, pt) in nodeCoords)
                {
                    bool match = row.Face switch
                    {
                        TransientDrainFace.Top => Math.Abs(pt.Y - maxY) <= tolY,
                        TransientDrainFace.Bottom => Math.Abs(pt.Y - minY) <= tolY,
                        TransientDrainFace.MinX => Math.Abs(pt.X - minX) <= tolX,
                        TransientDrainFace.MaxX => Math.Abs(pt.X - maxX) <= tolX,
                        _ => false
                    };
                    if (match) Assign(id, row.Pressure);
                }
                continue;
            }

            if (!row.HasPlacement && row.NodeId <= 0) continue;

            if (row.PlacementType == WaterHeadPlacementType.Line && row.HasPlacement)
            {
                foreach (var id in CollectNodesAlongSegment(
                             nodeCoords, row.StartX, row.StartY, row.EndX, row.EndY, baseTol))
                    Assign(id, row.Pressure);
                continue;
            }

            // Point / typed Node ID — coordinates are authoritative for p84 renumbering.
            int resolvedId = FindNearestNodeId(nodeCoords, row.StartX, row.StartY);
            if (resolvedId <= 0 && nodeCoords.ContainsKey(row.NodeId))
                resolvedId = row.NodeId;
            Assign(resolvedId, row.Pressure);
        }
        return result;
    }

    private static List<int> CollectNodesAlongSegment(
        IReadOnlyDictionary<int, (double X, double Y)> nodeCoords,
        double x0, double y0, double x1, double y1,
        double baseTol)
    {
        var ids = new List<int>();
        double sx = x1 - x0, sy = y1 - y0;
        double len2 = sx * sx + sy * sy;
        if (len2 < 1e-24)
        {
            var id = FindNearestNodeId(nodeCoords, x0, y0);
            if (id > 0) ids.Add(id);
            return ids;
        }

        double len = Math.Sqrt(len2);
        double band = Math.Max(baseTol * 10, len * 0.02);
        foreach (var (id, pt) in nodeCoords)
        {
            double t = ((pt.X - x0) * sx + (pt.Y - y0) * sy) / len2;
            if (t < -0.02 || t > 1.02) continue;
            double px = x0 + t * sx, py = y0 + t * sy;
            double dx = pt.X - px, dy = pt.Y - py;
            if (dx * dx + dy * dy <= band * band)
                ids.Add(id);
        }
        return ids;
    }

    private static int FindNearestNodeId(
        IReadOnlyDictionary<int, (double X, double Y)> nodeCoords,
        double x,
        double y)
    {
        int bestId = 0;
        double bestDistance = double.MaxValue;
        foreach (var (id, point) in nodeCoords)
        {
            var dx = point.X - x;
            var dy = point.Y - y;
            var distance = dx * dx + dy * dy;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            bestId = id;
        }
        return bestId;
    }

    private enum MeshKind { T3, T6, Q4, Q8 }

    private static MeshKind DetectHomogeneousMesh(IReadOnlyList<MeshInformationRow> rows)
    {
        bool allT3 = rows.All(r => r.IsT3);
        bool allT6 = rows.All(r => r.IsT6 && r.Node4 > 0 && r.Node5 > 0 && r.Node6 > 0);
        bool allQ4 = rows.All(r => r.IsQ4 && r.Node4 > 0);
        bool allQ8 = rows.All(r => r.IsQ8 && r.Node4 > 0 && r.Node5 > 0 && r.Node6 > 0
                                   && r.Node7 > 0 && r.Node8 > 0);
        int matchCount = (allT3 ? 1 : 0) + (allT6 ? 1 : 0) + (allQ4 ? 1 : 0) + (allQ8 ? 1 : 0);
        if (matchCount != 1)
            throw new InvalidOperationException(
                "Transient consolidation requires a homogeneous T3, T6, Q4, or Q8 mesh. " +
                "Regenerate so every element shares the same type (no mixed triangles/quads).");
        if (allT3) return MeshKind.T3;
        if (allT6) return MeshKind.T6;
        if (allQ4) return MeshKind.Q4;
        return MeshKind.Q8;
    }

    /// <summary>
    /// Orient connectivity for positive Jacobian with library shape_fun conventions
    /// (same approach as SeepageDatContentBuilder for p74).
    /// </summary>
    private static int[] FormatConnectivity(
        MeshInformationRow r,
        MeshKind kind,
        SortedDictionary<int, (double X, double Y)> coords)
        => kind switch
        {
            MeshKind.T3 => FormatT3(r, coords),
            MeshKind.T6 => FormatT6(r, coords),
            MeshKind.Q4 => FormatQ4(r),
            MeshKind.Q8 => FormatQ8(r),
            _ => throw new InvalidOperationException("Unsupported mesh kind.")
        };

    /// <summary>
    /// shape_fun nod=3 is N=[ξ, 1−ξ−η, η]. For CCW corners [a,b,c] that maps to [b, a, c].
    /// </summary>
    private static int[] FormatT3(
        MeshInformationRow r,
        SortedDictionary<int, (double X, double Y)> coords)
    {
        var ccw = OrientTriangleCcw(r.Node1, r.Node2, r.Node3, coords);
        return new[] { ccw[1], ccw[0], ccw[2] };
    }

    private static int[] FormatT6(
        MeshInformationRow r,
        SortedDictionary<int, (double X, double Y)> coords)
    {
        if (SignedTriangleArea(r.Node1, r.Node2, r.Node3, coords) >= 0.0)
            return new[] { r.Node1, r.Node2, r.Node3, r.Node4, r.Node5, r.Node6 };
        return new[] { r.Node1, r.Node3, r.Node2, r.Node6, r.Node5, r.Node4 };
    }

    private static int[] FormatQ4(MeshInformationRow r)
    {
        // shape_fun Q4 expects a winding that yields positive det; reverse CCW rings.
        double area2 = r.N1X * (r.N2Y - r.N4Y) + r.N2X * (r.N3Y - r.N1Y)
                     + r.N3X * (r.N4Y - r.N2Y) + r.N4X * (r.N1Y - r.N3Y);
        return area2 > 0
            ? new[] { r.Node1, r.Node4, r.Node3, r.Node2 }
            : new[] { r.Node1, r.Node2, r.Node3, r.Node4 };
    }

    private static int[] FormatQ8(MeshInformationRow r)
    {
        int[] emb = r.SmithEmb2dLocalOrder
            ? new[] { r.Node1, r.Node2, r.Node3, r.Node4, r.Node5, r.Node6, r.Node7, r.Node8 }
            : new[] { r.Node1, r.Node5, r.Node2, r.Node6, r.Node3, r.Node7, r.Node4, r.Node8 };

        double x0 = CoordX(r, emb[0]), y0 = CoordY(r, emb[0]);
        double x1 = CoordX(r, emb[2]), y1 = CoordY(r, emb[2]);
        double x2 = CoordX(r, emb[4]), y2 = CoordY(r, emb[4]);
        double x3 = CoordX(r, emb[6]), y3 = CoordY(r, emb[6]);
        double area2 = x0 * (y1 - y3) + x1 * (y2 - y0) + x2 * (y3 - y1) + x3 * (y0 - y2);
        if (area2 <= 0)
            return emb;
        return new[] { emb[0], emb[7], emb[6], emb[5], emb[4], emb[3], emb[2], emb[1] };
    }

    private static double CoordX(MeshInformationRow r, int nodeId)
    {
        if (nodeId == r.Node1) return r.N1X;
        if (nodeId == r.Node2) return r.N2X;
        if (nodeId == r.Node3) return r.N3X;
        if (nodeId == r.Node4) return r.N4X;
        if (nodeId == r.Node5) return r.N5X;
        if (nodeId == r.Node6) return r.N6X;
        if (nodeId == r.Node7) return r.N7X;
        if (nodeId == r.Node8) return r.N8X;
        return 0;
    }

    private static double CoordY(MeshInformationRow r, int nodeId)
    {
        if (nodeId == r.Node1) return r.N1Y;
        if (nodeId == r.Node2) return r.N2Y;
        if (nodeId == r.Node3) return r.N3Y;
        if (nodeId == r.Node4) return r.N4Y;
        if (nodeId == r.Node5) return r.N5Y;
        if (nodeId == r.Node6) return r.N6Y;
        if (nodeId == r.Node7) return r.N7Y;
        if (nodeId == r.Node8) return r.N8Y;
        return 0;
    }

    private static int[] OrientTriangleCcw(
        int a, int b, int c,
        SortedDictionary<int, (double X, double Y)> coords)
        => SignedTriangleArea(a, b, c, coords) < 0.0
            ? new[] { a, c, b }
            : new[] { a, b, c };

    private static double SignedTriangleArea(
        int n1, int n2, int n3,
        SortedDictionary<int, (double X, double Y)> coords)
    {
        if (!coords.TryGetValue(n1, out var p1)
            || !coords.TryGetValue(n2, out var p2)
            || !coords.TryGetValue(n3, out var p3))
            return 0.0;
        return 0.5 * ((p2.X - p1.X) * (p3.Y - p1.Y) - (p3.X - p1.X) * (p2.Y - p1.Y));
    }

    private sealed class StructuredGrid
    {
        public List<double> Xs = new();
        public List<double> Ys = new();
        public List<double> YsTopDown = new();
    }

    private static List<MeshInformationRow> RequireMeshRows(MeshViewModel meshVM)
    {
        var rows = meshVM?.MeshInformationRows?.ToList() ?? new List<MeshInformationRow>();
        if (rows.Count == 0)
            throw new InvalidOperationException("No mesh available. Generate the mesh (Mesh tab) before running the transient analysis.");
        return rows;
    }

    private static bool TryDetectStructuredQ4(MeshViewModel meshVM, out StructuredGrid? grid)
    {
        grid = null;
        try
        {
            var rows = RequireMeshRows(meshVM);
            if (!rows.All(r => r.IsQ4 && r.Node4 > 0))
                return false;

            var nodeCoords = CollectNodeCoords(rows);
            double minX = nodeCoords.Values.Min(p => p.X), maxX = nodeCoords.Values.Max(p => p.X);
            double minY = nodeCoords.Values.Min(p => p.Y), maxY = nodeCoords.Values.Max(p => p.Y);
            double tolX = Math.Max(1e-9, (maxX - minX) * 1e-6);
            double tolY = Math.Max(1e-9, (maxY - minY) * 1e-6);

            var xs = ClusterSorted(nodeCoords.Values.Select(p => p.X), tolX);
            var ys = ClusterSorted(nodeCoords.Values.Select(p => p.Y), tolY);
            if (xs.Count < 2 || ys.Count < 2 || nodeCoords.Count != xs.Count * ys.Count)
                return false;

            grid = new StructuredGrid
            {
                Xs = xs,
                Ys = ys,
                YsTopDown = ys.AsEnumerable().Reverse().ToList(),
            };
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static List<double> ClusterSorted(IEnumerable<double> values, double tol)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var result = new List<double>();
        foreach (var v in sorted)
        {
            if (result.Count == 0 || v - result[^1] > tol)
                result.Add(v);
        }
        return result;
    }

    private static void AppendStructuredEtypes(
        StringBuilder sb, StructuredGrid grid, MeshViewModel meshVM, int npTypes)
    {
        var rows = RequireMeshRows(meshVM);
        int nxe = grid.Xs.Count - 1, nye = grid.Ys.Count - 1;
        var cellEtype = new int[nxe * nye];
        for (int i = 0; i < cellEtype.Length; i++) cellEtype[i] = 1;

        foreach (var r in rows)
        {
            double cx = (r.N1X + r.N2X + r.N3X + r.N4X) / 4.0;
            double cy = (r.N1Y + r.N2Y + r.N3Y + r.N4Y) / 4.0;
            int ix = Math.Clamp(FindInterval(grid.Xs, cx), 0, nxe - 1);
            int iyAsc = Math.Clamp(FindInterval(grid.Ys, cy), 0, nye - 1);
            int iy = nye - 1 - iyAsc;
            cellEtype[iy * nxe + ix] = Math.Clamp(r.Etype < 1 ? 1 : r.Etype, 1, npTypes);
        }

        sb.AppendLine("! etype (structured element order: x-first, top row first)");
        int col = 0;
        foreach (var et in cellEtype)
        {
            sb.Append($"{et,3}");
            if (++col % 20 == 0) sb.AppendLine();
        }
        if (col % 20 != 0) sb.AppendLine();
    }

    private static int FindInterval(List<double> sortedAsc, double v)
    {
        for (int i = 0; i < sortedAsc.Count - 1; i++)
            if (v <= sortedAsc[i + 1]) return i;
        return sortedAsc.Count - 2;
    }

    private static (List<double> Kx, List<double> Ky, int NpTypes) CollectPermeabilities(
        MaterialPropertiesViewModel materialsVM)
    {
        var mats = materialsVM?.Materials?.ToList() ?? new List<MaterialModel>();
        if (mats.Count == 0)
            return (new List<double> { 1.0 }, new List<double> { 1.0 }, 1);
        var kx = mats.Select(m => m.PermeabilityX > 0 ? m.PermeabilityX : 1.0).ToList();
        var ky = mats.Select(m => m.PermeabilityY > 0 ? m.PermeabilityY : 1.0).ToList();
        return (kx, ky, mats.Count);
    }

    private static void AppendPermeabilityRows(StringBuilder sb, List<double> kx, List<double> ky, int npTypes)
    {
        sb.AppendLine("! kx ky (consolidation / diffusivity) per material type");
        for (int i = 0; i < npTypes; i++)
            sb.AppendLine($"{F(kx[i])}  {F(ky[i])}");
    }

    private static SortedDictionary<int, (double X, double Y)> CollectNodeCoords(IEnumerable<MeshInformationRow> rows)
    {
        var map = new SortedDictionary<int, (double X, double Y)>();
        foreach (var r in rows)
        {
            map[r.Node1] = (r.N1X, r.N1Y);
            map[r.Node2] = (r.N2X, r.N2Y);
            map[r.Node3] = (r.N3X, r.N3Y);
            if (r.Node4 > 0) map[r.Node4] = (r.N4X, r.N4Y);
            if (r.Node5 > 0) map[r.Node5] = (r.N5X, r.N5Y);
            if (r.Node6 > 0) map[r.Node6] = (r.N6X, r.N6Y);
            if (r.Node7 > 0) map[r.Node7] = (r.N7X, r.N7Y);
            if (r.Node8 > 0) map[r.Node8] = (r.N8X, r.N8Y);
        }
        return map;
    }

    private static int ClampMonitor(int nres, int nn) => Math.Clamp(nres < 1 ? 1 : nres, 1, Math.Max(1, nn));

    private static IEnumerable<List<T>> Chunk<T>(IReadOnlyList<T> items, int size)
    {
        for (int i = 0; i < items.Count; i += size)
            yield return items.Skip(i).Take(size).ToList();
    }

    private static string F(double v) => v.ToString("G10", CultureInfo.InvariantCulture);
}
