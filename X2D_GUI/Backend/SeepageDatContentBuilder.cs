using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Builds steady-seepage .dat decks (Smith &amp; Griffiths Chapter 7 layouts) from the
/// current mesh, materials, and <see cref="SeepageAnalysisSettings"/>:
///   p72 — 2D Plane/axisymmetric (structured Q4 grid, geom_rect layout),
///   p73 — Unconfined / free-surface flow (structured grid, geom_freesurf layout),
///   p74 — General 2D (explicit mesh).
/// Fixed total heads: upstream head on the min-x boundary and downstream head on max-x
/// unless interactive placement selects the opposite orientation. p73 is mirrored into its
/// internal S&amp;G convention when the physical upstream boundary is min-x.
/// </summary>
public static class SeepageDatContentBuilder
{
    /// <summary>Solver program id (fem_cpp dispatch token) for a seepage detailed analysis type.</summary>
    public static string? ResolveProgramId(string? detailedType) => detailedType switch
    {
        MaterialPropertiesViewModel.GroundwaterSeepagePlaneAxisymType => "p72",
        MaterialPropertiesViewModel.GroundwaterSeepageFreeSurfaceType => "p73",
        MaterialPropertiesViewModel.GroundwaterSeepageGeneralType => "p74",
        _ => null
    };

    public static string Build(
        string detailedType,
        MeshViewModel meshVM,
        MaterialPropertiesViewModel materialsVM,
        SeepageAnalysisSettings settings,
        IReadOnlyList<WaterHeadModel>? waterHeads = null,
        bool useInteractiveWaterHeads = false)
    {
        var programId = ResolveProgramId(detailedType)
            ?? throw new InvalidOperationException($"'{detailedType}' is not a seepage analysis type.");
        return programId switch
        {
            "p72" => BuildP72(meshVM, materialsVM, settings, waterHeads, useInteractiveWaterHeads),
            "p73" => BuildP73(meshVM, materialsVM, settings, waterHeads, useInteractiveWaterHeads),
            _ => BuildP74(meshVM, materialsVM, settings, waterHeads, useInteractiveWaterHeads),
        };
    }

    // ─── p72: structured plane / axisymmetric grid ──────────────────────────

    private static string BuildP72(
        MeshViewModel meshVM,
        MaterialPropertiesViewModel materialsVM,
        SeepageAnalysisSettings settings,
        IReadOnlyList<WaterHeadModel>? waterHeads,
        bool useInteractiveWaterHeads)
    {
        var grid = DetectStructuredGrid(meshVM,
            "2D Plane/axisymmetric (p72) requires a structured rectangular Q4 mesh. " +
            "Regenerate the mesh on a rectangular domain, or switch the analysis type to General 2D (p74).");
        int nxe = grid.Xs.Count - 1, nye = grid.Ys.Count - 1;
        var (kx, ky, npTypes) = CollectPermeabilities(materialsVM);

        var sb = new StringBuilder();
        sb.AppendLine("p72");
        sb.AppendLine("! Steady seepage — 2D Plane/axisymmetric (S&G Program 7.2, Q4 structured grid)");
        sb.AppendLine($"'{(settings.Axisymmetric ? "axisymmetric" : "plane")}' '{(settings.Axisymmetric ? "r" : "x")}'");
        sb.AppendLine($"{nxe} {nye} {npTypes}");
        AppendPermeabilityRows(sb, kx, ky, npTypes);
        if (npTypes > 1)
            AppendStructuredEtypes(sb, grid, meshVM, npTypes);
        sb.AppendLine("! x_coords then y_coords (top -> bottom)");
        sb.AppendLine(string.Join("  ", grid.Xs.Select(F)));
        sb.AppendLine(string.Join("  ", grid.YsTopDown.Select(F)));
        sb.AppendLine("! loaded nodes (sources/sinks)");
        sb.AppendLine("0");

        // geom_rect dir='x' numbering: node = row*(nxe+1) + col + 1, row 0 = top of y list.
        int cols = nxe + 1, rows = nye + 1;
        var nodeCoords = new Dictionary<int, (double X, double Y)>();
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
            nodeCoords[r * cols + c + 1] = (grid.Xs[c], grid.YsTopDown[r]);
        var fixedHeads = ResolveFixedHeads(nodeCoords, settings, waterHeads, useInteractiveWaterHeads);
        sb.AppendLine("! fixed total heads: upstream = min-x column, downstream = max-x column (or Water Head rows)");
        sb.AppendLine(fixedHeads.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var chunk in Chunk(fixedHeads, 6))
            sb.AppendLine(string.Join("   ", chunk.Select(f => $"{f.Node} {F(f.Value)}")));
        return sb.ToString();
    }

