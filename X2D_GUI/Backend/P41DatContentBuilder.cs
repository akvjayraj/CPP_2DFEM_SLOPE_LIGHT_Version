using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Builds p41-style .dat file content for 1D rod analysis from geometry, materials, and loads.
/// When a 1D mesh exists in MeshVM, uses mesh element lengths; otherwise uses geometry vertices.
/// Format matches chap04/p41_1.dat.
/// </summary>
public static class P41DatContentBuilder
{
    /// <summary>
    /// Build .dat content from the main window's view models.
    /// When meshVM has a 1D mesh (IsDiscretized, MeshElements), uses mesh for element count and lengths; otherwise uses geometry.
    /// Material geometry is used for EA per segment; loads use geometry for coordinate conversion.
    /// nr (number of restrained nodes) = number of rows in boundary conditions DataGrid when bcVM is provided.
    /// </summary>
    public static string Build(
        GeometryDefinitionViewModel geometry,
        LoadConditionsViewModel loads,
        GeometryDefinitionViewModel? materialGeometry = null,
        MeshViewModel? meshVM = null,
        BoundaryConditionsViewModel? bcVM = null,
        StagedAnalysisContext? stagedContext = null)
    {
        var matSegments = MaterialSegmentsFor1D(materialGeometry, geometry);
        List<double> ell;
        if (meshVM != null && meshVM.IsDiscretized && meshVM.MeshElements.Count > 0)
        {
            ell = new List<double>();
            foreach (var el in meshVM.MeshElements)
            {
                if (el.Nodes.Count < 2) continue;
                var p0 = el.Nodes[0];
                var p1 = el.Nodes[1];
                double dx = p1.X - p0.X;
                double dy = p1.Y - p0.Y;
                var len = System.Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-10) len = 1e-6;
                ell.Add(len);
            }
            if (ell.Count == 0)
                return BuildFromGeometry(geometry, loads, matSegments, bcVM, stagedContext);
        }
        else
            return BuildFromGeometry(geometry, loads, matSegments, bcVM, stagedContext);

