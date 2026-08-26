using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Parses Program 6.4 (p64) <c>.res</c> output: equation count and SRF trial summary table.
/// </summary>
public static class P64ResFileParser
{
    public sealed class P64ResSummary
    {
        public int? EquationCount { get; init; }
        public int? SkylineStorage { get; init; }
        public IReadOnlyList<SrfTrialRow> Trials { get; init; } = Array.Empty<SrfTrialRow>();
        public string RawText { get; init; } = "";
    }

    public sealed class SrfTrialRow
    {
        public double Srf { get; init; }
        public double MaxDisplacement { get; init; }
        public int Iterations { get; init; }
    }

    private static readonly Regex EquationLine = new(
        @"There are\s+(\d+)\s+equations and the skyline storage is\s+(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static P64ResSummary ParseFile(string resPath)
    {
        var lines = File.ReadAllLines(resPath);
        return ParseLines(lines);
    }

    public static P64ResSummary ParseLines(IEnumerable<string> lines)
    {
        int? neq = null, kv = null;
        var trials = new List<SrfTrialRow>();
        var raw = new System.Text.StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            raw.AppendLine(line);

            var mEq = EquationLine.Match(line);
            if (mEq.Success)
            {
                neq = int.Parse(mEq.Groups[1].Value, CultureInfo.InvariantCulture);
                kv = int.Parse(mEq.Groups[2].Value, CultureInfo.InvariantCulture);
                continue;
            }

            if (line.Length == 0 || line.StartsWith("srf", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var srf))
                continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var maxDisp))
                continue;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var iters))
                continue;

            trials.Add(new SrfTrialRow { Srf = srf, MaxDisplacement = maxDisp, Iterations = iters });
        }

        return new P64ResSummary
        {
            EquationCount = neq,
            SkylineStorage = kv,
            Trials = trials,
            RawText = raw.ToString()
        };
    }

    public static string FormatSummaryText(P64ResSummary summary, string workDir, string baseName)
    {
        var sb = new System.Text.StringBuilder();
        if (summary.EquationCount.HasValue)
            sb.AppendLine($"Equations: {summary.EquationCount}, skyline: {summary.SkylineStorage}");
        sb.AppendLine();
        sb.AppendLine("SRF trials (from .res):");
        sb.AppendLine("  SRF      max|u|      iters");
        foreach (var t in summary.Trials)
            sb.AppendLine($"  {t.Srf,5:F2}  {t.MaxDisplacement,11:E4}  {t.Iterations,5}");

        var artifacts = new[]
        {
            Path.Combine(workDir, baseName + ".res"),
            Path.Combine(workDir, baseName + ".json"),
            Path.Combine(workDir, baseName + ".msh"),
            Path.Combine(workDir, baseName + ".dis"),
            Path.Combine(workDir, baseName + ".vec"),
        };
        sb.AppendLine();
        sb.AppendLine("Output files:");
        foreach (var p in artifacts)
        {
            var label = Path.GetExtension(p).ToLowerInvariant() switch
            {
                ".msh" => " (PostScript mesh)",
                ".dis" => " (PostScript deformed mesh)",
                ".vec" => " (PostScript vectors)",
                _ => ""
            };
            sb.AppendLine(File.Exists(p) ? $"  {p}{label}" : $"  {p} (not found)");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Bracket FoS from multi-SRF trials: last converged (iters &lt; limit) and first failed trial.
    /// Returns midpoint SRF; also supports displacement-trend interpolation when bracket exists.
    /// </summary>
    public static bool TryEstimateFoS(
        IReadOnlyList<SrfTrialRow> trials,
        int iterationLimit,
        out double estimatedFoS,
        out string bracketNote)
    {
        estimatedFoS = 0;
        bracketNote = "";
        if (trials.Count == 0 || iterationLimit <= 0)
            return false;

        var ordered = trials.OrderBy(t => t.Srf).ToList();
        var converged = ordered.Where(t => t.Iterations < iterationLimit).ToList();
        var failed = ordered.Where(t => t.Iterations >= iterationLimit).ToList();
        if (converged.Count == 0 || failed.Count == 0)
        {
            bracketNote = converged.Count == 0
                ? "All trials hit the iteration limit."
                : "All trials converged within the iteration limit.";
            return false;
        }

        var lastStable = converged[^1];
        var firstFailed = failed[0];
        var midpoint = 0.5 * (lastStable.Srf + firstFailed.Srf);

        double fos = midpoint;
        if (converged.Count >= 2)
        {
            var prev = converged[^2];
            double dSrf = lastStable.Srf - prev.Srf;
            double dDisp = lastStable.MaxDisplacement - prev.MaxDisplacement;
            if (System.Math.Abs(dSrf) > 1e-12 && System.Math.Abs(dDisp) > 1e-14)
            {
                double targetDisp = 0.5 * (lastStable.MaxDisplacement + firstFailed.MaxDisplacement);
                double slope = dDisp / dSrf;
                fos = lastStable.Srf + (targetDisp - lastStable.MaxDisplacement) / slope;
                fos = System.Math.Clamp(fos, lastStable.Srf, firstFailed.Srf);
            }
        }

        estimatedFoS = fos;
        bracketNote =
            $"Last converged SRF={lastStable.Srf:F4} (|u|={lastStable.MaxDisplacement:E3}, iters={lastStable.Iterations}); " +
            $"first failed SRF={firstFailed.Srf:F4} (|u|={firstFailed.MaxDisplacement:E3}, iters={firstFailed.Iterations}).";
        return true;
    }

    /// <summary>Last SRF trial that converged within the iteration limit (RS2D "last stable" stage).</summary>
    public static bool TryGetLastConvergedSrf(
        IReadOnlyList<SrfTrialRow> trials,
        int iterationLimit,
        out double lastConvergedSrf)
    {
        lastConvergedSrf = 0;
        if (trials.Count == 0 || iterationLimit <= 0)
            return false;

        var converged = trials
            .Where(t => t.Iterations < iterationLimit && double.IsFinite(t.MaxDisplacement))
            .OrderBy(t => t.Srf)
            .ToList();
        if (converged.Count == 0)
            return false;

        lastConvergedSrf = converged[^1].Srf;
        return true;
    }

    /// <summary>First SRF trial that hit the iteration limit or produced non-finite displacement.</summary>
    public static bool TryGetFirstFailedSrf(
        IReadOnlyList<SrfTrialRow> trials,
        int iterationLimit,
        out double firstFailedSrf)
    {
        firstFailedSrf = 0;
        if (trials.Count == 0 || iterationLimit <= 0)
            return false;

        var failed = trials
            .Where(t => t.Iterations >= iterationLimit || !double.IsFinite(t.MaxDisplacement))
            .OrderBy(t => t.Srf)
            .FirstOrDefault();
        if (failed == null)
            return false;

        firstFailedSrf = failed.Srf;
        return true;
    }

    /// <summary>Re-parse SRF trial rows persisted in <see cref="FormatSummaryText"/> artifact output.</summary>
    public static bool TryParseTrialsFromArtifactSummary(string? text, out IReadOnlyList<SrfTrialRow> trials)
    {
        trials = Array.Empty<SrfTrialRow>();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var rows = new List<SrfTrialRow>();
        bool inSection = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Contains("SRF trials", StringComparison.OrdinalIgnoreCase))
            {
                inSection = true;
                continue;
            }

            if (!inSection)
                continue;

            if (line.StartsWith("Output files", StringComparison.OrdinalIgnoreCase))
                break;

            if (line.Contains("SRF", StringComparison.OrdinalIgnoreCase)
                && line.Contains("max|u|", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                continue;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var srf))
                continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var maxDisp))
                continue;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var iters))
                continue;

            rows.Add(new SrfTrialRow { Srf = srf, MaxDisplacement = maxDisp, Iterations = iters });
        }

        if (rows.Count == 0)
            return false;

        trials = rows;
        return true;
    }

    /// <summary>Parse iteration limit from p64 explicit/parametric tail (<c>tol limit</c>).</summary>
    public static int TryParseIterationLimitFromDat(string? datContent, int defaultLimit = 1000)
    {
        if (string.IsNullOrWhiteSpace(datContent))
            return defaultLimit;

        var dataLines = datContent.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('!'))
            .ToList();

        for (int i = dataLines.Count - 1; i >= 1; --i)
        {
            var parts = dataLines[i].Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1
                && parts.All(p => double.TryParse(p, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _)))
            {
                var prev = dataLines[i - 1].Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries);
                if (prev.Length >= 2
                    && int.TryParse(prev[1], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int limit)
                    && limit > 0)
                    return limit;
            }
        }

        return defaultLimit;
    }

    private static readonly Regex ResDispLine = new(
        @"^\s*(\d+)\s+([-+]?(?:\d+\.\d*|\d*\.?\d+)(?:[eE][-+]?\d+)?|[-+]?(?:nan(?:\([a-z0-9_]+\))?|inf(?:inity)?))\s+([-+]?(?:\d+\.\d*|\d*\.?\d+)(?:[eE][-+]?\d+)?|[-+]?(?:nan(?:\([a-z0-9_]+\))?|inf(?:inity)?))\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Scan a p610 T3 .res file for nodal displacements and detect NaN/Inf output.
    /// JSON export replaces non-finite values with 0, so this is the reliable health check.
    /// </summary>
    public static bool TryScanT3ResDisplacements(
        string resPath,
        out double maxAbsDisplacement,
        out bool hasNonFinite,
        out int nonFiniteLineCount)
    {
        maxAbsDisplacement = 0.0;
        hasNonFinite = false;
        nonFiniteLineCount = 0;
        if (string.IsNullOrWhiteSpace(resPath) || !File.Exists(resPath))
            return false;

        foreach (var rawLine in File.ReadLines(resPath))
        {
            var m = ResDispLine.Match(rawLine);
            if (!m.Success) continue;

            bool lineBad = false;
            foreach (var grp in new[] { m.Groups[2], m.Groups[3] })
            {
                if (IsNonFiniteToken(grp.Value))
                {
                    hasNonFinite = true;
                    nonFiniteLineCount++;
                    lineBad = true;
                    break;
                }
            }

            if (lineBad) continue;
            if (double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ux)
                && double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var uy))
                maxAbsDisplacement = Math.Max(maxAbsDisplacement, Math.Sqrt(ux * ux + uy * uy));
        }

        return true;
    }

    private static bool IsNonFiniteToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var t = token.ToLowerInvariant();
        return t.Contains("nan") || t.Contains("inf");
    }
}
