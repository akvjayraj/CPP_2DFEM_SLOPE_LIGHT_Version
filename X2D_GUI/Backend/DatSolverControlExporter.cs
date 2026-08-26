using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Writes FE Analysis → Solver control (tol, limit, nsrf, F list) back into Script Editor .dat.
/// Mirrors <see cref="DatSolverControlImporter"/> for p64 explicit multi-SRF tails.
/// </summary>
public static class DatSolverControlExporter
{
    /// <summary>Non-fatal warning from the last solver-control export (e.g. suboptimal tol/incs for T3 SRF).</summary>
    public static string? LastExportWarning { get; private set; }

    public static bool TryPatchSrfSolverControl(
        string datContent,
        FEAnalysisViewModel feVm,
        out string patched)
        => TryPatchSrfSolverControl(datContent, feVm, out patched, out _);

    public static bool TryPatchSrfSolverControl(
        string datContent,
        FEAnalysisViewModel feVm,
        out string patched,
        out bool contentChanged)
        => TryPatchSolverControl(datContent, feVm, srfAnalysisMode: true, out patched, out contentChanged);

    /// <summary>
    /// FE Analysis → Script Editor: patch solver control for p610 (tol/limit/incs/layers) and
    /// p64 SRF tails (tol/limit/nsrf/F list). When <paramref name="srfAnalysisMode"/> is false,
    /// only the p610 control line is updated.
    /// </summary>
    public static bool TryPatchSolverControl(
        string datContent,
        FEAnalysisViewModel feVm,
        bool srfAnalysisMode,
        out string patched,
        out bool contentChanged)
    {
        patched = datContent;
        contentChanged = false;
        if (string.IsNullOrWhiteSpace(datContent) || feVm == null)
            return false;

        if (!HasSolverControlBlock(datContent))
            return false;

        // p610_t3_elastic reads n_trac_groups immediately after output nodes. A numeric
        // tol/limit/incs/layers line in that slot is parsed as a huge traction-group count
        // and crashes the native solver (0xC0000005).
        if (P610T3ElasticDatContentBuilder.IsP610T3ElasticContent(datContent)
            && !P610AnalysisOptionsHelper.IsSrfDatDeck(datContent))
            return false;

        if (srfAnalysisMode && P610AnalysisOptionsHelper.IsSrfDatDeck(datContent))
        {
            if (feVm.IsSrfBracketMode)
            {
                return TryPatchSrfBracketControl(
                    datContent,
                    feVm,
                    out patched,
                    out contentChanged);
            }

            if (UsesP610SolverControlFormat(datContent))
            {
                return TryPatchP610T3SrfControl(
                    datContent,
                    feVm,
                    out patched,
                    out contentChanged);
            }

            var sweptDat = datContent;
            if (P610AnalysisOptionsHelper.TryPatchSrfSearchModeSweep(sweptDat, out var withSweepMode, out var sweepModeChanged))
            {
                sweptDat = withSweepMode;
            }

            var swept = TryPatchP64MultiSrfTailFromEnd(
                sweptDat,
                feVm.ConvergenceTolerance,
                feVm.MaxIterations,
                Math.Max(1, feVm.SrfTrialCount),
                NormalizeFValues(feVm),
                out patched,
                out contentChanged);
            contentChanged = contentChanged || sweepModeChanged;
            return swept;
        }

        if (UsesP610SolverControlFormat(datContent))
        {
            return TryPatchP610SolverControl(
                datContent,
                feVm,
                out patched,
                out contentChanged);
        }

        return false;
    }

    /// <summary>True when the .dat carries a p610 solver-control line or fenced block.</summary>
    public static bool HasSolverControlBlock(string datContent)
    {
        if (string.IsNullOrWhiteSpace(datContent))
            return false;
        if (datContent.Contains("! <BEGIN:SolverControl>", StringComparison.OrdinalIgnoreCase))
            return true;
        if (datContent.Contains("tol  limit  incs  layers", StringComparison.OrdinalIgnoreCase))
            return true;
        return UsesP610SolverControlFormat(datContent);
    }

