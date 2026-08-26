using System.Threading;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.ViewModels;

[TestFixture]
public class GeometryExplorerHighlightTests
{
    [Test]
    public void HighlightExplorerSelection_Point_MarksVertex()
    {
        var geo = new GeometryDefinitionViewModel();
        geo.SetOutlineAxis(new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 8.0), (0.0, 8.0) });

        geo.HighlightExplorerSelection(geo.GeometryVertexRows[1]);

        Assert.That(geo.GeometryVertexRows[1].IsHighlighted, Is.True);
        Assert.That(geo.GeometryVertexRows[0].IsHighlighted, Is.False);
        Assert.That(geo.HighlightedLineNumber, Is.Null);
    }

    [Test]
    public void HighlightExplorerSelection_Line_MarksSegmentAndEnds()
    {
        var geo = new GeometryDefinitionViewModel();
        geo.SetOutlineAxis(new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 8.0), (0.0, 8.0) });
        Assert.That(geo.GeometryLineSegments, Is.Not.Empty);
        var line = geo.GeometryLineSegments[0];

        geo.HighlightExplorerSelection(line);

        Assert.That(geo.HighlightedLineNumber, Is.EqualTo(line.LineNumber));
        Assert.That(line.IsHighlighted, Is.True);
        Assert.That(geo.HighlightedSegmentPoints.Count, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void HighlightExplorerSelection_Surface_SelectsRegionOverlay()
    {
        var geo = new GeometryDefinitionViewModel();
        geo.SetOutlineAxis(new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 8.0), (0.0, 8.0) });
        var surface = new GeometrySurface(1, "Pad");
        if (geo.GeometryLineSegments.Count > 0)
            surface.ConnectedLineNumbers.Add(geo.GeometryLineSegments[0].LineNumber);
        geo.GeometrySurfaces.Add(surface);

        geo.HighlightExplorerSelection(surface);

        Assert.That(geo.SelectedGeometrySurface, Is.SameAs(surface));
        Assert.That(geo.ExplorerHighlightPolylines, Is.Not.Empty);
    }

    [Test]
    public void HighlightExplorerSelection_SoilBlock_HighlightsClosedRing()
    {
        var geo = new GeometryDefinitionViewModel();
        geo.SetOutlineAxis(new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 8.0), (0.0, 8.0) });

        geo.HighlightExplorerSelection(GeometryDefinitionViewModel.SoilBlockExplorerPayload);

        Assert.That(geo.GeometryVertexRows.All(v => v.IsHighlighted), Is.True);
        Assert.That(geo.ExplorerHighlightPolylines, Is.Not.Empty);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void FinishActiveCanvasTool_ClearsDrawLinePreview()
    {
        var geo = new GeometryDefinitionViewModel();
        geo.SetOutlineAxis(new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 8.0), (0.0, 8.0) });
        geo.StartDrawLineModeCommand.Execute(null);
        var first = geo.GeometryVertexRows[0];
        geo.HandleDrawLineModeCanvasClick(new System.Windows.Point(first.X, first.Y));

        Assert.That(geo.IsDrawLinePreviewVisible, Is.True);

        geo.FinishActiveCanvasTool();

        Assert.That(geo.IsDrawLinePreviewVisible, Is.False);
        Assert.That(geo.IsDrawLineMode, Is.False);
        Assert.That(geo.CanFinishCanvasTool, Is.False);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void FinishActiveCanvasTool_LeavesSurfacePickMode()
    {
        var geo = new GeometryDefinitionViewModel();
        geo.SetOutlineAxis(new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 8.0), (0.0, 8.0) });
        geo.BeginExplorerSurfaceDefinition();
        Assert.That(geo.IsSurfacePolygonPickMode, Is.True);

        geo.FinishActiveCanvasTool();

        Assert.That(geo.IsSurfacePolygonPickMode, Is.False);
        Assert.That(geo.IsDrawLinePreviewVisible, Is.False);
    }
}
