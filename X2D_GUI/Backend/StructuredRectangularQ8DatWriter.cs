using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>Writes explicit p64 Q8 decks from a structured rectangular serendipity mesh.</summary>
public static class StructuredRectangularQ8DatWriter
{
    public sealed class WriteOptions
    {
        public double XMin { get; init; }
        public double XMax { get; init; }
        public double YMin { get; init; }
        public double YMax { get; init; }
        public int ElementsX { get; init; }
        public int ElementsY { get; init; }
        public IReadOnlyList<(double x, double y)>? BoundaryPolygon { get; init; }
        public IReadOnlyList<double>? YNodeLevels { get; init; }
        public bool MappedToGeometry { get; init; }
        public IReadOnlyList<P64EmbankmentDatWriter.MaterialRow>? MaterialRows { get; init; }
        public double PhiDeg { get; init; } = 20;
        public double C_kPa { get; init; } = 15;
        public double PsiDeg { get; init; } = 0;
        public double Gamma { get; init; } = 20;
        public double E_kPa { get; init; } = 1.0e5;
        public double Nu { get; init; } = 0.3;
        public double Tol { get; init; } = 0.0001;
        public int Limit { get; init; } = 500;
        public double MeshCoordYOffset { get; init; } = 0;
        public string SolverExecutable { get; init; } = "SRF";
        public double ExpectedFoS { get; init; } = 1.55;
        public double ExpectedFoSTolerancePct { get; init; } = 10;
        public IReadOnlyList<double> SrfValues { get; init; } = new[] { 1.0, 1.2, 1.4, 1.5, 1.55, 1.6 };
        public string Title { get; init; } =
            "Structured rectangular Q8 mesh (GUI Structured Mesh Generate)";

        public int NpTypes => Math.Max(1, MaterialRows?.Count ?? 1);
    }

    public sealed class LayerAwareBuildResult
    {
        public P64EmbankmentMeshBuilder.MeshResult Mesh { get; init; } = null!;
        public WriteOptions Options { get; init; } = null!;
        public Q8MeshMaterialLayerPlanner.LayerPlan? LayerPlan { get; init; }
        public string? AlignmentNote { get; init; }
        public string ValidationMessage { get; init; } = "";
        public int BoundaryFillElementCount { get; init; }
    }

    public static P64EmbankmentMeshBuilder.MeshResult BuildMesh(WriteOptions options) =>
        BuildMeshWithStats(options).Mesh;

    public static StructuredMeshBuildResult BuildMeshWithStats(WriteOptions options) =>
        StructuredRectangularQ8MeshBuilder.BuildWithStats(
            options.XMin, options.XMax, options.YMin, options.YMax,
            options.ElementsX, options.ElementsY,
            new StructuredRectangularQ8MeshBuilder.BuildOptions
            {
                BoundaryPolygon = options.BoundaryPolygon,
                YNodeLevels = options.YNodeLevels,
            });

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
            if (!Q8MeshMaterialLayerPlanner.TryBuildRectangularYNodes(
                    plan, baseOptions.YMin, baseOptions.YMax, baseOptions.ElementsY, meshDensity,
                    out var yNodes, out int elementsY, out alignNote, out error))
                return false;

