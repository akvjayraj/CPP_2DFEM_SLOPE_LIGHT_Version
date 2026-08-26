using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Runs multiple branded T3 slope SRF trials (<c>slope_stability_T3_SRF</c>) by patching
/// <c>srf_F</c> per trial and merging JSON stages for contour visualization. Fixed trial
/// list (sweep) mode — see <see cref="SlopeSrfBracketRunner"/> for the iterative bracket mode.
/// </summary>
public static class SlopeT3MultiSrfRunner
{
    public sealed class MultiSrfRunResult
    {
        public P610T3RunResult JsonResult { get; init; } = null!;
        public P64ResFileParser.P64ResSummary ResSummary { get; init; } = null!;
        public bool HasNonFiniteDisplacements { get; init; }
        public string? SolverHealthWarning { get; init; }
    }

    public static async Task<MultiSrfRunResult> RunTrialsAsync(
        CppAnalysisRunner runner,
        SlopeSrfTrialExecutor.SrfProgramKind kind,
        string program,
        string datContent,
        string baseName,
        IReadOnlyList<double> fValues,
        int iterationLimit,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default,
        string? brandedProgram = null)
    {
        if (fValues == null || fValues.Count == 0)
            throw new ArgumentException("At least one SRF trial value is required.", nameof(fValues));

        P610T3RunResult? meshTemplate = null;
        var mergedStages = new List<P610T3StageSnapshot>(fValues.Count);
        var trials = new List<P64ResFileParser.SrfTrialRow>(fValues.Count);
        bool anyNonFinite = false;

        for (int i = 0; i < fValues.Count; i++)
        {
            var f = fValues[i];
            var outcome = await SlopeSrfTrialExecutor.RunTrialAsync(
                    runner,
                    kind,
                    program,
                    datContent,
                    baseName,
                    f,
                    iterationLimit,
                    workingDirectory,
                    cancellationToken,
                    brandedProgram)
                .ConfigureAwait(false);

            meshTemplate ??= outcome.JsonResult;
            if (outcome.HasNonFiniteDisplacement)
                anyNonFinite = true;

            trials.Add(outcome.TrialRow);
            mergedStages.Add(new P610T3StageSnapshot
            {
                Index = i,
                Label = outcome.Stage.Label,
                Subtitle = outcome.Stage.Subtitle,
                DisplacementsByNodeIndex = outcome.Stage.DisplacementsByNodeIndex,
                ElementStresses = outcome.Stage.ElementStresses,
                ElementsSolid = outcome.Stage.ElementsSolid,
            });

            // #region agent log
            DebugAgentLog.Write("H3", "SlopeT3MultiSrfRunner.cs:RunTrialsAsync",
                "SRF trial displacement summary",
                new
                {
                    f,
                    stageLabel = outcome.Stage.Label,
                    converged = outcome.Converged,
                    umax = outcome.TrialRow.MaxDisplacement,
                    iters = outcome.TrialRow.Iterations,
                    nonFinite = outcome.HasNonFiniteDisplacement,
                    metaMaxDisp = outcome.JsonResult.Meta?.SrfMaxDisplacement,
                    metaMaxIters = outcome.JsonResult.Meta?.SrfMaxItersUsed,
                });
            // #endregion
        }

        var lastMeta = meshTemplate?.Meta;
        var merged = new P610T3RunResult
        {
            Nodes = meshTemplate!.Nodes,
            Elements = meshTemplate.Elements,
            Stages = mergedStages,
            Description =
                $"{program}: {fValues.Count} SRF trial(s); stages[] = post-gravity displacement/stress per SRF.",
            Meta = lastMeta,
        };

        return new MultiSrfRunResult
        {
            JsonResult = merged,
            ResSummary = new P64ResFileParser.P64ResSummary
            {
                Trials = trials,
                RawText = "SRF trials (T3 multi-run):",
            },
            HasNonFiniteDisplacements = anyNonFinite,
            SolverHealthWarning = anyNonFinite
                ? "Solver produced non-finite displacements (NaN/Inf in .res). The Force-Displacement plot may show zero because JSON masks failed values. Regenerate the mesh as 3-node triangles (not quads) and verify boundary conditions on the Boundary Conditions tab."
                : null,
        };
    }
}
