using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GeotechSlopeApp.Models.FiniteElement;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Geometry / loading / material context emitted by the p610_t3 family solvers in the
/// JSON's top-level <c>meta</c> block. Every field is nullable because older runs (and the
/// elasto-plastic <c>p610_t3</c> solver) may omit individual sub-blocks. The Verification
/// tab uses this to auto-prefill its Kirsch inputs.
/// </summary>
public sealed record P610T3MetaBlock(
    double? Cx,
    double? Cy,
    double? A,
    double? SigmaV_kPa,
    double? K0,
    double? E_kPa,
    double? Nu,
    double? PhiDeg,
    double? C_kPa,
    double? PsiDeg,
    double? Gamma,
    double? SrfFoS = null,
    double? SrfMaxItersUsed = null,
    double? SrfMaxDisplacement = null,
    bool? SrfAllIncrementsConverged = null,
    double? SrfMaxYieldRatio = null);

/// <summary>Pseudo-static seismic coefficients and per-node inertial forces from solver JSON.</summary>
public sealed record PseudoStaticRunInfo(
    double Kh,
    double Kv,
    IReadOnlyList<PseudoStaticNodalForce> NodalForces);

public sealed record PseudoStaticNodalForce(int NodeId, double Fx, double Fy);

/// <summary>Result of a p610_t3 run: mesh plus one or more solution stages from the .json file.</summary>
public sealed class P610T3RunResult
{
    public IReadOnlyList<Node> Nodes { get; init; } = new List<Node>();
    public IReadOnlyList<ElementBase> Elements { get; init; } = new List<ElementBase>();
    /// <summary>At least one entry; when the solver omits <c>stages[]</c>, a single "Final" stage is built from the top level.</summary>
    public IReadOnlyList<P610T3StageSnapshot> Stages { get; init; } = new List<P610T3StageSnapshot>();
    /// <summary>Top-level <c>joints[]</c> from the solver (final stage). Empty when the deck has no interfaces.</summary>
    public IReadOnlyList<P610T3JointResult> Joints { get; init; } = Array.Empty<P610T3JointResult>();
    public string? Description { get; init; }
    /// <summary>Optional tunnel/loading/material self-description (auto-detected by the solver).</summary>
    public P610T3MetaBlock? Meta { get; init; }
    /// <summary>Present when the solver exported <c>pseudo_static</c> + nodal forces (non-zero k_h/k_v).</summary>
    public PseudoStaticRunInfo? PseudoStatic { get; init; }
}

public sealed class P610T3StageSnapshot
{
    public int Index { get; init; }
    public string Label { get; init; } = "";
    public string? Subtitle { get; init; }
    /// <summary>Displacements keyed by 0-based node index in <see cref="P610T3RunResult.Nodes"/> (same as <see cref="Node.Id"/>).</summary>
    public IReadOnlyList<(double ux, double uy)> DisplacementsByNodeIndex { get; init; } = Array.Empty<(double, double)>();
    public IReadOnlyList<ElementStress> ElementStresses { get; init; } = new List<ElementStress>();
    /// <summary>Goodman interface results for this stage. Empty when the solver omitted <c>joints</c>.</summary>
    public IReadOnlyList<P610T3JointResult> Joints { get; set; } = Array.Empty<P610T3JointResult>();
    /// <summary>Number of structurally active (non-excavated) elements in this stage, from the solver's "elements_solid" field. -1 when absent (older JSONs).</summary>
    public int ElementsSolid { get; init; } = -1;
}

