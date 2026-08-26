using System.Windows;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.ViewModels;

[TestFixture]
public class GroundwaterTransientBcPopupTests
{
    private static GroundwaterViewModel BuildTransientGroundwater(
        out GeometryDefinitionViewModel geometry,
        out ProjectSettingsViewModel settings,
        MeshViewModel? mesh = null)
    {
        geometry = new GeometryDefinitionViewModel
        {
            CanvasWidth = 600,
            CanvasHeight = 400,
            AxisXMin = 0,
            AxisXMax = 12,
            AxisYMin = 0,
            AxisYMax = 8,
            ShowGroundwaterZoneOverlaysOnCanvas = true,
        };
        foreach (var (i, x, y) in new[] { (0, 0.0, 0.0), (1, 10.0, 0.0), (2, 10.0, 6.0), (3, 0.0, 6.0) })
        {
            geometry.GeometryVertexRows.Add(new GeometryDefinitionViewModel.GeometryVertexDisplay
            {
                Index = i,
                AxisX = x,
                AxisY = y,
                StablePointId = i + 1,
            });
        }

        var materials = new MaterialPropertiesViewModel
        {
            SelectedAnalysisCategory = MaterialPropertiesViewModel.GroundwaterAnalysisCategory,
            SelectedDetailedAnalysisType = MaterialPropertiesViewModel.GroundwaterTransientGeneral2DType,
        };
        settings = new ProjectSettingsViewModel();
        settings.SetAxisSyncTargets(geometry, materials);

        var groundwater = new GroundwaterViewModel
        {
            GeometryDefinition = geometry,
            IsEnabled = true,
        };
        groundwater.SetProjectSettingsViewModel(settings);
        groundwater.SetMaterialPropertiesViewModel(materials);
        if (mesh != null)
        {
            groundwater.SetMeshViewModel(mesh);
            geometry.MeshSource = mesh;
        }
        settings.SetGroundwaterViewModel(groundwater);
        geometry.SetGroundwaterViewModel(groundwater);
        settings.IsTransientP88Checked = true;
        return groundwater;
    }

    [Test]
    public void TransientStateSelected_AllowsTransientBcsAndSharesSettingsInstance()
    {
        var geometry = new GeometryDefinitionViewModel();
        var materials = new MaterialPropertiesViewModel
        {
            SelectedAnalysisCategory = MaterialPropertiesViewModel.GroundwaterAnalysisCategory,
            SelectedDetailedAnalysisType = MaterialPropertiesViewModel.GroundwaterStaticType,
        };
        var settings = new ProjectSettingsViewModel();
        settings.SetAxisSyncTargets(geometry, materials);

        var groundwater = new GroundwaterViewModel { IsEnabled = true };
        groundwater.SetProjectSettingsViewModel(settings);
        groundwater.SetMaterialPropertiesViewModel(materials);
        settings.SetGroundwaterViewModel(groundwater);

        Assert.That(groundwater.IsTransientBcAllowed, Is.False);
        settings.IsTransientP84Checked = true;
        Assert.That(groundwater.IsTransientBcAllowed, Is.True);
        Assert.That(groundwater.TransientSettings, Is.SameAs(settings.TransientSettings));
    }

    [Test]
    public void Section1FaceCheckboxes_DriveCanvasOverlays()
    {
        var groundwater = BuildTransientGroundwater(out var geometry, out var settings);
        settings.TransientSettings.FixTopFace = true;
        settings.TransientSettings.FixBottomFace = false;
        settings.TransientSettings.FixMinXFace = false;
        settings.TransientSettings.FixMaxXFace = true;
        settings.TransientSettings.FixedPressure = 1.5;
        geometry.RefreshTransientPressureOverlays();

        Assert.That(geometry.TransientPressureBoundaryOverlays, Has.Count.EqualTo(2));
        Assert.That(geometry.TransientPressureBoundaryOverlays.SelectMany(x => x.Labels)
            .Select(x => x.Text), Has.Some.Contains("1.5"));
    }

