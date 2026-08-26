using System.IO;
using GeotechSlopeApp.Backend;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Backend;

/// <summary>
/// Regression coverage for the SRF solver executable routing fix: dedicated subfolders
/// (<c>library\solver\SRF</c>, <c>library\solver\Structural-Static Elasticity</c>) must be
/// preferred over the flat <c>library\solver</c> root when a same-named executable exists in
/// both places, matching the documented intent in
/// <see cref="CppAnalysisRunner.GetExecutablePath"/>.
/// </summary>
[TestFixture]
public class CppAnalysisRunnerSolverRoutingTests
{
    [Test]
    public void GetExecutablePath_SlopeStabilitySrfProgram_ResolvesToSrfSubfolder()
    {
        var runner = new CppAnalysisRunner
        {
            ForceSourceSolverDirectory = true,
            ProgramName = "slope_stability_T3_SRF",
        };

        var path = runner.GetExecutablePath();

        Assert.That(File.Exists(path), Is.True, $"Resolved path does not exist: {path}");
        Assert.That(path, Does.Contain(Path.Combine("library", "solver", "SRF")),
            "slope_stability_*_SRF programs must resolve from library\\solver\\SRF, not the flat library\\solver root.");
    }

    [Test]
    public void GetExecutablePath_P610Program_ResolvesToStructuralStaticElasticitySubfolder()
    {
        var runner = new CppAnalysisRunner
        {
            ForceSourceSolverDirectory = true,
            ProgramName = "p610_t3_elastoplastic",
        };

        var path = runner.GetExecutablePath();

        Assert.That(File.Exists(path), Is.True, $"Resolved path does not exist: {path}");
        Assert.That(path, Does.Contain(Path.Combine("library", "solver", "Structural-Static Elasticity")),
            "p610_* programs must resolve from library\\solver\\Structural-Static Elasticity, not the flat library\\solver root.");
    }

    [Test]
    public void GetExecutablePath_SlopeStabilityQ8SrfProgram_ResolvesToSrfSubfolder()
    {
        var runner = new CppAnalysisRunner
        {
            ForceSourceSolverDirectory = true,
            ProgramName = "slope_stability_Q8_SRF",
        };

        var path = runner.GetExecutablePath();

        Assert.That(File.Exists(path), Is.True, $"Resolved path does not exist: {path}");
        Assert.That(path, Does.Contain(Path.Combine("library", "solver", "SRF")),
            "Q8 SRF programs must resolve from library\\solver\\SRF, not Q4-Q8 SRF.");
    }

    [Test]
    public void GetExecutablePath_SlopeStabilityQ4SrfProgram_ResolvesToSrfSubfolder()
    {
        var runner = new CppAnalysisRunner
        {
            ForceSourceSolverDirectory = true,
            ProgramName = "slope_stability_Q4_SRF",
        };

        var path = runner.GetExecutablePath();

        Assert.That(File.Exists(path), Is.True, $"Resolved path does not exist: {path}");
        Assert.That(path, Does.Contain(Path.Combine("library", "solver", "SRF")),
            "Q4 SRF programs must resolve from library\\solver\\SRF.");
    }

    [Test]
    public void GetExecutablePath_FemCpp_ResolvesToQ4Q8Subfolder()
    {
        var runner = new CppAnalysisRunner
        {
            ForceSourceSolverDirectory = true,
            ProgramName = "fem_cpp",
        };

        var path = runner.GetExecutablePath();

        Assert.That(File.Exists(path), Is.True, $"Resolved path does not exist: {path}");
        Assert.That(path, Does.Contain(Path.Combine("library", "solver", "Q4-Q8 SRF")),
            "fem_cpp must resolve from library\\solver\\Q4-Q8 SRF.");
        Assert.That(path, Does.Not.Contain(Path.Combine("solver", "SRF")));
    }

    [Test]
    public void GetExecutablePath_NonPreferredProgram_StillResolvesFromFlatRoot()
    {
        // Sanity check: programs with no dedicated subfolder mapping are unaffected by the fix.
        var runner = new CppAnalysisRunner
        {
            ForceSourceSolverDirectory = true,
            ProgramName = "p51",
        };

        var path = runner.GetExecutablePath();

        Assert.That(File.Exists(path), Is.True, $"Resolved path does not exist: {path}");
        Assert.That(path, Does.Not.Contain("Structural-Static Elasticity"));
        Assert.That(path, Does.Not.Contain(Path.Combine("solver", "SRF")));
        Assert.That(path, Does.Not.Contain("Q4-Q8 SRF"));
    }
}
