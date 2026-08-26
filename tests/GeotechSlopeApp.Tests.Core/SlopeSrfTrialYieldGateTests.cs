using GeotechSlopeApp.Backend;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core;

/// <summary>
/// Yield-ratio gate for SRF trial classification — rejects plastic runaway
/// even when the solver reports all_increments_converged=true (Example 16 wet).
/// </summary>
[TestFixture]
public class SlopeSrfTrialYieldGateTests
{
    [Test]
    public void ClassifyTrialConverged_AcceptsNearCriticalYieldBelowGate()
    {
        // Example 16 wet F=0.9: yield≈8.2, iters under limit
        Assert.That(
            SlopeSrfTrialExecutor.ClassifyTrialConverged(
                itersUsed: 116,
                iterationLimit: 1000,
                maxDisplacement: 0.25,
                sawNonFinite: false,
                allIncrementsConverged: true,
                maxYieldRatio: 8.2),
            Is.True);
    }

    [Test]
    public void ClassifyTrialConverged_RejectsRunawayYieldDespiteIncrementFlag()
    {
        // Example 16 wet F=1.0: yield≈48 with all_increments_converged=true
        Assert.That(
            SlopeSrfTrialExecutor.ClassifyTrialConverged(
                itersUsed: 134,
                iterationLimit: 1000,
                maxDisplacement: 0.28,
                sawNonFinite: false,
                allIncrementsConverged: true,
                maxYieldRatio: 47.9),
            Is.False);
    }

    [Test]
    public void ClassifyTrialConverged_RejectsIterationLimitHit()
    {
        Assert.That(
            SlopeSrfTrialExecutor.ClassifyTrialConverged(
                itersUsed: 1000,
                iterationLimit: 1000,
                maxDisplacement: 0.05,
                sawNonFinite: false,
                allIncrementsConverged: false,
                maxYieldRatio: 1.0),
            Is.False);
    }

    [Test]
    public void ClassifyTrialConverged_RespectsCustomYieldGate()
    {
        Assert.That(
            SlopeSrfTrialExecutor.ClassifyTrialConverged(
                itersUsed: 50,
                iterationLimit: 1000,
                maxDisplacement: 0.1,
                sawNonFinite: false,
                allIncrementsConverged: true,
                maxYieldRatio: 12.0,
                maxStableYieldRatio: 10.0),
            Is.False);

        Assert.That(
            SlopeSrfTrialExecutor.ClassifyTrialConverged(
                itersUsed: 50,
                iterationLimit: 1000,
                maxDisplacement: 0.1,
                sawNonFinite: false,
                allIncrementsConverged: true,
                maxYieldRatio: 12.0,
                maxStableYieldRatio: SlopeSrfTrialExecutor.MaxStableYieldRatio),
            Is.True);
    }

    [Test]
    public void BuildP64TrialOutcome_AppliesYieldGate()
    {
        var result = new P610T3RunResult
        {
            Stages = new[]
            {
                new P610T3StageSnapshot
                {
                    Index = 0,
                    Label = "after_gravity",
                    DisplacementsByNodeIndex = new[] { (0.01, 0.02) },
                },
            },
            Meta = new P610T3MetaBlock(
                Cx: null, Cy: null, A: null, SigmaV_kPa: null, K0: null,
                E_kPa: 1e5, Nu: 0.3, PhiDeg: 35, C_kPa: 9.8, PsiDeg: 0, Gamma: 20,
                SrfFoS: 1.0,
                SrfMaxItersUsed: 100,
                SrfMaxDisplacement: 0.2,
                SrfAllIncrementsConverged: true,
                SrfMaxYieldRatio: 48.0),
        };

        var outcome = SlopeSrfTrialExecutor.BuildP64TrialOutcome(
            result,
            new P64ResFileParser.SrfTrialRow { Srf = 1.0, MaxDisplacement = 0.2, Iterations = 100 },
            f: 1.0,
            iterationLimit: 1000);

        Assert.That(outcome.Converged, Is.False);
    }
}
