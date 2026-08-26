using GeotechSlopeApp.Backend;

const double tol = 1e-5;

static bool PointInQuad(double px, double py, IReadOnlyList<(double x, double y)> corners)
{
    if (corners.Count < 3) return false;

    var unique = new List<(double x, double y)>();
    foreach (var c in corners)
    {
        if (unique.Count == 0 || Math.Abs(unique[^1].x - c.x) > 1e-9 || Math.Abs(unique[^1].y - c.y) > 1e-9)
            unique.Add(c);
    }
    if (unique.Count >= 2 &&
        Math.Abs(unique[0].x - unique[^1].x) < 1e-9 &&
        Math.Abs(unique[0].y - unique[^1].y) < 1e-9)
        unique.RemoveAt(unique.Count - 1);

    if (unique.Count == 3)
        return PointInTriangle(px, py, unique[0], unique[1], unique[2]);

    int n = unique.Count;
    if (n < 3) return false;
    bool inside = false;
    for (int i = 0, j = n - 1; i < n; j = i++)
    {
        double xi = unique[i].x, yi = unique[i].y;
        double xj = unique[j].x, yj = unique[j].y;
        if (Math.Abs(yj - yi) < 1e-12) continue;
        if ((yi > py) != (yj > py))
        {
            double xInt = (xj - xi) * (py - yi) / (yj - yi) + xi;
            if (px < xInt) inside = !inside;
        }
    }
    return inside;
}

static bool PointInTriangle(double px, double py, (double x, double y) a, (double x, double y) b, (double x, double y) c)
{
    static double Cross((double x, double y) p, (double x, double y) q, (double x, double y) r)
        => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
    double s1 = Cross(a, b, (px, py));
    double s2 = Cross(b, c, (px, py));
    double s3 = Cross(c, a, (px, py));
    bool hasNeg = s1 < -1e-12 || s2 < -1e-12 || s3 < -1e-12;
    bool hasPos = s1 > 1e-12 || s2 > 1e-12 || s3 > 1e-12;
    return !(hasNeg && hasPos);
}

static bool IsPointCovered(double px, double py, P64EmbankmentMeshBuilder.MeshResult mesh)
{
    var nodeMap = mesh.Nodes.ToDictionary(n => n.id, n => (n.x, n.y));
    foreach (var (_, conn, _) in mesh.Elements)
    {
        if (conn.Length < 8) continue;
        var corners = new List<(double x, double y)>
        {
            nodeMap[conn[0]], nodeMap[conn[1]], nodeMap[conn[2]], nodeMap[conn[3]],
        };
        if (PointInQuad(px, py, corners))
            return true;
    }
    return false;
}

static bool IsNearPolygonBoundary(
    double px, double py,
    IReadOnlyList<(double x, double y)> polygon,
    double margin)
{
    int n = polygon.Count;
    for (int i = 0; i < n; i++)
    {
        var a = polygon[i];
        var b = polygon[(i + 1) % n];
        double dx = b.x - a.x;
        double dy = b.y - a.y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-18) continue;
        double t = ((px - a.x) * dx + (py - a.y) * dy) / lenSq;
        t = Math.Clamp(t, 0, 1);
        double cx = a.x + t * dx;
        double cy = a.y + t * dy;
        double distSq = (px - cx) * (px - cx) + (py - cy) * (py - cy);
        if (distSq <= margin * margin)
            return true;
    }
    return false;
}

static int CountUncoveredInteriorPoints(
    P64EmbankmentMeshBuilder.MeshResult mesh,
    IReadOnlyList<(double x, double y)> polygon,
    int samplesPerAxis = 40,
    double boundaryInset = 0.05)
{
    double minX = polygon.Min(p => p.x);
    double maxX = polygon.Max(p => p.x);
    double minY = polygon.Min(p => p.y);
    double maxY = polygon.Max(p => p.y);
    double dx = (maxX - minX) / samplesPerAxis;
    double dy = (maxY - minY) / samplesPerAxis;
    double x0 = minX + 0.5 * dx;
    double y0 = minY + 0.5 * dy;
    int uncovered = 0;
    int tested = 0;

    for (int ix = 0; ix < samplesPerAxis; ix++)
    {
        for (int iy = 0; iy < samplesPerAxis; iy++)
        {
            double px = x0 + ix * dx;
            double py = y0 + iy * dy;
            if (!PolygonElementMatcher.PointInPolygon(px, py, polygon))
                continue;
            if (IsNearPolygonBoundary(px, py, polygon, boundaryInset))
                continue;
            tested++;
            if (!IsPointCovered(px, py, mesh))
                uncovered++;
        }
    }

    Console.WriteLine($"  Interior sample points tested: {tested}, uncovered: {uncovered}");
    return uncovered;
}

