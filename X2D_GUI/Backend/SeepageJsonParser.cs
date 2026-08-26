using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GeotechSlopeApp.Backend;

/// <summary>One node of a seepage run: coordinates, heads, and net nodal flow rate.</summary>
public sealed class SeepageNodeResult
{
    public int Id { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double TotalHead { get; init; }
    public double PressureHead { get; init; }
    public double FlowRate { get; init; }
}

/// <summary>Parsed {base}.json written by the C++ seepage programs (p72 / p73 / p74).</summary>
public sealed class SeepageRunResult
{
    public string Solver { get; init; } = "";
    public int NodeCount { get; init; }
    public int ElementCount { get; init; }
    public int ElementNodes { get; init; }
    public int Iterations { get; init; }
    public bool Converged { get; init; } = true;
    public double Inflow { get; init; }
    public double Outflow { get; init; }
    public IReadOnlyList<SeepageNodeResult> Nodes { get; init; } = Array.Empty<SeepageNodeResult>();
    /// <summary>Element connectivity, 1-based node ids per element.</summary>
    public IReadOnlyList<int[]> Elements { get; init; } = Array.Empty<int[]>();
}

/// <summary>Parses the JSON output of the seepage solver programs.</summary>
public static class SeepageJsonParser
{
    public static SeepageRunResult ParseFile(string jsonPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = doc.RootElement;

        var nodes = new List<SeepageNodeResult>();
        if (root.TryGetProperty("nodes", out var nodesEl) && nodesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var n in nodesEl.EnumerateArray())
            {
                nodes.Add(new SeepageNodeResult
                {
                    Id = GetInt(n, "id"),
                    X = GetDouble(n, "x"),
                    Y = GetDouble(n, "y"),
                    TotalHead = GetDouble(n, "total_head"),
                    PressureHead = GetDouble(n, "pressure_head"),
                    FlowRate = GetDouble(n, "flow_rate"),
                });
            }
        }

        var elements = new List<int[]>();
        if (root.TryGetProperty("elements", out var elsEl) && elsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in elsEl.EnumerateArray())
            {
                if (!e.TryGetProperty("nodes", out var connEl) || connEl.ValueKind != JsonValueKind.Array)
                    continue;
                var conn = new List<int>();
                foreach (var v in connEl.EnumerateArray())
                    conn.Add(v.GetInt32());
                elements.Add(conn.ToArray());
            }
        }

        return new SeepageRunResult
        {
            Solver = root.TryGetProperty("solver", out var s) ? s.GetString() ?? "" : "",
            NodeCount = GetInt(root, "node_count"),
            ElementCount = GetInt(root, "element_count"),
            ElementNodes = GetInt(root, "element_nodes"),
            Iterations = GetInt(root, "iterations"),
            Converged = !root.TryGetProperty("converged", out var c) || c.ValueKind != JsonValueKind.False,
            Inflow = GetDouble(root, "inflow"),
            Outflow = GetDouble(root, "outflow"),
            Nodes = nodes,
            Elements = elements,
        };
    }

    private static int GetInt(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static double GetDouble(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number)
            return 0.0;
        var d = v.GetDouble();
        return double.IsFinite(d) ? d : 0.0;
    }
}