            var materialRows = BuildMaterialRows(plan, materials, baseOptions);
            options = new WriteOptions
            {
                XMin = baseOptions.XMin,
                XMax = baseOptions.XMax,
                YMin = baseOptions.YMin,
                YMax = baseOptions.YMax,
                ElementsX = baseOptions.ElementsX,
                ElementsY = elementsY,
                BoundaryPolygon = baseOptions.BoundaryPolygon,
                YNodeLevels = yNodes,
                MappedToGeometry = baseOptions.MappedToGeometry,
                MaterialRows = materialRows,
                Tol = baseOptions.Tol,
                Limit = baseOptions.Limit,
                MeshCoordYOffset = baseOptions.MeshCoordYOffset,
                SolverExecutable = baseOptions.SolverExecutable,
                ExpectedFoS = materialRows.Count > 1 ? 1.5 : baseOptions.ExpectedFoS,
                ExpectedFoSTolerancePct = baseOptions.ExpectedFoSTolerancePct,
                SrfValues = baseOptions.SrfValues,
                Title = materialRows.Count > 1
                    ? "Layered rectangular Q8 mesh (GUI Structured Mesh Generate)"
                    : baseOptions.Title,
            };
        }

        var buildStats = BuildMeshWithStats(options);
        var mesh = buildStats.Mesh;
        int boundaryFillCount = buildStats.BoundaryFillElementCount;
        double coordShift = Q8MeshMaterialLayerPlanner.ComputeCoordShift(mesh);

        if (plan.HasLayers)
            mesh = Q8MeshMaterialLayerPlanner.ApplyElementTypes(mesh, plan, coordShift);

        var validation = Q8MeshLayerValidator.Validate(mesh, plan, coordShift);
        if (!validation.IsValid)
        {
            error = validation.Message;
            return false;
        }

        var validationMessage = validation.Message;
        if (boundaryFillCount > 0)
        {
            validationMessage = string.IsNullOrWhiteSpace(validationMessage)
                ? $"Added {boundaryFillCount} boundary fill element(s) along sloped/clipped faces."
                : $"{validationMessage} Added {boundaryFillCount} boundary fill element(s) along sloped/clipped faces.";
        }

        result = new LayerAwareBuildResult
        {
            Mesh = mesh,
            Options = options,
            LayerPlan = plan.HasLayers ? plan : null,
            AlignmentNote = alignNote,
            ValidationMessage = validationMessage,
            BoundaryFillElementCount = boundaryFillCount,
        };
        return true;
    }

    private static IReadOnlyList<P64EmbankmentDatWriter.MaterialRow> BuildMaterialRows(
        Q8MeshMaterialLayerPlanner.LayerPlan plan,
        MaterialPropertiesViewModel? materials,
        WriteOptions baseOptions)
    {
        var distinctIds = plan.LayerMaterialIds.Distinct().OrderBy(i => i).ToList();
        var rows = new List<P64EmbankmentDatWriter.MaterialRow>();
        foreach (int id in distinctIds)
        {
            var mat = materials?.Materials?.FirstOrDefault(m => m.Id == id);
            if (mat != null)
                rows.Add(P64EmbankmentDatWriter.MaterialRow.FromMaterialModel(mat));
            else
                rows.Add(new P64EmbankmentDatWriter.MaterialRow
                {
                    PhiDeg = baseOptions.PhiDeg,
                    C_kPa = baseOptions.C_kPa,
                    PsiDeg = baseOptions.PsiDeg,
                    Gamma = baseOptions.Gamma,
                    E_kPa = baseOptions.E_kPa,
                    Nu = baseOptions.Nu,
                });
        }

        return rows;
    }

    public static string WriteExplicitDeck(
        P64EmbankmentMeshBuilder.MeshResult mesh,
        WriteOptions options)
    {
        double yMin = mesh.Nodes.Min(n => n.y);
        double coordShift = yMin < 0 ? -yMin : 0;
        double metaYOffset = coordShift > 0 ? coordShift : options.MeshCoordYOffset;

        var sb = new StringBuilder();
        sb.AppendLine("! <solver:p64>");
        sb.AppendLine($"! {options.Title}");
        sb.AppendLine("! Generated by GeotechSlopeApp (StructuredRectangularQ8DatWriter)");
        sb.AppendLine("! <BEGIN:AnalysisOptions>");
        sb.AppendLine("! failure_criterion MohrCoulomb");
        sb.AppendLine("! material_type Isotropic");
        sb.AppendLine("! mesh_type Q8");
        sb.AppendLine("! mesh_source explicit");
        sb.AppendLine("! q8_connectivity serendipity");
        sb.AppendLine($"! solver_executable {options.SolverExecutable}");
        sb.AppendLine($"! rectangular_xmin {Fmt(options.XMin)}");
        sb.AppendLine($"! rectangular_xmax {Fmt(options.XMax)}");
        sb.AppendLine($"! rectangular_ymin {Fmt(options.YMin)}");
        sb.AppendLine($"! rectangular_ymax {Fmt(options.YMax)}");
        sb.AppendLine($"! rectangular_nxe {options.ElementsX}");
        sb.AppendLine($"! rectangular_nye {options.ElementsY}");
        if (options.MappedToGeometry)
            sb.AppendLine("! rectangular_mapped_to_geometry true");
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

        int npTypes = options.NpTypes;
        var materialRows = options.MaterialRows?.ToList() ?? new List<P64EmbankmentDatWriter.MaterialRow>
        {
            new()
            {
                PhiDeg = options.PhiDeg,
                C_kPa = options.C_kPa,
                PsiDeg = options.PsiDeg,
                Gamma = options.Gamma,
                E_kPa = options.E_kPa,
                Nu = options.Nu,
            }
        };

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

    private static string Fmt(double v) => v.ToString("G10", CultureInfo.InvariantCulture);
}
