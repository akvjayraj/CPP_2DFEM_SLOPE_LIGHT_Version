using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Backend;

[TestFixture]
public class AnalysisKindRoutingTests
{
    [TestCase(MaterialPropertiesViewModel.GroundwaterStaticType, WaterParticipation.Prescribed)]
    [TestCase(MaterialPropertiesViewModel.GroundwaterSteadyStateType, WaterParticipation.Seepage)]
    [TestCase(MaterialPropertiesViewModel.GroundwaterSeepageGeneralType, WaterParticipation.Seepage)]
    [TestCase(MaterialPropertiesViewModel.GroundwaterSeepageFreeSurfaceType, WaterParticipation.Seepage)]
    [TestCase(MaterialPropertiesViewModel.GroundwaterTransientStateType, WaterParticipation.Dissipation)]
    [TestCase(MaterialPropertiesViewModel.GroundwaterTransientPlaneAxisymType, WaterParticipation.Dissipation)]
    [TestCase(MaterialPropertiesViewModel.GroundwaterTransientGeneral2DType, WaterParticipation.Dissipation)]
    [TestCase(MaterialPropertiesViewModel.GroundwaterSequentialType, WaterParticipation.Sequential)]
    [TestCase(MaterialPropertiesViewModel.GroundwaterFullyCoupledType, WaterParticipation.FullyCoupled)]
    public void WaterParticipation_FromTreeLeaf_WhenGroundwaterOn(string leaf, WaterParticipation expected)
    {
        Assert.That(AnalysisKindMapper.ResolveWaterParticipation(leaf, groundwaterOn: true), Is.EqualTo(expected));
    }

    [Test]
    public void WaterParticipation_IsNone_WhenGroundwaterOff()
    {
        Assert.That(
            AnalysisKindMapper.ResolveWaterParticipation(MaterialPropertiesViewModel.GroundwaterStaticType, groundwaterOn: false),
            Is.EqualTo(WaterParticipation.None));
    }

    [Test]
    public void SelectingStaticLeaf_RemembersStaticTreeMode_AndRoutesPrescribed()
    {
        var (settings, materials) = CreateWiredSettings();
        SelectGroundwaterLeaf(settings, MaterialPropertiesViewModel.GroundwaterStaticType);

        Assert.That(materials.LastGroundwaterTreeMode, Is.EqualTo(MaterialPropertiesViewModel.GroundwaterStaticType));
        Assert.That(settings.WaterParticipation, Is.EqualTo(WaterParticipation.Prescribed));
        Assert.That(settings.IsGroundwaterStaticDetailSelected, Is.True);
    }

    [Test]
    public void SelectingSteadyLeaf_RemembersSteadyTreeMode_AndRoutesSeepage()
    {
        var (settings, materials) = CreateWiredSettings();
        SelectGroundwaterLeaf(settings, MaterialPropertiesViewModel.GroundwaterSteadyStateType);

        Assert.That(materials.LastGroundwaterTreeMode, Is.EqualTo(MaterialPropertiesViewModel.GroundwaterSteadyStateType));
        Assert.That(settings.WaterParticipation, Is.EqualTo(WaterParticipation.Seepage));
        Assert.That(settings.IsSteadyStateDetailSelected, Is.True);
        Assert.That(MaterialPropertiesViewModel.IsSeepageFlowAnalysisType(materials.SelectedDetailedAnalysisType), Is.True);
    }

    [Test]
    public void SelectingTransientLeaf_RemembersTransientTreeMode_AndRoutesDissipation()
    {
        var (settings, materials) = CreateWiredSettings();
        SelectGroundwaterLeaf(settings, MaterialPropertiesViewModel.GroundwaterTransientStateType);

        Assert.That(materials.LastGroundwaterTreeMode, Is.EqualTo(MaterialPropertiesViewModel.GroundwaterTransientStateType));
        Assert.That(settings.WaterParticipation, Is.EqualTo(WaterParticipation.Dissipation));
        Assert.That(settings.IsTransientStateDetailSelected, Is.True);
        Assert.That(MaterialPropertiesViewModel.IsTransientFlowAnalysisType(materials.SelectedDetailedAnalysisType), Is.True);
    }