    // ─── p73: free-surface flow (structured grid dimensions from mesh) ──────

    private static string BuildP73(
        MeshViewModel meshVM,
        MaterialPropertiesViewModel materialsVM,
        SeepageAnalysisSettings settings,
        IReadOnlyList<WaterHeadModel>? waterHeads,
        bool useInteractiveWaterHeads)
    {
        var grid = DetectStructuredGrid(meshVM,
            "Unconfined / free-surface flow (p73) requires a structured rectangular Q4 mesh " +
            "(the solver re-positions node elevations while iterating on the phreatic surface).");
        int nxe = grid.Xs.Count - 1, nye = grid.Ys.Count - 1;
        var (kx, ky, npTypes) = CollectPermeabilities(materialsVM);

        double hup = settings.UpstreamHead;
        double hdown = settings.DownstreamHead;
        if (useInteractiveWaterHeads && waterHeads != null)
        {
            var up = PreferredRoleHead(waterHeads, WaterHeadRole.Upstream);
            var down = PreferredRoleHead(waterHeads, WaterHeadRole.Downstream);
            if (up != null) hup = up.Head;
            if (down != null) hdown = down.Head;
        }
        if (hup <= 0)
            throw new InvalidOperationException("Free-surface flow requires a positive Upstream Water Head.");
        if (hdown < 0 || hdown >= hup)
            throw new InvalidOperationException(
                $"Free-surface flow requires 0 <= downstream head < upstream head (current Hdown={F(hdown)}, Hup={F(hup)}).");

        bool upstreamOnMinX = ResolveP73UpstreamOnMinX(
            grid, waterHeads, useInteractiveWaterHeads);
        int orientation = upstreamOnMinX ? -1 : 1;

        // Downstream fixed rows: left-edge nodes at/below the downstream head level.
        int fixedDown = Math.Clamp((int)Math.Round(hdown / (hup / nye)) + 1, 1, nye - 1);
        int fixedUp = nye + 1;

        var sb = new StringBuilder();
        sb.AppendLine("p73");
        sb.AppendLine("! Free-surface (unconfined) seepage — S&G Program 7.3 (Q4, iterative phreatic surface)");
        sb.AppendLine($"! physical upstream face: {(upstreamOnMinX ? "min-x" : "max-x")}");
        sb.AppendLine($"{nxe} {nye} {F(settings.FreeSurfaceTolerance)} {settings.FreeSurfaceMaxIterations} {npTypes} {orientation} {F(grid.Xs[0])}");
        AppendPermeabilityRows(sb, kx, ky, npTypes);
        if (npTypes > 1)
            AppendStructuredEtypes(sb, grid, meshVM, npTypes, reverseX: upstreamOnMinX);
        sb.AppendLine("! bottom widths then top widths (vertical faces => identical)");
        var widths = upstreamOnMinX
            ? grid.Xs.AsEnumerable().Reverse().Select(x => grid.Xs[^1] - x).ToList()
            : grid.Xs.Select(x => x - grid.Xs[0]).ToList();
        sb.AppendLine(string.Join("  ", widths.Select(F)));
        sb.AppendLine(string.Join("  ", widths.Select(F)));
        sb.AppendLine("! initial free-surface height");
        sb.AppendLine(F(hup));
        sb.AppendLine("! upstream head + fixed node count, then node list (max-x face)");
        sb.AppendLine($"{F(hup)} {fixedUp}");
        sb.AppendLine(string.Join("  ", Enumerable.Range(1, fixedUp).Select(i => i * (nxe + 1))));
        sb.AppendLine("! downstream head + fixed node count, then node list (min-x face)");
        sb.AppendLine($"{F(hdown)} {fixedDown}");
        sb.AppendLine(string.Join("  ", Enumerable.Range(nye + 1 - fixedDown, fixedDown).Select(i => i * (nxe + 1) + 1)));
        return sb.ToString();
    }

