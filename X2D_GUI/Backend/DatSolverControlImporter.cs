using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Imports solver control (tol, limit, incs, layers for p610; tol, limit, nsrf, F list for p64 SRF)
/// from Script Editor .dat into <see cref="FEAnalysisViewModel"/>.
/// </summary>
public static class DatSolverControlImporter
{
    public static bool TryImportToFeAnalysis(
        string datContent,
        FEAnalysisViewModel feVm,
        bool srfAnalysisMode)
    {
        if (string.IsNullOrWhiteSpace(datContent) || feVm == null)
            return false;

        bool imported = false;

        if (DatSolverControlExporter.HasSolverControlBlock(datContent))
        {
            if (TryImportSolverControlSentinel(datContent, feVm))
                imported = true;
            else if (P610T3DatImporter.TryImportSolverControlOnly(datContent, feVm))
                imported = true;
        }

        if (!srfAnalysisMode)
            return imported;

        P610AnalysisOptionsHelper.TryParse(datContent, out var options);

        if (TryImportSrfBracketSettings(options, feVm))
            imported = true;

        if (TryImportSrfInsituMode(options, feVm))
            imported = true;

        var isP64Deck = string.Equals(
                FemCppProgramRouter.ResolveProgramId(datContent), "p64", StringComparison.OrdinalIgnoreCase)
            || P610AnalysisOptionsHelper.IsP64Q8Deck(options);

        // p610 T3 SRF decks encode nsrf / F trials on comment lines only — never use p64 tail heuristics.
        if (!isP64Deck && DatSolverControlExporter.UsesP610SolverControlFormat(datContent))
        {
            if (TryApplySrfTrialComments(datContent, feVm))
                imported = true;
            return imported;
        }

        if (TryImportP64CompactTail(datContent, feVm))
            return true;

        if (isP64Deck && LooksLikeExplicitMeshFile(datContent)
            && TryImportP64TailFromEnd(datContent, feVm, options))
            return true;

        if (TryImportP64ExplicitTail(datContent, feVm, options))
            return true;

        if (isP64Deck && TryImportP64TailFromEnd(datContent, feVm, options))
            return true;

        if (TryImportSrfTrialsFromAnalysisOptions(feVm, options))
            imported = true;

        if (TryApplySrfTrialComments(datContent, feVm))
            imported = true;

        return imported;
    }

    /// <summary>Restores SRF search mode + bracket settings (seed, expansion factor, F_min/F_max, tolerance, …) from the AnalysisOptions block.</summary>
    private static bool TryImportSrfBracketSettings(P610AnalysisOptions? options, FEAnalysisViewModel feVm)
    {
        if (options == null || string.IsNullOrWhiteSpace(options.SrfSearchMode))
            return false;

        bool isBracket = options.SrfSearchMode.Equals("bracket", StringComparison.OrdinalIgnoreCase);
        feVm.SelectedSrfSearchMode = isBracket ? SrfSearchMode.Bracket : SrfSearchMode.Sweep;
        if (!isBracket)
            return true;

        if (options.SrfBracketAuto.HasValue)
            feVm.AutoDetectSrfBracket = options.SrfBracketAuto.Value;
        if (options.SrfBracketSeed is { } seed && seed > 0)
            feVm.SrfBracketSeed = seed;
        if (options.SrfBracketExpansionFactor is { } exp && exp > 1.0)
            feVm.SrfBracketExpansionFactor = exp;
        if (options.SrfMaxExpansionSteps is { } maxExp && maxExp >= 1)
            feVm.SrfMaxExpansionSteps = maxExp;
        if (options.SrfFMin is { } fMin && fMin > 0)
            feVm.SrfFoSMin = fMin;
        if (options.SrfFMax is { } fMax && fMax > 0)
            feVm.SrfFoSMax = fMax;
        if (options.SrfBracketTol is { } tol && tol > 0)
            feVm.SrfBracketTolerance = tol;
        if (options.SrfMaxBracketIters is { } maxIters && maxIters >= 1)
            feVm.SrfMaxBracketIterations = maxIters;

        if (options.SrfFMin is { } previewMin && options.SrfFMax is { } previewMax && previewMin > 0 && previewMax > previewMin)
            feVm.SetDetectedSrfBracketPreview(previewMin, previewMax);

        return true;
    }