    [Test]
    public void Resolve_StaticPlusSrfOn_IsCoupledStaticSrf()
    {
        var kind = AnalysisKindMapper.Resolve(
            selectedCategory: MaterialPropertiesViewModel.SrfAnalysisCategory,
            detailedType: MaterialPropertiesViewModel.GroundwaterStaticType,
            lastGroundwaterTreeMode: MaterialPropertiesViewModel.GroundwaterStaticType,
            srfOn: true,
            groundwaterOn: true,
            selectedCategoryOn: true,
            is2DProblem: true);

        Assert.That(kind, Is.EqualTo(AnalysisKind.CoupledStaticSrf));
    }

    [Test]
    public void Resolve_SteadyPlusSrfOn_IsCoupledSeepageSrf()
    {
        var kind = AnalysisKindMapper.Resolve(
            selectedCategory: MaterialPropertiesViewModel.SrfAnalysisCategory,
            detailedType: MaterialPropertiesViewModel.GroundwaterSeepageGeneralType,
            lastGroundwaterTreeMode: MaterialPropertiesViewModel.GroundwaterSteadyStateType,
            srfOn: true,
            groundwaterOn: true,
            selectedCategoryOn: true,
            is2DProblem: true);

        Assert.That(kind, Is.EqualTo(AnalysisKind.CoupledSeepageSrf));
    }

    [Test]
    public void Resolve_TransientPlusSrfOn_IsNotCoupledStatic_RoutesTransientWhenGwSelected()
    {
        var kind = AnalysisKindMapper.Resolve(
            selectedCategory: MaterialPropertiesViewModel.GroundwaterAnalysisCategory,
            detailedType: MaterialPropertiesViewModel.GroundwaterTransientPlaneAxisymType,
            lastGroundwaterTreeMode: MaterialPropertiesViewModel.GroundwaterTransientStateType,
            srfOn: true,
            groundwaterOn: true,
            selectedCategoryOn: true,
            is2DProblem: true);

        Assert.That(kind, Is.EqualTo(AnalysisKind.GroundwaterTransient));
        Assert.That(kind, Is.Not.EqualTo(AnalysisKind.CoupledStaticSrf));
    }

    [Test]
    public void InferAnalysisKindFromLegacy_ConsolidationCategory_MapsToTransient()
    {
        Assert.That(
            AnalysisKindMapper.InferAnalysisKindFromLegacy(
                MaterialPropertiesViewModel.LegacyConsolidationCategory,
                "Consolidation 4-node"),
            Is.EqualTo(AnalysisKind.GroundwaterTransient));
    }

    [Test]
    public void InferAnalysisKindFromLegacy_OldGroundwaterLabels_StillMap()
    {
        Assert.That(
            AnalysisKindMapper.InferAnalysisKindFromLegacy(
                MaterialPropertiesViewModel.GroundwaterAnalysisCategoryLegacyShort,
                MaterialPropertiesViewModel.GroundwaterStaticType),
            Is.EqualTo(AnalysisKind.GroundwaterStatic));
        Assert.That(
            AnalysisKindMapper.InferAnalysisKindFromLegacy(
                MaterialPropertiesViewModel.GroundwaterAnalysisCategory,
                MaterialPropertiesViewModel.GroundwaterSeepageFreeSurfaceTypeLegacy),
            Is.EqualTo(AnalysisKind.GroundwaterSteady));
    }

    [Test]
    public void ApplyAnalysisKindToLegacyCategory_Steady_SelectsSeepageSolverAndTreeMode()
    {
        var materials = new MaterialPropertiesViewModel();
        AnalysisKindMapper.ApplyAnalysisKindToLegacyCategory(materials, AnalysisKind.GroundwaterSteady);

        Assert.That(materials.SelectedAnalysisCategory, Is.EqualTo(MaterialPropertiesViewModel.GroundwaterAnalysisCategory));
        Assert.That(MaterialPropertiesViewModel.IsSeepageFlowAnalysisType(materials.SelectedDetailedAnalysisType), Is.True);
        Assert.That(materials.LastGroundwaterTreeMode, Is.EqualTo(MaterialPropertiesViewModel.GroundwaterSteadyStateType));
    }