    public static bool UsesP610SolverControlFormat(string datContent)
    {
        if (P610T3DatContentBuilder.IsP610T3Content(datContent))
            return true;
        if (datContent.Contains("! <BEGIN:SolverControl>", StringComparison.OrdinalIgnoreCase))
            return true;
        if (datContent.Contains("solver:p610_t3", StringComparison.OrdinalIgnoreCase))
            return true;
        if (datContent.Contains("tol  limit  incs  layers", StringComparison.OrdinalIgnoreCase))
            return true;

        var program = P610AnalysisOptionsHelper.TryParse(datContent, out var options)
            ? P610AnalysisOptionsHelper.ResolveSlopeSrfRunner(options, null)
            : "";
        return P610AnalysisOptionsHelper.IsSlopeStabilitySrfExecutable(program)
               && !string.Equals(
                   FemCppProgramRouter.ResolveProgramId(datContent),
                   "p64",
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Bracket mode: writes the AnalysisOptions bracket keys (search mode, auto-detect, seed,
    /// expansion factor, max steps, F_min/F_max, tolerance, max iterations), then patches the
    /// tol/limit/incs/layers solver-control line without touching the nsrf/F-list trial comments
    /// (unused in bracket mode — single-trial F values are patched per solver call at run time).
    /// </summary>
    private static bool TryPatchSrfBracketControl(
        string datContent,
        FEAnalysisViewModel feVm,
        out string patched,
        out bool contentChanged)
    {
        patched = datContent;
        contentChanged = false;

        // In auto-detect mode, persist the last detected bracket (if the expansion phase has run
        // in this session) rather than the manual-override fields, so a saved/reloaded project
        // reflects the actual detected bracket without re-running the expansion phase.
        double persistedFMin = feVm.AutoDetectSrfBracket && feVm.LastDetectedSrfFoSMin.HasValue
            ? feVm.LastDetectedSrfFoSMin.Value
            : feVm.SrfFoSMin;
        double persistedFMax = feVm.AutoDetectSrfBracket && feVm.LastDetectedSrfFoSMax.HasValue
            ? feVm.LastDetectedSrfFoSMax.Value
            : feVm.SrfFoSMax;

        var working = datContent;
        if (P610AnalysisOptionsHelper.TryPatchSrfBracketSettings(
                working,
                feVm.AutoDetectSrfBracket,
                feVm.SrfBracketSeed,
                feVm.SrfBracketExpansionFactor,
                feVm.SrfMaxExpansionSteps,
                persistedFMin,
                persistedFMax,
                feVm.SrfBracketTolerance,
                feVm.SrfMaxBracketIterations,
                out var withBracket,
                out var bracketChanged))
        {
            working = withBracket;
            if (bracketChanged)
                contentChanged = true;
        }

        if (P610AnalysisOptionsHelper.TryPatchSrfInsituMode(
                working,
                feVm.ResolveEffectiveSrfInsituModeToken(),
                out var withInsitu,
                out var insituChanged))
        {
            working = withInsitu;
            if (insituChanged)
                contentChanged = true;
        }

        if (UsesP610SolverControlFormat(working))
        {
            if (TryPatchP610ControlCore(working, feVm, patchSrfF: true, writeTrialListComments: false, out var withControl, out var controlChanged))
            {
                working = withControl;
                if (controlChanged)
                    contentChanged = true;
            }
        }
        else if (TryPatchP64ToleranceLimitOnly(working, feVm.ConvergenceTolerance, feVm.MaxIterations, out var withTol, out var tolChanged))
        {
            working = withTol;
            if (tolChanged)
                contentChanged = true;
        }

        patched = working;
        if (!contentChanged)
            contentChanged = !string.Equals(patched, datContent, StringComparison.Ordinal);
        return true;
    }

    /// <summary>Patche p610_t3 solver control (tol, limit, incs, layers) without SRF AnalysisOptions.</summary>
    private static bool TryPatchP610SolverControl(
        string datContent,
        FEAnalysisViewModel feVm,
        out string patched,
        out bool contentChanged)
        => TryPatchP610ControlCore(datContent, feVm, patchSrfF: false, writeTrialListComments: false, out patched, out contentChanged);

    /// <summary>
    /// Patches p610_t3 solver control (tol, limit, incs, layers) and the first SRF trial into
    /// AnalysisOptions <c>srf_F</c>. Multi-SRF loops are orchestrated at run time for T3 solvers.
    /// </summary>
    private static bool TryPatchP610T3SrfControl(
        string datContent,
        FEAnalysisViewModel feVm,
        out string patched,
        out bool contentChanged)
    {
        var sweptDat = datContent;
        if (P610AnalysisOptionsHelper.TryPatchSrfSearchModeSweep(sweptDat, out var withSweepMode, out var sweepModeChanged))
            sweptDat = withSweepMode;
        else
            sweepModeChanged = false;

        if (P610AnalysisOptionsHelper.TryPatchSrfInsituMode(
                sweptDat,
                feVm.ResolveEffectiveSrfInsituModeToken(),
                out var withInsitu,
                out var insituChanged))
            sweptDat = withInsitu;
        else
            insituChanged = false;

        var ok = TryPatchP610ControlCore(sweptDat, feVm, patchSrfF: true, writeTrialListComments: true, out patched, out contentChanged);
        contentChanged = contentChanged || sweepModeChanged || insituChanged
            || !string.Equals(patched, datContent, StringComparison.Ordinal);
        return ok;
    }

    private static bool TryPatchP610ControlCore(
        string datContent,
        FEAnalysisViewModel feVm,
        bool patchSrfF,
        bool writeTrialListComments,
        out string patched,
        out bool contentChanged)
    {
        patched = datContent;
        contentChanged = false;
        LastExportWarning = null;

        var working = datContent;
        if (patchSrfF)
        {
            var fValues = NormalizeFValues(feVm);
            double srfF = fValues.Count > 0 ? fValues[0] : 1.0;
            if (P610AnalysisOptionsHelper.TryPatchSrfF(working, srfF, out var withSrf, out var srfChanged))
            {
                working = withSrf;
                if (srfChanged)
                    contentChanged = true;
            }
        }

        double tol = feVm.ConvergenceTolerance;
        int limit = Math.Max(1, feVm.MaxIterations);
        int incs = Math.Max(1, feVm.LoadIncrements);
        int layers = feVm.LayersOverride ?? 1;
        if (layers < 1)
            layers = 1;

        if (patchSrfF)
        {
            var warnings = new List<string>();
            if (tol > 0.001)
                warnings.Add($"SRF T3: convergence tolerance {Fmt(tol)} is loose; Ex2/book decks use 0.0001.");
            if (feVm.MaxIterations < 1)
                warnings.Add("SRF T3: max iterations was below 1; using 1 to avoid zero-iteration solves.");
            if (incs < 20)
            {
                warnings.Add($"SRF T3: load increments {incs} < 20; gravity equilibration may fail (using min 20).");
                incs = 20;
            }
            if (warnings.Count > 0)
                LastExportWarning = string.Join(" ", warnings);
        }

        var newline = working.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var controlLine = $"{Fmt(tol)}  {limit}  {incs}  {layers}";

        const string begin = "! <BEGIN:SolverControl>";
        const string end = "! <END:SolverControl>";
        var beginIdx = working.IndexOf(begin, StringComparison.OrdinalIgnoreCase);
        if (beginIdx >= 0)
        {
            var endIdx = working.IndexOf(end, beginIdx, StringComparison.OrdinalIgnoreCase);
            if (endIdx >= 0)
            {
                var head = working[..beginIdx];
                var tail = working[(endIdx + end.Length)..];
                var block = BuildSolverControlFencedBlock(
                    controlLine,
                    feVm,
                    writeTrialListComments,
                    working[beginIdx..(endIdx + end.Length)],
                    newline);
                patched = head + block + tail;
                if (!string.Equals(patched, datContent, StringComparison.Ordinal))
                    contentChanged = true;
                return FinalizeSrfT3Patch(datContent, patchSrfF, ref patched, ref contentChanged);
            }
        }

        var lines = working.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        var dataLineIndices = new List<int>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(StripComment(lines[i])))
                dataLineIndices.Add(i);
        }

        for (int i = dataLineIndices.Count - 1; i >= 0; i--)
        {
            int idx = dataLineIndices[i];
            var tokens = SplitDataTokens(lines[idx]);
            if (tokens.Count != 4)
                continue;
            if (!double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                continue;
            if (!int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                continue;
            if (!int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                continue;
            if (!int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                continue;

            lines[idx] = ReplaceDataPrefix(lines[idx], controlLine);
            if (writeTrialListComments)
                TryPatchSrfTrialCommentsAfterControl(lines, idx, dataLineIndices, i, feVm, ref contentChanged);

            patched = string.Join(newline, lines);
            if (datContent.EndsWith(newline, StringComparison.Ordinal) && !patched.EndsWith(newline, StringComparison.Ordinal))
                patched += newline;
            if (!string.Equals(patched, datContent, StringComparison.Ordinal))
                contentChanged = true;
            return FinalizeSrfT3Patch(datContent, patchSrfF, ref patched, ref contentChanged);
        }

        patched = working;
        return FinalizeSrfT3Patch(datContent, patchSrfF, ref patched, ref contentChanged);
    }

    private static bool FinalizeSrfT3Patch(
        string originalDat,
        bool patchSrfF,
        ref string patched,
        ref bool contentChanged)
    {
        if (!patchSrfF)
            return contentChanged;

        var equil = EnsureSrfT3GravityEquilibrationDeck(patched);
        if (!string.Equals(equil, patched, StringComparison.Ordinal))
        {
            patched = equil;
            contentChanged = true;
        }
        if (!string.Equals(patched, originalDat, StringComparison.Ordinal))
            contentChanged = true;
        return contentChanged;
    }

    private static string BuildSolverControlFencedBlock(
        string controlLine,
        FEAnalysisViewModel feVm,
        bool writeTrialListComments,
        string existingBlock,
        string newline)
    {
        const string begin = "! <BEGIN:SolverControl>";
        const string end = "! <END:SolverControl>";

        int nsrf = Math.Max(1, feVm.SrfTrialCount);
        var fValues = NormalizeFValues(feVm);
        bool hadExistingTrialComments = false;

        if (!writeTrialListComments && TryReadSrfTrialsFromBlock(existingBlock, out var oldNsrf, out var oldF))
        {
            nsrf = oldNsrf;
            fValues = oldF;
            hadExistingTrialComments = true;
        }

        var sb = new System.Text.StringBuilder();
        sb.Append(begin);
        sb.Append(newline);
        sb.Append("! tol  limit  incs  layers");
        sb.Append(newline);
        sb.Append(controlLine);
        if (writeTrialListComments || hadExistingTrialComments)
        {
            sb.Append(newline);
            sb.Append(FormatSrfNsrfCommentLine(nsrf));
            sb.Append(newline);
            sb.Append(FormatSrfTrialsCommentLine(fValues));
        }
        sb.Append(newline);
        sb.Append(end);
        return sb.ToString();
    }

    /// <summary>
    /// SRF trial metadata for GUI sync only — must stay on comment lines so the stripped
    /// numeric stream the C++ solver reads stays: tol limit incs layers | layer data …
    /// </summary>
    internal static string FormatSrfNsrfCommentLine(int nsrf) =>
        $"! nsrf {nsrf.ToString(CultureInfo.InvariantCulture)}";

    internal static string FormatSrfTrialsCommentLine(IReadOnlyList<double> fValues) =>
        $"! srf_F_trials {string.Join("  ", fValues.Select(Fmt))}";

    internal static bool TryParseSrfTrialComments(
        string datContent,
        out int nsrf,
        out List<double> fValues)
    {
        nsrf = 0;
        fValues = new List<double>();
        if (string.IsNullOrWhiteSpace(datContent))
            return false;

        bool gotNsrf = false;
        bool gotTrials = false;
        foreach (var raw in datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var bang = raw.IndexOf('!');
            if (bang < 0) continue;
            var body = raw[(bang + 1)..].Trim();
            if (body.Length == 0) continue;

            if (body.StartsWith("nsrf", StringComparison.OrdinalIgnoreCase))
            {
                var tail = body.Length > 4 ? body[4..].Trim() : "";
                if (int.TryParse(tail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 1)
                {
                    nsrf = n;
                    gotNsrf = true;
                }
                continue;
            }

            if (body.StartsWith("srf_F_trials", StringComparison.OrdinalIgnoreCase))
            {
                var tail = body.Length > 12 ? body[12..].Trim() : "";
                foreach (var tok in tail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out double f))
                        fValues.Add(f);
                }
                gotTrials = fValues.Count > 0;
            }
        }

        return gotNsrf || gotTrials;
    }

    private static bool TryReadSrfTrialsFromBlock(
        string blockContent,
        out int nsrf,
        out List<double> fValues)
        => TryParseSrfTrialComments(blockContent, out nsrf, out fValues);

    private static void TryPatchSrfTrialCommentsAfterControl(
        List<string> lines,
        int controlLineIdx,
        List<int> dataLineIndices,
        int controlPosInDataIndices,
        FEAnalysisViewModel feVm,
        ref bool contentChanged)
    {
        TryRemoveCorruptSrfTrialNumericLines(lines, dataLineIndices, controlPosInDataIndices, ref contentChanged);

        var fValues = NormalizeFValues(feVm);
        int nsrf = Math.Max(1, feVm.SrfTrialCount);
        string nsrfComment = FormatSrfNsrfCommentLine(nsrf);
        string trialsComment = FormatSrfTrialsCommentLine(fValues);

        bool haveNsrf = false;
        bool haveTrials = false;
        for (int i = controlLineIdx + 1; i < Math.Min(lines.Count, controlLineIdx + 12); i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;
            if (lines[i].IndexOf('!') < 0)
                break;

            var bang = lines[i].IndexOf('!');
            var body = lines[i][(bang + 1)..].Trim();
            if (body.StartsWith("nsrf", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(lines[i], nsrfComment, StringComparison.Ordinal))
                {
                    lines[i] = nsrfComment;
                    contentChanged = true;
                }
                haveNsrf = true;
            }
            else if (body.StartsWith("srf_F_trials", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(lines[i], trialsComment, StringComparison.Ordinal))
                {
                    lines[i] = trialsComment;
                    contentChanged = true;
                }
                haveTrials = true;
            }
        }

        int insertAt = controlLineIdx + 1;
        if (!haveNsrf)
        {
            lines.Insert(insertAt, nsrfComment);
            contentChanged = true;
            insertAt++;
        }
        if (!haveTrials)
        {
            lines.Insert(insertAt, trialsComment);
            contentChanged = true;
        }
    }

    /// <summary>
    /// Removes mistaken bare numeric nsrf / F lines that crash the C++ solver (reads them as excavation data).
    /// </summary>
    private static void TryRemoveCorruptSrfTrialNumericLines(
        List<string> lines,
        List<int> dataLineIndices,
        int controlPosInDataIndices,
        ref bool contentChanged)
    {
        int next = controlPosInDataIndices + 1;
        if (next + 1 >= dataLineIndices.Count)
            return;

        int nsrfIdx = dataLineIndices[next];
        int fIdx = dataLineIndices[next + 1];
        if (!LooksLikeCorruptSrfTrialNumericLines(lines, nsrfIdx, fIdx))
            return;

        lines.RemoveAt(Math.Max(nsrfIdx, fIdx));
        lines.RemoveAt(Math.Min(nsrfIdx, fIdx));
        contentChanged = true;

        for (int i = dataLineIndices.Count - 1; i >= 0; i--)
            if (dataLineIndices[i] == nsrfIdx || dataLineIndices[i] == fIdx)
                dataLineIndices.RemoveAt(i);
        for (int i = 0; i < dataLineIndices.Count; i++)
        {
            if (dataLineIndices[i] > nsrfIdx)
                dataLineIndices[i] -= 2;
        }
    }

    private static bool LooksLikeCorruptSrfTrialNumericLines(List<string> lines, int nsrfLineIdx, int fLineIdx)
    {
        var nsrfTok = SplitDataTokens(lines[nsrfLineIdx]);
        var fTok = SplitDataTokens(lines[fLineIdx]);
        if (nsrfTok.Count != 1
            || !int.TryParse(nsrfTok[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            || n < 1 || n > 64)
            return false;
        if (fTok.Count < 1
            || !fTok.All(t => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            return false;
        // SRF F trials use decimals (1.1); excavation element IDs are integers.
        return fTok.Any(t => t.Contains('.', StringComparison.Ordinal));
    }

    /// <summary>
    /// Strips mistaken bare numeric nsrf/F lines from p610 .dat before the C++ solver runs.
    /// </summary>
    public static string SanitizeP610DatForSolver(string datContent)
    {
        if (!UsesP610SolverControlFormat(datContent))
            return datContent;

        if (P610T3ElasticDatContentBuilder.IsP610T3ElasticContent(datContent)
            && !P610AnalysisOptionsHelper.IsSrfDatDeck(datContent))
            return StripElasticSolverControlNumericLines(datContent);

        var newline = datContent.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        var dataLineIndices = new List<int>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(StripComment(lines[i])))
                dataLineIndices.Add(i);
        }

        bool changed = false;
        for (int i = dataLineIndices.Count - 1; i >= 0; i--)
        {
            int idx = dataLineIndices[i];
            var tokens = SplitDataTokens(lines[idx]);
            if (tokens.Count != 4) continue;
            if (!double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                continue;
            if (!int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                continue;
            if (!int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                continue;
            if (!int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                continue;

            TryRemoveCorruptSrfTrialNumericLines(lines, dataLineIndices, i, ref changed);
            break;
        }

        return changed ? string.Join(newline, lines) : datContent;
    }

    /// <summary>
    /// p610_t3_elastic has no tol/limit/incs/layers field. Strip any numeric control line
    /// that was patched into the SolverControl fence (or left as a bare 4-token line).
    /// </summary>
    private static string StripElasticSolverControlNumericLines(string datContent)
    {
        var newline = datContent.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        bool changed = false;
        bool inSolverControl = false;

        for (int i = lines.Count - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Contains("<END:SolverControl>", StringComparison.OrdinalIgnoreCase))
            {
                inSolverControl = true;
                continue;
            }
            if (trimmed.Contains("<BEGIN:SolverControl>", StringComparison.OrdinalIgnoreCase))
            {
                inSolverControl = false;
                continue;
            }

            if (!IsElasticSolverControlNumericLine(lines[i]))
                continue;

            if (inSolverControl || !HasSolverControlFence(datContent))
            {
                lines.RemoveAt(i);
                changed = true;
            }
        }

        if (!changed)
            return datContent;

        var patched = string.Join(newline, lines);
        if (datContent.EndsWith(newline, StringComparison.Ordinal) && !patched.EndsWith(newline, StringComparison.Ordinal))
            patched += newline;
        return patched;
    }

    private static bool HasSolverControlFence(string datContent)
        => datContent.Contains("<BEGIN:SolverControl>", StringComparison.OrdinalIgnoreCase)
           && datContent.Contains("<END:SolverControl>", StringComparison.OrdinalIgnoreCase);

    private static bool IsElasticSolverControlNumericLine(string line)
    {
        var tokens = SplitDataTokens(line);
        if (tokens.Count != 4)
            return false;
        if (!double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var tol))
            return false;
        if (!int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return false;
        if (!int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return false;
        if (!int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return false;

        // Solver-control signature: tiny tolerance, not a traction mode (0/1) + stresses.
        return Math.Abs(tol) > 0 && Math.Abs(tol) < 1;
    }

    private static List<double> NormalizeFValues(FEAnalysisViewModel feVm)
    {
        var fValues = feVm.SrfValues.Select(e => e.Value).ToList();
        int nsrf = Math.Max(1, feVm.SrfTrialCount);
        while (fValues.Count < nsrf)
            fValues.Add(fValues.Count == 0 ? 1.0 : Math.Round(fValues[^1] + 0.1, 4));
        if (fValues.Count > nsrf)
            fValues = fValues.Take(nsrf).ToList();
        return fValues;
    }

    /// <summary>
    /// Replaces the last three numeric data lines: <c>tol limit</c>, <c>nsrf</c>, <c>F1 F2 …</c>.
    /// </summary>
    private static bool TryPatchP64MultiSrfTailFromEnd(
        string datContent,
        double tol,
        int limit,
        int nsrf,
        IReadOnlyList<double> fValues,
        out string patched,
        out bool contentChanged)
    {
        patched = datContent;
        contentChanged = false;
        var newline = datContent.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        var dataLineIndices = new List<int>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(StripComment(lines[i])))
                dataLineIndices.Add(i);
        }

        if (dataLineIndices.Count < 3)
            return false;

        int tolIdx = dataLineIndices[^3];
        int nsrfIdx = dataLineIndices[^2];
        int fIdx = dataLineIndices[^1];

        var tolTokens = SplitDataTokens(lines[tolIdx]);
        var nsrfTokens = SplitDataTokens(lines[nsrfIdx]);
        var fTokens = SplitDataTokens(lines[fIdx]);

        if (tolTokens.Count < 2
            || !double.TryParse(tolTokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            || !int.TryParse(tolTokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return false;

        if (nsrfTokens.Count != 1
            || !int.TryParse(nsrfTokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int oldNsrf)
            || oldNsrf < 1)
            return false;

        if (fTokens.Count < 1
            || !fTokens.All(t => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            return false;

        lines[tolIdx] = ReplaceDataPrefix(lines[tolIdx], $"{Fmt(tol)}  {limit}");
        lines[nsrfIdx] = ReplaceDataPrefix(lines[nsrfIdx], nsrf.ToString(CultureInfo.InvariantCulture));
        lines[fIdx] = ReplaceDataPrefix(lines[fIdx], string.Join("  ", fValues.Select(Fmt)));

        patched = string.Join(newline, lines);
        if (datContent.EndsWith(newline, StringComparison.Ordinal) && !patched.EndsWith(newline, StringComparison.Ordinal))
            patched += newline;

        contentChanged = !string.Equals(patched, datContent, StringComparison.Ordinal);
        return true;
    }

    /// <summary>
    /// Patches only the <c>tol limit</c> numeric line for p64-format decks (3rd-from-last data
    /// line), leaving the existing <c>nsrf</c> / F-list tail untouched — used by bracket mode,
    /// which never reads that tail (single-trial F values are patched per solver call at run time).
    /// </summary>
    private static bool TryPatchP64ToleranceLimitOnly(
        string datContent,
        double tol,
        int limit,
        out string patched,
        out bool contentChanged)
    {
        patched = datContent;
        contentChanged = false;
        var newline = datContent.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        var dataLineIndices = new List<int>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(StripComment(lines[i])))
                dataLineIndices.Add(i);
        }

        int tolIdx = -1;
        if (SrfExplicitDeckFreshness.TryLocateP64TolLimitDataLine(datContent, out int compactTolDataIdx)
            && compactTolDataIdx >= 0
            && compactTolDataIdx < dataLineIndices.Count)
        {
            tolIdx = dataLineIndices[compactTolDataIdx];
        }
        else if (dataLineIndices.Count >= 3)
        {
            int candidateIdx = dataLineIndices[^3];
            var tolTokens = SplitDataTokens(lines[candidateIdx]);
            if (tolTokens.Count >= 2
                && double.TryParse(tolTokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                && int.TryParse(tolTokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                tolIdx = candidateIdx;
        }

        if (tolIdx < 0)
            return false;

        lines[tolIdx] = ReplaceDataPrefix(lines[tolIdx], $"{Fmt(tol)}  {limit}");

        patched = string.Join(newline, lines);
        if (datContent.EndsWith(newline, StringComparison.Ordinal) && !patched.EndsWith(newline, StringComparison.Ordinal))
            patched += newline;

        contentChanged = !string.Equals(patched, datContent, StringComparison.Ordinal);
        return true;
    }

    private static string StripComment(string line)
    {
        var bang = line.IndexOf('!');
        return bang >= 0 ? line[..bang].Trim() : line.Trim();
    }

    private static List<string> SplitDataTokens(string line)
        => StripComment(line)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .ToList();

    private static string ReplaceDataPrefix(string originalLine, string newData)
    {
        var bang = originalLine.IndexOf('!');
        if (bang < 0)
            return newData;
        return newData + originalLine[bang..];
    }

    private static string Fmt(double v) => v.ToString("G10", CultureInfo.InvariantCulture);

    /// <summary>
    /// SRF T3 decks must use <c>layers ≥ 1</c> plus a gravity-only excavation block (<c>0</c> elements).
    /// Without this tail the native solver enters the <c>layers==0</c> traction path and returns u=0 / σ=0.
    /// </summary>
    public static string EnsureSrfT3GravityEquilibrationDeck(string datContent)
    {
        if (string.IsNullOrWhiteSpace(datContent))
            return datContent;

        var working = datContent;
        if (TryPatchBareSolverControlLayers(working, minLayers: 1, out var withLayers, out _))
            working = withLayers;
        if (TryPatchBareSolverControlIncs(working, minIncs: 20, out var withIncs, out _))
            working = withIncs;

        if (HasGravityOnlyLayerBlock(working))
            return working;

        const string layerBlock = "! <BEGIN:Layers>" + "\n"
            + "! Excavation layer 1 — gravity equilibration (0 elements removed)" + "\n"
            + "0" + "\n"
            + "! <END:Layers>" + "\n";

        const string begin = "! <BEGIN:SolverControl>";
        const string end = "! <END:SolverControl>";
        var beginIdx = working.IndexOf(begin, StringComparison.OrdinalIgnoreCase);
        if (beginIdx >= 0)
        {
            var endIdx = working.IndexOf(end, beginIdx, StringComparison.OrdinalIgnoreCase);
            if (endIdx >= 0)
            {
                var insertAt = endIdx + end.Length;
                var newline = working.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                return working.Insert(insertAt, newline + newline + layerBlock.TrimEnd('\r', '\n') + newline);
            }
        }

        var newlineBare = working.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return working.TrimEnd('\r', '\n') + newlineBare + newlineBare + "0" + newlineBare;
    }

    private static bool HasGravityOnlyLayerBlock(string datContent)
    {
        const string begin = "! <BEGIN:Layers>";
        const string end = "! <END:Layers>";
        var beginIdx = datContent.IndexOf(begin, StringComparison.OrdinalIgnoreCase);
        if (beginIdx >= 0)
        {
            var endIdx = datContent.IndexOf(end, beginIdx, StringComparison.OrdinalIgnoreCase);
            if (endIdx > beginIdx)
            {
                var body = datContent[(beginIdx + begin.Length)..endIdx];
                foreach (var raw in body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    var line = StripComment(raw).Trim();
                    if (line.Length == 0) continue;
                    return int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n == 0;
                }
            }
        }

        if (!TryFindSolverControlDataLine(datContent, out var controlIdx, out var layers))
            return false;
        if (layers < 1)
            return false;

        var lines = datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (int i = controlIdx + 1; i < lines.Length; i++)
        {
            var line = StripComment(lines[i]).Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("! <BEGIN:", StringComparison.OrdinalIgnoreCase))
                return false;
            return int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0;
        }

        return false;
    }

    private static bool TryPatchBareSolverControlIncs(
        string datContent,
        int minIncs,
        out string patched,
        out bool changed)
    {
        patched = datContent;
        changed = false;
        if (!TryFindSolverControlDataLine(datContent, out var idx, out _))
            return false;

        var lines = datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var tokens = SplitDataTokens(lines[idx]);
        if (tokens.Count != 4)
            return false;
        if (!int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int incs))
            return false;
        if (incs >= minIncs)
            return false;

        tokens[2] = minIncs.ToString(CultureInfo.InvariantCulture);
        var newData = string.Join("  ", tokens);
        lines[idx] = ReplaceDataPrefix(lines[idx], newData);
        patched = string.Join(datContent.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n", lines);
        changed = true;
        return true;
    }

    private static bool TryPatchBareSolverControlLayers(
        string datContent,
        int minLayers,
        out string patched,
        out bool changed)
    {
        patched = datContent;
        changed = false;
        if (!TryFindSolverControlDataLine(datContent, out var idx, out var layers))
            return false;
        if (layers >= minLayers)
            return false;

        var lines = datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var tokens = SplitDataTokens(lines[idx]);
        if (tokens.Count != 4)
            return false;

        tokens[3] = minLayers.ToString(CultureInfo.InvariantCulture);
        var newData = string.Join("  ", tokens);
        lines[idx] = ReplaceDataPrefix(lines[idx], newData);
        patched = string.Join(datContent.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n", lines);
        changed = true;
        return true;
    }

    private static bool TryFindSolverControlDataLine(string datContent, out int lineIndex, out int layers)
    {
        lineIndex = -1;
        layers = 0;
        var lines = datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var tokens = SplitDataTokens(lines[i]);
            if (tokens.Count != 4) continue;
            if (!double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                continue;
            if (!int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                continue;
            if (!int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                continue;
            if (!int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out layers))
                continue;
            lineIndex = i;
            return true;
        }

        return false;
    }
}