    private static WaterHeadModel? PreferredRoleHead(
        IReadOnlyList<WaterHeadModel> waterHeads,
        WaterHeadRole role)
        => waterHeads.FirstOrDefault(h => h.IsEnabled && h.Role == role
                && h.DefinitionMethod == WaterHeadDefinitionMethod.UserDefined)
            ?? waterHeads.FirstOrDefault(h => h.IsEnabled && h.Role == role);

    private static bool ResolveP73UpstreamOnMinX(
        StructuredGrid grid,
        IReadOnlyList<WaterHeadModel>? waterHeads,
        bool useInteractiveWaterHeads)
    {
        if (!useInteractiveWaterHeads || waterHeads == null)
            return false; // Legacy S&G p73 convention: upstream at max-x.

        var upstream = PreferredRoleHead(waterHeads, WaterHeadRole.Upstream);
        var downstream = PreferredRoleHead(waterHeads, WaterHeadRole.Downstream);
        bool? upstreamAtMin = ResolveP73BoundarySide(upstream, grid, WaterHeadRole.Upstream);
        bool? downstreamAtMin = ResolveP73BoundarySide(downstream, grid, WaterHeadRole.Downstream);

        if (upstreamAtMin.HasValue && downstreamAtMin.HasValue
            && upstreamAtMin.Value == downstreamAtMin.Value)
        {
            throw new InvalidOperationException(
                "p73 Upstream and Downstream Water Heads must be placed on opposite vertical boundaries.");
        }

        if (upstreamAtMin.HasValue)
            return upstreamAtMin.Value;
        if (downstreamAtMin.HasValue)
            return !downstreamAtMin.Value;
        return false;
    }

    private static bool? ResolveP73BoundarySide(
        WaterHeadModel? head,
        StructuredGrid grid,
        WaterHeadRole role)
    {
        if (head == null)
            return null;
        if (head.DefinitionMethod == WaterHeadDefinitionMethod.UserDefined && !head.HasPlacement)
            return role == WaterHeadRole.Upstream;
        if (!head.HasPlacement)
            return null;

        double x = head.PlacementType == WaterHeadPlacementType.Line
            ? (head.StartX + head.EndX) / 2.0
            : head.StartX;
        double minX = grid.Xs[0], maxX = grid.Xs[^1];
        double span = maxX - minX;
        double boundaryTolerance = Math.Max(1e-8, span * 1e-4);
        double dMin = Math.Abs(x - minX), dMax = Math.Abs(x - maxX);
        if (Math.Min(dMin, dMax) > boundaryTolerance)
        {
            throw new InvalidOperationException(
                $"p73 {role} Water Head must be placed on the min-x or max-x vertical boundary.");
        }
        return dMin <= dMax;
    }

    // ─── p74: general explicit 2D mesh ───────────────────────────────────────

    private enum P74MeshKind { T3, T6, Q4, Q8 }