        return BuildFromEll(ell, geometry, loads, matSegments, bcVM, stagedContext);
    }

    /// <summary>1D bar follows the boundary polyline only; shared/internal edges must not add extra material layers.</summary>
    private static IList<GeometryLineSegment> MaterialSegmentsFor1D(GeometryDefinitionViewModel? materialGeometry, GeometryDefinitionViewModel geometry)
    {
        var all = materialGeometry?.GeometryLineSegments ?? geometry.GeometryLineSegments;
        return all.Where(s => s.IsBoundaryEdge).ToList();
    }

    private static string BuildFromGeometry(
        GeometryDefinitionViewModel geometry,
        LoadConditionsViewModel loads,
        IList<GeometryLineSegment> matSegments,
        BoundaryConditionsViewModel? bcVM = null,
        StagedAnalysisContext? stagedContext = null)
    {
        var vertices = geometry.GeometryVertexRows;
        if (vertices.Count < 2)
            return GetEmptyTemplate();

        var ell = new List<double>();
        for (int i = 0; i < vertices.Count - 1; i++)
        {
            var v1 = vertices[i];
            var v2 = vertices[i + 1];
            var dx = v2.AxisX - v1.AxisX;
            var dy = v2.AxisY - v1.AxisY;
            var len = System.Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-10) len = 1e-6;
            ell.Add(len);
        }
        return BuildFromEll(ell, geometry, loads, matSegments, bcVM, stagedContext);
    }

    private static string BuildFromEll(
        List<double> ell,
        GeometryDefinitionViewModel geometry,
        LoadConditionsViewModel loads,
        IList<GeometryLineSegment> matSegments,
        BoundaryConditionsViewModel? bcVM = null,
        StagedAnalysisContext? stagedContext = null)
    {
        // np_types = number of layers in "Layer Number & Name" box (one per line segment)
        int npTypes = matSegments.Count;
        if (npTypes == 0) npTypes = 1;

        var propList = new List<double>();
        for (int i = 0; i < npTypes; i++)
        {
            double ea = 1e5;
            if (i < matSegments.Count)
            {
                var seg = matSegments[i];
                if (double.TryParse(seg.YoungsModulus, NumberStyles.Float, CultureInfo.InvariantCulture, out var e) && e > 0)
                    ea = e;
            }
            propList.Add(ea);
        }

        // Assign each element to a layer (1-based type index) by position along the bar
        var etype = new List<int>();
        var segmentBounds = geometry.GetSegmentBounds();
        double cum = 0;
        for (int i = 0; i < ell.Count; i++)
        {
            double elemCenter = cum + ell[i] * 0.5;
            cum += ell[i];
            int layer = 0;
            if (segmentBounds.Count >= 2)
            {
                for (int k = 0; k < segmentBounds.Count - 1; k++)
                {
                    if (elemCenter < segmentBounds[k + 1])
                    {
                        layer = k;
                        break;
                    }
                    layer = segmentBounds.Count - 2;
                }
            }
            etype.Add(Math.Min(layer + 1, npTypes));
        }

        int nels = ell.Count;

        var sb = new System.Text.StringBuilder();
        if (stagedContext != null)
        {
            sb.AppendLine($"# Staged phase {stagedContext.PhaseNumber}: {stagedContext.PhaseName}");
            sb.AppendLine($"# lambda={stagedContext.Lambda.ToString("0.###", CultureInfo.InvariantCulture)} loadFactor={stagedContext.LoadFactor.ToString("0.###", CultureInfo.InvariantCulture)} reset={stagedContext.ResetDisplacements} mode={stagedContext.SolverMode}");
            if (stagedContext.ExcavatedSurfaceNumbers.Count > 0)
                sb.AppendLine($"# excavatedSurfaces={string.Join(",", stagedContext.ExcavatedSurfaceNumbers)}");
        }
        sb.AppendLine($"{nels}  {npTypes}");
        sb.AppendLine(string.Join("  ", propList.Select(p => p.ToString("0.0", CultureInfo.InvariantCulture))));
        if (npTypes > 1)
            sb.AppendLine(string.Join("  ", etype));
        sb.AppendLine(string.Join("  ", ell.Select(l => l.ToString("0.##", CultureInfo.InvariantCulture))));

        // nr = restrained nodes (Fixed, Roller; exclude Prescribed Displacement and Force which go to fixed_freedoms)
        var restrainedNodes = GetRestrainedNodes(bcVM);
        int nr = restrainedNodes.Count;
        sb.AppendLine($"{nr}");
        foreach (var (nodeId, nf) in restrainedNodes)
            sb.AppendLine($"{nodeId}  {nf}");

        double loadScale = stagedContext != null ? stagedContext.Lambda * stagedContext.LoadFactor : 1.0;
        var loadLines = BuildLoadLines(loads, ell, geometry, loadScale);
        sb.AppendLine(loadLines.loadedNodes.ToString());
        sb.Append(loadLines.loadText);

        // fixed_freedoms = BC rows with BC Type "Prescribed Displacement" or "Force"; each line: node value
        var fixedFreedoms = GetFixedFreedoms(bcVM);
        sb.AppendLine(fixedFreedoms.Count.ToString());
        foreach (var (nodeId, value) in fixedFreedoms)
            sb.AppendLine($"{nodeId}  {value.ToString("0.####", CultureInfo.InvariantCulture)}");

        return sb.ToString();
    }

    /// <summary>Builds list of (nodeId, nf) for .dat restrained nodes. Excludes Prescribed Displacement and Force (those go to fixed_freedoms).</summary>
    private static List<(int nodeId, int nf)> GetRestrainedNodes(BoundaryConditionsViewModel? bcVM)
    {
        var list = new List<(int, int)>();
        if (bcVM?.BoundaryConditions != null && bcVM.BoundaryConditions.Count > 0)
        {
            foreach (var row in bcVM.BoundaryConditions)
            {
                if (IsFixedFreedomRow(row)) continue; // prescribed displacement / force go to fixed_freedoms
                list.Add((row.NodeId, 0)); // restrained
            }
            if (list.Count == 0)
                list.Add((1, 0)); // default: node 1 fixed if all rows are prescribed/force
            return list;
        }
        list.Add((1, 0)); // default: node 1 fixed
        return list;
    }

    /// <summary>fixed_freedoms = BC rows with BC Type "Prescribed Displacement" or "Force". p41 format: node value per line.</summary>
    private static List<(int nodeId, double value)> GetFixedFreedoms(BoundaryConditionsViewModel? bcVM)
    {
        var list = new List<(int, double)>();
        if (bcVM?.BoundaryConditions == null) return list;
        foreach (var row in bcVM.BoundaryConditions)
        {
            if (!IsFixedFreedomRow(row)) continue;
            if (!double.TryParse(row.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
                val = 0;
            list.Add((row.NodeId, val));
        }
        return list;
    }

    private static bool IsFixedFreedomRow(BoundaryConditionRow row)
    {
        var t = (row.BCType ?? "").Trim();
        return string.Equals(t, "Prescribed Displacement", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "Force", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds loaded_nodes and (k, loads(nf(:,k))) from Load Conditions popup.
    /// loaded_nodes = number of nodes with applied loads; for each node k output k and load value (p41 nodof=1).
    /// Point loads: map canvas position to path distance, then to nearest node; use Magnitude as-is.
    /// Line loads: distribute to nodes along segment. Sign from line direction: Start→End toward increasing node number (1→2→…) → positive magnitude; toward decreasing node number → negative magnitude. Script/.dat is updated with the signed value.
    /// </summary>
    private static (int loadedNodes, string loadText) BuildLoadLines(
        LoadConditionsViewModel loads,
        List<double> ell,
        GeometryDefinitionViewModel geometry,
        double loadScale = 1.0)
    {
        var nodePositions = new List<double> { 0 };
        double cum = 0;
        foreach (var l in ell)
        {
            cum += l;
            nodePositions.Add(cum);
        }

        var loadByNode = new Dictionary<int, double>();
        // FilteredLoads = current-stage loads only (StageNumber == active stage, plus legacy null).
        // Using Loads instead would inject every stage's loads into the .dat for the active stage's analysis.
        foreach (var load in loads.FilteredLoads.Where(l => l.IsEnabled))
        {
            if (load.Type == LoadType.Point)
            {
                var axisPt = geometry.ConvertCanvasToAxis(new Point(load.StartX, load.StartY));
                double pathDist = geometry.GetPathDistanceAtPoint(axisPt.X, axisPt.Y);
                int node = 1;
                double minDist = double.MaxValue;
                for (int i = 0; i < nodePositions.Count; i++)
                {
                    var d = Math.Abs(nodePositions[i] - pathDist);
                    if (d < minDist)
                    {
                        minDist = d;
                        node = i + 1;
                    }
                }
                if (!loadByNode.TryAdd(node, load.Magnitude))
                    loadByNode[node] += load.Magnitude;
            }
            else if ((load.Type == LoadType.Line || load.Type == LoadType.Triangular) && nodePositions.Count >= 2)
            {
                var startAxis = geometry.ConvertCanvasToAxis(new Point(load.StartX, load.StartY));
                var endAxis = geometry.ConvertCanvasToAxis(new Point(load.EndX, load.EndY));
                double sStart = geometry.GetPathDistanceAtPoint(startAxis.X, startAxis.Y);
                double sEnd = geometry.GetPathDistanceAtPoint(endAxis.X, endAxis.Y);
                double s0 = Math.Min(sStart, sEnd);
                double s1 = Math.Max(sStart, sEnd);
                double sign = sStart <= sEnd ? 1.0 : -1.0;
                var nodesInRange = new List<(int node, double s)>();
                for (int i = 0; i < nodePositions.Count; i++)
                {
                    double s = nodePositions[i];
                    if (s >= s0 - 1e-10 && s <= s1 + 1e-10)
                        nodesInRange.Add((i + 1, s));
                }
                if (nodesInRange.Count > 0)
                {
                    if (load.Type == LoadType.Line)
                    {
                        double signedMagnitude = sign * load.Magnitude;
                        double perNode = signedMagnitude / nodesInRange.Count;
                        foreach (var (n, _) in nodesInRange)
                        {
                            if (!loadByNode.TryAdd(n, perNode))
                                loadByNode[n] += perNode;
                        }
                    }
                    else
                    {
                        foreach (var (n, s) in nodesInRange)
                        {
                            double t = s1 > s0 ? (s - s0) / (s1 - s0) : 0.5;
                            double mag = sign * (load.Magnitude + t * (load.EndMagnitude - load.Magnitude));
                            if (!loadByNode.TryAdd(n, mag))
                                loadByNode[n] += mag;
                        }
                    }
                }
            }
        }

        if (loadByNode.Count == 0)
            return (0, "");

        var loadEntries = loadByNode.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value * loadScale)).ToList();
        var sb = new System.Text.StringBuilder();
        foreach (var (node, val) in loadEntries)
            sb.Append($" {node} {val.ToString("0.####", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        return (loadEntries.Count, sb.ToString());
    }

    private static string GetEmptyTemplate()
    {
        return @"4  1
100000.0
0.25  0.25  0.25  0.25
1  
5 0
5
1 -0.625  2 -1.25  3 -1.25  4 -1.25  5 -0.625
0
";
    }
}
