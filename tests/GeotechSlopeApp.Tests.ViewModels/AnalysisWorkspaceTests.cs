using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Analysis;
using GeotechSlopeApp.Models.FiniteElement;
using GeotechSlopeApp.Models.Appearance;
using GeotechSlopeApp.Persistence;
using GeotechSlopeApp.Services.Analysis;
using GeotechSlopeApp.Services.Appearance;
using GeotechSlopeApp.ViewModels;
using GeotechSlopeApp.ViewModels.Dialogs;
using GeotechSlopeApp.ViewModels.Workspace;
using GeotechSlopeApp.Visualization;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.ViewModels;

[TestFixture]
public class AnalysisWorkspaceTests
{
    [Test]
    public void Catalog_Thermal_IsNotImplemented()
    {
        var cap = AnalysisCapabilityCatalog.Get(AnalysisIntent.Thermal);
        Assert.That(cap.Status, Is.EqualTo(AnalysisCapabilityStatus.NotImplemented));
        Assert.That(AnalysisCapabilityCatalog.CanRunNativeSolver(AnalysisIntent.Thermal), Is.False);
        Assert.That(cap.UnavailableReason, Does.Contain("not available"));
    }

    [Test]
    public void Catalog_StaticAndSrf_AreAvailable()
    {
        Assert.That(AnalysisCapabilityCatalog.Get(AnalysisIntent.Static).Status,
            Is.EqualTo(AnalysisCapabilityStatus.Available));
        Assert.That(AnalysisCapabilityCatalog.Get(AnalysisIntent.Srf).Status,
            Is.EqualTo(AnalysisCapabilityStatus.Available));
        Assert.That(AnalysisCapabilityCatalog.CanRunNativeSolver(AnalysisIntent.Static), Is.True);
    }

    [Test]
    public void AnalysisOptions_GatedLists_MatchWizardDefaults()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var workspace = host.Workspace;
        var wizard = new AnalysisTypeWizardViewModel(host);

