using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core;

/// <summary>
/// End-to-end .dat round-trip tests for SRF Bracket Mode: configure a <see cref="FEAnalysisViewModel"/>,
/// export via <see cref="DatSolverControlExporter"/>, then re-import a fresh view model via
/// <see cref="DatSolverControlImporter"/> and assert the bracket settings survive the trip.
/// </summary>
[TestFixture]
public class SrfBracketDatRoundTripTests
{
    private static string BuildMinimalSrfT3Dat()
    {
        var options = new P610AnalysisOptions
        {
            FailureCriterion = FailureCriterion.MohrCoulomb,
            MaterialType = MaterialType.Isotropic,
            MeshType = "T3",
            PreferredExecutable = P610AnalysisOptionsHelper.FormatSlopeSrfExecutableName("T3", FailureCriterion.MohrCoulomb),
        };
        var block = P610AnalysisOptionsHelper.BuildBlock(options);
        return block + "\n\n! <BEGIN:SolverControl>\n! tol  limit  incs  layers\n1e-4  500  20  1\n! <END:SolverControl>\n";
    }

    private static FEAnalysisViewModel NewFeVm() => new(new ResultsViewModel());

    [Test]
    public void ExportThenImport_AutoDetectBracketSettings_RoundTrip()
    {
        var dat = BuildMinimalSrfT3Dat();
        var feVm = NewFeVm();
        feVm.SelectedSrfSearchMode = SrfSearchMode.Bracket;
        feVm.AutoDetectSrfBracket = true;
        feVm.SrfBracketSeed = 1.0;
        feVm.SrfBracketExpansionFactor = 1.5;
        feVm.SrfMaxExpansionSteps = 8;
        feVm.SrfBracketTolerance = 0.02;
        feVm.SrfMaxBracketIterations = 15;
        feVm.SetDetectedSrfBracketPreview(1.0, 2.25);

        Assert.That(DatSolverControlExporter.TryPatchSrfSolverControl(dat, feVm, out var exported, out var changed), Is.True);
        Assert.That(changed, Is.True);

        var feVm2 = NewFeVm();
        Assert.That(DatSolverControlImporter.TryImportToFeAnalysis(exported, feVm2, srfAnalysisMode: true), Is.True);

        Assert.That(feVm2.SelectedSrfSearchMode, Is.EqualTo(SrfSearchMode.Bracket));
        Assert.That(feVm2.AutoDetectSrfBracket, Is.True);
        Assert.That(feVm2.SrfBracketSeed, Is.EqualTo(1.0));
        Assert.That(feVm2.SrfBracketExpansionFactor, Is.EqualTo(1.5));
        Assert.That(feVm2.SrfMaxExpansionSteps, Is.EqualTo(8));
        Assert.That(feVm2.SrfBracketTolerance, Is.EqualTo(0.02));
        Assert.That(feVm2.SrfMaxBracketIterations, Is.EqualTo(15));
        Assert.That(feVm2.SrfFoSMin, Is.EqualTo(1.0));
        Assert.That(feVm2.SrfFoSMax, Is.EqualTo(2.25));
    }

    [Test]
    public void ExportThenImport_ManualBracketSettings_RoundTrip()
    {
        var dat = BuildMinimalSrfT3Dat();
        var feVm = NewFeVm();
        feVm.SelectedSrfSearchMode = SrfSearchMode.Bracket;
        feVm.AutoDetectSrfBracket = false;
        feVm.SrfFoSMin = 1.05;
        feVm.SrfFoSMax = 1.65;
        feVm.SrfBracketTolerance = 0.005;
        feVm.SrfMaxBracketIterations = 25;

        Assert.That(DatSolverControlExporter.TryPatchSrfSolverControl(dat, feVm, out var exported, out _), Is.True);

        var feVm2 = NewFeVm();
        Assert.That(DatSolverControlImporter.TryImportToFeAnalysis(exported, feVm2, srfAnalysisMode: true), Is.True);

        Assert.That(feVm2.SelectedSrfSearchMode, Is.EqualTo(SrfSearchMode.Bracket));
        Assert.That(feVm2.AutoDetectSrfBracket, Is.False);
        Assert.That(feVm2.SrfFoSMin, Is.EqualTo(1.05));
        Assert.That(feVm2.SrfFoSMax, Is.EqualTo(1.65));
        Assert.That(feVm2.SrfBracketTolerance, Is.EqualTo(0.005));
        Assert.That(feVm2.SrfMaxBracketIterations, Is.EqualTo(25));
    }

