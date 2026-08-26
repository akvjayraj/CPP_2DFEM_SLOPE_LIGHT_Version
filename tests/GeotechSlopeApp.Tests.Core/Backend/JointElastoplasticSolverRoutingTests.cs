using System.IO;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Geometry;
using GeotechSlopeApp.Persistence;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Backend;

[TestFixture]
public class JointElastoplasticSolverRoutingTests
{
    [Test]
    public void HasMohrCoulombJointSlip_EnabledWeakPlane_IsTrue()
    {
        var geom = new GeometryDefinitionViewModel();
        geom.EnsureDefaultJointPropertySet();
        var set = geom.JointPropertySets[0];
        Assert.That(set.Criterion, Is.EqualTo(FeJointSlipCriterion.MohrCoulomb));

        geom.GeologicalFeatures.Add(new GeologicalFeature
        {
            Id = "wp-1",
            Kind = GeologicalFeatureKind.WeakPlane,
            Name = "Joint",
            PropertySetId = set.Id,
            Vertices = { (0, 10), (20, 10) }
        });

        Assert.That(geom.HasMohrCoulombJointSlip(), Is.True);
    }

    [Test]
    public void HasMohrCoulombJointSlip_NoJoints_IsFalse()
    {
        var geom = new GeometryDefinitionViewModel();
        geom.EnsureDefaultJointPropertySet();
        Assert.That(geom.HasMohrCoulombJointSlip(), Is.False);
        Assert.That(geom.HasMohrCoulombJointSlip(Array.Empty<FeJointInterfaceElement>()), Is.False);
    }

    [Test]
    public void HasMohrCoulombJointSlip_GeneratedInterfaces_UsesDefaultSet()
    {
        var geom = new GeometryDefinitionViewModel();
        geom.EnsureDefaultJointPropertySet();
        var interfaces = new[] { new FeJointInterfaceElement { N1 = 1, N2 = 2, N3 = 3, N4 = 4 } };
        Assert.That(geom.HasMohrCoulombJointSlip(interfaces), Is.True);
    }

    [Test]
    public void ElasticIsotropic_EnabledLineLoad_RoutesToP610T3Elastoplastic()
    {
        var vm = NewElasticIsotropicProject();
        vm.LoadConditionsVM.Loads.Add(new LoadModel
        {
            Type = LoadType.Line,
            Magnitude = 1000,
            IsEnabled = true,
            StartX = 0,
            StartY = 3,
            EndX = 1,
            EndY = 3
        });
        vm.FEAnalysisVM.RefreshResolvedRunRoutingDisplay();

        Assert.That(vm.FEAnalysisVM.HasEnabledSurfaceOrLineLoad, Is.True);
        Assert.That(vm.FEAnalysisVM.UseP610T3ElasticFromScriptDat, Is.False);
        Assert.That(vm.FEAnalysisVM.UseP610T3FromScriptDat, Is.True);
        Assert.That(vm.FEAnalysisVM.ResolveStructuralP610ProgramName(), Is.EqualTo("p610_t3_elastoplastic"));
        Assert.That(vm.FEAnalysisVM.ResolvedSolverForRun, Is.EqualTo("p610_t3_elastoplastic"));
    }

    [Test]
    public void ElasticIsotropic_NoJoints_RoutesToP610T3Elastic()
    {
        var vm = NewElasticIsotropicProject();

        Assert.That(vm.FEAnalysisVM.HasMohrCoulombJointSlip, Is.False);
        Assert.That(vm.FEAnalysisVM.UseP610T3ElasticFromScriptDat, Is.True);
        Assert.That(vm.FEAnalysisVM.ResolveStructuralP610ProgramName(), Is.EqualTo("p610_t3_elastic"));

        vm.FEAnalysisVM.RefreshResolvedRunRoutingDisplay();
        Assert.That(vm.FEAnalysisVM.ResolvedSolverForRun, Is.EqualTo("p610_t3_elastic"));
    }

