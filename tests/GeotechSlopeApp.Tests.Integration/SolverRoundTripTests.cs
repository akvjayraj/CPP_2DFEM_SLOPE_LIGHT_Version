using System;
using System.IO;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Tests.Integration.Support;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Integration;

/// <summary>
/// End-to-end solver round-trip: write a .dat, run the native C++ solver executable, parse the .res.
/// Marked [Explicit] + Category("Integration") because it shells out to a built solver binary that is
/// not present in every environment. The test self-Ignores when the solver exe or fixture is missing,
/// so it never produces a spurious failure on a machine without the C++ build.
/// </summary>
[TestFixture]
[Category("Integration")]
[Explicit("Runs the native C++ solver; requires a built p41/fem_cpp executable.")]
public class SolverRoundTripTests
{
    [Test]
    public void P41_RodDeck_RunsAndParsesResults()
    {
        var datPath = RepoFixtures.RequireFile(Path.Combine("chap04", "p41_1.dat"));
        var datContent = File.ReadAllText(datPath);

        var workDir = Path.Combine(Path.GetTempPath(), "GeoSolverRoundTrip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        var runner = new CppAnalysisRunner();

        P41ResFileParser.P41ParseResult result;
        try
        {
            result = runner.RunP41(datContent, baseName: "p41_roundtrip", workingDirectory: workDir);
        }
        catch (FileNotFoundException ex)
        {
            Assert.Ignore($"Solver executable not available: {ex.Message}");
            return;
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* best-effort */ }
        }

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Nodes.Count, Is.GreaterThan(0), "solver should yield node displacements");
        Assert.That(result.EquationCount, Is.GreaterThan(0));
    }
}
