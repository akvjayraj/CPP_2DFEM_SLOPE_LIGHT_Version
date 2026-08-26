using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GeotechSlopeApp.Backend;

/// <summary>One monitor-node sample from a transient consolidation run.</summary>
public sealed class TransientMonitorSample
{
    public int Step { get; init; }
    public double Time { get; init; }
    public double Pressure { get; init; }
}

/// <summary>Spatial pressure field at one output time step.</summary>
public sealed class TransientTimeStepFrame
{
    public int Step { get; init; }
    public double Time { get; init; }
    /// <summary>Pressures for nodes 1..N (index 0 = node 1).</summary>
    public IReadOnlyList<double> Pressures { get; init; } = Array.Empty<double>();
}

/// <summary>Parsed multi-frame JSON from p84 / p88.</summary>
public sealed class TransientRunResult
{
    public string Solver { get; init; } = "";
    public string Analysis { get; init; } = "";
    public int NodeCount { get; init; }
    public int ElementCount { get; init; }
    public int ElementNodes { get; init; }
    public int Nstep { get; init; }
    public double Dtim { get; init; }
    public double Theta { get; init; }
    public int Npri { get; init; }
    public int Nres { get; init; }
    public int Ntime { get; init; }
    public int MonitorNode { get; init; }
    public IReadOnlyList<SeepageNodeResult> Nodes { get; init; } = Array.Empty<SeepageNodeResult>();
    public IReadOnlyList<int[]> Elements { get; init; } = Array.Empty<int[]>();
    public IReadOnlyList<TransientMonitorSample> MonitorHistory { get; init; } = Array.Empty<TransientMonitorSample>();
    public IReadOnlyList<TransientTimeStepFrame> Steps { get; init; } = Array.Empty<TransientTimeStepFrame>();

    public bool IsTransient =>
        string.Equals(Analysis, "transient_consolidation", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Solver, "p84", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Solver, "p88", StringComparison.OrdinalIgnoreCase);

    /// <summary>Final-frame snapshot compatible with the existing seepage loader.</summary>
    public SeepageRunResult AsSeepageSnapshot()
    {
        IReadOnlyList<SeepageNodeResult> nodes = Nodes;
        if (Steps.Count > 0 && Nodes.Count > 0)
        {
            var last = Steps[^1];
            nodes = Nodes
                .OrderBy(n => n.Id)
                .Select(n =>
                {
                    double p = n.PressureHead;
                    int idx = n.Id - 1;
                    if (idx >= 0 && idx < last.Pressures.Count)
                        p = last.Pressures[idx];
                    return new SeepageNodeResult
                    {
                        Id = n.Id,
                        X = n.X,
                        Y = n.Y,
                        TotalHead = p,
                        PressureHead = p,
                        FlowRate = 0,
                    };
                })
                .ToList();
        }

        return new SeepageRunResult
        {
            Solver = Solver,
            NodeCount = NodeCount > 0 ? NodeCount : nodes.Count,
            ElementCount = ElementCount > 0 ? ElementCount : Elements.Count,
            ElementNodes = ElementNodes,
            Inflow = 0,
            Outflow = 0,
            Nodes = nodes,
            Elements = Elements,
        };
    }
}

/// <summary>Parses p84/p88 multi-frame transient consolidation JSON.</summary>
public static class TransientJsonParser
{
    public static bool LooksLikeTransient(string jsonPath)
    {
        if (!File.Exists(jsonPath)) return false;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var root = doc.RootElement;
            if (root.TryGetProperty("analysis", out var a) &&
                string.Equals(a.GetString(), "transient_consolidation", StringComparison.OrdinalIgnoreCase))
                return true;
            if (root.TryGetProperty("solver", out var s))
            {
                var solver = s.GetString() ?? "";
                return string.Equals(solver, "p84", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(solver, "p88", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public static TransientRunResult ParseFile(string jsonPath)
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

        var history = new List<TransientMonitorSample>();
        if (root.TryGetProperty("monitor_history", out var histEl) && histEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var sampleEl in histEl.EnumerateArray())
            {
                history.Add(new TransientMonitorSample
                {
                    Step = GetInt(sampleEl, "step"),
                    Time = GetDouble(sampleEl, "time"),
                    Pressure = GetDouble(sampleEl, "pressure"),
                });
            }
        }

        var steps = new List<TransientTimeStepFrame>();
        if (root.TryGetProperty("steps", out var stepsEl) && stepsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in stepsEl.EnumerateArray())
            {
                var pressures = new List<double>();
                if (f.TryGetProperty("pressures", out var pEl) && pEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in pEl.EnumerateArray())
                        pressures.Add(v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0.0);
                }
                else if (f.TryGetProperty("nodes", out var frameNodes) && frameNodes.ValueKind == JsonValueKind.Array)
                {
                    // Alternate verbose schema: nodes[{id,pressure}]
                    var byId = new SortedDictionary<int, double>();
                    foreach (var n in frameNodes.EnumerateArray())
                        byId[GetInt(n, "id")] = GetDouble(n, "pressure");
                    if (byId.Count > 0)
                    {
                        int maxId = byId.Keys.Max();
                        pressures = Enumerable.Repeat(0.0, maxId).ToList();
                        foreach (var kv in byId)
                            if (kv.Key >= 1 && kv.Key <= maxId)
                                pressures[kv.Key - 1] = kv.Value;
                    }
                }

                steps.Add(new TransientTimeStepFrame
                {
                    Step = GetInt(f, "step"),
                    Time = GetDouble(f, "time"),
                    Pressures = pressures,
                });
            }
        }

        // Legacy single-frame JSON: synthesize one step from nodal snapshot.
        if (steps.Count == 0 && nodes.Count > 0)
        {
            var ordered = nodes.OrderBy(n => n.Id).ToList();
            steps.Add(new TransientTimeStepFrame
            {
                Step = GetInt(root, "nstep"),
                Time = GetDouble(root, "dtim") * GetInt(root, "nstep"),
                Pressures = ordered.Select(n => n.PressureHead).ToList(),
            });
        }

        int nres = GetInt(root, "nres");
        if (nres <= 0) nres = GetInt(root, "monitor_node");

        return new TransientRunResult
        {
            Solver = root.TryGetProperty("solver", out var s) ? s.GetString() ?? "" : "",
            Analysis = root.TryGetProperty("analysis", out var a) ? a.GetString() ?? "" : "",
            NodeCount = GetInt(root, "node_count"),
            ElementCount = GetInt(root, "element_count"),
            ElementNodes = GetInt(root, "element_nodes"),
            Nstep = GetInt(root, "nstep"),
            Dtim = GetDouble(root, "dtim"),
            Theta = GetDouble(root, "theta"),
            Npri = GetInt(root, "npri"),
            Nres = nres,
            Ntime = GetInt(root, "ntime"),
            MonitorNode = nres,
            Nodes = nodes,
            Elements = elements,
            MonitorHistory = history,
            Steps = steps,
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