    [Test]
    public void ElasticIsotropic_MohrCoulombWeakPlane_RoutesToP610T3Elastoplastic()
    {
        var vm = NewElasticIsotropicProject();
        AddMohrCoulombWeakPlane(vm);

        Assert.That(vm.FEAnalysisVM.HasMohrCoulombJointSlip, Is.True);
        Assert.That(vm.FEAnalysisVM.UseP610T3ElasticFromScriptDat, Is.False);
        Assert.That(vm.FEAnalysisVM.UseP610T3FromScriptDat, Is.True);
        Assert.That(vm.FEAnalysisVM.ResolveStructuralP610ProgramName(), Is.EqualTo("p610_t3_elastoplastic"));
        Assert.That(vm.FEAnalysisVM.ResolvedSolverForRun, Is.EqualTo("p610_t3_elastoplastic"));
    }

    [Test]
    public void LayersOverrideZero_CanvasStoredLineLoad_EmitsTopTraction()
    {
        var geom = new GeometryDefinitionViewModel
        {
            CanvasWidth = 600,
            CanvasHeight = 400,
            AxisXMin = -0.2,
            AxisXMax = 1.2,
            AxisYMin = -0.2,
            AxisYMax = 2.2
        };
        var mesh = new MeshViewModel { GeometryDefinitionVM = geom };
        mesh.MeshInformationRows.Add(new MeshInformationRow
        {
            ElementId = 1, Node1 = 1, Node2 = 2, Node3 = 4,
            N1X = 0, N1Y = 1, N2X = 1, N2Y = 1, N3X = 0, N3Y = 2,
            Kind = "T3", Status = "Active"
        });
        mesh.MeshInformationRows.Add(new MeshInformationRow
        {
            ElementId = 2, Node1 = 2, Node2 = 3, Node3 = 4,
            N1X = 1, N1Y = 1, N2X = 1, N2Y = 2, N3X = 0, N3Y = 2,
            Kind = "T3", Status = "Active"
        });

        var loads = new LoadConditionsViewModel { GeometryDefinition = geom };
        var load = new LoadModel
        {
            Type = LoadType.Line,
            Direction = LoadDirection.Y,
            Magnitude = 10000,
            EndMagnitude = 10000,
            StartX = 0,
            StartY = 2,
            EndX = 1,
            EndY = 2,
            Orientation = LoadOrientation.VerticalDown,
            IsEnabled = true
        };
        loads.Loads.Add(load);
        LoadPlacementCoords.NormalizeToCanvas(load, geom);
        Assert.That(load.StartX, Is.GreaterThan(10), "Sanity: load is stored in canvas pixels after project-load normalize.");

        var fe = new FEAnalysisViewModel(new ResultsViewModel()) { LayersOverride = 0 };
        var layers = P610T3DatContentBuilder.BuildLayers(mesh, stagedVM: null, feVM: fe, loads);

        Assert.That(layers, Does.Contain("-10000"));
        Assert.That(layers, Does.Match(@"(?m)^1\s*$"), "n_traction_groups must be 1, not 0.");
        Assert.That(layers, Does.Contain("3  4").Or.Contain("4  3"));
    }

    [Test]
    public void Jr01Xfem_BuiltDeck_HasLayersZeroAndTopTraction()
    {
        var path = FindJr01Xfem();
        Assert.That(File.Exists(path), Is.True, path);

        var vm = new MainWindowViewModel();
        XfemProjectPersistence.Load(vm, path);

        Assert.That(vm.FEAnalysisVM.LayersOverride, Is.EqualTo(0));
        Assert.That(vm.FEAnalysisVM.HasMohrCoulombJointSlip, Is.True);
        Assert.That(vm.LoadConditionsVM.Loads, Has.Count.EqualTo(1));

        var dat = vm.FEAnalysisVM.TryBuildMohrCoulombJointP610Dat();
        Assert.That(dat, Is.Not.Null.And.Not.Empty);
        TestContext.WriteLine(ExtractTail(dat!, 80));

        Assert.That(dat, Does.Contain("  0").Or.Contain(" 0"));
        Assert.That(dat, Does.Match(@"(?m)^0\.0001\s+80\s+5\s+0\s*$").Or.Match(@"(?m)^\S+\s+\d+\s+\d+\s+0\s*$"),
            "Solver control must declare layers=0.");
        Assert.That(dat, Does.Contain("-10000"), "Top compression 10 MPa must become syy=-10000 kPa.");
        Assert.That(vm.MeshVM.JointInterfaceElements, Has.Count.EqualTo(8),
            "Loaded JR-01 mesh is already split; Goodman interfaces must be recovered.");
        Assert.That(dat, Does.Contain("! ninterface 8"));
    }