static int CountUniqueCorners(IReadOnlyList<(double x, double y)> corners)
{
    return corners
        .Select(p => ((long)Math.Round(p.x / tol), (long)Math.Round(p.y / tol)))
        .Distinct()
        .Count();
}

static bool IsAxisAlignedRectangle(IReadOnlyList<(double x, double y)> c)
{
    if (c.Count < 4) return false;
    bool bottomFlat = Math.Abs(c[0].y - c[1].y) <= tol;
    bool topFlat = Math.Abs(c[3].y - c[2].y) <= tol;
    bool leftVertical = Math.Abs(c[0].x - c[3].x) <= tol;
    bool rightVertical = Math.Abs(c[1].x - c[2].x) <= tol;
    return bottomFlat && topFlat && leftVertical && rightVertical;
}

static bool IsPointOnSegment(
    (double x, double y) p,
    (double x, double y) segA,
    (double x, double y) segB,
    double margin)
{
    double dx = segB.x - segA.x;
    double dy = segB.y - segA.y;
    double lenSq = dx * dx + dy * dy;
    if (lenSq < 1e-18)
        return Math.Abs(p.x - segA.x) <= margin && Math.Abs(p.y - segA.y) <= margin;

    double t = ((p.x - segA.x) * dx + (p.y - segA.y) * dy) / lenSq;
    if (t < -margin || t > 1 + margin)
        return false;

    double cx = segA.x + t * dx;
    double cy = segA.y + t * dy;
    return (p.x - cx) * (p.x - cx) + (p.y - cy) * (p.y - cy) <= margin * margin;
}

static bool EdgeLiesOnSegment(
    (double x, double y) a,
    (double x, double y) b,
    (double x, double y) segA,
    (double x, double y) segB,
    double margin)
    => IsPointOnSegment(a, segA, segB, margin) && IsPointOnSegment(b, segA, segB, margin);

static bool AssertMeshInsidePolygon(
    P64EmbankmentMeshBuilder.MeshResult mesh,
    IReadOnlyList<(double x, double y)> polygon,
    ref int failures)
{
    if (StructuredQuadPolygonClipper.ValidateMeshInsidePolygon(mesh, polygon, out var error))
        return true;

    Console.WriteLine($"FAIL: {error}");
    failures++;
    return false;
}

static bool EdgeLiesOnPolygonEdge(
    (double x, double y) a,
    (double x, double y) b,
    IReadOnlyList<(double x, double y)> polygon,
    double margin)
{
    for (int i = 0; i < polygon.Count; i++)
    {
        var segA = polygon[i];
        var segB = polygon[(i + 1) % polygon.Count];
        if (EdgeLiesOnSegment(a, b, segA, segB, margin))
            return true;
    }
    return false;
}

