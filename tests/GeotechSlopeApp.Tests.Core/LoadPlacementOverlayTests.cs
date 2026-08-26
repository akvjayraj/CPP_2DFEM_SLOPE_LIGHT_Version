using System.Windows;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core;

[TestFixture]
public class LoadPlacementOverlayTests
{
    [Test]
    public void LineLoad_AxisMetres_DrawsOnLoadCanvas()
    {
        var geom = Jr01Geometry();
        var load = new LoadModel
        {
            Type = LoadType.Line,
            Direction = LoadDirection.Y,
            Orientation = LoadOrientation.VerticalDown,
            Magnitude = 10000,
            EndMagnitude = 10000,
            StartX = 0,
            StartY = 2,
            EndX = 1,
            EndY = 2,
            IsEnabled = true,
        };

        Assert.That(LoadPlacementCoords.LooksLikeAxisSegment(
            new Point(0, 2), new Point(1, 2), geom), Is.True);

        var overlay = LineLoadOverlayBuilder.Build(load, mesh: null, geom, ProblemDimension.TwoD);
        Assert.That(overlay, Is.Not.Null, "JR-01 top compression must appear on the Load Conditions canvas");
        Assert.That(overlay!.Arrows, Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(overlay.EdgePoints, Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(overlay.EdgePoints[0].X, Is.GreaterThan(50));
        Assert.That(overlay.EdgePoints[^1].X, Is.GreaterThan(overlay.EdgePoints[0].X + 50));
    }

    [Test]
    public void LineLoad_CanvasPixels_StillDraws()
    {
        var geom = Jr01Geometry();
        var start = geom.ConvertAxisToCanvas(new Point(0, 2));
        var end = geom.ConvertAxisToCanvas(new Point(1, 2));
        var load = new LoadModel
        {
            Type = LoadType.Line,
            Direction = LoadDirection.Y,
            Magnitude = 10000,
            EndMagnitude = 10000,
            StartX = start.X,
            StartY = start.Y,
            EndX = end.X,
            EndY = end.Y,
            IsEnabled = true,
        };

        var overlay = LineLoadOverlayBuilder.Build(load, mesh: null, geom, ProblemDimension.TwoD);
        Assert.That(overlay, Is.Not.Null);
        Assert.That(overlay!.Arrows, Has.Count.GreaterThanOrEqualTo(2));
    }

    private static GeometryDefinitionViewModel Jr01Geometry()
    {
        return new GeometryDefinitionViewModel
        {
            CanvasWidth = 600,
            CanvasHeight = 400,
            AxisXMin = -0.2,
            AxisXMax = 1.2,
            AxisYMin = -0.2,
            AxisYMax = 2.2,
        };
    }
}
