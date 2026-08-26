using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Writes explicit slope-stability .dat decks (p64 / fem_cpp layout) from the discretized Mesh tab.
/// </summary>
public static class SrfExplicitDatWriter
{
    public static string WriteFromViewModels(
        MeshViewModel meshVM,
        MaterialPropertiesViewModel materialsVM,
        BoundaryConditionsViewModel bcVM,
        FEAnalysisViewModel? feVM,
        LoadConditionsViewModel? loadConditionsVM = null,
        GroundwaterViewModel? groundwaterVM = null,
        SupportViewModel? supportVM = null)
    {
        var rows = meshVM.MeshInformationRows.ToList();
        if (rows.Count == 0)
            throw new InvalidOperationException("Mesh has no elements.");

        var built = P610AnalysisOptionsHelper.BuildFromViewModels(materialsVM, meshVM);
        var meshType = P610AnalysisOptionsHelper.NormalizeMeshTypeToken(built.MeshType);
        var nodeCoords = CollectNodeCoords(rows);
        var connectivity = EnumerateExportConnectivity(rows, meshType, nodeCoords).ToList();
        int nels = connectivity.Count;
        var usedNodeIds = connectivity.SelectMany(c => c).ToHashSet();
        var orphanNodeIds = nodeCoords.Keys.Where(id => !usedNodeIds.Contains(id)).OrderBy(id => id).ToList();
        if (orphanNodeIds.Count > 0)
        {
            throw new InvalidOperationException(
                $"Mesh has {orphanNodeIds.Count} node(s) with coordinates but no element connectivity " +
                $"(e.g. node {orphanNodeIds[0]}). Regenerate the mesh or remove orphan nodes before running p64 SRF.");
        }
        int nn = usedNodeIds.Max();
        int npTypes = Math.Max(1, materialsVM?.Materials?.Count ?? 1);

        var options = new P610AnalysisOptions
        {
            FailureCriterion = built.FailureCriterion,
            MaterialType = built.MaterialType,
            MeshType = meshType,
            PreferredExecutable = "SRF",
            SrfMode = "multi",
            MeshSource = "explicit",
            AnalysisType = feVM?.IsSrfAxisymmetric == true ? "axisymmetric" : "",
        };

        var sb = new StringBuilder();
        sb.AppendLine("! <solver:p64>");
        sb.AppendLine("! Slope stability explicit mesh (synced from Mesh tab)");
        sb.AppendLine($"! Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        var optionsBlock = P610AnalysisOptionsHelper.BuildBlock(options);
        if (loadConditionsVM?.IsSeismicLoadUiEnabled == true)
        {
            DatPseudoStaticExporter.TryPatchPseudoStatic(
                optionsBlock,
                loadConditionsVM.SeismicLegendKh,
                loadConditionsVM.SeismicLegendKv,
                out optionsBlock,
                out _);
        }
        sb.AppendLine(optionsBlock);
        sb.AppendLine("! mesh_source explicit");
        if (meshType == "Q8")
            sb.AppendLine("! q8_connectivity serendipity");

        sb.AppendLine($"{nels}  {nn}  {npTypes}");
        if (materialsVM?.Materials is { Count: > 0 } mats)
        {
            foreach (var mat in mats)
                sb.AppendLine(mat.P64MaterialRow);
        }
        else
        {
            sb.AppendLine(BuildMaterialRow(materialsVM));
        }

        if (npTypes > 1)
        {
            var etypeRows = rows.OrderBy(r => r.ElementId).ToList();
            var etypes = new List<int>();
            foreach (var r in etypeRows)
            {
                int et = Math.Clamp(Math.Max(1, r.Etype), 1, npTypes);
                int copies = meshType == "T3" && r.IsQuad && r.Node4 > 0 ? 2 : 1;
                for (int c = 0; c < copies; c++)
                    etypes.Add(et);
            }
            sb.AppendLine(string.Join("  ", etypes.Select(e => e.ToString(CultureInfo.InvariantCulture))));
        }

        foreach (var id in Enumerable.Range(1, nn))
        {
            var (x, y) = nodeCoords.TryGetValue(id, out var pt) ? pt : (0.0, 0.0);
            sb.AppendLine($"{Fmt(x)}  {Fmt(y)}");
        }

        foreach (var conn in connectivity)
            sb.AppendLine(string.Join("  ", conn.Select(n => n.ToString(CultureInfo.InvariantCulture))));

        var fixMap = BoundaryConditionFixityMapper.BuildFixMap(bcVM, rows, nn);
        sb.AppendLine(fixMap.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var (nodeId, (fx, fy)) in fixMap.OrderBy(kv => kv.Key))
            sb.AppendLine($"{nodeId}  {fx}  {fy}");

        double tol = feVM?.ConvergenceTolerance ?? SrfExplicitDeckFreshness.DefaultSrfTolerance;
        int limit = Math.Max(
            feVM?.MaxIterations ?? SrfExplicitDeckFreshness.DefaultSrfIterationLimit,
            SrfExplicitDeckFreshness.DefaultSrfIterationLimit);
        var fValues = CollectSrfValues(feVM);
        sb.AppendLine($"{Fmt(tol)}  {limit}");
        sb.AppendLine(fValues.Count.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine(string.Join("  ", fValues.Select(Fmt)));

        var text = sb.ToString().TrimEnd() + Environment.NewLine;
        if (groundwaterVM != null)
        {
            var displayModel = groundwaterVM.BuildModel();
            // Export γsat in material order when any material enables saturated unit weight.
            if (materialsVM?.Materials is { Count: > 0 } matsForSat
                && matsForSat.Any(m => m.IsSaturatedEnabled))
            {
                displayModel.GammaSat = matsForSat
                    .Select(m => m.IsSaturatedEnabled ? m.SaturatedUnitWeight : m.UnitWeight)
                    .ToList();
            }
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
            GroundwaterModel? pondedGw = null;
            if (groundwaterVM != null)
            {
                pondedGw = groundwaterVM.BuildModel();
                if (materialsVM?.Materials is { Count: > 0 } matsPond
                    && matsPond.Any(m => m.IsSaturatedEnabled))
                {
                    pondedGw.GammaSat = matsPond
                        .Select(m => m.IsSaturatedEnabled ? m.SaturatedUnitWeight : m.UnitWeight)
                        .ToList();
                }
            }
            text = DistributedEdgeLoadDatWriter.MergeFromViewModels(
                text, loadConditionsVM, meshVM, meshVM.GeometryDefinitionVM, pondedGw);
            text = NodalPointLoadDatWriter.MergeFromViewModels(
                text, loadConditionsVM, meshVM, meshVM.GeometryDefinitionVM);
        }

        return text;
    }

    private static string BuildMaterialRow(MaterialPropertiesViewModel? materialsVM)
    {
        var mat = materialsVM?.Materials?.FirstOrDefault();
        if (mat != null)
            return mat.P64MaterialRow;
        return "20.0  15.0  0.0  20.0  100000  0.3";
    }

    private static List<double> CollectSrfValues(FEAnalysisViewModel? feVM)
    {
        if (feVM?.SrfValues is { Count: > 0 } entries)
            return entries.Select(e => e.Value).ToList();
        return new List<double> { 1.0, 1.2, 1.4 };
    }

    private static IEnumerable<int[]> EnumerateExportConnectivity(
        IReadOnlyList<MeshInformationRow> rows,
        string meshType,
        SortedDictionary<int, (double X, double Y)> nodeCoords)
    {
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

    private static string Fmt(double v) => v.ToString("G10", CultureInfo.InvariantCulture);
}