    private static string BuildP74(
        MeshViewModel meshVM,
        MaterialPropertiesViewModel materialsVM,
        SeepageAnalysisSettings settings,
        IReadOnlyList<WaterHeadModel>? waterHeads,
        bool useInteractiveWaterHeads)
    {
        var rows = RequireMeshRows(meshVM);
        var nodeCoords = CollectNodeCoords(rows);
        int nn = nodeCoords.Keys.Max();
        var kind = DetectHomogeneousP74Mesh(rows);
        var (element, nod, nip) = kind switch
        {
            P74MeshKind.T3 => ("triangle", 3, 1),
            P74MeshKind.T6 => ("triangle", 6, 3),
            P74MeshKind.Q4 => ("quadrilateral", 4, 4),
            P74MeshKind.Q8 => ("quadrilateral", 8, 4),
            _ => throw new InvalidOperationException(
                "General 2D seepage (p74) requires a homogeneous T3, T6, Q4, or Q8 mesh.")
        };
        var (kx, ky, npTypes) = CollectPermeabilities(materialsVM);

        var sb = new StringBuilder();
        sb.AppendLine("p74");
        sb.AppendLine($"! Steady seepage — General 2D (S&G Program 7.4, explicit {kind} mesh)");
        sb.AppendLine($"'{element}' {nod} {rows.Count} {nn} {nip} 2 {npTypes}");
        AppendPermeabilityRows(sb, kx, ky, npTypes);
        if (npTypes > 1)
        {
            sb.AppendLine("! etype (1-based material index per element)");
            int col = 0;
            foreach (var r in rows)
            {
                sb.Append($"{Math.Clamp(r.Etype < 1 ? 1 : r.Etype, 1, npTypes),3}");
                if (++col % 20 == 0) sb.AppendLine();
            }
            if (col % 20 != 0) sb.AppendLine();
        }
        sb.AppendLine("! nodal coordinates x y (1..nn)");
        for (int id = 1; id <= nn; id++)
        {
            var (x, y) = nodeCoords.TryGetValue(id, out var pt) ? pt : (0.0, 0.0);
            sb.AppendLine($"{F(x)}  {F(y)}");
        }
        sb.AppendLine("! connectivity (oriented for positive seepage Jacobian)");
        foreach (var r in rows)
            sb.AppendLine(string.Join("  ", FormatP74Connectivity(r, kind, nodeCoords)));
        sb.AppendLine("! loaded nodes (sources/sinks)");
        sb.AppendLine("0");

        var fixedHeads = ResolveFixedHeads(nodeCoords, settings, waterHeads, useInteractiveWaterHeads);
        sb.AppendLine("! fixed total heads: upstream = min-x boundary, downstream = max-x boundary (or Water Head rows)");
        sb.AppendLine(fixedHeads.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var chunk in Chunk(fixedHeads, 6))
            sb.AppendLine(string.Join("   ", chunk.Select(f => $"{f.Node} {F(f.Value)}")));
        return sb.ToString();
    }

    private static P74MeshKind DetectHomogeneousP74Mesh(IReadOnlyList<MeshInformationRow> rows)
    {
        bool allT3 = rows.All(r => r.IsT3);
        bool allT6 = rows.All(r => r.IsT6 && r.Node4 > 0 && r.Node5 > 0 && r.Node6 > 0);
        bool allQ4 = rows.All(r => r.IsQ4 && r.Node4 > 0);
        bool allQ8 = rows.All(r => r.IsQ8 && r.Node4 > 0 && r.Node5 > 0 && r.Node6 > 0
                                   && r.Node7 > 0 && r.Node8 > 0);
        int matchCount = (allT3 ? 1 : 0) + (allT6 ? 1 : 0) + (allQ4 ? 1 : 0) + (allQ8 ? 1 : 0);
        if (matchCount != 1)
            throw new InvalidOperationException(
                "General 2D seepage (p74) requires a homogeneous T3, T6, Q4, or Q8 mesh. " +
                "Regenerate so every element shares the same type (no mixed triangles/quads).");
        if (allT3) return P74MeshKind.T3;
        if (allT6) return P74MeshKind.T6;
        if (allQ4) return P74MeshKind.Q4;
        return P74MeshKind.Q8;
    }

    private static int[] FormatP74Connectivity(
        MeshInformationRow r,
        P74MeshKind kind,
        SortedDictionary<int, (double X, double Y)> coords)
    {
        return kind switch
        {
            P74MeshKind.T3 => FormatP74T3(r, coords),
            P74MeshKind.T6 => FormatP74T6(r, coords),
            P74MeshKind.Q4 => FormatP74Q4(r),
            P74MeshKind.Q8 => FormatP74Q8(r),
            _ => throw new InvalidOperationException("Unsupported p74 mesh kind.")
        };
    }

    /// <summary>
    /// Seepage <c>shape_fun</c> nod=3 is N=[ξ, 1−ξ−η, η]. For CCW corners [a,b,c] that maps to
    /// connectivity [b, a, c] (ξ-vertex, origin, η-vertex).
    /// </summary>
    private static int[] FormatP74T3(
        MeshInformationRow r,
        SortedDictionary<int, (double X, double Y)> coords)
    {
        var ccw = OrientTriangleCcw(r.Node1, r.Node2, r.Node3, coords);
        return new[] { ccw[1], ccw[0], ccw[2] };
    }

    /// <summary>
    /// T6 kernels use standard barycentric order: corners N1–N3 then mids on edges 1–2, 2–3, 3–1.
    /// </summary>
    private static int[] FormatP74T6(
        MeshInformationRow r,
        SortedDictionary<int, (double X, double Y)> coords)
    {
        if (SignedTriangleArea(r.Node1, r.Node2, r.Node3, coords) >= 0.0)
            return new[] { r.Node1, r.Node2, r.Node3, r.Node4, r.Node5, r.Node6 };
        // Swap corners 2↔3 and their adjacent midsides (4↔6); mid on 2–3 stays Node5.
        return new[] { r.Node1, r.Node3, r.Node2, r.Node6, r.Node5, r.Node4 };
    }

