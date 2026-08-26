using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core;

/// <summary>
/// Dat round-trip tests for the SRF bracket AnalysisOptions keys: write via
/// <see cref="P610AnalysisOptionsHelper.TryPatchSrfBracketSettings"/>, read back via
/// <see cref="P610AnalysisOptionsHelper.TryParse"/>, and assert property equality.
/// </summary>
[TestFixture]
public class P610AnalysisOptionsBracketRoundTripTests
{
    private static string MinimalDatWithOptions(P610AnalysisOptions seed)
        => P610AnalysisOptionsHelper.BuildBlock(seed) + "\n\n1.0e-4  100  20  1\n";

    private static P610AnalysisOptions BaseOptions(string meshType = "T3", string srfMode = "") => new()
    {
        FailureCriterion = FailureCriterion.MohrCoulomb,
        MaterialType = MaterialType.Isotropic,
        MeshType = meshType,
        SrfMode = srfMode,
    };

    [Test]
    public void TryPatchSrfBracketSettings_AutoMode_RoundTripsAllFields()
    {
        var dat = MinimalDatWithOptions(BaseOptions());

        var ok = P610AnalysisOptionsHelper.TryPatchSrfBracketSettings(
            dat,
            auto: true,
            seed: 1.0,
            expansionFactor: 1.5,
            maxExpansionSteps: 10,
            fMin: 1.0,
            fMax: 1.5,
            bracketTol: 0.01,
            maxBracketIters: 20,
            out var patched,
            out var changed);

        Assert.That(ok, Is.True);
        Assert.That(changed, Is.True);

        Assert.That(P610AnalysisOptionsHelper.TryParse(patched, out var parsed), Is.True);
        Assert.That(parsed.SrfSearchMode, Is.EqualTo("bracket"));
        Assert.That(parsed.SrfBracketAuto, Is.True);
        Assert.That(parsed.SrfBracketSeed, Is.EqualTo(1.0));
        Assert.That(parsed.SrfBracketExpansionFactor, Is.EqualTo(1.5));
        Assert.That(parsed.SrfMaxExpansionSteps, Is.EqualTo(10));
        Assert.That(parsed.SrfFMin, Is.EqualTo(1.0));
        Assert.That(parsed.SrfFMax, Is.EqualTo(1.5));
        Assert.That(parsed.SrfBracketTol, Is.EqualTo(0.01));
        Assert.That(parsed.SrfMaxBracketIters, Is.EqualTo(20));
    }

    [Test]
    public void TryPatchSrfBracketSettings_ManualMode_RoundTripsFMinFMaxAndAutoFalse()
    {
        var dat = MinimalDatWithOptions(BaseOptions());

        P610AnalysisOptionsHelper.TryPatchSrfBracketSettings(
            dat,
            auto: false,
            seed: 1.0,
            expansionFactor: 1.5,
            maxExpansionSteps: 10,
            fMin: 1.05,
            fMax: 1.6,
            bracketTol: 0.005,
            maxBracketIters: 15,
            out var patched,
            out _);

        Assert.That(P610AnalysisOptionsHelper.TryParse(patched, out var parsed), Is.True);
        Assert.That(parsed.SrfBracketAuto, Is.False);
        Assert.That(parsed.SrfFMin, Is.EqualTo(1.05));
        Assert.That(parsed.SrfFMax, Is.EqualTo(1.6));
        Assert.That(parsed.SrfMaxBracketIters, Is.EqualTo(15));
    }

    [Test]
    public void TryPatchSrfSearchModeSweep_ResetsBackToSweepAfterBracket()
    {
        var dat = MinimalDatWithOptions(BaseOptions());
        P610AnalysisOptionsHelper.TryPatchSrfBracketSettings(
            dat, true, 1.0, 1.5, 10, 1.0, 1.5, 0.01, 20, out var bracketDat, out _);

        var ok = P610AnalysisOptionsHelper.TryPatchSrfSearchModeSweep(bracketDat, out var sweptDat, out var changed);

        Assert.That(ok, Is.True);
        Assert.That(changed, Is.True);
        Assert.That(P610AnalysisOptionsHelper.TryParse(sweptDat, out var parsed), Is.True);
        Assert.That(parsed.SrfSearchMode, Is.EqualTo("sweep"));
    }

