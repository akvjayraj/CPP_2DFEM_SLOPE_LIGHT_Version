using GeotechSlopeApp.Models;
using GeotechSlopeApp.Scripting;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Scripting;

[TestFixture]
public class FeProjectApiTests
{
    [Test]
    public void SetOutline_ReplacesAxisVertices()
    {
        var vm = new MainWindowViewModel();
        var api = new FeProjectApi(vm);

        api.SetOutline(new[] { (0.0, 0.0), (40.0, 0.0), (40.0, 10.0), (20.0, 20.0), (0.0, 20.0) });

        var outline = api.GetOutline();
        Assert.That(outline, Has.Count.EqualTo(5));
        Assert.That(outline[0].X, Is.EqualTo(0).Within(1e-9));
        Assert.That(outline[3].X, Is.EqualTo(20).Within(1e-9));
        Assert.That(outline[3].Y, Is.EqualTo(20).Within(1e-9));
        Assert.That(vm.GeometryDefinitionVM.IsOpenPolygonBoundaryMode, Is.False);
    }

    [Test]
    public void AddMaterial_AddsMohrCoulombAndAssignsLayer()
    {
        var vm = new MainWindowViewModel();
        var api = new FeProjectApi(vm);

        var id = api.AddMaterial("Clay", cKpa: 5, phiDeg: 20, eKpa: 1e5, nu: 0.3, gamma: 20);

        Assert.That(id, Is.GreaterThanOrEqualTo(1));
        Assert.That(api.MaterialCount, Is.GreaterThanOrEqualTo(1));
        var added = vm.MaterialPropertiesVM.Materials.First(m => m.Id == id);
        Assert.That(added.Name, Is.EqualTo("Clay"));
        Assert.That(added.Cohesion, Is.EqualTo(5).Within(1e-9));
        Assert.That(added.FrictionAngle, Is.EqualTo(20).Within(1e-9));
    }

    [Test]
    public void SetSsr_TurnsCategoryOnAndOff()
    {
        var vm = new MainWindowViewModel();
        var api = new FeProjectApi(vm);

        Assert.That(api.IsSsrOn(), Is.False);
        api.SetSsr(true);
        Assert.That(api.IsSsrOn(), Is.True);
        Assert.That(vm.MaterialPropertiesVM.SelectedAnalysisCategory,
            Is.EqualTo(MaterialPropertiesViewModel.SrfAnalysisCategory));
        Assert.That(vm.ProjectSettingsVM.IsCategoryOn(MaterialPropertiesViewModel.SrfAnalysisCategory), Is.True);

        api.SetSsr(false);
        Assert.That(api.IsSsrOn(), Is.False);
    }

    [Test]
    public void RunFe_WhenSsrOff_ThrowsInsteadOfDialog()
    {
        var vm = new MainWindowViewModel();
        var api = new FeProjectApi(vm);
        api.SetSsr(false);
        Assert.That(() => api.RunFe(TimeSpan.FromSeconds(1)), Throws.InvalidOperationException
            .With.Message.Contains("set_ssr"));
    }

    [Test]
    public void RpcDispatch_SetSsr_RoundTrips()
    {
        var vm = new MainWindowViewModel();
        var api = new FeProjectApi(vm);
        var host = new FeProjectRpcHost(api, "test-unused");
        var json = host.Dispatch("""{"id":1,"method":"fe.set_ssr","params":{"on":true}}""");
        Assert.That(json, Does.Contain("\"ok\":true"));
        Assert.That(api.IsSsrOn(), Is.True);
        json = host.Dispatch("""{"id":2,"method":"fe.ssr_on","params":{}}""");
        Assert.That(json, Does.Contain("true"));
    }

    [Test]
    public void RpcDispatch_SetOutline_RoundTrips()
    {
        var vm = new MainWindowViewModel();
        var api = new FeProjectApi(vm);
        var host = new FeProjectRpcHost(api, "test-unused");
        var json = host.Dispatch("""{"id":1,"method":"geometry.set_outline","params":{"points":[[0,0],[10,0],[10,10],[0,10]]}}""");
        Assert.That(json, Does.Contain("\"ok\":true"));
        Assert.That(api.GetOutline(), Has.Count.EqualTo(4));
    }

