using System.Windows;
using GeotechSlopeApp.Tests.ViewModels.Support;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.ViewModels;

/// <summary>
/// NUnit port of the NonHorizLayerSmoke console harness (X2D_GUI/SmokeTest/NonHorizLayerProgram.cs):
/// drives the GeometryDefinitionViewModel non-horizontal layer flow headlessly and asserts that a
/// finalized interface cut line is produced for straight, curved, and sloped boundaries.
/// </summary>
[TestFixture]
public class NonHorizontalLayerTests
{
    [SetUp]
    public void SetUp() => RecordingDialogService.Install();

    private static readonly (double x, double y)[] RectBoundary =
    {
        (2, 2), (28, 2), (28, 20), (2, 20),
    };

    private static readonly (double x, double y)[] SlopeBoundary =
    {
        (0, 0), (32, 0), (32, 8), (20, 8), (8, 16), (0, 16),
    };

    private static GeometryDefinitionViewModel BuildGeometry((double x, double y)[] boundary)
    {
        var vm = new GeometryDefinitionViewModel
        {
            CanvasWidth = 600,
            CanvasHeight = 400,
            AxisXMin = 0,
            AxisXMax = 34,
            AxisYMin = 0,
            AxisYMax = 22,
        };

        for (var i = 0; i < boundary.Length; i++)
        {
            vm.GeometryVertexRows.Add(new GeometryDefinitionViewModel.GeometryVertexDisplay
            {
                Index = i,
                AxisX = boundary[i].x,
                AxisY = boundary[i].y,
                StablePointId = i + 1,
            });
        }

        vm.RebuildGeometryAfterProjectLoad();
        vm.UseLayerNamesOnMaterialCanvas = true;
        vm.IsNonHorizontalLayerMode = true;
        return vm;
    }

    private static void DrawCut(GeometryDefinitionViewModel vm, (double x, double y)[] cut)
    {
        vm.StartNonHorizontalLayerDraw();
        foreach (var (x, y) in cut)
            vm.HandleNonHorizontalLayerCanvasClick(vm.ConvertAxisToCanvas(new Point(x, y)));
        vm.FinishNonHorizontalLayerDraw();
    }

    [Test]
    public void CurvedCut_OnRectangle_ProducesInterfaceLine()
    {
        var vm = BuildGeometry(RectBoundary);

        DrawCut(vm, new[] { (2.0, 8.0), (8.0, 14.0), (14.0, 6.0), (20.0, 14.0), (28.0, 9.0) });

        Assert.That(vm.NonHorizontalLayerCanvasLines.Count, Is.GreaterThan(0),
            "a finalized non-horizontal cut line should be drawn");
    }

    [Test]
    public void CurvedCut_OnSlope_ProducesInterfaceLine()
    {
        var vm = BuildGeometry(SlopeBoundary);

        DrawCut(vm, new[] { (2.0, 6.0), (10.0, 11.0), (16.0, 5.0), (24.0, 10.0), (30.0, 7.0) });

        Assert.That(vm.NonHorizontalLayerCanvasLines.Count, Is.GreaterThan(0));
    }

    [Test]
    public void TwoCuts_OnSlope_ProduceMultipleInterfaceLines()
    {
        var vm = BuildGeometry(SlopeBoundary);

        DrawCut(vm, new[] { (1.0, 4.0), (9.0, 7.0), (17.0, 3.5), (25.0, 6.5), (31.0, 4.5) });
        DrawCut(vm, new[] { (1.0, 11.0), (9.0, 13.5), (17.0, 10.5), (25.0, 7.6), (31.0, 7.2) });

        Assert.That(vm.NonHorizontalLayerCanvasLines.Count, Is.GreaterThanOrEqualTo(2));
    }
}
