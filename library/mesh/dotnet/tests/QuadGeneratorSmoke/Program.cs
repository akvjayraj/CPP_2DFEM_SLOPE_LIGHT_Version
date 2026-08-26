using System.Windows;
using GeotechSlopeApp.Backend;

// Ex2 two-layer slope outer boundary (axis units from project_multilayer_1.xfem).
var ex2Polygon = new List<Point>
{
    new(0, 2), new(0, 1), new(0, 0.4), new(0, 0),
    new(4.4, 0), new(4.4, 0.4), new(4.4, 1), new(3.2, 1), new(1.2, 2),
};

double globalSize = 0.5;
double nearSize = 0.1;
var regions = new List<(int, IReadOnlyList<Point>, double, double, int, double)>
{
    (1, ex2Polygon, nearSize, globalSize, 3, 1.3),
};

var results = GradedSurfaceQuadGenerator.GenerateConforming(regions, holeOnlyRegions: null, materialLayerSegments: null);
if (results.Count == 0 || results.All(r => r.Mesh.ElementCount == 0))
{
    Console.Error.WriteLine("FAIL: native Q4 generator produced no elements for Ex2 polygon.");
    return 1;
}

int totalQuads = results.Sum(r => r.Mesh.ElementCount);
int totalNodes = results[0].Mesh.NodeCount;
int maxDepth = results.Max(r => r.Mesh.MaxRefineDepth);
Console.WriteLine($"Ex2 native Q4: {totalQuads} quad elements, {totalNodes} nodes, max refine depth {maxDepth}.");

foreach (var (_, mesh) in results)
{
    var nodes = mesh.Nodes.ToList();
    var quads = mesh.Quads.ToList();

    if (GradedSurfaceQuadGenerator.AnyCornerOutsidePolygon(nodes, quads, ex2Polygon, nearSize))
    {
        Console.Error.WriteLine("FAIL: quad corner lies outside Ex2 polygon.");
        return 1;
    }

    double maxGap = GradedSurfaceQuadGenerator.MaxExteriorBoundaryGap(nodes, ex2Polygon, nearSize);
    Console.WriteLine($"  Max boundary-to-mesh gap: {maxGap:F4} (nearSize={nearSize})");
    if (maxGap > nearSize * 1.05)
    {
        Console.Error.WriteLine($"FAIL: mesh gap {maxGap:F4} exceeds near-boundary size {nearSize}.");
        return 1;
    }

    foreach (var (a, b, c, d) in quads)
    {
        var corners = new[] { a, b, c, d };
        if (corners.Distinct().Count() != 4)
        {
            Console.Error.WriteLine("FAIL: degenerate quad in export connectivity.");
            return 1;
        }
    }
}

Console.WriteLine("PASS: Ex2 mesh touches boundary (gap <= nearSize) with all corners inside/on polygon.");

// Embankment-style polygon (reference mesh: bench + two slope segments, layers at y=3 and y=5).
var embankment = new List<Point>
{
    new(0, 0), new(20, 0), new(20, 10), new(9, 10), new(6, 5), new(5, 3), new(0, 3),
};
var embRegions = new List<(int, IReadOnlyList<Point>, double, double, int, double)>
{
    (1, embankment, 0.5, 1.0, 3, 1.3),
};
var embResults = GradedSurfaceQuadGenerator.GenerateConforming(embRegions, holeOnlyRegions: null, materialLayerSegments: null);
if (embResults.Count == 0 || embResults.All(r => r.Mesh.ElementCount == 0))
{
    Console.Error.WriteLine("FAIL: embankment Q4 generator produced no elements.");
    return 1;
}
var embMesh = embResults[0].Mesh;
if (GradedSurfaceQuadGenerator.AnyCornerOutsidePolygon(embMesh.Nodes.ToList(), embMesh.Quads.ToList(), embankment, 0.5))
{
    Console.Error.WriteLine("FAIL: embankment quad corner outside polygon.");
    return 1;
}
double embGap = GradedSurfaceQuadGenerator.MaxExteriorBoundaryGap(embMesh.Nodes.ToList(), embankment, 0.5);
Console.WriteLine($"Embankment Q4: {embMesh.ElementCount} quads, max boundary gap {embGap:F4}, refine depth {embMesh.MaxRefineDepth}.");
if (embGap > 0.5 * 1.05)
{
    Console.Error.WriteLine($"FAIL: embankment mesh gap {embGap:F4} exceeds near size.");
    return 1;
}
Console.WriteLine("PASS: embankment-style horizontal sweep mesh.");

