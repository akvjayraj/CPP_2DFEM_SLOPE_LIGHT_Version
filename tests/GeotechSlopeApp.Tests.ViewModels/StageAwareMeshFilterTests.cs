using System.Linq;
using System.Windows.Media;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Tests.ViewModels.Support;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.ViewModels;

/// <summary>
/// Stage-aware mesh hiding on MeshTabCanvasWrapper (ported from TunnelWizardSmokeTest.TestStageAwareMeshHide):
/// elements excavated in the active stage drop out of the wrapper's visible element collection.
/// </summary>
[TestFixture]
public class StageAwareMeshFilterTests
{
    [SetUp]
    public void SetUp() => RecordingDialogService.Install();

    private static void SeedMesh(MeshViewModel mesh, int count)
    {
        for (int id = 1; id <= count; id++)
        {
            mesh.MeshInformationRows.Add(new MeshInformationRow
            {
                ElementId = id, Node1 = id, Node2 = id + 100, Node3 = id + 200,
                N1X = id, N1Y = 0, N2X = id + 1, N2Y = 0, N3X = id + 0.5, N3Y = 1, Kind = "T3",
            });
            mesh.MeshElements.Add(new MeshElementDisplay
            {
                DisplayId = id,
                Nodes = new PointCollection { new(id, 0), new(id + 1, 0), new(id + 0.5, 1) },
                CenterX = id + 0.5, CenterY = 0.33,
            });
        }
        mesh.IsDiscretized = true;
    }

    private static FEAnalysisViewModel BuildExcavationStages(StagedConstructionViewModel staged)
    {
        var fe = new FEAnalysisViewModel(new ResultsViewModel()) { StagedExcavationEnabled = true };
        fe.StageDefinitions.Add(new StageDefinition { StageNumber = 1, StageName = "Initial Stress", StageType = "Initial Stress" });
        fe.StageDefinitions.Add(new StageDefinition { StageNumber = 2, StageName = "Excavate 1", StageType = "Excavation" });
        staged.SetFeAnalysisViewModel(fe);
        staged.SyncConstructionStagesFromFeAnalysis(fe, true);
        return fe;
    }

    [Test]
    public void ExcavatedElements_HiddenOnlyWhenExcavationStageActive()
    {
        var (gd, mesh, _, _, staged) = HeadlessVms.Build();
        SeedMesh(mesh, 10);

        var wrapper = new MeshTabCanvasWrapper(gd, mesh);
        Assert.That(wrapper.MeshElements.Count, Is.EqualTo(10), "baseline shows all elements");

        wrapper.EnableStageAwareFilter(staged);
        Assert.That(wrapper.MeshElements.Count, Is.EqualTo(10), "no excavation stages yet");

        BuildExcavationStages(staged);
        var stage2 = staged.Stages.First(s => s.StageNumber == 2);
        stage2.ExcavatedElementIds.AddRange(new[] { 3, 7 });
        staged.NotifyExcavatedElementIdsChanged();

        staged.ActiveStage = staged.Stages.First(s => s.StageNumber == 1);
        Assert.That(wrapper.MeshElements.Count, Is.EqualTo(10), "Stage 1 (Initial Stress) hides nothing");

        staged.ActiveStage = staged.Stages.First(s => s.StageNumber == 2);
        Assert.That(wrapper.MeshElements.Count, Is.EqualTo(8), "Stage 2 hides the 2 excavated elements");
        Assert.That(wrapper.MeshElements.Any(el => el.DisplayId == 3), Is.False);
        Assert.That(wrapper.MeshElements.Any(el => el.DisplayId == 7), Is.False);
        Assert.That(wrapper.MeshElements.Any(el => el.DisplayId == 1), Is.True);

        staged.ActiveStage = staged.Stages.First(s => s.StageNumber == 1);
        Assert.That(wrapper.MeshElements.Count, Is.EqualTo(10), "toggling back restores all");
    }

    [Test]
    public void SingleInitialStressStage_FilterIsNoOp()
    {
        var (gd, mesh, _, _, staged) = HeadlessVms.Build();
        SeedMesh(mesh, 5);

        var wrapper = new MeshTabCanvasWrapper(gd, mesh);
        wrapper.EnableStageAwareFilter(staged);
        Assert.That(wrapper.MeshElements.Count, Is.EqualTo(5));

        var fe = new FEAnalysisViewModel(new ResultsViewModel()) { StagedExcavationEnabled = true };
        fe.StageDefinitions.Add(new StageDefinition { StageNumber = 1, StageName = "Initial Stress", StageType = "Initial Stress" });
        staged.SetFeAnalysisViewModel(fe);
        staged.SyncConstructionStagesFromFeAnalysis(fe, true);

        Assert.That(wrapper.MeshElements.Count, Is.EqualTo(5), "Initial-stress-only project never hides elements");
    }
}