    [Test]
    public void SectionAccordion_DefaultsToSection1AndIsExclusive()
    {
        var groundwater = BuildTransientGroundwater(out _, out _);
        Assert.That(groundwater.IsTransientBulkFaceSectionEnabled, Is.True);
        Assert.That(groundwater.IsTransientInteractiveBcSectionEnabled, Is.False);
        Assert.That(groundwater.IsTransientNodeSelectionEnabled, Is.False);

        groundwater.IsTransientInteractiveBcSectionEnabled = true;
        Assert.That(groundwater.IsTransientBulkFaceSectionEnabled, Is.False);
        Assert.That(groundwater.IsTransientInteractiveBcSectionEnabled, Is.True);
        Assert.That(groundwater.IsTransientNodeSelectionEnabled, Is.False);

        groundwater.IsTransientNodeSelectionEnabled = true;
        Assert.That(groundwater.IsTransientBulkFaceSectionEnabled, Is.False);
        Assert.That(groundwater.IsTransientInteractiveBcSectionEnabled, Is.False);
        Assert.That(groundwater.IsTransientNodeSelectionEnabled, Is.True);

        groundwater.IsTransientNodeSelectionEnabled = false;
        Assert.That(groundwater.IsTransientBulkFaceSectionEnabled, Is.False);
        Assert.That(groundwater.IsTransientInteractiveBcSectionEnabled, Is.False);
        Assert.That(groundwater.IsTransientNodeSelectionEnabled, Is.False);
    }

    [Test]
    public void FitAxisBoundsToVertexExtents_FitsGeometryContent()
    {
        var groundwater = BuildTransientGroundwater(out var geometry, out _);
        geometry.AxisXMin = -100;
        geometry.AxisXMax = 200;
        geometry.AxisYMin = -50;
        geometry.AxisYMax = 150;

        geometry.FitAxisBoundsToVertexExtents();

        Assert.That(geometry.AxisXMin, Is.LessThanOrEqualTo(0));
        Assert.That(geometry.AxisXMax, Is.GreaterThanOrEqualTo(10));
        Assert.That(geometry.AxisYMin, Is.LessThanOrEqualTo(0));
        Assert.That(geometry.AxisYMax, Is.GreaterThanOrEqualTo(6));
        Assert.That(geometry.AxisXMax - geometry.AxisXMin, Is.LessThan(200));
        Assert.That(geometry.AxisYMax - geometry.AxisYMin, Is.LessThan(150));
    }

    [Test]
    public void UserDefinedFaceRow_AddsWithoutCanvasOrNodeSelection()
    {
        var groundwater = BuildTransientGroundwater(out var geometry, out var settings);
        settings.TransientSettings.FixTopFace = false;
        settings.TransientSettings.FixMaxXFace = false;

        groundwater.IsTransientInteractiveBcSectionEnabled = true;
        groundwater.TransientFixedPressureDraft.DefinitionMethod = WaterHeadDefinitionMethod.UserDefined;
        groundwater.TransientFixedPressureDraft.Face = TransientDrainFace.Bottom;
        groundwater.TransientFixedPressureDraft.Pressure = 3.5;

        Assert.That(groundwater.IsTransientFixedPressureMousePlacementActive, Is.False);
        Assert.That(groundwater.AddTransientFixedPressureNodeCommand.CanExecute(null), Is.True);
        groundwater.AddTransientFixedPressureNodeCommand.Execute(null);

        Assert.That(groundwater.TransientFixedPressureNodes, Has.Count.EqualTo(1));
        Assert.That(groundwater.TransientFixedPressureNodes[0].IsUserDefined, Is.True);
        Assert.That(groundwater.TransientFixedPressureNodes[0].Face, Is.EqualTo(TransientDrainFace.Bottom));
    }