    [Test]
    public void DefaultPythonScript_CoversFullSlopeWorkflow()
    {
        var script = new MainWindowViewModel().ScriptVM.PythonScript;
        Assert.That(script, Does.Contain("set_outline"));
        Assert.That(script, Does.Contain("materials.add"));
        Assert.That(script, Does.Contain("mesh.generate"));
        Assert.That(script, Does.Contain("apply_slope_defaults"));
        Assert.That(script, Does.Contain("fe.run"));
        Assert.That(script, Does.Contain("set_ssr"));
    }

    [Test]
    public void ApplySlopeDefaults_WithoutMesh_Throws()
    {
        var vm = new MainWindowViewModel();
        var api = new FeProjectApi(vm);
        api.SetOutline(new[] { (0.0, 0.0), (40.0, 0.0), (40.0, 10.0), (20.0, 20.0), (0.0, 20.0) });
        Assert.That(() => api.ApplySlopeBoundaryDefaults(), Throws.InvalidOperationException
            .With.Message.Contains("mesh"));
    }

    [Test]
    public void ApplySlopeDefaults_AfterOutlineAndMesh_WritesLeftRightBaseCorners()
    {
        var vm = new MainWindowViewModel();
        var api = new FeProjectApi(vm);
        api.SetOutline(new[] { (0.0, 0.0), (40.0, 0.0), (40.0, 10.0), (20.0, 20.0), (0.0, 20.0) });
        SeedMinimalSlopeMesh(vm);

        var summary = api.ApplySlopeBoundaryDefaults();
        Assert.That(summary, Does.Contain("left").IgnoreCase);

        var rows = vm.BoundaryConditionsVM.BoundaryConditions.ToList();
        Assert.That(rows.Any(r => r.BCType == "Roller X" && r.BoundaryName == "left"), Is.True);
        Assert.That(rows.Any(r => r.BCType == "Roller X" && r.BoundaryName == "right"), Is.True);
        Assert.That(rows.Any(r => r.BCType == "Roller Y" && r.BoundaryName == "base"), Is.True);
        Assert.That(rows.Any(r => r.BCType == "Fixed" && r.BoundaryName == "corners"), Is.True);
    }

    [Test]
    public void RpcDispatch_ApplySlopeDefaults_RoundTrips()
    {
        var vm = new MainWindowViewModel();
        var api = new FeProjectApi(vm);
        api.SetOutline(new[] { (0.0, 0.0), (40.0, 0.0), (40.0, 10.0), (20.0, 20.0), (0.0, 20.0) });
        SeedMinimalSlopeMesh(vm);
        var host = new FeProjectRpcHost(api, "test-unused");
        var json = host.Dispatch("""{"id":1,"method":"bcs.apply_slope_defaults","params":{}}""");
        Assert.That(json, Does.Contain("\"ok\":true"));
        Assert.That(vm.BoundaryConditionsVM.BoundaryConditions, Has.Count.GreaterThan(0));
    }

    /// <summary>
    /// Coarse T3 cover of the 40×20 slope so Import Boundaries can match exterior nodes
    /// without running the RS2D mesher.
    /// </summary>
    private static void SeedMinimalSlopeMesh(MainWindowViewModel vm)
    {
        // 1 (0,0)  6 (20,0)  2 (40,0)  3 (40,10)  4 (20,20)  5 (0,20)
        vm.MeshVM.MeshInformationRows.Add(T3(1, 1, 6, 5, 0, 0, 20, 0, 0, 20));
        vm.MeshVM.MeshInformationRows.Add(T3(2, 6, 2, 3, 20, 0, 40, 0, 40, 10));
        vm.MeshVM.MeshInformationRows.Add(T3(3, 6, 3, 4, 20, 0, 40, 10, 20, 20));
        vm.MeshVM.MeshInformationRows.Add(T3(4, 6, 4, 5, 20, 0, 20, 20, 0, 20));
    }

    private static MeshInformationRow T3(
        int id, int n1, int n2, int n3,
        double x1, double y1, double x2, double y2, double x3, double y3) => new()
    {
        ElementId = id,
        Kind = "T3",
        Node1 = n1, N1X = x1, N1Y = y1,
        Node2 = n2, N2X = x2, N2Y = y2,
        Node3 = n3, N3X = x3, N3Y = y3,
    };
}