    private static int[] FormatP74Q4(MeshInformationRow r)
    {
        // Shoelace: positive area = counter-clockwise; seepage Q4 kernels need clockwise.
        double area2 = r.N1X * (r.N2Y - r.N4Y) + r.N2X * (r.N3Y - r.N1Y)
                     + r.N3X * (r.N4Y - r.N2Y) + r.N4X * (r.N1Y - r.N3Y);
        return area2 > 0
            ? new[] { r.Node1, r.Node4, r.Node3, r.Node2 }
            : new[] { r.Node1, r.Node2, r.Node3, r.Node4 };
    }

    private static int[] FormatP74Q8(MeshInformationRow r)
    {
        // Solver Q8 shapes expect emb_2d perimeter order (C-M-C-M-C-M-C-M).
        int[] emb = r.SmithEmb2dLocalOrder
            ? new[] { r.Node1, r.Node2, r.Node3, r.Node4, r.Node5, r.Node6, r.Node7, r.Node8 }
            : new[] { r.Node1, r.Node5, r.Node2, r.Node6, r.Node3, r.Node7, r.Node4, r.Node8 };

        // Corner shoelace on emb corners (indices 0,2,4,6); reverse full ring if CCW.
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

    /// <summary>
    /// Prefer interactive Groundwater Water Head rows when enabled; otherwise min-x / max-x scalars.
    /// </summary>
    private static List<(int Node, double Value)> ResolveFixedHeads(
        IReadOnlyDictionary<int, (double X, double Y)> nodeCoords,
        SeepageAnalysisSettings settings,
        IReadOnlyList<WaterHeadModel>? waterHeads,
        bool useInteractive)
    {
        double minX = nodeCoords.Values.Min(p => p.X);
        double maxX = nodeCoords.Values.Max(p => p.X);
        double tol = Math.Max(1e-9, (maxX - minX) * 1e-6);
        var result = new List<(int Node, double Value)>();
        var seen = new HashSet<int>();

        void Add(int id, double head)
        {
            if (id <= 0 || !nodeCoords.ContainsKey(id) || !seen.Add(id)) return;
            result.Add((id, head));
        }

        if (useInteractive && waterHeads != null)
        {
            foreach (var wh in waterHeads.Where(h => h.IsEnabled))
            {
                if (wh.DefinitionMethod == WaterHeadDefinitionMethod.UserDefined && !wh.HasPlacement)
                {
                    if (wh.Role == WaterHeadRole.Upstream)
                    {
                        foreach (var (id, pt) in nodeCoords)
                            if (Math.Abs(pt.X - minX) <= tol) Add(id, wh.Head);
                    }
                    else if (wh.Role == WaterHeadRole.Downstream)
                    {
                        foreach (var (id, pt) in nodeCoords)
                            if (Math.Abs(pt.X - maxX) <= tol) Add(id, wh.Head);
                    }
                    continue;
                }

                if (!wh.HasPlacement) continue;

                if (wh.PlacementType == WaterHeadPlacementType.Point)
                {
                    int id = wh.AppliedToNodeId ?? FindNearestWaterHeadNodeId(nodeCoords, wh.StartX, wh.StartY);
                    Add(id, wh.Head);
                }
                else
                {
                    foreach (var id in CollectWaterHeadNodesAlongSegment(nodeCoords, wh.StartX, wh.StartY, wh.EndX, wh.EndY, tol))
                        Add(id, wh.Head);
                }
            }
        }

        if (result.Count == 0)
        {
            foreach (var (id, pt) in nodeCoords)
            {
                if (Math.Abs(pt.X - minX) <= tol) Add(id, settings.UpstreamHead);
                else if (Math.Abs(pt.X - maxX) <= tol) Add(id, settings.DownstreamHead);
            }
        }

        return result;
    }

    private static int FindNearestWaterHeadNodeId(IReadOnlyDictionary<int, (double X, double Y)> nodeCoords, double x, double y)
    {
        int best = 0;
        double bestD2 = double.MaxValue;
        foreach (var (id, pt) in nodeCoords)
        {
            double dx = pt.X - x, dy = pt.Y - y;
            double d2 = dx * dx + dy * dy;
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = id;
            }
        }
        return best;
    }