    private static string FindJr01Xfem()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName,
                "Verification", "slope", "joint verification", "JR01_SingleJointNormalCompression",
                "jr01_single_joint_normal_compression.xfem");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return Path.GetFullPath("jr01_single_joint_normal_compression.xfem");
    }

    private static string ExtractTail(string text, int lines)
    {
        var all = text.Replace("\r\n", "\n").Split('\n');
        int take = Math.Min(lines, all.Length);
        return string.Join("\n", all.Skip(all.Length - take));
    }

    [Test]
    public void PlaneStrain_ElasticIsotropic_ShowsP610T3ElasticNotP51()
    {
        var vm = NewElasticIsotropicProject();
        vm.MaterialPropertiesVM.SelectedDetailedAnalysisType = "Plane strain";
        vm.FEAnalysisVM.RefreshResolvedRunRoutingDisplay();

        Assert.That(vm.FEAnalysisVM.UseP51FromScriptDat, Is.True);
        Assert.That(vm.FEAnalysisVM.UseP610T3ElasticFromScriptDat, Is.True);
        Assert.That(vm.FEAnalysisVM.UseP610T3FromScriptDat, Is.True);
        Assert.That(vm.FEAnalysisVM.ResolveStructuralP610ProgramName(), Is.EqualTo("p610_t3_elastic"));
        Assert.That(vm.FEAnalysisVM.ResolvedSolverForRun, Is.EqualTo("p610_t3_elastic"));
    }

    [Test]
    public void PlaneStrain_MohrCoulombWeakPlane_DoesNotShowP51()
    {
        var vm = NewElasticIsotropicProject();
        vm.MaterialPropertiesVM.SelectedDetailedAnalysisType = "Plane strain";
        AddMohrCoulombWeakPlane(vm);
        vm.FEAnalysisVM.RefreshResolvedRunRoutingDisplay();

        Assert.That(vm.FEAnalysisVM.UseP51FromScriptDat, Is.False);
        Assert.That(vm.FEAnalysisVM.ResolvedSolverForRun, Is.EqualTo("p610_t3_elastoplastic"));
        Assert.That(vm.FEAnalysisVM.ResolveStructuralP610ProgramName(), Is.EqualTo("p610_t3_elastoplastic"));
    }

    private static MainWindowViewModel NewElasticIsotropicProject()
    {
        var vm = new MainWindowViewModel();
        vm.MaterialPropertiesVM.SelectedAnalysisCategory = "Structural – static elasticity";
        vm.MaterialPropertiesVM.SelectedDetailedAnalysisType = "General 2D elastic";
        vm.MaterialPropertiesVM.SelectedFailureCriterion = FailureCriterion.Elastic;
        vm.MaterialPropertiesVM.SelectedMaterialType = MaterialType.Isotropic;
        vm.FEAnalysisVM.RefreshResolvedRunRoutingDisplay();
        return vm;
    }

    private static void AddMohrCoulombWeakPlane(MainWindowViewModel vm)
    {
        var geom = vm.GeometryDefinitionVM;
        geom.EnsureDefaultJointPropertySet();
        var set = geom.JointPropertySets[0];
        geom.GeologicalFeatures.Add(new GeologicalFeature
        {
            Id = "wp-1",
            Kind = GeologicalFeatureKind.WeakPlane,
            Name = "Joint",
            PropertySetId = set.Id,
            Vertices = { (0, 10), (20, 10) }
        });
    }
}