    private static bool TryImportSrfInsituMode(P610AnalysisOptions? options, FEAnalysisViewModel feVm)
    {
        if (options == null || string.IsNullOrWhiteSpace(options.SrfInsituMode))
            return false;

        var tok = options.SrfInsituMode.Trim();
        if (tok.Equals("k0", StringComparison.OrdinalIgnoreCase)
            || tok.Equals("keep_k0", StringComparison.OrdinalIgnoreCase))
            feVm.SelectedSrfInsituMode = SrfInsituMode.K0;
        else if (tok.Equals("gravity", StringComparison.OrdinalIgnoreCase))
            feVm.SelectedSrfInsituMode = SrfInsituMode.Gravity;
        else if (tok.Equals("auto", StringComparison.OrdinalIgnoreCase))
            feVm.SelectedSrfInsituMode = SrfInsituMode.Auto;
        else
            return false;

        return true;
    }

    private static bool TryApplySrfTrialComments(string datContent, FEAnalysisViewModel feVm)
    {
        if (!DatSolverControlExporter.TryParseSrfTrialComments(datContent, out var nsrf, out var fValues))
            return false;

        if (nsrf >= 1)
            feVm.SrfTrialCount = nsrf;
        if (fValues.Count > 0)
            feVm.ApplySrfSolverControlFromDat(
                feVm.ConvergenceTolerance,
                feVm.MaxIterations,
                Math.Max(1, nsrf > 0 ? nsrf : feVm.SrfTrialCount),
                fValues);
        return true;
    }

    private static bool TryImportSrfTrialsFromAnalysisOptions(
        FEAnalysisViewModel feVm,
        P610AnalysisOptions? options)
    {
        if (options?.SrfF is not { } f || !double.IsFinite(f) || f <= 0)
            return false;

        if (feVm.SrfValues.Count == 0)
            feVm.SrfTrialCount = Math.Max(1, feVm.SrfTrialCount);

        if (feVm.SrfValues.Count > 0)
            feVm.SrfValues[0].Value = f;

        return true;
    }