    [Test]
    public void MouseSelectLine_RequiresSection2Open()
    {
        var mesh = new MeshViewModel { IsDiscretized = true };
        mesh.DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay { NodeId = 1, Location = new Point(0, 0) });
        mesh.DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay { NodeId = 2, Location = new Point(10, 0) });
        var groundwater = BuildTransientGroundwater(out _, out _, mesh);

        groundwater.TransientFixedPressureDraft.DefinitionMethod = WaterHeadDefinitionMethod.MouseSelect;
        Assert.That(groundwater.IsTransientFixedPressureMousePlacementActive, Is.False,
            "Section 1 is open by default — Line placement should be inactive.");

        groundwater.IsTransientInteractiveBcSectionEnabled = true;
        Assert.That(groundwater.IsTransientFixedPressureMousePlacementActive, Is.True);
        Assert.That(groundwater.TransientFixedPressureDraft.PlacementType, Is.EqualTo(WaterHeadPlacementType.Line));

        groundwater.RegisterTransientFixedPressureNode(new Point(0.1, 0.1));
        Assert.That(groundwater.IsTransientFixedPressureLineAwaitingEnd, Is.True);
        groundwater.RegisterTransientFixedPressureNode(new Point(9.9, 0.1));
        Assert.That(groundwater.TransientFixedPressureDraft.HasPlacement, Is.True);
        groundwater.AddTransientFixedPressureNodeCommand.Execute(null);
        Assert.That(groundwater.TransientFixedPressureNodes, Has.Count.EqualTo(1));
        Assert.That(groundwater.TransientFixedPressureNodes[0].IsLinePlacement, Is.True);
    }

    [Test]
    public void TypedNodeIds_AddMultiplePointRowsWhenNodeSelectionEnabled()
    {
        var mesh = new MeshViewModel { IsDiscretized = true };
        mesh.DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay { NodeId = 7, Location = new Point(1, 2) });
        mesh.DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay { NodeId = 9, Location = new Point(3, 4) });
        var groundwater = BuildTransientGroundwater(out var geometry, out _, mesh);

        groundwater.IsTransientNodeSelectionEnabled = true;
        Assert.That(groundwater.IsTransientFixedPressureMousePlacementActive, Is.False);
        groundwater.TransientNodeSelectionPressure = 2.5;
        groundwater.TransientNodeSelectionIdsText = "7, 9";

        Assert.That(groundwater.AddTransientNodeSelectionCommand.CanExecute(null), Is.True);
        groundwater.AddTransientNodeSelectionCommand.Execute(null);

        Assert.That(groundwater.TransientFixedPressureNodes, Has.Count.EqualTo(2));
        Assert.That(groundwater.TransientFixedPressureNodes.All(n => n.IsPointPlacement), Is.True);
        Assert.That(geometry.TransientFixedPressureNodeOverlays.Count, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void DragSelection_RegistersPreviewThenAddsNodes()
    {
        var mesh = new MeshViewModel { IsDiscretized = true };
        mesh.DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay { NodeId = 11, Location = new Point(2, 2) });
        mesh.DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay { NodeId = 12, Location = new Point(5, 5) });
        var groundwater = BuildTransientGroundwater(out var geometry, out _, mesh);
        groundwater.IsTransientNodeSelectionEnabled = true;

        groundwater.RegisterTransientFixedPressureNodesFromSelection(new[]
        {
            (11, new Point(2, 2)),
            (12, new Point(5, 5)),
        });

        Assert.That(groundwater.TransientNodeSelectionIdsText, Does.Contain("11"));
        Assert.That(groundwater.TransientNodeSelectionPreview, Has.Count.EqualTo(2));

        groundwater.TransientNodeSelectionPressure = 8;
        groundwater.AddTransientNodeSelectionCommand.Execute(null);
        Assert.That(groundwater.TransientFixedPressureNodes, Has.Count.EqualTo(2));
        Assert.That(groundwater.TransientFixedPressureNodes[0].Pressure, Is.EqualTo(8));
    }
}
