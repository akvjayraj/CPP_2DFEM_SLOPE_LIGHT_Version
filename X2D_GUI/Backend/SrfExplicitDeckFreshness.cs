using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Detects stale SRF .dat decks (corner-only BC export, bad solver tol/limit) before
/// <see cref="ScriptViewModel.ShouldPreserveLoadedExplicitDeck"/> reuses a loaded script.
/// </summary>
public static class SrfExplicitDeckFreshness
{
    public const double DefaultSrfTolerance = 1.0e-5;
    public const int DefaultSrfIterationLimit = 1000;

    public static bool HasStaleSolverTail(double tolerance, int iterationLimit) =>
        tolerance >= 1.0 || iterationLimit < DefaultSrfIterationLimit;

    public static string ComputeFixitySignature(IReadOnlyDictionary<int, (int FixX, int FixY)> map)
    {
        if (map.Count == 0)
            return string.Empty;

        return string.Join(";",
            map.OrderBy(kv => kv.Key)
                .Select(kv => FormattableString.Invariant($"{kv.Key}:{kv.Value.FixX},{kv.Value.FixY}")));
    }

    public static Dictionary<int, (int FixX, int FixY)> BuildExpectedFixMap(
        BoundaryConditionsViewModel? bcVM,
        IReadOnlyList<MeshInformationRow> rows,
        int nn) =>
        BoundaryConditionFixityMapper.BuildFixMap(bcVM, rows, nn);

    public static bool TryParseScriptFixityMap(
        string? datContent,
        out Dictionary<int, (int FixX, int FixY)> map)
    {
        map = new Dictionary<int, (int FixX, int FixY)>();
        if (string.IsNullOrWhiteSpace(datContent))
            return false;

        if (DatMeshBoundaryImporter.TryParseBcFixMap(datContent, out map))
            return map.Count > 0;

        return TryParseFencedBcsBlock(datContent, out map);
    }

    public static bool TryParseScriptSolverTail(
        string? datContent,
        out double tolerance,
        out int iterationLimit)
    {
        tolerance = 0;
        iterationLimit = 0;
        if (string.IsNullOrWhiteSpace(datContent))
            return false;

        if (TryParseFencedSolverControl(datContent, out tolerance, out iterationLimit))
            return true;

        return TryParseP64CompactSolverTail(datContent, out tolerance, out iterationLimit);
    }