    /// <summary>
    /// Read p64 explicit multi-SRF tail from the last numeric lines (tol limit / nsrf / F list).
    /// Used when full token-walk alignment fails on large Q8 decks.
    /// </summary>
    private static bool TryImportP64TailFromEnd(
        string datContent,
        FEAnalysisViewModel feVm,
        P610AnalysisOptions? options)
    {
        var dataLines = CollectDataLines(datContent);
        if (dataLines.Count < 2)
            return false;

        var last = SplitLineTokens(dataLines[^1]);
        var second = SplitLineTokens(dataLines[^2]);

        // multi-SRF: ... / tol limit / nsrf / F1 F2 ...
        if (dataLines.Count >= 3
            && last.Count >= 1
            && last.All(t => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            && second.Count == 1
            && int.TryParse(second[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nsrf)
            && nsrf >= 1)
        {
            var third = SplitLineTokens(dataLines[^3]);
            if (third.Count >= 2
                && double.TryParse(third[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double tol)
                && int.TryParse(third[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int limit))
            {
                var fValues = last.Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToList();
                feVm.ApplySrfSolverControlFromDat(tol, limit, nsrf, fValues);
                return true;
            }
        }

        // single-SRF or compact-style tail at end: tol limit [nsrf] [F...]
        if (second.Count == 2
            && double.TryParse(second[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double tol2)
            && int.TryParse(second[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int limit2))
        {
            if (string.Equals(options?.SrfMode, "single", StringComparison.OrdinalIgnoreCase)
                || last.Count <= 2)
            {
                feVm.ApplySrfSolverControlFromDat(tol2, limit2, 1, null);
                return true;
            }
        }

        if (last.Count >= 2
            && double.TryParse(last[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double tol3)
            && int.TryParse(last[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int limit3))
        {
            feVm.ApplySrfSolverControlFromDat(tol3, limit3, 1, null);
            return true;
        }

        return false;
    }

    private static List<string> CollectDataLines(string datContent)
    {
        var lines = new List<string>();
        foreach (var raw in datContent.Split('\n'))
        {
            var line = raw;
            var bang = line.IndexOf('!');
            if (bang >= 0) line = line.Substring(0, bang);
            if (string.IsNullOrWhiteSpace(line)) continue;
            lines.Add(line.Trim());
        }
        return lines;
    }

    private static List<string> SplitLineTokens(string line) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static bool TryImportP64CompactTail(string datContent, FEAnalysisViewModel feVm)
    {
        if (!string.Equals(FemCppProgramRouter.ResolveProgramId(datContent), "p64", StringComparison.OrdinalIgnoreCase))
            return false;

        var sanitized = CppAnalysisRunner.StripFortranFullLineComments(datContent);
        var tokens = Tokenise(sanitized);
        if (tokens.Count == 0)
            return false;

        if (tokens.Peek().Equals("p64", StringComparison.OrdinalIgnoreCase))
            tokens.Dequeue();

        if (LooksLikeExplicitMeshHeader(tokens))
            return false;

        for (int i = 0; i < 5; i++)
            if (!TryDeqDbl(tokens, out _)) return false;
        for (int i = 0; i < 4; i++)
            if (!TryDeqInt(tokens, out _)) return false;
        if (!TryDeqInt(tokens, out int npTypes) || npTypes < 1)
            return false;

        for (int m = 0; m < npTypes; m++)
            if (!SkipMaterialLine(tokens)) return false;

        return TryReadP64SrfTail(tokens, feVm);
    }

    private static bool TryImportP64ExplicitTail(
        string datContent,
        FEAnalysisViewModel feVm,
        P610AnalysisOptions? options)
    {
        var tokens = Tokenise(datContent);
        if (!TryDeqInt(tokens, out int nels) ||
            !TryDeqInt(tokens, out int nn) ||
            !TryDeqInt(tokens, out int npTypes) ||
            nels < 1 || nn < 4 || npTypes < 1)
            return false;

        bool p64Layout = nels > 80 || nn > 200
            || string.Equals(options?.MeshType, "Q8", StringComparison.OrdinalIgnoreCase)
            || string.Equals(options?.MeshSource, "explicit", StringComparison.OrdinalIgnoreCase);
        if (!p64Layout)
            return false;

        for (int m = 0; m < npTypes; m++)
            if (!SkipMaterialLine(tokens)) return false;

        if (npTypes > 1)
        {
            for (int i = 0; i < nels; i++)
                if (!TryDeqInt(tokens, out _)) return false;
        }

        for (int i = 0; i < nn; i++)
            if (!TryDeqDbl(tokens, out _) || !TryDeqDbl(tokens, out _))
                return false;

        int connNod = string.Equals(options?.MeshType, "Q8", StringComparison.OrdinalIgnoreCase)
            || nels > 80
            || nn > 200
            ? 8
            : 4;
        for (int i = 0; i < nels * connNod; i++)
            if (!TryDeqInt(tokens, out _)) return false;

        if (!TryDeqInt(tokens, out int nr) || nr < 0)
            return false;
        for (int i = 0; i < nr * 3; i++)
            if (!TryDeqInt(tokens, out _)) return false;

        return TryReadP64SrfTail(tokens, feVm);
    }

    private static bool TryReadP64SrfTail(Queue<string> tokens, FEAnalysisViewModel feVm)
    {
        if (!TryDeqDbl(tokens, out double tol) || !TryDeqInt(tokens, out int limit))
            return false;

        if (tol <= 0.0 || limit < 1)
            return false;

        int nsrf = 0;
        if (!TryDeqInt(tokens, out nsrf) || nsrf < 1)
            return false;

        var fValues = new List<double>(nsrf);
        for (int i = 0; i < nsrf; i++)
        {
            if (!TryDeqDbl(tokens, out double f))
                break;
            fValues.Add(f);
        }

        feVm.ApplySrfSolverControlFromDat(tol, limit, nsrf, fValues);
        return true;
    }

    private static bool TryImportSolverControlSentinel(string datContent, FEAnalysisViewModel feVm)
    {
        const string begin = "<BEGIN:SolverControl>";
        const string end = "<END:SolverControl>";
        bool inBlock = false;
        var blockLines = new List<string>();

        foreach (var raw in datContent.Split('\n'))
        {
            var t = raw.Trim();
            if (t.Contains(begin, StringComparison.OrdinalIgnoreCase)) { inBlock = true; continue; }
            if (t.Contains(end, StringComparison.OrdinalIgnoreCase)) break;
            if (!inBlock) continue;
            var line = raw;
            var bang = line.IndexOf('!');
            if (bang >= 0) line = line.Substring(0, bang);
            blockLines.Add(line);
        }

        if (blockLines.Count == 0) return false;

        var tokens = Tokenise(string.Join("\n", blockLines));
        if (!TryDeqDbl(tokens, out double tol) ||
            !TryDeqInt(tokens, out int limit) ||
            !TryDeqInt(tokens, out int incs) ||
            !TryDeqInt(tokens, out int layers))
            return false;

        feVm.ConvergenceTolerance = tol;
        feVm.MaxIterations = limit;
        feVm.LoadIncrements = incs;
        feVm.LayersOverride = layers;
        return true;
    }

    private static bool SkipMaterialLine(Queue<string> tokens)
    {
        for (int i = 0; i < 6; i++)
            if (!TryDeqDbl(tokens, out _)) return false;
        TrySkipOptionalK0(tokens);
        return true;
    }

    private static bool LooksLikeExplicitMeshFile(string datContent)
    {
        var tokens = Tokenise(datContent);
        return LooksLikeExplicitMeshHeader(tokens);
    }

    private static bool LooksLikeExplicitMeshHeader(Queue<string> tokens)
    {
        if (tokens.Count < 3) return false;
        if (!int.TryParse(tokens.ElementAt(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out int nels))
            return false;
        if (!int.TryParse(tokens.ElementAt(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int nn))
            return false;
        if (!int.TryParse(tokens.ElementAt(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int npTypes))
            return false;
        return nels >= 1 && nn >= 4 && npTypes >= 1 && npTypes <= 64;
    }

    private static void TrySkipOptionalK0(Queue<string> tokens)
    {
        if (tokens.Count < 1) return;
        if (!double.TryParse(tokens.Peek(), NumberStyles.Float, CultureInfo.InvariantCulture, out double k0))
            return;
        if (k0 < 0 || k0 > 2.0) return;
        if (tokens.Count >= 2
            && double.TryParse(tokens.ElementAt(1), NumberStyles.Float, CultureInfo.InvariantCulture, out double next)
            && (next > k0 + 0.5 || Math.Abs(next) > 2.0))
            return;
        tokens.Dequeue();
    }

    private static Queue<string> Tokenise(string content)
    {
        var q = new Queue<string>();
        foreach (var raw in content.Split('\n'))
        {
            var line = raw;
            var bang = line.IndexOf('!');
            if (bang >= 0) line = line.Substring(0, bang);
            foreach (var tok in line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                q.Enqueue(tok);
        }
        return q;
    }

    private static bool TryDeqInt(Queue<string> q, out int v)
    {
        v = 0;
        return q.Count > 0 && int.TryParse(q.Dequeue(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    }

    private static bool TryDeqDbl(Queue<string> q, out double v)
    {
        v = 0;
        return q.Count > 0 && double.TryParse(q.Dequeue(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}
