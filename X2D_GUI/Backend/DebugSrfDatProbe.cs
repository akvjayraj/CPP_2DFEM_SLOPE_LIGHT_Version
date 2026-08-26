using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GeotechSlopeApp.Backend;

internal static class DebugSrfDatProbe
{
    public static object Probe(string datContent, string baseName)
    {
        TryReadSolverControl(datContent, out double tol, out int limit, out int incs, out int layers);
        return new
        {
            baseName,
            layers,
            incs,
            limit,
            tol,
            layerBlockCount = Regex.Matches(datContent, @"! <BEGIN:Layers>", RegexOptions.IgnoreCase).Count,
            hasGravityOnlyLayer = datContent.Contains("gravity equilibration", StringComparison.OrdinalIgnoreCase),
            srfF = P610AnalysisOptionsHelper.TryParse(datContent, out var opt) ? opt.SrfF : null,
        };
    }

    public static object ProbeResultFiles(string workDir, string baseName, P610T3RunResult result)
    {
        var resPath = Path.Combine(workDir, baseName + ".res");
        var jsonPath = Path.Combine(workDir, baseName + ".json");
        string resText = File.Exists(resPath) ? File.ReadAllText(resPath) : "";
        string jsonRaw = File.Exists(jsonPath) ? File.ReadAllText(jsonPath) : "";

        return new
        {
            baseName,
            resPath,
            jsonPath,
            resHasInfiniteMedium = resText.Contains("Infinite-medium traction", StringComparison.OrdinalIgnoreCase),
            resHasGravityEquilibration = resText.Contains("Gravity equilibration", StringComparison.OrdinalIgnoreCase),
            resNanTokenCount = CountNanTokens(resText),
            jsonNanTokenCount = CountNanTokens(jsonRaw),
            metaMaxDisp = result.Meta?.SrfMaxDisplacement,
            metaMaxIters = result.Meta?.SrfMaxItersUsed,
            stageCount = result.Stages.Count,
            stageMaxU = result.Stages.Select((st, i) => new
            {
                index = i,
                label = st.Label,
                maxU = MaxDisp(st.DisplacementsByNodeIndex),
                nonFiniteCount = CountNonFinite(st.DisplacementsByNodeIndex),
            }).ToList(),
            topLevelMaxU = MaxDisp(result.Nodes.Select(n =>
            {
                double u = n.Displacements.Length > 0 ? n.Displacements[0] : 0;
                double v = n.Displacements.Length > 1 ? n.Displacements[1] : 0;
                return (u, v);
            }).ToList()),
        };
    }

    private static void TryReadSolverControl(
        string datContent, out double tol, out int limit, out int incs, out int layers)
    {
        tol = 0; limit = 0; incs = 0; layers = -1;
        foreach (var raw in datContent.Split('\n'))
        {
            var line = raw;
            var bang = line.IndexOf('!');
            if (bang >= 0) line = line[..bang];
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 4) continue;
            if (!double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out tol)) continue;
            if (!int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out limit)) continue;
            if (!int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out incs)) continue;
            if (!int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out layers)) continue;
            return;
        }
    }

    private static int CountNanTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return Regex.Matches(text, @"(?i)(?:^|[\s,\[\{:])(?:\+|-)?nan(?:\([a-z0-9_]+\))?(?:[\s,\]\}:]|$)").Count
               + Regex.Matches(text, @"(?i)-?\d+\.#(?:QNAN|IND|INF)\d*").Count;
    }

    private static double MaxDisp(IReadOnlyList<(double ux, double uy)>? disp)
    {
        if (disp == null || disp.Count == 0) return 0;
        double umax = 0;
        foreach (var (ux, uy) in disp)
        {
            if (!double.IsFinite(ux) || !double.IsFinite(uy)) continue;
            umax = Math.Max(umax, Math.Sqrt(ux * ux + uy * uy));
        }
        return umax;
    }

    private static int CountNonFinite(IReadOnlyList<(double ux, double uy)>? disp)
    {
        if (disp == null) return 0;
        return disp.Count(t => !double.IsFinite(t.ux) || !double.IsFinite(t.uy));
    }
}