    /// <summary>
    /// Locates the compact p64 <c>tol limit</c> data line (0-based index in stripped data lines).
    /// </summary>
    public static bool TryLocateP64TolLimitDataLine(string? datContent, out int dataLineIndex)
    {
        dataLineIndex = -1;
        if (string.IsNullOrWhiteSpace(datContent))
            return false;

        if (!DatMeshBoundaryImporter.TryParseBcFixMap(datContent, out var fixMap) || fixMap.Count == 0)
            return false;

        var dataLines = datContent
            .Split('\n')
            .Select(line =>
            {
                var bang = line.IndexOf('!');
                return bang >= 0 ? line[..bang].Trim() : line.Trim();
            })
            .Where(line => line.Length > 0)
            .ToList();

        for (int i = 0; i < dataLines.Count; i++)
        {
            if (!int.TryParse(dataLines[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nr) || nr != fixMap.Count)
                continue;

            int tailIdx = i + 1 + nr;
            if (tailIdx >= dataLines.Count)
                continue;

            var tail = dataLines[tailIdx].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tail.Length >= 2
                && double.TryParse(tail[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                && int.TryParse(tail[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                dataLineIndex = tailIdx;
                return true;
            }
        }

        return false;
    }

    public static bool IsFixityFresh(
        string? datContent,
        BoundaryConditionsViewModel? bcVM,
        IReadOnlyList<MeshInformationRow> rows,
        int nn)
    {
        var expected = BuildExpectedFixMap(bcVM, rows, nn);
        if (expected.Count == 0)
            return false;

        if (!TryParseScriptFixityMap(datContent, out var script))
            return false;

        return string.Equals(
            ComputeFixitySignature(expected),
            ComputeFixitySignature(script),
            StringComparison.Ordinal);
    }

    public static bool IsSolverTailFresh(string? datContent, FEAnalysisViewModel? feVM)
    {
        if (!TryParseScriptSolverTail(datContent, out var tol, out var limit))
            return false;

        if (HasStaleSolverTail(tol, limit))
            return false;

        ResolveExpectedSolverTail(feVM, out var expectedTol, out var expectedLimit);
        return Math.Abs(tol - expectedTol) <= Math.Max(1e-12, expectedTol * 0.01)
               && limit == expectedLimit;
    }

    public static bool IsDeckFreshForPreserve(
        string? datContent,
        BoundaryConditionsViewModel? bcVM,
        IReadOnlyList<MeshInformationRow> rows,
        int nn,
        FEAnalysisViewModel? feVM) =>
        IsFixityFresh(datContent, bcVM, rows, nn)
        && IsSolverTailFresh(datContent, feVM);

    public static void EnsureSaneSrfSolverSettings(FEAnalysisViewModel feVM)
    {
        if (feVM == null)
            return;

        if (HasStaleSolverTail(feVM.ConvergenceTolerance, feVM.MaxIterations))
        {
            feVM.ConvergenceTolerance = DefaultSrfTolerance;
            feVM.MaxIterations = DefaultSrfIterationLimit;
        }
    }

    private static void ResolveExpectedSolverTail(
        FEAnalysisViewModel? feVM,
        out double tolerance,
        out int iterationLimit)
    {
        tolerance = feVM?.ConvergenceTolerance ?? DefaultSrfTolerance;
        iterationLimit = feVM?.MaxIterations ?? DefaultSrfIterationLimit;
        if (HasStaleSolverTail(tolerance, iterationLimit))
        {
            tolerance = DefaultSrfTolerance;
            iterationLimit = DefaultSrfIterationLimit;
        }
    }

    private static bool TryParseFencedBcsBlock(
        string datContent,
        out Dictionary<int, (int FixX, int FixY)> map)
    {
        map = new Dictionary<int, (int FixX, int FixY)>();
        var match = Regex.Match(
            datContent,
            @"! <BEGIN:Bcs>\s*(?:!.*?\r?\n)*\s*(\d+)\s*\r?\n((?:\s*\d+\s+\d+\s+\d+\s*\r?\n)+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int nr)
            || nr < 1)
            return false;

        foreach (var raw in match.Groups[2].Value.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int node)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fx)
                || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fy))
                continue;

            map[node] = (fx, fy);
        }

        return map.Count > 0 && map.Count == nr;
    }

    private static bool TryParseFencedSolverControl(
        string datContent,
        out double tolerance,
        out int iterationLimit)
    {
        tolerance = 0;
        iterationLimit = 0;
        var match = Regex.Match(
            datContent,
            @"! <BEGIN:SolverControl>\s*(?:!.*?\r?\n)*\s*([+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)\s+(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        return double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out tolerance)
               && int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out iterationLimit);
    }

    private static bool TryParseP64CompactSolverTail(
        string datContent,
        out double tolerance,
        out int iterationLimit)
    {
        tolerance = 0;
        iterationLimit = 0;
        if (!DatMeshBoundaryImporter.TryParseBcFixMap(datContent, out var fixMap) || fixMap.Count == 0)
            return false;

        var dataLines = datContent
            .Split('\n')
            .Select(line =>
            {
                var bang = line.IndexOf('!');
                return bang >= 0 ? line[..bang].Trim() : line.Trim();
            })
            .Where(line => line.Length > 0)
            .ToList();

        for (int i = 0; i < dataLines.Count; i++)
        {
            if (!int.TryParse(dataLines[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nr) || nr != fixMap.Count)
                continue;

            int tailIdx = i + 1 + nr;
            if (tailIdx >= dataLines.Count)
                continue;

            var tail = dataLines[tailIdx].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tail.Length >= 2
                && double.TryParse(tail[0], NumberStyles.Float, CultureInfo.InvariantCulture, out tolerance)
                && int.TryParse(tail[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out iterationLimit))
                return true;
        }

        return false;
    }
}
