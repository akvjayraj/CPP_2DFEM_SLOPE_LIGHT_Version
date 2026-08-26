using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Tests.Integration.Support;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Integration;

/// <summary>
/// File-dependent portion of the MeshBcImportSmokeTest harness: imports canonical verification
/// decks into the view-models and asserts node/element/BC counts and macro-boundary simplification.
/// Categorised Integration because it depends on the on-disk Verification fixtures.
/// </summary>
[TestFixture]
[Category("Integration")]
public class MeshDeckImportTests
{
    private static (MeshViewModel mesh, UserDefinedMeshViewModel udm, BoundaryConditionsViewModel bc, MeshBoundaryImportSummary summary)
        Import(string deckRelativePath)
    {
        var content = RepoFixtures.ReadFixture(deckRelativePath);
        var mesh = new MeshViewModel();
        var udm = new UserDefinedMeshViewModel(mesh);
        var bc = new BoundaryConditionsViewModel { MeshVM = mesh };
        Assert.That(DatMeshBoundaryImporter.TryImportToViewModels(content, udm, bc, out var summary), Is.True,
            $"failed to import {deckRelativePath}");
        return (mesh, udm, bc, summary);
    }

    [Test]
    public void Program64Embankment_ImportsExpectedMeshAndSimplifiesBoundary()
    {
        var (mesh, udm, bc, summary) = Import("Verification/slope/p64_embankment_program64.dat");

        Assert.That(summary.NNodes, Is.EqualTo(1141));
        Assert.That(summary.NElements, Is.EqualTo(350));
        Assert.That(bc.BoundaryConditions.Count, Is.EqualTo(191));
        Assert.That(udm.NodeRows.Count, Is.EqualTo(1141));
        Assert.That(udm.ElementRows.Count, Is.EqualTo(350));
        Assert.That(mesh.IsDiscretized, Is.True);

        var geom = new GeometryDefinitionViewModel { MeshSource = mesh };
        geom.ImportBoundaryFromMesh();
        Assert.That(geom.GeometryVertexRows.Count, Is.EqualTo(6), "embankment outline collapses to 6 macro corners");
        Assert.That(geom.GeometryLineSegments.Count, Is.EqualTo(6));
    }

    [Test]
    public void Ex2TwoLayerSlope_ImportsExpectedCounts()
    {
        var (mesh, udm, bc, summary) = Import("Verification/slope/Ex2_twolayerslope/ex2_twolayer_slope.dat");

        Assert.That(summary.NNodes, Is.EqualTo(266));
        Assert.That(summary.NElements, Is.EqualTo(75));
        Assert.That(bc.BoundaryConditions.Count, Is.EqualTo(91));
        Assert.That(mesh.IsDiscretized, Is.True);
    }

    [Test]
    public void StructuredRectangularQ8_BuiltDeckRoundTripsThroughImporter()
    {
        var opts = new StructuredRectangularQ8DatWriter.WriteOptions
        {
            XMin = 0, XMax = 60, YMin = 0, YMax = 15, ElementsX = 20, ElementsY = 10,
        };
        var built = StructuredRectangularQ8DatWriter.BuildMesh(opts);
        Assert.That(built.Nn, Is.EqualTo(861));
        Assert.That(built.Nels, Is.EqualTo(200));

        var dat = StructuredRectangularQ8DatWriter.WriteExplicitDeck(built, opts);
        Assert.That(DatMeshBoundaryImporter.IsSerendipityRectangularQ8Deck(dat), Is.True);

        var mesh = new MeshViewModel();
        var udm = new UserDefinedMeshViewModel(mesh);
        var bc = new BoundaryConditionsViewModel { MeshVM = mesh };
        Assert.That(DatMeshBoundaryImporter.TryImportToViewModels(dat, udm, bc, out var summary), Is.True);
        Assert.That(summary.NNodes, Is.EqualTo(861));
        Assert.That(summary.NElements, Is.EqualTo(200));
    }
}
