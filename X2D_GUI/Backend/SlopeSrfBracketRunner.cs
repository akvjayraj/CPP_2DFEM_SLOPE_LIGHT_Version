using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Runs an iterative SRF bracket search: an optional auto-expansion phase (Phase 0) that
/// discovers a converged/failed straddle from a seed FoS guess, followed by bisection
/// (Phase 2) that narrows the bracket to a critical FoS within tolerance. Works for both the
/// branded T3/T6/Q4 solver family and p64/Q8 (single-trial-per-call in both cases), reusing
/// <see cref="SlopeSrfTrialExecutor"/> for the actual solver invocation + convergence
/// classification.
/// </summary>
public static class SlopeSrfBracketRunner
{
    /// <summary>User-configurable bracket search settings (mirrors <see cref="FEAnalysisViewModel"/> bracket properties).</summary>
    public sealed class BracketSettings
    {
        public bool AutoDetect { get; init; } = true;
        public double Seed { get; init; } = 1.0;
        public double ExpansionFactor { get; init; } = 1.5;
        public int MaxExpansionSteps { get; init; } = 10;
        public double ManualFMin { get; init; } = 1.0;
        public double ManualFMax { get; init; } = 1.5;
        public double Tolerance { get; init; } = 0.01;
        public int MaxBisectionIterations { get; init; } = 20;
    }

    public sealed class BracketRunResult
    {
        public P610T3RunResult JsonResult { get; init; } = null!;
        public P64ResFileParser.P64ResSummary ResSummary { get; init; } = null!;
        public double CriticalFoS { get; init; }
        public double BracketLow { get; init; }
        public double BracketHigh { get; init; }
        public int ExpansionTrialCount { get; init; }
        public int BisectionTrialCount { get; init; }
        public bool AutoDetected { get; init; }
        public bool HasNonFiniteDisplacements { get; init; }
        public string? SolverHealthWarning { get; init; }
        public string BracketSummary { get; init; } = "";
    }

    /// <summary>Progress callback invoked after each trial (expansion or bisection) with a human-readable status line.</summary>
    public delegate void ProgressCallback(string message);

    /// <summary>Reported once the auto-expansion phase completes, so the UI can show the detected bracket even before bisection finishes.</summary>
    public delegate void BracketDetectedCallback(double fMin, double fMax);

