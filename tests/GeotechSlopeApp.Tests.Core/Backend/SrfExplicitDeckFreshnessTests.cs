using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Backend;

[TestFixture]
public class SrfExplicitDeckFreshnessTests
{
    private static List<MeshInformationRow> CreateQ8Row() =>
        new()
        {
            new()
            {
                ElementId = 1,
                Kind = "Q8",
                Node1 = 1, N1X = 0, N1Y = 0,
                Node2 = 2, N2X = 2, N2Y = 0,
                Node3 = 3, N3X = 2, N3Y = 2,
                Node4 = 4, N4X = 0, N4Y = 2,
                Node5 = 5, N5X = 1, N5Y = 0,
                Node6 = 6, N6X = 2, N6Y = 1,
                Node7 = 7, N7X = 1, N7Y = 2,
                Node8 = 8, N8X = 0, N8Y = 1,
            }
        };

    private static BoundaryConditionsViewModel CreateCornerBcVm()
    {
        var bcVm = new BoundaryConditionsViewModel();
        bcVm.BoundaryConditions.Add(new BoundaryConditionRow { NodeId = 1, BCType = "Fixed", DegreeOfFreedom = 0 });
        bcVm.BoundaryConditions.Add(new BoundaryConditionRow { NodeId = 2, BCType = "Fixed", DegreeOfFreedom = 0 });
        bcVm.BoundaryConditions.Add(new BoundaryConditionRow { NodeId = 3, BCType = "Roller X", DegreeOfFreedom = 0 });
        bcVm.BoundaryConditions.Add(new BoundaryConditionRow { NodeId = 4, BCType = "Roller X", DegreeOfFreedom = 0 });
        return bcVm;
    }

    private static string BuildFencedBcsScript(IReadOnlyDictionary<int, (int FixX, int FixY)> fixMap, string solverControlLine)
    {
        var sb = new StringBuilder();
        sb.AppendLine("! <BEGIN:Bcs>");
        sb.AppendLine("! nr");
        sb.AppendLine(fixMap.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var (nodeId, (fx, fy)) in fixMap)
            sb.AppendLine(FormattableString.Invariant($"{nodeId}  {fx}  {fy}"));
        sb.AppendLine("! <END:Bcs>");
        sb.AppendLine();
        sb.AppendLine("! <BEGIN:SolverControl>");
        sb.AppendLine("! tol  limit  incs  layers");
        sb.AppendLine(solverControlLine);
        sb.AppendLine("! <END:SolverControl>");
        return sb.ToString();
    }

    [Test]
    public void HasStaleSolverTail_RejectsKnownBadQ8SolverControl()
    {
        Assert.That(SrfExplicitDeckFreshness.HasStaleSolverTail(20, 1), Is.True);
        Assert.That(SrfExplicitDeckFreshness.HasStaleSolverTail(1.0e-5, 1000), Is.False);
    }

    [Test]
    public void IsFixityFresh_RejectsCornerOnlyQ8Script()
    {
        var rows = CreateQ8Row();
        var bcVm = CreateCornerBcVm();
        var expected = SrfExplicitDeckFreshness.BuildExpectedFixMap(bcVm, rows, nn: 8);
        Assert.That(expected.Count, Is.EqualTo(8), "Expected export should include boundary midside fixities.");

        var staleCorners = new Dictionary<int, (int FixX, int FixY)>
        {
            [1] = (0, 0),
            [2] = (0, 0),
            [3] = (0, 1),
            [4] = (0, 1),
        };
        var script = BuildFencedBcsScript(staleCorners, "1E-05  1000  20  1");

        Assert.That(
            SrfExplicitDeckFreshness.IsFixityFresh(script, bcVm, rows, nn: 8),
            Is.False,
            "Corner-only BC blocks must be treated as stale on Q8 meshes.");
    }

    [Test]
    public void IsFixityFresh_AcceptsScriptMatchingExpectedQ8Fixities()
    {
        var rows = CreateQ8Row();
        var bcVm = CreateCornerBcVm();
        var expected = SrfExplicitDeckFreshness.BuildExpectedFixMap(bcVm, rows, nn: 8);
        var script = BuildFencedBcsScript(expected, "1E-05  1000  20  1");

        Assert.That(SrfExplicitDeckFreshness.IsFixityFresh(script, bcVm, rows, nn: 8), Is.True);
    }

