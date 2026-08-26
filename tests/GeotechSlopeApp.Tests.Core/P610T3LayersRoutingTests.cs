using System.Globalization;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core;

/// <summary>
/// Regression coverage for the "spurious excavation layer" bug: a plain slope-stability
/// project with no staged construction and no tunnel geometry must resolve to a single
/// clean gravity-only layer (`layers=1`, one "0 elements" block) — matching the known-good
/// RS2_Example01_simple_slope deck — and must never fabricate an arbitrary single-element
/// excavation, and must never fall through to `layers=0` (an unrelated traction-boundary
/// "infinite medium" analysis mode; see p610_t3_elastoplastic.cpp header comment).
/// </summary>
[TestFixture]
public class P610T3LayersRoutingTests
{
    private static MeshViewModel BuildPlainMesh()
    {
        var mesh = new MeshViewModel();
        // A handful of plain T3 elements, none marked "Tunnel" -- mirrors a plain slope
        // stability mesh with no staged excavation and no tunnel geometry.
        for (int i = 1; i <= 3; i++)
        {
            mesh.MeshInformationRows.Add(new MeshInformationRow
            {
                ElementId = i,
                Node1 = i * 3 - 2,
                Node2 = i * 3 - 1,
                Node3 = i * 3,
                Etype = 1,
                N1X = 0, N1Y = 0,
                N2X = 1, N2Y = 0,
                N3X = 0, N3Y = 1,
                Kind = "T3",
                Status = "Active",
            });
        }
        return mesh;
    }

    private static FEAnalysisViewModel NewSrfFeVm()
    {
        var projectSettings = new ProjectSettingsViewModel(); // SelectedDimension defaults to TwoD
        var materials = new MaterialPropertiesViewModel();
        materials.SetProjectSettings(projectSettings);
        materials.SelectedAnalysisCategory = MaterialPropertiesViewModel.SrfAnalysisCategory;
        materials.SelectedFailureCriterion = FailureCriterion.MohrCoulomb;

        var feVm = new FEAnalysisViewModel(new ResultsViewModel());
        feVm.SetAnalysisContext(materials, null);
        Assert.That(feVm.IsSrfSolverMode, Is.True, "Test setup sanity check: expected SRF solver mode to be active.");
        return feVm;
    }

    private static int ParseLayersFromSolverControl(string solverControlBlock)
    {
        foreach (var raw in solverControlBlock.Split('\n'))
        {
            var line = raw;
            var bang = line.IndexOf('!');
            if (bang >= 0) line = line[..bang];
            var tokens = line.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 4) continue;
            if (!double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _)) continue;
            if (!int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var layers)) continue;
            return layers;
        }
        Assert.Fail("Could not find 'tol limit incs layers' line in SolverControl block:\n" + solverControlBlock);
        return -1;
    }

    [Test]
    public void BuildLayers_NoStagedNoTunnel_NonSrf_EmitsSingleGravityOnlyLayer()
    {
        var mesh = BuildPlainMesh();

        var solverControl = P610T3DatContentBuilder.BuildSolverControl(mesh, stagedVM: null, feVM: null);
        var layers = P610T3DatContentBuilder.BuildLayers(mesh, stagedVM: null, feVM: null);

        Assert.That(ParseLayersFromSolverControl(solverControl), Is.EqualTo(1),
            "Plain (non-SRF) gravity-only run must resolve to layers=1, never layers=0 or a fabricated multi-layer count.");
        Assert.That(layers, Does.Contain("! Excavation layer 1 — 0 elements"));
        Assert.That(layers, Does.Not.Contain("! Excavation layer 2"));
        // No arbitrary element id (from the old rows[0].ElementId fallback) may follow the
        // "0 elements" count line -- the very next non-comment token must be the closing tag.
        var bodyLines = layers.Replace("\r\n", "\n").Split('\n');
        int countLineIdx = System.Array.FindIndex(bodyLines, l => l.Trim() == "0");
        Assert.That(countLineIdx, Is.GreaterThanOrEqualTo(0), "Expected a lone '0' element-count line.");
        Assert.That(bodyLines[countLineIdx + 1].Trim(), Does.StartWith("! <END:Layers>").Or.Empty);
    }

    [Test]
    public void BuildLayers_NoStagedNoTunnel_SrfMode_EmitsSingleGravityOnlyLayer()
    {
        var mesh = BuildPlainMesh();
        var feVm = NewSrfFeVm();

        var solverControl = P610T3DatContentBuilder.BuildSolverControl(mesh, stagedVM: null, feVM: feVm);
        var layers = P610T3DatContentBuilder.BuildLayers(mesh, stagedVM: null, feVM: feVm);

        Assert.That(ParseLayersFromSolverControl(solverControl), Is.EqualTo(1),
            "SRF slope-stability run with no staged construction/tunnel must resolve to layers=1 (single gravity-only layer), matching the working RS2_Example01 deck -- not layers=2 (spurious excavation) or layers=0 (infinite-medium traction mode).");
        Assert.That(layers, Does.Contain("! Excavation layer 1 — 0 elements"));
        Assert.That(layers, Does.Not.Contain("! Excavation layer 2"));
    }

    [Test]
    public void BuildLayers_TunnelElements_SrfMode_PrependsGravityEquilibrationLayer()
    {
        // Regression guard for the pre-existing (still-desired) behavior: when there IS
        // real excavation/tunnel data, SRF mode still prepends a leading empty gravity
        // equilibration layer ahead of the real cut.
        var mesh = BuildPlainMesh();
        mesh.MeshInformationRows[0].Status = "Tunnel";
        var feVm = NewSrfFeVm();

        var solverControl = P610T3DatContentBuilder.BuildSolverControl(mesh, stagedVM: null, feVM: feVm);
        var layers = P610T3DatContentBuilder.BuildLayers(mesh, stagedVM: null, feVM: feVm);

        Assert.That(ParseLayersFromSolverControl(solverControl), Is.EqualTo(2));
        Assert.That(layers, Does.Contain("! Excavation layer 1 — 0 elements"));
        Assert.That(layers, Does.Contain("! Excavation layer 2 — 1 elements"));
    }

    [Test]
    public void BuildLayers_SolverControlAndLayersBlock_AgreeOnLayerCount()
    {
        // Guards against BuildSolverControlBody / BuildLayersBody drifting apart (the two
        // must always agree, since the solver reads the "layers" count off the control line
        // and then reads exactly that many layer blocks).
        var mesh = BuildPlainMesh();
        var feVm = NewSrfFeVm();

        var solverControl = P610T3DatContentBuilder.BuildSolverControl(mesh, stagedVM: null, feVM: feVm);
        var layers = P610T3DatContentBuilder.BuildLayers(mesh, stagedVM: null, feVM: feVm);

        int declaredLayers = ParseLayersFromSolverControl(solverControl);
        int emittedBlocks = System.Text.RegularExpressions.Regex.Matches(layers, "! Excavation layer").Count;
        Assert.That(emittedBlocks, Is.EqualTo(declaredLayers));
    }
}
