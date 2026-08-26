using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Services.GeometryImport;

public static class GeometryImportApplicator
{
    public static GeometryImportResult ApplyToXfemGeometry(
        GeometryDefinitionViewModel vm,
        GeometryImportDocument doc,
        GeometryImportOptions options)
    {
        if (options.ReplaceExisting)
            ClearGeometry(vm);

        var registry = BuildRegistry(doc, options.MergeTolerance);
        var boundaryIds = ResolveBoundaryPointIds(doc, registry);

        if (boundaryIds is { Count: >= 3 })
            PopulateBoundaryVertices(vm, registry, boundaryIds);

        AddStandalonePoints(vm, registry, boundaryIds);

        if (options.ImportLines)
        {
            AddLineEdges(vm, doc, registry, boundaryIds);
            AddOpenPolylineEdges(vm, doc, registry, boundaryIds);
            AddArcEdges(vm, doc, registry);
        }

        if (options.ImportLines)
            AddCircles(vm, doc);

        var surfaceCount = 0;
        if (options.ImportSurfaces)
            surfaceCount = AddSurfaces(vm, doc, registry, boundaryIds);
        else if (boundaryIds is { Count: >= 3 })
        {
            vm.LockImportedBoundaryAsSurfaceS1Public();
            surfaceCount = 1;
        }

        vm.PruneDuplicateBoundaryInternalEdges();
        vm.FitAxisBoundsToVertexExtents();
        vm.RebuildGeometryAfterProjectLoad();
        vm.RefreshMaterialLayerCanvasOverlays();
        vm.ProjectSettings?.PullAxisFromGeometry();

        return new GeometryImportResult
        {
            Document = doc,
            PointCount = vm.GeometryVertexRows.Count,
            LineCount = vm.GeometryInternalEdges.Count,
            SurfaceCount = vm.GeometrySurfaces.Count
        };
    }

    public static GeometryImportDocument BuildExportDocument(GeometryDefinitionViewModel vm)
    {
        var doc = new GeometryImportDocument();
        foreach (var v in vm.GeometryVertexRows)
        {
            doc.Points.Add(new GeometryImportPoint
            {
                Id = v.StablePointId,
                X = v.AxisX,
                Y = v.AxisY
            });
        }

        foreach (var ie in vm.GeometryInternalEdges)
        {
            if (ie.IsArc)
            {
                doc.Lines.Add(new GeometryImportLine
                {
                    FromId = ie.StableStart,
                    ToId = ie.StableEnd,
                    Kind = "arc"
                });
                continue;
            }

            doc.Lines.Add(new GeometryImportLine
            {
                FromId = ie.StableMin,
                ToId = ie.StableMax,
                Kind = "internal"
            });
        }

        var surfaceNumber = 1;
        foreach (var surface in vm.GeometrySurfaces.Where(s => s.IsPolygonLocked && s.LockedPolygonVertexLabels.Count >= 3))
        {
            var ids = new List<int>();
            foreach (var label in surface.LockedPolygonVertexLabels)
            {
                var v = vm.GeometryVertexRows.FirstOrDefault(r => r.PointLabel == label);
                if (v != null)
                    ids.Add(v.StablePointId);
            }

            if (ids.Count >= 3)
            {
                doc.Surfaces.Add(new GeometryImportSurface
                {
                    Number = surfaceNumber++,
                    Name = string.IsNullOrWhiteSpace(surface.SurfaceName) ? $"Surface {surface.SurfaceNumber}" : surface.SurfaceName,
                    PointIds = ids
                });
            }
        }

        return doc;
    }

    private static void ClearGeometry(GeometryDefinitionViewModel vm)
    {
        vm.GeometryVertexRows.Clear();
        vm.GeometryLineSegments.Clear();
        vm.GeometryInternalEdges.Clear();
        vm.GeometryInternalCircles.Clear();
        vm.GeometrySurfaces.Clear();
        vm.ResetStablePointIdCounter();
    }

    private static GeometryPointRegistry BuildRegistry(GeometryImportDocument doc, double mergeTolerance)
    {
        var registry = new GeometryPointRegistry(mergeTolerance);
        var maxId = 0;

        foreach (var p in doc.Points)
        {
            var id = registry.GetOrAdd(p.X, p.Y, p.Id);
            maxId = Math.Max(maxId, id);
        }

        foreach (var (x, y) in doc.LegacyBoundaryPoints)
        {
            var id = registry.GetOrAdd(x, y);
            maxId = Math.Max(maxId, id);
        }

        foreach (var poly in doc.ClosedPolygons)
        {
            foreach (var (x, y) in poly.Vertices)
            {
                var id = registry.GetOrAdd(x, y);
                maxId = Math.Max(maxId, id);
            }
        }

        foreach (var line in doc.Lines)
        {
            maxId = Math.Max(maxId, Math.Max(line.FromId, line.ToId));
        }

        foreach (var s in doc.Surfaces)
        {
            foreach (var pid in s.PointIds)
                maxId = Math.Max(maxId, pid);
        }

        registry.SeedNextId(maxId + 1);
        return registry;
    }

