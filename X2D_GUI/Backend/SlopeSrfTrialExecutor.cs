using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Runs one SRF trial (a single strength-reduction factor F) and classifies it as
/// converged/failed. Shared by <see cref="SlopeT3MultiSrfRunner"/> (fixed sweep) and
/// <see cref="SlopeSrfBracketRunner"/> (auto-expansion + bisection), across both the
/// branded T3/T6/Q4 solver family and the p64/Q8 route.
/// </summary>
public static class SlopeSrfTrialExecutor
{
    /// <summary>
    /// Maximum Mohr–Coulomb overstress ratio accepted as a "stable" SRF trial.
    /// Solvers can report <c>all_increments_converged=true</c> while plastic
    /// residual remains large (false stable). Example 16 wet F=1.0 has yield≈48
    /// while F=0.9 has yield≈8 — this gate rejects the runaway case.
    /// </summary>
    public const double MaxStableYieldRatio = 25.0;

    /// <summary>Which native solver route a trial should use.</summary>
    public enum SrfProgramKind
    {
        /// <summary>Branded Core A <c>slope_stability_*_SRF</c> exe with p610 deck.</summary>
        T3Family,
        /// <summary>Branded Core B <c>slope_stability_Q8_SRF</c> exe with explicit p64-layout deck.</summary>
        CoreBBranded,
        /// <summary><c>fem_cpp p64</c> (legacy explicit), driven via <see cref="CppAnalysisRunner.RunFemCppFromDatContentAsync"/>.</summary>
        P64,
    }

    public sealed class TrialOutcome
    {
        public P610T3RunResult JsonResult { get; init; } = null!;
        public P610T3StageSnapshot Stage { get; init; } = null!;
        public P64ResFileParser.SrfTrialRow TrialRow { get; init; } = null!;
        public bool Converged { get; init; }
        public bool HasNonFiniteDisplacement { get; init; }
    }

    /// <summary>Runs a single SRF trial at strength-reduction factor <paramref name="f"/> and classifies convergence.</summary>
    public static async Task<TrialOutcome> RunTrialAsync(
        CppAnalysisRunner runner,
        SrfProgramKind kind,
        string program,
        string datContent,
        string baseName,
        double f,
        int iterationLimit,
        string? workingDirectory,
        CancellationToken cancellationToken = default,
        string? brandedProgram = null)
    {
        var trialBase = $"{baseName}_srf_{f.ToString("G4", CultureInfo.InvariantCulture).Replace('.', '_')}";

        return kind switch
        {
            SrfProgramKind.P64 => await RunP64TrialAsync(runner, program, datContent, trialBase, f, iterationLimit, workingDirectory, cancellationToken).ConfigureAwait(false),
            SrfProgramKind.CoreBBranded => await RunCoreBBrandedTrialAsync(runner, program, datContent, trialBase, f, iterationLimit, workingDirectory, cancellationToken, brandedProgram).ConfigureAwait(false),
            _ => await RunT3TrialAsync(runner, program, datContent, trialBase, f, iterationLimit, workingDirectory, cancellationToken).ConfigureAwait(false),
        };
    }

    private static async Task<TrialOutcome> RunCoreBBrandedTrialAsync(
        CppAnalysisRunner runner,
        string program,
        string datContent,
        string trialBase,
        double f,
        int iterationLimit,
        string? workingDirectory,
        CancellationToken cancellationToken,
        string? brandedProgram = null)
    {
        var workDir = string.IsNullOrEmpty(workingDirectory) ? Path.GetTempPath() : workingDirectory;

        var trialDat = datContent;
        if (P610AnalysisOptionsHelper.TryPatchSrfFSingleTrial(datContent, f, out var patched, out _))
            trialDat = patched;

        var result = await runner.RunNativeSlopeSrfFromDatContentAsync(
                program, trialDat, trialBase, workingDirectory, cancellationToken, brandedProgram)
            .ConfigureAwait(false);

        var resPath = Path.Combine(workDir, trialBase + ".res");
        P64ResFileParser.SrfTrialRow? resRow = null;
        if (File.Exists(resPath))
        {
            var summary = P64ResFileParser.ParseFile(resPath);
            resRow = summary.Trials.FirstOrDefault(t => Math.Abs(t.Srf - f) < 1e-6)
                ?? summary.Trials.LastOrDefault();
        }

        var outcome = BuildP64TrialOutcome(result, resRow, f, iterationLimit);
        if (!outcome.Converged && resRow == null)
        {
            var recovered = TryRecoverTrialRowFromJsonStage(result, f);
            if (recovered != null)
                outcome = BuildP64TrialOutcome(result, recovered, f, iterationLimit);
        }

        return outcome;
    }

