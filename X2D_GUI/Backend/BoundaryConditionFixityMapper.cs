using System;
using System.Collections.Generic;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Builds solver fixity maps from Boundary Conditions rows and propagates corner-node
/// restraints onto boundary midside nodes for higher-order elements.
/// </summary>
internal static class BoundaryConditionFixityMapper
{
    internal static Dictionary<int, (int FixX, int FixY)> BuildFixMap(
        BoundaryConditionsViewModel? bcVM,
        IReadOnlyList<MeshInformationRow> rows,
        int nn)
    {
        var map = BuildExplicitNodeFixities(bcVM, nn);
        if (map.Count == 0 || rows.Count == 0)
            return map;

        PropagateBoundaryMidsideFixities(rows, nn, map);
        return map;
    }

    private static Dictionary<int, (int FixX, int FixY)> BuildExplicitNodeFixities(
        BoundaryConditionsViewModel? bcVM,
        int nn)
    {
        var map = new Dictionary<int, (int Fx, int Fy)>();
        if (bcVM?.BoundaryConditions == null)
            return map;

        foreach (var row in bcVM.BoundaryConditions)
        {
            if (row.NodeId < 1 || row.NodeId > nn)
                continue;

            if (!map.TryGetValue(row.NodeId, out var cur))
                cur = (1, 1);

            var bc = row.BCType?.ToUpperInvariant() ?? "";
            int fx = cur.Fx;
            int fy = cur.Fy;

            if (bc.Contains("FIXED", StringComparison.Ordinal)
                || bc.Contains("RESTRAIN", StringComparison.Ordinal)
                || bc.Contains("PIN", StringComparison.Ordinal))
            {
                fx = 0;
                fy = 0;
            }
            else if (bc.Contains("ROLLER", StringComparison.Ordinal))
            {
                if (row.DegreeOfFreedom == 0) fx = 0;
                else fy = 0;
            }
            else
            {
                if (row.DegreeOfFreedom == 0) fx = 0;
                else fy = 0;
            }

            if (fx == 0 || fy == 0)
                map[row.NodeId] = (fx, fy);
        }

        return map;
    }

    private static void PropagateBoundaryMidsideFixities(
        IReadOnlyList<MeshInformationRow> rows,
        int nn,
        Dictionary<int, (int FixX, int FixY)> map)
    {
        var edgeUseCounts = new Dictionary<(int A, int B), (int Count, int MidNodeId)>();
        foreach (var row in rows)
        {
            foreach (var (n1, n2, mid) in EnumerateEdgesWithMidside(row))
            {
                if (n1 <= 0 || n2 <= 0 || mid <= 0 || mid > nn)
                    continue;

                var key = NormalizeEdge(n1, n2);
                if (edgeUseCounts.TryGetValue(key, out var info))
                    edgeUseCounts[key] = (info.Count + 1, info.MidNodeId);
                else
                    edgeUseCounts[key] = (1, mid);
            }
        }

        foreach (var (key, info) in edgeUseCounts)
        {
            if (info.Count != 1)
                continue; // interior edge

            if (!map.TryGetValue(key.A, out var aFix) || !map.TryGetValue(key.B, out var bFix))
                continue;

            int fx = aFix.FixX == 0 && bFix.FixX == 0 ? 0 : 1;
            int fy = aFix.FixY == 0 && bFix.FixY == 0 ? 0 : 1;
            if (fx == 1 && fy == 1)
                continue;

            if (map.TryGetValue(info.MidNodeId, out var cur))
            {
                fx = Math.Min(fx, cur.FixX);
                fy = Math.Min(fy, cur.FixY);
            }

            map[info.MidNodeId] = (fx, fy);
        }
    }

    private static IEnumerable<(int N1, int N2, int MidNodeId)> EnumerateEdgesWithMidside(MeshInformationRow row)
    {
        foreach (var (a, b, mid) in row.EnumerateCornerEdgesWithMid())
        {
            if (mid > 0)
                yield return (a, b, mid);
        }
    }

    private static (int A, int B) NormalizeEdge(int a, int b)
        => a <= b ? (a, b) : (b, a);
}