    private static List<int>? ResolveBoundaryPointIds(GeometryImportDocument doc, GeometryPointRegistry registry)
    {
        if (doc.LegacyBoundaryPoints.Count >= 3)
        {
            return doc.LegacyBoundaryPoints
                .Select(p => registry.GetOrAdd(p.X, p.Y))
                .ToList();
        }

        if (doc.Surfaces.Count > 0)
        {
            var primary = doc.Surfaces.OrderBy(s => s.Number).First();
            if (primary.PointIds.Count >= 3)
                return primary.PointIds.ToList();
        }

        if (doc.ClosedPolygons.Count > 0)
        {
            return doc.ClosedPolygons[0].Vertices
                .Select(v => registry.GetOrAdd(v.X, v.Y))
                .ToList();
        }

        if (doc.Points.Count >= 3 && doc.Lines.Count == 0 && doc.Surfaces.Count == 0)
        {
            return doc.Points
                .Select(p => registry.GetOrAdd(p.X, p.Y, p.Id))
                .ToList();
        }

        return null;
    }

    private static void PopulateBoundaryVertices(GeometryDefinitionViewModel vm, GeometryPointRegistry registry, List<int> boundaryIds)
    {
        vm.GeometryVertexRows.Clear();
        var index = 0;
        foreach (var id in boundaryIds)
        {
            if (!registry.TryGetCoordinates(id, out var x, out var y))
                continue;

            var display = new GeometryDefinitionViewModel.GeometryVertexDisplay
            {
                Index = index++,
                StablePointId = id,
                AxisX = x,
                AxisY = y
            };
            display.Owner = vm;
            vm.GeometryVertexRows.Add(display);
        }

        vm.NormalizeStablePointIdsAfterLoad();
    }

    private static void AddStandalonePoints(GeometryDefinitionViewModel vm, GeometryPointRegistry registry, List<int>? boundaryIds)
    {
        var boundarySet = boundaryIds != null ? boundaryIds.ToHashSet() : new HashSet<int>();
        foreach (var (id, x, y) in registry.AllPoints)
        {
            if (boundarySet.Contains(id))
                continue;
            if (vm.GeometryVertexRows.Any(v => v.StablePointId == id))
                continue;

            var display = new GeometryDefinitionViewModel.GeometryVertexDisplay
            {
                Index = vm.GeometryVertexRows.Count,
                StablePointId = id,
                AxisX = x,
                AxisY = y
            };
            display.Owner = vm;
            vm.GeometryVertexRows.Add(display);
        }
    }

    private static void AddLineEdges(
        GeometryDefinitionViewModel vm,
        GeometryImportDocument doc,
        GeometryPointRegistry registry,
        List<int>? boundaryIds)
    {
        foreach (var line in doc.Lines)
        {
            if (!registry.TryGetCoordinates(line.FromId, out _, out _)
                || !registry.TryGetCoordinates(line.ToId, out _, out _))
                continue;

            if (IsConsecutiveBoundaryPair(boundaryIds, line.FromId, line.ToId))
                continue;

            if (vm.GeometryInternalEdges.Any(e =>
                    !e.IsArc && e.StableMin == Math.Min(line.FromId, line.ToId) && e.StableMax == Math.Max(line.FromId, line.ToId)))
                continue;

            vm.GeometryInternalEdges.Add(new GeometryInternalEdge(line.FromId, line.ToId));
        }
    }

    private static void AddOpenPolylineEdges(
        GeometryDefinitionViewModel vm,
        GeometryImportDocument doc,
        GeometryPointRegistry registry,
        List<int>? boundaryIds)
    {
        foreach (var chain in doc.OpenPolylines)
        {
            if (chain.Count < 2)
                continue;

            var ids = chain.Select(p => registry.GetOrAdd(p.X, p.Y)).ToList();
            for (var i = 0; i < ids.Count - 1; i++)
            {
                var from = ids[i];
                var to = ids[i + 1];
                if (IsConsecutiveBoundaryPair(boundaryIds, from, to))
                    continue;
                if (vm.GeometryInternalEdges.Any(e =>
                        !e.IsArc && e.StableMin == Math.Min(from, to) && e.StableMax == Math.Max(from, to)))
                    continue;
                vm.GeometryInternalEdges.Add(new GeometryInternalEdge(from, to));
            }
        }
    }

