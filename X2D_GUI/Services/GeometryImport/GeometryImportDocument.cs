using System.Collections.Generic;
using System.Linq;

namespace GeotechSlopeApp.Services.GeometryImport;

public sealed class GeometryImportPoint
{
    public int? Id { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
}

public sealed class GeometryImportLine
{
    public int FromId { get; init; }
    public int ToId { get; init; }
    public string Kind { get; init; } = "internal";
}

public sealed class GeometryImportArc
{
    public double StartX { get; init; }
    public double StartY { get; init; }
    public double MidX { get; init; }
    public double MidY { get; init; }
    public double EndX { get; init; }
    public double EndY { get; init; }
}

public sealed class GeometryImportCircle
{
    public double CenterX { get; init; }
    public double CenterY { get; init; }
    public double Radius { get; init; }
}

public sealed class GeometryImportSurface
{
    public int Number { get; init; } = 1;
    public string Name { get; init; } = "";
    public List<int> PointIds { get; init; } = new();
}

public sealed class GeometryImportClosedPolygon
{
    public string? Name { get; init; }
    public List<(double X, double Y)> Vertices { get; init; } = new();
}

public sealed class GeometryImportDocument
{
    public List<GeometryImportPoint> Points { get; } = new();
    public List<GeometryImportLine> Lines { get; } = new();
    public List<GeometryImportArc> Arcs { get; } = new();
    public List<GeometryImportCircle> Circles { get; } = new();
    public List<GeometryImportSurface> Surfaces { get; } = new();
    public List<GeometryImportClosedPolygon> ClosedPolygons { get; } = new();
    public List<List<(double X, double Y)>> OpenPolylines { get; } = new();
    public List<(double X, double Y)> LegacyBoundaryPoints { get; } = new();

    public bool IsEmpty =>
        Points.Count == 0
        && Lines.Count == 0
        && Arcs.Count == 0
        && Circles.Count == 0
        && Surfaces.Count == 0
        && ClosedPolygons.Count == 0
        && OpenPolylines.Count == 0
        && LegacyBoundaryPoints.Count == 0;

    public void AddOpenPolyline(IEnumerable<(double X, double Y)> vertices, string? layerName = null)
    {
        var pts = vertices.ToList();
        if (pts.Count >= 2)
            OpenPolylines.Add(pts);
    }
}

public sealed class GeometryImportOptions
{
    public bool ImportPoints { get; set; } = true;
    public bool ImportLines { get; set; } = true;
    public bool ImportSurfaces { get; set; } = true;
    public bool ReplaceExisting { get; set; } = true;
    public double MergeTolerance { get; set; } = 1e-4;
    public int ArcSegmentCount { get; set; } = 16;

    /// <summary>Optional project CRS used when importing GeoJSON map coordinates.</summary>
    public Models.ProjectCoordinateSystem? CoordinateSystem { get; set; }
}

public sealed class GeometryImportResult
{
    public GeometryImportDocument Document { get; init; } = new();
    public int PointCount { get; init; }
    public int LineCount { get; init; }
    public int SurfaceCount { get; init; }
}