    [Test]
    public void ExportThenImport_SweepMode_DoesNotSetBracketSearchMode()
    {
        var dat = BuildMinimalSrfT3Dat();
        var feVm = NewFeVm();
        feVm.SelectedSrfSearchMode = SrfSearchMode.Sweep;
        feVm.SrfTrialCount = 3;

        Assert.That(DatSolverControlExporter.TryPatchSrfSolverControl(dat, feVm, out var exported, out _), Is.True);

        var feVm2 = NewFeVm();
        Assert.That(DatSolverControlImporter.TryImportToFeAnalysis(exported, feVm2, srfAnalysisMode: true), Is.True);

        Assert.That(feVm2.SelectedSrfSearchMode, Is.EqualTo(SrfSearchMode.Sweep));
        Assert.That(feVm2.IsSrfBracketMode, Is.False);
    }

    [Test]
    public void ExportBracketThenSwitchToSweep_PatchedTrialSrfF_PreservesBracketMetadataUntilSweepPatch()
    {
        // Guards against a regression where patching ! srf_F for the first sweep trial silently
        // wiped the previously-persisted bracket keys (SrfSearchMode/SrfBracketAuto/etc.) because
        // the AnalysisOptions rebuild in TryPatchSrfF/TryPatchSrfFSingleTrial did not carry them
        // through. Sweep-mode export explicitly resets srf_search_mode to "sweep" via
        // TryPatchSrfSearchModeSweep, so after switching to Sweep the mode should read back as Sweep.
        var dat = BuildMinimalSrfT3Dat();
        var bracketVm = NewFeVm();
        bracketVm.SelectedSrfSearchMode = SrfSearchMode.Bracket;
        bracketVm.AutoDetectSrfBracket = true;
        bracketVm.SrfBracketSeed = 1.2;
        bracketVm.SrfBracketExpansionFactor = 1.4;
        bracketVm.SrfMaxExpansionSteps = 6;
        bracketVm.SrfBracketTolerance = 0.01;
        bracketVm.SrfMaxBracketIterations = 20;

        Assert.That(DatSolverControlExporter.TryPatchSrfSolverControl(dat, bracketVm, out var bracketDat, out _), Is.True);

        var sweepVm = NewFeVm();
        sweepVm.SelectedSrfSearchMode = SrfSearchMode.Sweep;
        sweepVm.SrfTrialCount = 2;

        Assert.That(DatSolverControlExporter.TryPatchSrfSolverControl(bracketDat, sweepVm, out var sweptDat, out _), Is.True);

        var readBack = NewFeVm();
        Assert.That(DatSolverControlImporter.TryImportToFeAnalysis(sweptDat, readBack, srfAnalysisMode: true), Is.True);
        Assert.That(readBack.SelectedSrfSearchMode, Is.EqualTo(SrfSearchMode.Sweep));
    }

    [Test]
    public void TryPatchSrfF_PreservesBracketKeys_WhenAppliedOnTopOfBracketSettings()
    {
        // Regression coverage for the underlying helper directly: TryPatchSrfF (used for
        // single-trial patches during a bracket run) must not drop the bracket AnalysisOptions
        // keys that TryPatchSrfBracketSettings just wrote, otherwise a saved/reloaded bracket
        // deck would silently revert to no search mode after any trial patch.
        var dat = BuildMinimalSrfT3Dat();

        Assert.That(P610AnalysisOptionsHelper.TryPatchSrfBracketSettings(
            dat, auto: true, seed: 1.0, expansionFactor: 1.5, maxExpansionSteps: 10,
            fMin: 1.0, fMax: 1.5, bracketTol: 0.01, maxBracketIters: 20,
            out var bracketDat, out _), Is.True);

        Assert.That(P610AnalysisOptionsHelper.TryPatchSrfF(bracketDat, 1.234, out var patched, out _), Is.True);

        Assert.That(P610AnalysisOptionsHelper.TryParse(patched, out var parsed), Is.True);
        Assert.That(parsed.SrfF, Is.EqualTo(1.234));
        Assert.That(parsed.SrfSearchMode, Is.EqualTo("bracket"));
        Assert.That(parsed.SrfBracketAuto, Is.True);
        Assert.That(parsed.SrfFMin, Is.EqualTo(1.0));
        Assert.That(parsed.SrfFMax, Is.EqualTo(1.5));
    }
}
