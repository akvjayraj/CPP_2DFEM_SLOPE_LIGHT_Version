using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Parses a p610_t3 / p610_t3_elastic .dat file content string and populates
/// Material, Boundary Condition, Field Stress, and Geometry Definition ViewModels
/// so every canvas reflects the loaded file immediately.
/// </summary>
public static class P610T3DatImporter
{
    public sealed class ImportSummary
    {
        public int NPTypes { get; init; }
        public int NNodes { get; init; }
        public int NElements { get; init; }
        public int NBoundaryConditions { get; init; }
        public int NGeomVertices { get; init; }
        public bool HasFieldStress { get; init; }
        public bool HasSolverControl { get; init; }
        public double? SolverTol { get; init; }
        public int? SolverLimit { get; init; }
        public int? SolverIncs { get; init; }
        public int? SolverLayers { get; init; }

        /// <summary>
        /// Excavated element IDs grouped by layer, parsed from the
        /// <c>! &lt;BEGIN:Layers&gt; ... ! &lt;END:Layers&gt;</c> sentinel block.
        /// One inner list per excavation stage (Stage 2..N+1; Stage 1 = Initial Stress is implicit).
        /// Empty when the .dat has no Layers block.
        /// </summary>
        public List<List<int>> ExcavationLayers { get; init; } = new();
    }

