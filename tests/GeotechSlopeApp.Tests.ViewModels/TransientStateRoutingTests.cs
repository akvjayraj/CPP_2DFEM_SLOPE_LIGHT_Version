using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.ViewModels;

[TestFixture]
public class TransientStateRoutingTests
{
    [Test]
    public void ActivateTransientP84SetsDetailedTypeAndTreeMode()
    {
        var materials = new MaterialPropertiesViewModel
        {
            SelectedAnalysisCategory = MaterialPropertiesViewModel.GroundwaterAnalysisCategory,
            SelectedDetailedAnalysisType = MaterialPropertiesViewModel.GroundwaterStaticType,
        };
        var settings = new ProjectSettingsViewModel();
        settings.SetAxisSyncTargets(new GeometryDefinitionViewModel(), materials);

        settings.IsTransientP84Checked = true;

        Assert.That(materials.SelectedDetailedAnalysisType,
            Is.EqualTo(MaterialPropertiesViewModel.GroundwaterTransientPlaneAxisymType));
        Assert.That(materials.LastGroundwaterTreeMode,
            Is.EqualTo(MaterialPropertiesViewModel.GroundwaterTransientStateType));
        Assert.That(materials.LastTransientSolverType,
            Is.EqualTo(MaterialPropertiesViewModel.GroundwaterTransientPlaneAxisymType));
        Assert.That(settings.IsTransientStateDetailSelected, Is.True);
        Assert.That(settings.IsTransientP84Selected, Is.True);
    }

    [Test]
    public void P84DeckContainsPlaneHeaderAndTimeControls()
    {
        var mesh = BuildStructuredQ4Mesh(2, 2);
        var settings = new TransientAnalysisSettings
        {
            TimeStep = 0.01,
            NumberOfSteps = 150,
            Theta = 0.5,
            PrintInterval = 10,
            MonitorNode = 5,
            ContourStep = 100,
        };

        var (programId, deck) = TransientDatContentBuilder.BuildWithProgramId(
            MaterialPropertiesViewModel.GroundwaterTransientPlaneAxisymType,
            mesh,
            new MaterialPropertiesViewModel(),
            settings);

        Assert.That(programId, Is.EqualTo("p84"));
        Assert.That(deck, Does.Contain("p84"));
        Assert.That(deck, Does.Contain("'plane' 'x'"));
        Assert.That(deck, Does.Match(@"(?m)^2 2 1\r?$"));
        Assert.That(deck, Does.Match(@"(?m)^0\.01 150 0\.5 10 5 100\r?$"));
    }

    [Test]
    public void NonStructuredT3UnderP84OptionAutoRoutesToP88()
    {
        var mesh = BuildT3Mesh();
        var settings = new TransientAnalysisSettings { Axisymmetric = false };

        var (programId, deck) = TransientDatContentBuilder.BuildWithProgramId(
            MaterialPropertiesViewModel.GroundwaterTransientPlaneAxisymType,
            mesh,
            new MaterialPropertiesViewModel(),
            settings);

        Assert.That(programId, Is.EqualTo("p88"));
        Assert.That(deck, Does.Contain("p88"));
        Assert.That(deck, Does.Contain("'triangle'"));
        // Header: 'triangle' nels nn nip nod ndim np_types
        Assert.That(deck, Does.Match(@"(?m)^'triangle' 2 4 1 3 2 1\r?$"));
    }

    [Test]
    public void General2DBuildsHomogeneousQ8Deck()
    {
        var mesh = BuildQ8Mesh();
        var settings = new TransientAnalysisSettings();

        var (programId, deck) = TransientDatContentBuilder.BuildWithProgramId(
            MaterialPropertiesViewModel.GroundwaterTransientGeneral2DType,
            mesh,
            new MaterialPropertiesViewModel(),
            settings);

        Assert.That(programId, Is.EqualTo("p88"));
        Assert.That(deck, Does.Contain("'quadrilateral'"));
        Assert.That(deck, Does.Match(@"(?m)^'quadrilateral' 1 8 4 8 2 1\r?$"));
    }

    [Test]
    public void AxisymmetricOnNonStructuredMeshThrows()
    {
        var mesh = BuildT3Mesh();
        var settings = new TransientAnalysisSettings { Axisymmetric = true };

        Assert.Throws<InvalidOperationException>(() =>
            TransientDatContentBuilder.BuildWithProgramId(
                MaterialPropertiesViewModel.GroundwaterTransientPlaneAxisymType,
                mesh,
                new MaterialPropertiesViewModel(),
                settings));
    }