// Three-layer embankment: mapped columns follow slope through all layers (reference image style).
var embLayerCuts = new List<(Point A, Point B)>
{
    (new Point(0, 3), new Point(20, 3)),
    (new Point(0, 5), new Point(20, 5)),
};
var embLayerResults = GradedSurfaceQuadGenerator.GenerateConforming(
    embRegions, holeOnlyRegions: null, materialLayerSegments: embLayerCuts);
var embLayerMesh = embLayerResults[0].Mesh;
var embLayerNodes = embLayerMesh.Nodes.ToList();
var midLayerQuads = embLayerMesh.Quads.Where(q =>
{
    double cy = (embLayerNodes[q.a].Y + embLayerNodes[q.b].Y + embLayerNodes[q.c].Y + embLayerNodes[q.d].Y) * 0.25;
    return cy > 3.05 && cy < 4.95;
}).ToList();
Console.WriteLine($"Three-layer mesh: {embLayerMesh.ElementCount} quads, refine depth {embLayerMesh.MaxRefineDepth}.");
if (midLayerQuads.Count < 20)
{
    Console.Error.WriteLine($"FAIL: middle material layer has too few quads ({midLayerQuads.Count}).");
    return 1;
}
double midGap = GradedSurfaceQuadGenerator.MaxExteriorBoundaryGap(
    embLayerNodes, embankment, 0.5, yMin: 3.0, yMax: 5.0);
Console.WriteLine($"Middle layer: {midLayerQuads.Count} quads, boundary gap {midGap:F4}.");
if (midGap > 0.5 * 1.1)
{
    Console.Error.WriteLine($"FAIL: middle layer gap {midGap:F4} too large.");
    return 1;
}
if (GradedSurfaceQuadGenerator.AnyCornerOutsidePolygon(embLayerNodes, embLayerMesh.Quads.ToList(), embankment, 0.5))
{
    Console.Error.WriteLine("FAIL: three-layer mesh has corners outside polygon.");
    return 1;
}
Console.WriteLine("PASS: three-layer mapped Q4 mesh fills all material bands.");

// Slope toe + horizontal shelf (right-side gap regression).
var shelfPoly = new List<Point>
{
    new(0, 0), new(10, 0), new(10, 3), new(7, 3), new(5, 5), new(0, 5),
};
var shelfRegions = new List<(int, IReadOnlyList<Point>, double, double, int, double)>
{
    (1, shelfPoly, 0.25, 0.5, 3, 1.3),
};
var shelfCuts = new List<(Point A, Point B)>
{
    (new Point(0, 3), new Point(10, 3)),
    (new Point(0, 5), new Point(10, 5)),
};
var shelfResults = GradedSurfaceQuadGenerator.GenerateConforming(
    shelfRegions, holeOnlyRegions: null, materialLayerSegments: shelfCuts);
var shelfMesh = shelfResults[0].Mesh;
var shelfNodes = shelfMesh.Nodes.ToList();
var lowerQuads = shelfMesh.Quads.Where(q =>
{
    double cy = (shelfNodes[q.a].Y + shelfNodes[q.b].Y + shelfNodes[q.c].Y + shelfNodes[q.d].Y) * 0.25;
    return cy > 0.05 && cy < 2.95;
}).ToList();
int wedgeQuads = shelfMesh.Quads.Count(q =>
{
    double cx = (shelfNodes[q.a].X + shelfNodes[q.b].X + shelfNodes[q.c].X + shelfNodes[q.d].X) * 0.25;
    double cy = (shelfNodes[q.a].Y + shelfNodes[q.b].Y + shelfNodes[q.c].Y + shelfNodes[q.d].Y) * 0.25;
    return cx > 6.5 && cy > 3.05 && cy < 4.95;
});
int midBandQuads = shelfMesh.Quads.Count(q =>
{
    double cy = (shelfNodes[q.a].Y + shelfNodes[q.b].Y + shelfNodes[q.c].Y + shelfNodes[q.d].Y) * 0.25;
    return cy > 3.05 && cy < 4.95;
});
double shelfGap = GradedSurfaceQuadGenerator.MaxExteriorBoundaryGap(
    shelfNodes, shelfPoly, 0.25, yMin: 2.5, yMax: 3.05);
