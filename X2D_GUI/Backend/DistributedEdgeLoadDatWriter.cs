using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>Builds and merges ! &lt;BEGIN:DistributedEdgeLoads&gt; blocks from line and triangular load rows.</summary>
public static class DistributedEdgeLoadDatWriter
{
    public const string BeginTag = "! <BEGIN:DistributedEdgeLoads>";
    public const string EndTag = "! <END:DistributedEdgeLoads>";
    public const string SectionName = "DistributedEdgeLoads";

    public sealed class EdgeLoadRow
    {
        public int NodeA { get; init; }
        public int NodeB { get; init; }
        public int NodeC { get; init; }
        public double QA { get; init; }
        public double QB { get; init; }
    }

    public sealed class LoadGroup
    {
        public int Kind { get; init; }
        public double DirX { get; init; }
        public double DirY { get; init; }
        public List<EdgeLoadRow> Edges { get; } = new();
    }

    public static string BuildBlock(
        LoadConditionsViewModel loads,
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry,
        GroundwaterModel? groundwater = null)
    {
        var groups = CollectGroups(loads, mesh, geometry);
        if (groundwater != null)
            groups.AddRange(PondedWaterEdgeLoadBuilder.CollectGroups(groundwater, mesh, geometry));
        var sb = new StringBuilder();
        sb.AppendLine(BeginTag);
        sb.AppendLine($"! {groups.Count.ToString(CultureInfo.InvariantCulture)}");
        foreach (var g in groups)
        {
            sb.AppendLine($"! {g.Kind}  {Fmt(g.DirX)}  {Fmt(g.DirY)}");
            sb.AppendLine($"! {g.Edges.Count.ToString(CultureInfo.InvariantCulture)}");
            foreach (var e in g.Edges)
            {
                if (e.NodeC > 0)
                    sb.AppendLine($"! {e.NodeA}  {e.NodeB}  {e.NodeC}  {Fmt(e.QA)}  {Fmt(e.QB)}");
                else
                    sb.AppendLine($"! {e.NodeA}  {e.NodeB}  {Fmt(e.QA)}  {Fmt(e.QB)}");
            }
        }
        sb.AppendLine(EndTag);
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string MergeSection(string datContent, string block)
    {
        if (string.IsNullOrEmpty(datContent))
            return block;

        var pattern = $@"! <BEGIN:{SectionName}>[\s\S]*?! <END:{SectionName}>\s*";
        if (Regex.IsMatch(datContent, pattern, RegexOptions.IgnoreCase))
            return Regex.Replace(datContent, pattern, block.TrimEnd() + Environment.NewLine, RegexOptions.IgnoreCase);

        int insertAt = datContent.IndexOf("! <BEGIN:", StringComparison.OrdinalIgnoreCase);
        if (insertAt < 0)
            return datContent.TrimEnd() + Environment.NewLine + Environment.NewLine + block;

        return datContent.Insert(insertAt, block + Environment.NewLine);
    }

    public static string MergeFromViewModels(
        string datContent,
        LoadConditionsViewModel loads,
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry,
        GroundwaterModel? groundwater = null)
        => MergeSection(datContent, BuildBlock(loads, mesh, geometry, groundwater));

    public static List<LoadGroup> CollectGroups(
        LoadConditionsViewModel loads,
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry)
    {
        var grouped = new Dictionary<(int kind, double dx, double dy), LoadGroup>();
        if (mesh is not { IsDiscretized: true } || mesh.MeshInformationRows.Count == 0)
            return new List<LoadGroup>();

        var boundaryEdges = BuildBoundaryEdgeSet(mesh);

        foreach (var load in loads.FilteredLoads.Where(l => l.IsEnabled && l.IsDistributedLineLoad))
        {
            if (!IsSegmentPlaced(load))
                continue;

            var (kind, dirX, dirY) = ResolveDirection(load, mesh, geometry);
            var key = (kind, Math.Round(dirX, 6), Math.Round(dirY, 6));
            if (!grouped.TryGetValue(key, out var group))
            {
                group = new LoadGroup { Kind = kind, DirX = dirX, DirY = dirY };
                grouped[key] = group;
            }

            var stations = CollectStations(load, mesh, geometry);
            if (stations.Count < 2)
                continue;

            bool isLine = load.Type == LoadType.Line;
            for (int i = 0; i < stations.Count - 1; i++)
            {
                var a = stations[i];
                var b = stations[i + 1];
                if (!TryResolveEdge(a.NodeId, b.NodeId, boundaryEdges, out var nA, out var nB, out var nC))
                    continue;

                double tA = stations.Count == 1 ? 0.0 : (double)i / (stations.Count - 1);
                double tB = stations.Count == 1 ? 1.0 : (double)(i + 1) / (stations.Count - 1);
                double qA = isLine
                    ? load.Magnitude
                    : load.Magnitude + tA * (load.EndMagnitude - load.Magnitude);
                double qB = isLine
                    ? load.Magnitude
                    : load.Magnitude + tB * (load.EndMagnitude - load.Magnitude);

                group.Edges.Add(new EdgeLoadRow
                {
                    NodeA = nA,
                    NodeB = nB,
                    NodeC = nC,
                    QA = qA,
                    QB = qB
                });
            }
        }

        return grouped.Values.Where(g => g.Edges.Count > 0).ToList();
    }

    private static bool IsSegmentPlaced(LoadModel load)
    {
        double dx = load.EndX - load.StartX;
        double dy = load.EndY - load.StartY;
        return dx * dx + dy * dy >= 4.0;
    }

    private sealed class Station
    {
        public int NodeId { get; init; }
        public double T { get; init; }
    }

    private static List<Station> CollectStations(
        LoadModel load,
        MeshViewModel mesh,
        GeometryDefinitionViewModel geometry)
    {
        var startAxis = geometry.ConvertCanvasToAxis(new Point(load.StartX, load.StartY));
        var endAxis = geometry.ConvertCanvasToAxis(new Point(load.EndX, load.EndY));
        double segDx = endAxis.X - startAxis.X;
        double segDy = endAxis.Y - startAxis.Y;
        double segLenSq = segDx * segDx + segDy * segDy;
        if (segLenSq < 1e-12)
            return new List<Station>();

        double tolerance = EstimateAxisTolerance(mesh);
        var hits = new List<(double T, int NodeId)>();

        for (int i = 0; i < mesh.DiscretizationNodes.Count; i++)
        {
            var node = mesh.DiscretizationNodes[i];
            double px = node.Location.X - startAxis.X;
            double py = node.Location.Y - startAxis.Y;
            double t = (px * segDx + py * segDy) / segLenSq;
            if (t < -0.05 || t > 1.05)
                continue;

            double closestX = startAxis.X + t * segDx;
            double closestY = startAxis.Y + t * segDy;
            double perp = Math.Sqrt(
                (node.Location.X - closestX) * (node.Location.X - closestX)
                + (node.Location.Y - closestY) * (node.Location.Y - closestY));
            if (perp > tolerance)
                continue;

            hits.Add((Math.Clamp(t, 0, 1), i + 1));
        }

        return hits
            .OrderBy(h => h.T)
            .DistinctBy(h => h.NodeId)
            .Select(h => new Station { NodeId = h.NodeId, T = h.T })
            .ToList();
    }

    private static double EstimateAxisTolerance(MeshViewModel mesh)
    {
        var heights = new List<double>();
        foreach (var row in mesh.MeshInformationRows)
        {
            if (row.Node1 <= 0) continue;
            var nodes = mesh.DiscretizationNodes;
            if (TryGetAxisY(nodes, row.Node1, out var y1) && TryGetAxisY(nodes, row.Node2, out var y2))
            {
                heights.Add(Math.Abs(y2 - y1));
                if (row.Node3 > 0 && TryGetAxisY(nodes, row.Node3, out var y3))
                {
                    heights.Add(Math.Abs(y3 - y1));
                    heights.Add(Math.Abs(y3 - y2));
                }
            }
        }

        if (heights.Count == 0)
            return 0.5;

        heights.Sort();
        return Math.Max(0.05, heights[heights.Count / 2] * 0.75);
    }

    private static bool TryGetAxisY(IReadOnlyList<MeshDiscretizationNodeDisplay> nodes, int nodeId, out double y)
    {
        y = 0;
        int idx = nodeId - 1;
        if (idx < 0 || idx >= nodes.Count)
            return false;
        y = nodes[idx].Location.Y;
        return true;
    }

    private static HashSet<(int, int)> BuildBoundaryEdgeSet(MeshViewModel mesh)
    {
        var counts = new Dictionary<(int, int), int>();
        foreach (var row in mesh.MeshInformationRows)
        {
            foreach (var (a, b) in EnumerateElementEdges(row))
            {
                var key = a < b ? (a, b) : (b, a);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        return counts.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToHashSet();
    }

    private static IEnumerable<(int A, int B)> EnumerateElementEdges(MeshInformationRow row)
    {
        foreach (var (a, b, _) in row.EnumerateCornerEdgesWithMid())
            yield return (a, b);
    }

    private static bool TryResolveEdge(
        int nodeA,
        int nodeB,
        HashSet<(int, int)> boundaryEdges,
        out int nA,
        out int nB,
        out int nC)
    {
        nA = nodeA;
        nB = nodeB;
        nC = 0;
        var key = nodeA < nodeB ? (nodeA, nodeB) : (nodeB, nodeA);
        if (!boundaryEdges.Contains(key))
            return false;
        if (nodeA > nodeB)
        {
            nA = nodeB;
            nB = nodeA;
        }
        return true;
    }

    private static (int kind, double dirX, double dirY) ResolveDirection(
        LoadModel load,
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry)
        => LoadExportDirectionResolver.ResolveEdgeGroupDirection(load, geometry, mesh);

    private static string Fmt(double v) => v.ToString("G10", CultureInfo.InvariantCulture);
}