    [Test]
    public void AnalysisTree_HidesConsolidation_AndAddsDisabledFutureLeaves()
    {
        var (settings, materials) = CreateWiredSettings();

        Assert.That(materials.AnalysisCategories, Does.Not.Contain(MaterialPropertiesViewModel.LegacyConsolidationCategory));
        Assert.That(settings.AnalysisTree.Any(c => c.Name == MaterialPropertiesViewModel.LegacyConsolidationCategory), Is.False);

        var gw = settings.AnalysisTree.Single(c =>
            MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(c.Name));
        var names = gw.Types.Select(t => t.Name).ToList();
        Assert.That(names, Does.Contain(MaterialPropertiesViewModel.GroundwaterStaticType));
        Assert.That(names, Does.Contain(MaterialPropertiesViewModel.GroundwaterSteadyStateType));
        Assert.That(names, Does.Contain(MaterialPropertiesViewModel.GroundwaterTransientStateType));
        Assert.That(names, Does.Contain(MaterialPropertiesViewModel.GroundwaterSequentialType));
        Assert.That(names, Does.Contain(MaterialPropertiesViewModel.GroundwaterFullyCoupledType));

        var sequential = gw.Types.Single(t => t.Name == MaterialPropertiesViewModel.GroundwaterSequentialType);
        var fully = gw.Types.Single(t => t.Name == MaterialPropertiesViewModel.GroundwaterFullyCoupledType);
        Assert.That(sequential.IsEnabled, Is.False);
        Assert.That(fully.IsEnabled, Is.False);
        Assert.That(sequential.DisabledReason, Is.EqualTo("Not in this release"));
    }

    [Test]
    public void DisabledFutureLeaf_CannotBecomeSelectedTreeNode()
    {
        var (settings, _) = CreateWiredSettings();
        var gw = settings.AnalysisTree.Single(c =>
            MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(c.Name));
        SelectGroundwaterLeaf(settings, MaterialPropertiesViewModel.GroundwaterStaticType);
        var sequential = gw.Types.Single(t => t.Name == MaterialPropertiesViewModel.GroundwaterSequentialType);

        settings.SelectedAnalysisTreeNode = sequential;

        Assert.That(settings.SelectedAnalysisTreeNode?.Name, Is.EqualTo(MaterialPropertiesViewModel.GroundwaterStaticType));
    }

    [Test]
    public void Idealization_FoldsSrfAxisymmetric()
    {
        var settings = new ProjectSettingsViewModel();
        Assert.That(settings.Idealization, Is.EqualTo(AnalysisIdealization.PlaneStrain));
        Assert.That(settings.SrfAxisymmetric, Is.False);

        settings.IsAxisymmetricIdealization = true;
        Assert.That(settings.SrfAxisymmetric, Is.True);
        Assert.That(settings.Idealization, Is.EqualTo(AnalysisIdealization.Axisymmetric));

        settings.Idealization = AnalysisIdealization.PlaneStrain;
        Assert.That(settings.SrfAxisymmetric, Is.False);
        Assert.That(settings.IsPlaneStrainIdealization, Is.True);
    }

    [Test]
    public void RapidDrawdown_VisibleOnlyWhenSrfOnAndStaticLeafSelected()
    {
        var (settings, _) = CreateWiredSettings();
        settings.SetCategoryOn(MaterialPropertiesViewModel.SrfAnalysisCategory, true);
        SelectGroundwaterLeaf(settings, MaterialPropertiesViewModel.GroundwaterStaticType);
        Assert.That(settings.IsRapidDrawdownSectionVisible, Is.True);

        SelectGroundwaterLeaf(settings, MaterialPropertiesViewModel.GroundwaterSteadyStateType);
        Assert.That(settings.IsRapidDrawdownSectionVisible, Is.False);

        SelectGroundwaterLeaf(settings, MaterialPropertiesViewModel.GroundwaterStaticType);
        settings.SetCategoryOn(MaterialPropertiesViewModel.SrfAnalysisCategory, false);
        Assert.That(settings.IsRapidDrawdownSectionVisible, Is.False);
    }

    private static (ProjectSettingsViewModel Settings, MaterialPropertiesViewModel Materials) CreateWiredSettings()
    {
        var materials = new MaterialPropertiesViewModel();
        var settings = new ProjectSettingsViewModel();
        materials.SetProjectSettings(settings);
        settings.SetAxisSyncTargets(new GeometryDefinitionViewModel(), materials);
        var groundwater = new GroundwaterViewModel { IsEnabled = true };
        settings.SetGroundwaterViewModel(groundwater);
        return (settings, materials);
    }

    private static void SelectGroundwaterLeaf(ProjectSettingsViewModel settings, string leafName)
    {
        var gw = settings.AnalysisTree.Single(c =>
            MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(c.Name));
        var leaf = gw.Types.Single(t => t.Name == leafName);
        settings.SelectedAnalysisTreeNode = leaf;
    }
}
