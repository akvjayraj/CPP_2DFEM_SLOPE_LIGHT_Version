using netDxf;
using netDxf.Entities;

namespace GeotechSlopeApp.Services.GeometryImport;

public static class DxfGeometryImporter
{
    public static GeometryImportParseResult ImportWithDiagnostics(string filePath, GeometryImportOptions options)
    {
        var dxf = DxfDocument.Load(filePath);
        var inventory = DxfEntityInventory.FromDocument(dxf);
        var document = ImportFromDocument(dxf, options);
        return new GeometryImportParseResult
        {
            Document = document,
            DxfDiagnostics = new DxfImportDiagnostics { RawInventory = inventory }
        };
    }

    public static GeometryImportDocument Import(string filePath, GeometryImportOptions options) =>
        ImportWithDiagnostics(filePath, options).Document;

    private static GeometryImportDocument ImportFromDocument(DxfDocument dxf, GeometryImportOptions options)
    {
        var doc = new GeometryImportDocument();
        var registry = new GeometryPointRegistry(options.MergeTolerance);
        var precision = Math.Max(4, options.ArcSegmentCount);

        ImportFlattenedEntities(DxfGeometrySource.FlattenEntities(DxfGeometrySource.CollectLayoutEntities(dxf)), doc, registry, options, precision);
        ImportFlattenedEntities(DxfGeometrySource.FlattenEntities(DxfGeometrySource.CollectBlockDefinitionEntities(dxf)), doc, registry, options, precision);

        if (options.ImportSurfaces)
            PromoteClosedPolygonsToSurfaces(doc, registry);

        AddRegistryPointsToDocument(doc, registry);
        return doc;
    }

    private static void ImportFlattenedEntities(
        IEnumerable<EntityObject> entities,
        GeometryImportDocument doc,
        GeometryPointRegistry registry,
        GeometryImportOptions options,
        int precision)
    {
        foreach (var entity in entities)
            ImportEntity(entity, doc, registry, options, precision);
    }

    private static void PromoteClosedPolygonsToSurfaces(GeometryImportDocument doc, GeometryPointRegistry registry)
    {
        var surfaceNumber = doc.Surfaces.Count > 0
            ? doc.Surfaces.Max(s => s.Number) + 1
            : 1;

        foreach (var poly in doc.ClosedPolygons)
        {
            var ids = new List<int>();
            foreach (var (x, y) in poly.Vertices)
                ids.Add(registry.GetOrAdd(x, y));

            if (ids.Count < 3)
                continue;

            if (doc.Surfaces.Any(s => s.PointIds.SequenceEqual(ids)))
                continue;

            doc.Surfaces.Add(new GeometryImportSurface
            {
                Number = surfaceNumber++,
                Name = poly.Name ?? $"Surface {surfaceNumber - 1}",
                PointIds = ids
            });
        }
    }

    private static void AddRegistryPointsToDocument(GeometryImportDocument doc, GeometryPointRegistry registry)
    {
        foreach (var (id, x, y) in registry.AllPoints)
        {
            if (doc.Points.Any(p => p.Id == id))
                continue;
            doc.Points.Add(new GeometryImportPoint { Id = id, X = x, Y = y });
        }
    }

