using GeotechSlopeApp.Backend;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Backend;

[TestFixture]
public class SlopeSrfTrialExecutorTests
{
    [Test]
    public void BuildP64TrialOutcome_MetaMarksIncrementFailure_OverridesStableResRow()
    {
        var result = new P610T3RunResult
        {
            Stages =
            [
                new P610T3StageSnapshot
                {
                    Index = 0,
                    Label = "srf_1.5",
                    DisplacementsByNodeIndex = [(0.0, 0.0), (0.03, 0.04)],
                }
            ],
            Meta = new P610T3MetaBlock(
                Cx: null,
                Cy: null,
                A: null,
                SigmaV_kPa: null,
                K0: null,
                E_kPa: null,
                Nu: null,
                PhiDeg: null,
                C_kPa: null,
                PsiDeg: null,
                Gamma: null,
                SrfFoS: null,
                SrfMaxItersUsed: 125,
                SrfMaxDisplacement: 0.08,
                SrfAllIncrementsConverged: false),
        };

        var outcome = SlopeSrfTrialExecutor.BuildP64TrialOutcome(
            result,
            new P64ResFileParser.SrfTrialRow
            {
                Srf = 1.5,
                MaxDisplacement = 1.0e-3,
                Iterations = 12,
            },
            f: 1.5,
            iterationLimit: 1000);

        Assert.That(outcome.Converged, Is.False);
        Assert.That(outcome.HasNonFiniteDisplacement, Is.False);
        Assert.That(outcome.TrialRow.Srf, Is.EqualTo(1.5).Within(1e-12));
        Assert.That(outcome.TrialRow.Iterations, Is.EqualTo(125));
        Assert.That(outcome.TrialRow.MaxDisplacement, Is.EqualTo(0.08).Within(1e-12));
    }

    [Test]
    public void BuildP64TrialOutcome_MetaIterationLimit_OverridesStableResRow()
    {
        var result = new P610T3RunResult
        {
            Stages =
            [
                new P610T3StageSnapshot
                {
                    Index = 0,
                    Label = "srf_2.0",
                    DisplacementsByNodeIndex = [(0.0, 0.0), (0.01, 0.02)],
                }
            ],
            Meta = new P610T3MetaBlock(
                Cx: null,
                Cy: null,
                A: null,
                SigmaV_kPa: null,
                K0: null,
                E_kPa: null,
                Nu: null,
                PhiDeg: null,
                C_kPa: null,
                PsiDeg: null,
                Gamma: null,
                SrfFoS: null,
                SrfMaxItersUsed: 1000,
                SrfMaxDisplacement: 0.02,
                SrfAllIncrementsConverged: true),
        };

        var outcome = SlopeSrfTrialExecutor.BuildP64TrialOutcome(
            result,
            new P64ResFileParser.SrfTrialRow
            {
                Srf = 2.0,
                MaxDisplacement = 1.0e-4,
                Iterations = 10,
            },
            f: 2.0,
            iterationLimit: 1000);

        Assert.That(outcome.Converged, Is.True);
        Assert.That(outcome.TrialRow.Iterations, Is.EqualTo(10));
        Assert.That(outcome.TrialRow.MaxDisplacement, Is.EqualTo(0.022360679774997897d).Within(1e-12));
    }

    [Test]
    public void BuildP64TrialOutcome_NonFiniteStageDisplacement_FailsTrial()
    {
        var result = new P610T3RunResult
        {
            Stages =
            [
                new P610T3StageSnapshot
                {
                    Index = 0,
                    Label = "srf_1.2",
                    DisplacementsByNodeIndex = [(0.0, 0.0), (double.NaN, 0.1)],
                }
            ],
        };

        var outcome = SlopeSrfTrialExecutor.BuildP64TrialOutcome(
            result,
            new P64ResFileParser.SrfTrialRow
            {
                Srf = 1.2,
                MaxDisplacement = 0.03,
                Iterations = 12,
            },
            f: 1.2,
            iterationLimit: 1000);

        Assert.That(outcome.Converged, Is.False);
        Assert.That(outcome.HasNonFiniteDisplacement, Is.True);
    }
}
