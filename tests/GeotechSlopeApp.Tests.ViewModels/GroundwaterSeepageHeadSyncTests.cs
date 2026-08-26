using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.ViewModels;

[TestFixture]
public class GroundwaterSeepageHeadSyncTests
{
    [Test]
    public void MouseSelectRowsUpdateP73ScalarHeads()
    {
        var geometry = new GeometryDefinitionViewModel();
        var materials = new MaterialPropertiesViewModel
        {
            SelectedAnalysisCategory = MaterialPropertiesViewModel.GroundwaterAnalysisCategory,
            SelectedDetailedAnalysisType = MaterialPropertiesViewModel.GroundwaterSeepageFreeSurfaceType,
        };
        var settings = new ProjectSettingsViewModel();
        settings.SetAxisSyncTargets(geometry, materials);
        settings.SeepageSettings.IncludeWaterHeadFreeSurface = true;

        var groundwater = new GroundwaterViewModel();
        groundwater.SetProjectSettingsViewModel(settings);
        groundwater.WaterHeads.Add(new WaterHeadModel
        {
            IsEnabled = true,
            Role = WaterHeadRole.Upstream,
            DefinitionMethod = WaterHeadDefinitionMethod.MouseSelect,
            Head = 100.0,
        });
        groundwater.WaterHeads.Add(new WaterHeadModel
        {
            IsEnabled = true,
            Role = WaterHeadRole.Downstream,
            DefinitionMethod = WaterHeadDefinitionMethod.MouseSelect,
            Head = 20.0,
        });

        groundwater.SyncSeepageSettingsFromWaterHeads();

        Assert.That(settings.SeepageSettings.UpstreamHead, Is.EqualTo(100.0));
        Assert.That(settings.SeepageSettings.DownstreamHead, Is.EqualTo(20.0));
    }

    [Test]
    public void P73DeckMarksMinXAsPhysicalUpstreamWhenPlacedThere()
    {
        var mesh = new MeshViewModel();
        int elementId = 0;
        for (int iy = 0; iy < 2; iy++)
        for (int ix = 0; ix < 2; ix++)
        {
            int n1 = iy * 3 + ix + 1;
            int n2 = n1 + 1;
            int n4 = n1 + 3;
            int n3 = n4 + 1;
            mesh.MeshInformationRows.Add(new MeshInformationRow
            {
                ElementId = ++elementId,
                Node1 = n1, Node2 = n2, Node3 = n3, Node4 = n4,
                N1X = ix, N1Y = iy,
                N2X = ix + 1, N2Y = iy,
                N3X = ix + 1, N3Y = iy + 1,
                N4X = ix, N4Y = iy + 1,
                Etype = 1,
                Kind = "Q4",
                Status = "Active",
            });
        }

        var settings = new SeepageAnalysisSettings
        {
            UpstreamHead = 10.0,
            DownstreamHead = 2.0,
        };
        var heads = new[]
        {
            new WaterHeadModel
            {
                IsEnabled = true,
                Role = WaterHeadRole.Upstream,
                DefinitionMethod = WaterHeadDefinitionMethod.MouseSelect,
                PlacementType = WaterHeadPlacementType.Line,
                Head = 10.0,
                StartX = 0, StartY = 0, EndX = 0, EndY = 2,
                HasPlacement = true,
            },
            new WaterHeadModel
            {
                IsEnabled = true,
                Role = WaterHeadRole.Downstream,
                DefinitionMethod = WaterHeadDefinitionMethod.MouseSelect,
                PlacementType = WaterHeadPlacementType.Line,
                Head = 2.0,
                StartX = 2, StartY = 0, EndX = 2, EndY = 2,
                HasPlacement = true,
            },
        };

        string deck = SeepageDatContentBuilder.Build(
            MaterialPropertiesViewModel.GroundwaterSeepageFreeSurfaceType,
            mesh,
            new MaterialPropertiesViewModel(),
            settings,
            heads,
            useInteractiveWaterHeads: true);

        Assert.That(deck, Does.Contain("! physical upstream face: min-x"));
        Assert.That(deck, Does.Match(@"(?m)^2 2 0\.01 20 1 -1 0\r?$"));
    }
}