    private static void ImportEntity(
        EntityObject entity,
        GeometryImportDocument doc,
        GeometryPointRegistry registry,
        GeometryImportOptions options,
        int precision)
    {
        switch (entity)
        {
            case Point pt when options.ImportPoints:
                var id = registry.GetOrAdd(pt.Position.X, pt.Position.Y);
                doc.Points.Add(new GeometryImportPoint { Id = id, X = pt.Position.X, Y = pt.Position.Y });
                break;

            case Line line when options.ImportLines:
            {
                var a = registry.GetOrAdd(line.StartPoint.X, line.StartPoint.Y);
                var b = registry.GetOrAdd(line.EndPoint.X, line.EndPoint.Y);
                doc.Lines.Add(new GeometryImportLine { FromId = a, ToId = b, Kind = "internal" });
                break;
            }

            case Polyline2D poly when options.ImportLines || options.ImportSurfaces:
                ImportPolyline2D(poly, doc, registry, options);
                break;

            case Polyline3D poly3d when options.ImportLines || options.ImportSurfaces:
                ImportPolyline3D(poly3d, doc, registry, options);
                break;

            case Arc arc when options.ImportLines:
                ImportArc(arc, doc, registry);
                break;

            case Circle circle when options.ImportLines:
                doc.Circles.Add(new GeometryImportCircle
                {
                    CenterX = circle.Center.X,
                    CenterY = circle.Center.Y,
                    Radius = circle.Radius
                });
                break;

            case Spline spline when options.ImportLines || options.ImportSurfaces:
                ImportPolyline2D(spline.ToPolyline2D(precision), doc, registry, options);
                break;

            case Ellipse ellipse when options.ImportLines || options.ImportSurfaces:
                ImportPolyline2D(ellipse.ToPolyline2D(precision), doc, registry, options);
                break;

            case Hatch hatch when options.ImportLines || options.ImportSurfaces:
                ImportHatch(hatch, doc, registry, options);
                break;

            case Solid solid when options.ImportLines:
                ImportSolid(solid, doc, registry);
                break;

            case Trace trace when options.ImportLines:
                ImportTrace(trace, doc, registry);
                break;
        }
    }

    private static void ImportSolid(Solid solid, GeometryImportDocument doc, GeometryPointRegistry registry)
    {
        AddLine(doc, registry, solid.FirstVertex.X, solid.FirstVertex.Y, solid.SecondVertex.X, solid.SecondVertex.Y);
        AddLine(doc, registry, solid.SecondVertex.X, solid.SecondVertex.Y, solid.ThirdVertex.X, solid.ThirdVertex.Y);
        AddLine(doc, registry, solid.ThirdVertex.X, solid.ThirdVertex.Y, solid.FourthVertex.X, solid.FourthVertex.Y);
        AddLine(doc, registry, solid.FourthVertex.X, solid.FourthVertex.Y, solid.FirstVertex.X, solid.FirstVertex.Y);
    }

    private static void ImportTrace(Trace trace, GeometryImportDocument doc, GeometryPointRegistry registry)
    {
        AddLine(doc, registry, trace.FirstVertex.X, trace.FirstVertex.Y, trace.SecondVertex.X, trace.SecondVertex.Y);
        AddLine(doc, registry, trace.SecondVertex.X, trace.SecondVertex.Y, trace.ThirdVertex.X, trace.ThirdVertex.Y);
        AddLine(doc, registry, trace.ThirdVertex.X, trace.ThirdVertex.Y, trace.FourthVertex.X, trace.FourthVertex.Y);
        AddLine(doc, registry, trace.FourthVertex.X, trace.FourthVertex.Y, trace.FirstVertex.X, trace.FirstVertex.Y);
    }

    private static void AddLine(GeometryImportDocument doc, GeometryPointRegistry registry, double x1, double y1, double x2, double y2)
    {
        var a = registry.GetOrAdd(x1, y1);
        var b = registry.GetOrAdd(x2, y2);
        doc.Lines.Add(new GeometryImportLine { FromId = a, ToId = b, Kind = "internal" });
    }

    private static void ImportHatch(Hatch hatch, GeometryImportDocument doc, GeometryPointRegistry registry, GeometryImportOptions options)
    {
        foreach (var boundary in hatch.BoundaryPaths)
        {
            foreach (var boundaryEntity in boundary.Entities)
                ImportEntity(boundaryEntity, doc, registry, options, Math.Max(4, options.ArcSegmentCount));

            if (boundary.Edges is { Count: > 0 })
            {
                var vertices = new List<(double X, double Y)>();
                foreach (var edge in boundary.Edges)
                {
                    switch (edge)
                    {
                        case HatchBoundaryPath.Line line:
                            if (vertices.Count == 0)
                                vertices.Add((line.Start.X, line.Start.Y));
                            vertices.Add((line.End.X, line.End.Y));
                            break;
                        case HatchBoundaryPath.Arc arc:
                            SampleArcEdge(arc, vertices, Math.Max(4, options.ArcSegmentCount));
                            break;
                    }
                }

                if (vertices.Count >= 2)
                {
                    var isClosed = boundary.PathType.HasFlag(HatchBoundaryPathTypeFlags.External)
                        || boundary.PathType.HasFlag(HatchBoundaryPathTypeFlags.Outermost);
                    ImportVertexChain(vertices, isClosed || vertices.Count >= 3, doc, registry, options, hatch.Layer?.Name);
                }
            }
        }
    }