static bool AssertTrapeziumBoundaryElements(
    P64EmbankmentMeshBuilder.MeshResult mesh,
    IReadOnlyList<(double x, double y)> polygon,
    int expectedBoundaryFill,
    ref int failures)
{
    var nodeMap = mesh.Nodes.ToDictionary(n => n.id, n => (n.x, n.y));
    int boundaryLike = 0;
    int boundaryAligned = 0;

    foreach (var (id, conn, _) in mesh.Elements)
    {
        if (conn.Length < 8) continue;
        var corners = new List<(double x, double y)>
        {
            nodeMap[conn[0]], nodeMap[conn[1]], nodeMap[conn[2]], nodeMap[conn[3]],
        };

        if (CountUniqueCorners(corners) < 3)
        {
            Console.WriteLine($"FAIL: element {id} is not a valid boundary element.");
            failures++;
            continue;
        }

        // Non-rectangular elements are slope/boundary trapeziums or clip fill.
        if (IsAxisAlignedRectangle(corners))
            continue;

        boundaryLike++;
        if (EdgeLiesOnPolygonEdge(corners[0], corners[1], polygon, tol * 10)
            || EdgeLiesOnPolygonEdge(corners[1], corners[2], polygon, tol * 10)
            || EdgeLiesOnPolygonEdge(corners[2], corners[3], polygon, tol * 10)
            || EdgeLiesOnPolygonEdge(corners[3], corners[0], polygon, tol * 10))
            boundaryAligned++;
    }

    Console.WriteLine($"  Non-rectangular (boundary-like) elements: {boundaryLike}, boundary-aligned: {boundaryAligned}");
    if (expectedBoundaryFill > 0 && boundaryLike <= 0)
    {
        Console.WriteLine("FAIL: expected trapezium boundary elements along slope.");
        failures++;
    }
    if (expectedBoundaryFill > 0 && boundaryAligned <= 0)
    {
        Console.WriteLine("FAIL: no boundary element edge aligned with polygon boundary.");
        failures++;
    }
    return failures == 0;
}

int failures = 0;

// Regression: axis-aligned rectangle should not need boundary fill.
{
    var rect = new List<(double x, double y)> { (0, 0), (10, 0), (10, 6), (0, 6) };
    var stats = StructuredRectangularQ8MeshBuilder.BuildWithStats(
        0, 10, 0, 6, 5, 3,
        new StructuredRectangularQ8MeshBuilder.BuildOptions { BoundaryPolygon = rect });
    Console.WriteLine("Rectangle clip:");
    Console.WriteLine($"  elements={stats.Mesh.Nels}, boundaryFill={stats.BoundaryFillElementCount}");
    AssertMeshInsidePolygon(stats.Mesh, rect, ref failures);
    if (stats.BoundaryFillElementCount != 0)
    {
        Console.WriteLine("FAIL: expected zero boundary fill for axis-aligned rectangle.");
        failures++;
    }
    if (stats.Mesh.Nels != 15)
    {
        Console.WriteLine($"FAIL: expected 15 elements, got {stats.Mesh.Nels}.");
        failures++;
    }
}

// Stepped slope with diagonal face — boundary fill should close slope gaps.
{
    var stepped = new List<(double x, double y)>
    {
        (0, 0), (10, 0), (10, 3), (7, 6), (0, 6),
    };
    var stats = StructuredRectangularQ8MeshBuilder.BuildWithStats(
        0, 10, 0, 6, 8, 4,
        new StructuredRectangularQ8MeshBuilder.BuildOptions { BoundaryPolygon = stepped });
    Console.WriteLine("Stepped/diagonal slope clip:");
    Console.WriteLine($"  elements={stats.Mesh.Nels}, boundaryFill={stats.BoundaryFillElementCount}");
    AssertMeshInsidePolygon(stats.Mesh, stepped, ref failures);
    if (stats.BoundaryFillElementCount <= 0)
    {
        Console.WriteLine("FAIL: expected boundary fill elements along diagonal slope.");
        failures++;
    }

    AssertMeshInsidePolygon(stats.Mesh, stepped, ref failures);
    AssertTrapeziumBoundaryElements(stats.Mesh, stepped, stats.BoundaryFillElementCount, ref failures);

    if (CountUncoveredInteriorPoints(stats.Mesh, stepped) > 0)
    {
        Console.WriteLine("FAIL: interior polygon still has uncovered sample points.");
        failures++;
    }

    if (!IsPointCovered(8.5, 4.0, stats.Mesh))
    {
        Console.WriteLine("FAIL: sample point (8.5, 4.0) near slope should be covered.");
        failures++;
    }
}

if (failures == 0)
{
    Console.WriteLine("StructuredMeshClipSmokeTest: PASS");
    return 0;
}

Console.WriteLine($"StructuredMeshClipSmokeTest: FAIL ({failures} check(s))");
return 1;