    [Test]
    public void P84ExplicitFixedPressureUsesPlacedCoordinatesAndPerNodeValue()
    {
        var mesh = BuildStructuredQ4Mesh(2, 2);
        var settings = new TransientAnalysisSettings
        {
            FixTopFace = false,
            FixBottomFace = false,
            FixMinXFace = false,
            FixMaxXFace = false,
        };
        var explicitNodes = new[]
        {
            new TransientFixedPressureNode
            {
                DefinitionMethod = WaterHeadDefinitionMethod.MouseSelect,
                PlacementType = WaterHeadPlacementType.Point,
                NodeId = 1,
                StartX = 0,
                StartY = 0,
                HasPlacement = true,
                Pressure = 12.5,
            }
        };

        var (_, deck) = TransientDatContentBuilder.BuildWithProgramId(
            MaterialPropertiesViewModel.GroundwaterTransientPlaneAxisymType,
            mesh,
            new MaterialPropertiesViewModel(),
            settings,
            explicitNodes);

        // p84 numbers its structured grid top-down, so mesh node 1 at (0,0) becomes deck node 7.
        Assert.That(deck, Does.Match(@"(?m)^! fixed freedoms\r?\n1\r?\n7 12\.5\r?$"));
        Assert.That(deck, Does.Not.Contain("1 12.5"));
    }

    [Test]
    public void P84UserDefinedFaceAppliesWholeFaceAtRowPressure()
    {
        var mesh = BuildStructuredQ4Mesh(2, 2);
        var settings = new TransientAnalysisSettings
        {
            FixTopFace = false,
            FixBottomFace = false,
            FixMinXFace = false,
            FixMaxXFace = false,
            FixedPressure = 0,
        };
        var explicitNodes = new[]
        {
            new TransientFixedPressureNode
            {
                DefinitionMethod = WaterHeadDefinitionMethod.UserDefined,
                Face = TransientDrainFace.MaxX,
                Pressure = 4.25,
                HasPlacement = false,
            }
        };

        var (_, deck) = TransientDatContentBuilder.BuildWithProgramId(
            MaterialPropertiesViewModel.GroundwaterTransientPlaneAxisymType,
            mesh,
            new MaterialPropertiesViewModel(),
            settings,
            explicitNodes);

        Assert.That(deck, Does.Contain("4.25"));
        // max-x face on 2x2 grid (3 cols × 3 rows): nodes 3,6,9 in top-down numbering.
        Assert.That(deck, Does.Match(@"(?m)^! fixed freedoms\r?\n3\r?\n"));
    }

    private static MeshViewModel BuildStructuredQ4Mesh(int nxe, int nye)
    {
        var mesh = new MeshViewModel();
        int elementId = 0;
        for (int iy = 0; iy < nye; iy++)
        for (int ix = 0; ix < nxe; ix++)
        {
            int n1 = iy * (nxe + 1) + ix + 1;
            int n2 = n1 + 1;
            int n4 = n1 + (nxe + 1);
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
        return mesh;
    }

    private static MeshViewModel BuildT3Mesh()
    {
        var mesh = new MeshViewModel();
        mesh.MeshInformationRows.Add(new MeshInformationRow
        {
            ElementId = 1,
            Node1 = 1, Node2 = 2, Node3 = 3,
            N1X = 0, N1Y = 0,
            N2X = 1, N2Y = 0,
            N3X = 0, N3Y = 1,
            Etype = 1,
            Kind = "T3",
            Status = "Active",
        });
        mesh.MeshInformationRows.Add(new MeshInformationRow
        {
            ElementId = 2,
            Node1 = 2, Node2 = 4, Node3 = 3,
            N1X = 1, N1Y = 0,
            N2X = 1, N2Y = 1,
            N3X = 0, N3Y = 1,
            Etype = 1,
            Kind = "T3",
            Status = "Active",
        });
        return mesh;
    }

    private static MeshViewModel BuildQ8Mesh()
    {
        var mesh = new MeshViewModel();
        mesh.MeshInformationRows.Add(new MeshInformationRow
        {
            ElementId = 1,
            Node1 = 1, Node2 = 2, Node3 = 3, Node4 = 4,
            Node5 = 5, Node6 = 6, Node7 = 7, Node8 = 8,
            N1X = 0, N1Y = 0,
            N2X = 1, N2Y = 0,
            N3X = 1, N3Y = 1,
            N4X = 0, N4Y = 1,
            N5X = 0.5, N5Y = 0,
            N6X = 1, N6Y = 0.5,
            N7X = 0.5, N7Y = 1,
            N8X = 0, N8Y = 0.5,
            Etype = 1,
            Kind = "Q8",
            Status = "Active",
            SmithEmb2dLocalOrder = false,
        });
        return mesh;
    }
}