    private static void AddArcEdges(GeometryDefinitionViewModel vm, GeometryImportDocument doc, GeometryPointRegistry registry)
    {
        foreach (var arc in doc.Arcs)
        {
            var startId = registry.GetOrAdd(arc.StartX, arc.StartY);
            var midId = registry.GetOrAdd(arc.MidX, arc.MidY);
            var endId = registry.GetOrAdd(arc.EndX, arc.EndY);
            EnsureVertexExists(vm, registry, startId);
            EnsureVertexExists(vm, registry, midId);
            EnsureVertexExists(vm, registry, endId);
            vm.GeometryInternalEdges.Add(new GeometryInternalEdge(startId, midId, endId));
        }
    }

    private static void AddCircles(GeometryDefinitionViewModel vm, GeometryImportDocument doc)
    {
        var nextCircleId = vm.GeometryInternalCircles.Count > 0
            ? vm.GeometryInternalCircles.Max(c => c.Id) + 1
            : 1;

        foreach (var circle in doc.Circles)
        {
            vm.GeometryInternalCircles.Add(new GeometryInternalCircle
            {
                Id = nextCircleId++,
                CenterAxisX = circle.CenterX,
                CenterAxisY = circle.CenterY,
                RadiusAxis = circle.Radius
            });
        }
    }

    private static int AddSurfaces(
        GeometryDefinitionViewModel vm,
        GeometryImportDocument doc,
        GeometryPointRegistry registry,
        List<int>? boundaryIds)
    {
        vm.GeometrySurfaces.Clear();
        var count = 0;

        IEnumerable<GeometryImportSurface> surfaces = doc.Surfaces.Count > 0
            ? doc.Surfaces.OrderBy(s => s.Number)
            : doc.ClosedPolygons.Select((p, i) => new GeometryImportSurface
            {
                Number = i + 1,
                Name = p.Name ?? $"Surface {i + 1}",
                PointIds = p.Vertices.Select(v => registry.GetOrAdd(v.X, v.Y)).ToList()
            });

        foreach (var surface in surfaces)
        {
            if (surface.PointIds.Count < 3)
                continue;

            foreach (var pid in surface.PointIds)
                EnsureVertexExists(vm, registry, pid);

            var geoSurface = new GeometrySurface(surface.Number, surface.Name);
            geoSurface.IsPolygonLocked = false;
            geoSurface.LockedPolygonVertexLabels.Clear();
            foreach (var pid in surface.PointIds)
            {
                var v = vm.GeometryVertexRows.FirstOrDefault(r => r.StablePointId == pid);
                if (v != null)
                    geoSurface.LockedPolygonVertexLabels.Add(v.PointLabel);
            }

            if (geoSurface.LockedPolygonVertexLabels.Count < 3)
                continue;

            geoSurface.IsPolygonLocked = true;
            vm.GeometrySurfaces.Add(geoSurface);
            count++;
        }

        if (count == 0 && boundaryIds is { Count: >= 3 })
        {
            vm.LockImportedBoundaryAsSurfaceS1Public();
            count = 1;
        }

        if (count > 0)
            vm.SelectedGeometrySurface = vm.GeometrySurfaces[0];

        vm.RefreshSurfaceRegionOverlaysPublic();
        return count;
    }

    private static void EnsureVertexExists(GeometryDefinitionViewModel vm, GeometryPointRegistry registry, int id)
    {
        if (vm.GeometryVertexRows.Any(v => v.StablePointId == id))
            return;

        if (!registry.TryGetCoordinates(id, out var x, out var y))
            return;

        var display = new GeometryDefinitionViewModel.GeometryVertexDisplay
        {
            Index = vm.GeometryVertexRows.Count,
            StablePointId = id,
            AxisX = x,
            AxisY = y
        };
        display.Owner = vm;
        vm.GeometryVertexRows.Add(display);
    }

    private static bool IsConsecutiveBoundaryPair(List<int>? boundaryIds, int fromId, int toId)
    {
        if (boundaryIds is not { Count: >= 2 })
            return false;

        for (var i = 0; i < boundaryIds.Count; i++)
        {
            var a = boundaryIds[i];
            var b = boundaryIds[(i + 1) % boundaryIds.Count];
            if ((a == fromId && b == toId) || (a == toId && b == fromId))
                return true;
        }

        return false;
    }
}