    private static void SampleArcEdge(HatchBoundaryPath.Arc arc, List<(double X, double Y)> vertices, int segments)
    {
        var start = arc.StartAngle * Math.PI / 180.0;
        var end = arc.EndAngle * Math.PI / 180.0;
        if (vertices.Count == 0)
        {
            vertices.Add((
                arc.Center.X + arc.Radius * Math.Cos(start),
                arc.Center.Y + arc.Radius * Math.Sin(start)));
        }

        var sweep = end - start;
        if (arc.IsCounterclockwise && sweep < 0) sweep += 2 * Math.PI;
        if (!arc.IsCounterclockwise && sweep > 0) sweep -= 2 * Math.PI;

        for (var i = 1; i <= segments; i++)
        {
            var t = start + sweep * i / segments;
            vertices.Add((arc.Center.X + arc.Radius * Math.Cos(t), arc.Center.Y + arc.Radius * Math.Sin(t)));
        }
    }

    private static void ImportPolyline2D(Polyline2D poly, GeometryImportDocument doc, GeometryPointRegistry registry, GeometryImportOptions options)
    {
        var vertices = poly.Vertexes.Select(v => (v.Position.X, v.Position.Y)).ToList();
        ImportVertexChain(vertices, poly.IsClosed, doc, registry, options, poly.Layer?.Name);
    }

    private static void ImportPolyline3D(Polyline3D poly, GeometryImportDocument doc, GeometryPointRegistry registry, GeometryImportOptions options)
    {
        var vertices = poly.Vertexes.Select(v => (v.X, v.Y)).ToList();
        ImportVertexChain(vertices, poly.IsClosed, doc, registry, options, poly.Layer?.Name);
    }

    private static void ImportVertexChain(
        IReadOnlyList<(double X, double Y)> vertices,
        bool isClosed,
        GeometryImportDocument doc,
        GeometryPointRegistry registry,
        GeometryImportOptions options,
        string? layerName)
    {
        if (vertices.Count < 2)
            return;

        if (isClosed && options.ImportSurfaces && vertices.Count >= 3)
        {
            doc.ClosedPolygons.Add(new GeometryImportClosedPolygon
            {
                Name = layerName,
                Vertices = vertices.Select(v => (v.X, v.Y)).ToList()
            });
        }

        if (!options.ImportLines)
            return;

        var ids = vertices.Select(v => registry.GetOrAdd(v.X, v.Y)).ToList();
        var limit = isClosed ? ids.Count : ids.Count - 1;
        for (var i = 0; i < limit; i++)
        {
            var from = ids[i];
            var to = ids[(i + 1) % ids.Count];
            if (from == to)
                continue;
            doc.Lines.Add(new GeometryImportLine { FromId = from, ToId = to, Kind = isClosed ? "boundary" : "internal" });
        }

        if (!isClosed)
            doc.OpenPolylines.Add(vertices.Select(v => (v.X, v.Y)).ToList());
    }

    private static void ImportArc(Arc arc, GeometryImportDocument doc, GeometryPointRegistry registry)
    {
        var startAngle = arc.StartAngle * Math.PI / 180.0;
        var endAngle = arc.EndAngle * Math.PI / 180.0;
        var cx = arc.Center.X;
        var cy = arc.Center.Y;
        var r = arc.Radius;

        var sx = cx + r * Math.Cos(startAngle);
        var sy = cy + r * Math.Sin(startAngle);
        var ex = cx + r * Math.Cos(endAngle);
        var ey = cy + r * Math.Sin(endAngle);

        var midAngle = startAngle + ((endAngle - startAngle + 2 * Math.PI) % (2 * Math.PI)) / 2.0;
        var mx = cx + r * Math.Cos(midAngle);
        var my = cy + r * Math.Sin(midAngle);

        doc.Arcs.Add(new GeometryImportArc
        {
            StartX = sx,
            StartY = sy,
            MidX = mx,
            MidY = my,
            EndX = ex,
            EndY = ey
        });

        registry.GetOrAdd(sx, sy);
        registry.GetOrAdd(mx, my);
        registry.GetOrAdd(ex, ey);
    }
}