double wedgeGap = GradedSurfaceQuadGenerator.MaxExteriorBoundaryGap(
    shelfNodes, shelfPoly, 0.25, yMin: 3.5, yMax: 4.95);
Console.WriteLine($"Shelf toe: {lowerQuads.Count} lower-band, {midBandQuads} mid-band quads, toe gap {shelfGap:F4}, shelf quads {wedgeQuads}, upper gap {wedgeGap:F4}.");
if (shelfGap > 0.25 * 1.15)
{
    Console.Error.WriteLine($"FAIL: shelf toe gap {shelfGap:F4} too large.");
    return 1;
}
if (midBandQuads < 20)
{
    Console.Error.WriteLine($"FAIL: Material 2 mid-band has only {midBandQuads} quads.");
    return 1;
}
if (wedgeQuads < 3)
{
    Console.Error.WriteLine($"FAIL: top-right shelf has only {wedgeQuads} quads.");
    return 1;
}
if (wedgeGap > 0.25 * 1.2)
{
    Console.Error.WriteLine($"FAIL: upper wedge gap {wedgeGap:F4} too large.");
    return 1;
}
Console.WriteLine("PASS: slope-toe horizontal shelf filled.");

// Interface conformity + duplicate-edge regression (M1/M2 artifacts).
var layerNodes = embLayerMesh.Nodes.ToList();
var layerQuads = embLayerMesh.Quads.ToList();
int totalDups = GradedSurfaceQuadGenerator.CountGeometricDuplicateQuads(layerNodes, layerQuads);
int cut3Dups = GradedSurfaceQuadGenerator.CountGeometricDuplicateQuadsNearCut(layerNodes, layerQuads, 3.0, 0.35);
int cut5Dups = GradedSurfaceQuadGenerator.CountGeometricDuplicateQuadsNearCut(layerNodes, layerQuads, 5.0, 0.35);
int nodesAtCut3 = GradedSurfaceQuadGenerator.CountNodesAtCutY(layerNodes, 3.0, 0.05);
int nodesAtCut5 = GradedSurfaceQuadGenerator.CountNodesAtCutY(layerNodes, 5.0, 0.05);
int alignedMid = GradedSurfaceQuadGenerator.CountAxisAlignedQuads(layerNodes, layerQuads, 3.05, 4.95);
Console.WriteLine($"Interface: cut y=3 nodes={nodesAtCut3}, cut y=5 nodes={nodesAtCut5}, mid-band axis-aligned={alignedMid}, dup quads total={totalDups}, near cut3={cut3Dups}, near cut5={cut5Dups}.");
if (totalDups > 0)
{
    Console.Error.WriteLine($"FAIL: {totalDups} geometrically duplicate quads in three-layer mesh.");
    return 1;
}
if (cut3Dups > 0 || cut5Dups > 0)
{
    Console.Error.WriteLine($"FAIL: duplicate quads at layer cuts (cut3={cut3Dups}, cut5={cut5Dups}).");
    return 1;
}
if (nodesAtCut3 < 10 || nodesAtCut5 < 10)
{
    Console.Error.WriteLine($"FAIL: interface row too sparse at cuts (y=3: {nodesAtCut3}, y=5: {nodesAtCut5}).");
    return 1;
}
if (alignedMid < midLayerQuads.Count * 0.85)
{
    Console.Error.WriteLine($"FAIL: middle band not axis-aligned enough ({alignedMid}/{midLayerQuads.Count}).");
    return 1;
}

var shelfQuads = shelfMesh.Quads.ToList();
int shelfDups = GradedSurfaceQuadGenerator.CountGeometricDuplicateQuads(shelfNodes, shelfQuads);
int shelfCutDups = GradedSurfaceQuadGenerator.CountGeometricDuplicateQuadsNearCut(shelfNodes, shelfQuads, 3.0, 0.35);
Console.WriteLine($"Shelf interface: dup quads total={shelfDups}, near cut y=3={shelfCutDups}.");
if (shelfDups > 0 || shelfCutDups > 0)
{
    Console.Error.WriteLine($"FAIL: duplicate quads in shelf toe mesh (total={shelfDups}, cut={shelfCutDups}).");
    return 1;
}
Console.WriteLine("PASS: interface rows conforming, no duplicate quads at layer cuts.");

return 0;
