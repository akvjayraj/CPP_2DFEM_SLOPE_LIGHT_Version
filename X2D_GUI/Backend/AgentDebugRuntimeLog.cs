using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GeotechSlopeApp.Models.FiniteElement;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

internal static class AgentDebugRuntimeLog
{
    private const string LogPath = @"C:\Users\akv\.cursor\worktrees\CPP_2DFEM_SLOPE\debug-d5c178.log";
    private const string SessionId = "d5c178";

    public static void Write(string runId, string hypothesisId, string location, string message, object? data)
    {
        try
        {
            var payload = new
            {
                sessionId = SessionId,
                id = "log_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "_" + Guid.NewGuid().ToString("N")[..8],
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                runId,
                hypothesisId,
                location,
                message,
                data
            };
            File.AppendAllText(LogPath, JsonSerializer.Serialize(payload) + Environment.NewLine);
        }
        catch
        {
            // Debug instrumentation must never affect the GUI run path.
        }
    }

    public static object SummarizeMesh(MeshViewModel? mesh)
    {
        var rows = mesh?.MeshInformationRows;
        if (mesh == null || rows == null || rows.Count == 0)
            return new { hasMesh = false };

        var nodeIds = rows
            .SelectMany(r => new[] { r.Node1, r.Node2, r.Node3, r.Node4, r.Node5, r.Node6, r.Node7, r.Node8 })
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        return new
        {
            hasMesh = true,
            mesh.IsDiscretized,
            rowCount = rows.Count,
            nodeIdCount = nodeIds.Count,
            t3Rows = rows.Count(r => r.IsT3),
            t6Rows = rows.Count(r => r.IsT6),
            q4Rows = rows.Count(r => r.IsQuad && !r.IsQ8),
            q8Rows = rows.Count(r => r.IsQ8),
            firstRows = rows.Take(5).Select(r => new
            {
                r.ElementId,
                r.Etype,
                r.IsT3,
                r.IsT6,
                r.IsQuad,
                r.IsQ8,
                nodes = new[] { r.Node1, r.Node2, r.Node3, r.Node4, r.Node5, r.Node6, r.Node7, r.Node8 }
            }).ToList()
        };
    }

    public static object SummarizeScript(ScriptViewModel? script)
    {
        if (script == null)
            return new { hasScriptVm = false };
        var content = script.ScriptContent ?? "";
        return new
        {
            hasScriptVm = true,
            script.IsManualScriptContent,
            contentLength = content.Length,
            isSrfDeck = P610AnalysisOptionsHelper.IsSrfDatDeck(content),
            programId = FemCppProgramRouter.ResolveProgramId(content),
            dat = SummarizeDat(content)
        };
    }

    public static object SummarizeDat(string? datContent)
    {
        if (string.IsNullOrWhiteSpace(datContent))
            return new { hasDat = false };

        string meshType = "";
        string executable = "";
        if (P610AnalysisOptionsHelper.TryParse(datContent, out var options))
        {
            meshType = options.MeshType;
            executable = options.PreferredExecutable;
        }

        return new
        {
            hasDat = true,
            length = datContent.Length,
            meshType,
            executable,
            firstConnectivityRows = TryReadFirstConnectivityRows(datContent, count: 5)
        };
    }

    private static IReadOnlyList<int[]> TryReadFirstConnectivityRows(string datContent, int count)
    {
        var rows = new List<int[]>();
        try
        {
            var lines = datContent.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("!", StringComparison.Ordinal))
                .ToList();
            if (lines.Count == 0)
                return rows;

            int header = -1;
            int nels = 0;
            int nn = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                var parts = SplitInts(lines[i]);
                if (parts.Length >= 2 && parts[0] > 0 && parts[1] > 0)
                {
                    header = i;
                    nels = parts[0];
                    nn = parts[1];
                    break;
                }
            }
            if (header < 0 || nels <= 0 || nn <= 0)
                return rows;

            int materialRows = 1;
            int etypeRows = 0;
            int connectivityStart = header + 1 + materialRows + nn + etypeRows;
            if (connectivityStart + nels > lines.Count)
            {
                // Multi-material decks have one etype row between material and coordinates.
                etypeRows = 1;
                connectivityStart = header + 1 + materialRows + etypeRows + nn;
            }

            for (int i = connectivityStart; i < lines.Count && rows.Count < count; i++)
            {
                var ints = SplitInts(lines[i]);
                if (ints.Length >= 3)
                    rows.Add(ints.Take(Math.Min(8, ints.Length)).ToArray());
            }
        }
        catch
        {
            // Best effort summary only.
        }
        return rows;
    }

    private static int[] SplitInts(string line)
        => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => int.TryParse(p, out var v) ? v : (int?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToArray();

    public static object SummarizeResult(P610T3RunResult? result)
    {
        if (result == null)
            return new { hasResult = false };

        var lastStage = result.Stages.LastOrDefault();
        var disp = lastStage?.DisplacementsByNodeIndex;
        int nonFinite = 0;
        double maxDisp = 0.0;
        if (disp != null)
        {
            foreach (var (ux, uy) in disp)
            {
                if (!double.IsFinite(ux) || !double.IsFinite(uy))
                {
                    nonFinite++;
                    continue;
                }
                maxDisp = Math.Max(maxDisp, Math.Sqrt(ux * ux + uy * uy));
            }
        }

        return new
        {
            hasResult = true,
            nodeCount = result.Nodes.Count,
            elementCount = result.Elements.Count,
            stageCount = result.Stages.Count,
            nodeBounds = Bounds(result.Nodes),
            elementTypes = result.Elements
                .GroupBy(e => e.GetType().Name)
                .ToDictionary(g => g.Key, g => g.Count()),
            firstElements = result.Elements.Take(5).Select(e => new
            {
                e.Id,
                type = e.GetType().Name,
                e.NodeIds
            }).ToList(),
            lastStage = lastStage == null ? null : new
            {
                lastStage.Index,
                lastStage.Label,
                displacementCount = disp?.Count ?? 0,
                nonFinite,
                maxDisp
            }
        };
    }

    public static object SummarizeVisualization(
        IReadOnlyList<Node> nodes,
        IReadOnlyList<ElementBase> elements,
        string visualizationMode,
        double exaggeration)
        => new
        {
            visualizationMode,
            exaggeration,
            nodeCount = nodes.Count,
            elementCount = elements.Count,
            nodeBounds = Bounds(nodes),
            elementTypes = elements.GroupBy(e => e.GetType().Name).ToDictionary(g => g.Key, g => g.Count()),
            firstElements = elements.Take(5).Select(e => new { e.Id, type = e.GetType().Name, e.NodeIds }).ToList()
        };

    private static object Bounds(IReadOnlyList<Node> nodes)
    {
        if (nodes.Count == 0)
            return new { empty = true };
        return new
        {
            minX = nodes.Min(n => n.X),
            maxX = nodes.Max(n => n.X),
            minY = nodes.Min(n => n.Y),
            maxY = nodes.Max(n => n.Y)
        };
    }
}
