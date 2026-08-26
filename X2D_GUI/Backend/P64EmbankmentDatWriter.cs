using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Writes explicit p64 Q8 embankment .dat decks (same layout as <c>tools/gen_p64_q8_srf.ps1</c>).
/// </summary>
public static class P64EmbankmentDatWriter
{
    public sealed class MaterialRow
    {
        public double PhiDeg { get; init; } = 20;
        public double C_kPa { get; init; } = 15;
        public double PsiDeg { get; init; } = 0;
        public double Gamma { get; init; } = 20;
        public double E_kPa { get; init; } = 1.0e5;
        public double Nu { get; init; } = 0.3;

        public static MaterialRow FromMaterialModel(MaterialModel m) => new()
        {
            PhiDeg = m.FrictionAngle,
            C_kPa = m.Cohesion,
            PsiDeg = m.DilationAngle,
            Gamma = m.UnitWeight,
            E_kPa = m.YoungsModulus,
            Nu = m.PoissonsRatio,
        };

        public string ToDatLine() =>
            $"{Fmt(PhiDeg)}  {Fmt(C_kPa)}  {Fmt(PsiDeg)}  {Fmt(Gamma)}  {Fmt(E_kPa)}  {Fmt(Nu)}";
    }

    public sealed class WriteOptions
    {
        public int Nx1 { get; init; } = 20;
        public int Nx2 { get; init; } = 10;
        public int Ny1 { get; init; } = 10;
        public int Ny2 { get; init; } = 5;
        public double W1 { get; init; } = 20;
        public double S1 { get; init; } = 20;
        public double W2 { get; init; } = 20;
        public double H1 { get; init; } = 10;
        public double H2 { get; init; } = 5;
        public double PhiDeg { get; init; } = 20;
        public double C_kPa { get; init; } = 15;
        public double PsiDeg { get; init; } = 0;
        public double Gamma { get; init; } = 20;
        public double E_kPa { get; init; } = 1.0e5;
        public double Nu { get; init; } = 0.3;
        public IReadOnlyList<MaterialRow>? MaterialRows { get; init; }
        public double Tol { get; init; } = 0.0001;
        public int Limit { get; init; } = 500;
        public double MeshCoordYOffset { get; init; } = 15;
        public string SolverExecutable { get; init; } = "SRF";
        public double ExpectedFoS { get; init; } = 1.55;
        public double ExpectedFoSTolerancePct { get; init; } = 10;
        public IReadOnlyList<double> SrfValues { get; init; } = new[] { 1.0, 1.2, 1.4, 1.5, 1.55, 1.6 };
        public string MeshType { get; init; } = "Q8";
        public string Title { get; init; } =
            "Program 6.4 embankment — 8-node Q8, Smith geometry (GUI Structured Mesh Generate)";

