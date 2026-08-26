using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GeotechSlopeApp.Backend;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core;

/// <summary>
/// Unit tests for <see cref="SrfBracketSearchAlgorithm"/> using mocked convergence sequences —
/// no solver process is spawned, so these are fast, deterministic, and CI-safe.
/// </summary>
[TestFixture]
public class SrfBracketSearchAlgorithmTests
{
    /// <summary>Simulates a slope whose SRF trials converge for F &lt;= <paramref name="criticalFoS"/> and fail above it.</summary>
    private static Func<double, Task<bool>> MockSlope(double criticalFoS, List<double>? callLog = null)
        => f =>
        {
            callLog?.Add(f);
            return Task.FromResult(f <= criticalFoS);
        };

    [Test]
    public async Task AutoDetectBracket_SeedConverges_ExpandsUpwardAndStraddlesFailure()
    {
        var calls = new List<double>();
        var runTrial = MockSlope(criticalFoS: 1.32, calls);

        var (fLo, fHi, trialCount) = await SrfBracketSearchAlgorithm.AutoDetectBracketAsync(
            seed: 1.0, expansionFactor: 1.5, maxExpansionSteps: 10, runTrial);

        // Seed=1.0 converges (<=1.32); expand: 1.5^1=1.5 fails (>1.32) -> straddle [1.0, 1.5].
        Assert.That(fLo, Is.EqualTo(1.0).Within(1e-9));
        Assert.That(fHi, Is.EqualTo(1.5).Within(1e-9));
        Assert.That(trialCount, Is.EqualTo(2));
        Assert.That(calls, Is.EqualTo(new List<double> { 1.0, 1.5 }));
    }

    [Test]
    public async Task AutoDetectBracket_SeedFails_ExpandsDownwardAndStraddlesConvergence()
    {
        var calls = new List<double>();
        var runTrial = MockSlope(criticalFoS: 0.6, calls);

        var (fLo, fHi, trialCount) = await SrfBracketSearchAlgorithm.AutoDetectBracketAsync(
            seed: 1.0, expansionFactor: 2.0, maxExpansionSteps: 10, runTrial);

        // Seed=1.0 fails (>0.6); expand downward: 1.0/2^1=0.5 converges (<=0.6) -> straddle [0.5, 1.0].
        Assert.That(fLo, Is.EqualTo(0.5).Within(1e-9));
        Assert.That(fHi, Is.EqualTo(1.0).Within(1e-9));
        Assert.That(trialCount, Is.EqualTo(2));
        Assert.That(calls, Is.EqualTo(new List<double> { 1.0, 0.5 }));
    }

    [Test]
    public void AutoDetectBracket_ExhaustsSteps_ThrowsClearError()
    {
        // A slope that never fails within the searched range — expansion should give up cleanly.
        var runTrial = MockSlope(criticalFoS: 1000.0);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() =>
            SrfBracketSearchAlgorithm.AutoDetectBracketAsync(
                seed: 1.0, expansionFactor: 1.5, maxExpansionSteps: 3, runTrial));

        Assert.That(ex!.Message, Does.Contain("No failure detected"));
        Assert.That(ex.Message, Does.Contain("3 expansion step"));
    }

    [Test]
    public void AutoDetectBracket_ExhaustsStepsSearchingDownward_ThrowsClearError()
    {
        // A slope that never converges within the searched range going downward.
        var runTrial = MockSlope(criticalFoS: -1000.0);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() =>
            SrfBracketSearchAlgorithm.AutoDetectBracketAsync(
                seed: 1.0, expansionFactor: 1.5, maxExpansionSteps: 3, runTrial));

        Assert.That(ex!.Message, Does.Contain("No convergence detected"));
    }

    [TestCase(1.0, 2.0, 1.234, 0.01)]
    [TestCase(0.5, 4.0, 2.71, 0.001)]
    [TestCase(1.0, 1.6, 1.05, 0.01)]
    public async Task Bisect_ConvergesToCriticalFoSWithinTolerance(
        double fLo, double fHi, double criticalFoS, double tolerance)
    {
        var runTrial = MockSlope(criticalFoS);

        var (resultLo, resultHi, iterations) = await SrfBracketSearchAlgorithm.BisectAsync(
            fLo, fHi, tolerance, maxIterations: 50, runTrial);

        Assert.That(resultHi - resultLo, Is.LessThanOrEqualTo(tolerance));
        Assert.That(resultLo, Is.LessThanOrEqualTo(criticalFoS));
        Assert.That(resultHi, Is.GreaterThanOrEqualTo(criticalFoS));
        Assert.That(iterations, Is.GreaterThan(0));
    }

    [Test]
    public async Task Bisect_StopsAtMaxIterationsEvenIfToleranceNotMet()
    {
        var runTrial = MockSlope(criticalFoS: 1.234567);

        var (fLo, fHi, iterations) = await SrfBracketSearchAlgorithm.BisectAsync(
            1.0, 2.0, tolerance: 1e-9, maxIterations: 5, runTrial);

        Assert.That(iterations, Is.EqualTo(5));
        Assert.That(fHi - fLo, Is.GreaterThan(1e-9));
    }

    [Test]
    public async Task Bisect_LoConverged_HiFailed_NarrowsCorrectDirection()
    {
        var calls = new List<double>();
        var runTrial = MockSlope(criticalFoS: 1.25, calls);

        await SrfBracketSearchAlgorithm.BisectAsync(1.0, 1.5, 0.01, 20, runTrial);

        // First midpoint 1.25 converges (<=1.25) -> fLo becomes 1.25; every recorded call must be
        // consistent with monotonic convergence classification (mock is monotonic by construction).
        Assert.That(calls[0], Is.EqualTo(1.25).Within(1e-9));
    }

    [Test]
    public void AutoDetectBracket_InvalidSeed_ThrowsArgumentOutOfRange()
    {
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            SrfBracketSearchAlgorithm.AutoDetectBracketAsync(0.0, 1.5, 10, MockSlope(1.0)));
    }

    [Test]
    public void AutoDetectBracket_InvalidExpansionFactor_ThrowsArgumentOutOfRange()
    {
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            SrfBracketSearchAlgorithm.AutoDetectBracketAsync(1.0, 1.0, 10, MockSlope(1.0)));
    }

    [Test]
    public void Bisect_InvalidTolerance_ThrowsArgumentOutOfRange()
    {
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            SrfBracketSearchAlgorithm.BisectAsync(1.0, 1.5, 0.0, 20, MockSlope(1.25)));
    }

    [Test]
    public void Bisect_InvertedBracket_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(() =>
            SrfBracketSearchAlgorithm.BisectAsync(1.5, 1.0, 0.01, 20, MockSlope(1.25)));
    }
}
