using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>Builds and merges ! &lt;BEGIN:NodalPointLoads&gt; blocks for 2D solvers.</summary>
public static class NodalPointLoadDatWriter
{
    public const string BeginTag = "! <BEGIN:NodalPointLoads>";
    public const string EndTag = "! <END:NodalPointLoads>";
    public const string SectionName = "NodalPointLoads";

    public sealed class NodalRow
    {
        public int NodeId { get; init; }
        public double Fx { get; init; }
        public double Fy { get; init; }
    }

    public static string BuildBlock(
        LoadConditionsViewModel loads,
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry)
    {
        var rows = CollectRows(loads, mesh, geometry);
        var sb = new StringBuilder();
        sb.AppendLine(BeginTag);
        sb.AppendLine($"! {rows.Count.ToString(CultureInfo.InvariantCulture)}");
        foreach (var row in rows)
            sb.AppendLine($"! {row.NodeId}  {Fmt(row.Fx)}  {Fmt(row.Fy)}");
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
        GeometryDefinitionViewModel geometry)
        => MergeSection(datContent, BuildBlock(loads, mesh, geometry));

    public static List<NodalRow> CollectRows(
        LoadConditionsViewModel loads,
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry)
    {
        var merged = new Dictionary<int, (double Fx, double Fy)>();
        if (mesh is not { IsDiscretized: true })
            return new List<NodalRow>();

        foreach (var load in loads.FilteredLoads.Where(l => l.IsEnabled && l.IsPointLoad))
        {
            if (!IsPointPlaced(load))
                continue;

            int nodeId = LoadExportDirectionResolver.ResolvePointNodeId(load, mesh, geometry);
            if (nodeId <= 0)
                continue;

            var (fx, fy) = LoadExportDirectionResolver.ResolvePointForceComponents(load, geometry, mesh, nodeId);
            if (merged.TryGetValue(nodeId, out var existing))
                merged[nodeId] = (existing.Fx + fx, existing.Fy + fy);
            else
                merged[nodeId] = (fx, fy);
        }

        return merged
            .OrderBy(kv => kv.Key)
            .Select(kv => new NodalRow { NodeId = kv.Key, Fx = kv.Value.Fx, Fy = kv.Value.Fy })
            .ToList();
    }

    private static bool IsPointPlaced(LoadModel load)
        => load.AppliedToNodeId is > 0
           || Math.Abs(load.StartX) > 1e-6
           || Math.Abs(load.StartY) > 1e-6;

    private static string Fmt(double v) => v.ToString("G10", CultureInfo.InvariantCulture);
}