    [Test]
    public void IsFixityFresh_RejectsCornerOnlyT6Script()
    {
        var rows = new List<MeshInformationRow>
        {
            new()
            {
                ElementId = 1,
                Kind = "T6",
                Node1 = 1, N1X = 0, N1Y = 0,
                Node2 = 2, N2X = 2, N2Y = 0,
                Node3 = 3, N3X = 1, N3Y = 2,
                Node4 = 4, N4X = 1, N4Y = 0,
                Node5 = 5, N5X = 1.5, N5Y = 1,
                Node6 = 6, N6X = 0.5, N6Y = 1,
            }
        };

        var bcVm = new BoundaryConditionsViewModel();
        bcVm.BoundaryConditions.Add(new BoundaryConditionRow { NodeId = 1, BCType = "Fixed", DegreeOfFreedom = 0 });
        bcVm.BoundaryConditions.Add(new BoundaryConditionRow { NodeId = 2, BCType = "Fixed", DegreeOfFreedom = 0 });
        bcVm.BoundaryConditions.Add(new BoundaryConditionRow { NodeId = 3, BCType = "Roller X", DegreeOfFreedom = 0 });

        var expected = SrfExplicitDeckFreshness.BuildExpectedFixMap(bcVm, rows, nn: 6);
        Assert.That(expected.Count, Is.GreaterThan(3), "T6 export should propagate midside boundary fixities.");

        var staleCorners = new Dictionary<int, (int FixX, int FixY)>
        {
            [1] = (0, 0),
            [2] = (0, 0),
            [3] = (0, 1),
        };
        var script = BuildFencedBcsScript(staleCorners, "1E-05  1000  20  1");

        Assert.That(
            SrfExplicitDeckFreshness.IsFixityFresh(script, bcVm, rows, nn: 6),
            Is.False,
            "Corner-only BC blocks must be treated as stale on T6 meshes.");
    }

    [Test]
    public void IsSolverTailFresh_RejectsStaleSolverControlBlock()
    {
        var script = BuildFencedBcsScript(
            new Dictionary<int, (int FixX, int FixY)> { [1] = (0, 0) },
            "20  1  20  1");

        Assert.That(SrfExplicitDeckFreshness.TryParseScriptSolverTail(script, out var tol, out var limit), Is.True);
        Assert.That(tol, Is.EqualTo(20).Within(1e-12));
        Assert.That(limit, Is.EqualTo(1));
        Assert.That(SrfExplicitDeckFreshness.IsSolverTailFresh(script, feVM: null), Is.False);
    }

    [Test]
    public void IsDeckFreshForPreserve_RejectsSavedGriffithsQ8ScriptSnippet()
    {
        var repoRoot = FindRepoRoot();
        var xfemPath = Path.Combine(
            repoRoot,
            "Verification",
            "slope",
            "Griffiths_Lane_Example1_2H1V",
            "griffiths_lane_example1_2h1v_Q8.xfem");
        Assume.That(File.Exists(xfemPath), Is.True, "Griffiths Q8 xfem fixture is required for this regression test.");

        var script = ExtractScriptContentFromXfem(xfemPath);
        Assume.That(script.Length, Is.GreaterThan(100));

        Assert.That(SrfExplicitDeckFreshness.TryParseScriptFixityMap(script, out var fixMap), Is.True);
        Assert.That(fixMap.Count, Is.EqualTo(43));
        Assert.That(SrfExplicitDeckFreshness.TryParseScriptSolverTail(script, out var tol, out var limit), Is.True);
        Assert.That(SrfExplicitDeckFreshness.HasStaleSolverTail(tol, limit), Is.True);
    }

    [Test]
    public void EnsureSaneSrfSolverSettings_ReplacesImportedBadValues()
    {
        var feVm = new FEAnalysisViewModel(new ResultsViewModel());
        feVm.ConvergenceTolerance = 20;
        feVm.MaxIterations = 1;

        SrfExplicitDeckFreshness.EnsureSaneSrfSolverSettings(feVm);

        Assert.That(feVm.ConvergenceTolerance, Is.EqualTo(SrfExplicitDeckFreshness.DefaultSrfTolerance).Within(1e-12));
        Assert.That(feVm.MaxIterations, Is.EqualTo(SrfExplicitDeckFreshness.DefaultSrfIterationLimit));
    }

    private static string FindRepoRoot()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, "Verification"))
                && Directory.Exists(Path.Combine(dir, "X2D_GUI")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }

    private static string ExtractScriptContentFromXfem(string xfemPath)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(xfemPath));
        if (doc.RootElement.TryGetProperty("script", out var script)
            && script.TryGetProperty("scriptContent", out var content))
            return content.GetString() ?? string.Empty;
        return string.Empty;
    }
}