internal static class P610T3JsonParser
{
    public static P610T3RunResult ParseFile(string jsonPath)
    {
        var jsonText = File.ReadAllText(jsonPath);
        // #region agent log
        var rawNanCount = RegexCountNan(jsonText);
        // #endregion
        jsonText = SanitizeNonFiniteJsonNumbers(jsonText);
        // #region agent log
        DebugAgentLog.Write("H2", "P610T3JsonParser.cs:ParseFile",
            "JSON nan tokens before/after sanitize",
            new { jsonPath, rawNanCount, fileName = Path.GetFileName(jsonPath) });
        // #endregion
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            return ParseRoot(doc.RootElement);
        }
        catch (JsonException ex)
        {
            var hint = ex.Message.Contains("'n'", StringComparison.OrdinalIgnoreCase)
                       || ex.Message.Contains("nan", StringComparison.OrdinalIgnoreCase)
                ? " The solver likely wrote NaN/Inf into the JSON (non-converged step, invalid material, or singular stiffness)."
                : "";
            throw new InvalidOperationException(
                $"Failed to read solver JSON ({Path.GetFileName(jsonPath)}): {ex.Message}{hint}",
                ex);
        }
    }

    /// <summary>
    /// Replace bare nan/inf tokens (invalid JSON) emitted by some solver builds.
    /// Handles both object values ("ux": -nan) and array elements ([ -nan, 1.0 ]).
    /// </summary>
    internal static string SanitizeNonFiniteJsonNumbers(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        // libstdc++/MSVC bare tokens: -nan, -nan(ind), nan(snan), -inf
        var s = System.Text.RegularExpressions.Regex.Replace(
            json,
            @"(?<=[\[,:]\s*)(?:\+|-)?(?:nan|inf)(?:\([a-z0-9_]+\))?(?=\s*[,}\]])",
            "0",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // MSVC legacy: -1.#QNAN0, 1.#IND00, -1.#INF00
        s = System.Text.RegularExpressions.Regex.Replace(
            s,
            @"(?<=[\[,:]\s*)-?\d+\.#(?:QNAN|IND|INF)\d*(?=\s*[,}\]])",
            "0",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return s;
    }

    private static int RegexCountNan(string json)
    {
        if (string.IsNullOrEmpty(json)) return 0;
        return System.Text.RegularExpressions.Regex.Matches(
            json,
            @"(?<=[\[,:]\s*)(?:\+|-)?(?:nan|inf)(?:\([a-z0-9_]+\))?(?=\s*[,}\]])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
    }

    public static P610T3RunResult ParseRoot(JsonElement root)
    {
        var desc = root.TryGetProperty("description", out var d) ? d.GetString() : null;
        var meta = ReadMeta(root);
        if (!root.TryGetProperty("nodes", out var nodesJson) || nodesJson.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("p610_t3 JSON: missing or invalid \"nodes\" array.");

        if (!root.TryGetProperty("elements", out var elementsJson) || elementsJson.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("p610_t3 JSON: missing or invalid \"elements\" array.");

        var (nodeModels, fileIdToIndex) = ReadNodesAndMap(nodesJson);
        var elements = ReadElements(elementsJson, fileIdToIndex);
        var topJoints = ReadJoints(root, fileIdToIndex);

        List<P610T3StageSnapshot> stages;
        if (root.TryGetProperty("stages", out var stagesEl) && stagesEl.ValueKind == JsonValueKind.Array && stagesEl.GetArrayLength() > 0)
        {
            stages = new List<P610T3StageSnapshot>(stagesEl.GetArrayLength());
            int i = 0;
            foreach (var st in stagesEl.EnumerateArray())
            {
                stages.Add(ReadStage(i, st, nodeModels.Count, fileIdToIndex));
                i++;
            }

            var topDisp = ReadDisplacementsForNodes(nodesJson, nodeModels.Count, fileIdToIndex);
            ApplyTopLevelDisplacementFallback(stages, topDisp);
        }
        else
        {
            var disp = ReadDisplacementsForNodes(nodesJson, nodeModels.Count, fileIdToIndex);
            var stresses = ReadElementStressesIfPresent(root);
            stages =
            [
                new P610T3StageSnapshot
                {
                    Index = 0,
                    Label = "Final",
                    Subtitle = "Top-level solution (no multi-stage array in file)",
                    DisplacementsByNodeIndex = disp,
                    ElementStresses = stresses,
                    Joints = topJoints,
                }
            ];
        }

        if (stages.Count == 0)
            throw new InvalidOperationException("p610_t3 JSON: no valid stages were parsed.");

        FillMissingStageJoints(stages, topJoints, nodeModels);

        var pseudoStatic = ReadPseudoStatic(root, nodeModels, fileIdToIndex);

        return new P610T3RunResult
        {
            Nodes = nodeModels,
            Elements = elements,
            Stages = stages,
            Joints = topJoints,
            Description = desc,
            Meta = meta,
            PseudoStatic = pseudoStatic,
        };
    }

    private static PseudoStaticRunInfo? ReadPseudoStatic(
        JsonElement root,
        IReadOnlyList<Node> nodes,
        IReadOnlyDictionary<int, int> fileIdToIndex)
    {
        if (!root.TryGetProperty("pseudo_static", out var ps) || ps.ValueKind != JsonValueKind.Object)
            return null;

        var kh = TryDouble(ps, "k_h") ?? 0.0;
        var kv = TryDouble(ps, "k_v") ?? 0.0;
        if (Math.Abs(kh) < 1e-12 && Math.Abs(kv) < 1e-12)
            return null;

        if (!root.TryGetProperty("pseudo_static_nodal_forces", out var forcesEl)
            || forcesEl.ValueKind != JsonValueKind.Array)
        {
            return new PseudoStaticRunInfo(kh, kv, Array.Empty<PseudoStaticNodalForce>());
        }

        var forces = new List<PseudoStaticNodalForce>();
        foreach (var f in forcesEl.EnumerateArray())
        {
            if (!f.TryGetProperty("id", out var idEl)) continue;
            int fileId = idEl.GetInt32();
            if (!fileIdToIndex.TryGetValue(fileId, out int nodeIndex))
                continue;
            double fx = f.TryGetProperty("fx", out var fxEl) ? ReadFiniteDouble(fxEl) : 0.0;
            double fy = f.TryGetProperty("fy", out var fyEl) ? ReadFiniteDouble(fyEl) : 0.0;
            // NodeId is 1-based for display (matches solver JSON id field).
            forces.Add(new PseudoStaticNodalForce(nodeIndex + 1, fx, fy));
        }

        if (forces.Count == 0 && nodes.Count > 0)
        {
            for (int i = 0; i < nodes.Count; i++)
                forces.Add(new PseudoStaticNodalForce(i + 1, 0.0, 0.0));
        }

        return new PseudoStaticRunInfo(kh, kv, forces);
    }

    /// <summary>
    /// Optional auto-detection block emitted by the p610_t3 family solvers. Each sub-block
    /// (<c>tunnel</c>, <c>loading</c>, <c>material</c>) is independently optional; missing
    /// keys leave their corresponding field as <c>null</c> rather than failing the parse.
    /// </summary>
    private static P610T3MetaBlock? ReadMeta(JsonElement root)
    {
        if (!root.TryGetProperty("meta", out var meta) || meta.ValueKind != JsonValueKind.Object)
            return null;

        double? cx = null, cy = null, a = null;
        if (meta.TryGetProperty("tunnel", out var tunnel) && tunnel.ValueKind == JsonValueKind.Object)
        {
            cx = TryDouble(tunnel, "cx");
            cy = TryDouble(tunnel, "cy");
            a  = TryDouble(tunnel, "a");
        }

        double? sigV = null, k0 = null;
        if (meta.TryGetProperty("loading", out var loading) && loading.ValueKind == JsonValueKind.Object)
        {
            sigV = TryDouble(loading, "sigma_v_kPa");
            k0   = TryDouble(loading, "K0");
        }

        double? E = null, nu = null;
        if (meta.TryGetProperty("material", out var material) && material.ValueKind == JsonValueKind.Object)
        {
            E  = TryDouble(material, "E_kPa");
            nu = TryDouble(material, "nu");
        }

        double? phiDeg = null, cKpa = null, psiDeg = null, gamma = null;
        if (meta.TryGetProperty("mohr_coulomb", out var mc) && mc.ValueKind == JsonValueKind.Object)
        {
            phiDeg = TryDouble(mc, "phi_deg");
            cKpa   = TryDouble(mc, "c_kPa");
            psiDeg = TryDouble(mc, "psi_deg");
            gamma  = TryDouble(mc, "gamma");
        }

        // M2.2 Option B: solver-internal strength-reduction-factor bisection writes
        // its converged FoS under meta.srf.fos. Older runs (and runs without
        // srf_mode 1 in the .dat) leave this null, in which case the slope-stability
        // VM branch falls back to the lambda-sweep diagnostic.
        double? srfFos = null;
        double? srfMaxIters = null;
        double? srfMaxDisp = null;
        bool? srfAllConverged = null;
        double? srfMaxYield = null;
        if (meta.TryGetProperty("srf", out var srf) && srf.ValueKind == JsonValueKind.Object)
        {
            srfFos = TryDouble(srf, "fos");
            srfMaxIters = TryDouble(srf, "max_iters_used");
            srfMaxDisp = TryDouble(srf, "max_disp");
            srfAllConverged = TryBool(srf, "all_increments_converged");
            srfMaxYield = TryDouble(srf, "max_yield_ratio");
        }

        // If literally every field is null the meta block carries no signal — collapse to null
        // so downstream code can treat "no meta present" identically to "all fields null".
        if (cx is null && cy is null && a is null && sigV is null && k0 is null
            && E is null && nu is null && phiDeg is null && cKpa is null && psiDeg is null && gamma is null
            && srfFos is null && srfMaxIters is null && srfMaxDisp is null && srfAllConverged is null
            && srfMaxYield is null)
            return null;

        return new P610T3MetaBlock(cx, cy, a, sigV, k0, E, nu, phiDeg, cKpa, psiDeg, gamma, srfFos, srfMaxIters, srfMaxDisp, srfAllConverged, srfMaxYield);
    }

    private static double ReadFiniteDouble(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Number) return 0;
        var v = el.GetDouble();
        return double.IsFinite(v) ? v : 0;
    }

    private static double? TryDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number => ReadFiniteDouble(el),
            JsonValueKind.String => double.TryParse(el.GetString(), out var d) ? d : (double?)null,
            _ => null,
        };
    }

    private static bool? TryBool(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el))
            return null;

        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(el.GetString(), out var b) ? b : (bool?)null,
            _ => null,
        };
    }

    private static (List<Node> nodes, Dictionary<int, int> fileIdToIndex) ReadNodesAndMap(JsonElement nodesJson)
    {
        var all = new List<(int fileId, double x, double y)>();
        foreach (var n in nodesJson.EnumerateArray())
        {
            if (!n.TryGetProperty("id", out var idEl)) continue;
            int fileId = idEl.GetInt32();
            double x = ReadFiniteDouble(n.GetProperty("x"));
            double y = ReadFiniteDouble(n.GetProperty("y"));
            all.Add((fileId, x, y));
        }

        all.Sort((a, b) => a.fileId.CompareTo(b.fileId));
        var fileIdToIndex = new Dictionary<int, int>(all.Count);
        var nodeModels = new List<Node>(all.Count);
        for (int i = 0; i < all.Count; i++)
        {
            var (fileId, x, y) = all[i];
            fileIdToIndex[fileId] = i;
            nodeModels.Add(new Node
            {
                Id = i,
                X = x,
                Y = y,
                Displacements = new[] { 0.0, 0.0 }
            });
        }
        return (nodeModels, fileIdToIndex);
    }

    private static List<ElementBase> ReadElements(JsonElement elementsJson, IReadOnlyDictionary<int, int> fileIdToIndex)
    {
        var list = new List<ElementBase>();
        foreach (var e in elementsJson.EnumerateArray())
        {
            if (!e.TryGetProperty("n1", out _)) continue;
            int n1 = e.GetProperty("n1").GetInt32();
            int n2 = e.GetProperty("n2").GetInt32();
            int n3 = e.GetProperty("n3").GetInt32();
            if (!fileIdToIndex.TryGetValue(n1, out var i1) ||
                !fileIdToIndex.TryGetValue(n2, out var i2) ||
                !fileIdToIndex.TryGetValue(n3, out var i3))
                continue;

            int eid = e.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : list.Count;

            // Staged excavation flag written by p610_t3_elastoplastic.write_json:
            //   "active": 1  -> solid (default)
            //   "active": 0  -> excavated; solver wiped stress/stiffness, void in current stage.
            // Absent on older JSONs or on element_stresses-only output — treat as active.
            bool isActive = true;
            if (e.TryGetProperty("active", out var activeEl) && activeEl.ValueKind == JsonValueKind.Number)
                isActive = activeEl.GetInt32() != 0;

            // Probe optional node keys n4..n8 so we can infer the element family:
            //   n1..n3 only                         -> T3  (linear triangle, 3 nodes)
            //   n1..n4  (no n5)                     -> Q4  (linear quadrilateral, 4 nodes)
            //   n1..n6  (no n7)                     -> T6  (quadratic triangle, 6 nodes)
            //   n1..n8                              -> Q8  (serendipity quadrilateral, 8 nodes)
            bool has4 = TryReadNodeIndex(e, "n4", fileIdToIndex, out int i4);
            bool has5 = TryReadNodeIndex(e, "n5", fileIdToIndex, out int i5);
            bool has6 = TryReadNodeIndex(e, "n6", fileIdToIndex, out int i6);
            bool has7 = TryReadNodeIndex(e, "n7", fileIdToIndex, out int i7);
            bool has8 = TryReadNodeIndex(e, "n8", fileIdToIndex, out int i8);

            if (has4 && has5 && has6 && has7 && has8)
            {
                list.Add(new QuadraticQuadrilateralElement
                {
                    Id = eid,
                    NodeIds = new[] { i1, i2, i3, i4, i5, i6, i7, i8 },
                    IsActive = isActive
                });
                continue;
            }

            if (has4 && has5 && has6)
            {
                list.Add(new QuadraticTriangleElement
                {
                    Id = eid,
                    NodeIds = new[] { i1, i2, i3, i4, i5, i6 },
                    IsActive = isActive
                });
                continue;
            }

            if (has4)
            {
                list.Add(new LinearQuadrilateralElement
                {
                    Id = eid,
                    NodeIds = new[] { i1, i2, i3, i4 },
                    IsActive = isActive
                });
                continue;
            }

            list.Add(new LinearTriangleElement
            {
                Id = eid,
                NodeIds = new[] { i1, i2, i3 },
                IsActive = isActive
            });
        }
        return list;
    }

    private static bool TryReadNodeIndex(
        JsonElement element,
        string propertyName,
        IReadOnlyDictionary<int, int> fileIdToIndex,
        out int index)
    {
        index = -1;
        if (!element.TryGetProperty(propertyName, out var el)) return false;
        if (el.ValueKind != JsonValueKind.Number) return false;
        int fileId = el.GetInt32();
        return fileIdToIndex.TryGetValue(fileId, out index);
    }

    private static P610T3StageSnapshot ReadStage(int arrayIndex, JsonElement st, int nodeCount, IReadOnlyDictionary<int, int> fileIdToIndex)
    {
        int index = st.TryGetProperty("index", out var ix) ? ix.GetInt32() : arrayIndex;
        string label = "Stage " + index;
        if (st.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
            label = nameEl.GetString() ?? label;
        else if (st.TryGetProperty("label", out var labelEl) && labelEl.ValueKind == JsonValueKind.String)
            label = labelEl.GetString() ?? label;
        else if (st.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String)
            label = titleEl.GetString() ?? label;

        string? sub = st.TryGetProperty("subtitle", out var subEl) && subEl.ValueKind == JsonValueKind.String
            ? subEl.GetString()
            : null;

        IReadOnlyList<(double, double)> disp;
        if (st.TryGetProperty("nodes", out var stageNodes) && stageNodes.ValueKind == JsonValueKind.Array)
            disp = ReadDisplacementsForNodes(stageNodes, nodeCount, fileIdToIndex);
        else
            disp = ZeroDisplacements(nodeCount);

        var stresses = st.TryGetProperty("element_stresses", out var es)
            ? ReadElementStressesArray(es)
            : st.TryGetProperty("elementStresses", out var es2)
                ? ReadElementStressesArray(es2)
                : (IReadOnlyList<ElementStress>)new List<ElementStress>();

        int elementsSolid = st.TryGetProperty("elements_solid", out var esEl) && esEl.ValueKind == JsonValueKind.Number
            ? esEl.GetInt32()
            : -1;

        var joints = ReadJoints(st, fileIdToIndex);

        return new P610T3StageSnapshot
        {
            Index = index,
            Label = label,
            Subtitle = sub,
            DisplacementsByNodeIndex = disp,
            ElementStresses = stresses,
            Joints = joints,
            ElementsSolid = elementsSolid,
        };
    }

    private static void FillMissingStageJoints(
        List<P610T3StageSnapshot> stages,
        IReadOnlyList<P610T3JointResult> topJoints,
        IReadOnlyList<Node> nodes)
    {
        if (topJoints.Count == 0)
            return;

        foreach (var stage in stages)
        {
            if (stage.Joints.Count > 0)
                continue;
            stage.Joints = P610T3JointKinematics.RecomputeAll(
                topJoints, nodes, stage.DisplacementsByNodeIndex);
        }
    }

    private static IReadOnlyList<P610T3JointResult> ReadJoints(
        JsonElement parent,
        IReadOnlyDictionary<int, int> fileIdToIndex)
    {
        if (!parent.TryGetProperty("joints", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<P610T3JointResult>();

        var list = new List<P610T3JointResult>(arr.GetArrayLength());
        foreach (var j in arr.EnumerateArray())
        {
            if (!TryReadNodeIndex(j, "n1", fileIdToIndex, out var n1)
                || !TryReadNodeIndex(j, "n2", fileIdToIndex, out var n2)
                || !TryReadNodeIndex(j, "n3", fileIdToIndex, out var n3)
                || !TryReadNodeIndex(j, "n4", fileIdToIndex, out var n4))
                continue;

            double un = GetDoubleProp(j, "un", "opening", "u_n");
            double closure = j.TryGetProperty("closure", out _) || j.TryGetProperty("close", out _)
                || j.TryGetProperty("delta_n", out _)
                ? GetDoubleProp(j, "closure", "close", "delta_n")
                : -un;
            list.Add(new P610T3JointResult
            {
                N1 = n1,
                N2 = n2,
                N3 = n3,
                N4 = n4,
                Un = un,
                Us = GetDoubleProp(j, "us", "slip", "u_s"),
                Closure = closure,
                SnKpa = GetDoubleProp(j, "sn_kPa", "sn", "sigma_n"),
                TauKpa = GetDoubleProp(j, "tau_kPa", "tau", "tau_s"),
                L = GetDoubleProp(j, "L", "length", "len"),
                Nx = GetDoubleProp(j, "nx", "n_x", "normal_x"),
                Ny = GetDoubleProp(j, "ny", "n_y", "normal_y"),
                Slipped = TryBool(j, "slipped") == true,
            });
        }

        return list;
    }

    private static IReadOnlyList<(double, double)> ZeroDisplacements(int nodeCount)
    {
        var a = new (double, double)[nodeCount];
        return a;
    }

    private static IReadOnlyList<(double, double)> ReadDisplacementsForNodes(
        JsonElement nodeArray, int nodeCount, IReadOnlyDictionary<int, int> fileIdToIndex)
    {
        var arr = new (double, double)[nodeCount];
        foreach (var n in nodeArray.EnumerateArray())
        {
            if (!n.TryGetProperty("id", out var idEl)) continue;
            int fileId = idEl.GetInt32();
            if (!fileIdToIndex.TryGetValue(fileId, out var ni)) continue;
            double ux = n.TryGetProperty("ux", out var uxEl) ? ReadFiniteDouble(uxEl) : 0;
            double uy = n.TryGetProperty("uy", out var uyEl) ? ReadFiniteDouble(uyEl) : 0;
            arr[ni] = (ux, uy);
        }
        return arr;
    }

    private static IReadOnlyList<ElementStress> ReadElementStressesIfPresent(JsonElement root)
    {
        foreach (var name in new[] { "element_stresses", "elementStresses" })
        {
            if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Array)
                return ReadElementStressesArray(el);
        }
        return new List<ElementStress>();
    }

    private static IReadOnlyList<ElementStress> ReadElementStressesArray(JsonElement array)
    {
        var list = new List<ElementStress>(array.GetArrayLength());
        foreach (var s in array.EnumerateArray())
        {
            if (!s.TryGetProperty("id", out var idEl)) continue;
            int eid = idEl.GetInt32();
            double sxx = GetDoubleProp(s, "sxx", "sig_x", "sigma_xx");
            double syy = GetDoubleProp(s, "syy", "sig_y", "sigma_yy");
            double txy = GetDoubleProp(s, "txy", "tau_xy", "sig_xy");
            double szz = s.TryGetProperty("szz", out var szzEl) ? ReadFiniteDouble(szzEl) :
                s.TryGetProperty("sig_z", out var sigz) ? ReadFiniteDouble(sigz) : 0;
            var stress = StressFromTensor(eid, sxx, syy, txy, szz);
            if (s.TryGetProperty("u_kPa", out var uEl))
                stress.PorePressure = ReadFiniteDouble(uEl);
            if (s.TryGetProperty("h_total_m", out var htEl))
                stress.TotalHead = ReadFiniteDouble(htEl);
            if (s.TryGetProperty("h_pressure_m", out var hpEl))
                stress.PressureHead = ReadFiniteDouble(hpEl);
            if (s.TryGetProperty("sxx_eff_kPa", out var exx))
            {
                stress.EffectiveStressXX = ReadFiniteDouble(exx);
                stress.EffectiveStressYY = GetDoubleProp(s, "syy_eff_kPa", "syy_eff", "sigma_yy_eff");
                stress.EffectiveStressXY = GetDoubleProp(s, "sxy_eff_kPa", "sxy_eff", "tau_xy_eff");
            }
            else if (Math.Abs(stress.PorePressure) > 0.0)
            {
                // Terzaghi (Smith tension+): σ' = σ + u on normal components.
                stress.EffectiveStressXX = stress.StressXX + stress.PorePressure;
                stress.EffectiveStressYY = stress.StressYY + stress.PorePressure;
                stress.EffectiveStressXY = stress.StressXY;
            }
            if (s.TryGetProperty("sxx_eff_kPa", out _) || Math.Abs(stress.PorePressure) > 0.0
                || Math.Abs(stress.EffectiveStressXX) > 0.0 || Math.Abs(stress.EffectiveStressYY) > 0.0)
            {
                double ec = 0.5 * (stress.EffectiveStressXX + stress.EffectiveStressYY);
                double er = Math.Sqrt(Math.Pow(0.5 * (stress.EffectiveStressXX - stress.EffectiveStressYY), 2)
                                      + stress.EffectiveStressXY * stress.EffectiveStressXY);
                stress.EffectivePrincipalMax = ec + er;
                stress.EffectivePrincipalMin = ec - er;
            }
            stress.StrainXX = GetDoubleProp(s, "exx", "eps_xx", "strain_xx");
            stress.StrainYY = GetDoubleProp(s, "eyy", "eps_yy", "strain_yy");
            stress.ShearStrain = GetDoubleProp(s, "gamma_xy", "gxy", "eps_xy", "strain_xy");
            stress.PeakMaximumShearStrain = GetDoubleProp(s, "gamma_max", "gmax", "peak_gamma_max");
            stress.YieldRatio = GetDoubleProp(s, "yield_ratio", "yieldRatio", "f_yield");
            stress.PlasticStrainNorm = GetDoubleProp(s, "plastic_strain", "plastic_strain_norm", "evpt_norm");
            list.Add(stress);
        }
        return list;
    }

    private static double GetDoubleProp(JsonElement s, string a, string b, string c, string d = "")
    {
        if (s.TryGetProperty(a, out var p)) return ReadFiniteDouble(p);
        if (s.TryGetProperty(b, out p)) return ReadFiniteDouble(p);
        if (s.TryGetProperty(c, out p)) return ReadFiniteDouble(p);
        if (!string.IsNullOrEmpty(d) && s.TryGetProperty(d, out p)) return ReadFiniteDouble(p);
        return 0;
    }

    /// <summary>Plane strain von Mises with out-of-plane normal stress szz; principals from in-plane tensor.</summary>
    internal static ElementStress StressFromTensor(int elementId, double sxx, double syy, double txy, double szz)
    {
        double c = 0.5 * (sxx + syy);
        double r = Math.Sqrt(Math.Pow(0.5 * (sxx - syy), 2) + txy * txy);
        double s1 = c + r;
        double s2 = c - r;
        double vm = Math.Sqrt(
            0.5 * (
                (sxx - syy) * (sxx - syy) +
                (syy - szz) * (syy - szz) +
                (szz - sxx) * (szz - sxx)
            ) + 3.0 * txy * txy);
        return new ElementStress
        {
            ElementId = elementId,
            StressXX = sxx,
            StressYY = syy,
            StressXY = txy,
            PrincipalStressMax = s1,
            PrincipalStressMin = s2,
            VonMisesStress = vm,
        };
    }

    private static void ApplyTopLevelDisplacementFallback(
        List<P610T3StageSnapshot> stages,
        IReadOnlyList<(double ux, double uy)> topDisp)
    {
        if (stages.Count == 0 || !HasNonZeroDisplacement(topDisp))
            return;

        for (int i = 0; i < stages.Count; i++)
        {
            var stage = stages[i];
            if (IsInSituReferenceLabel(stage.Label))
                continue;
            if (HasNonZeroDisplacement(stage.DisplacementsByNodeIndex))
                continue;

            stages[i] = new P610T3StageSnapshot
            {
                Index = stage.Index,
                Label = stage.Label,
                Subtitle = stage.Subtitle,
                DisplacementsByNodeIndex = topDisp,
                ElementStresses = stage.ElementStresses,
                Joints = stage.Joints,
                ElementsSolid = stage.ElementsSolid,
            };
        }
    }

    private static bool IsInSituReferenceLabel(string label)
        => label.Contains("in_situ", StringComparison.OrdinalIgnoreCase)
           || label.Contains("reference_zero", StringComparison.OrdinalIgnoreCase);

    private static bool HasNonZeroDisplacement(IReadOnlyList<(double ux, double uy)> disp)
    {
        foreach (var (ux, uy) in disp)
        {
            if (Math.Abs(ux) > 1e-15 || Math.Abs(uy) > 1e-15)
                return true;
        }

        return false;
    }
}