    private static List<int> CollectWaterHeadNodesAlongSegment(
        IReadOnlyDictionary<int, (double X, double Y)> nodeCoords,
        double x0, double y0, double x1, double y1,
        double baseTol)
    {
        var ids = new List<int>();
        double sx = x1 - x0, sy = y1 - y0;
        double len2 = sx * sx + sy * sy;
        if (len2 < 1e-24)
        {
            var id = FindNearestWaterHeadNodeId(nodeCoords, x0, y0);
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

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private sealed class StructuredGrid
    {
        public List<double> Xs = new();          // ascending
        public List<double> Ys = new();          // ascending
        public List<double> YsTopDown = new();   // descending (deck order)
    }

    private static List<MeshInformationRow> RequireMeshRows(MeshViewModel meshVM)
    {
        var rows = meshVM?.MeshInformationRows?.ToList() ?? new List<MeshInformationRow>();
        if (rows.Count == 0)
            throw new InvalidOperationException("No mesh available. Generate the mesh (Mesh tab) before running the seepage analysis.");
        return rows;
    }

    private static StructuredGrid DetectStructuredGrid(MeshViewModel meshVM, string errorMessage)
    {
        var rows = RequireMeshRows(meshVM);
        var nodeCoords = CollectNodeCoords(rows);
        double minX = nodeCoords.Values.Min(p => p.X), maxX = nodeCoords.Values.Max(p => p.X);
        double minY = nodeCoords.Values.Min(p => p.Y), maxY = nodeCoords.Values.Max(p => p.Y);
        double tolX = Math.Max(1e-9, (maxX - minX) * 1e-6);
        double tolY = Math.Max(1e-9, (maxY - minY) * 1e-6);

        var xs = ClusterSorted(nodeCoords.Values.Select(p => p.X), tolX);
        var ys = ClusterSorted(nodeCoords.Values.Select(p => p.Y), tolY);
        if (xs.Count < 2 || ys.Count < 2 || nodeCoords.Count != xs.Count * ys.Count)
            throw new InvalidOperationException(errorMessage);

        return new StructuredGrid
        {
            Xs = xs,
            Ys = ys,
            YsTopDown = ys.AsEnumerable().Reverse().ToList(),
        };
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

    /// <summary>Maps GUI element etypes onto the solver's structured (row-major, x-first) element order.</summary>
    private static void AppendStructuredEtypes(
        StringBuilder sb,
        StructuredGrid grid,
        MeshViewModel meshVM,
        int npTypes,
        bool reverseX = false)
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
            // Solver rows count from the top y downwards (deck lists y descending).
            int iyAsc = Math.Clamp(FindInterval(grid.Ys, cy), 0, nye - 1);
            int iy = nye - 1 - iyAsc;
            cellEtype[iy * nxe + ix] = Math.Clamp(r.Etype < 1 ? 1 : r.Etype, 1, npTypes);
        }

        sb.AppendLine("! etype (structured element order: x-first, top row first)");
        int col = 0;
        var outputEtypes = reverseX
            ? Enumerable.Range(0, nye)
                .SelectMany(row => cellEtype.Skip(row * nxe).Take(nxe).Reverse())
            : cellEtype;
        foreach (var et in outputEtypes)
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
            return (new List<double> { 1e-5 }, new List<double> { 1e-5 }, 1);
        var kx = mats.Select(m => m.PermeabilityX > 0 ? m.PermeabilityX : 1e-5).ToList();
        var ky = mats.Select(m => m.PermeabilityY > 0 ? m.PermeabilityY : 1e-5).ToList();
        return (kx, ky, mats.Count);
    }

    private static void AppendPermeabilityRows(StringBuilder sb, List<double> kx, List<double> ky, int npTypes)
    {
        sb.AppendLine("! kx ky per material type");
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

    private static IEnumerable<List<T>> Chunk<T>(IReadOnlyList<T> items, int size)
    {
        for (int i = 0; i < items.Count; i += size)
            yield return items.Skip(i).Take(size).ToList();
    }

    private static string F(double v) => v.ToString("G10", CultureInfo.InvariantCulture);
}
