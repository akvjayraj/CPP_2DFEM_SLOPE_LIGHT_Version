using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>Builds a p610_t3 .dat input file from current mesh, materials, BCs, and staged excavation.</summary>
public static class P610T3DatContentBuilder
{
    public const string SolverTag = "! solver:p610_t3";

    /// <summary>Ordered list of fenced sections that make up a p610_t3 .dat script.</summary>
    public enum Section
    {
        Header,
        Materials,
        Mesh,
        Bcs,
        OutputNodes,
        SolverControl,
        Layers,
    }

    /// <summary>Order the wizard emits sections in (matches p610_t3 .dat layout).</summary>
    public static readonly Section[] SectionOrder =
    {
        Section.Header,
        Section.Materials,
        Section.Mesh,
        Section.Bcs,
        Section.OutputNodes,
        Section.SolverControl,
        Section.Layers,
    };

    /// <summary>Returns true when the content was generated for p610_t3 (tag present in first few lines).</summary>
    public static bool IsP610T3Content(string content)
    {
        if (string.IsNullOrEmpty(content)) return false;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.Contains("solver:p610_t3", StringComparison.OrdinalIgnoreCase)) return true;
            if (!line.StartsWith('!')) break; // stop scanning after first non-comment line
        }
        return false;
    }

    // ─── Public section wrappers ─────────────────────────────────────────────

    public static string BuildHeader(MeshViewModel meshVM)
        => BuildHeader(meshVM, null, null);

    public static string BuildHeader(MeshViewModel meshVM, MaterialPropertiesViewModel? materialsVM)
        => BuildHeader(meshVM, materialsVM, null);

    public static string BuildHeader(
        MeshViewModel meshVM,
        MaterialPropertiesViewModel? materialsVM,
        FEAnalysisViewModel? feVM)
        => Wrap(Section.Header, BuildHeaderBody(meshVM, materialsVM, feVM));

    public static string BuildMaterials(MaterialPropertiesViewModel materialsVM)
        => Wrap(Section.Materials, BuildMaterialsBody(materialsVM));

    public static string BuildMesh(MeshViewModel meshVM, MaterialPropertiesViewModel materialsVM)
        => Wrap(Section.Mesh, BuildMeshBody(meshVM, materialsVM, null));

    public static string BuildMesh(
        MeshViewModel meshVM,
        MaterialPropertiesViewModel materialsVM,
        GeometryDefinitionViewModel? geometryVM)
        => Wrap(Section.Mesh, BuildMeshBody(meshVM, materialsVM, geometryVM));

    public static string BuildBcs(BoundaryConditionsViewModel bcVM, MeshViewModel meshVM)
        => Wrap(Section.Bcs, BuildBcsBody(bcVM, meshVM));

    public static string BuildOutputNodes(BoundaryConditionsViewModel bcVM, MeshViewModel meshVM)
        => Wrap(Section.OutputNodes, BuildOutputNodesBody(bcVM, meshVM));

    public static string BuildSolverControl(MeshViewModel meshVM, StagedConstructionViewModel? stagedVM)
        => BuildSolverControl(meshVM, stagedVM, null);

    /// <summary>
    /// Build the SolverControl section using the values held on <paramref name="feVM"/>
    /// (tol, limit, incs, layers override). Falls back to legacy hardcoded defaults when
    /// <paramref name="feVM"/> is null. Pairs with <see cref="BuildLayers(MeshViewModel, StagedConstructionViewModel?, FEAnalysisViewModel?)"/>
    /// so the layer count printed here matches the number of layer blocks emitted next.
    /// </summary>
    public static string BuildSolverControl(MeshViewModel meshVM, StagedConstructionViewModel? stagedVM, FEAnalysisViewModel? feVM)
        => Wrap(Section.SolverControl, BuildSolverControlBody(meshVM, stagedVM, feVM));

    public static string BuildLayers(MeshViewModel meshVM, StagedConstructionViewModel? stagedVM)
        => BuildLayers(meshVM, stagedVM, null);

    /// <summary>
    /// Build the Layers section honouring <see cref="FEAnalysisViewModel.LayersOverride"/>:
    /// truncates auto-derived layers when override &lt; auto-count, pads with empty (0-count)
    /// blocks when override &gt; auto-count, and emits nothing when override == 0
    /// (infinite-medium MC path).
    /// </summary>
    public static string BuildLayers(MeshViewModel meshVM, StagedConstructionViewModel? stagedVM, FEAnalysisViewModel? feVM)
        => BuildLayers(meshVM, stagedVM, feVM, null);

    public static string BuildLayers(
        MeshViewModel meshVM,
        StagedConstructionViewModel? stagedVM,
        FEAnalysisViewModel? feVM,
        LoadConditionsViewModel? loadConditionsVM)
        => Wrap(Section.Layers, BuildLayersBody(meshVM, stagedVM, feVM, loadConditionsVM));

    /// <summary>
    /// Skeleton emitted when the editor first switches into p610_t3 mode.
    /// Contains the solver tag, banner, and seven fenced placeholders the user fills via wizard buttons.
    /// </summary>
    public static string SkeletonHeader()
    {
        var sb = new StringBuilder();
        sb.AppendLine(SolverTag);
        sb.AppendLine("! p610_t3 — elasto-plastic Mohr-Coulomb excavation (T3 plane-strain)");
        sb.AppendLine("! Unit system: lengths m | stresses & moduli kPa | unit weight kN/m³ | angles °");
        sb.AppendLine($"! Skeleton created {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("! Click the numbered buttons (1..7) above the editor to emit each section.");
        sb.AppendLine("! Hand-edits outside the sentinel markers are preserved when re-emitting.");
        sb.AppendLine();

        int step = 1;
        foreach (var s in SectionOrder)
        {
            sb.AppendLine(Wrap(s, PlaceholderBody(step, s)).TrimEnd('\r', '\n'));
            sb.AppendLine();
            step++;
        }
        return sb.ToString();
    }

    /// <summary>Full .dat output (used by "Emit All"): skeleton tag + every section concatenated in solver order.</summary>
    public static string Build(
        MeshViewModel meshVM,
        MaterialPropertiesViewModel materialsVM,
        BoundaryConditionsViewModel bcVM,
        StagedConstructionViewModel? stagedVM = null,
        FEAnalysisViewModel? feVM = null,
        LoadConditionsViewModel? loadConditionsVM = null,
        GroundwaterViewModel? groundwaterVM = null,
        SupportViewModel? supportVM = null)
    {
        ValidateMesh(meshVM);
        // #region agent log
        DebugAgentLog.Write("H9", "P610T3DatContentBuilder.cs:Build",
            "Mesh export inventory",
            ProbeMeshExportInventory(meshVM, materialsVM));
        // #endregion

        var sb = new StringBuilder();
        sb.AppendLine(SolverTag);
        sb.AppendLine("! p610_t3 — elasto-plastic Mohr-Coulomb excavation (T3 plane-strain)");
        sb.AppendLine("! Unit system: lengths m | stresses & moduli kPa | unit weight kN/m³ | angles °");
        sb.AppendLine($"! Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        sb.Append(BuildHeader(meshVM, materialsVM, feVM));
        sb.AppendLine();
        sb.Append(BuildMaterials(materialsVM));
        sb.AppendLine();
        sb.Append(BuildMesh(meshVM, materialsVM, meshVM.GeometryDefinitionVM));
        sb.AppendLine();
        sb.Append(BuildBcs(bcVM, meshVM));
        sb.AppendLine();
        sb.Append(BuildOutputNodes(bcVM, meshVM));
        sb.AppendLine();
        sb.Append(BuildSolverControl(meshVM, stagedVM, feVM));
        sb.AppendLine();
        sb.Append(BuildLayers(meshVM, stagedVM, feVM, loadConditionsVM));

        var text = sb.ToString();
        if (loadConditionsVM?.IsSeismicLoadUiEnabled == true)
        {
            DatPseudoStaticExporter.TryPatchPseudoStatic(
                text,
                loadConditionsVM.SeismicLegendKh,
                loadConditionsVM.SeismicLegendKv,
                out text,
                out _);
        }

        // Embed Groundwater into the T3 deck (same as SrfExplicitDatWriter for Q4/Q8),
        // so Static WT / Ru / grids reach SRF without depending on a later Script sync.
        GroundwaterModel? pondedGw = null;
        if (groundwaterVM != null)
        {
            var displayModel = groundwaterVM.BuildModel();
            if (materialsVM.Materials is { Count: > 0 }
                && materialsVM.Materials.Any(m => m.IsSaturatedEnabled))
            {
                displayModel.GammaSat = materialsVM.Materials
                    .Select(m => m.IsSaturatedEnabled ? m.SaturatedUnitWeight : m.UnitWeight)
                    .ToList();
            }
            pondedGw = displayModel;
            var gw = GroundwaterDatImporter.CloneForSolverExport(displayModel, text);
            text = GroundwaterDatWriter.MergeGroundwaterSection(text, gw);
            if (GroundwaterDatWriter.ShouldAppendNumericTail(text, gw))
                text = GroundwaterDatWriter.AppendNumericTail(text, gw);
        }

        if (supportVM != null)
            text = SupportDatWriter.MergeFromViewModels(text, supportVM.PropertySets, supportVM.Elements);
        if (meshVM.GeometryDefinitionVM?.GeologicalFeatures is { Count: > 0 } joints)
            text = JointDatWriter.MergeSection(
                text,
                JointDatWriter.BuildBlock(joints, meshVM.GeometryDefinitionVM.JointPropertySets, meshVM.JointInterfaceElements));

        if (loadConditionsVM != null && meshVM.GeometryDefinitionVM != null)
        {
            text = DistributedEdgeLoadDatWriter.MergeFromViewModels(
                text, loadConditionsVM, meshVM, meshVM.GeometryDefinitionVM, pondedGw);
            text = NodalPointLoadDatWriter.MergeFromViewModels(
                text, loadConditionsVM, meshVM, meshVM.GeometryDefinitionVM);
        }

        return text;
    }

    // ─── Merge helper: swap a single fenced block in an existing script ─────

    /// <summary>
    /// Replace the content between ! &lt;BEGIN:section&gt; and ! &lt;END:section&gt; with <paramref name="newBlock"/>.
    /// If the section markers are missing, the block is appended before the next existing section marker
    /// (following <see cref="SectionOrder"/>) or at the end of the script.
    /// </summary>
    public static string MergeSection(string existing, Section section, string newBlock)
    {
        existing ??= "";
        // Ensure skeleton/tag is present; if not, treat as empty and start from skeleton.
        if (!IsP610T3Content(existing))
            existing = SkeletonHeader();

        newBlock = NormalizeBlock(newBlock);
        var pattern = $@"! <BEGIN:{section}>[\s\S]*?! <END:{section}>\s*";
        var rx = new Regex(pattern, RegexOptions.Multiline);
        if (rx.IsMatch(existing))
        {
            return rx.Replace(existing, newBlock + Environment.NewLine, 1);
        }

        // Section marker missing — insert before next known section or append at end.
        int sectionIdx = Array.IndexOf(SectionOrder, section);
        for (int i = sectionIdx + 1; i < SectionOrder.Length; i++)
        {
            var nextPattern = $@"! <BEGIN:{SectionOrder[i]}>";
            var nextRx = new Regex(nextPattern);
            var m = nextRx.Match(existing);
            if (m.Success)
            {
                return existing.Insert(m.Index, newBlock + Environment.NewLine + Environment.NewLine);
            }
        }

        var trailing = existing.EndsWith("\n") ? "" : Environment.NewLine;
        return existing + trailing + Environment.NewLine + newBlock + Environment.NewLine;
    }

    /// <summary>Returns true when the fenced section in <paramref name="content"/> contains non-placeholder content.</summary>
    public static bool IsSectionDone(string content, Section section)
    {
        if (string.IsNullOrEmpty(content)) return false;
        var pattern = $@"! <BEGIN:{section}>([\s\S]*?)! <END:{section}>";
        var m = Regex.Match(content, pattern);
        if (!m.Success) return false;
        var body = m.Groups[1].Value;
        // Done = body has a non-comment, non-empty line (ignore awaiting-placeholder).
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("!")) continue;
            return true;
        }
        return false;
    }

    // ─── Internal body builders (no fence) ──────────────────────────────────

    private static string BuildHeaderBody(
        MeshViewModel meshVM,
        MaterialPropertiesViewModel? materialsVM,
        FEAnalysisViewModel? feVM)
    {
        ValidateMesh(meshVM);
        var rows = meshVM.MeshInformationRows.ToList();
        int nn = CollectNodeCoords(rows).Keys.Max();
        var options = P610AnalysisOptionsHelper.BuildFromViewModels(materialsVM, meshVM, feVM);
        var meshType = options.MeshType;
        int nels = CountExportElements(rows, meshType);
        int npTypes = ResolveNpTypes(rows, materialsVM);

        var sb = new StringBuilder();
        sb.AppendLine(P610AnalysisOptionsHelper.BuildBlock(options));
        sb.AppendLine("! nels  nn  np_types");
        sb.Append($"{nels}  {nn}  {npTypes}");
        return sb.ToString();
    }

    // np_types must equal the number of material rows actually emitted in the
    // Materials block, otherwise the C++ solver's `dat >> prop[p][m]` runs off
    // into the coord/connectivity blocks and dereferences prop[p][-1].
    private static int ResolveNpTypes(List<MeshInformationRow> rows, MaterialPropertiesViewModel? materialsVM)
    {
        int matCount = materialsVM?.Materials?.Count ?? 0;
        return matCount > 0 ? matCount : 1;
    }

    private static string BuildMaterialsBody(MaterialPropertiesViewModel materialsVM)
    {
        var mats = materialsVM?.Materials?.ToList() ?? new List<MaterialModel>();

        var sb = new StringBuilder();
        sb.AppendLine("! phi(°)  c(kPa)  psi(°)  gamma(kN/m³)  E(kPa)  v(-)  K0(-)  — one row per property, one value per material");
        if (mats.Count == 0)
        {
            sb.Append("30.0  10.0  0.0  20.0  50000.0  0.30  0.50");
        }
        else
        {
            static double K0(double phiDeg) => Math.Max(0.1, 1.0 - Math.Sin(phiDeg * Math.PI / 180.0));
            sb.AppendLine(ValRow(mats.Select(m => m.FrictionAngle)));
            sb.AppendLine(ValRow(mats.Select(m => m.Cohesion)));
            sb.AppendLine(ValRow(mats.Select(m => m.DilationAngle)));
            sb.AppendLine(ValRow(mats.Select(m => m.UnitWeight)));
            sb.AppendLine(ValRow(mats.Select(m => m.YoungsModulus)));
            sb.AppendLine(ValRow(mats.Select(m => m.PoissonsRatio)));
            sb.Append(ValRow(mats.Select(m => K0(m.FrictionAngle))));
            sb.AppendLine();
            sb.AppendLine("! <BEGIN:MaterialModelOptions>");
            for (int i = 0; i < mats.Count; i++)
            {
                var m = mats[i];
                sb.AppendLine($"! material_index {i + 1}");
                sb.AppendLine($"! failure_criterion {m.FailureCriterion}");
                sb.AppendLine($"! material_type {m.MaterialType}");
                AppendMaterialModelOptionLines(sb, m);
            }
            sb.Append("! <END:MaterialModelOptions>");
        }
        return sb.ToString();
    }

    private static string BuildMeshBody(
        MeshViewModel meshVM,
        MaterialPropertiesViewModel materialsVM,
        GeometryDefinitionViewModel? geometryVM)
    {
        ValidateMesh(meshVM);
        var rows = meshVM.MeshInformationRows.ToList();
        int npTypes = ResolveNpTypes(rows, materialsVM);
        if (npTypes > 1)
        {
            Q8MeshMaterialLayerPlanner.TryRefreshMeshInformationEtypes(
                rows,
                geometryVM ?? meshVM.GeometryDefinitionVM,
                materialsVM,
                out _);
        }

        var nodeCoords = CollectNodeCoords(rows);
        int nn = nodeCoords.Keys.Max();
        var options = P610AnalysisOptionsHelper.BuildFromViewModels(materialsVM, meshVM);
        var meshType = options.MeshType;

        var sb = new StringBuilder();
        if (npTypes > 1)
        {
            sb.AppendLine("! etype (1-based material index per element, 20 per line)");
            int col = 0;
            foreach (var r in rows)
            {
                int et = r.Etype < 1 ? 1 : Math.Min(r.Etype, npTypes);
                int copies = meshType == "T3" && r.IsQuad && r.Node4 > 0 ? 2 : 1;
                for (int c = 0; c < copies; c++)
                {
                    sb.Append($"{et,3}");
                    if (++col % 20 == 0) sb.AppendLine();
                }
            }
            if (col % 20 != 0) sb.AppendLine();
            sb.AppendLine();
        }

        sb.AppendLine("! x(m)  y(m)  — nodal coordinates, 1..nn");
        for (int id = 1; id <= nn; id++)
        {
            var (x, y) = nodeCoords.TryGetValue(id, out var pt) ? pt : (0.0, 0.0);
            sb.AppendLine($"{x:G10}  {y:G10}");
        }
        sb.AppendLine();

        sb.AppendLine($"! connectivity ({meshType}, 1-based)");
        var conn = EnumerateExportConnectivity(rows, meshType, nodeCoords).ToList();
        for (int i = 0; i < conn.Count; i++)
        {
            sb.Append(string.Join("  ", conn[i]));
            if (i < conn.Count - 1) sb.AppendLine();
        }
        return sb.ToString();
    }

    private static int CountExportElements(IReadOnlyList<MeshInformationRow> rows, string meshType)
    {
        if (meshType != "T3") return rows.Count;
        int n = 0;
        foreach (var r in rows)
            n += r.IsQuad && r.Node4 > 0 ? 2 : 1;
        return n;
    }

    private static IEnumerable<int[]> EnumerateExportConnectivity(
        IReadOnlyList<MeshInformationRow> rows,
        string meshType,
        SortedDictionary<int, (double X, double Y)>? nodeCoords = null)
    {
        nodeCoords ??= CollectNodeCoords(rows);
        foreach (var r in rows)
        {
            if (meshType == "T6")
            {
                if (r.Node4 <= 0 || r.Node5 <= 0 || r.Node6 <= 0)
                    throw new InvalidOperationException("T6 mesh type selected but mesh rows do not contain Node4/Node5/Node6 midside nodes.");
                yield return new[] { r.Node1, r.Node2, r.Node3, r.Node4, r.Node5, r.Node6 };
            }
            else if (meshType == "Q4")
            {
                if (r.IsT3 || r.Node4 <= 0)
                    throw new InvalidOperationException(
                        "Q4 SRF requires an all-quadrilateral mesh. Regenerate without triangle fallbacks, or use the T3 solver path.");
                yield return new[] { r.Node1, r.Node2, r.Node3, r.Node4 };
            }
            else if (meshType == "Q8")
            {
                if (r.Node4 <= 0 || r.Node5 <= 0 || r.Node6 <= 0 || r.Node7 <= 0 || r.Node8 <= 0)
                    throw new InvalidOperationException(
                        "Q8 mesh type selected but mesh rows do not contain Node4..Node8 corner+midside connectivity.");
                yield return new[] { r.Node1, r.Node2, r.Node3, r.Node4, r.Node5, r.Node6, r.Node7, r.Node8 };
            }
            else
            {
                if (r.IsQuad && r.Node4 > 0)
                {
                    foreach (var tri in SplitQuadToT3(r.Node1, r.Node2, r.Node3, r.Node4, nodeCoords))
                        yield return tri;
                }
                else
                    yield return OrientT3Nodes(r.Node1, r.Node2, r.Node3, nodeCoords);
            }
        }
    }

    /// <summary>
    /// Split a Q4 into two T3 elements using the better diagonal and CCW node order
    /// (p610 T3 uses signed detJ in bee; inverted triangles destabilize the solve).
    /// </summary>
    private static IEnumerable<int[]> SplitQuadToT3(
        int n1, int n2, int n3, int n4,
        SortedDictionary<int, (double X, double Y)> coords)
    {
        double a13_1 = Math.Abs(SignedTriangleArea(n1, n2, n3, coords));
        double a13_2 = Math.Abs(SignedTriangleArea(n1, n3, n4, coords));
        double a24_1 = Math.Abs(SignedTriangleArea(n1, n2, n4, coords));
        double a24_2 = Math.Abs(SignedTriangleArea(n2, n3, n4, coords));
        double score13 = Math.Min(a13_1, a13_2);
        double score24 = Math.Min(a24_1, a24_2);
        if (score24 > score13)
        {
            yield return OrientT3Nodes(n1, n2, n4, coords);
            yield return OrientT3Nodes(n2, n3, n4, coords);
        }
        else
        {
            yield return OrientT3Nodes(n1, n2, n3, coords);
            yield return OrientT3Nodes(n1, n3, n4, coords);
        }
    }

    private static int[] OrientT3Nodes(
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

    private static string BuildBcsBody(BoundaryConditionsViewModel bcVM, MeshViewModel meshVM)
    {
        ValidateMesh(meshVM);
        var rows = meshVM.MeshInformationRows.ToList();
        int nn = CollectNodeCoords(rows).Keys.Max();
        var fixMap = BoundaryConditionFixityMapper.BuildFixMap(bcVM, rows, nn);

        var sb = new StringBuilder();
        sb.AppendLine("! nr  (restrained node count; rows: node fix_x fix_y; 0 = restrained, 1 = free)");
        sb.AppendLine($"{fixMap.Count}");
        int idx = 0;
        foreach (var (nodeId, (fx, fy)) in fixMap.OrderBy(kv => kv.Key))
        {
            sb.Append($"{nodeId}  {fx}  {fy}");
            if (idx < fixMap.Count - 1) sb.AppendLine();
            idx++;
        }
        return sb.ToString();
    }

    private static string BuildOutputNodesBody(BoundaryConditionsViewModel bcVM, MeshViewModel meshVM)
    {
        ValidateMesh(meshVM);
        var rows = meshVM.MeshInformationRows.ToList();
        int nn = CollectNodeCoords(rows).Keys.Max();
        var fixMap = BoundaryConditionFixityMapper.BuildFixMap(bcVM, rows, nn);

        var outNodes = fixMap.Keys.OrderBy(x => x).Take(20).ToList();
        if (outNodes.Count == 0)
            outNodes = Enumerable.Range(1, Math.Min(5, nn)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("! nouts  (output node count, then node numbers)");
        sb.AppendLine($"{outNodes.Count}");
        sb.Append(string.Join("  ", outNodes));
        return sb.ToString();
    }

    private static string BuildSolverControlBody(MeshViewModel meshVM, StagedConstructionViewModel? stagedVM, FEAnalysisViewModel? feVM)
    {
        ValidateMesh(meshVM);
        var rows = meshVM.MeshInformationRows.ToList();
        int autoLayers = ResolveAutoLayerElements(rows, stagedVM, feVM).Count;
        int    layers  = feVM?.ResolveEffectiveLayers(autoLayers) ?? autoLayers;
        if (feVM?.IsSrfSolverMode == true && layers < 1)
            layers = 1;
        double tol     = feVM?.ConvergenceTolerance ?? 1.0e-5;
        int    limit   = Math.Max(1, feVM?.MaxIterations ?? 250);
        int    incs    = Math.Max(1, feVM?.LoadIncrements ?? 5);
        if (feVM?.IsSrfSolverMode == true)
            incs = Math.Max(20, incs);

        var sb = new StringBuilder();
        sb.AppendLine("! tol  limit  incs  layers");
        sb.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0:G6}  {1}  {2}  {3}", tol, limit, incs, layers));
        if (feVM?.IsSrfSolverMode == true)
        {
            var fValues = CollectSrfValues(feVM);
            int nsrf = Math.Max(1, feVM.SrfTrialCount);
            sb.AppendLine();
            sb.AppendLine(DatSolverControlExporter.FormatSrfNsrfCommentLine(nsrf));
            sb.AppendLine(DatSolverControlExporter.FormatSrfTrialsCommentLine(fValues));
        }
        return sb.ToString();
    }

    private static void AppendMaterialModelOptionLines(StringBuilder sb, MaterialModel m)
    {
        if (m.MaterialType == MaterialType.TransverselyIsotropic)
        {
            sb.AppendLine(
                $"! ti Eh={m.YoungsModulusH:G10} Ev={m.YoungsModulusV:G10} nuhh={m.PoissonsRatioHH:G10} nuvh={m.PoissonsRatioVH:G10} Gvh={m.ShearModulusVH:G10}");
        }

        if (m.FailureCriterion is FailureCriterion.HoekBrown or FailureCriterion.GeneralizedHoekBrown)
        {
            sb.AppendLine(
                $"! hb ucs={m.Ucs:G10} mi={m.HoekBrownMi:G10} gsi={m.Gsi:G10} disturbance={m.Disturbance:G10} mr={m.ModulusRatio:G10}");
        }

        if (m.FailureCriterion == FailureCriterion.DruckerPrager)
            sb.AppendLine($"! dp alpha={m.DpAlpha:G10} k={m.DpK:G10}");

        if (m.FailureCriterion is FailureCriterion.MohrCoulomb
            or FailureCriterion.ElastoPlastic
            or FailureCriterion.HoekBrown
            or FailureCriterion.GeneralizedHoekBrown
            or FailureCriterion.DruckerPrager)
        {
            sb.AppendLine($"! strength tensile_cutoff={m.TensileCutoff:G10}");
        }
    }

    private static List<double> CollectSrfValues(FEAnalysisViewModel feVM)
    {
        var fValues = feVM.SrfValues.Select(e => e.Value).ToList();
        int nsrf = Math.Max(1, feVM.SrfTrialCount);
        while (fValues.Count < nsrf)
            fValues.Add(fValues.Count == 0 ? 1.0 : Math.Round(fValues[^1] + 0.1, 4));
        if (fValues.Count > nsrf)
            fValues = fValues.Take(nsrf).ToList();
        return fValues;
    }

    private static string BuildLayersBody(
        MeshViewModel meshVM,
        StagedConstructionViewModel? stagedVM,
        FEAnalysisViewModel? feVM,
        LoadConditionsViewModel? loadConditionsVM)
    {
        ValidateMesh(meshVM);
        var rows = meshVM.MeshInformationRows.ToList();
        var layerElements = ResolveAutoLayerElements(rows, stagedVM, feVM);

        // Apply the user's layers override (when set):
        //   target == 0                  -> infinite-medium MC traction groups (not excavation).
        //   target <  auto-count         -> truncate to the first `target` auto layers.
        //   target >  auto-count         -> pad with empty (0-count) layer blocks.
        int target = feVM?.ResolveEffectiveLayers(layerElements.Count) ?? layerElements.Count;
        if (feVM?.IsSrfSolverMode == true && target < 1)
            target = 1;
        if (target == 0)
            return BuildInfiniteMediumTractionBody(meshVM, loadConditionsVM);

        if (target < layerElements.Count)
        {
            layerElements = layerElements.Take(target).ToList();
        }
        else if (target > layerElements.Count)
        {
            int pad = target - layerElements.Count;
            for (int i = 0; i < pad; i++)
                layerElements.Add(new List<int>());
        }

        var sb = new StringBuilder();
        for (int li = 0; li < layerElements.Count; li++)
        {
            var layer = layerElements[li];
            sb.AppendLine($"! Excavation layer {li + 1} — {layer.Count} elements");
            sb.AppendLine($"{layer.Count}");
            if (layer.Count == 0)
            {
                // Empty padded layer: keep the count line, no element ids; trailing newline only when
                // there's a following layer block.
                if (li < layerElements.Count - 1)
                    sb.AppendLine();
                continue;
            }
            for (int i = 0; i < layer.Count; i++)
            {
                sb.Append(layer[i]);
                if ((i + 1) % 10 == 0 || i == layer.Count - 1)
                {
                    if (!(li == layerElements.Count - 1 && i == layer.Count - 1))
                        sb.AppendLine();
                }
                else
                {
                    sb.Append("  ");
                }
            }
        }
        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>
    /// Numeric traction-group tail for <c>layers == 0</c>. Always emits at least
    /// <c>n_traction_groups = 0</c> so the solver does not read past EOF.
    /// Line loads (axis coordinates) become mode-0 groups on matching boundary edges.
    /// </summary>
    private static string BuildInfiniteMediumTractionBody(
        MeshViewModel meshVM,
        LoadConditionsViewModel? loadConditionsVM)
    {
        var rows = meshVM.MeshInformationRows.ToList();
        var nodeCoords = CollectNodeCoords(rows);
        var boundary = new HashSet<(int A, int B)>();
        var edgeCounts = new Dictionary<(int A, int B), int>();
        foreach (var row in rows)
        {
            foreach (var (a, b) in new[] { (row.Node1, row.Node2), (row.Node2, row.Node3), (row.Node3, row.Node1) })
            {
                if (a <= 0 || b <= 0) continue;
                var key = a < b ? (a, b) : (b, a);
                edgeCounts[key] = edgeCounts.GetValueOrDefault(key) + 1;
            }
        }
        foreach (var kv in edgeCounts)
        {
            if (kv.Value == 1)
                boundary.Add(kv.Key);
        }

        var groups = new List<(double Sxx, double Syy, double Sxy, List<(int A, int B)> Edges)>();
        if (loadConditionsVM != null)
        {
            var geom = meshVM.GeometryDefinitionVM;
            foreach (var load in loadConditionsVM.Loads.Where(l => l.IsEnabled && l.IsDistributedLineLoad))
            {
                LoadPlacementCoords.WriteAxisForPersist(load, geom, out var x0, out var y0, out var x1, out var y1);
                var edges = FindEdgesOnSegment(nodeCoords, boundary, x0, y0, x1, y1);
                if (edges.Count == 0)
                    continue;
                var (sxx, syy, sxy) = TractionTensorFromLoad(load);
                groups.Add((sxx, syy, sxy, edges));
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("! n_traction_groups");
        sb.AppendLine(groups.Count.ToString());
        foreach (var g in groups)
        {
            sb.AppendLine($"! mode=0 direct (tension-positive, kPa): sxx={g.Sxx:G6} syy={g.Syy:G6} sxy={g.Sxy:G6}");
            sb.AppendLine($"0  {g.Sxx:G10}  {g.Syy:G10}  {g.Sxy:G10}");
            sb.AppendLine(g.Edges.Count.ToString());
            foreach (var (a, b) in g.Edges)
                sb.AppendLine($"{a}  {b}");
        }
        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static (double Sxx, double Syy, double Sxy) TractionTensorFromLoad(LoadModel load)
    {
        double q = load.Magnitude;
        return load.Orientation switch
        {
            LoadOrientation.VerticalDown => (0, -q, 0),
            LoadOrientation.HorizontalRight => (q, 0, 0),
            _ => load.Direction == LoadDirection.X ? (q, 0, 0) : (0, -q, 0)
        };
    }

    private static List<(int A, int B)> FindEdgesOnSegment(
        SortedDictionary<int, (double X, double Y)> nodeCoords,
        HashSet<(int A, int B)> boundary,
        double x0, double y0, double x1, double y1)
    {
        double dx = x1 - x0, dy = y1 - y0;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-16)
            return new List<(int, int)>();
        double tol = Math.Max(1e-3, 0.02 * Math.Sqrt(lenSq));
        bool OnSeg(int id)
        {
            if (!nodeCoords.TryGetValue(id, out var p)) return false;
            double px = p.X - x0, py = p.Y - y0;
            double t = (px * dx + py * dy) / lenSq;
            if (t < -0.05 || t > 1.05) return false;
            double cx = x0 + t * dx, cy = y0 + t * dy;
            double ddx = p.X - cx, ddy = p.Y - cy;
            return ddx * ddx + ddy * ddy <= tol * tol;
        }

        var edges = new List<(int A, int B)>();
        foreach (var (a, b) in boundary)
        {
            if (OnSeg(a) && OnSeg(b))
                edges.Add((a, b));
        }
        return edges;
    }

    // ─── Low-level helpers ──────────────────────────────────────────────────

    private static void ValidateMesh(MeshViewModel meshVM)
    {
        var rows = meshVM?.MeshInformationRows?.ToList() ?? new List<MeshInformationRow>();
        if (rows.Count == 0)
            throw new InvalidOperationException("No mesh elements. Generate a T3 mesh first (Mesh tab → Tunnel Meshing).");
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

    private static int DistinctEtypeCount(IEnumerable<MeshInformationRow> rows)
    {
        var set = new HashSet<int>();
        foreach (var r in rows) set.Add(r.Etype < 1 ? 1 : r.Etype);
        return set.Count;
    }

    private static string Wrap(Section s, string body)
    {
        body = NormalizeBlockBody(body);
        return $"! <BEGIN:{s}>" + Environment.NewLine + body + Environment.NewLine + $"! <END:{s}>" + Environment.NewLine;
    }

    private static string NormalizeBlockBody(string body)
    {
        if (string.IsNullOrEmpty(body)) return "";
        return body.TrimEnd('\r', '\n');
    }

    private static string NormalizeBlock(string block)
    {
        if (string.IsNullOrEmpty(block)) return "";
        return block.TrimEnd('\r', '\n');
    }

    private static string PlaceholderBody(int step, Section s)
        => $"! (awaiting: click \"{step}. {FriendlyName(s)}\" above the editor)";

    private static string FriendlyName(Section s) => s switch
    {
        Section.Header        => "Header",
        Section.Materials     => "Materials",
        Section.Mesh          => "Mesh",
        Section.Bcs           => "BCs",
        Section.OutputNodes   => "Output nodes",
        Section.SolverControl => "Solver control",
        Section.Layers        => "Excavation layers",
        _ => s.ToString(),
    };

    private static string ValRow(IEnumerable<double> vals)
        => string.Join("  ", vals.Select(v => v.ToString("G10")));

    internal static object ProbeMeshExportInventory(MeshViewModel meshVM, MaterialPropertiesViewModel? materialsVM)
    {
        var rows = meshVM.MeshInformationRows.ToList();
        int quadRows = rows.Count(r => r.IsQuad && r.Node4 > 0);
        int triRows = rows.Count - quadRows;
        var options = P610AnalysisOptionsHelper.BuildFromViewModels(materialsVM, meshVM);
        int exportElems = CountExportElements(rows, options.MeshType);
        var mats = materialsVM?.Materials?.ToList() ?? new List<MaterialModel>();
        static bool IsSaneMat(MaterialModel m)
            => m.YoungsModulus >= 1e3 && m.PoissonsRatio > 0.0 && m.PoissonsRatio < 0.499;
        return new
        {
            meshType = options.MeshType,
            rowCount = rows.Count,
            quadRows,
            triRows,
            exportElems,
            nodeCount = rows.Count > 0 ? CollectNodeCoords(rows).Keys.Max() : 0,
            npTypes = ResolveNpTypes(rows, materialsVM),
            matCount = mats.Count,
            mat1E = mats.Count > 0 ? mats[0].YoungsModulus : (double?)null,
            mat1Nu = mats.Count > 0 ? mats[0].PoissonsRatio : (double?)null,
            mat2E = mats.Count > 1 ? mats[1].YoungsModulus : (double?)null,
            mat2Nu = mats.Count > 1 ? mats[1].PoissonsRatio : (double?)null,
            invalidMaterialIndices = mats.Select((m, i) => IsSaneMat(m) ? -1 : i + 1).Where(i => i > 0).ToList(),
        };
    }

    /// <summary>
    /// Resolves the final, ready-to-emit layer list shared by <see cref="BuildSolverControlBody"/>
    /// (layer count for the "tol limit incs layers" line) and <see cref="BuildLayersBody"/> (the
    /// actual per-layer element blocks) so the two can never disagree on how many layers exist.
    ///
    /// When there is no staged construction and no tunnel geometry (a plain gravity-only slope /
    /// embankment analysis), <see cref="BuildExcavationLayers"/> returns an empty list. That case
    /// always resolves to exactly one <c>gravity_only_layer</c> (0 elements) here — i.e. `layers=1`
    /// with a single empty block — for both SRF and non-SRF runs. This must never fall through to
    /// `layers=0`: that value selects a completely different, traction-boundary-loaded
    /// "infinite-medium Mohr-Coulomb" analysis mode with no gravity/self-weight at all (see the
    /// `layers==0` branch header comment in p610_t3_elastoplastic.cpp), which is not applicable to
    /// gravity-driven slope-stability/SRF analyses and uses an entirely different .dat block format.
    /// </summary>
    private static List<List<int>> ResolveAutoLayerElements(
        List<MeshInformationRow> rows,
        StagedConstructionViewModel? stagedVM,
        FEAnalysisViewModel? feVM)
    {
        var layerElements = BuildExcavationLayers(rows, stagedVM);

        if (layerElements.Count == 0)
        {
            // Plain gravity-only run: a single gravity-only layer (0 elements) so the solver
            // performs its geostatic/self-weight step without any staged excavation.
            layerElements.Add(new List<int>());
        }
        else if (feVM?.IsSrfSolverMode == true && layerElements[0].Count != 0)
        {
            // First layer must be gravity equilibration (0 elements removed) for slope_stability_T3_SRF.
            layerElements.Insert(0, new List<int>());
        }

        return layerElements;
    }

    private static List<List<int>> BuildExcavationLayers(
        List<MeshInformationRow> rows,
        StagedConstructionViewModel? stagedVM)
    {
        if (stagedVM?.Stages.Count > 0)
        {
            var stageResults = new List<List<int>>();

            // A stage participates when it has either:
            //   (a) parsed IDs (.dat-import path — preserves byte-for-byte round-trip), OR
            //   (b) excavation zones (polygon-edit path — current behaviour)
            // Both sources are unioned into a SortedSet so re-emit dedupes and orders stably.
            foreach (var stage in stagedVM.Stages
                .Where(s => s.ExcavatedElementIds.Count > 0 || s.ExcavationZones.Count > 0))
            {
                var layer = new SortedSet<int>(stage.ExcavatedElementIds);

                if (stage.ExcavationZones.Count > 0)
                {
                    foreach (var r in rows)
                    {
                        var (cx, cy) = PolygonElementMatcher.ElementCentroid(r);
                        foreach (var zone in stage.ExcavationZones)
                        {
                            if (zone.AxisPolygon.Count >= 3
                                && PolygonElementMatcher.PointInPolygon(cx, cy, zone.AxisPolygon))
                            {
                                layer.Add(r.ElementId);
                                break;
                            }
                        }
                    }
                }

                if (layer.Count > 0)
                    stageResults.Add(layer.ToList());
            }
            if (stageResults.Count > 0)
                return stageResults;
        }

        var tunnelIds = rows
            .Where(r => string.Equals(r.Status, "Tunnel", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.ElementId)
            .ToList();

        if (tunnelIds.Count > 0)
            return new List<List<int>> { tunnelIds };

        // No staged construction and no tunnel geometry: this is a plain gravity-only
        // analysis (e.g. slope-stability SRF). Returning an empty list here — rather than
        // fabricating a single-element "excavation" of an arbitrary mesh element — lets
        // ResolveAutoLayerElements emit exactly one clean gravity-only layer instead.
        return new List<List<int>>();
    }

}
