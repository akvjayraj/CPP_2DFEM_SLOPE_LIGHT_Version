using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Geometry;

namespace GeotechSlopeApp.Services.GeometryImport;

/// <summary>EPSG-aware GeoJSON import/export for soil boundaries and geological features.</summary>
public static class GeoJsonGeometryInterchange
{
    public static PlanarGeometryKernel Import(string filePath, ProjectCoordinateSystem? crs = null)
    {
        using var stream = File.OpenRead(filePath);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        var kernel = new PlanarGeometryKernel();
        var nextVid = 1;
        var nextFace = 1;

        void AddRing(IEnumerable<(double X, double Y)> ring, string name, GeometryEdgeRole role)
        {
            var list = ring.ToList();
            if (list.Count < 2) return;
            if (role == GeometryEdgeRole.WeakPlane
                || role == GeometryEdgeRole.Bedding
                || role == GeometryEdgeRole.TensionCrack)
            {
                var kind = role switch
                {
                    GeometryEdgeRole.Bedding => GeologicalFeatureKind.Bedding,
                    GeometryEdgeRole.TensionCrack => GeologicalFeatureKind.TensionCrack,
                    _ => GeologicalFeatureKind.WeakPlane
                };
                var feature = new GeologicalFeature
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Kind = kind,
                    Name = name
                };
                feature.Vertices.AddRange(list);
                kernel.GeologicalFeatures.Add(feature);
                return;
            }

            var face = new PlanarMapFace { Id = nextFace++, Name = name };
            foreach (var (x, y) in list)
            {
                var id = nextVid++;
                kernel.Vertices.Add(new PlanarMapVertex { StableId = id, X = x, Y = y });
                face.VertexStableIds.Add(id);
            }

            kernel.Faces.Add(face);
        }

        if (root.TryGetProperty("features", out var features))
        {
            foreach (var f in features.EnumerateArray())
            {
                var props = f.TryGetProperty("properties", out var p) ? p : default;
                var name = props.ValueKind == JsonValueKind.Object && props.TryGetProperty("name", out var n)
                    ? n.GetString() ?? "Feature"
                    : "Feature";
                var roleName = props.ValueKind == JsonValueKind.Object && props.TryGetProperty("role", out var r)
                    ? r.GetString()
                    : null;
                var role = ParseRole(roleName);
                if (!f.TryGetProperty("geometry", out var geom)) continue;
                var type = geom.GetProperty("type").GetString();
                if (type is "Polygon" or "LineString")
                {
                    var coords = geom.GetProperty("coordinates");
                    var ring = type == "Polygon"
                        ? ReadRing(coords[0], crs)
                        : ReadRing(coords, crs);
                    AddRing(ring, name, role);
                }
            }
        }

        if (kernel.Faces.Count == 0 && kernel.GeologicalFeatures.Count == 0)
            throw new InvalidOperationException("GeoJSON contained no Polygon/LineString features.");

        return kernel;
    }

    public static void Export(PlanarGeometryKernel kernel, string filePath, ProjectCoordinateSystem? crs = null)
    {
        var features = new List<object>();
        foreach (var face in kernel.SoilFaces.Where(f => f.VertexStableIds.Count >= 3))
        {
            var ring = face.VertexStableIds
                .Select(id => kernel.FindVertex(id))
                .Where(v => v != null)
                .Select(v => ToLonLat(v!.X, v.Y, crs))
                .ToList();
            if (ring.Count >= 3 && (ring[0][0] != ring[^1][0] || ring[0][1] != ring[^1][1]))
                ring.Add(ring[0]);
            features.Add(new
            {
                type = "Feature",
                properties = new { name = face.Name, role = "boundary", materialId = face.MaterialId },
                geometry = new { type = "Polygon", coordinates = new[] { ring } }
            });
        }

        foreach (var g in kernel.GeologicalFeatures.Where(f => f.Vertices.Count >= 2))
        {
            var line = g.Vertices.Select(v => ToLonLat(v.X, v.Y, crs)).ToList();
            features.Add(new
            {
                type = "Feature",
                properties = new { name = g.Name, role = g.Kind.ToString() },
                geometry = new { type = "LineString", coordinates = line }
            });
        }

        var fc = new
        {
            type = "FeatureCollection",
            crs = crs?.EpsgCode is int epsg
                ? new { type = "name", properties = new { name = $"EPSG:{epsg}" } }
                : null,
            features
        };
        var json = JsonSerializer.Serialize(fc, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }

    private static List<(double X, double Y)> ReadRing(JsonElement coords, ProjectCoordinateSystem? crs)
    {
        var list = new List<(double X, double Y)>();
        foreach (var c in coords.EnumerateArray())
        {
            var lon = c[0].GetDouble();
            var lat = c[1].GetDouble();
            if (crs?.EpsgCode != null)
            {
                var (x, y) = crs.FromMap(lon, lat);
                list.Add((x, y));
            }
            else
            {
                list.Add((lon, lat));
            }
        }

        return list;
    }

    private static double[] ToLonLat(double x, double y, ProjectCoordinateSystem? crs)
    {
        if (crs?.EpsgCode != null)
        {
            var (e, n) = crs.ToMap(x, y);
            return new[] { e, n };
        }

        return new[] { x, y };
    }

    private static GeometryEdgeRole ParseRole(string? role) => role?.ToLowerInvariant() switch
    {
        "weakplane" or "weak_plane" => GeometryEdgeRole.WeakPlane,
        "bedding" => GeometryEdgeRole.Bedding,
        "tensioncrack" or "tension_crack" => GeometryEdgeRole.TensionCrack,
        "watertable" or "water_table" => GeometryEdgeRole.WaterTable,
        _ => GeometryEdgeRole.Boundary
    };
}
