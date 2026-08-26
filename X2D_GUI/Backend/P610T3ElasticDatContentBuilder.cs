using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Builds a p610_t3_elastic .dat input file (linear-elastic T3, far-field traction BCs)
/// from current mesh, materials, BCs, and field-stress configuration.
///
/// Reuses the same fenced section markers and section enum as <see cref="P610T3DatContentBuilder"/>
/// so the existing 7-button wizard works unchanged: sections 1..5 (Header, Materials, Mesh, Bcs,
/// OutputNodes) are byte-for-byte identical to the elasto-plastic builder, section 6 (SolverControl)
/// is a comment-only stub (the elastic solver does a single direct solve — no tol/incs/layers),
/// and section 7 (Layers) emits the traction-group block instead of excavation layers.
/// </summary>
public static class P610T3ElasticDatContentBuilder
{
    public const string SolverTag = "! solver:p610_t3_elastic";

    /// <summary>
    /// Returns true when the script content was generated for p610_t3_elastic
    /// (looks for the elastic-specific tag, distinct from the plain p610_t3 tag).
    /// </summary>
    public static bool IsP610T3ElasticContent(string content)
    {
        if (string.IsNullOrEmpty(content)) return false;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.Contains("solver:p610_t3_elastic", StringComparison.OrdinalIgnoreCase)) return true;
            if (!line.StartsWith('!')) break; // stop scanning after first non-comment line
        }
        return false;
    }

    // ─── Public section wrappers ─────────────────────────────────────────────
    //
    // Header / Materials / Mesh / BCs / OutputNodes are emitted exactly the
    // same way as the elasto-plastic builder — the elastic solver consumes the
    // same first 5 sections. We delegate so the two builders never drift.

    public static string BuildHeader(MeshViewModel meshVM)
        => P610T3DatContentBuilder.BuildHeader(meshVM);

    public static string BuildHeader(MeshViewModel meshVM, MaterialPropertiesViewModel? materialsVM)
        => P610T3DatContentBuilder.BuildHeader(meshVM, materialsVM);

    public static string BuildMaterials(MaterialPropertiesViewModel materialsVM)
        => P610T3DatContentBuilder.BuildMaterials(materialsVM);

    public static string BuildMesh(MeshViewModel meshVM, MaterialPropertiesViewModel materialsVM)
        => P610T3DatContentBuilder.BuildMesh(meshVM, materialsVM);

    public static string BuildBcs(BoundaryConditionsViewModel bcVM, MeshViewModel meshVM)
        => P610T3DatContentBuilder.BuildBcs(bcVM, meshVM);

    public static string BuildOutputNodes(BoundaryConditionsViewModel bcVM, MeshViewModel meshVM)
        => P610T3DatContentBuilder.BuildOutputNodes(bcVM, meshVM);

    /// <summary>
    /// SolverControl placeholder for elastic mode — the elastic solver does a single direct
    /// solve and ignores tol/incs/layers, so this section becomes a comment-only stub.
    /// We still emit the fenced block so the wizard's "Done" indicator can light up.
    /// </summary>
    public static string BuildSolverControl()
        => Wrap(P610T3DatContentBuilder.Section.SolverControl,
                "! (single-solve linear-elastic — p610_t3_elastic ignores tol/limit/incs/layers)");

    /// <summary>
    /// Tractions block emitted under the <c>Layers</c> sentinel (the wizard reuses the same
    /// 7-button XAML for both formats; in elastic mode the 7th button writes far-field tractions
    /// instead of excavation layers).
    /// </summary>
    public static string BuildTractions(
        MeshViewModel meshVM,
        BoundaryConditionsViewModel bcVM,
        FieldStressViewModel? fieldStressVM,
        MaterialPropertiesViewModel? materialsVM)
        => Wrap(P610T3DatContentBuilder.Section.Layers,
                BuildTractionsBody(meshVM, bcVM, fieldStressVM, materialsVM));

    /// <summary>
    /// Skeleton emitted when the editor first switches into p610_t3_elastic mode.
    /// Identical layout to the elasto-plastic skeleton — same 7 fenced placeholders — but
    /// the elastic solver tag and a banner that explains sections 6 & 7 behave differently.
    /// </summary>
    public static string SkeletonHeader()
    {
        var sb = new StringBuilder();
        sb.AppendLine(SolverTag);
        sb.AppendLine("! p610_t3_elastic — linear-elastic plane-strain T3 with far-field traction BCs");
        sb.AppendLine("! Unit system: lengths m | stresses & moduli kPa | unit weight kN/m³ | angles °");
        sb.AppendLine($"! Skeleton created {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("! Click the numbered buttons (1..7) above the editor to emit each section.");
        sb.AppendLine("! Note: section 6 is a comment stub (no tol/incs/layers in elastic), section 7");
        sb.AppendLine("!       writes the far-field traction block instead of excavation layers.");
        sb.AppendLine();

        int step = 1;
        foreach (var s in P610T3DatContentBuilder.SectionOrder)
        {
            sb.AppendLine(Wrap(s, PlaceholderBody(step, s)).TrimEnd('\r', '\n'));
            sb.AppendLine();
            step++;
        }
        return sb.ToString();
    }

    /// <summary>Full elastic .dat output (used by "Emit All").</summary>
    public static string Build(
        MeshViewModel meshVM,
        MaterialPropertiesViewModel materialsVM,
        BoundaryConditionsViewModel bcVM,
        FieldStressViewModel? fieldStressVM)
    {
        var sb = new StringBuilder();
        sb.AppendLine(SolverTag);
        sb.AppendLine("! p610_t3_elastic — linear-elastic plane-strain T3 with far-field traction BCs");
        sb.AppendLine("! Unit system: lengths m | stresses & moduli kPa | unit weight kN/m³ | angles °");
        sb.AppendLine($"! Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        sb.Append(BuildHeader(meshVM, materialsVM));
        sb.AppendLine();
        sb.Append(BuildMaterials(materialsVM));
        sb.AppendLine();
        sb.Append(BuildMesh(meshVM, materialsVM));
        sb.AppendLine();
        sb.Append(BuildBcs(bcVM, meshVM));
        sb.AppendLine();
        sb.Append(BuildOutputNodes(bcVM, meshVM));
        sb.AppendLine();
        sb.Append(BuildSolverControl());
        sb.AppendLine();
        sb.Append(BuildTractions(meshVM, bcVM, fieldStressVM, materialsVM));

        return sb.ToString();
    }

    // ─── Tractions body ─────────────────────────────────────────────────────

    private static string BuildTractionsBody(
        MeshViewModel meshVM,
        BoundaryConditionsViewModel bcVM,
        FieldStressViewModel? fieldStressVM,
        MaterialPropertiesViewModel? materialsVM)
    {
        // Honour an explicit, user-edited traction-group list whenever it is non-empty,
        // regardless of the current field-stress mode. This lets the user pin specific edge
        // selections from a loaded .dat, switch modes freely in the GUI, and still get the
        // exact same traction block back on Execute Script.
        var explicitGroups = fieldStressVM?.Model?.TractionGroups;
        if (explicitGroups != null && explicitGroups.Count > 0)
            return BuildTractionsFromExplicitGroups(explicitGroups);

        var rows = meshVM?.MeshInformationRows?.ToList() ?? new List<MeshInformationRow>();
        if (rows.Count == 0)
            throw new InvalidOperationException("No mesh elements. Generate a 2D mesh first (Mesh tab → Tunnel Meshing or RS2D).");

        var nodeCoords = CollectNodeCoords(rows);
        int nn = nodeCoords.Keys.Max();

        // Restraint map: nodes NOT in the BC list are fully free (1,1).
        var fixMap = BoundaryConditionFixityMapper.BuildFixMap(bcVM, rows, nn);

        // Boundary edges = edges referenced by exactly one element.
        // We emit them in the CCW order they appear in that element so that the
        // right-hand-rule outward normal matches what the elastic solver expects.
        var edgeOwner = new Dictionary<(int A, int B), (int Owner, int Opposite, int OrigA, int OrigB)>();

        foreach (var r in rows)
        {
            int v1 = r.Node1, v2 = r.Node2, v3 = r.Node3;
            var (x1, y1) = nodeCoords[v1];
            var (x2, y2) = nodeCoords[v2];
            var (x3, y3) = nodeCoords[v3];
            double signed2 = (x2 - x1) * (y3 - y1) - (x3 - x1) * (y2 - y1);
            // Iterate edges in CCW order; if element is CW, swap so we still emit CCW.
            (int a, int b, int opp)[] edges = signed2 >= 0
                ? new[] { (v1, v2, v3), (v2, v3, v1), (v3, v1, v2) }
                : new[] { (v1, v3, v2), (v3, v2, v1), (v2, v1, v3) };
            foreach (var (a, b, opp) in edges)
            {
                var key = a < b ? (a, b) : (b, a);
                if (edgeOwner.ContainsKey(key))
                    edgeOwner.Remove(key);            // shared by 2 elements → interior edge
                else
                    edgeOwner[key] = (r.ElementId, opp, a, b);
            }
        }

        // Candidate traction edges: boundary edges where BOTH endpoints are fully free.
        // (Restrained nodes carry reactions, not external tractions.)
        bool IsFullyFree(int nodeId)
        {
            if (!fixMap.TryGetValue(nodeId, out var f)) return true;
            return f.FixX == 1 && f.FixY == 1;
        }

        var candidates = new List<(int A, int B)>();
        foreach (var kv in edgeOwner)
        {
            int a = kv.Value.OrigA;
            int b = kv.Value.OrigB;
            if (IsFullyFree(a) && IsFullyFree(b))
                candidates.Add((a, b));
        }

        // Convex-hull filter: outer-boundary heuristic that correctly excludes
        // inner annular arcs (e.g. the kirsch_quarter inner tunnel wall, which is
        // a concave free surface and must remain traction-free).
        var hull = ComputeConvexHull(nodeCoords);
        List<(int A, int B)> tractionEdges;
        if (hull.Count >= 3 && candidates.Count > 0)
        {
            tractionEdges = candidates.Where(e => hull.Contains(e.A) && hull.Contains(e.B)).ToList();
            if (tractionEdges.Count == 0)
                tractionEdges = candidates;     // no hull-edges free → use all free boundary edges
        }
        else
        {
            tractionEdges = candidates;
        }

        // Resolve far-field stress tensor (sxx, syy, sxy) from the field-stress VM.
        var (sxx, syy, sxy, modeNote) = ResolveFarFieldStress(fieldStressVM, materialsVM, nodeCoords);

        var sb = new StringBuilder();
        sb.AppendLine("! n_traction_groups");
        if (tractionEdges.Count == 0)
        {
            sb.AppendLine("0");
            sb.Append("! (no candidate boundary edges — every boundary node is restrained or no free-surface edges. Add at least one traction group manually.)");
            return sb.ToString();
        }

        sb.AppendLine("1");
        sb.AppendLine($"! mode=0 direct (tension-positive, kPa): sxx={sxx:G6} syy={syy:G6} sxy={sxy:G6}  [{modeNote}]");
        sb.AppendLine($"0  {sxx:G10}  {syy:G10}  {sxy:G10}  ! kPa");
        sb.AppendLine($"{tractionEdges.Count}");
        for (int i = 0; i < tractionEdges.Count; i++)
        {
            var (a, b) = tractionEdges[i];
            sb.Append($"{a}  {b}");
            if (i < tractionEdges.Count - 1) sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Emits the .dat traction block verbatim from a user-edited
    /// <see cref="FieldStressModel.TractionGroups"/> list. Format mirrors the solver:
    /// <code>
    ///   n_groups
    ///   per group:
    ///     mode  sxx  syy  sxy
    ///     n_edges
    ///     n_edges rows: nodeA  nodeB  [nodeC]
    /// </code>
    /// </summary>
    private static string BuildTractionsFromExplicitGroups(
        ObservableCollection<TractionGroup> groups)
    {
        var sb = new StringBuilder();
        sb.AppendLine("! n_traction_groups");
        sb.AppendLine($"{groups.Count}");
        for (int g = 0; g < groups.Count; g++)
        {
            var grp = groups[g];
            sb.AppendLine($"! Group {g + 1}: mode={grp.Mode} (0=direct sxx,syy,sxy; 1=far-field sigma_v,K0)");
            sb.AppendLine($"{grp.Mode}  {grp.Sxx:G10}  {grp.Syy:G10}  {grp.Txy:G10}");
            sb.AppendLine($"{grp.Edges.Count}");
            for (int i = 0; i < grp.Edges.Count; i++)
            {
                var e = grp.Edges[i];
                if (e.NodeC > 0)
                    sb.Append($"{e.NodeA}  {e.NodeB}  {e.NodeC}");
                else
                    sb.Append($"{e.NodeA}  {e.NodeB}");
                bool lastEdge = (i == grp.Edges.Count - 1);
                bool lastGroup = (g == groups.Count - 1);
                if (!(lastEdge && lastGroup)) sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    private static (double Sxx, double Syy, double Sxy, string Note) ResolveFarFieldStress(
        FieldStressViewModel? fieldStressVM,
        MaterialPropertiesViewModel? materialsVM,
        SortedDictionary<int, (double X, double Y)> nodeCoords)
    {
        // Default fallback when the user hasn't configured field stress yet.
        // (Round, easy-to-spot numbers so the user sees the placeholder and edits.)
        if (fieldStressVM?.Global == null)
            return (-100.0, -100.0, 0.0, "default placeholder — edit Load Conditions ▸ Field Stress");

        var spec = fieldStressVM.Global;
        if (spec.Mode == FieldStressMode.Direct || spec.Mode == FieldStressMode.Traction)
        {
            // Traction mode normally goes through the explicit-groups path above; this
            // fallback only runs when the user picked Traction but never added groups,
            // in which case the seed Direct values are still the most sensible default.
            return (spec.DirectSxx, spec.DirectSyy, spec.DirectTxy,
                    spec.Mode == FieldStressMode.Traction
                        ? "Traction mode (no explicit groups yet) — using Direct-tensor seed"
                        : "Direct tensor from Load Conditions");
        }

        if (spec.Mode == FieldStressMode.Constant)
        {
            // σ1, σ3 stored tension-positive. Rotate by AngleDeg (CCW from +x to σ1).
            double s1 = spec.Sigma1, s3 = spec.Sigma3;
            double th = spec.AngleDeg * Math.PI / 180.0;
            double c = Math.Cos(th), s = Math.Sin(th);
            double sxx = s1 * c * c + s3 * s * s;
            double syy = s1 * s * s + s3 * c * c;
            double sxy = (s1 - s3) * c * s;
            return (sxx, syy, sxy, "Constant: σ1, σ3 rotated by AngleDeg");
        }

        // Gravity mode: estimate a representative σ_v at the mesh centroid using the
        // first material's γ (the elastic solver applies a uniform traction per group;
        // if the user wants a depth-varying field they can add per-row tractions manually).
        double gamma = 20.0;
        if (materialsVM?.Materials?.Count > 0)
            gamma = Math.Max(1e-3, materialsVM.Materials[0].UnitWeight);

        double yMax = nodeCoords.Values.Max(p => p.Y);
        double yMin = nodeCoords.Values.Min(p => p.Y);
        double yTop = spec.AutoSurface ? yMax : spec.YSurface;
        double yMid = 0.5 * (yMax + yMin);
        double depth = Math.Max(0.0, yTop - yMid);
        double sigmaV = -gamma * depth;       // tension-positive → compression negative
        double sigmaH = spec.Kx * sigmaV;
        return (sigmaH, sigmaV, 0.0,
                $"Gravity: γ={gamma:G4}, depth(centroid)={depth:G4}, Kx={spec.Kx:G4}");
    }

    // ─── Convex hull (Andrew's monotone chain) ──────────────────────────────

    private static HashSet<int> ComputeConvexHull(SortedDictionary<int, (double X, double Y)> nodes)
    {
        var pts = nodes
            .Select(kv => (Id: kv.Key, X: kv.Value.X, Y: kv.Value.Y))
            .OrderBy(p => p.X).ThenBy(p => p.Y)
            .ToList();
        int n = pts.Count;
        var hull = new HashSet<int>();
        if (n < 3)
        {
            foreach (var p in pts) hull.Add(p.Id);
            return hull;
        }

        var lower = new List<(int Id, double X, double Y)>();
        foreach (var p in pts)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }

        var upper = new List<(int Id, double X, double Y)>();
        for (int i = n - 1; i >= 0; --i)
        {
            var p = pts[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }

        foreach (var p in lower) hull.Add(p.Id);
        foreach (var p in upper) hull.Add(p.Id);
        return hull;
    }

    private static double Cross(
        (int Id, double X, double Y) o,
        (int Id, double X, double Y) a,
        (int Id, double X, double Y) b)
        => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    // ─── Helpers (parallel the elasto-plastic builder; kept private) ────────

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

    private static string Wrap(P610T3DatContentBuilder.Section s, string body)
    {
        body = string.IsNullOrEmpty(body) ? "" : body.TrimEnd('\r', '\n');
        return $"! <BEGIN:{s}>" + Environment.NewLine + body + Environment.NewLine + $"! <END:{s}>" + Environment.NewLine;
    }

    private static string PlaceholderBody(int step, P610T3DatContentBuilder.Section s)
        => $"! (awaiting: click \"{step}. {FriendlyName(s)}\" above the editor)";

    private static string FriendlyName(P610T3DatContentBuilder.Section s) => s switch
    {
        P610T3DatContentBuilder.Section.Header        => "Header",
        P610T3DatContentBuilder.Section.Materials     => "Materials",
        P610T3DatContentBuilder.Section.Mesh          => "Mesh",
        P610T3DatContentBuilder.Section.Bcs           => "BCs",
        P610T3DatContentBuilder.Section.OutputNodes   => "Output nodes",
        P610T3DatContentBuilder.Section.SolverControl => "Solver (elastic stub)",
        P610T3DatContentBuilder.Section.Layers        => "Tractions",
        _ => s.ToString(),
    };
}