        public int NpTypes => Math.Max(1, MaterialRows?.Count ?? 1);
        public bool UseEx2ElementTypes => NpTypes > 1;
        public bool IsQ4 => string.Equals(MeshType, "Q4", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class LayerAwareBuildResult
    {
        public P64EmbankmentMeshBuilder.MeshResult Mesh { get; init; } = null!;
        public WriteOptions Options { get; init; } = null!;
        public Q8MeshMaterialLayerPlanner.LayerPlan? LayerPlan { get; init; }
        public string? AlignmentNote { get; init; }
        public string ValidationMessage { get; init; } = "";
    }

    public static WriteOptions BuildWriteOptionsFromViewModels(
        int nx1, int nx2, int ny1, int ny2,
        double w1, double s1, double w2, double h1, double h2,
        MaterialPropertiesViewModel? materialsVM,
        FEAnalysisViewModel? feVM,
        string? loadedScriptContent,
        string meshType = "Q8",
        GeometryDefinitionViewModel? geometry = null)
    {
        var materialRows = ResolveMaterialRows(materialsVM, geometry, loadedScriptContent);
        var srfValues = ResolveSrfValues(feVM, loadedScriptContent);
        double tol = feVM?.ConvergenceTolerance ?? 0.0001;
        int limit = feVM?.MaxIterations ?? 500;
        bool multiMaterial = materialRows.Count > 1;
        bool isQ4 = string.Equals(meshType, "Q4", StringComparison.OrdinalIgnoreCase);
        string elemLabel = isQ4 ? "4-node Q4" : "8-node Q8";

        return new WriteOptions
        {
            MeshType = isQ4 ? "Q4" : "Q8",
            Nx1 = nx1, Nx2 = nx2, Ny1 = ny1, Ny2 = ny2,
            W1 = w1, S1 = s1, W2 = w2, H1 = h1, H2 = h2,
            MaterialRows = materialRows,
            Tol = tol,
            Limit = limit,
            SrfValues = srfValues,
            ExpectedFoS = multiMaterial ? 1.5 : 1.55,
            Title = multiMaterial
                ? $"Layered embankment (Smith & Griffiths Program 6.4, {elemLabel})"
                : $"Program 6.4 embankment — {elemLabel}, Smith geometry (GUI Structured Mesh Generate)",
        };
    }

    public static bool TryBuildLayerAwareMesh(
        WriteOptions baseOptions,
        GeometryDefinitionViewModel? geometry,
        MaterialPropertiesViewModel? materials,
        double meshDensity,
        out LayerAwareBuildResult? result,
        out string error)
    {
        result = null;
        error = "";

        if (!Q8MeshMaterialLayerPlanner.TryCreatePlan(geometry, materials, out var plan, out error))
            return false;

        var options = baseOptions;
        string? alignNote = null;

        if (plan.HasLayers)
        {
            if (!Q8MeshMaterialLayerPlanner.TryAlignEmbankmentSubdivisions(
                    plan, baseOptions.Ny1, baseOptions.Ny2, baseOptions.H1, baseOptions.H2, meshDensity,
                    out var alignment, out error))
                return false;

            if (!string.IsNullOrEmpty(alignment.AdjustmentNote))
                alignNote = alignment.AdjustmentNote;

            options = new WriteOptions
            {
                MeshType = baseOptions.MeshType,
                Nx1 = baseOptions.Nx1,
                Nx2 = baseOptions.Nx2,
                Ny1 = alignment.Ny1,
                Ny2 = alignment.Ny2,
                W1 = baseOptions.W1,
                S1 = baseOptions.S1,
                W2 = baseOptions.W2,
                H1 = baseOptions.H1,
                H2 = baseOptions.H2,
                MaterialRows = ResolveMaterialRowsForLayerPlan(plan, baseOptions),
                Tol = baseOptions.Tol,
                Limit = baseOptions.Limit,
                MeshCoordYOffset = baseOptions.MeshCoordYOffset,
                SolverExecutable = baseOptions.SolverExecutable,
                ExpectedFoS = baseOptions.ExpectedFoS,
                ExpectedFoSTolerancePct = baseOptions.ExpectedFoSTolerancePct,
                SrfValues = baseOptions.SrfValues,
                Title = baseOptions.Title,
            };
        }

        var mesh = BuildDefaultMesh(options);
        double coordShift = Q8MeshMaterialLayerPlanner.ComputeCoordShift(mesh);

        if (plan.HasLayers)
            mesh = Q8MeshMaterialLayerPlanner.ApplyElementTypes(mesh, plan, coordShift);

        var validation = Q8MeshLayerValidator.Validate(mesh, plan, coordShift, horizontalZoneMaxX: options.W1);
        if (!validation.IsValid)
        {
            error = validation.Message;
            return false;
        }

        result = new LayerAwareBuildResult
        {
            Mesh = mesh,
            Options = options,
            LayerPlan = plan.HasLayers ? plan : null,
            AlignmentNote = alignNote,
            ValidationMessage = validation.Message,
        };
        return true;
    }

    public static string WriteExplicitProgram64Deck(
        P64EmbankmentMeshBuilder.MeshResult mesh,
        WriteOptions? options = null)
    {
        options ??= new WriteOptions();
        double yMin = mesh.Nodes.Min(n => n.y);
        double coordShift = yMin < 0 ? -yMin : 0;
        double metaYOffset = coordShift > 0 ? coordShift : options.MeshCoordYOffset;
        int npTypes = options.NpTypes;
        var materialRows = ResolveMaterialRowsForWrite(options);

        var sb = new StringBuilder();
        sb.AppendLine("! <solver:p64>");
        sb.AppendLine($"! {options.Title}");
        sb.AppendLine("! Generated by GeotechSlopeApp (P64EmbankmentDatWriter)");
        sb.AppendLine("! <BEGIN:AnalysisOptions>");
        sb.AppendLine("! failure_criterion MohrCoulomb");
        sb.AppendLine("! material_type Isotropic");
        sb.AppendLine($"! mesh_type {(options.IsQ4 ? "Q4" : "Q8")}");
        sb.AppendLine("! mesh_source explicit");
        sb.AppendLine($"! solver_executable {options.SolverExecutable}");
        sb.AppendLine($"! embankment_nx1 {options.Nx1}");
        sb.AppendLine($"! embankment_nx2 {options.Nx2}");
        sb.AppendLine($"! embankment_ny1 {options.Ny1}");
        sb.AppendLine($"! embankment_ny2 {options.Ny2}");
        sb.AppendLine($"! embankment_w1 {Fmt(options.W1)}");
        sb.AppendLine($"! embankment_s1 {Fmt(options.S1)}");
        sb.AppendLine($"! embankment_w2 {Fmt(options.W2)}");
        sb.AppendLine($"! embankment_h1 {Fmt(options.H1)}");
        sb.AppendLine($"! embankment_h2 {Fmt(options.H2)}");
        if (metaYOffset > 0)
            sb.AppendLine($"! mesh_coord_y_offset {Fmt(metaYOffset)}");
        sb.AppendLine("! srf_mode multi");
        if (options.ExpectedFoS > 0)
        {
            sb.AppendLine($"! expected_FoS {Fmt(options.ExpectedFoS)}");
            sb.AppendLine($"! expected_FoS_tolerance_pct {Fmt(options.ExpectedFoSTolerancePct)}");
            sb.AppendLine("! expected_FoS_mode srf");
        }
        sb.AppendLine("! <END:AnalysisOptions>");

        sb.AppendLine($"{mesh.Nels}  {mesh.Nn}  {npTypes}");
        foreach (var row in materialRows)
            sb.AppendLine(row.ToDatLine());

        if (npTypes > 1)
        {
            sb.AppendLine(string.Join("  ",
                mesh.Elements.OrderBy(e => e.id).Select(e => e.etype.ToString(CultureInfo.InvariantCulture))));
        }

        foreach (var (_, x, y) in mesh.Nodes.OrderBy(n => n.id))
            sb.AppendLine($"{Fmt(x)}  {Fmt(y + coordShift)}");

        foreach (var (_, conn, _) in mesh.Elements.OrderBy(e => e.id))
            sb.AppendLine(string.Join("  ", conn.Select(c => c.ToString(CultureInfo.InvariantCulture))));

        sb.AppendLine(mesh.BoundaryConditions.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var (node, fx, fy) in mesh.BoundaryConditions.OrderBy(b => b.node))
            sb.AppendLine($"{node}  {fx}  {fy}");

        sb.AppendLine($"{Fmt(options.Tol)}  {options.Limit}");
        sb.AppendLine(options.SrfValues.Count.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine(string.Join("  ", options.SrfValues.Select(Fmt)));

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    public static P64EmbankmentMeshBuilder.MeshResult BuildDefaultMesh(WriteOptions? options = null)
    {
        options ??= new WriteOptions();
        if (options.IsQ4)
        {
            return P64EmbankmentMeshBuilder.BuildQ4Mesh(
                options.Nx1, options.Nx2, options.Ny1, options.Ny2,
                options.W1, options.S1, options.W2, options.H1, options.H2,
                assignEx2ElementTypes: options.UseEx2ElementTypes);
        }

        return P64EmbankmentMeshBuilder.BuildQ8Mesh(
            options.Nx1, options.Nx2, options.Ny1, options.Ny2,
            options.W1, options.S1, options.W2, options.H1, options.H2,
            assignEx2ElementTypes: options.UseEx2ElementTypes);
    }

    private static IReadOnlyList<MaterialRow> ResolveMaterialRowsForWrite(WriteOptions options)
    {
        if (options.MaterialRows is { Count: > 0 })
            return options.MaterialRows;
        return new[]
        {
            new MaterialRow
            {
                PhiDeg = options.PhiDeg,
                C_kPa = options.C_kPa,
                PsiDeg = options.PsiDeg,
                Gamma = options.Gamma,
                E_kPa = options.E_kPa,
                Nu = options.Nu,
            }
        };
    }

    private static IReadOnlyList<MaterialRow> ResolveMaterialRowsForLayerPlan(
        Q8MeshMaterialLayerPlanner.LayerPlan plan,
        WriteOptions baseOptions)
    {
        var distinctIds = plan.LayerMaterialIds.Distinct().OrderBy(i => i).ToList();
        var fromBase = baseOptions.MaterialRows?.ToList() ?? new List<MaterialRow>();
        var rows = new List<MaterialRow>();
        foreach (int id in distinctIds)
        {
            if (id > 0 && id <= fromBase.Count)
                rows.Add(fromBase[id - 1]);
            else if (fromBase.Count > 0)
                rows.Add(fromBase[Math.Min(id - 1, fromBase.Count - 1)]);
            else
                rows.Add(new MaterialRow());
        }

        return rows.Count > 0 ? rows : fromBase;
    }

    private static List<MaterialRow> ResolveMaterialRows(
        MaterialPropertiesViewModel? materialsVM,
        GeometryDefinitionViewModel? geometry,
        string? loadedScriptContent)
    {
        if (geometry != null
            && geometry.MaterialLayerCutElevations.Count > 0
            && Q8MeshMaterialLayerPlanner.TryCreatePlan(geometry, materialsVM, out var plan, out _)
            && plan.HasLayers)
        {
            var ids = plan.LayerMaterialIds.Distinct().OrderBy(i => i).ToList();
            var fromVm = materialsVM?.Materials?.ToList();
            if (fromVm is { Count: > 0 })
            {
                var rows = new List<MaterialRow>();
                foreach (int id in ids)
                {
                    var mat = fromVm.FirstOrDefault(m => m.Id == id);
                    if (mat != null)
                        rows.Add(MaterialRow.FromMaterialModel(mat));
                }

                if (rows.Count > 0)
                    return rows;
            }
        }

        var fromVmAll = materialsVM?.Materials?
            .Select(MaterialRow.FromMaterialModel)
            .ToList();
        if (fromVmAll is { Count: > 1 })
            return fromVmAll;

        if (!string.IsNullOrWhiteSpace(loadedScriptContent)
            && DatMeshBoundaryImporter.TryGetNpTypes(loadedScriptContent, out int npTypes)
            && npTypes > 1
            && DatMaterialImporter.TryReadP64ExplicitMaterialRows(loadedScriptContent, out var imported)
            && imported.Count >= npTypes)
        {
            return imported.Take(npTypes).Select(m => new MaterialRow
            {
                PhiDeg = m.phi,
                C_kPa = m.c,
                PsiDeg = m.psi,
                Gamma = m.gamma,
                E_kPa = m.e,
                Nu = m.nu,
            }).ToList();
        }

        if (fromVmAll is { Count: 1 })
            return fromVmAll;

        return new List<MaterialRow>();
    }

    private static IReadOnlyList<double> ResolveSrfValues(
        FEAnalysisViewModel? feVM,
        string? loadedScriptContent)
    {
        if (feVM?.SrfValues is { Count: > 0 } entries)
            return entries.Select(e => e.Value).ToList();

        if (!string.IsNullOrWhiteSpace(loadedScriptContent)
            && TryReadSrfValuesFromDat(loadedScriptContent, out var fromDat))
            return fromDat;

        return new[] { 1.0, 1.2, 1.4, 1.5, 1.55, 1.6 };
    }

    private static bool TryReadSrfValuesFromDat(string datContent, out List<double> values)
    {
        values = new List<double>();
        var lines = datContent.Split('\n')
            .Select(l =>
            {
                var bang = l.IndexOf('!');
                return bang >= 0 ? l[..bang].Trim() : l.Trim();
            })
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count < 3)
            return false;

        var last = lines[^1].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var second = lines[^2].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (second.Length != 1
            || !int.TryParse(second[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nsrf)
            || nsrf < 1
            || last.Length < nsrf)
            return false;

        foreach (var tok in last.Take(nsrf))
        {
            if (!double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out double f))
                return false;
            values.Add(f);
        }

        return values.Count == nsrf;
    }

    private static string Fmt(double v) => v.ToString("G10", CultureInfo.InvariantCulture);
}
