using System.IO;
using System.Windows;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.FieldStations;
using GeotechSlopeApp.Models.Geometry;
using GeotechSlopeApp.Services.FieldStations;
using GeotechSlopeApp.Services.Geometry;
using GeotechSlopeApp.Services.Help;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Services;

[TestFixture]
public class Wave1ProductionGeometryTests
{
    [Test]
    public void SnapEngine_snaps_to_vertex_when_in_tolerance()
    {
        var vertices = new List<Point> { new(0, 0), new(10, 0), new(10, 10) };
        var settings = new GeometrySnapSettings
        {
            SnapEnabled = true,
            GridSnap = false,
            VertexSnap = true,
            MidpointSnap = false,
            IntersectionSnap = false,
            EdgeSnap = false
        };

        var snap = GeometrySnapEngine.Snap(new Point(0.2, 0.1), vertices, [], settings, 0.5);
        Assert.That(snap.Kind, Is.EqualTo(GeometrySnapKind.Vertex));
        Assert.That(snap.Point.X, Is.EqualTo(0).Within(1e-9));
        Assert.That(snap.Point.Y, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void HealService_merges_near_coincident_vertices()
    {
        var healed = GeometryHealService.HealVertices(
            [(1, 0.0, 0.0), (2, 0.01, 0.0), (3, 10.0, 0.0)],
            0.05);

        Assert.That(healed, Has.Count.EqualTo(3));
        Assert.That(healed[0].X, Is.EqualTo(healed[1].X).Within(1e-9));
        Assert.That(healed[0].Y, Is.EqualTo(healed[1].Y).Within(1e-9));
        Assert.That(healed[2].X, Is.EqualTo(10).Within(1e-9));
    }

    [Test]
    public void CoordinateSystem_round_trips_local_and_map()
    {
        var crs = new ProjectCoordinateSystem
        {
            OriginEasting = 500_000,
            OriginNorthing = 6_000_000,
            RotationDeg = 15,
            Scale = 1
        };

        var (e, n) = crs.ToMap(12, 8);
        var (x, y) = crs.FromMap(e, n);
        Assert.That(x, Is.EqualTo(12).Within(1e-9));
        Assert.That(y, Is.EqualTo(8).Within(1e-9));
    }

    [Test]
    public void SoilBlock_pads_borehole_extents()
    {
        var stations = new[]
        {
            new FieldStation
            {
                Id = "BH1",
                X = 10,
                CollarY = 20,
                Intervals = { new FieldUnitInterval { TopY = 20, BottomY = 5, UnitCode = "CLAY" } }
            },
            new FieldStation
            {
                Id = "BH2",
                X = 30,
                CollarY = 18,
                Intervals = { new FieldUnitInterval { TopY = 18, BottomY = 4, UnitCode = "SAND" } }
            }
        };

        Assert.That(FieldStationSoilBlock.TryComputeBounds(stations, 5, out var block), Is.True);
        Assert.That(block.MinX, Is.EqualTo(5).Within(1e-9));
        Assert.That(block.MaxX, Is.EqualTo(35).Within(1e-9));
        Assert.That(block.MinY, Is.EqualTo(-1).Within(1e-9));
        Assert.That(block.MaxY, Is.EqualTo(25).Within(1e-9));
    }

    [Test]
    public void SlopeImage_axis_aligned_bounds_are_valid()
    {
        var underlay = new SlopeImageUnderlay
        {
            Mode = SlopeImageCalibrationMode.AxisAlignedBounds,
            ImagePixelWidth = 200,
            ImagePixelHeight = 100,
            WorldMinX = 0,
            WorldMinY = 0,
            WorldMaxX = 40,
            WorldMaxY = 20
        };

        Assert.That(underlay.TryGetWorldBounds(out var minX, out var minY, out var maxX, out var maxY), Is.True);
        Assert.That(minX, Is.EqualTo(0).Within(1e-9));
        Assert.That(minY, Is.EqualTo(0).Within(1e-9));
        Assert.That(maxX, Is.EqualTo(40).Within(1e-9));
        Assert.That(maxY, Is.EqualTo(20).Within(1e-9));
    }

    [Test]
    public void CadEdit_moves_selected_vertices()
    {
        var moved = GeometryCadEditService.Move(
            [(1, 0, 0), (2, 4, 0), (3, 4, 3)],
            new[] { 2, 3 },
            1,
            2);

        Assert.That(moved.Single(v => v.StableId == 1).X, Is.EqualTo(0).Within(1e-9));
        Assert.That(moved.Single(v => v.StableId == 2).X, Is.EqualTo(5).Within(1e-9));
        Assert.That(moved.Single(v => v.StableId == 2).Y, Is.EqualTo(2).Within(1e-9));
        Assert.That(moved.Single(v => v.StableId == 3).Y, Is.EqualTo(5).Within(1e-9));
    }

    [Test]
    public void HelpToc_is_available_and_lists_wave1_topics()
    {
        var toc = HelpTableOfContents.Load();
        Assert.That(toc.IsAvailable, Is.True, "Help/toc.json should ship beside the app or source tree.");
        var ids = toc.Flatten().Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.That(ids, Does.Contain("index"));
        Assert.That(ids, Does.Contain("quick-start"));
        Assert.That(ids, Does.Contain("production-geometry"));
        Assert.That(ids, Does.Contain("slope-image"));
        Assert.That(ids, Does.Contain("geometry-import"));

        var missing = toc.Flatten()
            .Where(t => t.HasContent && !File.Exists(toc.AbsolutePathOf(t)))
            .Select(t => t.Id)
            .ToList();
        Assert.That(missing, Is.Empty, "TOC entries without files: " + string.Join(", ", missing));
    }
}
