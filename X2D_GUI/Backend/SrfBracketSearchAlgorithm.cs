using System;
using System.Threading.Tasks;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Pure SRF bracket search algorithm (auto-expansion + bisection), decoupled from the solver
/// invocation so it can be unit tested with mocked convergence sequences. <see cref="SlopeSrfBracketRunner"/>
/// wires this against real solver trials via <see cref="SlopeSrfTrialExecutor"/>.
/// </summary>
public static class SrfBracketSearchAlgorithm
{
    /// <summary>
    /// Phase 0: expand outward from <paramref name="seed"/> until a converged/failed straddle is
    /// found. Searches upward when the seed converges, downward when it fails. Returns the
    /// detected [F_lo, F_hi] bracket (F_lo always the converged side) and the number of trials run.
    /// </summary>
    /// <param name="runTrial">Runs one trial at the given F and returns true if it converged.</param>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="maxExpansionSteps"/> is exhausted without finding a straddle.</exception>
    public static async Task<(double fLo, double fHi, int trialCount)> AutoDetectBracketAsync(
        double seed,
        double expansionFactor,
        int maxExpansionSteps,
        Func<double, Task<bool>> runTrial,
        Action<string>? onProgress = null)
    {
        if (seed <= 0) throw new ArgumentOutOfRangeException(nameof(seed), "Starting FoS guess must be greater than 0.");
        if (expansionFactor <= 1.0) throw new ArgumentOutOfRangeException(nameof(expansionFactor), "Expansion factor must be greater than 1.0.");
        if (maxExpansionSteps < 1) throw new ArgumentOutOfRangeException(nameof(maxExpansionSteps), "Max expansion steps must be at least 1.");

        int trialCount = 0;
        onProgress?.Invoke($"SRF auto-bracket: probing seed F={Fmt(seed)}");
        bool seedConverged = await runTrial(seed).ConfigureAwait(false);
        trialCount++;

        if (seedConverged)
        {
            double prevF = seed;
            double f = seed;
            bool converged = true;
            int step = 0;
            while (converged)
            {
                if (step >= maxExpansionSteps)
                    throw new InvalidOperationException(
                        $"No failure detected up to F={Fmt(f)} after {maxExpansionSteps} expansion step(s) from seed {Fmt(seed)}. " +
                        "Increase the expansion factor/steps, or check materials/geometry.");

                prevF = f;
                f = seed * Math.Pow(expansionFactor, step + 1);
                onProgress?.Invoke($"SRF auto-bracket: expanding upward, F={Fmt(f)}");
                converged = await runTrial(f).ConfigureAwait(false);
                trialCount++;
                step++;
            }

            return (prevF, f, trialCount);
        }
        else
        {
            double prevF = seed;
            double f = seed;
            bool converged = false;
            int step = 0;
            while (!converged)
            {
                if (step >= maxExpansionSteps)
                    throw new InvalidOperationException(
                        $"No convergence detected down to F={Fmt(f)} after {maxExpansionSteps} expansion step(s) from seed {Fmt(seed)}. " +
                        "Decrease the seed, or check materials/geometry.");

                prevF = f;
                f = seed / Math.Pow(expansionFactor, step + 1);
                onProgress?.Invoke($"SRF auto-bracket: expanding downward, F={Fmt(f)}");
                converged = await runTrial(f).ConfigureAwait(false);
                trialCount++;
                step++;
            }

            return (f, prevF, trialCount);
        }
    }

    /// <summary>
    /// Phase 2: bisects [<paramref name="fLo"/>, <paramref name="fHi"/>] until the width falls
    /// below <paramref name="tolerance"/> or <paramref name="maxIterations"/> is reached.
    /// <paramref name="fLo"/> must be the converged side, <paramref name="fHi"/> the failed side.
    /// </summary>
    public static async Task<(double fLo, double fHi, int iterationCount)> BisectAsync(
        double fLo,
        double fHi,
        double tolerance,
        int maxIterations,
        Func<double, Task<bool>> runTrial,
        Action<int, double, bool, double, double>? onIteration = null)
    {
        if (tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance), "Bracket tolerance must be greater than 0.");
        if (maxIterations < 1) throw new ArgumentOutOfRangeException(nameof(maxIterations), "Max bracket iterations must be at least 1.");
        if (fHi <= fLo) throw new ArgumentException("F_hi must be greater than F_lo.", nameof(fHi));

        int iterations = 0;
        while (fHi - fLo >= tolerance && iterations < maxIterations)
        {
            double fMid = 0.5 * (fLo + fHi);
            bool converged = await runTrial(fMid).ConfigureAwait(false);
            iterations++;

            if (converged)
                fLo = fMid;
            else
                fHi = fMid;

            onIteration?.Invoke(iterations, fMid, converged, fLo, fHi);
        }

        return (fLo, fHi, iterations);
    }

    private static string Fmt(double v) => v.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
}