    [Test]
    public void TryPatchSrfSearchModeSweep_NoOpWhenAlreadySweep()
    {
        var dat = MinimalDatWithOptions(BaseOptions());

        var ok = P610AnalysisOptionsHelper.TryPatchSrfSearchModeSweep(dat, out var patched, out var changed);

        Assert.That(ok, Is.True);
        Assert.That(changed, Is.False);
        Assert.That(patched, Is.EqualTo(dat));
    }

    [Test]
    public void TryPatchSrfFSingleTrial_ForcesSingleModeRegardlessOfExistingMode()
    {
        var dat = MinimalDatWithOptions(BaseOptions(meshType: "Q8", srfMode: "multi"));

        var ok = P610AnalysisOptionsHelper.TryPatchSrfFSingleTrial(dat, 1.234, out var patched, out var changed);

        Assert.That(ok, Is.True);
        Assert.That(changed, Is.True);
        Assert.That(P610AnalysisOptionsHelper.TryParse(patched, out var parsed), Is.True);
        Assert.That(parsed.SrfMode, Is.EqualTo("single"));
        Assert.That(parsed.SrfF, Is.EqualTo(1.234));
    }

    [Test]
    public void TryPatchSrfFSingleTrial_PreservesPseudoStaticKhKv()
    {
        // Guards the regression where SRF trial AnalysisOptions rebuild stripped ! k_h / ! k_v,
        // so seismic FoS matched the dry (non-seismic) result.
        var seed = BaseOptions(meshType: "T3", srfMode: "multi") with
        {
            Kh = 0.7,
            Kv = 0.0,
        };
        var dat = MinimalDatWithOptions(seed);

        var ok = P610AnalysisOptionsHelper.TryPatchSrfFSingleTrial(dat, 1.25, out var patched, out var changed);

        Assert.That(ok, Is.True);
        Assert.That(changed, Is.True);
        Assert.That(patched, Does.Contain("! k_h 0.7"));
        Assert.That(patched, Does.Contain("! k_v 0"));
        Assert.That(P610AnalysisOptionsHelper.TryParse(patched, out var parsed), Is.True);
        Assert.That(parsed.SrfMode, Is.EqualTo("single"));
        Assert.That(parsed.SrfF, Is.EqualTo(1.25));
        Assert.That(parsed.Kh, Is.EqualTo(0.7));
        Assert.That(parsed.Kv, Is.EqualTo(0.0));
    }

    [Test]
    public void PrepareDatForNativeSlopeSrfExecutable_PreservesPseudoStaticKhKv()
    {
        var seed = BaseOptions(meshType: "T3", srfMode: "single") with
        {
            SrfF = 1.0,
            PreferredExecutable = "slope_stability_T3_SRF",
            Kh = 0.7,
            Kv = 0.05,
        };
        var dat = MinimalDatWithOptions(seed) + "\n! layers\n1\n0\n";

        var prepared = P610AnalysisOptionsHelper.PrepareDatForNativeSlopeSrfExecutable(
            dat, "slope_stability_T3_SRF");

        Assert.That(P610AnalysisOptionsHelper.TryParse(prepared, out var parsed), Is.True);
        Assert.That(parsed.Kh, Is.EqualTo(0.7));
        Assert.That(parsed.Kv, Is.EqualTo(0.05));
    }

    [Test]
    public void TryPatchSrfBracketSettings_ReturnsFalse_WhenNoAnalysisOptionsBlockPresent()
    {
        const string datWithoutOptionsBlock = "1.0e-4  100  20  1\n";

        var ok = P610AnalysisOptionsHelper.TryPatchSrfBracketSettings(
            datWithoutOptionsBlock, true, 1.0, 1.5, 10, 1.0, 1.5, 0.01, 20, out var patched, out var changed);

        Assert.That(ok, Is.False);
        Assert.That(changed, Is.False);
    }
}