    private static async Task<TrialOutcome> RunT3TrialAsync(
        CppAnalysisRunner runner,
        string program,
        string datContent,
        string trialBase,
        double f,
        int iterationLimit,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        var workDir = string.IsNullOrEmpty(workingDirectory) ? Path.GetTempPath() : workingDirectory;

        var trialDat = datContent;
        if (P610AnalysisOptionsHelper.TryPatchSrfFSingleTrial(datContent, f, out var patched, out _))
            trialDat = patched;

        var result = await runner.RunP610T3VariantFromDatContentAsync(
                program, trialDat, trialBase, workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        var stageIdx = PickPostGravityStageIndex(result);
        var snap = result.Stages[stageIdx];
        double umax = ComputeMaxDisplacement(snap.DisplacementsByNodeIndex);
        bool nonFinite = false;
        var resPath = Path.Combine(workDir, trialBase + ".res");
        if (P64ResFileParser.TryScanT3ResDisplacements(resPath, out var resMax, out var resNan, out _))
        {
            if (resNan)
            {
                nonFinite = true;
                umax = double.NaN;
            }
            else if (resMax > umax)
                umax = resMax;
        }
        if (result.Meta?.SrfMaxDisplacement is { } metaDisp && metaDisp > umax)
            umax = metaDisp;

        int iters = result.Meta?.SrfMaxItersUsed is { } mi && mi > 0
            ? (int)Math.Round(mi)
            : iterationLimit;

        bool converged = ClassifyTrialConverged(
            iters,
            iterationLimit,
            umax,
            sawNonFinite: nonFinite,
            result.Meta?.SrfAllIncrementsConverged,
            result.Meta?.SrfMaxYieldRatio);

        return new TrialOutcome
        {
            JsonResult = result,
            Stage = new P610T3StageSnapshot
            {
                Index = 0,
                Label = $"srf_{f:G2}",
                Subtitle = $"SRF={f:G2}, |u|max={umax:E2}",
                DisplacementsByNodeIndex = snap.DisplacementsByNodeIndex,
                ElementStresses = snap.ElementStresses,
                ElementsSolid = snap.ElementsSolid,
            },
            TrialRow = new P64ResFileParser.SrfTrialRow { Srf = f, MaxDisplacement = umax, Iterations = iters },
            Converged = converged,
            HasNonFiniteDisplacement = nonFinite || !double.IsFinite(umax),
        };
    }

    private static async Task<TrialOutcome> RunP64TrialAsync(
        CppAnalysisRunner runner,
        string program,
        string datContent,
        string trialBase,
        double f,
        int iterationLimit,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        var trialDat = datContent;
        P610AnalysisOptionsHelper.TryPatchSrfFSingleTrial(datContent, f, out trialDat, out _);

        var femResult = await runner.RunFemCppFromDatContentAsync(
                program, trialDat, trialBase, workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        return BuildP64TrialOutcome(
            femResult.JsonResult,
            femResult.ResSummary.Trials.FirstOrDefault(),
            f,
            iterationLimit);
    }

    internal static TrialOutcome BuildP64TrialOutcome(
        P610T3RunResult result,
        P64ResFileParser.SrfTrialRow? resRow,
        double f,
        int iterationLimit)
    {
        var row = resRow
            ?? new P64ResFileParser.SrfTrialRow
            {
                Srf = f,
                MaxDisplacement = double.NaN,
                Iterations = iterationLimit,
            };

        var stage = result.Stages.Count > 0
            ? result.Stages[PickPostGravityStageIndex(result)]
            : new P610T3StageSnapshot { Index = 0, Label = $"srf_{f:G2}" };

        double stageUmax = ComputeMaxDisplacement(stage.DisplacementsByNodeIndex);
        double umax = row.MaxDisplacement;
        bool sawNonFinite = !double.IsFinite(row.MaxDisplacement);

        if (!double.IsFinite(stageUmax))
            sawNonFinite = true;
        else if (!double.IsFinite(umax) || stageUmax > umax)
            umax = stageUmax;

        if (result.Meta?.SrfMaxDisplacement is { } metaDisp)
        {
            if (!double.IsFinite(metaDisp))
                sawNonFinite = true;
            else if (!double.IsFinite(umax) || metaDisp > umax)
                umax = metaDisp;
        }

        int iters = row.Iterations;
        bool metaMarksFailure = result.Meta?.SrfAllIncrementsConverged == false;
        if (resRow != null && row.Iterations > 0 && !metaMarksFailure)
        {
            iters = row.Iterations;
        }
        else if (result.Meta?.SrfMaxItersUsed is { } metaIters && metaIters > 0)
        {
            iters = Math.Max(iters, (int)Math.Round(metaIters));
        }

        bool converged = ClassifyTrialConverged(
            iters,
            iterationLimit,
            umax,
            sawNonFinite,
            result.Meta?.SrfAllIncrementsConverged,
            result.Meta?.SrfMaxYieldRatio);

        return new TrialOutcome
        {
            JsonResult = result,
            Stage = stage,
            TrialRow = new P64ResFileParser.SrfTrialRow
            {
                Srf = f,
                MaxDisplacement = umax,
                Iterations = iters,
            },
            Converged = converged,
            HasNonFiniteDisplacement = sawNonFinite || !double.IsFinite(umax),
        };
    }

    /// <summary>
    /// Shared SRF pass/fail gate: iteration limit, finite displacement, solver
    /// increment flag, and max Mohr–Coulomb yield ratio.
    /// </summary>
    public static bool ClassifyTrialConverged(
        int itersUsed,
        int iterationLimit,
        double maxDisplacement,
        bool sawNonFinite,
        bool? allIncrementsConverged,
        double? maxYieldRatio,
        double maxStableYieldRatio = MaxStableYieldRatio)
    {
        bool converged = itersUsed < iterationLimit
            && double.IsFinite(maxDisplacement)
            && !sawNonFinite;
        if (allIncrementsConverged == false)
            converged = false;
        if (maxYieldRatio is { } yld && yld > maxStableYieldRatio)
            converged = false;
        return converged;
    }

    internal static int PickPostGravityStageIndex(P610T3RunResult result)
    {
        if (result.Stages.Count == 0)
            return 0;

        int bestIdx = -1;
        double bestU = 0;
        for (int i = 0; i < result.Stages.Count; i++)
        {
            var label = result.Stages[i].Label ?? "";
            if (IsInSituReferenceStage(label))
                continue;

            double u = ComputeMaxDisplacement(result.Stages[i].DisplacementsByNodeIndex);
            if (bestIdx < 0 || u > bestU)
            {
                bestU = u;
                bestIdx = i;
            }
        }

        if (bestIdx >= 0 && bestU > 0)
            return bestIdx;

        for (int i = result.Stages.Count - 1; i >= 0; i--)
        {
            var label = result.Stages[i].Label ?? "";
            if (label.Contains("excavation", StringComparison.OrdinalIgnoreCase)
                || label.Contains("after_", StringComparison.OrdinalIgnoreCase)
                || label.Contains("elastic_solution", StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return result.Stages.Count > 1 ? result.Stages.Count - 1 : 0;
    }

    private static P64ResFileParser.SrfTrialRow? TryRecoverTrialRowFromJsonStage(P610T3RunResult result, double f)
    {
        if (result.Stages.Count == 0)
            return null;

        var stage = result.Stages[PickPostGravityStageIndex(result)];
        int iters = 0;
        if (!string.IsNullOrWhiteSpace(stage.Subtitle))
        {
            var match = Regex.Match(stage.Subtitle, @"iters\s*=\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
                int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out iters);
        }

        double umax = ComputeMaxDisplacement(stage.DisplacementsByNodeIndex);
        if (iters <= 0 && !double.IsFinite(umax))
            return null;

        return new P64ResFileParser.SrfTrialRow
        {
            Srf = f,
            MaxDisplacement = umax,
            Iterations = iters > 0 ? iters : 0,
        };
    }

    private static bool IsInSituReferenceStage(string label)
        => label.Contains("in_situ", StringComparison.OrdinalIgnoreCase)
           || label.Contains("reference_zero", StringComparison.OrdinalIgnoreCase);

    internal static double ComputeMaxDisplacement(IReadOnlyList<(double ux, double uy)>? disp)
    {
        if (disp == null || disp.Count == 0)
            return 0.0;

        double umax = 0.0;
        for (int i = 0; i < disp.Count; i++)
        {
            var t = disp[i];
            if (!double.IsFinite(t.ux) || !double.IsFinite(t.uy))
                return double.NaN;
            umax = Math.Max(umax, Math.Sqrt(t.ux * t.ux + t.uy * t.uy));
        }
        return umax;
    }

    internal static int CountNonFiniteDisplacements(IReadOnlyList<(double ux, double uy)>? disp)
    {
        if (disp == null) return 0;
        int n = 0;
        for (int i = 0; i < disp.Count; i++)
        {
            if (!double.IsFinite(disp[i].ux) || !double.IsFinite(disp[i].uy))
                n++;
        }
        return n;
    }
}