    /// <summary>
    /// Import only the material property block (p610 row-major layout) into Project Materials.
    /// </summary>
    public static bool TryImportMaterialsOnly(string datContent, MaterialPropertiesViewModel materialsVM)
    {
        try
        {
            P610AnalysisOptionsHelper.TryParse(datContent, out var analysisOptions);
            var tokens = Tokenise(datContent);

            if (!TryDeqInt(tokens, out int nels) ||
                !TryDeqInt(tokens, out int nn) ||
                !TryDeqInt(tokens, out int npTypes) ||
                npTypes < 1)
                return false;

            const int nprops = 7;
            var prop = new double[nprops, npTypes];
            for (int p = 0; p < nprops; p++)
                for (int m = 0; m < npTypes; m++)
                    if (!TryDeqDbl(tokens, out prop[p, m])) return false;

            ApplyMaterials(materialsVM, prop, npTypes, analysisOptions);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Import p610 solver-control line (tol limit incs layers) after mesh/BC prelude.
    /// </summary>
    public static bool TryImportSolverControlOnly(string datContent, FEAnalysisViewModel feVm)
    {
        try
        {
            P610AnalysisOptionsHelper.TryParse(datContent, out var analysisOptions);
            var meshType = analysisOptions?.MeshType ?? "T3";
            var tokens = Tokenise(datContent);

            if (!TryDeqInt(tokens, out int nels) ||
                !TryDeqInt(tokens, out int nn) ||
                !TryDeqInt(tokens, out int npTypes))
                return false;

            const int nprops = 7;
            for (int p = 0; p < nprops; p++)
                for (int m = 0; m < npTypes; m++)
                    if (!TryDeqDbl(tokens, out _)) return false;

            int netypes = npTypes > 1 ? nels : 0;
            for (int i = 0; i < netypes; i++)
                if (!TryDeqInt(tokens, out _)) return false;

            for (int i = 0; i < nn; i++)
                if (!TryDeqDbl(tokens, out _) || !TryDeqDbl(tokens, out _))
                    return false;

            int connNod = ConnectivityNodeCount(meshType);
            for (int i = 0; i < nels; i++)
                for (int k = 0; k < connNod; k++)
                    if (!TryDeqInt(tokens, out _)) return false;

            if (!TryDeqInt(tokens, out int nr)) return false;
            for (int i = 0; i < nr * 3; i++)
                if (!TryDeqInt(tokens, out _)) return false;

            if (!TryDeqInt(tokens, out int nouts) || nouts < 0)
                return false;
            for (int i = 0; i < nouts; i++)
                if (!TryDeqInt(tokens, out _)) return false;

            if (!TryDeqDbl(tokens, out double tol) ||
                !TryDeqInt(tokens, out int limit) ||
                !TryDeqInt(tokens, out int incs) ||
                !TryDeqInt(tokens, out int layers))
                return false;

            feVm.ConvergenceTolerance = tol;
            feVm.MaxIterations = limit;
            feVm.LoadIncrements = incs;
            feVm.LayersOverride = layers;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parse the .dat and push data into all four ViewModels.
    /// Returns null when the content cannot be recognised as a p610_t3 .dat.
    /// </summary>
    public static ImportSummary? Import(
        string datContent,
        MaterialPropertiesViewModel materialsVM,
        BoundaryConditionsViewModel bcVM,
        FieldStressViewModel? fieldStressVM,
        GeometryDefinitionViewModel? geomVM,
        FEAnalysisViewModel? feVM = null)
    {
        try
        {
            P610AnalysisOptionsHelper.TryParse(datContent, out var analysisOptions);
            var meshType = analysisOptions?.MeshType ?? "T3";
            var tokens = Tokenise(datContent);

            if (!TryDeqInt(tokens, out int nels) ||
                !TryDeqInt(tokens, out int nn)   ||
                !TryDeqInt(tokens, out int npTypes)) return null;

            // ── material properties (7 rows × npTypes columns, row-major) ─────
            const int nprops = 7;
            var prop = new double[nprops, npTypes];
            for (int p = 0; p < nprops; p++)
                for (int m = 0; m < npTypes; m++)
                    if (!TryDeqDbl(tokens, out prop[p, m])) return null;

            // ── etype (skip when np_types == 1) ──────────────────────────────
            int netypes = npTypes > 1 ? nels : 0;
            var etype = new int[nels];
            for (int i = 0; i < netypes; i++)
                if (!TryDeqInt(tokens, out etype[i])) return null;

            // ── nodal coordinates ─────────────────────────────────────────────
            var coords = new (double x, double y)[nn];
            for (int i = 0; i < nn; i++)
            {
                if (!TryDeqDbl(tokens, out double x) ||
                    !TryDeqDbl(tokens, out double y)) return null;
                coords[i] = (x, y);
            }

            // ── connectivity (T3/T6/Q4/Q8) ───────────────────────────────────
            int connNod = ConnectivityNodeCount(meshType);
            var conn = new List<int[]>(nels);
            for (int i = 0; i < nels; i++)
            {
                var el = new int[connNod];
                for (int k = 0; k < connNod; k++)
                    if (!TryDeqInt(tokens, out el[k])) return null;
                conn.Add(el);
            }

            // ── boundary conditions ───────────────────────────────────────────
            if (!TryDeqInt(tokens, out int nr)) return null;
            var bcs = new List<(int node, int fx, int fy)>(nr);
            for (int i = 0; i < nr; i++)
            {
                if (!TryDeqInt(tokens, out int nd) ||
                    !TryDeqInt(tokens, out int fx) ||
                    !TryDeqInt(tokens, out int fy)) break;
                bcs.Add((nd, fx, fy));
            }

            // ── output nodes (nouts followed by nouts ids) + solver control
            //
            // Layout per p610_t3 .dat:
            //   nouts
            //   no[1..nouts]
            //   tol  limit  incs  layers
            //
            // We commit the four solver-control values to feVM only when all four
            // parse cleanly so traction-block scanners (elastic .dat) still work
            // when the SolverControl line is absent or malformed.
            double? parsedTol = null;
            int? parsedLimit = null, parsedIncs = null, parsedLayers = null;
            if (TryDeqInt(tokens, out int nouts) && nouts >= 0)
            {
                bool noutsOk = true;
                for (int i = 0; i < nouts; i++)
                {
                    if (!TryDeqInt(tokens, out _)) { noutsOk = false; break; }
                }

                if (noutsOk &&
                    TryDeqDbl(tokens, out double tol) &&
                    TryDeqInt(tokens, out int limit) &&
                    TryDeqInt(tokens, out int incs)  &&
                    TryDeqInt(tokens, out int layers))
                {
                    parsedTol = tol;
                    parsedLimit = limit;
                    parsedIncs = incs;
                    parsedLayers = layers;
                }
            }

            // ── apply all sections ────────────────────────────────────────────
            ApplyMaterials(materialsVM, prop, npTypes, analysisOptions);
            ApplyBoundaryConditions(bcVM, bcs);

            if (feVM != null && parsedTol.HasValue && parsedLimit.HasValue && parsedIncs.HasValue && parsedLayers.HasValue)
            {
                feVM.ConvergenceTolerance = parsedTol.Value;
                feVM.MaxIterations        = parsedLimit.Value;
                feVM.LoadIncrements       = parsedIncs.Value;
                feVM.LayersOverride       = parsedLayers.Value;
            }

            var fsModel = ParseFieldStressBlock(datContent)
                          ?? ParseDirectTractionTensor(datContent, meshType);
            if (fsModel != null && fieldStressVM != null)
                fieldStressVM.LoadFromModel(fsModel);

            int nGeomVerts = 0;
            if (geomVM != null)
            {
                bool smithQ8 = analysisOptions != null
                    && P610AnalysisOptionsHelper.IsP64Q8Deck(analysisOptions);
                nGeomVerts = ApplyGeometry(geomVM, coords, conn, meshType, smithQ8);
            }

            var excavationLayers = ParseLayersBlock(datContent, parsedLayers);

            return new ImportSummary
            {
                NPTypes            = npTypes,
                NNodes             = nn,
                NElements          = nels,
                NBoundaryConditions= bcs.Count,
                NGeomVertices      = nGeomVerts,
                HasFieldStress     = fsModel != null,
                HasSolverControl   = parsedTol.HasValue,
                SolverTol          = parsedTol,
                SolverLimit        = parsedLimit,
                SolverIncs         = parsedIncs,
                SolverLayers       = parsedLayers,
                ExcavationLayers   = excavationLayers,
            };
        }
        catch
        {
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Material properties
    // ─────────────────────────────────────────────────────────────────────────

    private static void ApplyMaterials(MaterialPropertiesViewModel vm, double[,] prop, int npTypes, P610AnalysisOptions? options)
    {
        vm.Materials.Clear();
        var failure = options?.FailureCriterion ?? FailureCriterion.Elastic;
        var materialType = options?.MaterialType ?? MaterialType.Isotropic;
        vm.SelectedFailureCriterion = failure;
        vm.SelectedMaterialType = materialType;
        for (int m = 0; m < npTypes; m++)
        {
            vm.Materials.Add(new MaterialModel
            {
                Id              = m + 1,
                Name            = $"Material {m + 1}",
                FrictionAngle   = prop[0, m],
                Cohesion        = prop[1, m],
                DilationAngle   = prop[2, m],
                UnitWeight      = prop[3, m],
                YoungsModulus   = prop[4, m],
                PoissonsRatio   = prop[5, m],
                FailureCriterion = failure,
                MaterialType = materialType,
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Boundary conditions
    // ─────────────────────────────────────────────────────────────────────────

    private static void ApplyBoundaryConditions(
        BoundaryConditionsViewModel vm,
        List<(int node, int fx, int fy)> bcs)
        => DatBoundaryConditionApplier.Apply(vm, bcs);

    // ─────────────────────────────────────────────────────────────────────────
    //  Geometry — reconstruct domain outline from mesh boundary edges
    // ─────────────────────────────────────────────────────────────────────────

    private static int ApplyGeometry(
        GeometryDefinitionViewModel geomVM,
        (double x, double y)[] coords,
        List<int[]> conn,
        string meshType,
        bool smithEmb2dQ8LocalOrder)
    {
        if (!MeshBoundaryGeometryExtractor.TryExtractOuterBoundary(
                coords, conn, meshType, smithEmb2dQ8LocalOrder, out var boundary))
            return 0;

        // Reset the geometry tab to the same state a user lands in after manually
        // entering points in the Geometry Definition popup: clean vertex/line/edge
        // collections, no horizontal-elevation layering, no internal edges/circles.
        geomVM.GeometryVertexRows.Clear();
        geomVM.GeometryLineSegments.Clear();
        geomVM.GeometryInternalEdges.Clear();
        geomVM.GeometryInternalCircles.Clear();
        geomVM.HasAddedElevationLayer = false;

        // Populate vertices in the same shape as GeometryDefinitionViewModel.AddPointFromInput:
        //   – sequential Index along the polygon
        //   – freshly-allocated StablePointId so labels render as "Point N"
        //   – Owner set so AxisX/AxisY can be projected to canvas coordinates.
        // Boundary edges between consecutive vertices are implicit and rendered
        // via GeometryPathPoints, so we deliberately do NOT push them into
        // GeometryLineSegments (which only holds shared/internal edges and circles).
        for (int i = 0; i < boundary.Count; i++)
        {
            var (x, y) = coords[boundary[i] - 1];
            var display = new GeometryDefinitionViewModel.GeometryVertexDisplay
            {
                Index = i,
                AxisX = x,
                AxisY = y,
                StablePointId = geomVM.AllocateStablePointId(),
            };
            display.Owner = geomVM;
            geomVM.GeometryVertexRows.Add(display);
        }

        // Fit the geometry-definition axes around the loaded outline so the
        // polyline is visible immediately (mirrors what RebuildGeometryAfterProjectLoad
        // expects from a freshly-loaded .xfem project).
        FitAxisBoundsToVertices(geomVM);

        // Rebuild the polyline path / point labels / line-segment grid (same
        // sequence used after loading an .xfem project).
        geomVM.RebuildGeometryAfterProjectLoad();
        geomVM.ProjectSettings?.PullAxisFromGeometry();

        return boundary.Count;
    }

    private static int ConnectivityNodeCount(string meshType)
    {
        if (string.Equals(meshType, "T6", StringComparison.OrdinalIgnoreCase)) return 6;
        if (string.Equals(meshType, "Q4", StringComparison.OrdinalIgnoreCase)) return 4;
        if (string.Equals(meshType, "Q8", StringComparison.OrdinalIgnoreCase)) return 8;
        return 3;
    }

    /// <summary>
    /// Expands the geometry-definition axis bounds so all vertices (and GW overlays)
    /// fit with nice outward-rounded limits shared by every model canvas.
    /// </summary>
    private static void FitAxisBoundsToVertices(GeometryDefinitionViewModel geomVM)
        => geomVM.FitAxisBoundsToVertexExtents();

    // ─────────────────────────────────────────────────────────────────────────
    //  Excavation Layers block parser
    //
    //  Format (matches P610T3DatContentBuilder.BuildLayersBody):
    //    ! <BEGIN:Layers>
    //    ! Excavation layer 1 — N elements      (optional comment)
    //    N
    //    id_1  id_2  id_3  id_4  id_5  id_6  id_7  id_8  id_9  id_10
    //    id_11 id_12 ...                          (10 IDs per line, free format otherwise)
    //    ! Excavation layer 2 — M elements
    //    M
    //    ...
    //    ! <END:Layers>
    //
    //  We tolerate:
    //    * Missing block          → returns empty list (graceful no-op)
    //    * Interleaved comments   → '!' lines stripped before tokenisation
    //    * Empty (0-count) layers → emitted as an empty list, preserving stage count
    //    * Per-layer count larger/smaller than the actual ID count → bounded by EOF
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse the Excavation Layers block. Public so headless smoke tests can drive it without
    /// constructing the full ViewModel graph required by <see cref="Import"/>.
    /// </summary>
    public static List<List<int>> ParseLayersBlock(string datContent, int? expectedLayerCount = null)
    {
        const string begin = "<BEGIN:Layers>";
        const string end   = "<END:Layers>";

        bool inBlock = false;
        var blockLines = new List<string>();

        foreach (var raw in datContent.Split('\n'))
        {
            var t = raw.Trim();
            if (t.Contains(begin)) { inBlock = true;  continue; }
            if (t.Contains(end))   { break; }
            if (!inBlock) continue;
            // Strip inline / full-line comments — keep numeric-only lines for the count/id stream.
            var line = raw;
            var bang = line.IndexOf('!');
            if (bang >= 0) line = line.Substring(0, bang);
            blockLines.Add(line);
        }

        var result = new List<List<int>>();
        if (blockLines.Count == 0) return result;

        var tokens = Tokenise(string.Join("\n", blockLines));

        // Sentinel: stop after `expectedLayerCount` (when supplied via solver-control), otherwise drain until EOF.
        int safeMax = expectedLayerCount.GetValueOrDefault(int.MaxValue);
        if (safeMax < 0) safeMax = int.MaxValue;

        while (tokens.Count > 0 && result.Count < safeMax)
        {
            if (!TryDeqInt(tokens, out int n)) break;
            if (n < 0) n = 0;
            var ids = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                if (!TryDeqInt(tokens, out int id)) break;
                if (id > 0) ids.Add(id);
            }
            result.Add(ids);
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  FieldStress block parser  (C# mirror of read_field_stress_block in C++)
    // ─────────────────────────────────────────────────────────────────────────

    private static FieldStressModel? ParseFieldStressBlock(string datContent)
    {
        const string begin = "<BEGIN:FieldStress>";
        const string end   = "<END:FieldStress>";

        bool inBlock = false;
        var blockLines = new List<string>();

        foreach (var raw in datContent.Split('\n'))
        {
            var t = raw.Trim();
            if (t.Contains(begin)) { inBlock = true;  continue; }
            if (t.Contains(end))   { break; }
            if (!inBlock) continue;
            var line = raw;
            var bang = line.IndexOf('!');
            if (bang >= 0) line = line.Substring(0, bang);
            blockLines.Add(line);
        }

        if (blockLines.Count == 0) return null;

        var tokens = Tokenise(string.Join("\n", blockLines));

        if (!TryDeqInt(tokens, out int mode)) return null;
        if (!TryDeqDbl(tokens, out double p0) || !TryDeqDbl(tokens, out double p1) ||
            !TryDeqDbl(tokens, out double p2) || !TryDeqDbl(tokens, out double p3)) return null;

        var model = new FieldStressModel();
        if (mode == 0)
        {
            model.Global.Mode     = FieldStressMode.Constant;
            model.Global.Sigma1   = p0;
            model.Global.Sigma3   = p1;
            model.Global.SigmaZ   = p2;
            model.Global.AngleDeg = p3;
        }
        else
        {
            model.Global.Mode                   = FieldStressMode.Gravity;
            model.Global.Kx                     = p0;
            model.Global.Kz                     = p1;
            model.Global.AutoSurface            = p2 > 1.0e29;
            model.Global.YSurface               = model.Global.AutoSurface ? 0.0 : p2;
            model.Global.UseEffectiveStressRatio= p3 != 0.0;
        }

        if (!TryDeqInt(tokens, out int nOverrides)) return model;

        for (int i = 0; i < nOverrides; i++)
        {
            if (!TryDeqInt(tokens, out int matIdx) ||
                !TryDeqInt(tokens, out int ovMode)) break;
            if (!TryDeqDbl(tokens, out double q0) || !TryDeqDbl(tokens, out double q1) ||
                !TryDeqDbl(tokens, out double q2) || !TryDeqDbl(tokens, out double q3)) break;

            var spec = new FieldStressSpec();
            if (ovMode == 0)
            {
                spec.Mode = FieldStressMode.Constant;
                spec.Sigma1 = q0; spec.Sigma3 = q1; spec.SigmaZ = q2; spec.AngleDeg = q3;
            }
            else
            {
                spec.Mode        = FieldStressMode.Gravity;
                spec.Kx          = q0;
                spec.Kz          = q1;
                spec.AutoSurface = q2 > 1.0e29;
                spec.YSurface    = spec.AutoSurface ? 0.0 : q2;
                spec.UseEffectiveStressRatio = q3 != 0.0;
            }
            model.Overrides.Add(new FieldStressOverride
            {
                MaterialIndex = matIdx,
                Spec          = spec,
                MaterialName  = $"Material {matIdx}",
            });
        }

        return model;
    }

    /// <summary>
    /// Reads the p610 elastic / elastoplastic traction-group form:
    /// <code>
    ///   n_traction_groups
    ///   per group:
    ///     mode  p0  p1  p2          ! mode=0 direct: sxx=p0, syy=p1, sxy=p2
    ///     n_edges
    ///     n_edges rows: nodeA  nodeB  [nodeC]   (nodeC = optional mid-edge node, T6/Q8)
    /// </code>
    /// and maps it to the Load Conditions ▸ Field Stress Traction mode (auto-selected
    /// when an explicit traction block is present). The scanner skips the
    /// mesh / BC / output-node prelude using flat tokens, then switches to a line-aware
    /// reader so it can detect "row ended" for the variable edge widths (2 nodes for
    /// T3/Q4, 3 nodes for T6/Q8). Captures every group plus its edges into
    /// <see cref="FieldStressModel.TractionGroups"/> so the GUI can display and round-trip them.
    ///
    /// The elastoplastic .dat also has a numeric solver-control line
    /// (<c>tol limit incs layers</c>) between the output-node block and the traction block;
    /// the elastic .dat has only a comment-only stub there. We disambiguate by peeking the
    /// next token: <c>tol</c> always carries a decimal point or scientific exponent (e.g.
    /// "0.001", "1e-3"), while <c>n_traction_groups</c> is always a pure integer (e.g. "1").
    /// </summary>
    private static FieldStressModel? ParseDirectTractionTensor(string datContent, string meshType)
    {
        var cur = new TokenCursor(datContent);

        if (!cur.NextInt(out int nels) ||
            !cur.NextInt(out int nn) ||
            !cur.NextInt(out int npTypes)) return null;

        for (int i = 0; i < 7 * npTypes; i++)
            if (!cur.NextDouble(out _)) return null;

        if (npTypes > 1)
            for (int i = 0; i < nels; i++)
                if (!cur.NextInt(out _)) return null;

        for (int i = 0; i < nn * 2; i++)
            if (!cur.NextDouble(out _)) return null;

        int connNod = ConnectivityNodeCount(meshType);
        for (int i = 0; i < nels * connNod; i++)
            if (!cur.NextInt(out _)) return null;

        if (!cur.NextInt(out int nr)) return null;
        for (int i = 0; i < nr * 3; i++)
            if (!cur.NextInt(out _)) return null;

        // Output-node section: nouts followed by nouts node ids.
        if (!cur.NextInt(out int nouts)) return null;
        for (int i = 0; i < nouts; i++)
            if (!cur.NextInt(out _)) return null;

        // Detect-and-skip the optional elastoplastic solver-control line. We peek the next
        // token: "tol" always contains a '.' or [eE] (real-valued tolerance), whereas
        // n_traction_groups is a plain integer. Without this disambiguation, an elastic .dat
        // whose first traction-block tokens happen to parse as ints would accidentally have
        // them swallowed (kirsch_quarter1.dat: "1 0 -30000 -30000" would be eaten as the
        // solver-control line, leaving the cursor at "10" and the subsequent edge data).
        if (cur.TryPeekToken(out string nextTok)
            && (nextTok.IndexOf('.') >= 0 || nextTok.IndexOf('e') >= 0 || nextTok.IndexOf('E') >= 0))
        {
            if (!cur.NextDouble(out _)) return null; // tol
            if (!cur.NextInt(out _))    return null; // limit
            if (!cur.NextInt(out _))    return null; // incs
            if (!cur.NextInt(out _))    return null; // layers
        }

        // ── Traction block (line-aware so we can detect 2- vs 3-node edges) ──
        if (!cur.NextInt(out int nGroups) || nGroups <= 0) return null;

        var groups = new List<TractionGroup>(nGroups);
        for (int g = 0; g < nGroups; g++)
        {
            if (!cur.NextInt(out int mode) ||
                !cur.NextDouble(out double p0) ||
                !cur.NextDouble(out double p1) ||
                !cur.NextDouble(out double p2)) return null;

            if (!cur.NextInt(out int nEdges) || nEdges < 0) return null;

            var grp = new TractionGroup
            {
                Mode = mode,
                Sxx  = p0,
                Syy  = p1,
                Txy  = p2,
            };

            for (int e = 0; e < nEdges; e++)
            {
                if (!cur.NextInt(out int nA)) return null;
                int edgeLine = cur.LastConsumedLine;
                if (!cur.NextInt(out int nB)) return null;

                int nC = 0;
                // Peek for an optional 3rd node ID on the same source line (T6/Q8 mid-edge).
                if (cur.TryPeekLine(out int nextLine) && nextLine == edgeLine)
                {
                    if (cur.NextInt(out int v)) nC = v;
                }

                grp.Edges.Add(new TractionEdge { NodeA = nA, NodeB = nB, NodeC = nC });
            }

            groups.Add(grp);
        }

        // First mode-0 group seeds the legacy Global.Direct{Sxx,Syy,Txy} values that the rest
        // of the app already binds to. Fall back to the first group of any mode if no mode=0
        // group is present (covers far-field "mode=1" definitions).
        var seed = groups.FirstOrDefault(grp => grp.Mode == 0) ?? groups.FirstOrDefault();
        if (seed == null) return null;

        // Select FieldStressMode.Traction so the GUI's Load Conditions ▸ Field Stress
        // ▸ "Traction" radio is auto-checked and the parsed n_traction_groups list
        // becomes immediately visible/editable. The Direct-tensor seed is preserved
        // on Global so switching back to "Direct tensor" still shows sensible values.
        var model = new FieldStressModel();
        model.Global.Mode      = FieldStressMode.Traction;
        model.Global.DirectSxx = seed.Sxx;
        model.Global.DirectSyy = seed.Syy;
        model.Global.DirectTxy = seed.Txy;

        foreach (var grp in groups) model.TractionGroups.Add(grp);
        return model;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Tokeniser helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static Queue<string> Tokenise(string content)
    {
        var q = new Queue<string>();
        foreach (var raw in content.Split('\n'))
        {
            var line = raw;
            var bang = line.IndexOf('!');
            if (bang >= 0) line = line.Substring(0, bang);
            foreach (var tok in line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                q.Enqueue(tok);
        }
        return q;
    }

    private static bool TryDeqInt(Queue<string> q, out int v)
    {
        v = 0;
        return q.Count > 0 && int.TryParse(q.Dequeue(), out v);
    }

    private static bool TryDeqDbl(Queue<string> q, out double v)
    {
        v = 0;
        return q.Count > 0 && TryParseDouble(q.Dequeue(), out v);
    }

    private static bool TryParseInt(string token, out int v)
        => int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    private static bool TryParseDouble(string token, out double v)
        => double.TryParse(
            token,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out v);

    /// <summary>
    /// Sequential token reader that also tracks the source line of every consumed/peeked token.
    /// Used by the traction-block parser to detect edge-row boundaries (2 vs 3 node IDs per row).
    /// </summary>
    private sealed class TokenCursor
    {
        private readonly List<(int Line, string Token)> _flat;
        private int _idx;

        public int LastConsumedLine { get; private set; } = -1;

        public TokenCursor(string content)
        {
            _flat = new List<(int, string)>();
            int lineNo = 0;
            foreach (var raw in content.Split('\n'))
            {
                var line = raw;
                var bang = line.IndexOf('!');
                if (bang >= 0) line = line.Substring(0, bang);
                foreach (var tok in line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                    _flat.Add((lineNo, tok));
                lineNo++;
            }
        }

        public bool NextInt(out int v)
        {
            if (_idx >= _flat.Count) { v = 0; return false; }
            var (line, tok) = _flat[_idx];
            if (!int.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                return false;
            LastConsumedLine = line;
            _idx++;
            return true;
        }

        public bool NextDouble(out double v)
        {
            if (_idx >= _flat.Count) { v = 0; return false; }
            var (line, tok) = _flat[_idx];
            if (!double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return false;
            LastConsumedLine = line;
            _idx++;
            return true;
        }

        public bool TryPeekLine(out int line)
        {
            if (_idx >= _flat.Count) { line = -1; return false; }
            line = _flat[_idx].Line;
            return true;
        }

        /// <summary>
        /// Peek the next un-consumed token text without advancing. Returns false at EOF.
        /// Used by the traction-block parser to detect whether the elastoplastic-only
        /// "tol limit incs layers" line is present (tol contains a decimal point) or
        /// whether the next token is already n_traction_groups (a pure integer).
        /// </summary>
        public bool TryPeekToken(out string token)
        {
            if (_idx >= _flat.Count) { token = ""; return false; }
            token = _flat[_idx].Token;
            return true;
        }
    }
}