    public static async Task<BracketRunResult> RunAsync(
        CppAnalysisRunner runner,
        SlopeSrfTrialExecutor.SrfProgramKind kind,
        string program,
        string datContent,
        string baseName,
        BracketSettings settings,
        int iterationLimit,
        string? workingDirectory = null,
        ProgressCallback? onProgress = null,
        BracketDetectedCallback? onBracketDetected = null,
        CancellationToken cancellationToken = default,
        string? brandedProgram = null)
    {
        ValidateSettings(settings);

        P610T3RunResult? meshTemplate = null;
        var stages = new List<P610T3StageSnapshot>();
        var trials = new List<P64ResFileParser.SrfTrialRow>();
        bool anyNonFinite = false;
        // Caches trials by F so re-visiting a bracket endpoint (e.g. a manual F_min that also
        // happens to be re-probed) never re-runs the solver.
        var cache = new Dictionary<double, SlopeSrfTrialExecutor.TrialOutcome>();

        async Task<SlopeSrfTrialExecutor.TrialOutcome> RunAndRecordAsync(double f)
        {
            if (cache.TryGetValue(f, out var cached))
                return cached;

            var outcome = await SlopeSrfTrialExecutor.RunTrialAsync(
                    runner, kind, program, datContent, baseName, f, iterationLimit, workingDirectory, cancellationToken, brandedProgram)
                .ConfigureAwait(false);

            cache[f] = outcome;
            meshTemplate ??= outcome.JsonResult;
            if (outcome.HasNonFiniteDisplacement)
                anyNonFinite = true;

            trials.Add(outcome.TrialRow);
            stages.Add(new P610T3StageSnapshot
            {
                Index = stages.Count,
                Label = outcome.Stage.Label,
                Subtitle = outcome.Stage.Subtitle,
                DisplacementsByNodeIndex = outcome.Stage.DisplacementsByNodeIndex,
                ElementStresses = outcome.Stage.ElementStresses,
                ElementsSolid = outcome.Stage.ElementsSolid,
            });

            onProgress?.Invoke($"SRF trial F={f.ToString("G4", CultureInfo.InvariantCulture)}: {(outcome.Converged ? "converged" : "failed")}");
            return outcome;
        }

        async Task<bool> RunTrialConvergedAsync(double f) => (await RunAndRecordAsync(f).ConfigureAwait(false)).Converged;

        double fLo, fHi;
        int expansionCount;

        if (settings.AutoDetect)
        {
            (fLo, fHi, expansionCount) = await SrfBracketSearchAlgorithm.AutoDetectBracketAsync(
                    settings.Seed, settings.ExpansionFactor, settings.MaxExpansionSteps, RunTrialConvergedAsync,
                    onProgress is null ? null : msg => onProgress(msg))
                .ConfigureAwait(false);
            onBracketDetected?.Invoke(fLo, fHi);
        }
        else
        {
            fLo = settings.ManualFMin;
            fHi = settings.ManualFMax;
            expansionCount = 0;

            bool loConverged = await RunTrialConvergedAsync(fLo).ConfigureAwait(false);
            expansionCount++;
            if (!loConverged)
                throw new InvalidOperationException(
                    $"Manual F_min={fLo.ToString("G4", CultureInfo.InvariantCulture)} did not converge. " +
                    "Decrease F_min and try again — it must be a known-stable lower bound.");

            bool hiConverged = await RunTrialConvergedAsync(fHi).ConfigureAwait(false);
            expansionCount++;
            if (hiConverged)
                throw new InvalidOperationException(
                    $"Manual F_max={fHi.ToString("G4", CultureInfo.InvariantCulture)} converged (did not fail). " +
                    "Increase F_max and try again — it must be a known-unstable upper bound.");
        }

        var (bisectedLo, bisectedHi, bisectionCount) = await SrfBracketSearchAlgorithm.BisectAsync(
                fLo, fHi, settings.Tolerance, settings.MaxBisectionIterations, RunTrialConvergedAsync,
                onIteration: (iter, fMid, converged, curLo, curHi) => onProgress?.Invoke(
                    $"SRF bracket iter {iter}/{settings.MaxBisectionIterations}: " +
                    $"F={fMid.ToString("G4", CultureInfo.InvariantCulture)} ({(converged ? "converged" : "failed")}), " +
                    $"bracket=[{curLo.ToString("G4", CultureInfo.InvariantCulture)}, {curHi.ToString("G4", CultureInfo.InvariantCulture)}]"))
            .ConfigureAwait(false);
        fLo = bisectedLo;
        fHi = bisectedHi;

        double criticalFoS = 0.5 * (fLo + fHi);
        // Refine with the existing displacement-slope interpolation so bracket mode benefits
        // from the same precision as the post-hoc sweep estimator when trend data is available.
        if (P64ResFileParser.TryEstimateFoS(trials, iterationLimit, out var refinedFoS, out _))
            criticalFoS = refinedFoS;

        var lastMeta = meshTemplate?.Meta;
        var merged = new P610T3RunResult
        {
            Nodes = meshTemplate!.Nodes,
            Elements = meshTemplate.Elements,
            Stages = stages,
            Description =
                $"{program}: SRF bracket search ({expansionCount} expansion + {bisectionCount} bisection trial(s)); " +
                $"critical FoS \u2248 {criticalFoS.ToString("G4", CultureInfo.InvariantCulture)}.",
            Meta = lastMeta,
        };

        var summary =
            $"Critical FoS \u2248 {criticalFoS.ToString("G4", CultureInfo.InvariantCulture)} " +
            $"({(settings.AutoDetect ? "auto-detected" : "manual")} bracket in {expansionCount} expansion + {bisectionCount} bisection trial(s), " +
            $"F \u2208 [{fLo.ToString("G4", CultureInfo.InvariantCulture)}, {fHi.ToString("G4", CultureInfo.InvariantCulture)}])";

        return new BracketRunResult
        {
            JsonResult = merged,
            ResSummary = new P64ResFileParser.P64ResSummary
            {
                Trials = trials,
                RawText = "SRF trials (bracket search):",
            },
            CriticalFoS = criticalFoS,
            BracketLow = fLo,
            BracketHigh = fHi,
            ExpansionTrialCount = expansionCount,
            BisectionTrialCount = bisectionCount,
            AutoDetected = settings.AutoDetect,
            HasNonFiniteDisplacements = anyNonFinite,
            SolverHealthWarning = anyNonFinite
                ? "Solver produced non-finite displacements (NaN/Inf in .res) during the bracket search. Regenerate the mesh as 3-node triangles (not quads) and verify boundary conditions on the Boundary Conditions tab."
                : null,
            BracketSummary = summary,
        };
    }

    private static void ValidateSettings(BracketSettings settings)
    {
        if (settings.Tolerance <= 0)
            throw new ArgumentException("Bracket tolerance must be greater than 0.", nameof(settings));
        if (settings.MaxBisectionIterations < 1)
            throw new ArgumentException("Max bracket iterations must be at least 1.", nameof(settings));

        if (settings.AutoDetect)
        {
            if (settings.Seed <= 0)
                throw new ArgumentException("Starting FoS guess must be greater than 0.", nameof(settings));
            if (settings.ExpansionFactor <= 1.0)
                throw new ArgumentException("Expansion factor must be greater than 1.0.", nameof(settings));
            if (settings.MaxExpansionSteps < 1)
                throw new ArgumentException("Max expansion steps must be at least 1.", nameof(settings));
        }
        else
        {
            if (settings.ManualFMin <= 0)
                throw new ArgumentException("Minimum FoS (F_min) must be greater than 0.", nameof(settings));
            if (settings.ManualFMax <= settings.ManualFMin)
                throw new ArgumentException("Maximum FoS (F_max) must be greater than Minimum FoS (F_min).", nameof(settings));
        }
    }
}
