using GeotechSlopeApp.Services.Geometry;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Services;

[TestFixture]
public class FeJointPlacementServiceTests
{
    private static readonly (double X, double Y)[] Bench2H1V =
    {
        (0, 20), (10, 20), (20, 10), (40, 10)
    };

    private static readonly (double X, double Y)[] ClosedDomain =
    {
        (0, 0), (40, 0), (40, 20), (0, 20)
    };

    [Test]
    public void CrestToe_ConnectsPlateauEdgeToSlopeBreak()
    {
        Assert.That(FeJointPlacementService.TryFindCrest(Bench2H1V, out var crest), Is.True);
        Assert.That(crest.X, Is.EqualTo(10).Within(1e-9));
        Assert.That(crest.Y, Is.EqualTo(20).Within(1e-9));
        Assert.That(FeJointPlacementService.TryFindToe(Bench2H1V, crest, out var toe), Is.True);
        Assert.That(toe.X, Is.EqualTo(20).Within(1e-9));
        Assert.That(toe.Y, Is.EqualTo(10).Within(1e-9));

        var placed = FeJointPlacementService.Place(BaseRequest(FeJointPlacementMode.CrestToe));
        Assert.That(placed, Has.Count.EqualTo(1));
        Assert.That(placed[0].Vertices[0], Is.EqualTo(crest));
        Assert.That(placed[0].Vertices[^1], Is.EqualTo(toe));
    }

    [Test]
    public void LayerContact_ClipsHorizontalCutToPolygon()
    {
        var request = BaseRequest(FeJointPlacementMode.LayerContact) with
        {
            DomainPolygon = ClosedDomain,
            LayerCutElevations = new[] { 10.0 },
            LayerIndex = 0
        };

        var placed = FeJointPlacementService.Place(request);
        Assert.That(placed, Has.Count.EqualTo(1));
        Assert.That(placed[0].Vertices[0].Y, Is.EqualTo(10).Within(1e-6));
        Assert.That(placed[0].Vertices[1].Y, Is.EqualTo(10).Within(1e-6));
        var xs = placed[0].Vertices.Select(v => v.X).OrderBy(x => x).ToArray();
        Assert.That(xs[0], Is.EqualTo(0).Within(1e-4));
        Assert.That(xs[1], Is.EqualTo(40).Within(1e-4));
    }

    [Test]
    public void ThroughPoint_ClipsToAxisBox()
    {
        var request = BaseRequest(FeJointPlacementMode.ThroughPoint) with
        {
            DomainPolygon = null,
            AxisXMin = 0,
            AxisXMax = 10,
            AxisYMin = 0,
            AxisYMax = 10,
            ThroughX = 5,
            ThroughY = 5,
            DipAngleDeg = 0,
            UseDipOverride = true
        };

        var placed = FeJointPlacementService.Place(request);
        Assert.That(placed, Has.Count.EqualTo(1));
        var ys = placed[0].Vertices.Select(v => v.Y).ToArray();
        Assert.That(ys, Has.All.EqualTo(5).Within(1e-6));
        var xs = placed[0].Vertices.Select(v => v.X).OrderBy(x => x).ToArray();
        Assert.That(xs[0], Is.EqualTo(0).Within(1e-6));
        Assert.That(xs[^1], Is.EqualTo(10).Within(1e-6));
    }

    [Test]
    public void ParallelSet_ReturnsRequestedCount()
    {
        var request = BaseRequest(FeJointPlacementMode.ParallelSet) with
        {
            DomainPolygon = ClosedDomain,
            ThroughX = 20,
            ThroughY = 10,
            DipAngleDeg = 0,
            UseDipOverride = true,
            ParallelCount = 3,
            ParallelSpacing = 2
        };

        var placed = FeJointPlacementService.Place(request);
        Assert.That(placed, Has.Count.EqualTo(3));
        foreach (var poly in placed)
            Assert.That(poly.Vertices, Has.Count.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void TensionCrack_DropsFromCrestByDepth()
    {
        var request = BaseRequest(FeJointPlacementMode.TensionCrack) with
        {
            TensionCrackDepth = 2,
            UseDipOverride = false
        };

        var placed = FeJointPlacementService.Place(request);
        Assert.That(placed, Has.Count.EqualTo(1));
        Assert.That(placed[0].Vertices[0].X, Is.EqualTo(10).Within(1e-6));
        Assert.That(placed[0].Vertices[0].Y, Is.EqualTo(20).Within(1e-6));
        Assert.That(placed[0].Vertices[1].X, Is.EqualTo(10).Within(1e-6));
        Assert.That(placed[0].Vertices[1].Y, Is.EqualTo(18).Within(1e-6));
    }

    [Test]
    public void Suggest_IncludesCrestToeTensionAndLayers()
    {
        var suggestions = FeJointPlacementService.Suggest(Bench2H1V, new[] { 12.0 });
        Assert.That(suggestions.Select(s => s.Mode), Does.Contain(FeJointPlacementMode.CrestToe));
        Assert.That(suggestions.Select(s => s.Mode), Does.Contain(FeJointPlacementMode.TensionCrack));
        Assert.That(suggestions.Count(s => s.Mode == FeJointPlacementMode.LayerContact), Is.EqualTo(1));
    }

    private static FeJointPlacementRequest BaseRequest(FeJointPlacementMode mode) => new(
        mode,
        Bench2H1V,
        ClosedDomain,
        Array.Empty<double>(),
        0, 40, 0, 20,
        -45,
        false,
        15, 15,
        2,
        3,
        2);
}