        Assert.That(workspace.GatedLoadingType, Is.EqualTo(wizard.GatedLoadingType));
        Assert.That(workspace.GatedGravityDirection, Is.EqualTo(wizard.GatedGravityDirection));
        Assert.That(workspace.GatedTangentStiffnessValue, Is.EqualTo(wizard.GatedTangentStiffnessValue));
        Assert.That(workspace.GatedConvergenceOption, Is.EqualTo(wizard.GatedConvergenceOption));
        Assert.That(workspace.GatedK0Method, Is.EqualTo(wizard.GatedK0Method));
        Assert.That(workspace.GatedDampingType, Is.EqualTo(wizard.GatedDampingType));
        Assert.That(workspace.GatedDampingRatio, Is.EqualTo(wizard.GatedDampingRatio));
        Assert.That(workspace.GatedIncompatibleMode, Is.EqualTo(wizard.GatedIncompatibleMode));
        Assert.That(workspace.GatedBoundaryStiffnessValue, Is.EqualTo(wizard.GatedBoundaryStiffnessValue));
        Assert.That(workspace.GatedStressRecoveryValue, Is.EqualTo(wizard.GatedStressRecoveryValue));
        Assert.That(workspace.GatedStiffnessMatrixValue, Is.EqualTo(wizard.GatedStiffnessMatrixValue));
        Assert.That(workspace.GatedLineSearchValue, Is.EqualTo(wizard.GatedLineSearchValue));
        Assert.That(workspace.GenerateInitialStress, Is.True);
        Assert.That(workspace.FeatureNotAvailable, Does.Contain("not available"));
        Assert.That(workspace.ThermalUnavailableReason, Does.Contain("not available"));
    }

    [Test]
    public void AnalysisOptions_GatedDropdowns_ExposeFullCatalogsAndPersistSelection()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var workspace = host.Workspace;
        var wizard = new AnalysisTypeWizardViewModel(host);

        Assert.That(workspace.GatedLoadingTypes, Is.EqualTo(AnalysisGatedOptionCatalog.LoadingTypes));
        Assert.That(workspace.GatedTangentStiffness, Is.EqualTo(AnalysisGatedOptionCatalog.TangentStiffness));
        Assert.That(workspace.GatedConvergenceOptions, Is.EqualTo(AnalysisGatedOptionCatalog.ConvergenceOptions));
        Assert.That(workspace.GatedK0Methods, Is.EqualTo(AnalysisGatedOptionCatalog.K0Methods));
        Assert.That(workspace.GatedDampingTypes, Is.EqualTo(AnalysisGatedOptionCatalog.DampingTypes));
        Assert.That(workspace.GatedDampingRatios, Is.EqualTo(AnalysisGatedOptionCatalog.DampingRatios));
        Assert.That(workspace.GatedIncompatibleModes, Is.EqualTo(AnalysisGatedOptionCatalog.IncompatibleModes));
        Assert.That(workspace.GatedBoundaryStiffness, Is.EqualTo(AnalysisGatedOptionCatalog.BoundaryStiffness));
        Assert.That(workspace.GatedStressRecovery, Is.EqualTo(AnalysisGatedOptionCatalog.StressRecovery));
        Assert.That(workspace.GatedStiffnessMatrix, Is.EqualTo(AnalysisGatedOptionCatalog.StiffnessMatrix));
        Assert.That(workspace.GatedLineSearch, Is.EqualTo(AnalysisGatedOptionCatalog.LineSearch));
        Assert.That(wizard.GatedLoadingTypes, Is.EqualTo(workspace.GatedLoadingTypes));
        Assert.That(wizard.GatedDampingRatios, Is.EqualTo(workspace.GatedDampingRatios));

        Assert.That(workspace.GatedLoadingType, Is.EqualTo("Static (Constant)"));
        Assert.That(workspace.GatedTangentStiffnessValue, Is.EqualTo("Auto"));
        Assert.That(workspace.GatedConvergenceOption, Is.EqualTo("Energy Norm"));
        Assert.That(workspace.GatedK0Method, Is.EqualTo("Jaky"));
        Assert.That(workspace.GatedDampingType, Is.EqualTo("None"));
        Assert.That(workspace.GatedDampingRatio, Is.EqualTo("0.00"));
        Assert.That(workspace.GatedIncompatibleMode, Is.EqualTo("Automatic"));
        Assert.That(workspace.GatedBoundaryStiffnessValue, Is.EqualTo("Program Controlled"));
        Assert.That(workspace.GatedStressRecoveryValue, Is.EqualTo("At Gauss Points"));
        Assert.That(workspace.GatedStiffnessMatrixValue, Is.EqualTo("Symmetric"));
        Assert.That(workspace.GatedLineSearchValue, Is.EqualTo("On"));

        workspace.GatedLoadingType = "Cyclic";
        wizard.GatedTangentStiffnessValue = "Secant";
        wizard.GatedConvergenceOption = "Residual Force";
        workspace.GatedK0Method = "Mayne & Kulhawy";
        workspace.GatedDampingType = "Rayleigh";
        workspace.GatedDampingRatio = "0.05";
        workspace.GatedIncompatibleMode = "Enhanced Strain";
        wizard.GatedBoundaryStiffnessValue = "Stiff";
        workspace.GatedStressRecoveryValue = "At Nodes";
        workspace.GatedStiffnessMatrixValue = "Unsymmetric";
        workspace.GatedLineSearchValue = "Automatic";

        Assert.That(wizard.GatedLoadingType, Is.EqualTo("Cyclic"));
        Assert.That(workspace.GatedTangentStiffnessValue, Is.EqualTo("Secant"));
        Assert.That(workspace.GatedConvergenceOption, Is.EqualTo("Residual Force"));
        Assert.That(wizard.GatedK0Method, Is.EqualTo("Mayne & Kulhawy"));
        Assert.That(wizard.GatedDampingType, Is.EqualTo("Rayleigh"));
        Assert.That(wizard.GatedDampingRatio, Is.EqualTo("0.05"));
        Assert.That(wizard.GatedIncompatibleMode, Is.EqualTo("Enhanced Strain"));
        Assert.That(workspace.GatedBoundaryStiffnessValue, Is.EqualTo("Stiff"));
        Assert.That(wizard.GatedStressRecoveryValue, Is.EqualTo("At Nodes"));
        Assert.That(wizard.GatedStiffnessMatrixValue, Is.EqualTo("Unsymmetric"));
        Assert.That(wizard.GatedLineSearchValue, Is.EqualTo("Automatic"));

        var previousK0 = workspace.GatedK0Method;
        workspace.GatedK0Method = "not-a-real-option";
        Assert.That(workspace.GatedK0Method, Is.EqualTo(previousK0));
    }

    [Test]
    public void AnalysisOptions_LiveCouplingAndSolverFields_DoNotNeedWizard()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var workspace = host.Workspace;

        Assert.That(workspace.SelectedFormulation, Is.EqualTo("Total Stress"));
        Assert.That(workspace.SelectedInitialStressMethod, Is.EqualTo("In-Situ"));

        workspace.SelectedFormulation = "Effective Stress";
        Assert.That(host.LoadConditionsVM.FieldStress.Global.UseEffectiveStressRatio, Is.True);

        workspace.SelectedInitialStressMethod = "Constant";
        Assert.That(host.LoadConditionsVM.FieldStress.Global.Mode, Is.EqualTo(FieldStressMode.Constant));

        host.GroundwaterVM.IsEnabled = true;
        workspace.Settings.UpdatePorePressure = true;
        host.FEAnalysisVM.MaxIterations = 1000;

        Assert.That(host.GroundwaterVM.IsEnabled, Is.True);
        Assert.That(workspace.Settings.UpdatePorePressure, Is.True);
        Assert.That(host.FEAnalysisVM.MaxIterations, Is.EqualTo(1000));
    }

    [Test]
    public void WizardFinish_WithDefinedStages_OpensFooterMatchingStageCount()
    {
        var host = new MainWindowViewModel();
        host.FEAnalysisVM.StagedExcavationEnabled = false;
        host.FEAnalysisVM.StageDefinitions.Clear();
        var wizard = new AnalysisTypeWizardViewModel(host);

        Assert.That(wizard.PendingStaged, Is.False);
        wizard.AddStageCommand.Execute(null);
        wizard.AddStageCommand.Execute(null);
        wizard.AddStageCommand.Execute(null);
        Assert.That(wizard.PendingStaged, Is.True);
        Assert.That(host.FEAnalysisVM.StageDefinitions, Has.Count.EqualTo(3));

        wizard.Finish();

        Assert.That(host.FEAnalysisVM.StagedExcavationEnabled, Is.True);
        Assert.That(host.ShowStageFooterRow, Is.True);
        Assert.That(host.ProjectSettingsVM.NumberOfStages, Is.EqualTo(3));
        Assert.That(host.ProjectSettingsVM.StageFooterTabs, Has.Count.EqualTo(3));
        Assert.That(host.ProjectSettingsVM.StageFooterTabs.Select(t => t.StageName),
            Is.EqualTo(new[] { "Initial Stress", "Stage 2", "Stage 3" }));
        Assert.That(host.ProjectSettingsVM.StageFooterTabs.Select(t => t.StageNumber),
            Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void WizardFinish_WithZeroStagesAndStagedOff_LeavesFooterHidden()
    {
        var host = new MainWindowViewModel();
        host.FEAnalysisVM.StagedExcavationEnabled = false;
        host.FEAnalysisVM.StageDefinitions.Clear();
        var wizard = new AnalysisTypeWizardViewModel(host);

        Assert.That(wizard.PendingStaged, Is.False);
        wizard.Finish();

        Assert.That(host.FEAnalysisVM.StagedExcavationEnabled, Is.False);
        Assert.That(host.ShowStageFooterRow, Is.False);
        Assert.That(host.ProjectSettingsVM.StageFooterTabs, Is.Empty);
        Assert.That(host.ProjectSettingsVM.NumberOfStages, Is.EqualTo(0));
    }

    [Test]
    public void Explorer_UsesProjectRoot_Without3DView()
    {
        var host = HostWithFullExplorer();
        host.ProjectSettingsVM.Project.ProjectName = "GriffithsLane";
        host.CurrentXfemProjectPath = @"C:\models\GriffithsLane.xfem";
        host.Workspace.Explorer.Refresh();

        Assert.That(host.Workspace.Explorer.Roots, Has.Count.EqualTo(1));
        var root = host.Workspace.Explorer.Roots[0];
        Assert.That(root.Title, Is.EqualTo("GriffithsLane.xfem"));
        Assert.That(root.Kind, Is.EqualTo(ExplorerNodeKind.Project));
        Assert.That(root.Children.Select(c => c.Title), Is.EqualTo(new[]
        {
            "Geometry",
            "Materials",
            "Features",
            "Mesh",
            "Boundary Conditions",
            "Loads",
            "Staged Construction",
            "Analysis",
            "Results"
        }));
        Assert.That(FindByTitle(root, "3D View"), Is.Null);
    }

    [Test]
    public void Explorer_DefaultProjectTitle_IsProject1_WithoutXfemSuffix()
    {
        var host = new MainWindowViewModel();
        Assert.That(host.Workspace.Explorer.Roots[0].Title, Is.EqualTo("Project 1"));
    }

    [Test]
    public void Explorer_GroupsMaterialsBySoilRockStructural()
    {
        var host = HostWithFullExplorer();
        host.MaterialPropertiesVM.Materials.Clear();
        host.MaterialPropertiesVM.Materials.Add(new MaterialModel
        {
            Id = 1, Name = "Clay", FailureCriterion = FailureCriterion.MohrCoulomb
        });
        host.MaterialPropertiesVM.Materials.Add(new MaterialModel
        {
            Id = 2, Name = "Intact rock", FailureCriterion = FailureCriterion.HoekBrown
        });
        host.MaterialPropertiesVM.Materials.Add(new MaterialModel
        {
            Id = 3, Name = "Steel", FailureCriterion = FailureCriterion.Elastic
        });
        host.Workspace.Explorer.Refresh();

        var materials = Child(host.Workspace.Explorer.Roots[0], "Materials");
        var soil = Child(materials, "Soil");
        var rock = Child(materials, "Rock");
        var structural = Child(materials, "Structural");
        Assert.That(soil.Count, Is.EqualTo(1));
        Assert.That(rock.Count, Is.EqualTo(1));
        Assert.That(structural.Count, Is.EqualTo(1));
        Assert.That(soil.Children.Single().Title, Is.EqualTo("Clay"));
        Assert.That(rock.Children.Single().Title, Is.EqualTo("Intact rock"));
        Assert.That(structural.Children.Single().Title, Is.EqualTo("Steel"));
        Assert.That(ProjectExplorerViewModel.ClassifyMaterial(FailureCriterion.DruckerPrager), Is.EqualTo("Soil"));
        Assert.That(ProjectExplorerViewModel.ClassifyMaterial(FailureCriterion.GeneralizedHoekBrown), Is.EqualTo("Rock"));
        Assert.That(ProjectExplorerViewModel.ClassifyMaterial(FailureCriterion.Elastic), Is.EqualTo("Structural"));
    }

    [Test]
    public void Explorer_LoadGroups_CountByLoadType()
    {
        var host = HostWithFullExplorer();
        host.LoadConditionsVM.Loads.Clear();
        host.LoadConditionsVM.Loads.Add(new LoadModel { Name = "P1", Type = LoadType.Point });
        host.LoadConditionsVM.Loads.Add(new LoadModel { Name = "L1", Type = LoadType.Line });
        host.LoadConditionsVM.Loads.Add(new LoadModel { Name = "S1", Type = LoadType.Triangular });
        host.Workspace.Explorer.Refresh();

        var loads = Child(host.Workspace.Explorer.Roots[0], "Loads");
        Assert.That(Child(loads, "Nodal Load").Count, Is.EqualTo(1));
        Assert.That(Child(loads, "Line Load").Count, Is.EqualTo(1));
        Assert.That(Child(loads, "Surface Load").Count, Is.EqualTo(1));
        Assert.That(Child(loads, "Nodal Load").Children.Single().Title, Is.EqualTo("P1"));
    }

    [Test]
    public void Explorer_EmptyGroups_ShowZeroCounts()
    {
        var host = HostWithFullExplorer();
        host.MaterialPropertiesVM.Materials.Clear();
        host.LoadConditionsVM.Loads.Clear();
        host.Workspace.Explorer.Refresh();

        var root = host.Workspace.Explorer.Roots[0];
        var materials = Child(root, "Materials");
        Assert.That(Child(materials, "Soil").Count, Is.EqualTo(0));
        Assert.That(Child(materials, "Rock").Count, Is.EqualTo(0));
        Assert.That(Child(materials, "Structural").Count, Is.EqualTo(0));
        var features = Child(root, "Features");
        Assert.That(Child(features, "Line").Count, Is.EqualTo(0));
        Assert.That(Child(features, "Surface").Count, Is.EqualTo(0));
        var loads = Child(root, "Loads");
        Assert.That(Child(loads, "Nodal Load").Count, Is.EqualTo(0));
        Assert.That(Child(loads, "Line Load").Count, Is.EqualTo(0));
        Assert.That(Child(loads, "Surface Load").Count, Is.EqualTo(0));
        Assert.That(Child(root, "Staged Construction").Count, Is.EqualTo(host.StagedConstructionVM.Stages.Count));
        var results = Child(root, "Results");
        Assert.That(Child(results, "Result Sets").Count, Is.EqualTo(0));
        Assert.That(Child(results, "Charts").Count, Is.EqualTo(0));
        Assert.That(Child(results, "Table").Count, Is.EqualTo(0));
    }

    [Test]
    public void Explorer_Search_FiltersByTitle()
    {
        var host = HostWithFullExplorer();
        host.Workspace.Explorer.SearchText = "Nodal";
        var filtered = host.Workspace.Explorer.Roots.Single();
        Assert.That(FindByTitle(filtered, "Nodal Load"), Is.Not.Null);
        Assert.That(FindByTitle(filtered, "Materials"), Is.Null);
        Assert.That(FindByTitle(filtered, "Geometry"), Is.Null);
    }

    [Test]
    public void Explorer_SrfMode_AppendsFinalStageSsr_WhenMissing()
    {
        var host = HostWithFullExplorer();
        host.MaterialPropertiesVM.SelectedAnalysisCategory = MaterialPropertiesViewModel.SrfAnalysisCategory;
        var analysis = Child(host.Workspace.Explorer.Roots[0], "Analysis");
        var stages = Child(analysis, "Stages");
        Assert.That(stages.Children.Any(c => c.Title == "Final Stage (SSR)"), Is.True);
    }

    [Test]
    public void Explorer_AnalysisFolder_HasSettingsSolverAndStages()
    {
        var host = HostWithFullExplorer();
        var analysis = Child(host.Workspace.Explorer.Roots[0], "Analysis");
        Assert.That(analysis.Children.Select(c => c.Title), Is.EqualTo(new[]
        {
            "Analysis Settings", "Solver Settings", "Stages"
        }));
        Assert.That(Child(analysis, "Analysis Settings").Payload,
            Is.EqualTo(AnalysisWorkspaceViewModel.PayloadAnalysisSettings));
        Assert.That(Child(analysis, "Solver Settings").Payload,
            Is.EqualTo(AnalysisWorkspaceViewModel.PayloadSolverSettings));
        Assert.That(Child(analysis, "Stages").Payload,
            Is.EqualTo(AnalysisWorkspaceViewModel.PayloadAnalysisStages));
    }

    [Test]
    public void AnalysisRibbon_Explorer_ShowsOnlyAnalysisFolder()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("ANALYSIS");
        host.Workspace.Explorer.Refresh();

        Assert.That(host.Workspace.Explorer.IsAnalysisOnlyExplorer, Is.True);
        Assert.That(host.Workspace.Explorer.Roots[0].Children.Select(c => c.Title),
            Is.EqualTo(new[] { "Analysis" }));
        Assert.That(Child(host.Workspace.Explorer.Roots[0], "Analysis").Children.Select(c => c.Title),
            Is.EqualTo(new[] { "Analysis Settings", "Solver Settings", "Stages" }));
        Assert.That(FindByTitle(host.Workspace.Explorer.Roots[0], "Geometry"), Is.Null);
        Assert.That(FindByTitle(host.Workspace.Explorer.Roots[0], "Materials"), Is.Null);
        Assert.That(FindByTitle(host.Workspace.Explorer.Roots[0], "Results"), Is.Null);

        host.Workspace.SelectRibbonTabCommand.Execute("RESULTS");
        Assert.That(host.Workspace.Explorer.IsAnalysisOnlyExplorer, Is.False);
        Assert.That(FindByTitle(host.Workspace.Explorer.Roots[0], "Geometry"), Is.Not.Null);
        Assert.That(FindByTitle(host.Workspace.Explorer.Roots[0], "Results"), Is.Not.Null);
    }

    [Test]
    public void AnalysisRibbon_Toolbar_SelectsTypeThenIntent()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("ANALYSIS");
        Assert.That(host.Workspace.IsRibbonToolType, Is.True);

        host.Workspace.SelectAnalysisRibbonToolCommand.Execute("Srf");
        Assert.That(host.Workspace.IsRibbonToolSrf, Is.True);
        Assert.That(host.Workspace.Settings.Intent, Is.EqualTo(AnalysisIntent.Srf));
        Assert.That(host.Workspace.IsAnalysisTypeExpanded, Is.True);
    }

    [Test]
    public void AnalysisRibbon_ShowsSummaryDashboardAndTools()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("ANALYSIS");

        Assert.That(host.IsAnalysisRibbonWorkspace, Is.True);
        Assert.That(host.IsResultsInspectorChrome, Is.False);
        Assert.That(host.IsAnalysisChrome, Is.True);
        Assert.That(host.SelectedTabIndex, Is.EqualTo(7));
        Assert.That(host.Workspace.IsCenterView2D, Is.True);
        Assert.That(host.Workspace.IsCenterOverlayVisible, Is.False);
        Assert.That(host.Workspace.AnalysisTypeSummary, Is.Not.Empty);
        Assert.That(host.Workspace.LastSrfSummary, Is.EqualTo("—"));
        Assert.That(host.Workspace.SettingsSummaryText, Does.Contain("Analysis Type"));
        Assert.That(host.WorkspaceSolverLogRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedDashboardHeight));
    }

    [Test]
    public void AnalysisCenter_StageAndSolverNodes_SwitchCenterView()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("ANALYSIS");

        host.Workspace.Explorer.SelectedNode =
            Child(Child(host.Workspace.Explorer.Roots[0], "Analysis"), "Solver Settings");
        Assert.That(host.Workspace.IsCenterViewMonitor, Is.True);
        Assert.That(host.Workspace.IsCenterOverlayVisible, Is.True);

        host.Workspace.SelectCenterViewCommand.Execute(AnalysisWorkspaceViewModel.CenterViewStages);
        Assert.That(host.Workspace.IsCenterViewStages, Is.True);

        host.Workspace.SelectCenterViewCommand.Execute(AnalysisWorkspaceViewModel.CenterView2D);
        Assert.That(host.Workspace.IsCenterView2D, Is.True);
        Assert.That(host.Workspace.IsCenterOverlayVisible, Is.False);
    }

    [Test]
    public void Explorer_SelectingGeometry_SetsGeometryTab()
    {
        var host = HostWithFullExplorer();
        host.SelectedTabIndex = 0;
        host.Workspace.Explorer.SelectedNode = Child(host.Workspace.Explorer.Roots[0], "Geometry");
        Assert.That(host.SelectedTabIndex, Is.EqualTo(1));
    }

    private static MainWindowViewModel HostWithFullExplorer()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = false };
        host.Workspace.Explorer.Refresh();
        return host;
    }

    private static ProjectExplorerNode Child(ProjectExplorerNode parent, string title) =>
        parent.Children.Single(c => c.Title == title);

    private static ProjectExplorerNode? FindByTitle(ProjectExplorerNode node, string title)
    {
        if (node.Title == title)
            return node;
        foreach (var child in node.Children)
        {
            var match = FindByTitle(child, title);
            if (match != null)
                return match;
        }
        return null;
    }

    [Test]
    public void FileRibbon_ShowsFileWorkspace_HidesAnalysisChrome()
    {
        var host = new MainWindowViewModel();
        host.UseAnalysisWorkspace = true;
        host.Workspace.RibbonTab = "FILE";
        Assert.That(host.IsFileWorkspace, Is.True);
        Assert.That(host.IsAnalysisChrome, Is.False);
        Assert.That(host.IsGeometryWorkspace, Is.False);

        host.Workspace.RibbonTab = "GEOMETRY";
        Assert.That(host.IsFileWorkspace, Is.False);
        Assert.That(host.IsGeometryWorkspace, Is.True);
    }

    [Test]
    public void FileHome_UsesLiveProjectName_AndFiltersRecent()
    {
        var host = new MainWindowViewModel();
        host.ProjectSettingsVM.Project.ProjectName = "GriffithsLane";
        host.Workspace.FileHome.Refresh();
        Assert.That(host.Workspace.FileHome.ProjectTitle, Is.EqualTo("GriffithsLane"));

        host.Workspace.FileHome.SearchText = "zzz-no-match";
        Assert.That(host.Workspace.FileHome.HasRecent, Is.False);
    }

    [Test]
    public void Appearance_ApplyDark_UsesCharcoalPanel_NotClassicLight()
    {
        var fromClassic = UiAppearancePalettes.ClassicLight();
        fromClassic.Preset = UiAppearancePreset.Dark;
        fromClassic.WindowBackground = "#FF0F172A";
        fromClassic.WindowForeground = "#FFE2E8F0";
        UiAppearancePalettes.SyncPanelChrome(fromClassic);
        Assert.That(fromClassic.PanelBackground, Is.EqualTo("#FF1E293B"));
        Assert.That(fromClassic.WindowForeground, Is.EqualTo("#FFE2E8F0"));

        var custom = UiAppearancePalettes.Dark();
        custom.Preset = UiAppearancePreset.Custom;
        custom.WindowBackground = "#FF0F172A";
        UiAppearancePalettes.SyncPanelChrome(custom);
        Assert.That(custom.PanelBackground, Is.EqualTo("#FF0F172A"));
    }

    [Test]
    public void Appearance_PoorContrast_IsFlagged()
    {
        var light = UiAppearancePalettes.Parse("#FFFFFF", System.Windows.Media.Colors.White);
        var yellow = UiAppearancePalettes.Parse("#FFFF00", System.Windows.Media.Colors.Yellow);
        var dark = UiAppearancePalettes.Parse("#000000", System.Windows.Media.Colors.Black);
        Assert.That(UiAppearancePalettes.ContrastRatio(light, dark), Is.GreaterThan(10));
        Assert.That(UiAppearancePalettes.ContrastRatio(light, yellow), Is.LessThan(4.5));
    }

    [Test]
    public void Appearance_RemapSystemColors_DarkThenClassicLight()
    {
        var resources = new ResourceDictionary();
        var darkPaper = new SolidColorBrush(UiAppearancePalettes.Parse(
            UiAppearancePalettes.Dark().ViewportPaper, Colors.Black));
        var darkFg = new SolidColorBrush(UiAppearancePalettes.Parse(
            UiAppearancePalettes.Dark().WindowForeground, Colors.White));
        resources[UiAppearanceService.ViewportPaperBrushKey] = darkPaper;
        resources[UiAppearanceService.WindowForegroundBrushKey] = darkFg;
        resources[UiAppearanceService.ExplorerSelectionBrushKey] = darkPaper;

        UiAppearanceService.RemapSystemColors(resources);

        Assert.That(((SolidColorBrush)resources[SystemColors.WindowBrushKey]).Color,
            Is.EqualTo(darkPaper.Color));
        Assert.That(((SolidColorBrush)resources[SystemColors.ControlBrushKey]).Color,
            Is.EqualTo(darkPaper.Color));
        Assert.That(((SolidColorBrush)resources[SystemColors.ControlLightLightBrushKey]).Color,
            Is.EqualTo(darkPaper.Color));
        Assert.That(((SolidColorBrush)resources[SystemColors.WindowTextBrushKey]).Color,
            Is.EqualTo(darkFg.Color));
        Assert.That(darkPaper.Color,
            Is.Not.EqualTo(UiAppearancePalettes.Parse("#FFF8FAFC", Colors.White)));

        var lightPaper = new SolidColorBrush(UiAppearancePalettes.Parse(
            UiAppearancePalettes.ClassicLight().ViewportPaper, Colors.White));
        var lightFg = new SolidColorBrush(UiAppearancePalettes.Parse(
            UiAppearancePalettes.ClassicLight().WindowForeground, Colors.Black));
        resources[UiAppearanceService.ViewportPaperBrushKey] = lightPaper;
        resources[UiAppearanceService.WindowForegroundBrushKey] = lightFg;
        resources[UiAppearanceService.ExplorerSelectionBrushKey] = lightPaper;
        UiAppearanceService.RemapSystemColors(resources);

        Assert.That(((SolidColorBrush)resources[SystemColors.WindowBrushKey]).Color,
            Is.EqualTo(lightPaper.Color));
        Assert.That(((SolidColorBrush)resources[SystemColors.WindowTextBrushKey]).Color,
            Is.EqualTo(lightFg.Color));
    }

    [Test]
    public void GeometryRibbon_OpensGeometryWorkspacePanels()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("GEOMETRY");

        Assert.That(host.Workspace.IsGeometryRibbon, Is.True);
        Assert.That(host.IsGeometryWorkspace, Is.True);
        Assert.That(host.IsMaterialsWorkspace, Is.False);
        Assert.That(host.IsAnalysisChrome, Is.False);
        Assert.That(host.SelectedTabIndex, Is.EqualTo(1));
        Assert.That(host.WorkspaceBottomChromeRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));
        Assert.That(host.WorkspaceRightColumnWidth.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedRightWidth));
        Assert.That(host.Workspace.Geometry.Roots, Has.Count.EqualTo(1));
        Assert.That(host.Workspace.Geometry.Roots[0].Title, Is.EqualTo("Project Geometry"));
        Assert.That(host.Workspace.Geometry.Roots[0].Children.Select(c => c.Title), Is.EqualTo(new[]
        {
            "Points", "Lines", "Surfaces", "Regions", "Blocks", "Boolean", "Construction Geometry"
        }));
    }

    [Test]
    public void GeometryModify_Delete_RemovesExplorerPointLineAndSurface()
    {
        var host = new MainWindowViewModel();
        var geometry = host.GeometryDefinitionVM;
        var explorer = host.Workspace.Geometry;

        geometry.GeometryVertexRows.Clear();
        geometry.GeometryLineSegments.Clear();
        geometry.GeometrySurfaces.Clear();
        explorer.Refresh();

        var vertex = new GeometryDefinitionViewModel.GeometryVertexDisplay
        {
            Index = 0,
            AxisX = 1,
            AxisY = 2,
            StablePointId = 1
        };
        geometry.GeometryVertexRows.Add(vertex);
        var points = explorer.Roots[0].Children.Single(c => c.Title == "Points");
        explorer.SelectedNode = points.Children.Single();
        explorer.DeleteSelectedCommand.Execute(null);
        Assert.That(explorer.PointCount, Is.EqualTo(0));

        geometry.GeometryLineSegments.Add(new GeometryLineSegment(1, "P1", "P2"));
        var lines = explorer.Roots[0].Children.Single(c => c.Title == "Lines");
        explorer.SelectedNode = lines.Children.Single();
        explorer.DeleteSelectedCommand.Execute(null);
        Assert.That(explorer.LineCount, Is.EqualTo(0));

        geometry.GeometrySurfaces.Add(new GeometrySurface(1, "Face"));
        var surfaces = explorer.Roots[0].Children.Single(c => c.Title == "Surfaces");
        explorer.SelectedNode = surfaces.Children.Single(c => c.Title == "Face");
        explorer.DeleteSelectedCommand.Execute(null);
        Assert.That(explorer.SurfaceCount, Is.EqualTo(0));
        Assert.That(explorer.RegionCount, Is.EqualTo(0));
    }

    [Test]
    public void GeometryModify_Delete_BlocksFolder_IsNoOp()
    {
        var host = new MainWindowViewModel();
        var explorer = host.Workspace.Geometry;
        var blocks = explorer.Roots[0].Children.Single(c => c.Title == "Blocks");
        explorer.SelectedNode = blocks;
        Assert.DoesNotThrow(() => explorer.DeleteSelectedCommand.Execute(null));
        Assert.That(explorer.BlockCount, Is.EqualTo(0));
    }

    [Test]
    public void GeometryModify_Delete_UsesDetailsActionsForSelectedEntity()
    {
        var host = new MainWindowViewModel();
        var geometry = host.GeometryDefinitionVM;
        var explorer = host.Workspace.Geometry;
        geometry.SetOutlineAxis(new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 8.0), (0.0, 8.0) });
        explorer.Refresh();

        var pointsFolder = explorer.Roots[0].Children.Single(c => c.Title == "Points");
        explorer.SelectedNode = pointsFolder;
        explorer.SelectedEntity = geometry.GeometryVertexRows[1];

        var remainingId = geometry.GeometryVertexRows[0].StablePointId;
        Assert.That(explorer.TryDeleteSelection(), Is.True);
        Assert.That(geometry.GeometryVertexRows.Any(v => v.StablePointId == remainingId), Is.True);
        Assert.That(geometry.GeometryVertexRows.Count, Is.EqualTo(3));
    }

    [Test]
    public void GeometryModify_Delete_UsesDetailsLineAndSurfaceActions()
    {
        var host = new MainWindowViewModel();
        var geometry = host.GeometryDefinitionVM;
        var explorer = host.Workspace.Geometry;
        geometry.SetOutlineAxis(new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 8.0), (0.0, 8.0) });
        explorer.SelectedNode = explorer.Roots[0].Children.Single(c => c.Title == "Lines");
        var lineNumber = geometry.GeometryLineSegments[0].LineNumber;
        geometry.SetHighlightedLineSegment(lineNumber);

        Assert.That(explorer.TryDeleteSelection(), Is.True);
        Assert.That(geometry.GeometryVertexRows.Count, Is.EqualTo(3));

        var surface = new GeometrySurface(1, "Pad");
        geometry.GeometrySurfaces.Add(surface);
        explorer.SelectedNode = explorer.Roots[0].Children.Single(c => c.Title == "Surfaces");
        geometry.SelectedGeometrySurface = surface;

        Assert.That(explorer.TryDeleteSelection(), Is.True);
        Assert.That(geometry.GeometrySurfaces, Does.Not.Contain(surface));
    }

    [Test]
    public void GeometryExplorer_CountsVerticesAndLines()
    {
        var host = new MainWindowViewModel();
        host.GeometryDefinitionVM.GeometryVertexRows.Clear();
        host.GeometryDefinitionVM.GeometryLineSegments.Clear();
        host.Workspace.Geometry.Refresh();
        Assert.That(host.Workspace.Geometry.PointCount, Is.EqualTo(0));

        host.GeometryDefinitionVM.GeometryLineSegments.Add(new GeometryLineSegment(1, "P1", "P2"));
        Assert.That(host.Workspace.Geometry.LineCount, Is.EqualTo(1));
        Assert.That(host.Workspace.Geometry.Roots[0].Children.Any(c => c.Title == "Lines" && c.Count == 1), Is.True);

        host.Workspace.Geometry.FilterText = "P1";
        var lines = host.Workspace.Geometry.Roots[0].Children.First(c => c.Title == "Lines");
        Assert.That(lines.Children, Has.Count.EqualTo(1));
        host.Workspace.Geometry.FilterText = "no-such-line";
        lines = host.Workspace.Geometry.Roots[0].Children.First(c => c.Title == "Lines");
        Assert.That(lines.Children, Is.Empty);
    }

    [Test]
    public void GeometryExplorer_Surfaces_NewSurface_PicksLinesOnCanvas()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("GEOMETRY");
        var geometry = host.GeometryDefinitionVM;
        var explorer = host.Workspace.Geometry;
        geometry.SetOutlineAxis(new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 8.0), (0.0, 8.0) });
        explorer.Refresh();

        var surfaces = explorer.Roots[0].Children.Single(c => c.Title == "Surfaces");
        var newSurface = surfaces.Children.Single(c => c.Title == "New Surface");
        explorer.SelectedNode = newSurface;

        Assert.That(geometry.SelectedGeometrySurface, Is.Not.Null);
        Assert.That(geometry.SelectedGeometrySurface!.IsPolygonLocked, Is.False);
        Assert.That(geometry.IsSurfacePolygonPickMode, Is.True);

        var draft = geometry.SelectedGeometrySurface;
        explorer.Refresh();
        var surfacesAgain = explorer.Roots[0].Children.Single(c => c.Title == "Surfaces");
        explorer.SelectedNode = surfacesAgain;
        Assert.That(geometry.GeometrySurfaces.Count(s => !s.IsPolygonLocked), Is.EqualTo(1));
        Assert.That(geometry.SelectedGeometrySurface, Is.SameAs(draft));

        var from = geometry.GeometryVertexRows[0];
        var to = geometry.GeometryVertexRows[1];
        var mid = new System.Windows.Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        geometry.HandleSurfaceLinePickClick(mid);
        Assert.That(geometry.SelectedGeometrySurface!.PickedLineNumbers, Is.Not.Empty);
    }

    [Test]
    public void GeometryPointsTable_NewEditUpdate_UsePointPopupCommands()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("GEOMETRY");
        var geometry = host.Workspace.Geometry.Geometry;

        geometry.StartNewPointModeCommand.Execute(null);
        Assert.That(geometry.IsNewPointMode, Is.True);

        geometry.StartGeometryVerticesEditCommand.Execute(null);
        Assert.That(geometry.IsGeometryVerticesEditMode, Is.True);

        geometry.GeometryXInput = "2.5";
        geometry.GeometryYInput = "4.5";
        var before = geometry.GeometryVertexRows.Count;
        geometry.AddGeometryPointFromInputCommand.Execute(null);
        Assert.That(geometry.GeometryVertexRows.Count, Is.EqualTo(before + 1));
        Assert.That(geometry.GeometryVertexRows.Last().AxisX, Is.EqualTo(2.5).Within(1e-9));
        Assert.That(geometry.GeometryVertexRows.Last().AxisY, Is.EqualTo(4.5).Within(1e-9));
    }

    [Test]
    public void GeometryExplorer_FolderSelection_SwapsBottomTable()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("GEOMETRY");
        var explorer = host.Workspace.Geometry;
        var root = explorer.Roots[0];

        explorer.SelectedNode = root.Children.Single(c => c.Title == "Lines");
        Assert.That(explorer.BottomTableKind, Is.EqualTo(GeometryBottomTableKind.Lines));
        Assert.That(explorer.TableTitle, Is.EqualTo("Lines"));
        Assert.That(explorer.IsLinesTable, Is.True);

        explorer.SelectedNode = root.Children.Single(c => c.Title == "Surfaces");
        Assert.That(explorer.BottomTableKind, Is.EqualTo(GeometryBottomTableKind.Surfaces));
        Assert.That(explorer.TableTitle, Is.EqualTo("Surfaces"));
        Assert.That(explorer.IsSurfacesTable, Is.True);

        explorer.SelectedNode = root.Children.Single(c => c.Title == "Points");
        Assert.That(explorer.BottomTableKind, Is.EqualTo(GeometryBottomTableKind.Points));
        Assert.That(explorer.TableTitle, Is.EqualTo("Points Table"));
        Assert.That(explorer.IsPointsTable, Is.True);
    }

    [Test]
    public void GeometryPanels_CollapseToHeaderStrip_AndRestore()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("GEOMETRY");
        var geom = host.Workspace.Geometry;

        Assert.That(host.WorkspaceLeftColumnWidth.Value, Is.EqualTo(MainWindowViewModel.WorkspaceExpandedLeftWidth));
        Assert.That(host.WorkspaceRightColumnWidth.Value, Is.EqualTo(MainWindowViewModel.WorkspaceExpandedRightWidth));
        Assert.That(host.WorkspaceGeometryTableRowHeight.Value, Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));

        geom.ToggleExplorerCommand.Execute(null);
        geom.TogglePropertiesCommand.Execute(null);
        geom.TogglePointsTableCommand.Execute(null);

        Assert.That(geom.IsExplorerExpanded, Is.False);
        Assert.That(geom.IsPropertiesExpanded, Is.False);
        Assert.That(geom.IsPointsTableExpanded, Is.False);
        Assert.That(host.ShowWorkspaceLeftColumn, Is.False);
        Assert.That(host.ShowWorkspaceRightColumn, Is.False);
        Assert.That(host.ShowWorkspaceBottomSplitter, Is.False);
        Assert.That(host.WorkspaceLeftColumnWidth.Value, Is.EqualTo(0));
        Assert.That(host.WorkspaceRightColumnWidth.Value, Is.EqualTo(0));
        Assert.That(host.WorkspaceGeometryTableRowHeight.Value, Is.EqualTo(0));

        geom.ToggleExplorerCommand.Execute(null);
        geom.TogglePropertiesCommand.Execute(null);
        geom.TogglePointsTableCommand.Execute(null);

        Assert.That(host.ShowWorkspaceLeftColumn, Is.True);
        Assert.That(host.ShowWorkspaceRightColumn, Is.True);
        Assert.That(host.ShowWorkspaceBottomSplitter, Is.True);
        Assert.That(host.WorkspaceLeftColumnWidth.Value, Is.EqualTo(MainWindowViewModel.WorkspaceExpandedLeftWidth));
        Assert.That(host.WorkspaceRightColumnWidth.Value, Is.EqualTo(MainWindowViewModel.WorkspaceExpandedRightWidth));
        Assert.That(host.WorkspaceGeometryTableRowHeight.Value, Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));
    }

    [Test]
    public void GeometryDetails_Toggle_DoesNotChangeColumnWidth()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("GEOMETRY");
        var before = host.WorkspaceLeftColumnWidth;

        host.Workspace.Geometry.ToggleDetailsCommand.Execute(null);

        Assert.That(host.Workspace.Geometry.IsDetailsExpanded, Is.False);
        Assert.That(host.WorkspaceLeftColumnWidth, Is.EqualTo(before));
        Assert.That(host.WorkspaceLeftColumnWidth.Value, Is.EqualTo(MainWindowViewModel.WorkspaceExpandedLeftWidth));
    }

    [Test]
    public void AnalysisChrome_KeepsFullExplorerAndResultsColumns()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("ANALYSIS");

        Assert.That(host.IsAnalysisChrome, Is.True);
        Assert.That(host.WorkspaceLeftColumnWidth.Value, Is.EqualTo(MainWindowViewModel.WorkspaceExpandedLeftWidth));
        Assert.That(host.WorkspaceRightColumnWidth.Value, Is.EqualTo(MainWindowViewModel.WorkspaceExpandedRightWidth));
        Assert.That(host.WorkspaceGeometryTableRowHeight.Value, Is.EqualTo(0));
        Assert.That(host.WorkspaceMaterialsTableRowHeight.Value, Is.EqualTo(0));
    }

    [Test]
    public void GeometryRibbon_AfterMaterials_HidesMaterialsChrome()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MATERIALS");
        Assert.That(host.IsMaterialsWorkspace, Is.True);

        host.Workspace.SelectRibbonTabCommand.Execute("GEOMETRY");

        Assert.That(host.IsGeometryWorkspace, Is.True);
        Assert.That(host.IsMaterialsWorkspace, Is.False);
        Assert.That(host.SelectedTabIndex, Is.EqualTo(1));
        Assert.That(host.WorkspaceBottomChromeRowHeight.Value,
            Is.EqualTo(host.WorkspaceGeometryTableRowHeight.Value));
        Assert.That(host.WorkspaceMaterialsTableRowHeight.Value, Is.EqualTo(0));
    }

    [Test]
    public void MaterialsRibbon_OpensMaterialsWorkspacePanels()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MATERIALS");

        Assert.That(host.Workspace.IsMaterialsRibbon, Is.True);
        Assert.That(host.IsMaterialsWorkspace, Is.True);
        Assert.That(host.IsAnalysisChrome, Is.False);
        Assert.That(host.SelectedTabIndex, Is.EqualTo(2));
        Assert.That(host.ShowWorkspaceRightColumn, Is.True);
        Assert.That(host.WorkspaceRightColumnWidth.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedRightWidth));
        Assert.That(host.WorkspaceBottomChromeRowHeight.Value, Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));
        Assert.That(host.Workspace.Materials.Roots.Select(r => r.Title), Is.EqualTo(new[]
        {
            "Tools", "All Materials"
        }));
        var tools = host.Workspace.Materials.Roots[0];
        Assert.That(tools.Children.Select(c => c.Title), Is.EqualTo(new[]
        {
            "Layering controls", "Project materials", "Layer material assignment"
        }));
        Assert.That(host.Workspace.Materials.Roots[1].Children.Select(c => c.Title), Is.EqualTo(new[]
        {
            "Soil", "Rock", "Structural", "Interface"
        }));
    }

    [Test]
    public void MaterialsExplorer_CountsAndSelectsExistingTools()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MATERIALS");
        host.MaterialPropertiesVM.Materials.Clear();
        host.MaterialPropertiesVM.Materials.Add(new MaterialModel
        {
            Id = 1,
            Name = "Clay",
            FailureCriterion = FailureCriterion.MohrCoulomb
        });
        host.Workspace.Materials.Refresh();

        Assert.That(host.Workspace.Materials.TotalCount, Is.EqualTo(1));
        Assert.That(host.Workspace.Materials.SoilCount, Is.EqualTo(1));
        Assert.That(host.Workspace.Materials.InterfaceCount, Is.EqualTo(0));

        var layering = host.Workspace.Materials.Roots[0].Children.First(c => c.Title == "Layering controls");
        host.Workspace.Materials.SelectedNode = layering;
        Assert.That(host.SelectedTabIndex, Is.EqualTo(2));
        Assert.That(host.MaterialPropertiesVM.SelectedMaterialSubtabIndex,
            Is.EqualTo(MaterialPropertiesViewModel.LayeringControlsSubtabIndex));

        var project = host.Workspace.Materials.Roots[0].Children.First(c => c.Title == "Project materials");
        host.Workspace.Materials.SelectedNode = project;
        Assert.That(host.MaterialPropertiesVM.SelectedMaterialSubtabIndex,
            Is.EqualTo(MaterialPropertiesViewModel.ProjectMaterialsSubtabIndex));

        var assignment = host.Workspace.Materials.Roots[0].Children.First(c => c.Title == "Layer material assignment");
        host.Workspace.Materials.SelectedNode = assignment;
        Assert.That(host.MaterialPropertiesVM.SelectedMaterialSubtabIndex,
            Is.EqualTo(MaterialPropertiesViewModel.LayerMaterialAssignmentSubtabIndex));
    }

    [Test]
    public void MaterialsExplorer_SelectedGroup_SeedsNewMaterialCriterion()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MATERIALS");
        host.MaterialPropertiesVM.Materials.Clear();
        host.Workspace.Materials.Refresh();
        var groups = host.Workspace.Materials.Roots[1].Children;

        host.Workspace.Materials.SelectedNode = groups.First(c => c.Title == "Soil");
        Assert.That(host.Workspace.Materials.SelectedMaterialGroup, Is.EqualTo("Soil"));
        Assert.That(host.MaterialPropertiesVM.NewMaterialFailureCriterion, Is.EqualTo(FailureCriterion.MohrCoulomb));

        host.Workspace.Materials.SelectedNode = groups.First(c => c.Title == "Rock");
        Assert.That(host.MaterialPropertiesVM.NewMaterialFailureCriterion, Is.EqualTo(FailureCriterion.HoekBrown));

        host.Workspace.Materials.SelectedNode = groups.First(c => c.Title == "Structural");
        Assert.That(host.MaterialPropertiesVM.NewMaterialFailureCriterion, Is.EqualTo(FailureCriterion.Elastic));

        host.Workspace.Materials.SelectedNode = groups.First(c => c.Title == "Soil");
        host.MaterialPropertiesVM.RequestAddMaterialDialog = vm => true;
        host.MaterialPropertiesVM.OpenAddMaterialDialogCommand.Execute(null);
        host.Workspace.Materials.Refresh();

        var added = host.MaterialPropertiesVM.Materials.Last();
        Assert.That(added.FailureCriterion, Is.EqualTo(FailureCriterion.MohrCoulomb));
        var soil = host.Workspace.Materials.Roots[1].Children.First(c => c.Title == "Soil");
        var structural = host.Workspace.Materials.Roots[1].Children.First(c => c.Title == "Structural");
        Assert.That(soil.Children.Any(c => c.Payload == added), Is.True);
        Assert.That(structural.Children.Any(c => c.Payload == added), Is.False);
    }

    [Test]
    public void MaterialsExplorer_ShowAssignButton_OnlyOnMaterialLeaves()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MATERIALS");
        host.MaterialPropertiesVM.Materials.Clear();
        host.MaterialPropertiesVM.Materials.Add(new MaterialModel
        {
            Id = 1,
            Name = "Clay",
            FailureCriterion = FailureCriterion.MohrCoulomb
        });
        host.Workspace.Materials.Refresh();

        var tools = host.Workspace.Materials.Roots[0];
        var all = host.Workspace.Materials.Roots[1];
        var soil = all.Children.First(c => c.Title == "Soil");
        var clay = soil.Children.First(c => c.Title == "Clay");

        Assert.That(clay.Kind, Is.EqualTo(ExplorerNodeKind.Material));
        Assert.That(clay.ShowAssignButton, Is.True);
        Assert.That(tools.ShowAssignButton, Is.False);
        Assert.That(tools.Children.All(c => !c.ShowAssignButton), Is.True);
        Assert.That(all.ShowAssignButton, Is.False);
        Assert.That(all.Children.All(c => !c.ShowAssignButton), Is.True);
    }

    [Test]
    public void MaterialsExplorer_AllMaterials_DeleteClearsLibrary()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MATERIALS");
        var materials = host.MaterialPropertiesVM;
        materials.Materials.Clear();
        materials.Materials.Add(new MaterialModel { Id = 1, Name = "Clay" });
        materials.Materials.Add(new MaterialModel { Id = 2, Name = "Sand" });
        host.Workspace.Materials.Refresh();

        var all = host.Workspace.Materials.Roots.Single(r => r.Title == "All Materials");
        host.Workspace.Materials.SelectedNode = all;

        Assert.That(materials.DeleteTargetsAllMaterials, Is.True);
        Assert.That(materials.RemoveMaterialCommand.CanExecute(null), Is.True);

        materials.ClearAllMaterials();

        Assert.That(materials.Materials, Is.Empty);
        Assert.That(materials.SelectedMaterialModel, Is.Null);
    }

    [Test]
    public void MaterialsExplorer_ProjectMaterialsTool_DeleteClearsLibrary()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MATERIALS");
        var materials = host.MaterialPropertiesVM;
        materials.Materials.Clear();
        materials.Materials.Add(new MaterialModel { Id = 1, Name = "Clay" });
        materials.Materials.Add(new MaterialModel { Id = 2, Name = "Sand" });
        host.Workspace.Materials.Refresh();

        var tools = host.Workspace.Materials.Roots.Single(r => r.Title == "Tools");
        var project = tools.Children.Single(c => c.Title == "Project materials");
        host.Workspace.Materials.SelectedNode = project;

        Assert.That(materials.DeleteTargetsAllMaterials, Is.True);
        Assert.That(materials.RemoveMaterialCommand.CanExecute(null), Is.True);

        materials.ClearAllMaterials();

        Assert.That(materials.Materials, Is.Empty);
        Assert.That(materials.SelectedMaterialModel, Is.Null);
    }

    [Test]
    public void MaterialsExplorer_AllMaterials_HidesProjectMaterialsToolbar()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MATERIALS");
        var materials = host.MaterialPropertiesVM;
        materials.Materials.Clear();
        materials.Materials.Add(new MaterialModel
        {
            Id = 1,
            Name = "Clay",
            FailureCriterion = FailureCriterion.MohrCoulomb
        });
        host.Workspace.Materials.Refresh();

        var tools = host.Workspace.Materials.Roots.Single(r => r.Title == "Tools");
        var project = tools.Children.Single(c => c.Title == "Project materials");
        host.Workspace.Materials.SelectedNode = project;
        Assert.That(materials.ShowProjectMaterialsToolbar, Is.True);

        var all = host.Workspace.Materials.Roots.Single(r => r.Title == "All Materials");
        host.Workspace.Materials.SelectedNode = all;
        Assert.That(materials.ShowProjectMaterialsToolbar, Is.False);

        var soil = all.Children.Single(c => c.Title == "Soil");
        host.Workspace.Materials.SelectedNode = soil;
        Assert.That(materials.ShowProjectMaterialsToolbar, Is.False);

        var clay = soil.Children.Single(c => c.Title == "Clay");
        host.Workspace.Materials.SelectedNode = clay;
        Assert.That(materials.ShowProjectMaterialsToolbar, Is.False);

        host.Workspace.Materials.SelectedNode = project;
        Assert.That(materials.ShowProjectMaterialsToolbar, Is.True);
    }

    [Test]
    public void MaterialsExplorer_EditMaterialName_UpdatesAllMaterialsLeaf()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MATERIALS");
        var materials = host.MaterialPropertiesVM;
        materials.Materials.Clear();
        var clay = new MaterialModel
        {
            Id = 1,
            Name = "Clay",
            FailureCriterion = FailureCriterion.MohrCoulomb
        };
        materials.Materials.Add(clay);
        host.Workspace.Materials.Refresh();

        var soil = host.Workspace.Materials.Roots.Single(r => r.Title == "All Materials")
            .Children.Single(c => c.Title == "Soil");
        Assert.That(soil.Children.Single().Title, Is.EqualTo("Clay"));

        clay.Name = "Renamed Clay";

        var renamed = host.Workspace.Materials.Roots.Single(r => r.Title == "All Materials")
            .Children.Single(c => c.Title == "Soil")
            .Children.Single();
        Assert.That(renamed.Title, Is.EqualTo("Renamed Clay"));
        Assert.That(renamed.Payload, Is.SameAs(clay));
    }

    [Test]
    public void GeometryExplorer_FolderSelection_ShowsOnlyMatchingBottomTable()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("GEOMETRY");
        var explorer = host.Workspace.Geometry;
        var root = explorer.Roots[0];

        explorer.SelectedNode = Child(root, "Points");
        AssertOnlyTable(explorer, GeometryBottomTableKind.Points);

        explorer.SelectedNode = Child(root, "Lines");
        AssertOnlyTable(explorer, GeometryBottomTableKind.Lines);

        explorer.SelectedNode = Child(root, "Surfaces");
        AssertOnlyTable(explorer, GeometryBottomTableKind.Surfaces);

        explorer.SelectedNode = Child(root, "Regions");
        AssertOnlyTable(explorer, GeometryBottomTableKind.Regions);

        explorer.SelectedNode = Child(root, "Blocks");
        AssertOnlyTable(explorer, GeometryBottomTableKind.Blocks);
    }

    [Test]
    public void GeometryExplorer_NamedRegionLeaf_ShowsRegionsTableNotSurfaces()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("GEOMETRY");
        var geometry = host.GeometryDefinitionVM;
        var explorer = host.Workspace.Geometry;
        geometry.GeometrySurfaces.Clear();
        geometry.GeometrySurfaces.Add(new GeometrySurface(1, "Clay"));
        explorer.Refresh();

        var regions = Child(explorer.Roots[0], "Regions");
        var clay = regions.Children.Single(c => c.Title == "Clay");
        Assert.That(clay.Payload, Is.TypeOf<RegionExplorerPayload>());

        explorer.SelectedNode = clay;

        AssertOnlyTable(explorer, GeometryBottomTableKind.Regions);
        Assert.That(explorer.SelectedRegionRow, Is.Not.Null);
        Assert.That(explorer.SelectedRegionRow!.Name, Is.EqualTo("Clay"));
        Assert.That(explorer.IsSurfacesTable, Is.False);
    }

    private static void AssertOnlyTable(GeometryWorkspaceViewModel explorer, GeometryBottomTableKind expected)
    {
        Assert.That(explorer.BottomTableKind, Is.EqualTo(expected));
        Assert.That(explorer.IsPointsTable, Is.EqualTo(expected == GeometryBottomTableKind.Points));
        Assert.That(explorer.IsLinesTable, Is.EqualTo(expected == GeometryBottomTableKind.Lines));
        Assert.That(explorer.IsSurfacesTable, Is.EqualTo(expected == GeometryBottomTableKind.Surfaces));
        Assert.That(explorer.IsRegionsTable, Is.EqualTo(expected == GeometryBottomTableKind.Regions));
        Assert.That(explorer.IsBlocksTable, Is.EqualTo(expected == GeometryBottomTableKind.Blocks));
    }

    [Test]
    public void MeshRibbon_OpensMeshWorkspacePanels()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MESH");

        Assert.That(host.Workspace.IsMeshRibbon, Is.True);
        Assert.That(host.IsMeshWorkspace, Is.True);
        Assert.That(host.IsGeometryWorkspace, Is.False);
        Assert.That(host.IsMaterialsWorkspace, Is.False);
        Assert.That(host.IsAnalysisChrome, Is.False);
        Assert.That(host.SelectedTabIndex, Is.EqualTo(3));
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.True);
        Assert.That(host.ShowWorkspaceRightColumn, Is.False);
        Assert.That(host.WorkspaceRightColumnWidth.Value, Is.EqualTo(0));
        Assert.That(host.WorkspaceBottomChromeRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));
        Assert.That(host.WorkspaceMeshTableRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));
        Assert.That(host.Workspace.Mesh.QualityMinAngleText, Is.EqualTo("—"));
        Assert.That(host.Workspace.Mesh.QualityAspectText, Is.EqualTo("—"));
        Assert.That(host.Workspace.Mesh.QualitySkewText, Is.EqualTo("—"));
        Assert.That(host.Workspace.Mesh.QualityJacobianText, Is.EqualTo("—"));
        Assert.That(host.Workspace.Mesh.Roots, Has.Count.EqualTo(1));
        Assert.That(host.Workspace.Mesh.Roots[0].Title, Is.EqualTo("Mesh"));
        Assert.That(host.Workspace.Mesh.Roots[0].Children.Select(c => c.Title), Is.EqualTo(new[]
        {
            "Global Mesh (Structured)", "Elements by Etype", "Elements by Kind", "Diagnostics"
        }));
        Assert.That(host.Workspace.Mesh.ElementCount, Is.EqualTo(0));
        Assert.That(host.Workspace.Mesh.NodeCount, Is.EqualTo(0));
        Assert.That(host.Workspace.Mesh.QualityGrade, Is.EqualTo("—"));
    }

    [Test]
    public void MeshExplorer_GroupsRealInformationRows_NotInventedSets()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MESH");
        host.MeshVM.MeshInformationRows.Add(new MeshInformationRow
        {
            ElementId = 1,
            Etype = 2,
            Kind = "Q8",
            Status = "Active",
            Node1 = 1,
            Node2 = 2,
            Node3 = 3,
            Node4 = 4
        });
        host.MeshVM.MeshInformationRows.Add(new MeshInformationRow
        {
            ElementId = 2,
            Etype = 2,
            Kind = "Q8",
            Status = "Excavated",
            Node1 = 4,
            Node2 = 5,
            Node3 = 6,
            Node4 = 7
        });

        var mesh = host.Workspace.Mesh;
        Assert.That(mesh.ElementCount, Is.EqualTo(2));
        Assert.That(mesh.NodeCount, Is.EqualTo(7));
        Assert.That(mesh.ActiveElementCount, Is.EqualTo(1));
        Assert.That(mesh.InactiveElementCount, Is.EqualTo(1));
        Assert.That(mesh.ElementTypesLabel, Is.EqualTo("Q8"));

        var etypes = mesh.Roots[0].Children.First(c => c.Title == "Elements by Etype");
        Assert.That(etypes.Count, Is.EqualTo(1));
        Assert.That(etypes.Children, Has.Count.EqualTo(1));
        Assert.That(etypes.Children[0].Title, Is.EqualTo("Etype 2"));
        Assert.That(etypes.Children[0].Count, Is.EqualTo(2));

        var kinds = mesh.Roots[0].Children.First(c => c.Title == "Elements by Kind");
        Assert.That(kinds.Children[0].Title, Is.EqualTo("Q8"));
        Assert.That(kinds.Children[0].Count, Is.EqualTo(2));

        var diag = mesh.Roots[0].Children.First(c => c.Title == "Diagnostics");
        Assert.That(diag.Count, Is.EqualTo(1));
        Assert.That(diag.Children, Has.Count.EqualTo(1));
        Assert.That(diag.Children[0].Title, Does.Contain("Element 2"));
        Assert.That(diag.Children[0].Payload, Is.TypeOf<MeshInformationRow>());
    }

    [Test]
    public void MeshRibbon_ClearMesh_RemovesAllElementsAndNodes()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MESH");
        host.MeshVM.MeshInformationRows.Add(new MeshInformationRow
        {
            ElementId = 1,
            Etype = 2,
            Kind = "T3",
            Status = "Active",
            Node1 = 1,
            Node2 = 2,
            Node3 = 3
        });
        host.MeshVM.DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay { NodeId = 1 });
        host.MeshVM.MeshElements.Add(new MeshElementDisplay { DisplayId = 1, IsActive = true });
        host.MeshVM.IsDiscretized = true;

        var mesh = host.Workspace.Mesh;
        Assert.That(mesh.ElementCount, Is.EqualTo(1));
        Assert.That(mesh.NodeCount, Is.GreaterThan(0));

        mesh.ClearCommand.Execute(null);

        Assert.That(host.MeshVM.MeshInformationRows, Is.Empty);
        Assert.That(host.MeshVM.MeshElements, Is.Empty);
        Assert.That(host.MeshVM.DiscretizationNodes, Is.Empty);
        Assert.That(host.MeshVM.IsDiscretized, Is.False);
        Assert.That(mesh.ElementCount, Is.EqualTo(0));
        Assert.That(mesh.NodeCount, Is.EqualTo(0));
        Assert.That(mesh.QualityGrade, Is.EqualTo("—"));
        Assert.That(host.IsMeshWorkspace, Is.True);
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.True);
    }

    [Test]
    public void MeshRibbon_AfterGeometryAndMaterials_HidesOtherChrome()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("GEOMETRY");
        host.Workspace.SelectRibbonTabCommand.Execute("MATERIALS");
        host.Workspace.SelectRibbonTabCommand.Execute("MESH");

        Assert.That(host.IsMeshWorkspace, Is.True);
        Assert.That(host.IsGeometryWorkspace, Is.False);
        Assert.That(host.IsMaterialsWorkspace, Is.False);
        Assert.That(host.IsAnalysisChrome, Is.False);
        Assert.That(host.SelectedTabIndex, Is.EqualTo(3));
        Assert.That(host.Workspace.RibbonTab, Is.EqualTo("MESH"));
        Assert.That(host.ShowWorkspaceRightColumn, Is.False);
        Assert.That(host.WorkspaceRightColumnWidth.Value, Is.EqualTo(0));
    }

    [Test]
    public void MeshRibbon_ToggleControls_AffectsCenterColumnOnly()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MESH");
        var mesh = host.Workspace.Mesh;

        Assert.That(mesh.IsControlsExpanded, Is.True);
        Assert.That(mesh.IsTableExpanded, Is.True);
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.True);
        Assert.That(host.WorkspaceMeshTableRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));

        mesh.ToggleControlsCommand.Execute(null);

        Assert.That(mesh.IsControlsExpanded, Is.False);
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.False);
        Assert.That(mesh.IsTableExpanded, Is.True);
        Assert.That(host.WorkspaceMeshTableRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));
        Assert.That(host.WorkspaceBottomChromeRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));
        Assert.That(host.ShowWorkspaceRightColumn, Is.False);
        Assert.That(host.WorkspaceRightColumnWidth.Value, Is.EqualTo(0));
        Assert.That(host.IsMeshWorkspace, Is.True);
        Assert.That(host.SelectedTabIndex, Is.EqualTo(3));

        mesh.ToggleControlsCommand.Execute(null);

        Assert.That(mesh.IsControlsExpanded, Is.True);
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.True);
        Assert.That(mesh.IsTableExpanded, Is.True);
    }

    [Test]
    public void MeshRibbon_ToggleTable_AffectsInformationRowOnly()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MESH");
        var mesh = host.Workspace.Mesh;

        Assert.That(mesh.IsTableExpanded, Is.True);
        Assert.That(mesh.IsControlsExpanded, Is.True);
        Assert.That(host.WorkspaceMeshTableRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.True);

        mesh.ToggleTableCommand.Execute(null);

        Assert.That(mesh.IsTableExpanded, Is.False);
        Assert.That(host.WorkspaceMeshTableRowHeight.Value, Is.EqualTo(0));
        Assert.That(host.WorkspaceBottomChromeRowHeight.Value, Is.EqualTo(0));
        Assert.That(mesh.IsControlsExpanded, Is.True);
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.True);
        Assert.That(host.ShowWorkspaceRightColumn, Is.False);
        Assert.That(host.IsMeshWorkspace, Is.True);
        Assert.That(host.SelectedTabIndex, Is.EqualTo(3));

        mesh.ToggleTableCommand.Execute(null);

        Assert.That(mesh.IsTableExpanded, Is.True);
        Assert.That(host.WorkspaceMeshTableRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));
        Assert.That(mesh.IsControlsExpanded, Is.True);
    }

    [Test]
    public void LoadsRibbon_OpensLoadsWorkspacePanels()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("LOADS");

        Assert.That(host.Workspace.IsLoadsRibbon, Is.True);
        Assert.That(host.IsLoadsWorkspace, Is.True);
        Assert.That(host.IsGeometryWorkspace, Is.False);
        Assert.That(host.IsMaterialsWorkspace, Is.False);
        Assert.That(host.IsMeshWorkspace, Is.False);
        Assert.That(host.IsAnalysisChrome, Is.False);
        Assert.That(host.SelectedTabIndex, Is.EqualTo(4));
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.True);
        Assert.That(host.ShowWorkspaceRightColumn, Is.True);
        Assert.That(host.WorkspaceRightColumnWidth.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedRightWidth));
        Assert.That(host.WorkspaceLoadsTableRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));
        Assert.That(host.WorkspaceBottomChromeRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));
        Assert.That(host.Workspace.Loads.Roots, Has.Count.EqualTo(1));
        Assert.That(host.Workspace.Loads.Roots[0].Title, Is.EqualTo("All Loads"));
        Assert.That(host.Workspace.Loads.Roots[0].Children.Select(c => c.Title), Is.EqualTo(new[]
        {
            "Nodal Loads", "Line Loads", "Triangular Edge", "Field Stress", "Seismic"
        }));
    }

    [Test]
    public void LoadsExplorer_GroupsRealLoadRows_NotInventedCombinations()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("LOADS");
        host.LoadConditionsVM.Loads.Clear();
        host.LoadConditionsVM.Loads.Add(new LoadModel { Type = LoadType.Point, Name = "P1", Magnitude = 5 });
        host.LoadConditionsVM.Loads.Add(new LoadModel { Type = LoadType.Line, Name = "L1", Magnitude = 8 });
        host.LoadConditionsVM.Loads.Add(new LoadModel { Type = LoadType.Triangular, Name = "T1", Magnitude = 2 });

        var loads = host.Workspace.Loads;
        Assert.That(loads.NodalCount, Is.EqualTo(1));
        Assert.That(loads.LineCount, Is.EqualTo(1));
        Assert.That(loads.TriangularCount, Is.EqualTo(1));
        Assert.That(loads.TotalLoadCount, Is.EqualTo(3));

        var titles = loads.Roots[0].Children.Select(c => c.Title).ToList();
        Assert.That(titles, Does.Not.Contain("Load Combinations"));
        Assert.That(titles, Does.Not.Contain("Surface Load"));
        Assert.That(loads.Roots[0].Children.First(c => c.Title == "Nodal Loads").Children[0].Title, Is.EqualTo("P1"));
        Assert.That(loads.Roots[0].Children.First(c => c.Title == "Line Loads").Children[0].Title, Is.EqualTo("L1"));
        Assert.That(loads.Roots[0].Children.First(c => c.Title == "Triangular Edge").Children[0].Title, Is.EqualTo("T1"));
        Assert.That(loads.Roots[0].Children.First(c => c.Title == "Triangular Edge").Children[0].Payload, Is.TypeOf<LoadModel>());
    }

    [Test]
    public void LoadsRibbon_AddNodalAndLine_CreateExistingLoadTypes()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("LOADS");
        host.LoadConditionsVM.Loads.Clear();
        var loads = host.Workspace.Loads;

        loads.AddNodalLoadCommand.Execute(null);
        Assert.That(host.LoadConditionsVM.Loads, Has.Count.EqualTo(1));
        Assert.That(host.LoadConditionsVM.Loads[0].Type, Is.EqualTo(LoadType.Point));
        Assert.That(host.LoadConditionsVM.SelectedLoad, Is.SameAs(host.LoadConditionsVM.Loads[0]));

        loads.AddLineLoadCommand.Execute(null);
        Assert.That(host.LoadConditionsVM.Loads, Has.Count.EqualTo(2));
        Assert.That(host.LoadConditionsVM.Loads[1].Type, Is.EqualTo(LoadType.Line));
        Assert.That(loads.NodalCount, Is.EqualTo(1));
        Assert.That(loads.LineCount, Is.EqualTo(1));
    }

    [Test]
    public void LoadsRibbon_ToggleTable_AffectsInformationRowOnly()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("LOADS");
        var loads = host.Workspace.Loads;

        Assert.That(loads.IsTableExpanded, Is.True);
        Assert.That(loads.IsPropertiesExpanded, Is.True);
        Assert.That(host.WorkspaceLoadsTableRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.True);

        loads.ToggleTableCommand.Execute(null);

        Assert.That(loads.IsTableExpanded, Is.False);
        Assert.That(host.WorkspaceLoadsTableRowHeight.Value, Is.EqualTo(0));
        Assert.That(host.WorkspaceBottomChromeRowHeight.Value, Is.EqualTo(0));
        Assert.That(loads.IsPropertiesExpanded, Is.True);
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.True);
        Assert.That(host.IsLoadsWorkspace, Is.True);
        Assert.That(host.SelectedTabIndex, Is.EqualTo(4));

        loads.ToggleTableCommand.Execute(null);
        Assert.That(loads.IsTableExpanded, Is.True);
        Assert.That(host.WorkspaceLoadsTableRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));
    }

    [Test]
    public void LoadsInspector_SelectedPointLoad_ShowsMockupSectionsFromRealFields()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("LOADS");
        host.LoadConditionsVM.Loads.Clear();
        var load = new LoadModel
        {
            Type = LoadType.Point,
            Name = "Surcharge Load",
            IdCode = "SL-01",
            Magnitude = 20,
            Orientation = LoadOrientation.VerticalDown,
            Direction = LoadDirection.Y,
            IsEnabled = true,
            ApplyFromStage = 1
        };
        host.LoadConditionsVM.Loads.Add(load);
        host.LoadConditionsVM.SelectedLoad = load;

        var inspector = host.Workspace.Loads;
        Assert.That(inspector.HasSelectedLoad, Is.True);
        Assert.That(inspector.InspectorName, Is.EqualTo("Surcharge Load (SL-01)"));
        Assert.That(inspector.InspectorType, Is.EqualTo("Point Load"));
        Assert.That(inspector.InspectorDirection, Is.EqualTo("Vertical"));
        Assert.That(inspector.InspectorValue, Is.EqualTo("20.00 kN"));
        Assert.That(inspector.InspectorDistribution, Is.EqualTo("Uniform"));
        Assert.That(inspector.InspectorStatus, Is.EqualTo("Active"));
        Assert.That(inspector.InspectorSurfaces, Is.EqualTo("—"));
        Assert.That(inspector.InspectorArea, Is.EqualTo("—"));
        Assert.That(inspector.InspectorPreviewCaption, Does.Contain("Vertical"));
        Assert.That(inspector.InspectorStageFrom, Is.EqualTo("Stage 1"));
        Assert.That(inspector.InspectorStageTo, Is.EqualTo("All Subsequent"));
        Assert.That(inspector.InspectorResultantVertical, Is.EqualTo((-20.0).ToString("N2")));
        Assert.That(inspector.InspectorResultantHorizontalX, Is.EqualTo(0.0.ToString("N2")));
        Assert.That(inspector.InspectorResultantHorizontalY, Is.EqualTo("0.00"));
    }

    [Test]
    public void LoadsInspector_TriangularSurface_LeavesAreaAndResultantBlank()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("LOADS");
        host.LoadConditionsVM.Loads.Clear();
        var load = new LoadModel
        {
            Type = LoadType.Triangular,
            Name = "Surface",
            Magnitude = 20,
            Orientation = LoadOrientation.VerticalDown
        };
        host.LoadConditionsVM.Loads.Add(load);
        host.LoadConditionsVM.SelectedLoad = load;

        var inspector = host.Workspace.Loads;
        Assert.That(inspector.InspectorType, Is.EqualTo("Surface Load"));
        Assert.That(inspector.InspectorSurfaces, Is.EqualTo("1"));
        Assert.That(inspector.InspectorArea, Is.EqualTo("—"));
        Assert.That(inspector.InspectorResultantVertical, Is.EqualTo("—"));
        Assert.That(inspector.InspectorShowsLength, Is.True);
    }

    [Test]
    public void GeometryRibbon_ViewToggles_AffectExplorerPropertiesAndTable()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("GEOMETRY");
        var geometry = host.Workspace.Geometry;

        Assert.That(geometry.IsExplorerExpanded, Is.True);
        Assert.That(geometry.IsPropertiesExpanded, Is.True);
        Assert.That(geometry.IsPointsTableExpanded, Is.True);
        Assert.That(host.WorkspaceLeftColumnWidth.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedLeftWidth));
        Assert.That(host.WorkspaceRightColumnWidth.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedRightWidth));
        Assert.That(host.WorkspaceGeometryTableRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));

        geometry.ToggleExplorerCommand.Execute(null);
        Assert.That(geometry.IsExplorerExpanded, Is.False);
        Assert.That(host.ShowWorkspaceLeftColumn, Is.False);
        Assert.That(host.WorkspaceLeftColumnWidth.Value, Is.EqualTo(0));

        geometry.ToggleDetailsCommand.Execute(null);
        Assert.That(geometry.IsDetailsExpanded, Is.False);
        Assert.That(host.ShowWorkspaceLeftColumn, Is.False);

        geometry.ToggleExplorerCommand.Execute(null);
        Assert.That(host.ShowWorkspaceLeftColumn, Is.True);
        Assert.That(host.WorkspaceLeftColumnWidth.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedLeftWidth));
        Assert.That(geometry.IsDetailsExpanded, Is.False);

        geometry.TogglePropertiesCommand.Execute(null);
        Assert.That(geometry.IsPropertiesExpanded, Is.False);
        Assert.That(host.ShowWorkspaceRightColumn, Is.False);
        Assert.That(host.WorkspaceRightColumnWidth.Value, Is.EqualTo(0));

        geometry.TogglePointsTableCommand.Execute(null);
        Assert.That(geometry.IsPointsTableExpanded, Is.False);
        Assert.That(host.ShowWorkspaceBottomSplitter, Is.False);
        Assert.That(host.WorkspaceGeometryTableRowHeight.Value, Is.EqualTo(0));
        Assert.That(host.IsGeometryWorkspace, Is.True);
    }

    [Test]
    public void MaterialsRibbon_ViewToggles_AffectExplorerTableAndInspector()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MATERIALS");
        var materials = host.Workspace.Materials;

        Assert.That(materials.IsExplorerExpanded, Is.True);
        Assert.That(materials.IsTableExpanded, Is.True);
        Assert.That(materials.IsInspectorExpanded, Is.True);
        Assert.That(host.ShowWorkspaceRightColumn, Is.True);
        Assert.That(host.WorkspaceRightColumnWidth.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedRightWidth));
        Assert.That(host.WorkspaceMaterialsTableRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));

        materials.ToggleExplorerCommand.Execute(null);
        Assert.That(materials.IsExplorerExpanded, Is.False);
        Assert.That(host.ShowWorkspaceLeftColumn, Is.False);
        Assert.That(host.WorkspaceLeftColumnWidth.Value, Is.EqualTo(0));

        materials.ToggleTableCommand.Execute(null);
        Assert.That(materials.IsTableExpanded, Is.False);
        Assert.That(host.WorkspaceMaterialsTableRowHeight.Value, Is.EqualTo(0));
        Assert.That(host.WorkspaceBottomChromeRowHeight.Value, Is.EqualTo(0));

        materials.ToggleInspectorCommand.Execute(null);
        Assert.That(materials.IsInspectorExpanded, Is.False);
        Assert.That(host.ShowWorkspaceRightColumn, Is.False);
        Assert.That(host.WorkspaceRightColumnWidth.Value, Is.EqualTo(0));
        Assert.That(host.IsMaterialsWorkspace, Is.True);
    }

    [Test]
    public void MeshRibbon_ViewToggles_ReuseExistingPanelFlags()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MESH");
        var mesh = host.Workspace.Mesh;

        Assert.That(mesh.IsExplorerExpanded, Is.True);
        Assert.That(mesh.IsControlsExpanded, Is.True);
        Assert.That(mesh.IsTableExpanded, Is.True);
        Assert.That(mesh.IsStatsExpanded, Is.True);

        mesh.ToggleExplorerCommand.Execute(null);
        Assert.That(host.ShowWorkspaceLeftColumn, Is.False);
        Assert.That(host.WorkspaceLeftColumnWidth.Value, Is.EqualTo(0));

        mesh.ToggleControlsCommand.Execute(null);
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.False);
        Assert.That(mesh.IsTableExpanded, Is.True);

        mesh.ToggleTableCommand.Execute(null);
        Assert.That(host.WorkspaceMeshTableRowHeight.Value, Is.EqualTo(0));

        mesh.ToggleStatsCommand.Execute(null);
        Assert.That(mesh.IsStatsExpanded, Is.False);
        Assert.That(host.IsMeshWorkspace, Is.True);
        Assert.That(host.ShowWorkspaceRightColumn, Is.False);
    }

    [Test]
    public void LoadsRibbon_ViewToggles_AffectExplorerPropertiesTableAndInspector()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("LOADS");
        var loads = host.Workspace.Loads;

        Assert.That(loads.IsExplorerExpanded, Is.True);
        Assert.That(loads.IsPropertiesExpanded, Is.True);
        Assert.That(loads.IsTableExpanded, Is.True);
        Assert.That(loads.IsInspectorExpanded, Is.True);
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.True);
        Assert.That(host.ShowWorkspaceRightColumn, Is.True);

        loads.ToggleExplorerCommand.Execute(null);
        Assert.That(host.ShowWorkspaceLeftColumn, Is.False);
        Assert.That(host.WorkspaceLeftColumnWidth.Value, Is.EqualTo(0));

        loads.TogglePropertiesCommand.Execute(null);
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.False);
        Assert.That(loads.IsTableExpanded, Is.True);

        loads.ToggleTableCommand.Execute(null);
        Assert.That(host.WorkspaceLoadsTableRowHeight.Value, Is.EqualTo(0));

        loads.ToggleInspectorCommand.Execute(null);
        Assert.That(host.ShowWorkspaceRightColumn, Is.False);
        Assert.That(host.WorkspaceRightColumnWidth.Value, Is.EqualTo(0));
        Assert.That(host.IsLoadsWorkspace, Is.True);
    }

    [Test]
    public void AnalysisRibbon_ViewToggles_AffectExplorerTypeLogAndResults()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("ANALYSIS");
        var workspace = host.Workspace;

        Assert.That(host.IsAnalysisChrome, Is.True);
        Assert.That(workspace.IsExplorerExpanded, Is.True);
        Assert.That(workspace.IsAnalysisTypeExpanded, Is.True);
        Assert.That(workspace.IsSolverLogExpanded, Is.True);
        Assert.That(workspace.IsResultsInspectorExpanded, Is.True);
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.True);
        Assert.That(host.ShowWorkspaceRightColumn, Is.True);
        Assert.That(host.WorkspaceSolverLogRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedDashboardHeight));
        Assert.That(host.WorkspaceRightColumnWidth.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedRightWidth));

        workspace.ToggleExplorerCommand.Execute(null);
        Assert.That(workspace.IsExplorerExpanded, Is.False);
        Assert.That(host.ShowWorkspaceLeftColumn, Is.False);
        Assert.That(host.WorkspaceLeftColumnWidth.Value, Is.EqualTo(0));

        workspace.ToggleAnalysisTypeCommand.Execute(null);
        Assert.That(workspace.IsAnalysisTypeExpanded, Is.False);
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.False);

        workspace.ToggleSolverLogCommand.Execute(null);
        Assert.That(workspace.IsSolverLogExpanded, Is.False);
        Assert.That(host.WorkspaceSolverLogRowHeight.Value, Is.EqualTo(0));

        workspace.ToggleResultsInspectorCommand.Execute(null);
        Assert.That(workspace.IsResultsInspectorExpanded, Is.False);
        Assert.That(host.ShowWorkspaceRightColumn, Is.False);
        Assert.That(host.WorkspaceRightColumnWidth.Value, Is.EqualTo(0));
        Assert.That(host.IsAnalysisChrome, Is.True);
    }

    [Test]
    public void ResultsRibbon_ViewToggles_ShareAnalysisChromeFlags()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("RESULTS");

        Assert.That(host.IsResultsWorkspace, Is.True);
        Assert.That(host.IsAnalysisChrome, Is.False);
        Assert.That(host.Workspace.IsResultsRibbon, Is.True);
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.False);
        Assert.That(host.ShowWorkspaceLeftColumn, Is.True);
        Assert.That(host.ShowWorkspaceRightColumn, Is.True);
        Assert.That(host.WorkspaceLeftColumnWidth.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedResultsLeftWidth));
        Assert.That(host.WorkspaceResultsTableRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedResultsTableHeight));
        Assert.That(host.WorkspaceSolverLogRowHeight.Value, Is.EqualTo(0));

        host.Workspace.Results.ToggleExplorerCommand.Execute(null);
        host.Workspace.Results.TogglePropertiesCommand.Execute(null);
        host.Workspace.Results.ToggleTableCommand.Execute(null);

        Assert.That(host.ShowWorkspaceLeftColumn, Is.False);
        Assert.That(host.ShowWorkspaceRightColumn, Is.False);
        Assert.That(host.WorkspaceResultsTableRowHeight.Value, Is.EqualTo(0));
        Assert.That(host.IsResultsWorkspace, Is.True);
    }

    [Test]
    public void RibbonViewOptions_DefaultOn_ToggleDoesNotChangeChromeWidths()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };

        host.Workspace.SelectRibbonTabCommand.Execute("GEOMETRY");
        Assert.That(host.Workspace.Geometry.IsViewOptionsExpanded, Is.True);
        AssertViewOptionsToggleLeavesChrome(host, host.Workspace.Geometry.ToggleViewOptionsCommand);
        Assert.That(host.Workspace.Geometry.IsViewOptionsExpanded, Is.False);

        host.Workspace.SelectRibbonTabCommand.Execute("MATERIALS");
        Assert.That(host.Workspace.Materials.IsViewOptionsExpanded, Is.True);
        AssertViewOptionsToggleLeavesChrome(host, host.Workspace.Materials.ToggleViewOptionsCommand);
        Assert.That(host.Workspace.Materials.IsViewOptionsExpanded, Is.False);

        host.Workspace.SelectRibbonTabCommand.Execute("MESH");
        Assert.That(host.Workspace.Mesh.IsViewOptionsExpanded, Is.True);
        AssertViewOptionsToggleLeavesChrome(host, host.Workspace.Mesh.ToggleViewOptionsCommand);
        Assert.That(host.Workspace.Mesh.IsViewOptionsExpanded, Is.False);

        host.Workspace.SelectRibbonTabCommand.Execute("LOADS");
        Assert.That(host.Workspace.Loads.IsViewOptionsExpanded, Is.True);
        AssertViewOptionsToggleLeavesChrome(host, host.Workspace.Loads.ToggleViewOptionsCommand);
        Assert.That(host.Workspace.Loads.IsViewOptionsExpanded, Is.False);

        host.Workspace.SelectRibbonTabCommand.Execute("BOUNDARY");
        Assert.That(host.Workspace.Boundary.IsViewOptionsExpanded, Is.True);
        AssertViewOptionsToggleLeavesChrome(host, host.Workspace.Boundary.ToggleViewOptionsCommand);
        Assert.That(host.Workspace.Boundary.IsViewOptionsExpanded, Is.False);

        host.Workspace.SelectRibbonTabCommand.Execute("ANALYSIS");
        Assert.That(host.Workspace.IsViewOptionsExpanded, Is.True);
        AssertViewOptionsToggleLeavesChrome(host, host.Workspace.ToggleViewOptionsCommand);
        Assert.That(host.Workspace.IsViewOptionsExpanded, Is.False);

        host.Workspace.SelectRibbonTabCommand.Execute("RESULTS");
        Assert.That(host.Workspace.IsViewOptionsExpanded, Is.False);
        AssertViewOptionsToggleLeavesChrome(host, host.Workspace.ToggleViewOptionsCommand);
        Assert.That(host.Workspace.IsViewOptionsExpanded, Is.True);
    }

    [Test]
    public void RibbonViewOptions_GeometryLoadsBoundaryFlags_DefaultTrueAndPersist()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };

        Assert.That(host.Workspace.Geometry.ShowPoints, Is.True);
        Assert.That(host.Workspace.Geometry.ShowLines, Is.True);
        host.Workspace.Geometry.ShowPoints = false;
        host.Workspace.Geometry.ShowLines = false;
        Assert.That(host.Workspace.Geometry.ShowPoints, Is.False);
        Assert.That(host.Workspace.Geometry.ShowLines, Is.False);

        Assert.That(host.Workspace.Loads.ShowPointLoads, Is.True);
        Assert.That(host.Workspace.Loads.ShowLineLoads, Is.True);
        host.Workspace.Loads.ShowPointLoads = false;
        host.Workspace.Loads.ShowLineLoads = false;
        Assert.That(host.Workspace.Loads.ShowPointLoads, Is.False);
        Assert.That(host.Workspace.Loads.ShowLineLoads, Is.False);

        host.Workspace.SelectRibbonTabCommand.Execute("GEOMETRY");
        host.Workspace.Geometry.ShowLines = false;
        host.Workspace.Geometry.ShowPoints = false;
        Assert.That(host.GeometryDefinitionVM.ShowGeometryEdgeLinesOnCanvas, Is.False);
        Assert.That(host.GeometryDefinitionVM.ShowGeometryPointsOnCanvas, Is.False);

        host.Workspace.SelectRibbonTabCommand.Execute("LOADS");
        Assert.That(host.GeometryDefinitionVM.ShowLineLoadOverlaysOnCanvas, Is.False);
        Assert.That(host.GeometryDefinitionVM.ShowPointLoadOverlaysOnCanvas, Is.False);
        host.Workspace.Loads.ShowLineLoads = true;
        host.Workspace.Loads.ShowPointLoads = true;
        Assert.That(host.GeometryDefinitionVM.ShowLineLoadOverlaysOnCanvas, Is.True);
        Assert.That(host.GeometryDefinitionVM.ShowPointLoadOverlaysOnCanvas, Is.True);

        host.Workspace.Boundary.MarkersVisible = true;
        Assert.That(host.Workspace.Boundary.MarkersVisible, Is.True);
        Assert.That(host.BoundaryConditionsVM.MarkersVisible, Is.True);
        host.Workspace.Boundary.MarkersVisible = false;
        Assert.That(host.Workspace.Boundary.MarkersVisible, Is.False);
        Assert.That(host.BoundaryConditionsVM.MarkersVisible, Is.False);
    }

    [Test]
    public void LoadsRibbon_ThenMesh_OpensMeshChrome()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("LOADS");
        host.Workspace.SelectRibbonTabCommand.Execute("MESH");

        Assert.That(host.IsMeshWorkspace, Is.True);
        Assert.That(host.IsLoadsWorkspace, Is.False);
        Assert.That(host.IsAnalysisChrome, Is.False);
        Assert.That(host.SelectedTabIndex, Is.EqualTo(3));
        Assert.That(host.ShowWorkspaceRightColumn, Is.False);
        Assert.That(host.WorkspaceMeshTableRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));
    }

    [Test]
    public void MeshRibbon_IgnoresSpuriousJumpToMaterialsTab()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MESH");

        Assert.That(host.IsMeshWorkspace, Is.True);
        Assert.That(host.SelectedTabIndex, Is.EqualTo(3));

        host.SelectedTabIndex = 2;

        Assert.That(host.SelectedTabIndex, Is.EqualTo(3),
            "Nested ComboBox/DataGrid selection must not switch MESH to Materials.");
        Assert.That(host.IsMeshWorkspace, Is.True);
        Assert.That(host.IsMaterialsWorkspace, Is.False);
        Assert.That(host.Workspace.RibbonTab, Is.EqualTo("MESH"));
        Assert.That(MainWindowViewModel.RibbonAllowsMaterialsTab("MESH"), Is.False);
    }

    [Test]
    public void MeshRibbon_SelectMaterialsRibbon_StillSwitches()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MESH");
        host.Workspace.SelectRibbonTabCommand.Execute("MATERIALS");

        Assert.That(host.IsMaterialsWorkspace, Is.True);
        Assert.That(host.SelectedTabIndex, Is.EqualTo(2));
        Assert.That(host.IsMeshWorkspace, Is.False);
    }

    [Test]
    public void MeshWorkspace_Generate_WithoutPriorDiscretize_CreatesElementsAndShowsThem()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MESH");
        var geometry = host.GeometryDefinitionVM;
        var mesh = host.MeshVM;
        var workspace = host.Workspace.Mesh;

        workspace.SelectUnstructuredCommand.Execute(null);
        geometry.SetOutlineAxis(new[] { (0.0, 0.0), (20.0, 0.0), (20.0, 10.0), (0.0, 10.0) });
        mesh.GlobalMeshSize = "5";
        mesh.NearBoundaryMeshSize = "5";
        mesh.Rs2dElementType = "3-node triangle";
        mesh.Rs2dMeshType = "Uniform";
        mesh.ShowMeshElements = false;
        mesh.Rs2dDiscretized = false;

        Assert.That(workspace.IsUnstructuredGenerator, Is.True);
        Assert.That(mesh.Rs2dDiscretized, Is.False);
        Assert.That(mesh.MeshInformationRows, Is.Empty);

        workspace.GenerateCommand.Execute(null);

        Assert.That(mesh.MeshInformationRows.Count, Is.GreaterThan(0),
            mesh.SurfaceMeshStatus ?? "Generate produced no elements.");
        Assert.That(mesh.ShowMeshElements, Is.True);
        Assert.That(workspace.HasMesh, Is.True);
        Assert.That(workspace.ElementCount, Is.GreaterThan(0));
        Assert.That(workspace.NodeCount, Is.GreaterThan(0));
    }

    [Test]
    public void MeshWorkspace_Generate_AfterDiscretize_DefaultStructured_CreatesElements()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MESH");
        var geometry = host.GeometryDefinitionVM;
        var mesh = host.MeshVM;
        var workspace = host.Workspace.Mesh;

        Assert.That(workspace.IsStructuredGenerator, Is.True,
            "MESH defaults to Structured; that path must generate after Discretize.");

        geometry.SetOutlineAxis(new[] { (0.0, 0.0), (20.0, 0.0), (20.0, 10.0), (0.0, 10.0) });
        mesh.GlobalMeshSize = "5";
        mesh.NearBoundaryMeshSize = "5";
        mesh.ShowMeshElements = false;

        Assert.That(geometry.GeometrySurfaces, Is.Empty,
            "Typical outline has vertices/lines but no locked surface.");

        workspace.DiscretizeActiveCommand.Execute(null);
        Assert.That(mesh.Rs2dDiscretized, Is.True, mesh.SurfaceMeshStatus ?? "Discretize failed.");
        Assert.That(mesh.DiscretizationNodes.Count, Is.GreaterThan(0));
        Assert.That(mesh.MeshInformationRows, Is.Empty);
        Assert.That(workspace.HasMesh, Is.False);

        workspace.GenerateCommand.Execute(null);

        Assert.That(mesh.MeshInformationRows.Count, Is.GreaterThan(0),
            mesh.SurfaceMeshStatus ?? "Generate produced no elements after discretize.");
        Assert.That(mesh.ShowMeshElements, Is.True);
        Assert.That(workspace.HasMesh, Is.True);
        Assert.That(workspace.IsTableExpanded, Is.True);
        Assert.That(workspace.ElementCount, Is.GreaterThan(0));
        Assert.That(workspace.NodeCount, Is.GreaterThan(0));
    }

    [Test]
    public void MeshRibbon_IsNotLoadsWorkspace()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MESH");

        Assert.That(host.IsMeshWorkspace, Is.True);
        Assert.That(host.IsLoadsWorkspace, Is.False);
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.EqualTo(host.Workspace.Mesh.IsControlsExpanded));

        host.Workspace.SelectRibbonTabCommand.Execute("LOADS");
        Assert.That(host.IsMeshWorkspace, Is.False);
        Assert.That(host.IsLoadsWorkspace, Is.True);
    }

    [Test]
    public void LoadsWorkspace_SurfaceLoad_ExposesScreenshotPropertyFields()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("LOADS");
        var workspace = host.Workspace.Loads;

        workspace.Loads.AddLoadOfType(LoadType.Triangular);
        var load = workspace.SelectedLoad;
        Assert.That(load, Is.Not.Null);
        Assert.That(load!.DisplayType, Is.EqualTo("Surface Load"));
        Assert.That(load.IdCode, Does.StartWith("SL-"));
        Assert.That(load.LoadGroup, Is.EqualTo("LG-01 (Permanent)"));
        Assert.That(load.CoordinateSystem, Is.EqualTo("Global"));
        Assert.That(load.DisplayDirection, Is.EqualTo("Vertical"));
        Assert.That(load.DisplayDistribution, Is.EqualTo("Uniform"));
        Assert.That(load.Magnitude, Is.EqualTo(20).Within(1e-9));
        Assert.That(load.MagnitudeUnit, Is.EqualTo("kN/m²"));
        Assert.That(load.LoadProjection, Is.EqualTo("Normal to Surface"));
        Assert.That(load.Status, Is.EqualTo("Active"));
        Assert.That(load.Notes, Does.Contain("surcharge"));

        load.IsStageLoad = true;
        load.ApplyFromStage = 1;
        load.ApplyToStageOrAll = 0;
        load.Status = "Inactive";
        load.Notes = "Surface surcharge load due to equipment and construction activities.";

        Assert.That(load.IsEnabled, Is.False);
        Assert.That(load.ApplyToStage, Is.Null);
        Assert.That(workspace.StageToChoices.Any(c => c.Name == "All Subsequent"), Is.True);
        Assert.That(workspace.InspectorId, Is.EqualTo(load.IdCode));
        Assert.That(workspace.InspectorGroup, Is.EqualTo("LG-01 (Permanent)"));
        Assert.That(workspace.InspectorType, Is.EqualTo("Surface Load"));
    }

    [Test]
    public void LoadsWorkspace_AddPointLoad_DoesNotRecurseOrChangeTypeOnNullCombo()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("LOADS");
        var workspace = host.Workspace.Loads;
        workspace.Loads.Loads.Clear();

        workspace.AddNodalLoadCommand.Execute(null);
        var load = workspace.SelectedLoad;
        Assert.That(load, Is.Not.Null);
        Assert.That(load!.Type, Is.EqualTo(LoadType.Point));
        Assert.That(load.DisplayType, Is.EqualTo("Point Load"));

        load.DisplayType = null;
        load.DisplayDirection = null;
        load.DisplayDistribution = null;
        load.Status = null;
        load.ApplyFromStage = 1;
        load.ApplyFromStage = 1;
        load.ApplyToStageOrAll = 0;
        Assert.That(load.Type, Is.EqualTo(LoadType.Point));
        Assert.That(load.DisplayType, Is.EqualTo("Point Load"));
        Assert.That(workspace.InspectorResultantVertical, Is.Not.Null);
        Assert.That(workspace.InspectorPreviewAngle, Is.GreaterThanOrEqualTo(-360));

        load.DisplayType = "Surface Load";
        Assert.That(load.Type, Is.EqualTo(LoadType.Triangular));
        load.DisplayType = "Point Load";
        Assert.That(load.Type, Is.EqualTo(LoadType.Point));
        Assert.That(workspace.HasSelectedLoad, Is.True);
    }

    [Test]
    public void LoadsRibbon_OpensProperties_WithFirstLoadSelected()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.LoadConditionsVM.AddLoadOfType(LoadType.Point);
        host.LoadConditionsVM.SelectedLoad = null;
        Assert.That(host.Workspace.Loads.HasSelectedLoad, Is.False);

        host.Workspace.SelectRibbonTabCommand.Execute("LOADS");

        Assert.That(host.IsLoadsWorkspace, Is.True);
        Assert.That(host.Workspace.Loads.HasSelectedLoad, Is.True);
        Assert.That(host.Workspace.Loads.IsLoadSection, Is.True);
        Assert.That(host.Workspace.Loads.SelectedLoad!.Type, Is.EqualTo(LoadType.Point));

        host.LoadConditionsVM.AddLoadOfType(LoadType.Line);
        var nodal = host.Workspace.Loads.Roots[0].Children.First(c => c.Title == "Nodal Loads");
        host.Workspace.Loads.SelectedNode = nodal;
        Assert.That(host.Workspace.Loads.SelectedLoad!.Type, Is.EqualTo(LoadType.Point));
    }

    [Test]
    public void BoundaryRibbon_OpensBoundaryWorkspaceAfterMesh()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("MESH");
        host.Workspace.SelectRibbonTabCommand.Execute("BOUNDARY");

        Assert.That(host.Workspace.IsBoundaryRibbon, Is.True);
        Assert.That(host.IsBoundaryWorkspace, Is.True);
        Assert.That(host.IsMeshWorkspace, Is.False);
        Assert.That(host.IsLoadsWorkspace, Is.False);
        Assert.That(host.IsAnalysisChrome, Is.False);
        Assert.That(host.IsFileOrAnalysisChrome, Is.False);
        Assert.That(host.SelectedTabIndex, Is.EqualTo(5));
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.True);
        Assert.That(host.ShowWorkspaceRightColumn, Is.False);
        Assert.That(host.WorkspaceRightColumnWidth.Value, Is.EqualTo(0));
        Assert.That(host.ShowWorkspaceCanvasZoomBar, Is.True);
        Assert.That(host.WorkspaceBoundaryTableRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));
        Assert.That(host.WorkspaceBottomChromeRowHeight.Value,
            Is.EqualTo(MainWindowViewModel.WorkspaceExpandedTableHeight));
        Assert.That(host.Workspace.Boundary.Roots, Has.Count.EqualTo(2));
        Assert.That(host.Workspace.Boundary.Roots[0].Title,
            Is.EqualTo(BoundaryWorkspaceViewModel.OuterBoundariesFolderTitle));
        Assert.That(host.Workspace.Boundary.Roots[1].Title, Is.EqualTo("Boundary Conditions"));
        Assert.That(host.Workspace.Boundary.Roots[1].Children.Select(c => c.Title),
            Is.EqualTo(new[] { "Structural", "Seepage" }));
    }

    [Test]
    public void BoundaryExplorer_AddingFixedAndRollerX_UpdatesExplorerAndSummary()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("BOUNDARY");
        var workspace = host.Workspace.Boundary;
        host.BoundaryConditionsVM.BoundaryConditions.Clear();

        host.BoundaryConditionsVM.BoundaryConditions.Add(new BoundaryConditionRow
        {
            NodeId = 1,
            BoundaryName = "L1",
            BCType = "Fixed",
            AnalysisCategory = BoundaryConditionAnalysisCategories.Structural,
            DegreeOfFreedom = 0,
            Value = "0"
        });
        host.BoundaryConditionsVM.BoundaryConditions.Add(new BoundaryConditionRow
        {
            NodeId = 2,
            BoundaryName = "L2",
            BCType = "Roller X",
            AnalysisCategory = BoundaryConditionAnalysisCategories.Structural,
            DegreeOfFreedom = 0,
            Value = "0"
        });

        Assert.That(workspace.TotalBcCount, Is.EqualTo(2));
        Assert.That(workspace.StructuralCount, Is.EqualTo(2));
        Assert.That(workspace.SeepageCount, Is.EqualTo(0));

        var bcRoot = workspace.Roots.Single(r => r.Title == "Boundary Conditions");
        var structural = bcRoot.Children.First(c => c.Title == "Structural");
        Assert.That(structural.Count, Is.EqualTo(2));
        Assert.That(structural.Children.Select(c => c.Title), Is.EqualTo(new[] { "Fixed", "Roller X" }));
        Assert.That(structural.Children.First(c => c.Title == "Fixed").Children[0].Title, Is.EqualTo("N1 · L1"));
        Assert.That(structural.Children.First(c => c.Title == "Roller X").Children[0].Payload,
            Is.TypeOf<BoundaryConditionRow>());

        Assert.That(workspace.LegendItems.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(workspace.LegendItems.Select(i => i.Label), Does.Contain("Fixed").Or.Contain("UX+UY"));
        Assert.That(workspace.LegendItems.Sum(i => i.Count), Is.EqualTo(2));
    }

    [Test]
    public void BoundaryRibbon_IgnoresSpuriousJumpToMaterialsTab()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("BOUNDARY");

        Assert.That(host.IsBoundaryWorkspace, Is.True);
        Assert.That(host.SelectedTabIndex, Is.EqualTo(5));

        host.SelectedTabIndex = 2;

        Assert.That(host.SelectedTabIndex, Is.EqualTo(5),
            "Nested ComboBox/DataGrid selection must not switch BOUNDARY to Materials.");
        Assert.That(host.IsBoundaryWorkspace, Is.True);
        Assert.That(host.IsMaterialsWorkspace, Is.False);
        Assert.That(host.Workspace.RibbonTab, Is.EqualTo("BOUNDARY"));
        Assert.That(MainWindowViewModel.RibbonAllowsMaterialsTab("BOUNDARY"), Is.False);
    }

    [Test]
    public void BoundaryRibbon_SelectMaterialsRibbon_StillSwitches()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("BOUNDARY");
        host.Workspace.SelectRibbonTabCommand.Execute("MATERIALS");

        Assert.That(host.IsMaterialsWorkspace, Is.True);
        Assert.That(host.SelectedTabIndex, Is.EqualTo(2));
        Assert.That(host.IsBoundaryWorkspace, Is.False);
        Assert.That(host.Workspace.RibbonTab, Is.EqualTo("MATERIALS"));
    }

    [Test]
    public void BoundaryRibbon_DisplacementAndRoller_SetExistingTypes()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("BOUNDARY");
        var workspace = host.Workspace.Boundary;

        workspace.StartDisplacementCommand.Execute(null);
        Assert.That(host.BoundaryConditionsVM.SelectedBCTypeForBoundary, Is.EqualTo("Prescribed Displacement"));
        Assert.That(workspace.IsPropertiesExpanded, Is.True);

        workspace.StartRollerCommand.Execute(null);
        Assert.That(host.BoundaryConditionsVM.SelectedBCTypeForBoundary, Is.EqualTo("Roller X"));

        workspace.StartFixedCommand.Execute(null);
        Assert.That(host.BoundaryConditionsVM.SelectedBCTypeForBoundary, Is.EqualTo("Fixed"));

        workspace.StartSeepageFaceCommand.Execute(null);
        Assert.That(host.BoundaryConditionsVM.SelectedBCTypeForBoundary, Is.EqualTo("Zero Pressure (P=0)"));
    }

    [Test]
    public void BoundaryProperties_TypeList_IncludesExistingTypesWhenCategoriesOff()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("BOUNDARY");

        Assert.That(host.BoundaryConditionsVM.HasAvailableBcTypes, Is.True);
        Assert.That(host.BoundaryConditionsVM.AvailableBCTypes, Does.Contain("Fixed"));
        Assert.That(host.BoundaryConditionsVM.AvailableBCTypes, Does.Contain("Roller X"));
        Assert.That(host.BoundaryConditionsVM.AvailableBCTypes, Does.Contain("Prescribed Displacement"));
        Assert.That(host.BoundaryConditionsVM.AvailableBCTypes, Does.Contain("Pore Pressure"));
        Assert.That(host.BoundaryConditionsVM.AvailableBCTypes, Does.Contain("Zero Pressure (P=0)"));
        Assert.That(host.Workspace.Boundary.SelectedType, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void BoundaryProperties_SelectingRollerX_FillsRestraintFlagsAndHidesDof()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("BOUNDARY");
        var workspace = host.Workspace.Boundary;

        workspace.SelectedType = "Roller X";

        Assert.That(workspace.UxValue, Is.EqualTo("1"));
        Assert.That(workspace.UyValue, Is.EqualTo("0"));
        Assert.That(workspace.SelectedDofDisplay, Is.EqualTo("0 (X)"));
        Assert.That(host.BoundaryConditionsVM.BcValueForBoundary, Is.EqualTo("0"));
        Assert.That(workspace.ShowRestraintFields, Is.True);
        Assert.That(workspace.ShowDofField, Is.False);
        Assert.That(workspace.ShowValueField, Is.True);
    }

    [Test]
    public void BoundaryProperties_SelectingFixed_FillsBothRestraints()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("BOUNDARY");
        var workspace = host.Workspace.Boundary;

        workspace.SelectedType = "Fixed";

        Assert.That(workspace.UxValue, Is.EqualTo("1"));
        Assert.That(workspace.UyValue, Is.EqualTo("1"));
        Assert.That(workspace.ShowRestraintFields, Is.True);
        Assert.That(workspace.ShowDofField, Is.False);
        Assert.That(host.BoundaryConditionsVM.BcValueForBoundary, Is.EqualTo("0"));
    }

    [Test]
    public void BoundaryProperties_SelectingTotalHead_ShowsHeadValueOnly()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("BOUNDARY");
        var workspace = host.Workspace.Boundary;

        workspace.SelectedType = "Total Head (H)";

        Assert.That(workspace.ShowRestraintFields, Is.False);
        Assert.That(workspace.ShowDofField, Is.False);
        Assert.That(workspace.ShowValueField, Is.True);
        Assert.That(workspace.ValueFieldLabel, Is.EqualTo("Head"));
        Assert.That(workspace.ValueIsReadOnly, Is.False);
    }

    [Test]
    public void BoundaryProperties_ApplyRollerY_WritesImpliedDof()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("BOUNDARY");
        var workspace = host.Workspace.Boundary;
        host.GeometryDefinitionVM.GeometryLineSegments.Clear();
        host.GeometryDefinitionVM.GeometryLineSegments.Add(new GeometryLineSegment(1, "P1", "P2"));
        host.BoundaryConditionsVM.SelectedImportMode = BoundaryConditionsViewModel.ImportModeBoundary;
        host.BoundaryConditionsVM.BoundaryConditions.Clear();
        host.BoundaryConditionsVM.NodesText = "4";
        workspace.SelectedType = "Roller Y";

        workspace.ApplyCommand.Execute(null);

        Assert.That(host.BoundaryConditionsVM.BoundaryConditions, Has.Count.EqualTo(1));
        Assert.That(host.BoundaryConditionsVM.BoundaryConditions[0].BCType, Is.EqualTo("Roller Y"));
        Assert.That(host.BoundaryConditionsVM.BoundaryConditions[0].DegreeOfFreedom, Is.EqualTo(1));
    }

    [Test]
    public void BoundaryExplorer_ImportModeBoundary_PopulatesOuterBoundariesFolder()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("BOUNDARY");
        var workspace = host.Workspace.Boundary;
        host.GeometryDefinitionVM.GeometryLineSegments.Clear();
        host.GeometryDefinitionVM.GeometryLineSegments.Add(new GeometryLineSegment(1, "P1", "P2"));
        host.GeometryDefinitionVM.GeometryLineSegments.Add(new GeometryLineSegment(2, "P2", "P3"));

        host.BoundaryConditionsVM.SelectedImportMode = BoundaryConditionsViewModel.ImportModeBoundary;

        Assert.That(host.BoundaryConditionsVM.AvailableOuterBoundaries, Has.Count.EqualTo(2));
        var outer = workspace.Roots.Single(r => r.Title == BoundaryWorkspaceViewModel.OuterBoundariesFolderTitle);
        Assert.That(outer.Count, Is.EqualTo(2));
        Assert.That(outer.Children.Select(c => c.Title), Is.EqualTo(new[] { "L1", "L2" }));
        Assert.That(outer.Children[0].Payload, Is.TypeOf<OuterBoundaryInfo>());
    }

    [Test]
    public void BoundaryExplorer_SelectOuterBoundary_SetsSelectedOuterBoundaryAndOpensProperties()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("BOUNDARY");
        var workspace = host.Workspace.Boundary;
        workspace.IsPropertiesExpanded = false;
        host.GeometryDefinitionVM.GeometryLineSegments.Clear();
        host.GeometryDefinitionVM.GeometryLineSegments.Add(new GeometryLineSegment(1, "P1", "P2"));
        host.GeometryDefinitionVM.GeometryLineSegments.Add(new GeometryLineSegment(2, "P2", "P3"));
        host.BoundaryConditionsVM.SelectedImportMode = BoundaryConditionsViewModel.ImportModeBoundary;

        var outer = workspace.Roots.Single(r => r.Title == BoundaryWorkspaceViewModel.OuterBoundariesFolderTitle);
        workspace.SelectedNode = outer.Children.Single(c => c.Title == "L2");

        Assert.That(host.BoundaryConditionsVM.SelectedOuterBoundary, Is.Not.Null);
        Assert.That(host.BoundaryConditionsVM.SelectedOuterBoundary!.BoundaryTag, Is.EqualTo("L2"));
        Assert.That(workspace.IsPropertiesExpanded, Is.True);
    }

    [Test]
    public void BoundaryRibbon_ImportBoundariesCommand_UsesBoundaryImportMode()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("BOUNDARY");
        var workspace = host.Workspace.Boundary;
        host.GeometryDefinitionVM.GeometryLineSegments.Clear();
        host.GeometryDefinitionVM.GeometryLineSegments.Add(new GeometryLineSegment(3, "A", "B"));

        workspace.ImportBoundariesCommand.Execute(null);

        Assert.That(host.BoundaryConditionsVM.SelectedImportMode,
            Is.EqualTo(BoundaryConditionsViewModel.ImportModeBoundary));
        Assert.That(host.BoundaryConditionsVM.AvailableOuterBoundaries, Has.Count.EqualTo(1));
        Assert.That(workspace.IsPropertiesExpanded, Is.True);
    }

    [Test]
    public void BoundaryProperties_ApplyCommand_AddsRowForSelectedOuterBoundaryNodes()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("BOUNDARY");
        var workspace = host.Workspace.Boundary;
        host.GeometryDefinitionVM.GeometryLineSegments.Clear();
        host.GeometryDefinitionVM.GeometryLineSegments.Add(new GeometryLineSegment(1, "P1", "P2"));
        host.BoundaryConditionsVM.SelectedImportMode = BoundaryConditionsViewModel.ImportModeBoundary;
        host.ProjectSettingsVM.SetCategoryOn(MaterialPropertiesViewModel.SrfAnalysisCategory, true);
        host.BoundaryConditionsVM.RefreshAvailableBcTypes();
        host.BoundaryConditionsVM.SelectedBCTypeForBoundary = "Fixed";
        host.BoundaryConditionsVM.BcValueForBoundary = "0";
        host.BoundaryConditionsVM.NodesText = "7";
        host.BoundaryConditionsVM.BoundaryConditions.Clear();

        Assert.That(host.BoundaryConditionsVM.HasAvailableBcTypes, Is.True);
        workspace.ApplyCommand.Execute(null);

        Assert.That(host.BoundaryConditionsVM.BoundaryConditions, Has.Count.EqualTo(1));
        Assert.That(host.BoundaryConditionsVM.BoundaryConditions[0].NodeId, Is.EqualTo(7));
        Assert.That(host.BoundaryConditionsVM.BoundaryConditions[0].BCType, Is.EqualTo("Fixed"));
        Assert.That(host.BoundaryConditionsVM.BoundaryConditions[0].BoundaryName, Is.EqualTo("L1"));
    }

    [Test]
    public void ContourHelper_ManualRange_WinsOverAuto()
    {
        var range = FEContourScaleHelper.ResolveManualOrAutoColorRange(
            new ResultsViewModel(),
            ResultsViewModel.OutputDisplacementMagnitude,
            -2,
            5);
        Assert.That(range.Min, Is.EqualTo(-2));
        Assert.That(range.Max, Is.EqualTo(5));
    }

    private static void AssertViewOptionsToggleLeavesChrome(MainWindowViewModel host, ICommand toggle)
    {
        var left = host.WorkspaceLeftColumnWidth.Value;
        var right = host.WorkspaceRightColumnWidth.Value;
        var center = host.ShowWorkspaceCenterToolsColumn;
        var bottom = host.ShowWorkspaceBottomSplitter;

        toggle.Execute(null);

        Assert.That(host.WorkspaceLeftColumnWidth.Value, Is.EqualTo(left));
        Assert.That(host.WorkspaceRightColumnWidth.Value, Is.EqualTo(right));
        Assert.That(host.ShowWorkspaceCenterToolsColumn, Is.EqualTo(center));
        Assert.That(host.ShowWorkspaceBottomSplitter, Is.EqualTo(bottom));
    }

    [Test]
    public void AnalysisTypeWizard_ListsEightCards_DefaultPendingStatic()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var wizard = new AnalysisTypeWizardViewModel(host);

        Assert.That(wizard.Cards, Has.Count.EqualTo(8));
        Assert.That(wizard.Cards.Select(c => c.Title), Is.EqualTo(new[]
        {
            "Static", "Seepage", "Consolidation", "Dynamic",
            "Thermal", "Coupled THM", "Stability (SSR)", "Stage Construction"
        }));
        Assert.That(wizard.PendingIntent, Is.EqualTo(AnalysisIntent.Static));
        Assert.That(wizard.SelectedCardKey, Is.EqualTo("Static"));
        Assert.That(wizard.CurrentStep, Is.EqualTo(AnalysisTypeWizardViewModel.StepSelectType));
        Assert.That(wizard.CanGoBack, Is.False);
    }

    [Test]
    public void AnalysisTypeWizard_FinishSeepage_AppliesIntent()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.SelectCardCommand.Execute(wizard.Cards.Single(c => c.Key == "Seepage"));

        wizard.Finish();

        Assert.That(host.Workspace.Settings.Intent, Is.EqualTo(AnalysisIntent.Seepage));
        Assert.That(AnalysisCapabilityCatalog.Get(host.Workspace.Settings.Intent).Status,
            Is.EqualTo(AnalysisCapabilityStatus.Available));
    }

    [Test]
    public void AnalysisTypeWizard_FinishSrf_AppliesIntent()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.SelectCardCommand.Execute(wizard.Cards.Single(c => c.Key == "Srf"));

        wizard.Finish();

        Assert.That(host.Workspace.Settings.Intent, Is.EqualTo(AnalysisIntent.Srf));
        Assert.That(AnalysisCapabilityCatalog.CanRunNativeSolver(AnalysisIntent.Srf), Is.True);
    }

    [Test]
    public void AnalysisTypeWizard_FinishThermal_DoesNotInventSolver()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.SelectCardCommand.Execute(wizard.Cards.Single(c => c.Key == "Thermal"));

        wizard.Finish();

        Assert.That(host.Workspace.Settings.Intent, Is.EqualTo(AnalysisIntent.Thermal));
        Assert.That(AnalysisCapabilityCatalog.Get(AnalysisIntent.Thermal).Status,
            Is.EqualTo(AnalysisCapabilityStatus.NotImplemented));
        Assert.That(AnalysisCapabilityCatalog.CanRunNativeSolver(AnalysisIntent.Thermal), Is.False);
    }

    [Test]
    public void AnalysisTypeWizard_GatedDefineOptions_AcceptAndReadBack()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var wizard = new AnalysisTypeWizardViewModel(host);

        wizard.GatedSeepageFlowType = AnalysisGatedOptionCatalog.SeepageFlowTypes[2];
        wizard.GatedViscosity = "2.00E-03";
        wizard.GatedThermalProblemType = AnalysisGatedOptionCatalog.ThermalProblemTypes[1];
        wizard.IncludeLargeDeformation = true;
        wizard.SpatiallyVaryingPermeability = true;
        wizard.IncludeTemperatureEffect = true;
        wizard.AutomaticTimeStepping = true;

        Assert.That(wizard.GatedSeepageFlowType, Is.EqualTo("Transient"));
        Assert.That(wizard.GatedViscosity, Is.EqualTo("2.00E-03"));
        Assert.That(wizard.GatedThermalProblemType, Is.EqualTo("Transient"));
        Assert.That(wizard.IncludeLargeDeformation, Is.True);
        Assert.That(wizard.SpatiallyVaryingPermeability, Is.True);
        Assert.That(wizard.IncludeTemperatureEffect, Is.True);
        Assert.That(wizard.AutomaticTimeStepping, Is.True);
    }

    [Test]
    public void AnalysisTypeWizard_DefineOptions_ShowFlags_MatchPendingPhysics()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var wizard = new AnalysisTypeWizardViewModel(host);

        AdvanceWizardToDefineOptions(wizard, "Seepage");
        AssertDefineOptionsPanel(wizard, seepage: true);

        AdvanceWizardToDefineOptions(wizard, "Consolidation");
        AssertDefineOptionsPanel(wizard, consolidation: true);

        AdvanceWizardToDefineOptions(wizard, "Dynamic");
        AssertDefineOptionsPanel(wizard, showDynamic: true);

        AdvanceWizardToDefineOptions(wizard, "Thermal");
        AssertDefineOptionsPanel(wizard, thermal: true);

        AdvanceWizardToDefineOptions(wizard, "Coupled");
        AssertDefineOptionsPanel(wizard, coupled: true);

        AdvanceWizardToDefineOptions(wizard, "Srf");
        AssertDefineOptionsPanel(wizard, srf: true);

        AdvanceWizardToDefineOptions(wizard, "Static");
        AssertDefineOptionsPanel(wizard, generic: true);
    }

    private static void AdvanceWizardToDefineOptions(AnalysisTypeWizardViewModel wizard, string cardKey)
    {
        wizard.CurrentStep = AnalysisTypeWizardViewModel.StepSelectType;
        wizard.SelectCardCommand.Execute(wizard.Cards.Single(c => c.Key == cardKey));
        wizard.NextCommand.Execute(null);
        Assert.That(wizard.CurrentStep, Is.EqualTo(AnalysisTypeWizardViewModel.StepDefineOptions));
    }

    private static void AssertDefineOptionsPanel(
        AnalysisTypeWizardViewModel wizard,
        bool seepage = false,
        bool consolidation = false,
        bool showDynamic = false,
        bool thermal = false,
        bool coupled = false,
        bool srf = false,
        bool generic = false)
    {
        Assert.That(wizard.ShowSeepageDefineOptions, Is.EqualTo(seepage));
        Assert.That(wizard.ShowConsolidationDefineOptions, Is.EqualTo(consolidation));
        Assert.That(wizard.ShowDynamicDefineOptions, Is.EqualTo(showDynamic));
        Assert.That(wizard.ShowThermalDefineOptions, Is.EqualTo(thermal));
        Assert.That(wizard.ShowCoupledDefineOptions, Is.EqualTo(coupled));
        Assert.That(wizard.ShowSrfDefineOptions, Is.EqualTo(srf));
        Assert.That(wizard.ShowGenericDefineOptions, Is.EqualTo(generic));
        Assert.That(wizard.ShowTypedDefineOptions, Is.EqualTo(!generic));
    }

    [Test]
    public void AnalysisTypeWizard_StageConstructionCard_JumpsToStagePage()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.FEAnalysisVM.StagedExcavationEnabled = false;
        var wizard = new AnalysisTypeWizardViewModel(host);

        wizard.SelectCardCommand.Execute(wizard.Cards.Single(c => c.IsStageConstruction));

        Assert.That(wizard.PendingStaged, Is.True);
        Assert.That(wizard.CurrentStep, Is.EqualTo(AnalysisTypeWizardViewModel.StepStageSettings));
        Assert.That(wizard.PendingIntent, Is.EqualTo(AnalysisIntent.Static));
        Assert.That(host.FEAnalysisVM.StagedExcavationEnabled, Is.False);

        wizard.Finish();

        Assert.That(host.FEAnalysisVM.StagedExcavationEnabled, Is.True);
        Assert.That(host.Workspace.Settings.Intent, Is.EqualTo(AnalysisIntent.Static));
    }

    [Test]
    public void AnalysisTypeWizard_DuplicateStage_ClonesExistingFields()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var source = new StageDefinition
        {
            StageNumber = 1,
            StageName = "Excavation Stage 1",
            StageType = "Excavation",
            Description = "Excavate to -5.0 m",
            Duration = 2.5,
            Units = "day",
            LoadFactor = 0.75,
            IsActive = true,
            Notes = "keep walls"
        };
        source.ActivateSurfaceNumbers.Add(3);
        host.FEAnalysisVM.StageDefinitions.Add(source);
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.SelectedStage = source;

        var copy = wizard.DuplicateSelectedStage();

        Assert.That(host.FEAnalysisVM.StageDefinitions, Has.Count.EqualTo(2));
        Assert.That(copy.StageName, Is.EqualTo("Excavation Stage 1 copy"));
        Assert.That(copy.StageType, Is.EqualTo("Excavation"));
        Assert.That(copy.Description, Is.EqualTo("Excavate to -5.0 m"));
        Assert.That(copy.Duration, Is.EqualTo(2.5));
        Assert.That(copy.Units, Is.EqualTo("day"));
        Assert.That(copy.LoadFactor, Is.EqualTo(0.75));
        Assert.That(copy.Notes, Is.EqualTo("keep walls"));
        Assert.That(copy.ActivateSurfaceNumbers, Is.EquivalentTo(new[] { 3 }));
        Assert.That(copy.StageNumber, Is.EqualTo(2));
    }

    [Test]
    public void AnalysisTypeWizard_FormulationEffectiveStress_SetsExistingRatio()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.LoadConditionsVM.FieldStress.Global.UseEffectiveStressRatio = false;
        var wizard = new AnalysisTypeWizardViewModel(host);

        wizard.SelectedFormulation = "Effective Stress";

        Assert.That(host.LoadConditionsVM.FieldStress.Global.UseEffectiveStressRatio, Is.True);
        Assert.That(wizard.SelectedFormulation, Is.EqualTo("Effective Stress"));
        Assert.That(wizard.GatedDampingTypes, Does.Contain("None"));
        Assert.That(wizard.GatedK0Methods, Does.Contain("Jaky"));
        Assert.That(wizard.GatedStressRecovery, Does.Contain("At Gauss Points"));
        Assert.That(wizard.PhysicsTypeChoices, Does.Not.Contain("Stage Construction"));
    }

    [Test]
    public void AnalysisWorkspace_GatedLoadingType_IgnoresTwoWayWrites()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };

        host.Workspace.GatedLoadingType = "Dynamic";

        Assert.That(host.Workspace.GatedLoadingType, Is.EqualTo("Static (Constant)"));
        Assert.That(host.Workspace.GatedDampingRatio, Is.EqualTo("0.00"));
    }

    [Test]
    public void AnalysisTypeWizard_SeepageOptions_HydraulicHeadSetsWaterHeadFlags()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.ProjectSettingsVM.SeepageSettings.SetAllIncludeWaterHead(false);
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.PendingIntent = AnalysisIntent.Seepage;

        Assert.That(wizard.IsSeepagePending, Is.True);
        Assert.That(wizard.IsGenericDefineOptions, Is.False);
        wizard.IsSeepageHydraulicHead = true;

        Assert.That(host.ProjectSettingsVM.SeepageSettings.IncludeWaterHeadFreeSurface, Is.True);
        Assert.That(wizard.SeepageFlowTypes, Does.Contain("Steady-State"));
        Assert.That(wizard.InitialConditionTypes, Does.Contain("Hydrostatic"));
    }

    [Test]
    public void AnalysisTypeWizard_FinishSeepage_SteadyHydraulicHead_EnablesGroundwater()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.GroundwaterVM.IsEnabled = false;
        host.ProjectSettingsVM.SeepageSettings.SetAllIncludeWaterHead(false);
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.SelectCardCommand.Execute(wizard.Cards.Single(c => c.Key == "Seepage"));
        wizard.GatedSeepageFlowType = AnalysisGatedOptionCatalog.SeepageFlowTypes[1];
        wizard.IsSeepageHydraulicHead = true;

        wizard.Finish();

        Assert.That(host.GroundwaterVM.IsEnabled, Is.True);
        Assert.That(host.ProjectSettingsVM.IsCategoryOn(MaterialPropertiesViewModel.GroundwaterAnalysisCategory), Is.True);
        Assert.That(host.ProjectSettingsVM.ResolvedAnalysisKind, Is.EqualTo(AnalysisKind.GroundwaterSteady));
        Assert.That(host.ProjectSettingsVM.SeepageSettings.IncludeWaterHeadGeneral2D, Is.True);
        Assert.That(host.Workspace.Settings.Intent, Is.EqualTo(AnalysisIntent.Seepage));
    }

    [Test]
    public void AnalysisTypeWizard_FinishSeepage_TransientFlow_RoutesGroundwaterTransient()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.SelectCardCommand.Execute(wizard.Cards.Single(c => c.Key == "Seepage"));
        wizard.IsSeepageHydraulicHead = true;
        wizard.GatedSeepageFlowType = AnalysisGatedOptionCatalog.SeepageFlowTypes[2];

        Assert.That(wizard.IncludeTimeDependency, Is.True);
        wizard.Finish();

        Assert.That(host.GroundwaterVM.IsEnabled, Is.True);
        Assert.That(host.ProjectSettingsVM.ResolvedAnalysisKind, Is.EqualTo(AnalysisKind.GroundwaterTransient));
        Assert.That(host.MaterialPropertiesVM.LastGroundwaterTreeMode,
            Is.EqualTo(MaterialPropertiesViewModel.GroundwaterTransientStateType));
    }

    [Test]
    public void AnalysisTypeWizard_FinishSeepage_PorePressure_RoutesGroundwaterStatic()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.SelectCardCommand.Execute(wizard.Cards.Single(c => c.Key == "Seepage"));
        wizard.GatedSeepageFlowType = AnalysisGatedOptionCatalog.SeepageFlowTypes[0];
        wizard.IsSeepagePorePressureFormulation = true;

        wizard.Finish();

        Assert.That(host.GroundwaterVM.IsEnabled, Is.True);
        Assert.That(host.ProjectSettingsVM.ResolvedAnalysisKind, Is.EqualTo(AnalysisKind.GroundwaterStatic));
        Assert.That(host.ProjectSettingsVM.SeepageSettings.IncludeWaterHeadGeneral2D, Is.False);
        Assert.That(host.ProjectSettingsVM.SeepageSettings.IncludeWaterHeadFreeSurface, Is.False);
        Assert.That(host.ProjectSettingsVM.GroundwaterStaticSettings.IncludeWaterPressureGridPorePressure, Is.False);
    }

    [Test]
    public void AnalysisTypeWizard_SeepageDefine_WritesLivePermeabilityAndInitialCondition()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.MaterialPropertiesVM.Materials.Add(new MaterialModel { Id = 1, Name = "Soil", PermeabilityX = 1e-6 });
        host.MaterialPropertiesVM.SelectedMaterialModel = host.MaterialPropertiesVM.Materials[^1];
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.PendingIntent = AnalysisIntent.Seepage;

        wizard.SelectedPermeabilityX = 3.5e-5;
        wizard.GatedInitialConditionType = "User Defined";
        wizard.GatedReferenceLevel = "12.5";
        wizard.GatedPorePressureAtReference = "8.25";
        wizard.SeepageUpstreamHead = 22;

        Assert.That(host.MaterialPropertiesVM.SelectedMaterialModel!.PermeabilityX, Is.EqualTo(3.5e-5).Within(1e-12));
        Assert.That(host.GroundwaterVM.Method, Is.EqualTo(GroundwaterMethod.Grid));
        Assert.That(host.GroundwaterVM.WaterTableInput, Is.EqualTo("12.5"));
        Assert.That(host.ProjectSettingsVM.TransientSettings.InitialPressure, Is.EqualTo(8.25).Within(1e-9));
        Assert.That(host.ProjectSettingsVM.SeepageSettings.UpstreamHead, Is.EqualTo(22).Within(1e-9));
    }

    [Test]
    public void AnalysisTypeWizard_EditWaterHeads_OpensGroundwaterTab()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.SelectedTabIndex = 0;
        var wizard = new AnalysisTypeWizardViewModel(host);

        wizard.EditWaterHeadsCommand.Execute(null);

        Assert.That(host.SelectedTabIndex, Is.EqualTo(6));
        Assert.That(host.Workspace.RibbonTab, Is.EqualTo("GROUNDWATER"));
    }

    [Test]
    public void AnalysisTypeWizard_FinishSeepage_ShowsGroundwaterRibbon()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.SelectCardCommand.Execute(wizard.Cards.Single(c => c.Key == "Seepage"));
        wizard.Finish();

        Assert.That(host.Workspace.ShowGroundwaterRibbon, Is.True);
        Assert.That(host.Workspace.Settings.Intent, Is.EqualTo(AnalysisIntent.Seepage));
        Assert.That(wizard.SeepageFlowTypes[0], Is.EqualTo("Static"));
        Assert.That(host.ProjectSettingsVM.ResolvedAnalysisKind, Is.EqualTo(AnalysisKind.GroundwaterStatic));
    }

    [Test]
    public void AnalysisTypeWizard_FinishStatic_TurnsStructuralCategoryOn()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.ProjectSettingsVM.SetCategoryOn(FEAnalysisViewModel.StructuralStaticElasticityCategory, false);
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.SelectCardCommand.Execute(wizard.Cards.Single(c => c.Key == "Static"));

        wizard.Finish();

        Assert.That(host.Workspace.Settings.Intent, Is.EqualTo(AnalysisIntent.Static));
        Assert.That(host.ProjectSettingsVM.IsCategoryOn(FEAnalysisViewModel.StructuralStaticElasticityCategory), Is.True);
        Assert.That(host.FEAnalysisVM.IsActiveAnalysisCategoryEnabled, Is.True);
    }

    [Test]
    public void AnalysisWorkspace_ApplySettings_Static_TurnsStructuralCategoryOn()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.ProjectSettingsVM.SetCategoryOn(FEAnalysisViewModel.StructuralStaticElasticityCategory, false);
        host.Workspace.Settings.Intent = AnalysisIntent.Static;

        host.Workspace.ApplySettingsCommand.Execute(null);

        Assert.That(host.ProjectSettingsVM.IsCategoryOn(FEAnalysisViewModel.StructuralStaticElasticityCategory), Is.True);
        Assert.That(host.FEAnalysisVM.IsActiveAnalysisCategoryEnabled, Is.True);
    }

    [Test]
    public void AnalysisWorkspace_ApplySettings_Srf_TurnsSrfCategoryOn()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.ProjectSettingsVM.SetCategoryOn(MaterialPropertiesViewModel.SrfAnalysisCategory, false);
        host.Workspace.Settings.Intent = AnalysisIntent.Srf;

        host.Workspace.ApplySettingsCommand.Execute(null);

        Assert.That(host.ProjectSettingsVM.IsCategoryOn(MaterialPropertiesViewModel.SrfAnalysisCategory), Is.True);
    }

    [Test]
    public void BlankProject_SeedsStructuralCategoryOn()
    {
        var doc = XfemProjectPersistence.CreateBlankDocument();
        var structural = doc.ProjectSettings!.AnalysisCategoryOnFlags!
            .Single(f => f.Category == FEAnalysisViewModel.StructuralStaticElasticityCategory);

        Assert.That(structural.IsOn, Is.True);

        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.ProjectSettingsVM.SetCategoryOn(FEAnalysisViewModel.StructuralStaticElasticityCategory, false);
        XfemProjectPersistence.ApplyNewBlankProject(host);

        Assert.That(host.ProjectSettingsVM.IsCategoryOn(FEAnalysisViewModel.StructuralStaticElasticityCategory), Is.True);
    }

    [Test]
    public void ResultsRibbon_Static_DefaultsToTotalDisplacementContours()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.Settings.Intent = AnalysisIntent.Static;
        host.ResultsVM.VisualizationMode = ResultsViewModel.VisualizationModeVector;
        host.ResultsVM.SelectedOutputType = ResultsViewModel.OutputStressXX;

        host.Workspace.SelectRibbonTabCommand.Execute("RESULTS");

        Assert.That(host.IsResultsWorkspace, Is.True);
        Assert.That(host.Workspace.IsResultsRibbon, Is.True);
        Assert.That(host.ResultsVM.VisualizationMode, Is.EqualTo(ResultsViewModel.VisualizationModeContour2D));
        Assert.That(host.ResultsVM.SelectedOutputType, Is.EqualTo(ResultsViewModel.OutputDisplacementMagnitude));
        Assert.That(host.ResultsVM.ResultsSubTabIndex, Is.EqualTo(1));
        Assert.That(host.Workspace.Results.ResultTypeDisplay, Is.EqualTo("Total Displacement"));
        Assert.That(host.Workspace.Results.DisplayMode, Is.EqualTo(ResultsWorkspaceViewModel.DisplayFilledContours));
        Assert.That(host.Workspace.Results.IsContoursTool, Is.True);
        Assert.That(host.Workspace.Results.AnalysisTypeLabel, Is.EqualTo("Static"));
        Assert.That(host.Workspace.Results.LoadCaseLabel, Is.EqualTo("1 - Static"));
    }

    [Test]
    public void ResultsExplorer_SelectXDisplacement_UpdatesOutputType()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.SelectRibbonTabCommand.Execute("RESULTS");
        var xNode = FlattenResults(host.Workspace.Results.Roots)
            .Single(n => n.Title == "X Displacement");

        host.Workspace.Results.SelectNodeCommand.Execute(xNode);

        Assert.That(host.ResultsVM.SelectedOutputType, Is.EqualTo(ResultsViewModel.OutputDisplacementX));
        Assert.That(host.Workspace.Results.ResultTypeDisplay, Is.EqualTo("X Displacement"));
        Assert.That(xNode.IsChecked, Is.True);
    }

    [Test]
    public void ResultsWorkspace_StaticNodes_PopulateMinMax()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.ResultsVM.FENodes = new[]
        {
            new Node { Id = 1, X = 0, Y = 0, Displacements = new[] { 0.0, 0.0 } },
            new Node { Id = 2, X = 10, Y = 5, Displacements = new[] { 0.15, 0.0 } }
        };

        host.Workspace.SelectRibbonTabCommand.Execute("RESULTS");
        var total = host.Workspace.Results.MinMaxRows.Single(r => r.Quantity == "Total Displacement");

        Assert.That(total.MaxText, Is.EqualTo("0.1500"));
        Assert.That(total.MaxLocation, Does.Contain("10.00"));
        Assert.That(host.Workspace.Results.RangeMaxText, Is.EqualTo("0.1500"));
        Assert.That(host.Workspace.Results.HasCompletedStaticResults, Is.True);
        Assert.That(host.Workspace.Results.RootTitle, Does.Contain("Completed"));
    }

    private static IEnumerable<ResultsManagerNode> FlattenResults(IEnumerable<ResultsManagerNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in FlattenResults(node.Children))
                yield return child;
        }
    }

    [Test]
    public void AnalysisWorkspace_GroundwaterRadio_HiddenWhenIntentIsStatic()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.Settings.Intent = AnalysisIntent.Static;

        Assert.That(host.Workspace.ShowGroundwaterRibbon, Is.False);
    }

    [Test]
    public void AnalysisWorkspace_SelectGroundwaterRibbon_OpensTab6()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.Workspace.Settings.Intent = AnalysisIntent.Seepage;
        host.Workspace.SelectRibbonTabCommand.Execute("GROUNDWATER");

        Assert.That(host.SelectedTabIndex, Is.EqualTo(6));
        Assert.That(host.Workspace.IsGroundwaterRibbon, Is.True);
        Assert.That(host.IsGroundwaterWorkspace, Is.True);
    }

    [Test]
    public void AnalysisTypeWizard_SeepageMethod_StaticCheckboxWritesSettings()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.SelectCardCommand.Execute(wizard.Cards.Single(c => c.Key == "Seepage"));
        wizard.GatedSeepageFlowType = "Static";

        Assert.That(wizard.ShowSeepageStaticMethod, Is.True);
        wizard.ProjectSettingsVM.GroundwaterStaticSettings.IncludeWaterSurfaces = true;

        Assert.That(host.ProjectSettingsVM.GroundwaterStaticSettings.IncludeWaterSurfaces, Is.True);
        Assert.That(host.MaterialPropertiesVM.LastGroundwaterTreeMode,
            Is.EqualTo(MaterialPropertiesViewModel.GroundwaterStaticType));
    }

    [Test]
    public void GroundwaterExplorer_StaticWaterSurfaces_ShowsPhreaticNotPonded()
    {
        var host = FinishSeepageWithFlow("Static");
        host.ProjectSettingsVM.GroundwaterStaticSettings.IncludeWaterSurfaces = true;

        Assert.That(host.GroundwaterVM.IsEnabled, Is.True);
        Assert.That(host.GroundwaterVM.IsWaterSurfacesAllowed, Is.True);
        Assert.That(host.GroundwaterVM.IsPhreaticEditorAllowed, Is.True);
        Assert.That(host.GroundwaterVM.IsHydrostaticMode, Is.True);
        Assert.That(host.GroundwaterVM.IsPondedFreeWaterAllowed, Is.False);
        Assert.That(host.GroundwaterVM.IsRuCoefficientsAllowed, Is.False);
        Assert.That(host.GroundwaterVM.IsAnyWaterPressureGridAllowed, Is.False);
        Assert.That(host.GroundwaterVM.IsWaterHeadAllowed, Is.False);
        Assert.That(host.GroundwaterVM.IsTransientBcAllowed, Is.False);
        Assert.That(host.Workspace.Groundwater.IsPropertiesExpanded, Is.False);
        host.Workspace.SelectRibbonTabCommand.Execute("GROUNDWATER");
        Assert.That(host.WorkspaceLeftColumnWidth.Value, Is.EqualTo(MainWindowViewModel.WorkspaceExpandedGroundwaterLeftWidth));
    }

    [Test]
    public void GroundwaterExplorer_StaticWaterSurfacesPonded_ShowsPonded()
    {
        var host = FinishSeepageWithFlow("Static");
        host.ProjectSettingsVM.GroundwaterStaticSettings.IncludeWaterSurfaces = true;
        host.ProjectSettingsVM.GroundwaterStaticSettings.IncludePondedFreeWater = true;

        Assert.That(host.GroundwaterVM.IsPhreaticEditorAllowed, Is.True);
        Assert.That(host.GroundwaterVM.IsPondedFreeWaterAllowed, Is.True);
        Assert.That(host.GroundwaterVM.IsHydrostaticMode, Is.True);
        Assert.That(host.GroundwaterVM.IsAnyWaterPressureGridAllowed, Is.False);
    }

    [Test]
    public void GroundwaterExplorer_StaticRu_ShowsRuNotPhreatic()
    {
        var host = FinishSeepageWithFlow("Static");
        host.ProjectSettingsVM.GroundwaterStaticSettings.IncludeRuCoefficients = true;

        Assert.That(host.GroundwaterVM.Method, Is.EqualTo(GroundwaterMethod.Ru));
        Assert.That(host.GroundwaterVM.IsRuCoefficientsAllowed, Is.True);
        Assert.That(host.GroundwaterVM.IsWaterSurfacesAllowed, Is.False);
        Assert.That(host.GroundwaterVM.IsPhreaticEditorAllowed, Is.False);
        Assert.That(host.GroundwaterVM.IsPondedFreeWaterAllowed, Is.False);
        Assert.That(host.GroundwaterVM.IsAnyWaterPressureGridAllowed, Is.False);
    }

    [Test]
    public void GroundwaterExplorer_StaticGridTotalHead_ShowsOnlyTotalHeadRow()
    {
        var host = FinishSeepageWithFlow("Static");
        host.ProjectSettingsVM.GroundwaterStaticSettings.IncludeWaterPressureGridTotalHead = true;

        Assert.That(host.GroundwaterVM.IsTotalHeadGridAllowed, Is.True);
        Assert.That(host.GroundwaterVM.IsPressureHeadGridAllowed, Is.False);
        Assert.That(host.GroundwaterVM.IsPorePressureGridAllowed, Is.False);
        Assert.That(host.GroundwaterVM.IsAnyWaterPressureGridAllowed, Is.True);
        Assert.That(host.GroundwaterVM.IsWaterSurfacesAllowed, Is.False);
        Assert.That(host.GroundwaterVM.IsRuCoefficientsAllowed, Is.False);
        Assert.That(host.GroundwaterVM.IsHydrostaticMode, Is.True);
        Assert.That(host.GroundwaterVM.StaticMethodRestrictionMessage, Does.Contain("Water Surfaces"));
    }

    [Test]
    public void GroundwaterExplorer_StaticGridPressureHead_ShowsOnlyPressureHeadRow()
    {
        var host = FinishSeepageWithFlow("Static");
        host.ProjectSettingsVM.GroundwaterStaticSettings.IncludeWaterPressureGridPressureHead = true;

        Assert.That(host.GroundwaterVM.IsPressureHeadGridAllowed, Is.True);
        Assert.That(host.GroundwaterVM.IsTotalHeadGridAllowed, Is.False);
        Assert.That(host.GroundwaterVM.IsPorePressureGridAllowed, Is.False);
        Assert.That(host.GroundwaterVM.IsPhreaticEditorAllowed, Is.False);
        Assert.That(host.GroundwaterVM.IsRuCoefficientsAllowed, Is.False);
    }

    [Test]
    public void GroundwaterExplorer_StaticGridPorePressure_ShowsOnlyPorePressureRow()
    {
        var host = FinishSeepageWithFlow("Static");
        host.ProjectSettingsVM.GroundwaterStaticSettings.IncludeWaterPressureGridPorePressure = true;

        Assert.That(host.GroundwaterVM.IsPorePressureGridAllowed, Is.True);
        Assert.That(host.GroundwaterVM.IsTotalHeadGridAllowed, Is.False);
        Assert.That(host.GroundwaterVM.IsPressureHeadGridAllowed, Is.False);
        Assert.That(host.GroundwaterVM.IsPhreaticEditorAllowed, Is.False);
        Assert.That(host.GroundwaterVM.IsRuCoefficientsAllowed, Is.False);
    }

    [Test]
    public void GroundwaterExplorer_SteadyGeneralMeshWaterHead_AllowsWaterHeads()
    {
        var host = FinishSeepageWithFlow("Steady-State");
        host.ProjectSettingsVM.IsSteadyStateGeneral2DChecked = true;
        host.ProjectSettingsVM.SeepageSettings.IncludeWaterHeadGeneral2D = true;

        Assert.That(host.GroundwaterVM.IsWaterHeadAllowed, Is.True);
        Assert.That(host.GroundwaterVM.IsTransientBcAllowed, Is.False);
        Assert.That(host.GroundwaterVM.IsAnyWaterPressureGridAllowed, Is.False);
    }

    [Test]
    public void GroundwaterExplorer_TransientGeneralMesh_ShowsPhreaticAndTransientBcs()
    {
        var host = FinishSeepageWithFlow("Transient");
        host.ProjectSettingsVM.IsTransientP88Checked = true;

        Assert.That(host.GroundwaterVM.IsTransientBcAllowed, Is.True);
        Assert.That(host.GroundwaterVM.IsPhreaticEditorAllowed, Is.True);
        Assert.That(host.GroundwaterVM.IsPondedFreeWaterAllowed, Is.True);
        Assert.That(host.GroundwaterVM.IsHydrostaticMode, Is.True);
        Assert.That(host.ProjectSettingsVM.IsTransientP88Selected, Is.True);
    }

    private static MainWindowViewModel FinishSeepageWithFlow(string flowType)
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.SelectCardCommand.Execute(wizard.Cards.Single(c => c.Key == "Seepage"));
        wizard.GatedSeepageFlowType = flowType;
        wizard.Finish();
        return host;
    }

    [Test]
    public void AnalysisTypeWizard_SeepageMethod_TransientTimeStepWritesSettings()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.SelectCardCommand.Execute(wizard.Cards.Single(c => c.Key == "Seepage"));
        wizard.GatedSeepageFlowType = "Transient";
        wizard.ProjectSettingsVM.TransientSettings.TimeStep = 0.25;

        Assert.That(wizard.ShowSeepageTransientMethod, Is.True);
        Assert.That(host.ProjectSettingsVM.TransientSettings.TimeStep, Is.EqualTo(0.25).Within(1e-9));
        wizard.Finish();
        Assert.That(host.ProjectSettingsVM.ResolvedAnalysisKind, Is.EqualTo(AnalysisKind.GroundwaterTransient));
    }

    [Test]
    public void AnalysisTypeWizard_ConsolidationOptions_WritesTransientAndGatedCc()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.PendingIntent = AnalysisIntent.Consolidation;
        wizard.ProjectSettingsVM.TransientSettings.TimeStep = 2;
        wizard.ProjectSettingsVM.TransientSettings.NumberOfSteps = 10;
        wizard.ProjectSettingsVM.TransientSettings.InitialPressure = 12.5;

        Assert.That(wizard.IsConsolidationPending, Is.True);
        Assert.That(wizard.ConsolidationTotalTime, Is.EqualTo(20));
        Assert.That(wizard.ProjectSettingsVM.TransientSettings.InitialPressure, Is.EqualTo(12.5));
        Assert.That(wizard.GatedCompressionCc, Is.EqualTo("0.250"));
        Assert.That(wizard.ConsolidationTypes, Does.Contain("1D Primary (Terzaghi)"));
    }

    [Test]
    public void AnalysisTypeWizard_DynamicOptions_WritesPseudoStaticKh()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.PendingIntent = AnalysisIntent.Dynamic;
        wizard.LoadConditionsVM.SeismicAnalysisEnabled = true;
        wizard.DynamicSeismicKh = 0.15;

        Assert.That(wizard.IsDynamicPending, Is.True);
        Assert.That(wizard.LoadConditionsVM.SeismicAnalysisEnabled, Is.True);
        Assert.That(wizard.DynamicSeismicKh, Is.EqualTo(0.15).Within(1e-9));
        Assert.That(wizard.IntegrationMethods, Does.Contain("Newmark"));
        Assert.That(wizard.GatedRayleighAlpha, Is.EqualTo("0.020"));
    }

    [Test]
    public void AnalysisTypeWizard_ThermalOptions_WritesMaterialConductivity()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        host.MaterialPropertiesVM.Materials.Add(new MaterialModel { Id = 1, Name = "Soil", ThermalConductivityX = 2.0 });
        host.MaterialPropertiesVM.SelectedMaterialModel = host.MaterialPropertiesVM.Materials[^1];
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.PendingIntent = AnalysisIntent.Thermal;
        Assert.That(wizard.HasSelectedMaterial, Is.True);
        wizard.SelectedThermalConductivityX = 3.5;

        Assert.That(wizard.IsThermalPending, Is.True);
        Assert.That(wizard.SelectedMaterial!.ThermalConductivityX, Is.EqualTo(3.5).Within(1e-9));
        Assert.That(wizard.ThermalUnavailableReason, Does.Contain("Thermal"));
        Assert.That(wizard.GatedHeatGeneration, Is.EqualTo("0"));
    }

    [Test]
    public void AnalysisTypeWizard_CoupledOptions_EchoesLiveSeepagePath()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.PendingIntent = AnalysisIntent.Coupled;
        wizard.GroundwaterVM.IsEnabled = true;
        if (wizard.HasSelectedMaterial)
            wizard.SelectedPermeabilityX = 2.5e-6;

        Assert.That(wizard.IsCoupledPending, Is.True);
        Assert.That(wizard.GroundwaterVM.IsEnabled, Is.True);
        Assert.That(wizard.CoupledSchemes, Does.Contain("Seepage → SRF (live)"));
        Assert.That(wizard.GatedCoupledScheme, Is.EqualTo("Seepage → SRF (live)"));
    }

    [Test]
    public void AnalysisTypeWizard_SrfOptions_WritesExistingBracket()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        var wizard = new AnalysisTypeWizardViewModel(host);
        wizard.PendingIntent = AnalysisIntent.Srf;
        wizard.Workspace.Settings.InitialStrengthReductionFactor = 0.8;
        wizard.FEAnalysisVM.SrfFoSMin = 0.6;

        Assert.That(wizard.IsSrfPending, Is.True);
        Assert.That(wizard.IsGenericDefineOptions, Is.False);
        Assert.That(host.Workspace.Settings.InitialStrengthReductionFactor, Is.EqualTo(0.8).Within(1e-9));
        Assert.That(host.FEAnalysisVM.SrfFoSMin, Is.EqualTo(0.6).Within(1e-9));
        wizard.Workspace.Settings.Method = "Limit Equilibrium";
        Assert.That(wizard.IsLimitEquilibriumSelected, Is.True);
    }

    [Test]
    public void ProjectSettingsGeneralCommand_InWorkspace_RequestsDialogOnGeneralTab()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = true };
        ProjectSettingsTabKind? requested = null;
        host.RequestProjectSettingsDialog = kind => requested = kind;

        host.ProjectSettingsGeneralCommand.Execute(null);

        Assert.That(requested, Is.EqualTo(ProjectSettingsTabKind.General));
        Assert.That(host.ProjectSettingsVM.SettingsTabs[host.ProjectSettingsVM.SelectedSettingsTabIndex].Kind,
            Is.EqualTo(ProjectSettingsTabKind.General));
    }

    [Test]
    public void ProjectSettingsGeneralCommand_ClassicLayout_ShowsEmbeddedGeneralTab()
    {
        var host = new MainWindowViewModel { UseAnalysisWorkspace = false };
        var requested = false;
        host.RequestProjectSettingsDialog = _ => requested = true;

        host.ProjectSettingsGeneralCommand.Execute(null);

        Assert.That(requested, Is.False);
        Assert.That(host.SelectedTabIndex, Is.EqualTo(0));
        Assert.That(host.ProjectSettingsVM.SettingsTabs[host.ProjectSettingsVM.SelectedSettingsTabIndex].Kind,
            Is.EqualTo(ProjectSettingsTabKind.General));
    }

    /*
     * Manual / scripted engine checks for the first workspace merge (real solvers, no GUI automation):
     * - EL-01 uniaxial elastic column (Verification/slope/elastic analysis) — Static + top line load
     *   must run p610_t3_elastoplastic, σy ≈ −1000 kPa, u(top) ≈ 1.5 mm.
     * - One SRF homogeneous slope (scripts/slope_stability_T3_SRF).
     * - One steady seepage (p73/p74) deck.
     * - JR-05 triaxial joint slip (Verification/slope/joint verification).
     */
}
